@AGENTS.md

## Específico do Claude Code

- Para testar e compilar, rode `build.cmd`. Ele roda os testes e gera `dist\VALORANT CFG.exe` (se o exe estiver aberto, ele é renomeado antes da cópia). Só testes: `dotnet run --project tests -c Release`.
- **Nunca** chame `savePreference` (nem `RiotClient.PutSettings`) para testar: isso grava na conta real logada. Para investigar, use só `getPreference` (GET). Nunca imprima o `accessToken`.
- O watcher de verdade grava em `%LOCALAPPDATA%\ValorantCFG`. Nos testes, use o `Vcs(riot, dataDir, gameConfigDir)` com pastas temporárias.
- Use a skill `ponytail` nas mudanças e auditorias.
