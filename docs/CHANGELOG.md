# Changelog

## 1.0.2 — 22/11/2026 — Instalador corrigido

- **Instalador fechava ao clicar Install/Update** (falha nativa `0xc000027b` em `Microsoft.UI.Xaml.dll`): o `ProgressBar` do WinUI derrubava o processo ao montar a página de progresso. Trocado por uma barra própria no visual do Dock.
- **App instalado não abria** (`XamlParseException` no `DockWindow`): a publicação não copiava o `NovaTaskbar.pri` (XAML compilado). O `.csproj` agora o copia (`CopyAppPriToPublish`) e o `RUN.cmd` acusa `ERRO` se faltar. Afetava também 1.0.0 e 1.0.1.
- Assistente: cartão de conteúdo usa os pincéis do tema escuro (antes ficava branco com texto branco em Windows no tema claro).
- Instalador registra etapas e erros em `%TEMP%\CleanWinVista-setup.log`; qualquer falha volta à tela de opções com a mensagem.
- **Auto-ocultar**: com app em tela cheia, passar ou rolar o mouse na parte de baixo da tela não traz mais o Dock escondido (`DockVisibilityRules.PointerMayReveal`, teste novo).
- Testado de ponta a ponta no PC: `CleanWinVista-Setup-1.0.2.exe` → Update → Finish → Dock instalado abrindo.

## 1.0.1 — 22/11/2026 — Nova identidade visual

- **Novo logo**: gato sentado de perfil, em silhueta (navy `#16243F` / azul `#3F7CC4`), com o nome em DM Sans.
- **Novo ícone do app** (`Assets\CleanWinVista.ico`, 16–256 px): gato branco sobre quadrado azul; versão simplificada em 16–24 px. Vale para o exe, janelas, barra de tarefas e instalador.
- `Assets\CleanWinVista-64.png` atualizado (Configurações e assistente de instalação).
- README com o logo (versões para tema claro e escuro) e imagem de prévia social (`docs\images\social-preview.png`).

## 1.0.0 — 22/11/2026 — Inglês, 20 idiomas e instalador

- **Idioma principal: inglês.** Todo o texto do app parte do inglês; traduções em `i18n\<código>.json` ao lado do exe.
- **20 idiomas** (os mais falados): English, 简体中文, हिन्दी, Español, Français, العربية, বাংলা, Português (Brasil), Русский,
  اردو, Bahasa Indonesia, Deutsch, 日本語, Türkçe, Tiếng Việt, 한국어, Italiano, فارسی, தமிழ், Polski.
  Árabe, urdu e persa usam layout da direita para a esquerda. Datas e números seguem o idioma escolhido.
- Troca de idioma em Configurações → Language (pede reiniciar; botão "Restart now").
- **Instalador** com o visual do Dock (vidro, barra lateral de etapas): Idioma → Opções → Instalar → Concluir.
  Instala só para o usuário (sem admin) em `%LOCALAPPDATA%\Programs\CleanWinVista`, atalhos no Menu Iniciar/área de trabalho,
  iniciar com o Windows, entrada em "Aplicativos instalados" para desinstalar. O idioma escolhido vira o idioma do app.
- `dist\CleanWinVista-Setup-1.0.0.exe`: arquivo único (NSIS, `installer\CleanWinVista.nsi`) que extrai o app e abre o assistente.
- **Padrão de fábrica = o Dock do dono do app**: tema do sistema, luminosidade 4 %, espaçamento 6, margem interna 10, distância da borda 16,
  ampliação 1,1× (raio 170, influência 20 %, 30 ms, suavização 25 ms), clique minimiza o app ativo, mesma ordem/ligados/estilos dos widgets
  (CPU/RAM e rede em texto), apps fixados detectados por PC (Explorer, Bloco de Notas, Edge, Chrome, Steam, Gerenciador de Tarefas, Claude… se instalados).
  Internet dos widgets continua desligada por padrão (privacidade): o Clima pede para ativar.
- **Instalador**: opção "Also install CleanPano" (desmarcada) — abre o instalador oficial do CleanPano 1.16.1, conferido por SHA-256, com o pedido de administrador do Windows. Janela um pouco mais alta; botões de minimizar/maximizar removidos; etapa atual marcada.
- **Segurança (auditoria 1.0.0)**: instalação sempre numa subpasta `CleanWinVista`; desinstalação apaga só os arquivos do manifesto (nunca conteúdo alheio); caminhos com `% ^ ! "` recusados; pacote publicado sem PDB nem caminho de build; checagem de pacotes vulneráveis no `RUN.cmd`.
- **Janelas dos widgets com o padrão do Dock**: painéis, menus e lembretes usam o mesmo material (Acrylic/Mica, mesma cor e
  transparência), os mesmos cantos e a mesma borda da pílula. Os painéis abrem acima do Dock, sem cobri-lo, e para longe da borda da tela.

