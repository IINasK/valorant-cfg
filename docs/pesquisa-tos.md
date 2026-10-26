# Pesquisa: termos de serviço, anti-cheat e legalidade

Levantamento feito em 25/10/2026. **Não é aconselhamento jurídico.** As citações vêm das páginas oficiais
listadas; revalide se os termos mudarem.

## 1. Termos de Serviço da Riot ("Last Modified: December 1, 2024")
<https://www.riotgames.com/en/terms-of-service>

- §7.1(11) proíbe "unauthorized third party programs, including mods, hacks, cheats, scripts, bots, trainers".
  O item fala de programas que automatizam a interação com os serviços, interceptam ou emulam comunicação,
  ou leem a memória do jogo.
- §3.2 proíbe "modification of the Riot Services". §7.1(8) proíbe contornar "any technological measure".
- **Os ToS não falam de arquivos de configuração.** Ninguém testou juridicamente se trocar as próprias
  preferências conta como "modificação".
- Contas:
  - §1.5: "You can't share your account or Login Credentials with anyone".
  - §7.1(15)(a) proíbe "playing on another person's account".
  - Ter várias contas **não** é proibido.

## 2. Vanguard e a pasta Config
- Nenhum artigo oficial diz que editar arquivos de config é detectado ou punido.
- A própria Riot manda apagar a pasta `AppData\Local\VALORANT\Saved\Config\` como procedimento de suporte:
  <https://support.riotgames.com/en-us/valorant/support-tools/how-to-fix-error-retrieving-settings-from-server>.
- O alvo do anti-cheat é "unauthorized hardware or software that gives players an unfair advantage"
  (auto-clickers, aimbots): <https://support.riotgames.com/en-us/valorant/support-tools/addressing-cheating-in-valorant>.
- Atenção: a Riot já tratou como exploit um ganho de FOV obtido com stretched em janela (patch 9.09):
  <https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-9-09/>.

## 3. Várias contas e compartilhamento
- "Even if you own multiple accounts, as long as you are not sharing any of them, these requirements
  shouldn't affect you." Conta compartilhada = credenciais repassadas "to be played by someone who is not
  the owner". O BR está na beta, e conta compartilhada passa a exigir MFA no Competitivo.
  Fonte: <https://playvalorant.com/en-us/news/game-updates/riot-mobile-verification-beta/>.
- "sharing accounts between multiple users, purchasing accounts" é "explicitly against our terms of
  service, and will result in a ban":
  <https://playvalorant.com/en-us/news/dev/valorant-systems-health-series-smurf-detection/>.

## 4. Compartilhar configurações é um recurso oficial
- O patch 4.05 adicionou import/export de mira por código:
  <https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-4-05/>.
  Ou seja, levar as suas preferências para outra conta é um caso de uso que a própria Riot apoia.

## 5. Brasil (breve)
- No próprio PC e com as próprias contas, a questão é só contratual (ToS). O pior caso é sanção na
  conta, não crime.
- O art. 154-A do Código Penal (Lei 14.155/2021) pune quem invade "dispositivo informático de uso alheio"
  sem autorização. Instalar a ferramenta no PC de outra pessoa sem o consentimento dela é outra história.

## 6. Casos de ban (pesquisa de 25/10/2026)

- **Confirmados pela Riot:** todos envolvem ferramentas que dão vantagem ou revelam informação.
  - Patch 13.05 (01/09/2026): penalidades para "stream sniping and instalock tools"
    (<https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-13-05/>).
  - Ban wave de 15/09/2026 com cerca de 10 mil contas. Segundo GamerDoc, do anti-cheat: "Anything hitting a
    service allowing instalocking agents or revealing names is bannable regardless of who made it". Ou seja,
    a detecção é pelo endpoint chamado, não pela ferramenta
    (<https://www.strafe.com/news/read/valorant-bans-10-000-players-in-wave-targeting-stream-sniping-and-instalock-tools/>).
- **Relatos sem confirmação:**
  - O VRY (rank yoinker) teve suspensões quando revelava informação oculta
    (<https://github.com/zayKenyon/VALORANT-rank-yoinker/issues/73>).
  - O ValorantCC (cor da mira, que grava pela mesma API) tem 2 relatos de ban em 2022, na onda de junho. O
    mantenedor nega a causa, e não há confirmação da Riot
    (<https://github.com/weedeej/ValorantCC/issues/26>).
- **Nada encontrado:** nenhum relato de ban por ferramenta que só lê ou grava `Ares.PlayerSettings`
  (ValoUtils, Settings Applier) nem por editar `.ini`. Também não há declaração da Riot de que isso seja
  proibido ou permitido.

**Resumo:** nenhum ban confirmado para ferramentas só de configurações. O risco é baixo, mas não zero: o uso
da API local não é autorizado oficialmente, e ondas amplas de ban já pegaram ferramentas pequenas.

## Conclusão

| Situação | Avaliação |
|---|---|
| Trocar as **suas próprias** preferências entre as **suas próprias** contas, no seu PC | Permitido na prática, risco baixo. Não dá vantagem e não toca memória, input nem arquivos do jogo |
| Ser uma ferramenta automática (watcher) que lê e grava as preferências da conta logada no serviço de preferências da Riot, com o token do Riot Client local | Zona cinzenta, risco baixo. Não é API pública oficial e o §7.1(11) é amplo, mas é a mesma rota de ValoUtils, ValorantCC e valorant-tracker, sem ban confirmado. A rota *local* equivalente foi restrita pela Riot em 2026 ("Unable to find caller ID"); contornar essa restrição seria violação (§7.1(8)) e não é feito |
| Jogar na conta de **outra pessoa** (ou emprestar a sua) | **Viola os ToS** (§1.5, §7.1(15)(a)), com ou sem esta ferramenta |
| Usar a cfg para ganhar vantagem (FOV/stretched fora do suportado) | Fora do escopo; a Riot trata como exploit |

Não verificado oficialmente: a lista exata do que sincroniza com a nuvem e o texto dos ToS em pt-BR
(as URLs deram 404).
