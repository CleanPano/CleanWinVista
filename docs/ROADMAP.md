# Roadmap — Nova Taskbar

Status: **Fases 0 e 1 verificadas · Fase 2 (gerenciamento de apps) entregue e verificada no Windows 11 real** — 20/11/2026.

Legenda: ✅ Verificado (testado com sucesso) · 🟦 Código completo (implementado, falta verificação) · 🟨 Em andamento · ⬜ Pendente · ⛔ Limitação técnica

## Matriz de status

| Funcionalidade | Status | Evidência |
|---|---|---|
| **Fase 0** | | |
| Inspeção do ambiente (SO, SDKs, VS, WinAppSDK, rede) | ✅ | `docs/env-report.txt` |
| .NET 10 SDK 10.0.404 instalado (usuário, sem admin) | ✅ | log do `install-sdk.ps1` |
| Solução + projetos Core/App/UnitTests | ✅ | build OK |
| Documentação inicial / ADRs | ✅ | `ARCHITECTURE.md` |
| **Fase 1 — Classic Dock** | | |
| Dock flutuante inferior centralizado (monitor principal, área de trabalho) | ✅ | screenshot + `diag/windows.txt` |
| Sempre no topo, sem foco, fora do Alt+Tab/barra | ✅ | estilos verificados; clicar não rouba foco |
| Transparência real (janela transparente) | ✅ | desktop visível ao redor da pílula |
| Desktop Acrylic com desfoque real | ✅ | papel de parede desfocado sob o Dock |
| Acrylic fino / Mica | 🟦 | implementado; não inspecionado visualmente |
| Material Translúcido com raio livre | ✅ | testado (raio 16, cor azul) |
| Tema claro/escuro/automático | 🟦 | escuro verificado; claro não inspecionado |
| Ícones reais em alta resolução (exe/lnk) | ✅ | Explorer, Edge, Chrome, Steam, Gerenciador |
| Magnificação estilo macOS (vizinhos, segue o cursor, sem saltos) | ✅ | 27 testes (motor + animador) + inspeção visual |
| Alargamento suave da pílula na magnificação | ✅ | visual |
| Animação de entrada/saída e salto ao abrir | ✅ | visual |
| Rótulo (tooltip) do app | ✅ | visual |
| Clique abre o app | ✅ | Explorer abriu em primeiro plano |
| Menu de contexto (Abrir, Remover, Adicionar, Configurações, Sair) | ✅ menu · 🟦 Adicionar/Sair | Remover via Configurações usado pelo usuário |
| Configurações básicas com aplicação instantânea | ✅ | cor, modo e material mudaram ao vivo |
| Modos Clássico / Flutuante | ✅ | testados |
| Modo Estendido | ✅ | widgets visíveis com dados reais |
| Persistência JSON versionada, backup, recuperação | ✅ | 8 testes automatizados |
| Instância única + "abrir configurações" pela 2ª execução | ✅ | `SINGLE_INSTANCE: antes=1 depois=1` |
| Reposicionar em mudança de resolução/DPI/área de trabalho | 🟦 | implementado (mensagens Win32), não exercitado |
| DPI 100% / 200% | 🟦 | matemática testada; tela real só a 150% |
| CPU ociosa ≈ 0 | ✅ | 0,000% em 20 s |

