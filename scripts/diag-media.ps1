# Diagnostico: uso de CPU/GPU do Dock e do Chrome por 15 s (nao reinicia nada) + fim do log do app.
$p = Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "DIAG: dock nao esta rodando"; return }
"DOCK pid=$($p.Id) path=$($p.Path) start=$($p.StartTime) WS=$([int]($p.WorkingSet64/1MB))MB threads=$($p.Threads.Count) handles=$($p.HandleCount)"
$chrome = @(Get-Process chrome -ErrorAction SilentlyContinue)
"CHROME procs=$($chrome.Count)"
$c0 = $p.TotalProcessorTime; $ch0 = ($chrome | Measure-Object -Property CPU -Sum).Sum; $t0 = Get-Date
$gpu = @{}
for ($i = 0; $i -lt 5; $i++) {
  try {
    $s = (Get-Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction Stop).CounterSamples | Where-Object { $_.CookedValue -gt 0.05 }
    foreach ($x in $s) { if ($x.InstanceName -match 'pid_(\d+)_') { $id = [int]$Matches[1]; $gpu[$id] = [double]$gpu[$id] + $x.CookedValue } }
  } catch { "GPU counter: $($_.Exception.Message)" }
  Start-Sleep -Seconds 2
}
$p.Refresh(); $secs = ((Get-Date) - $t0).TotalSeconds
"DOCK CPU = {0:N2}% de 1 nucleo em {1:N0}s" -f ((($p.TotalProcessorTime - $c0).TotalSeconds / $secs) * 100), $secs
$chrome | ForEach-Object { $_.Refresh() }
"CHROME CPU = {0:N1}% de 1 nucleo" -f (((($chrome | Measure-Object -Property CPU -Sum).Sum - $ch0) / $secs) * 100)
"GPU medio por processo (5 amostras):"
$gpu.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 8 | ForEach-Object { $n = (Get-Process -Id $_.Key -ErrorAction SilentlyContinue).ProcessName; "  {0,-22} pid {1,-6} {2:N1}%" -f $n, $_.Key, ($_.Value / 5) }
"== LOG"
Get-ChildItem (Join-Path $env:LOCALAPPDATA 'NovaTaskbar') -Recurse -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName; Get-Content $_.FullName -Tail 40 }
