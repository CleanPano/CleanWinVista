# Relatório de Auditoria de Segurança — CleanWinVista 1.0.0

**Data**: 2026-11-22
**Escopo**: código novo desde a auditoria 0.6.0 (localização `L`/`Localizer`, assistente `Setup/` — `Installer.cs`, `SetupWindow.cs`, `ShortcutWriter.cs`, `PanelStyle`, novos padrões de fábrica), `installer/CleanWinVista.nsi`, `scripts/next.ps1`, `dist/app` (pasta publicada) e `dist/CleanWinVista-Setup-1.0.0.exe`; revarredura de segredos no projeto inteiro.
**Modo**: Read-only na análise; correções aplicadas com autorização do dono do projeto (22/11/2026) e revalidadas
**Ferramentas usadas**: grep de padrões de segredo (chaves de IA, cloud, pagamentos, tokens, chaves privadas, connection strings), busca de arquivos sensíveis (`.env`, `.pem`, `.key`, `.pfx`, credentials), `strings` nos binários publicados, revisão manual do fluxo de instalação/desinstalação. gitleaks/semgrep/trivy indisponíveis.
**Limitações**: sem repositório Git (sem histórico); `dotnet list package --vulnerable` depende de rede no PC (executado no build — ver resultado no fim); instalação/desinstalação real não executada nesta auditoria; sem backend (Fases 4–5 reduzidas).

## Scorecard Executivo

| Categoria                  | Status | Críticos | High | Medium | Low |
|----------------------------|--------|----------|------|--------|-----|
| Secrets & Credentials      | ✅ OK | 0 | 0 | 0 | 0 |
| Access Control / Auth      | n/a (app local, sem contas) | 0 | 0 | 0 | 0 |
| Business Logic             | n/a (sem licença/pagamento) | 0 | 0 | 0 | 0 |
| Privacy & Dados Pessoais   | ✅ corrigido | 0 | 0 | 0 | 0 |
| Frontend / Builds          | ⚠️ | 0 | 0 | 1 | 0 |
| Supply Chain               | ✅ sem pacotes vulneráveis | 0 | 0 | 0 | 0 |
| Infra / Desktop (instalador) | ✅ corrigido | 0 | 0 | 0 | 0 |

**Veredito Final**: **GO com restrições** — falta só a assinatura de código (MEDIUM).
**Justificativa**: nenhum segredo, telemetria ou rede oculta; internet continua opt-in. O HIGH do desinstalador e os dois LOW foram corrigidos e revalidados (build 0 erro, 217 testes, assistente testado na tela, 0 PDB e 0 caminho de build no pacote).

## Achados (ordenados por severidade)

### [HIGH] Desinstalar pode apagar arquivos do usuário fora do app — CORRIGIDO
- **Status**: Confirmada (leitura do código) → corrigida
- **Localização**: `src/NovaTaskbar.App/Setup/Installer.cs` — `Install()` copia direto para a pasta escolhida; `Uninstall()` apaga **todos** os arquivos da pasta (`EnumerateFiles(folder, AllDirectories)`) e roda `rmdir /s /q` nela.
- **Evidência**: a única trava é `File.Exists(folder\NovaTaskbar.exe)`, que é verdadeira em qualquer pasta onde o app foi instalado.
- **Impacto**: perda de dados. Se a pessoa escolher em "Procurar…" uma pasta existente (ex.: `C:\Ferramentas`, `Documentos`, `Área de Trabalho`), ao desinstalar tudo dentro dela é apagado, sem lixeira.
- **Cenário de exploração**: sem atacante — erro de uso comum. Usuário instala em `C:\Users\Fulano\Desktop`; desinstala; a área de trabalho inteira some.
- **Remediação**: (1) instalar sempre numa subpasta dedicada `…\CleanWinVista` quando a pasta escolhida não for vazia/não terminar nesse nome; (2) gravar um manifesto (`install-manifest.txt`) com os arquivos copiados e, na desinstalação, apagar **só** esses arquivos e remover diretórios **vazios**; (3) trocar `rmdir /s /q` por `del` do exe + `rmdir` não recursivo.
- **Correção aplicada**: `Installer.ResolveTarget` sempre instala em `…\CleanWinVista`; `install-manifest.txt` lista os arquivos copiados; `Uninstall` apaga só os itens do manifesto que estão dentro da pasta, remove apenas pastas vazias e o cmd final usa `del` do exe + `rmdir` sem `/s`. Sem manifesto, só apaga se a pasta for a dedicada.
- **Referências**: CWE-73 (controle externo de caminho), CWE-1188 (padrão inseguro), CWE-459.

