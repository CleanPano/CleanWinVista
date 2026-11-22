# Arquitetura — Nova Taskbar

## Visão geral

```
NovaTaskbar/
├─ NovaTaskbar.sln            (gerado pelo scripts/build.ps1 na 1ª execução)
├─ RUN.cmd                    executa scripts/next.ps1 (tarefa atual) e grava docs/last-run.log
├─ src/
│  ├─ NovaTaskbar.Core/       net10.0, sem dependência de Windows/UI → 100% testável
│  │  ├─ Magnification/       motor de magnificação (função pura) + animador temporal
│  │  ├─ Layout/              geometria do Dock (janela, pílula, posição, folga para ampliação)
│  │  └─ Configuration/       modelos, validação, persistência JSON versionada, migração
│  ├─ NovaTaskbar.Windows/    net10.0-windows, sem WinUI: interop Win32/DWM/Shell isolado e testável
│  │  ├─ Interop/             P/Invoke, ícones (IShellItemImageFactory), atalhos .lnk (IShellLink)
│  │  └─ Apps/                enumeração/rastreamento de janelas (WinEvent), ações, lançador, nomes/identidade
│  └─ NovaTaskbar.App/        WinUI 3 / Windows App SDK 2.7, não empacotado, self-contained
│     ├─ Services/            configuração, cache de ícones (memória + disco), log em arquivo
│     ├─ Dock/                janela dos ícones, janela de vidro, backdrops
│     └─ Settings/            janela de configurações (MVVM, CommunityToolkit.Mvvm)
├─ tests/NovaTaskbar.UnitTests/         xUnit sobre o Core (99 testes)
├─ tests/NovaTaskbar.IntegrationTests/  xUnit contra o Windows real: janelas Win32, Shell, Bloco de Notas ponta a ponta
├─ scripts/                   check-env, install-sdk, build
└─ docs/
```

Na Fase 2 o interop foi extraído para **`NovaTaskbar.Windows`** (o segundo consumidor apareceu: os testes de
integração). `.Infrastructure`, `.UI` e `.Widgets` continuam como pastas de `NovaTaskbar.App` até terem consumidores próprios.

## Fluxo de inicialização

1. Mutex `Local\NovaTaskbar.SingleInstance`. Segunda instância sinaliza `Local\NovaTaskbar.ShowSettings` e sai.
2. `SettingsService.Load()` → `SettingsStore` (lê, migra, valida; se corrompido: guarda `.corrupt-*`, tenta `.bak`, senão padrões).
3. Primeira execução: `DefaultApps.Discover()` fixa apenas apps **existentes no disco**.
4. `DockWindow.Start()` → posiciona no monitor principal (área de trabalho, DPI efetivo) → ícones → material.
5. Thread em espera pelo evento de "abrir configurações".

## Como o Dock é desenhado

Duas janelas de topo, ambas `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, fora do Alt+Tab, sempre no topo:

| Janela | Conteúdo | Entrada |
|---|---|---|
| **DockWindow** | Ícones, rótulo, pílula "Tint". Fundo 100% transparente (`TransparentBackdrop`: pincel de composição transparente + `DwmExtendFrameIntoClientArea(-1)`). Tamanho fixo = largura em repouso + expansão máxima + folga para o ícone ampliado e o rótulo. | Em repouso, `SetWindowRgn` limita a área clicável à pílula; durante a magnificação, a janela inteira. Fora disso os cliques passam para o que está atrás. |
| **GlassWindow** | Só o material (`DesktopAcrylicController`/`MicaController`) com cantos e borda do DWM. Redimensionada a cada quadro para acompanhar a largura da pílula. | Nunca (coberta pela DockWindow). |

Motivo das duas janelas: o Acrylic/Mica do Windows preenche a janela inteira. Para os ícones ampliados
"saírem" por cima do vidro (como no macOS) sem vidro na área de folga, o vidro precisa ser uma janela
do tamanho exato da pílula.

### Renderização

- `MagnificationEngine.Compute` (Core) devolve escala e centro de cada ícone — sem alocação.
- Cada ícone é um `Grid` do tamanho **máximo** (ícone ampliado) com `Image` do tamanho físico exato
  (extraído em alta resolução). Em repouso ele é reduzido com `UIElement.Scale`; nada é reamostrado para cima.
- Posição/escala via `UIElement.Translation`/`Scale` (propriedades de composição): **sem relayout por quadro**.
- `CompositionTarget.Rendering` só é assinado enquanto há animação; em repouso não há trabalho periódico.

## Gerenciamento de apps (Fase 2)

```
SetWinEventHook (create/destroy/show/hide/nome/cloak/foco/minimizar) ──► WindowTracker (debounce 60 ms, UI thread)
        └─► WindowEnumerator.Snapshot(): EnumWindows (ordem Z) → WindowFilter → exe/AUMID (cache por PID/HWND)
                └─► DockItemsBuilder (Core, puro): fixados + janelas → entradas; apps abertos não fixados em ordem estável
                        └─► DockWindow.SyncItems: anima a diferença (presença com mola; separador; indicadores)
