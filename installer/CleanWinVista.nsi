; CleanWinVista - instalador de arquivo único.
; Só empacota a pasta publicada (dist\app) e abre o assistente do próprio app (NovaTaskbar.exe --setup),
; que tem o visual do Dock e os 20 idiomas. Instalação por usuário: sem admin, sem tocar em pastas do sistema.
; Gerar (Linux):   makensis -DVERSION=1.0.1 installer/CleanWinVista.nsi
; Gerar (Windows): makensis /DVERSION=1.0.1 installer\CleanWinVista.nsi
Unicode true
!ifndef VERSION
  !define VERSION "1.0.1"
!endif

Name "CleanWinVista"
OutFile "..\dist\CleanWinVista-Setup-${VERSION}.exe"
Icon "..\src\NovaTaskbar.App\Assets\CleanWinVista.ico"
RequestExecutionLevel user
SilentInstall silent
SetCompressor /SOLID lzma
ManifestDPIAware true

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "CleanWinVista"
VIAddVersionKey "FileDescription" "CleanWinVista Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "CleanWinVista"

Section
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\app"
  File /r "..\dist\app\*.*"
  ; Opcional: instalador oficial do CleanPano (o assistente confere o SHA-256 antes de oferecer a opção).
  ; Copie para installer\extras\ antes de gerar: ..\CleanPano\GitHub\release-1.16.1\CleanPano-Setup.exe
  SetOutPath "$PLUGINSDIR\app\extras"
  File "extras\CleanPano-Setup.exe"
  SetOutPath "$PLUGINSDIR\app"
  ; O assistente copia desta pasta temporária para o destino escolhido; a pasta some ao terminar.
  ExecWait '"$PLUGINSDIR\app\NovaTaskbar.exe" --setup'
  SetOutPath "$TEMP"
SectionEnd
