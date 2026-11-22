& "$PSScriptRoot\build.ps1"
# Pasta publicada (Release, self-contained) para o instalador de arquivo único
$root = Split-Path -Parent $PSScriptRoot
$dn = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $dn 'dotnet.exe')) { $env:DOTNET_ROOT = $dn; $env:PATH = "$dn;$env:PATH" }
"== PUBLISH"
if (Test-Path "$root\dist\app") { Remove-Item "$root\dist\app" -Recurse -Force }
dotnet publish "$root\src\NovaTaskbar.App\NovaTaskbar.App.csproj" -c Release -r win-x64 -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$root=C:\src" -o "$root\dist\app" -nologo -v:minimal
"PUBLISH_EXIT=$LASTEXITCODE"
"== PACOTES VULNERAVEIS"
dotnet list "$root\src\NovaTaskbar.App\NovaTaskbar.App.csproj" package --vulnerable --include-transitive 2>&1 | Select-String -Pattern "vulner|Critical|High|Moderate|Low|error" | ForEach-Object { $_.Line }
"VULN_EXIT=$LASTEXITCODE"
Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
$exe = "$PSScriptRoot\..\src\NovaTaskbar.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\NovaTaskbar.exe"
if (Test-Path $exe) { Start-Process $exe; "STARTED" }