Clique ─► ClickPolicy (Core, puro) ─► AppLauncher / WindowActions (ativar, minimizar, fechar)
```

- **Identidade**: AUMID quando existe (apps empacotados, Chrome/Edge), senão o executável; atalhos `.lnk` são
  resolvidos para destino + AUMID; apps empacotados são fixados como `shell:AppsFolder\<AUMID>`.
- **Sem polling**: só eventos do sistema; mudança só de foco atualiza o indicador sem reenumerar.
- **Movimento**: `Spring` (Core) no modelo resposta/amortecimento da Apple; presença dos itens anima o espaço;
  reordenar usa FLIP (deslocamento residual com mola); rótulo ancorado no ícone; respeita "Efeitos de animação".

## Widgets (modo Estendido)

```
WidgetHost (timer 1 s só com widgets visíveis) ─► SystemMetrics / AudioVolume / MediaWatcher ─► WidgetData (+ History)
        └─► WidgetView.Update (Views/*) ─► Visuals/* (Ring, Gauge, Chart, RollingText) ─► NeedsFrame → DockWindow anima
```

- `Widgets/Views` — um arquivo por widget; `Widgets/Visuals` — peças de desenho reutilizáveis; `Palette` centraliza
  cores (opacas, ver KNOWN-ISSUES #16); `WidgetLabels` nomes/ícones da UI.
- Estilo por widget em `WidgetConfig.Style` (validado em `WidgetSettings.Sanitize`); a chave do item no Dock inclui
  o estilo, então trocar o estilo recria só aquele widget.

## Decisões (ADR resumidos)

- **ADR-001 .NET 10 LTS + WinUI 3 + Windows App SDK 2.7.0** (estável mais recente; 2.x é a continuação do 1.x com SemVer).
- **ADR-002 Core sem dependências de plataforma**: magnificação, geometria e configuração testáveis em qualquer SO.
- **ADR-003 Não empacotado (unpackaged) + self-contained**: o Dock usa regiões de janela, estilos estendidos e
  DWM diretamente; MSIX x instalador tradicional será decidido na Fase 5 (ver ROADMAP).
- **ADR-004 Duas janelas (ícones transparente + vidro)** — ver acima.
- **ADR-005 Configuração**: JSON camelCase versionado em `%LOCALAPPDATA%\NovaTaskbar\settings.json`,
  escrita atômica (`.tmp` + `File.Replace` com `.bak`), validação que nunca confia no conteúdo.
- **ADR-006 Segurança de apps fixados**: só caminhos locais absolutos `.exe`/`.lnk`/`.appref-ms`/pastas; sem argumentos,
  sem UNC, sem URI. Lançamento via `ShellExecuteEx` verbo `open`.
- **ADR-008 `NovaTaskbar.Windows` separado** do App para testar Win32 sem WinUI. Consequência: dentro de `NovaTaskbar.*`,
  `Windows.X` resolve para `NovaTaskbar.Windows.X` → o App usa `global::Windows.…` em nomes qualificados.
- **ADR-009 Eventos, não polling** (`SetWinEventHook` fora do processo) com debounce; CPU ociosa medida 0,000%.
- **ADR-007 Log** em `%LOCALAPPDATA%\NovaTaskbar\logs` (provedor próprio, retenção de 7 dias). Sem telemetria, sem rede.

## Dependências

| Pacote | Versão | Uso |
|---|---|---|
| Microsoft.WindowsAppSDK | 2.7.0 | WinUI 3, AppWindow, SystemBackdrops |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2957 | ferramentas de build do Windows SDK |
| CommunityToolkit.Mvvm | 8.4.2 | ObservableObject / RelayCommand |
| Microsoft.Extensions.DependencyInjection | 10.0.15 | DI |
| Microsoft.Extensions.Logging | 10.0.15 | log |
| Microsoft.Extensions.Logging.Abstractions | 10.0.15 | log em `NovaTaskbar.Windows` |
| xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 2.9.3 / 3.1.5 / 18.12.0 | testes |
