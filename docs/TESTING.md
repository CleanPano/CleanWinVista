# Testes — Nova Taskbar

Ambiente real: **Windows 11 Home 26H2 (build 26300.9974)**, x64, monitor único 2560×1440 a **150%**,
.NET SDK 10.0.404, Windows App SDK 2.7.0. Executado em 20/11/2026.

## Automatizados (xUnit)

`scripts\build.ps1 -Integration` ou `scripts\test-only.ps1` (sem parar o Dock).

**Unitários: 99 · 99 passaram · 0 falharam** (`docs/test-results/unit-tests.trx`).
**Integração (Windows real): 13 · 13 passaram · 0 falharam** (`docs/test-results/integration-tests.trx`).

### Fase 2 — unitários adicionados (44)

| Grupo | Qtde | Cobre |
|---|---|---|
| WeightedEngine | 5 | itens com peso (entrada/saída sem salto), separador não amplia, presença 0 não ocupa espaço, hit-test ignora itens sumindo |
| Spring | 6 | crítica sem overshoot, sub-amortecida quica, reversão preserva valor/velocidade, resposta 0, animador por mola revertendo no meio, curva ease-out forte |
| WindowFilter | 11 | normal, oculta, cloaked, sem título, ferramenta, com dono, no-activate, APPWINDOW, próprio processo, shell, CoreWindow UWP |
| DockItemsBuilder | 9 | casa por exe (sem diferenciar maiúsculas), .lnk pelo destino, empacotado por AUMID, AUMID tem prioridade, agrupamento e ordem estável, ocultar abertos, app ativo, abrir via AppsFolder, app fechado sai da ordem |
| Validação AUMID | 8 | aceita AUMIDs válidos, recusa vazio/espaço/../aspas, nome padrão legível, padrões de comportamento |
| ClickPolicy | 5 | abrir, trazer a mais recente, minimizar só se configurado, alternar para a menos recente, sem caminho |

### Integração (13) — `tests/NovaTaskbar.IntegrationTests`

| Teste | Verifica |
|---|---|
| Describe/ToolWindow/Owned/Hidden | dados reais de janelas Win32 criadas no teste e o filtro |
| Snapshot | exclui o próprio processo; **0,58 ms por enumeração** (5 janelas de app) |
| Minimizar/restaurar, handle inválido | ações em janela real |
| Extração de ícone | ≥ 96 px, BGRA pré-multiplicado |
| Atalho .lnk | destino lido via IShellLink (atalho criado no teste) |
| Nome amigável / app empacotado | FileVersionInfo; Calculadora via shell:AppsFolder (nome + ícone) |
| Lançador | recusa caminho inexistente, UNC e .bat |
| **Bloco de Notas ponta a ponta** | abrir → descoberto em 253 ms com AUMID `Microsoft.WindowsNotepad_8wekyb3d8bbwe!App` → casado com pin AppsFolder → minimizar → restaurar → fechar (WM_CLOSE). Só fecha a janela que ele abriu; pula se o Bloco de Notas já estiver aberto. |

### Fase 1 — unitários (55)

| Grupo | Qtde | Cobre |
|---|---|---|
| MagnificationEngine | 18 | escala no foco/fora do raio, monotonia, simetria, influência dos vizinhos, layout em repouso, sem sobreposição, continuidade (sem saltos), ícone sob o cursor fica sob o cursor, expansão máxima, desligado, entradas inválidas, independência de DPI (100/150/200%) |
| MagnificationAnimator | 9 | entrada/saída completas, entrada sem salto, movimento brusco suavizado, pausa longa limitada, ocioso não anima, curvas |
| SettingsValidator | 15 | padrões válidos, limites, caminhos inseguros rejeitados (UNC, relativos, .bat/.ps1, URI, aspas), duplicados, cor inválida, presets de modo |
| SettingsStore | 8 | arquivo ausente, ida e volta, backup, corrompido → backup, corrompido sem backup → padrões, esquema futuro recusado, migração v0→v1, enums como texto |
| DockGeometry | 5 | janela comporta a expansão máxima, folga para o ícone ampliado, posição inferior centralizada, Dock vazio, centralização |

## Manuais executados no Windows real (com screenshots)

| Teste | Resultado |
|---|---|
| Build Debug x64 | ✅ 0 avisos, 0 erros |
| Inicialização | ✅ 215–304 ms até o Dock visível |
| Dock aparece inferior centralizado, acima da barra nativa | ✅ |
| Janelas não minimizadas, popup, topmost, toolwindow, noactivate | ✅ (`diag/windows.txt`) |
| Acrylic desfoca o papel de parede | ✅ |
| Hover: ícone e vizinhos ampliam, pílula alarga, rótulo aparece | ✅ |
| Sair com o mouse: volta ao repouso | ✅ |
| Clique no Explorador abre janela em primeiro plano + salto | ✅ |
| Clique direito: menu de contexto | ✅ |
| Configurações: cor, modo Flutuante, material Translúcido aplicam na hora | ✅ |
| 2ª execução não cria 2º processo e abre Configurações em primeiro plano | ✅ |
| CPU ociosa 20 s | ✅ 0,000% · WS 136 MB · privado 92 MB (Debug) |

## Manuais da Fase 2 (Windows real, screenshots)

| Teste | Resultado |
|---|---|
| Fixados + separador + apps abertos (Notas Autoadesivas, Claude, Bloco de Notas) | ✅ |
| Indicadores sob apps abertos; alongado no app ativo | ✅ (após correção — ver CHANGELOG) |
| Clique no Explorador (aberto atrás) traz a janela para frente | ✅ |
| Menu de app aberto: Nova janela, Mostrar janela, Fechar janela, Fixar no Dock… | ✅ exibido |
| Fixar no Dock (Bloco de Notas, empacotado) → vai para os fixados com ícone moderno | ✅ |
| Arrastar o Bloco de Notas para antes do Edge → vizinhos abrem espaço, ordem gravada | ✅ |
| Trocar tema com o Dock aberto | ✅ corrigida exceção (OnDefaultSystemBackdropConfigurationChanged) |

Observação de método: a ferramenta de captura usada nos testes **oculta janelas de apps não autorizados** a cada screenshot;
o Dock reage corretamente (o app some e volta), mas isso impede testar menus desses apps. O Bloco de Notas foi autorizado para os testes.

## Não verificado (ainda)

- Acrylic fino e Mica visualmente.
- Fase 2: alternar entre várias janelas, minimizar ao clicar, botão do meio, Fechar janela/todas pelo menu,
  soltar arquivo do Explorer, arrastar app aberto para os fixados, "reduzir animações" do Windows, toggles novos.
- Ações do menu "Adicionar app…", "Remover do Dock", "Sair" (código pronto, não clicadas).
- Troca de resolução/DPI em tempo de execução; DPI real de 100% e 200%.
- Execução prolongada (estabilidade de memória por horas) — Fase 5.
- Testes de integração Win32 automatizados — Fase 2.

## Como rodar

```powershell
scripts\build.ps1            # build + testes
scripts\build.ps1 -Run       # build + testes + inicia o Dock
scripts\measure.ps1 -Seconds 20   # CPU/memória ociosa
scripts\diag.ps1             # janelas do processo + cópia do log
```