## 0.10.1 — 21/11/2026 — Calculadora completa

- Calculadora (ideias do app Unitto, código próprio): teclado padrão + científico (sin, cos, tan, asin, acos, atan,
  ln, log, √, ∛, xʸ, x², n!, π, e), graus/radianos, prévia do resultado enquanto digita, multiplicação implícita (2π, 3(1+2)),
  parênteses inteligentes, histórico salvo (clique reutiliza o resultado), copiar resultado; Enter calcula, Esc limpa.
- Aba **Datas**: diferença entre datas (anos/meses/dias, total de dias, semanas, dias úteis) e somar/subtrair dias/semanas/meses/anos.
- Aba **Bases**: converte entre decimal, hexadecimal, binário e octal (inteiros de 64 bits).

## 0.10.0 — 21/11/2026 — Calendário completo e doação

- Calendário do relógio (funções inspiradas no FullCalendar, código e visual próprios, monocromático):
  visões **Mês / Semana / Dia / Lista**, navegação ‹ › Hoje, número da semana (opcional), linha "agora",
  compromissos com dia inteiro (vários dias), horário, **repetição** (diária, dias úteis, semanal, mensal, anual, com fim opcional),
  **lembrete** (som + aviso no relógio), local e notas. Criar: botão +, clique duplo no dia (mês) ou clique num horário vazio
  (semana/dia). Editar/excluir clicando no compromisso. Sobreposições lado a lado. Importar/exportar **.ics**.
  A agenda iCal assinada (widget Agenda) aparece junto, somente leitura. Dica do relógio mostra o próximo compromisso do dia.
  Não incluído: arrastar compromissos para mudar o horário (editar pelo formulário).
- **Apoiar o projeto**: card nas Configurações com os endereços Bitcoin e Ethereum (copiar / abrir carteira), seção no README e `.github/FUNDING.yml`.

## 0.9.3 — 21/11/2026 — Notas rápidas com várias notas

- Widget Notas (ideias do Sticky Notes do GNOME, código próprio): várias notas; lista ordenada pela última edição,
  com busca, prévia e data; editor com negrito, itálico, sublinhado, tachado e lista com marcadores; excluir pela lista ou pelo editor, com "Desfazer". Salva sozinho (400 ms após parar de digitar).
- Teclado nos painéis: com um painel aberto (notas, calculadora, busca, conversor…) o Dock vira a janela ativa para receber as teclas; ao fechar, o foco volta ao app anterior.
- A nota única antiga é migrada para a lista na primeira abertura.
- Cores por nota (do Sticky) ficaram de fora: no popup do Dock as cores saíam desbotadas e o visual do app é monocromático.

## 0.9.2 — 21/11/2026 — Visual minimalista e arrastar widgets

- Rótulo do hover usa o mesmo vidro do Dock (Acrylic/Mica: 2ª janela de vidro atrás do texto; Translúcido: mesma cor e borda da pílula).
- Widgets monocromáticos (clássico minimalista): anéis, barras, gráficos, bateria, volume, foco e lembretes na cor do texto do Dock; upload/pausado em tom mais suave. Sem verde/azul/laranja/vermelho.
- Ampliação dos apps no hover não move mais o resto do Dock: com widgets, a expansão cresce só para a esquerda (widgets, separador e borda direita ficam parados). Janela ganhou folga extra para isso.
- Widgets podem ser arrastados para mudar a ordem no Dock; a ordem é salva na hora (Configurações → Widgets reflete a mudança). Apps fixados já reordenavam e salvavam; app aberto arrastado para a área dos fixados é fixado.

## 0.9.1 — 21/11/2026 — Painéis fecham ao clicar fora

