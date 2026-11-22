# Somente desenvolvimento: arquiva o settings.json atual (renomeia, não apaga) para o Dock recriar os padrões.
$f = Join-Path $env:LOCALAPPDATA 'NovaTaskbar\settings.json'
Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
foreach ($x in @($f, "$f.bak")) { if (Test-Path $x) { Rename-Item $x ("{0}.old-{1:yyyyMMddHHmmss}" -f (Split-Path $x -Leaf), (Get-Date)) ; "arquivado: $x" } }
