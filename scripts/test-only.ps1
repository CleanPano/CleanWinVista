# Roda testes unitários + integração sem parar o Dock em execução.
$root = Split-Path -Parent $PSScriptRoot; Set-Location $root
$dn = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'; $env:DOTNET_ROOT = $dn; $env:PATH = "$dn;$env:PATH"; $env:DOTNET_NOLOGO = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet test tests\NovaTaskbar.UnitTests\NovaTaskbar.UnitTests.csproj -nologo -v:minimal --logger "trx;LogFileName=unit-tests.trx" --results-directory docs\test-results
"TEST_EXIT=$LASTEXITCODE"
dotnet test tests\NovaTaskbar.IntegrationTests\NovaTaskbar.IntegrationTests.csproj -nologo -v:minimal --logger "console;verbosity=detailed" --logger "trx;LogFileName=integration-tests.trx" --results-directory docs\test-results
"INTEGRATION_EXIT=$LASTEXITCODE"
& "$PSScriptRoot\measure.ps1" -Seconds 30
& "$PSScriptRoot\diag.ps1" | Out-Null
