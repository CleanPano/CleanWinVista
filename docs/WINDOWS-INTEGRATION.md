# Integração com o Windows

Nova Taskbar é um app **independente** do Explorer: não substitui o shell, não altera arquivos do sistema,
não mexe na barra de tarefas nativa e roda **sem privilégios de administrador** (`asInvoker`).

## APIs usadas (Fase 0–1)

| Área | API | Onde | Observações |
|---|---|---|---|
| Janela sem moldura | `OverlappedPresenter.SetBorderAndTitleBar(false,false)` + `GWL_STYLE` → `WS_POPUP` | `Interop/NativeMethods.MakePopup` | o presenter sozinho deixa `WS_DLGFRAME`; forçamos popup puro |
| Fora do Alt+Tab / barra | `AppWindow.IsShownInSwitchers=false` + `WS_EX_TOOLWINDOW` | Dock, Glass | |
| Não roubar foco | `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE → MA_NOACTIVATE` + `ShowWindow(SW_SHOWNOACTIVATE)` | DockWindow | **`AppWindow.Show(false)` mostra a janela MINIMIZADA** (verificado) — não usar |
| Sempre no topo | `OverlappedPresenter.IsAlwaysOnTop` + `SetWindowPos(HWND_TOPMOST)` | | vidro inserido logo abaixo dos ícones |
| Transparência de janela | `ICompositionSupportsSystemBackdrop.SystemBackdrop` = pincel transparente (`Windows.UI.Composition`) + `DwmExtendFrameIntoClientArea(-1)` | `TransparentBackdrop` | é transparência, **não** desfoque |
| Desfoque real | `DesktopAcrylicController` (Kind Base/Thin) | `GlassBackdrop` | desfoca o que está atrás, feito pelo DWM |
| Mica | `MicaController` (Kind BaseAlt) | `GlassBackdrop` | tinge com o papel de parede; **não** desfoca janelas atrás |
| Cantos / borda | `DWMWA_WINDOW_CORNER_PREFERENCE=ROUND`, `DWMWA_BORDER_COLOR` | `GlassWindow` | Windows 11 22000+ |
| Área clicável | `SetWindowRgn` | DockWindow | em repouso só a pílula; cliques fora passam |
| Monitor / DPI | `MonitorFromPoint`, `GetMonitorInfo` (rcWork), `GetDpiForMonitor` | DockWindow | manifest PerMonitorV2 |
| Mudanças de tela | subclasse `SetWindowSubclass`: `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE(SPI_SETWORKAREA)` | DockWindow | reposiciona sem polling |
| Ícones | `SHCreateItemFromParsingName` + `IShellItemImageFactory.GetImage` (`SIIGBF_ICONONLY`) | `ShellIcons` | thread STA dedicada; mesma fonte do Explorer |
| Abrir apps | `ShellExecuteEx` verbo `open`, `AllowSetForegroundWindow` | `AppLauncher` | sem argumentos vindos da configuração |
| Seletor de arquivo | `Windows.Storage.Pickers.FileOpenPicker` + `InitializeWithWindow` | DockWindow | |

## APIs da Fase 2

| Área | API | Onde |
|---|---|---|
| Eventos de janela | `SetWinEventHook` (OUTOFCONTEXT, SKIPOWNPROCESS): foco, minimizar, create/destroy/show/hide, nome, cloaked | `WindowTracker` |
| Enumeração | `EnumWindows`, `GetWindowText`, `GetClassName`, `GetWindowThreadProcessId`, `GetWindow(GW_OWNER)`, `IsWindowVisible`, `IsIconic`, `DwmGetWindowAttribute(DWMWA_CLOAKED)` | `WindowEnumerator` |
| Processo | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, `QueryFullProcessImageName`, `GetApplicationUserModelId` | `WindowEnumerator` |
| AUMID da janela | `SHGetPropertyStoreForWindow` + `PKEY_AppUserModel_ID` | `WindowEnumerator` |
| Apps UWP | `EnumChildWindows` → `Windows.UI.Core.CoreWindow` do processo real | `WindowEnumerator` |
| Atalhos | `IShellLinkW.GetPath` + `IPropertyStore` (AUMID do atalho) | `ShellLink` |
| Nome de app empacotado | `SHCreateItemFromParsingName("shell:AppsFolder\\AUMID")` + `IShellItem.GetDisplayName` | `AppInfoProvider` |
| Ativar / minimizar / fechar | `ShowWindow(SW_RESTORE/SW_MINIMIZE)`, `SetForegroundWindow`, `PostMessage(WM_CLOSE)` | `WindowActions` |
| Soltar arquivos | WinUI `AllowDrop` + `StandardDataFormats.StorageItems` | `DockWindow` |
| Moldura | `WM_NCCALCSIZE` → 0 (área cliente = janela) | `DockWindow`, `GlassWindow` |

Fechar usa `WM_CLOSE`: o app decide (pode pedir para salvar). Nunca encerramos processos.

## Limitações conhecidas (honestas)

1. **Raio dos cantos nos materiais Acrylic/Mica é o do Windows** (~8 px lógicos). O DWM não expõe raio livre
   para janelas com backdrop do sistema; recortar com região (`SetWindowRgn`) deixaria a borda serrilhada.
   O material **Translúcido** (desenhado pelo app) aceita qualquer raio (0–40), mas não desfoca.
2. **Intensidade do desfoque não é ajustável**: o Windows não expõe raio de blur do Acrylic.
   Ajustáveis: cor, opacidade do tingimento, opacidade de luminosidade, variante Base/Thin.
3. Se "Efeitos de transparência" estiver desligado no Windows ou em modo economia de bateria, o Acrylic mostra a
   cor de fallback sólida (comportamento do sistema).
4. Apps empacotados são suportados desde a Fase 2 por `shell:AppsFolder\<AUMID>` (fixe-os pelo menu "Fixar no Dock" com o app aberto).
   Os `.exe` de stub em `System32` (calc/notepad) continuam com ícone legado se fixados diretamente.
5. Apenas o **monitor principal** e a borda **inferior** nesta fase (multi-monitor = Fase 3; topo/esquerda/direita = Fase 4).
6. Sem auto-ocultar e sem detecção de tela cheia (Fase 3).
7. Barra de tarefas nativa permanece intacta. Ocultá-la (opcional, documentado) é Fase 3.
