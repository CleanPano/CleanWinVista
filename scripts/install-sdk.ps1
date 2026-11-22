# Instala o .NET 10 SDK somente no usuario atual (sem admin) usando o script oficial da Microsoft.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$dir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$tmp = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $tmp -UseBasicParsing
# Só executa se a assinatura Authenticode for válida e da Microsoft (protege contra script adulterado)
$sig = Get-AuthenticodeSignature $tmp
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
  Remove-Item $tmp -Force -ErrorAction SilentlyContinue
  throw "dotnet-install.ps1 sem assinatura valida da Microsoft ($($sig.Status)). Instalacao cancelada."
}
& $tmp -Channel 10.0 -Quality GA -InstallDir $dir -NoPath
# PATH do usuario (persistente) com o SDK local na frente
$p = [Environment]::GetEnvironmentVariable('Path','User')
if (-not ($p -split ';' | ? { $_ -eq $dir })) { [Environment]::SetEnvironmentVariable('Path', "$dir;$p", 'User') }
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', $dir, 'User')
& (Join-Path $dir 'dotnet.exe') --list-sdks