| **Fase 2 — Gerenciamento de apps** | | |
| Descoberta de janelas por eventos (SetWinEventHook) + filtro (ocultas, ferramenta, com dono, sistema, cloaked) | ✅ | 11 testes unitários + 4 de integração com janelas Win32 reais |
| Identidade de app (AUMID, exe, destino de .lnk) | ✅ | testes + Bloco de Notas real casado por AUMID |
| Apps abertos não fixados após separador, ordem estável | ✅ | visual (Notas, Claude, Bloco de Notas) |
| Indicador de execução + indicador alongado do app ativo | ✅ | visual (após corrigir a moldura do presenter) |
| Clique: abre / traz janela / alterna entre várias / minimiza (opcional) | ✅ abrir e trazer · 🟦 alternar e minimizar | Explorador trazido à frente; política com 5 testes |
| Botão do meio: nova janela | 🟦 | implementado, não clicado |
| Menu: Nova janela, Mostrar janela(s), Fechar janela/todas, Fixar/Remover, Adicionar, Configurações, Sair | ✅ menu e Fixar · 🟦 demais itens | menu de app aberto exibido; Fixar testado |
| Reordenar arrastando (1:1, histerese, vizinhos com mola, grava a ordem) | ✅ | Bloco de Notas arrastado para antes do Edge; persistido |
| Arrastar app aberto para os fixados (fixa) | 🟦 | implementado, não testado |
| Soltar .exe/.lnk do Explorer para fixar | 🟦 | implementado; arrastar do Explorer não é permitido pela ferramenta de teste |
| Apps empacotados (fixar/abrir/ícone/nome via shell:AppsFolder) | ✅ | Bloco de Notas fixado com ícone moderno; testes |
| Minimizar / restaurar / fechar janelas | ✅ | integração: janela Win32 + Bloco de Notas real |
| Cache de ícones em disco com invalidação por data | 🟦 | implementado; não medido |
| Configurações: apps abertos, indicadores, clique minimiza, curva "Mola" | 🟦 | UI exibida; toggles não clicados |
| Animações (molas Apple, entrada/saída de itens, rótulo ancorado, reduzir movimento) | ✅ visual parcial | entrada/saída e reordenação vistas; reduzir movimento não testado |
| CPU ociosa com hooks ativos | ✅ | 0,000% (30 s) e 0,017% (30 s logo após os testes) |
| **Widgets (v0.3–0.4)** | | |
| Mídia, CPU/RAM, Rede, Volume, Bateria, Foco, Relógio | ✅ | screenshot com dados reais (música tocando, taxas de rede, CPU/RAM) |
| Estilos Anéis / Velocímetro / Linha / Barras (CPU/RAM e Rede) | ✅ Anéis, Velocímetro, Linha, Barras · 🟦 Só números | screenshots |
| Submenu Estilo (clique direito) e seletor em Configurações | 🟦 | implementado; não clicado (ferramenta bloqueia clique direito com o Explorer em primeiro plano) |
| Histórico/escala (`History`) | ✅ | 8 testes |
| **Posição (v0.5)** | | |
| Embaixo / Em cima / Esquerda / Direita (magnificação, rótulo, indicadores, separador) | ✅ | screenshots das 4 bordas com hover; 11 testes de orientação |
| Seletor em Configurações e submenu Posição | ✅ seletor · 🟦 submenu | Esquerda/Embaixo clicados em Configurações, Dock mudou ao vivo |
| Ícones nítidos (redução por média de área, 1:1) | ✅ | screenshot; 3 testes |
| **Widgets contextuais (v0.6)** | | |
| Uso do Claude | ❌ removido (0.6.1) | contagem local não refletia o uso real |
| Música só quando tocando | ✅ oculto sem música · 🟦 aparecer ao tocar | widget sumiu sem sessão de mídia |
| Configurações: estilos por widget | ✅ |
| **v0.7** | | |
| Auto-ocultar Inteligente (Explorer em foco esconde; borda revela; sair esconde) | ✅ | testado na tela |
| Widget Teclado (PT↔EN pela janela ativa) | ✅ | clique trocou POR→ENG→POR na barra do Windows |
| Widget Periféricos Bluetooth | 🟦 | implementado; sem aparelho com bateria conectado no teste (widget oculto) |
| Bateria só em notebook | ✅ | oculto no desktop |
| Ícones centralizados nos anéis | ✅ | screenshot (volume/foco) |
| Configurações com vidro do Dock | ✅ | screenshot | tela de Configurações inspecionada |

## Próximas fases

### Ideias avaliadas de Latte Dock (GPL — só ideias) e OpenDock
- Indicador de áudio por app (ícone mostra que o app está tocando som; roda ajusta o volume daquele app).
- Mais modos de visibilidade: esconder só com janela maximizada / ao sobrepor o Dock; atraso configurável.
- Barra de progresso e selos (contagem) sobre ícones (downloads, mensagens).
- Roda do mouse sobre um ícone alterna entre as janelas do app.
- Reservar a borda da tela (janelas maximizadas não ficam sob o Dock).
- Iniciador com busca de apps + ações de energia; widgets de Notas, Tarefas, Próximo evento (iCal), Progresso do dia.
- Automação (gatilho → condição → ação, ex.: "modo reunião").
- Restauração da barra do Windows com processo guardião (para o modo "substituir barra").

### Fase 3 — Integração avançada (ordem combinada; recursos do OpenTaskbar, MIT, com atribuição)
1. Miniaturas DWM das janelas · 2. Auto-ocultar (Smart/Sempre) + tela cheia · 3. Iniciar com o Windows ·
4. Importar fixados da barra do Windows · 5. Alinhamento e borda · 6. Ocultar barra nativa com recuperação ·
7. Jump Lists · 8. Widgets extras (GPU, armazenamento, cronômetro, Bluetooth, saída de áudio).

### Fase 4 — Personalização
Painel completo, widgets (modo Estendido), perfis, importar/exportar, posições topo/esquerda/direita.

### Fase 5 — Endurecimento e lançamento
Recuperação de falhas, perfilamento, vazamentos, revisão de segurança, instalador (MSIX × WiX), documentação final.
