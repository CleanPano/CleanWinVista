# Mede CPU ociosa (sem interação) e memória do processo do Nova por N segundos.
param([int]$Seconds = 20)
$p = Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "MEASURE: nao esta rodando"; return }
$c0 = $p.TotalProcessorTime; $t0 = Get-Date
Start-Sleep -Seconds $Seconds
$p.Refresh()
$cpu = ($p.TotalProcessorTime - $c0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds / [Environment]::ProcessorCount * 100
"MEASURE: idle {0}s CPU={1:N3}% (de {6} núcleos; {7:N1} ms de CPU) WS={2}MB Private={3}MB Handles={4} Threads={5}" -f $Seconds, $cpu, [int]($p.WorkingSet64/1MB), [int]($p.PrivateMemorySize64/1MB), $p.HandleCount, $p.Threads.Count, [Environment]::ProcessorCount, ($p.TotalProcessorTime - $c0).TotalMilliseconds
