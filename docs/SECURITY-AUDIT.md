# Relatório de Auditoria de Segurança — Nova Taskbar

**Data**: 2026-11-21
**Escopo**: código-fonte (Core, Windows, App, testes), scripts de build, docs, artefatos locais (`docs/diag`, `docs/test-results`, logs), configuração `.gitignore`. Versão 0.6.0.
**Modo**: read-only na análise; correções aplicadas com autorização do dono do projeto.
**Ferramentas usadas**: análise manual + grep de padrões de segredo (chaves de IA, cloud, pagamentos, tokens, chaves privadas, connection strings) em todo o projeto, inclusive docs e artefatos de teste. gitleaks/semgrep/trivy indisponíveis no PC.
**Limitações**: sem repositório Git (sem histórico a analisar); binários de build não inspecionados com `strings`; sem `dotnet list package --vulnerable` (rodar antes do release); app sem backend/servidor (Fases 4–5 reduzidas).

## Scorecard Executivo

| Categoria                  | Status | Críticos | High | Medium | Low |
|----------------------------|--------|----------|------|--------|-----|
| Secrets & Credentials      | ✅ OK  | 0 | 0 | 0 | 0 |
| Access Control / Auth      | n/a (app local, sem contas) | 0 | 0 | 0 | 0 |
| Business Logic             | n/a (sem licença/pagamento ainda) | 0 | 0 | 0 | 0 |
| Privacy & Dados Pessoais   | ✅ corrigido | 0 | 0 | 0 | 2 |
| Frontend / Builds          | ⚠️ release | 0 | 0 | 0 | 1 |
| Supply Chain               | ✅ corrigido | 0 | 0 | 1 | 0 |
| Infra / Desktop            | ✅ corrigido | 0 | 0 | 1 | 1 |

**Veredito Final**: **GO com restrições** (para distribuição: assinar o executável e checar pacotes vulneráveis).
**Justificativa**: nenhum segredo ou credencial no projeto; sem rede oculta nem telemetria; entradas de configuração validadas. Os dois achados médios foram corrigidos e revalidados.

## Achados

### [MEDIUM] Leitura dos registros do Claude Code sem limite de memória — CORRIGIDO (widget removido na 0.6.1)
- **Status**: Confirmada
- **Localização**: `src/NovaTaskbar.Core/Usage/ClaudeLogReader.cs` (`ReadNew`)
- **Evidência**: alocava `new byte[length - offset]` — um .jsonl de centenas de MB era lido inteiro de uma vez.
- **Impacto**: pico de memória/travamento do Dock (negação de serviço local) com sessões longas do Claude Code.
- **Remediação aplicada**: leitura em blocos de 1 MB; arquivo novo grande lê só os últimos 16 MB; linha incompleta acima de 4 MB é descartada.
- **Referências**: CWE-400, CWE-789.

### [MEDIUM] Script de instalação executava código baixado sem verificar assinatura — CORRIGIDO
- **Status**: Confirmada (script de desenvolvimento, não vai no app)
- **Localização**: `scripts/install-sdk.ps1`
- **Impacto**: um `dotnet-install.ps1` adulterado (proxy/DNS comprometido) rodaria com os direitos do usuário.
- **Remediação aplicada**: só executa se `Get-AuthenticodeSignature` for `Valid` e o certificado for da Microsoft.
- **Referências**: CWE-494.

### [LOW] Arquivos de diagnóstico com dados da máquina fora do `.gitignore` — CORRIGIDO
- **Status**: Confirmada
- **Localização**: `docs/env-report.txt` (versão do Windows, caminhos com o nome de usuário), `scripts/.transfer.tgz`, `scripts/.edge-step`, `_to_delete/`, `*.log`.
- **Impacto**: exposição de informação da máquina se o projeto for publicado no GitHub.
- **Remediação aplicada**: adicionados ao `.gitignore`. `docs/diag/`, `docs/test-results/` e `docs/last-run.log` já estavam cobertos.
- **Ação manual recomendada**: antes de enviar um .zip do projeto, apagar `docs/diag`, `docs/test-results`, `docs/last-run.log`, `docs/env-report.txt` e `_to_delete`.

### [LOW] Log diário sem teto de tamanho — CORRIGIDO
- **Localização**: `src/NovaTaskbar.App/Services/FileLogger.cs`
- **Impacto**: um laço de erro poderia encher o disco (retenção era só por data).
- **Remediação aplicada**: teto de 5 MB por dia (+ retenção de 7 dias). Logs não registram títulos de janelas nem conteúdo do usuário.

### [LOW] Caminho fixo do sistema — CORRIGIDO
- **Localização**: `Widgets/Views/SystemWidget.cs` — `C:\Windows\System32\Taskmgr.exe` fixo.
- **Remediação aplicada**: `Environment.SystemDirectory` (funciona com Windows em outra unidade).

### [LOW] Nome pessoal em comentário de código — CORRIGIDO
- `DockSettings.cs` citava o apelido do dono; neutralizado. `<Company>ElChef</Company>` no .csproj foi **mantido** (aparece nas propriedades do .exe como editor — troque pela marca comercial antes de vender, se preferir).

### [INFO] Executável sem assinatura de código
- Para distribuição (site/GitHub), assine o .exe/instalador (certificado de code signing ou MSIX pela Microsoft Store); sem isso o SmartScreen alerta o usuário.

### [INFO] Privacidade do widget do Claude (widget removido na 0.6.1 — não lê mais nada do Claude)
- Lê `~/.claude/projects/**/*.jsonl` somente para os campos de **uso de tokens** e horário; não lê, guarda nem registra o conteúdo das conversas; não envia nada pela rede; não usa login/cookies. Leitura compartilhada (não bloqueia o Claude Code).

### Controles já presentes (verificados)
- Apps fixados: só caminhos locais absolutos de .exe/.lnk/.appref-ms/pastas ou `shell:AppsFolder\<AUMID>` validado; UNC, URIs, aspas e argumentos rejeitados (`SettingsValidator.IsAcceptablePath`).
- Abertura via `ShellExecuteEx` verbo "open" sem argumentos; ações de widget com alvos fixos; configurações do Windows só `ms-settings:`.
- Fechar janelas só com `WM_CLOSE` (nunca mata processos). Sem privilégios de administrador; nada gravado em pastas do sistema ou registro.
- Configuração JSON validada, gravação atômica + backup, recuperação de arquivo corrompido.
- Objetos de instância única no namespace `Local\` (por sessão).
- Sem telemetria; a única navegação web é o clique do usuário no widget do Claude (URL fixa https).

## Caminhos de Ataque Combinados
1. Nenhum caminho crítico identificado: o app não abre portas, não baixa nem atualiza código, não tem contas. O risco residual principal é um `settings.json` alterado por malware já rodando como o usuário — mitigado pela validação (só executa caminhos locais aprovados, sem argumentos).

## Top 5 Recomendações Prioritárias
1. Assinar o executável/instalador antes de distribuir.
2. Rodar `dotnet list package --vulnerable --include-transitive` antes de cada release.
3. Apagar os artefatos locais de diagnóstico antes de compartilhar o projeto (lista acima).
4. Trocar `<Company>` pela marca comercial, se desejar.
5. Gerar build Release (sem logs Debug) para distribuição.

## Checklist de Liberação
- [x] Nenhum segredo encontrado (código, docs, scripts, artefatos de teste)
- [x] Sem backend: controle de acesso não se aplica
- [x] Nenhum segredo em configurações públicas ou builds
- [ ] Dependências checadas com `dotnet list package --vulnerable`
- [x] Logs com teto e sem dados do usuário
- [ ] Executável assinado