- Ícones dos widgets de ferramenta e relógio analógico: sem disco cinza; só um contorno de 1 px um pouco mais escuro que o vidro (preto com alfa ~22%), dando relevo sutil. Obs.: traço fino com alfa renderiza certo na janela transparente (o problema #16 vale para preenchimentos).
- Calendário, painéis dos widgets e menus de clique direito agora fecham ao clicar em qualquer lugar fora do Dock
  (área de trabalho, outro app). Motivo: o Dock não recebe foco, então o fechamento automático do WinUI não via esses cliques.

## 0.9.0 — 21/11/2026 — 26 widgets de ferramenta (inspirados no CoolDock)

Funções inspiradas na lista pública do CoolDock (Mac), com código e visual próprios. Todos desligados de fábrica:
ative em Configurações → Widgets. Clique abre o painel; clique direito tem opções extras.

- **Utilitários**: Área de transferência (histórico de texto só na memória), Captura de tela (recorte do Windows),
  Conta-gotas (cor de qualquer ponto da tela → hex copiado), Emoji (painel Win + .), Conversor de unidades e moedas,
  Calculadora, Cronômetro com voltas, Timer com anel e som.
- **Produtividade**: Notas (salvas sozinhas), Tarefas (selo com pendentes), Água (copos/meta + lembrete), Postura
  (lembrete de pausa), Progresso do dia/semana/mês/ano, Relógio analógico, Relógio mundial (2 fusos).
- **Arquivos**: Downloads recentes (abrir, mostrar na pasta, arrastar para fora), Prateleira (solte arquivos no ícone),
  Favoritos de links, Busca (apps do menu Iniciar + busca do Explorador), Janelas abertas, Fotos recentes.
- **Sistema**: Atalhos rápidos (Wi-Fi, Bluetooth, tema escuro, microfone + atalhos de configurações), Gravador de voz
  (.m4a em Músicas\CleanWinVista).
- **Internet (opt-in)**: Clima (Open-Meteo), Agenda (link iCal), Moedas no Conversor (frankfurter.app), Serviços
  (GitHub, Stripe, Plausible, Shopify, Linear ou qualquer API JSON). Nova chave **“Permitir widgets com internet”**
  (desligada de fábrica); só HTTPS, sem redirecionamento, log guarda só o host; chaves de API criptografadas (DPAPI).
- Dados dos widgets em `%LOCALAPPDATA%\NovaTaskbar\widgets-data.json`; chaves em `secrets.bin` (DPAPI do usuário).

## 0.8.0 — 21/11/2026 — Saída de áudio, iniciar com o Windows, janela maximizada

- Clique no widget de Volume: lista os dispositivos de saída ativos e troca o padrão do Windows (todas as funções:
  console, multimídia, comunicação) + volume e mudo.
- **Iniciar com o Windows**: pergunta no 1º uso (diálogo) e chave em Configurações → Comportamento (HKCU\…\Run).
- Modo Inteligente também esconde com janela **maximizada** (além de tela cheia, navegador e Explorer).

## 0.7.1 — 21/11/2026 — Marca CleanWinVista

- Nome do produto: **CleanWinVista** (títulos de janela, Configurações, menu "Sair", metadados do .exe).
  Pastas internas e dados do usuário (%LOCALAPPDATA%\NovaTaskbar) mantidos para não perder configurações.
- Novo ícone: "gato-janela" (traço único arredondado, violeta #7C5CFA): corpo = janela com barra de título, orelhas, olhos e cauda; antes, direção C,
  .ico com 16–256 px; versão simplificada de traço grosso em 16–24 px para ler na barra de tarefas.

## 0.7.0 — 21/11/2026 — Auto-ocultar, teclado, periféricos, Configurações de vidro

- **Auto-ocultar** (Comportamento → Visibilidade): *Sempre visível*, *Inteligente* (padrão: some com navegador,
  Explorador de Arquivos ou app em tela cheia em primeiro plano) e *Auto-ocultar*. Volta ao encostar o mouse na borda;
  esconde 0,7 s após o mouse sair. Desliza para fora e é "camuflado" no DWM (SW_HIDE derruba janelas WinUI).
- **Widget Teclado**: idioma da janela ativa ("PT", "EN"); clique/roda troca, clique direito escolhe. Só com 2+ idiomas.
- **Widget Periféricos**: um anel por aparelho Bluetooth conectado com bateria (fones, mouse, teclado, controle),
  menor carga primeiro; vermelho < 20 %, laranja ≤ 40 %. Fontes: propriedade de bateria do Windows e serviço GATT 0x180F.
- **Bateria** só aparece em notebook/tablet (com bateria).
- **Ícones centralizados** nos anéis: correções por glifo medidas na própria fonte Segoe Fluent Icons (`Glyphs`).
- **Configurações** com o vidro do Dock (mesmo material, cor, intensidade e luminosidade) e cartões translúcidos.
- Corrigido: widgets podiam ser criados com tamanho 0 durante a 1ª aplicação de configurações (SyncItems reentrante),
  derrubando o XAML; corrigido NullReference na leitura de mídia durante troca de faixa.

## 0.6.1 — 21/11/2026 — Remoção do widget do Claude + auditoria de segurança

- Removido o widget de uso do Claude (a contagem local não refletia o uso real). Configurações antigas com o widget
  são lidas normalmente: o item desconhecido é descartado (`TolerantWidgetKindConverter`), o resto é mantido.
- Ícones dos apps menores (78 % da vaga) e alinhados aos pixels físicos — mais nítidos.
- Auditoria de segurança (`docs/SECURITY-AUDIT.md`): logs com teto de 5 MB/dia, `install-sdk.ps1` verifica a
  assinatura da Microsoft, `.gitignore` cobre diagnósticos locais; nenhum pacote com vulnerabilidade conhecida.

## 0.6.0 — 21/11/2026 — Widgets contextuais (Claude, música)

- **Widgets contextuais**: aparecem/somem sozinhos (timer de 5 s só no modo Estendido horizontal).
- **Uso do Claude**: aparece com o Claude aberto (claude.exe / AUMID) ou com Claude Code usado nos últimos 15 min.
  Lê os registros locais do Claude Code (`~/.claude/projects/**/*.jsonl`, leitura incremental, fora do UI thread),
  agrupa em janelas de 5 h (início na hora cheia), mostra tokens usados, % da meta do usuário e tempo até renovar.
  Clique abre claude.ai/settings/usage (limite real do plano). Sem rede, login ou cookies — o limite do plano não é
  público, por isso a meta é configurável (Configurações → Widgets).
- **Música**: o widget só aparece com música tocando (8 s de carência ao pausar para retomar pelo Dock).

## 0.5.0 — 21/11/2026 — Posição do Dock, padrão de fábrica e ícones nítidos

- **Posição**: Embaixo, Em cima, Esquerda (vertical), Direita (vertical) — em Configurações → Posição na tela ou
  clique direito → Posição. Layout calculado num espaço canônico e mapeado por `DockOrientation` (Core, testado);
  rótulo ao lado do ícone na vertical; menus abrem para longe da borda. Widgets ocultos na vertical.
- **Padrão de fábrica** = escolha do dono do app (21/11): modo Estendido com widgets, ícones 40, cantos 12, Acrylic escuro.
- **Ícones**: extraídos a 256 px e reduzidos por média de área (`ImageResampler`) ao tamanho exato de pixels;
  caixa do ícone = tamanho em repouso (bitmap 1:1); desenho ocupa 86 % da vaga. Cache de ícones v2.

## 0.4.0 — 21/11/2026 — Estilos de visualização + código dos widgets reorganizado

### Adicionado
- Estilos para CPU/RAM (Anéis, Velocímetro, Gráfico de linha, Gráfico de barras, Só números) e Rede (Linha, Barras
  espelhadas ↓/↑, Só números). Tipos de visualização inspirados no Stat Space; desenho e código próprios.
- Histórico de 40 amostras (80 s) por métrica (`History`, Core) com escala "agradável" 1/2/5×10ⁿ.
- Troca de estilo pelo clique direito no widget (submenu *Estilo*) ou em Configurações → Widgets.

### Alterado
- `WidgetViews.cs` (673 linhas) dividido: `Widgets/Views/*` (um arquivo por widget), `Widgets/Visuals/*`
  (Ring, Gauge, Chart, RollingText, Palette, GeometryBuilder), `WidgetActions`, `CalendarFlyout`, `WidgetLabels`.
- Cores de formas opacas (misturadas com o fundo) — a janela transparente não compõe alfa parcial de formas.

### Corrigido
- Anéis apareciam sempre cheios (trilho translúcido renderizado sólido).
- "Nada tocando" truncado (equalizador agora colapsa sem sessão).
- Janelas do Dock minimizadas por terceiros (SC_MINIMIZE bloqueado; restauração automática).

## 0.3.0 — 21/11/2026 — Widgets (modo Estendido)

- Modo Estendido liberado: widgets à direita dos apps após um separador.
- Mídia (capa, título, equalizador no compositor), CPU/RAM, Rede, Volume (roda ±2 %, controle deslizante, mudo),
  Bateria, Timer de foco, Relógio (minutos rolando; clique abre calendário). Dados locais, sem internet.
- Timer de 1 s só com widgets visíveis; CPU/rede/volume a cada 2 s, bateria a cada 30 s, mídia por eventos.

## 0.2.1 — 20/11/2026 — Padrão de fábrica

- Padrão inicial = configuração escolhida pelo dono do app: Clássico, Acrylic, tema escuro, cor 60 %, luminosidade 38 %,
  sem borda, cantos 10, ícones 33, espaçamento 8, preenchimento 12, margem 10, ampliação 131 %, raio 53,
  animação 40 ms (cúbica), suavidade 70 ms. "Clássico" e "Restaurar aparência padrão" reproduzem esse padrão.
- Apps padrão: Explorador, Bloco de Notas (empacotado), Edge, Chrome e demais instalados detectados no PC.
- Presets Flutuante (compacto, ícones 28) e Estendido (ícones 40) recalibrados a partir do novo Clássico.

## 0.2.0 — 20/11/2026 — Fase 2 (gerenciamento de apps)

### Adicionado
- Projeto `NovaTaskbar.Windows` (Win32 isolado) e `NovaTaskbar.IntegrationTests` (13 testes contra o Windows real).
- Rastreamento de janelas por eventos (`SetWinEventHook`, debounce 60 ms) — sem polling.
- Filtro de janelas de app, identidade por AUMID/exe/destino de `.lnk`, apps UWP via CoreWindow filha.
- Apps abertos não fixados após um separador, em ordem estável; indicadores de execução e de app ativo.
- Clique: abre / traz a janela / alterna entre várias / minimiza (opcional). Botão do meio: nova janela.
- Menu de app: Nova janela, Mostrar janela(s), Fechar janela / todas, Fixar / Remover.
- Reordenar arrastando (histerese 6 DIP, 1:1, FLIP com mola); arrastar app aberto para os fixados o fixa.
- Soltar `.exe`/`.lnk` do Explorer no Dock para fixar (com marcador de posição).
- Apps empacotados: fixar/abrir/ícone/nome por `shell:AppsFolder\<AUMID>`.
- Cache de ícones em disco com invalidação pela data do arquivo.
- Motor de magnificação v2 com itens de peso variável (entrada/saída sem saltos) e separador.
- Molas no modelo da Apple (resposta + amortecimento) — curva padrão "Mola", interrompível.
- Feedback no pointer-down, rótulo ancorado no ícone (96 %→100 %, 150 ms), entrada de itens de 92 %→100 %, respeito a "Efeitos de animação".
- Configurações: mostrar apps abertos, indicadores, clique minimiza, curva "Mola".
- Referência de movimento: skills *Apple Design* e *Animate* (Emil Kowalski), listadas em designbookmark.com/skills.

### Corrigido
- **Moldura invisível de 3 px**: o `OverlappedPresenter` recoloca `WS_DLGFRAME`, encolhendo a área cliente (cortava a base do Dock desde a Fase 1). Agora `WM_NCCALCSIZE` zera a área não-cliente das duas janelas.
- **Exceção ao trocar o tema** (`ArgumentException` em `OnDefaultSystemBackdropConfigurationChanged`) — encontrada no log após uso real; os backdrops próprios agora sobrescrevem o método.
- Indicadores ficavam sob os ícones → camada acima.

## 0.1.0 — 20/11/2026 — Fase 0 + Fase 1 (Classic Dock)

### Adicionado
- Inspeção do ambiente e instalação do .NET 10 SDK no usuário (sem admin).
- Solução `NovaTaskbar.sln`: `NovaTaskbar.Core`, `NovaTaskbar.App` (WinUI 3, Windows App SDK 2.7.0), `NovaTaskbar.UnitTests`.
- Motor de magnificação estilo macOS (puro, sem alocação) + animador com curvas e suavização do cursor.
- Geometria do Dock (janela fixa com folga para ampliação e rótulo; pílula variável).
- Dock flutuante inferior centralizado: duas janelas (ícones transparente + vidro Acrylic/Mica do DWM).
- Materiais: Acrylic, Acrylic fino, Mica, Translúcido (raio livre). Cor, intensidade, luminosidade, borda, tema.
- Ícones reais em alta resolução via `IShellItemImageFactory` em thread STA.
- Clique abre o app (`ShellExecuteEx`) com animação de salto; rótulo ao passar o mouse.
- Menu de contexto: Abrir, Remover do Dock, Adicionar app…, Configurações…, Sair.
- Janela de Configurações (Mica, MVVM) com aplicação instantânea; modos Clássico e Flutuante.
- Configuração JSON versionada com escrita atômica, backup, recuperação de arquivo corrompido e validação.
- Instância única; 2ª execução abre as Configurações da instância ativa.
- Log em arquivo (7 dias). 55 testes automatizados.

### Corrigido durante a verificação
- `AppWindow.Show(false)` exibia as janelas minimizadas → `ShowWindow(SW_SHOWNOACTIVATE)` + estilo `WS_POPUP`.
- Vidro claro no tema escuro → cor automática explícita por tema.
- Margem de sombra invadia a barra de tarefas nativa → removida.
- Configurações abriam atrás de outras janelas e maiores que a tela → centralizadas, limitadas à área de trabalho, foco concedido pela 2ª instância.
