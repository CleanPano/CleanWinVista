# Nova Taskbar - inspecao do ambiente (somente leitura)
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\env-report.txt'
$r = New-Object System.Collections.Generic.List[string]
function S($t){ $r.Add(''); $r.Add("=== $t ===") }
S 'OS'
$os = Get-CimInstance Win32_OperatingSystem
$r.Add("$($os.Caption) | Version $($os.Version) | Build $($os.BuildNumber) | $($os.OSArchitecture)")
$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$r.Add("DisplayVersion: $($cv.DisplayVersion) UBR: $($cv.UBR)")
S 'PowerShell'; $r.Add($PSVersionTable.PSVersion.ToString())
S 'dotnet'
$dn = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dn) { $r.Add("path: $($dn.Source)"); $r.Add((& dotnet --list-sdks 2>&1 | Out-String)); $r.Add((& dotnet --list-runtimes 2>&1 | Out-String)); $r.Add((& dotnet workload list 2>&1 | Out-String)) } else { $r.Add('dotnet NAO encontrado no PATH') }
S 'Windows SDK (Windows Kits 10)'
$kits = 'C:\Program Files (x86)\Windows Kits\10\Include'
if (Test-Path $kits) { Get-ChildItem $kits -Directory | % { $r.Add($_.Name) } } else { $r.Add('Windows Kits 10 nao encontrado') }
S 'Visual Studio / Build Tools'
$vsw = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsw) { $r.Add((& $vsw -all -prerelease -format text -property displayName 2>&1 | Out-String)); $r.Add((& $vsw -all -prerelease -property installationPath 2>&1 | Out-String)) } else { $r.Add('vswhere nao encontrado (sem VS/Build Tools)') }
S 'Windows App Runtime instalado'
try { Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' | % { $r.Add("$($_.Name) $($_.Version) $($_.Architecture)") } } catch { $r.Add("erro: $_") }
S 'NuGet cache (Microsoft.WindowsAppSDK)'
$nu = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windowsappsdk'
if (Test-Path $nu) { Get-ChildItem $nu -Directory | % { $r.Add($_.Name) } } else { $r.Add('nenhum') }
S 'Ferramentas'
foreach ($t in 'git','winget','msbuild','makensis','wix') { $c = Get-Command $t -ErrorAction SilentlyContinue; $r.Add("$t : " + $(if ($c) { $c.Source } else { 'nao' })) }
S 'Rede nuget.org'
try { $x = Invoke-WebRequest 'https://api.nuget.org/v3/index.json' -UseBasicParsing -TimeoutSec 10; $r.Add("OK $($x.StatusCode)") } catch { $r.Add("FALHA: $($_.Exception.Message)") }
S 'Monitores / DPI'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Screen]::AllScreens | % { $r.Add("$($_.DeviceName) $($_.Bounds) primary=$($_.Primary)") }
$r | Set-Content -Path $out -Encoding UTF8
"Relatorio salvo em $out"
