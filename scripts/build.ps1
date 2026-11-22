# Nova Taskbar - build + testes (Debug x64). Uso: scripts\build.ps1 [-Run] [-Release] [-Integration] [-NoTests]
param([switch]$Run, [switch]$Release, [switch]$NoTests, [switch]$Integration)
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$dn = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $dn 'dotnet.exe')) { $env:DOTNET_ROOT = $dn; $env:PATH = "$dn;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$cfg = if ($Release) { 'Release' } else { 'Debug' }
"== dotnet $(dotnet --version) | $cfg | $(Get-Date -Format s)"

if (-not (Test-Path 'NovaTaskbar.sln')) { dotnet new sln -n NovaTaskbar --format sln | Out-Null }
$inSln = (dotnet sln NovaTaskbar.sln list) -join "`n"
foreach ($proj in Get-ChildItem src, tests -Recurse -Filter *.csproj) {
  $rel = Resolve-Path -Relative $proj.FullName
  if ($inSln -notmatch [regex]::Escape($proj.Name)) { dotnet sln NovaTaskbar.sln add $rel | Out-Null; "sln + $rel" }
}

# o exe em execução trava as DLLs de saída
Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

"== BUILD App"
$sw = [Diagnostics.Stopwatch]::StartNew()
dotnet build src\NovaTaskbar.App\NovaTaskbar.App.csproj -c $cfg -p:Platform=x64 -nologo -v:minimal -clp:"Summary;ForceNoAlign"
$buildExit = $LASTEXITCODE
"BUILD_EXIT=$buildExit ($([int]$sw.Elapsed.TotalSeconds)s)"

if (-not $NoTests) {
  "== TESTES"
  dotnet test tests\NovaTaskbar.UnitTests\NovaTaskbar.UnitTests.csproj -c $cfg -nologo -v:minimal --logger "console;verbosity=normal" --logger "trx;LogFileName=unit-tests.trx" --results-directory docs\test-results
  "TEST_EXIT=$LASTEXITCODE"
  if ($Integration) {
    "== TESTES DE INTEGRACAO (Windows real)"
    dotnet test tests\NovaTaskbar.IntegrationTests\NovaTaskbar.IntegrationTests.csproj -c $cfg -nologo -v:minimal --logger "console;verbosity=detailed" --logger "trx;LogFileName=integration-tests.trx" --results-directory docs\test-results
    "INTEGRATION_EXIT=$LASTEXITCODE"
  }
}

if ($Run -and $buildExit -eq 0) {
  $exe = Get-ChildItem "src\NovaTaskbar.App\bin\x64\$cfg" -Recurse -Filter NovaTaskbar.exe | Select-Object -First 1
  if ($exe) {
    Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName
    "RUN=$($exe.FullName)"
  } else { "RUN=exe nao encontrado" }
}