### [MEDIUM] Executável e instalador sem assinatura digital
- **Status**: Confirmada
- **Localização**: `dist/CleanWinVista-Setup-1.0.0.exe`, `NovaTaskbar.exe`
- **Impacto**: SmartScreen mostra "Editor desconhecido"; o usuário não tem como verificar se o arquivo foi adulterado no caminho (mirror, reupload).
- **Remediação**: assinar com certificado de code signing (Authenticode, com timestamp) antes de distribuir; publicar o SHA-256 do instalador nas notas de release (feito no post do GitHub).
- **Referências**: CWE-494.

### [LOW] Comando de limpeza via `cmd.exe` interpola o caminho da pasta — CORRIGIDO
- **Status**: Confirmada → corrigida (`%`, `^`, `!`, `"` recusados no caminho; `cmd /d`; sem `rmdir /s`)
- **Localização**: `Installer.Uninstall()` — `cmd /c ping … & rmdir /s /q "{folder}"`
- **Impacto**: `%VAR%` dentro do caminho é expandido pelo cmd (o caminho vem do HKCU do próprio usuário; não há elevação). Combinado com o HIGH aumenta o alcance do apagamento.
- **Remediação**: rejeitar `%`, `^`, `!` e `"` no caminho de instalação; usar só `del` do exe e `rmdir` não recursivo.
- **Referências**: CWE-78.

### [LOW] PDBs publicados revelam caminho de build com o nome do usuário do Windows — CORRIGIDO
- **Status**: Confirmada → corrigida (`DebugType=None`, `DebugSymbols=false`, `PathMap`; revalidado: 0 PDB e nenhum caminho de build nos binários. Resta só o campo Company=ElChef nas propriedades do exe, mantido por escolha na auditoria 0.6.0)
- **Localização**: `dist/app/*.pdb` e `NovaTaskbar.dll` (`C:\Users\El…\Desktop\ClaudeIA\projetos\NovaTaskbar\…`)
- **Impacto**: vazamento de informação pessoal/da máquina no instalador público; +MB sem uso.
- **Remediação**: publicar com `-p:DebugType=None -p:DebugSymbols=false` e `PathMap` (caminho neutro).
- **Referências**: CWE-200.

### [INFO] Pontos verificados sem problema
- `L.Load` só aceita os 20 códigos conhecidos (`Normalize`) e limita o JSON a 4 MB — sem path traversal pelo idioma.
- Instalação 100 % por usuário: `RequestExecutionLevel user`, HKCU, `%LOCALAPPDATA%`; recusa a pasta do Windows e caminhos de rede.
- `UninstallString`/atalhos com caminho entre aspas; `settings.json` gravado de forma atômica (`.tmp` + move).
- NSIS extrai em `$PLUGINSDIR` (pasta temporária aleatória do usuário) e apaga ao terminar.
- Novos padrões de fábrica mantêm `allowNetwork = false`; nenhuma chave/URL pessoal embutida. Endereços BTC/ETH são públicos por design.
- `.gitignore` cobre `dist/`, logs, `docs/diag`, `env-report.txt`, `_to_delete/`.
- CleanPano no instalador (novo): só o instalador oficial 1.16.1 empacotado, conferido por **SHA-256 fixo** antes de executar; abre o assistente próprio dele (UAC visível), nunca silencioso.

## Caminhos de Ataque Combinados
1. Instalador sem assinatura republicado com malware num site de terceiros → usuário confia no nome → execução com os direitos do usuário. Mitigação: assinatura + hash publicado + baixar só do GitHub oficial.
2. Instalação em pasta existente + desinstalação recursiva → perda de dados (HIGH).

## Top 5 Recomendações Prioritárias
1. ~~Corrigir o desinstalador.~~ Feito.
2. ~~Publicar sem PDB e com `PathMap`.~~ Feito.
3. Assinar instalador e exe (code signing com timestamp).
4. Publicar SHA-256 do instalador no release.
5. Rodar `dotnet list package --vulnerable` a cada release (adicionado ao `scripts/next.ps1`).

## Checklist de Liberação
- [x] Nenhum segredo no projeto, binários ou instalador
- [x] Sem telemetria; internet opt-in
- [x] Desinstalador apaga só os arquivos do app
- [x] Publicação sem PDB/caminho pessoal
- [ ] Instalador assinado (pendente: certificado de code signing)
- [x] Dependências sem CVE conhecido (`dotnet list package --vulnerable --include-transitive`: nenhum)
- [x] SHA-256 do instalador publicado nas notas de release
