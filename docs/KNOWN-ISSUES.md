# Problemas conhecidos

| # | Problema | Contorno / plano |
|---|---|---|
| 1 | Nos materiais Acrylic/Mica o raio dos cantos é o do Windows (~8 px), não configurável. | Use "Translúcido" para raio livre. Investigar alternativa sem serrilhado (Fase 4). |
| 2 | Intensidade do desfoque não é ajustável (o Windows não expõe). | Ajuste cor, intensidade e luminosidade. |
| 3 | Calculadora/Bloco de Notas (apps empacotados) aparecem com ícone legado se fixados pelo `.exe` de System32. | Suporte a AUMID na Fase 2. Removidos dos padrões. |
| 4 | Sombra no material Translúcido desativada: a margem de sombra fazia a janela invadir a barra nativa e capturar cliques. | Reimplementar sombra dentro da área da pílula (Fase 4). Configuração `shadowEnabled` reservada. |
| 5 | Só monitor principal e borda inferior. | Fases 3 e 4. |
| 6 | Memória de ~136 MB (Debug, Windows App SDK self-contained). | Medir Release e otimizar na Fase 5. |
| 8 | Durante a magnificação a janela inteira do Dock (incluindo a faixa acima dos ícones) captura o mouse. | Comportamento intencional enquanto o cursor está no Dock; volta à pílula ao sair. |
| 9 | Janelas de PWAs do Chrome/Edge sem pin próprio aparecem agrupadas no ícone do navegador quando ele está fixado pelo .exe. | Fixar o PWA pelo atalho (AUMID) separa. |
| 10 | Ícone de app empacotado em cache não é invalidado quando o app atualiza (não há arquivo para comparar a data). | Apagar `%LOCALAPPDATA%\NovaTaskbar\IconCache`; invalidação por versão do pacote na Fase 5. |
| 11 | `SetForegroundWindow` pode ser negado pelo Windows em casos raros (regra de primeiro plano); o app pisca na barra. | Clicar de novo; investigar alternativa documentada na Fase 3. |
| 12 | Janelas de outras áreas de trabalho virtuais ("cloaked") não aparecem (igual à barra do Windows no padrão). | Opção na Fase 4. |
| 13 | Memória após 30 min com testes: ~204 MB WS (Debug). | Perfil de memória/vazamentos na Fase 5. |
| 14 | Menu de contexto no tema claro tem texto com baixo contraste. | Ajuste de tema do popup na Fase 4. |
| 15 | A instância usada durante os testes interativos acumulou 104 s de CPU em ~14 min (arrastos, sliders, a ferramenta de teste ocultando/mostrando janelas). Ociosa mede 0,000–0,026 %. | Perfilar uso sob interação contínua na Fase 5 (contador `Diag:` já existe no log em nível Debug). |
| 16 | Formas com cor semitransparente (alfa parcial) aparecem sólidas na janela transparente do Dock. | Widgets usam cores opacas misturadas com o fundo (`Palette.Mix`); o trilho pode parecer um pouco mais claro sobre fundos claros atrás do vidro. |
| 17 | Gráficos de rede com tráfego baixo (< 64 KB/s) mostram barras mínimas (escala mínima intencional). | Ajustar escala mínima se necessário. |
| 18 | Na posição vertical (Esquerda/Direita) os widgets ficam ocultos. | Versões compactas de widgets para coluna — futuro. |
| 19 | Ícone ampliado (hover) é o bitmap de repouso escalado (~1,3×), um pouco menos nítido que em repouso. | Carregar bitmap maior durante a ampliação, se necessário. |
| 5 | (atualizado) Bordas Superior/Esquerda/Direita implementadas; ainda só no monitor principal. | Multi-monitor na Fase 3. |
| 21 | Janelas WinUI não podem ser escondidas com ShowWindow(SW_HIDE) a partir do Dock (XAML cai com 0x80040111). | Auto-ocultar usa deslocamento + DWM cloak. |
| 22 | Widget Periféricos só vê aparelhos Bluetooth que o Windows reporta com bateria; receptores USB proprietários (ex.: Logi Bolt/Unifying) não aparecem. | Limitação do Windows. |
| 23 | Troca de idioma pelo widget usa WM_INPUTLANGCHANGEREQUEST na janela ativa; apps elevados (admin) ignoram o pedido. | Limitação do Windows (UIPI). |
| 24 | Widgets 0.9.0 ainda **não testados na tela** (compilam; teste manual pendente pelo usuário). | Relatar o que falhar. |
| 25 | Histórico da área de transferência guarda só texto e só enquanto o app está aberto (sem disco, por privacidade). | Intencional. |
| 26 | Atalhos rápidos: Wi-Fi/Bluetooth dependem de o Windows permitir controle de rádio a apps desktop; "Não perturbe" e "Luz noturna" abrem a página de configurações (sem API pública para alternar). | Limitação do Windows. |
| 27 | Gravador de voz exige "Permitir que apps da área de trabalho acessem o microfone" ligado em Privacidade. | O widget abre a página se for negado. |
| 28 | E-mail (não lidos) do CoolDock não foi incluído: exigiria login OAuth em Gmail/Outlook. | Futuro, via conector oficial. |
| 28 | Traduções (19 idiomas) feitas por IA; podem ter termos pouco naturais. | Revisão por falantes nativos; corrigir em `i18n\<código>.json`. |
| 29 | Trocar o idioma exige reiniciar o app. | Intencional (botão "Restart now"). |
| 30 | Instalador não é assinado digitalmente: o SmartScreen pode avisar "Editor desconhecido". | Assinatura de código antes de distribuir. |
| 31 | Instalação completa (copiar, atalhos, registro), desinstalação e a abertura do instalador do CleanPano **não testadas na tela**; testados: assistente, troca de idioma ao vivo, RTL, página de opções com a opção CleanPano (hash conferido) e o .exe único abrindo o assistente. | Testar instalar/desinstalar. |
| 32 | Painéis com material Acrylic/Mica: sobre fundos muito claros, botões e texto claros perdem contraste (mesma transparência do Dock). | Aumentar "Intensidade da cor" nas Configurações ou usar tema claro. |
| 33 | Submenus (ex.: Posição no menu do Dock) e listas suspensas dentro dos painéis ainda usam o fundo padrão do Windows. | Estilizar na próxima rodada. |
| 34 | O botão Fechar (X) do instalador tem pouco contraste sobre fundos claros (vidro). | Usar Cancelar; ajustar cor dos botões da barra de título. |
