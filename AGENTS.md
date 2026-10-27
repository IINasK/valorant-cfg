# VALORANT CFG — guia para agentes

Leva a cfg do dono (mira, sensibilidade, teclas, gráficos) para qualquer conta de VALORANT neste PC e
devolve a cfg original da conta quando o jogo fecha.

**O nome do programa é "VALORANT CFG"** (pedido do dono). É ele que aparece na janela, na bandeja, nos
avisos, no exe (`VALORANT CFG.exe`), na pasta de dados (`ValorantCFG`) e na chave de autostart.
Internamente, o código, o namespace e a pasta do repositório ainda se chamam `ValCfgSync`, nome antigo; não
exponha esse nome ao usuário.

Entrega: **um único `VALORANT CFG.exe`**, em C# WinForms sobre .NET Framework 4.8, sem instalação e sem DLLs.
- Sem argumentos, abre o menu.
- Com `--watcher`, fica minimizado na bandeja.

UI e mensagens em pt-BR. Este arquivo guarda só o que **não dá para deduzir lendo o código**.

## Fatos sobre o VALORANT que definem o desenho

- **Mira, sensibilidade e teclas ficam na nuvem, por conta** (`Ares.PlayerSettings`).
  `RiotUserSettings.ini` é só um cache que o jogo sobrescreve com a nuvem. Por isso a v1, que copiava o
  .ini, não resolvia. Nunca volte a sincronizar esse arquivo.
- **Os gráficos ficam só no PC**, em `%LOCALAPPDATA%\VALORANT\Saved\Config\<puuid>-<região>\WindowsClient\GameUserSettings.ini`.
  - A pasta só existe depois que a conta abriu o jogo neste PC.
  - O jogo grava esse arquivo **ao fechar**, então com o jogo aberto ele está desatualizado.
- **Conta logada e token vêm do Riot Client local:**
  - Lockfile `%LOCALAPPDATA%\Riot Games\Riot Client\Config\lockfile`, formato `nome:pid:porta:senha:protocolo`.
    Abra com `FileShare.ReadWrite`, porque o cliente mantém o arquivo aberto.
  - Autenticação: `Basic riot:<senha>`. HTTPS com certificado autoassinado, aceito **só** para loopback.
  - `GET https://127.0.0.1:<porta>/entitlements/v1/token` devolve
    `{accessToken, entitlements, issuer, subject, token}`. `subject` é o PUUID.
  - Sem login, responde **404**: significa "sem login", não bug.
- **A cfg é lida e gravada no serviço de preferências da Riot**, o mesmo que o jogo usa:
  - `GET https://player-preferences-usw2.pp.sgp.pvp.net/playerPref/v3/getPreference/Ares.PlayerSettings`,
    com `Bearer <accessToken>`, devolve `{type, data, modified}`.
  - `data` é **deflate cru (sem zlib) + base64** de um json com `actionMappings`, `floatSettings`,
    `stringSettings` etc.
  - `PUT .../playerPref/v3/savePreference` com `{type:"Ares.PlayerSettings", data:<deflate+base64>}`.
  - `usw2` foi verificado com conta BR em 25/10/2026; `euc1` devolveu os mesmos dados (parece global). O
    PUT **não foi testado** (gravaria na conta real).
- **Por que não a rota local** (`/player-preferences/v1/data-json/...` no Riot Client): desde 2026 ela
  responde 500 *"Unable to find caller ID for session"* para terceiros, porque só atende sessões de
  produto da Riot (o próprio jogo).
  - **Nunca** tente "consertar" isso forjando caller ID ou usando as rotas `...DebugCallerIdMockId`: isso
    é contornar medida técnica (ToS §7.1(8)).
- **O jogo só lê a cfg da nuvem ao iniciar.** Uma gravação com o jogo carregado só vale na próxima
  abertura. O ValoUtils exige o jogo fechado ao carregar um perfil.
- Por isso o `Vcs.Step` (polling de 2s):
  - **Aplica quando a conta loga no Riot Client**, ainda com o jogo fechado.
  - **Restaura quando o jogo fecha**, com o Riot Client ainda logado, porque depois do logout a API some e
    não dá mais para restaurar.
  - Se o jogo reabrir no mesmo login, reaplica ao detectar o processo. É melhor esforço, porque o jogo
    pode já ter lido a nuvem.
- `VALORANT.exe` é só o lançador; o jogo é `VALORANT-Win64-Shipping.exe`.
- `C:\ProgramData\Riot Games\RiotClientInstalls.json` (`associated_client`) diz onde o jogo está
  instalado. É a base do "Detectar".

## Invariantes da sessão (não quebre)

1. O original da conta vai para `pending\<puuid>.json` **antes** de aplicar (write-ahead).
2. Uma pendência existente **nunca é sobrescrita**: depois de um crash, a cfg na nuvem já é a do dono.
3. A pendência só é apagada **depois** de uma restauração bem-sucedida.
4. Só restaura com o **jogo fechado** e com **a mesma conta logada**.
   - Desligamento forçado: no próximo login a pendência continua e a cfg do dono segue aplicada. A
     original volta quando o jogo fechar.
   - Por decisão do dono, a cfg é aplicada no login, e não no início do jogo. Por isso não existe
     "restaurar no boot".
5. Sem original, nada é aplicado. Os gráficos só são aplicados se o pending tem `IniPath`.
6. Ficam salvas **várias cfgs com nome** (`profiles\<nome>.json`), e o watcher aplica só a **cfg em uso**
   (`config.json` → `ActiveProfile`).
   - **Conta dona** (a de onde a cfg foi salva, `SourcePuuid`): recebe a cfg também, mas o pending é criado
     **sem `Cloud`**, e ao fechar o jogo nada é restaurado.
     - Ela é a conta do dono, então os ajustes feitos no jogo ficam nela.
     - Antes ela era ignorada, e isso quebrava ter várias cfgs da mesma conta: escolher "Vitão" não mudava
       nada, porque Vitão e NasK eram da mesma conta (bug relatado em 25/10/2026). Não volte a ignorá-la.
   - Trocar a cfg em uso com o Riot Client logado e o jogo fechado aplica na hora (`WatchState.Applied`).
9. **Mudanças feitas dentro do jogo** (pedido do dono):
   - Ao fechar o jogo, **antes** de restaurar, `CaptureSessionChanges` lê a conta e compara com a cfg
     aplicada (`SameSettings`).
     - A comparação é item a item: float/int/bool/string settings e teclas. A ordem não importa, ruído de
       float além de 5 casas some, e campos internos do jogo são ignorados.
   - Se mudou, grava `changes\<puuid>.json` (settings + `.ini` recém-gravado pelo jogo) e avisa na bandeja.
   - O menu mostra no cartão "Você mudou a cfg no jogo" com **[Manter do jogo]** (`KeepSessionChanges`: vira
     a cfg) e **[Manter original]** (`DiscardSessionChanges`: descarta; a cfg volta a valer na próxima
     abertura do jogo, inclusive na conta dona).
   - A pergunta fica até ser respondida. Uma sessão nova com mudanças substitui a anterior da mesma conta.
   - Limite: mudar **só gráficos** não é detectado (o `.ini` muda sozinho a cada fechamento). Se o usuário
     escolher "manter do jogo" por outro motivo, os gráficos vão junto.
   - Salvar uma cfg no meio de uma sessão mantém a dona da cfg em uso.
   - Excluir a cfg em uso não afeta as pendências: o original continua sendo restaurado.
   - O `profile.json` das versões até a 2.0 é migrado para a cfg "Minha cfg" (`MigrateLegacyProfile`).
7. Toda escrita de JSON é atômica (`Json.WriteFile`: tmp + `File.Replace`).

8. "Aplicada tarde": se o watcher encontrou o jogo **já aberto** (nunca o viu fechado), aplica mesmo assim,
   mas grava `Late: true` no pending.
   - O jogo provavelmente já leu a nuvem, e o cartão avisa "VALORANT está rodando sem a cfg".
   - Ao fechar, restaura; ao reabrir, o jogo é detectado em até 2s e a cfg chega a tempo.
   - Atualizar `Late`/`Profile` num pending existente é permitido; tocar em `Cloud`/`Ini` não.

`tests/` cobre esses pontos com um Riot Client falso (84 casos). Mudou `Core.cs`? Rode `build.cmd`.

## Interface (desenho do dono, 25/10/2026)

- Layout: logo + título "VALORANT CFG" + botão ☰, cartão de status e a lista de cfgs. **Não existe botão
  "Usar cfg"** (o dono tirou): confirmar uma cfg no ✓ já liga o watcher.
  - **Título = logo oficial "VALORANT" (PNG) + "CFG"** (`BrandTitle`).
    - `src/brand.png` é o recorte reduzido (834×120) do PNG de 5000 px que o dono baixou, **embutido no exe**
      como recurso (`EmbeddedResource`, carregado em `LoadPng`).
    - O "CFG" é desenhado com as letras exatamente da altura das do logo.
  - **Fonte VALORANT** (fan font "VALORANT", instalada só no Windows do dono): no "CFG" do título e no selo
    "EM USO".
    - A fonte só tem maiúsculas e não tem acentos (daí o `Caps()`).
    - **Não embuta a fonte no exe**: ela não declara autor nem licença, e as permissões internas dela só
      permitem "visualizar e imprimir". Sem ela instalada (o caso dos colegas), `Display()` cai em `UiBold`.
  - **Resto da interface em Bahnschrift** (`Ui`/`UiBold`): a DIN que vem no Windows 10/11, parecida com a DIN
    Next dos textos do VALORANT (o dono pediu "essa fonte ou parecido"). Sem ela, Segoe UI. Não use outra
    família solta no código: use `Ui`, `UiBold` ou `Display`.
  - **Barra superior sem ícone e sem texto** (pedido do dono), via `SetWindowThemeAttribute`
    (NODRAWCAPTION/NODRAWICON), e pintada de branco como o app (`DWMWA_CAPTION_COLOR`, só Windows 11). O `Text` continua "VALORANT CFG" para a barra de tarefas e o Alt+Tab: não
    zere o `Text`.
  - Ao lado do título e no **ícone do exe** (`src/app.ico`: exe, barra de tarefas, Alt+Tab, bandeja) fica a
    **prancheta vermelha** (`assets/icone-prancheta.png`, a atual, com um check; `-v1` e `-v2` são as anteriores; no título, `AppLogo` com
    `src/logo.png` embutido).
    Escolha do dono, que **não usa o símbolo V do VALORANT**: não o reintroduza.
  - `assets/` guarda os originais que o dono mandou (logo, símbolo, prancheta, ícones Material de exportar e
    importar). `src/brand.png`, `src/logo.png` e `src/app.ico` são recortes/reduções gerados deles.
  - **Escolher a cfg em dois passos (desenho do dono):**
    - clicar no nome faz um check vermelho deslizar da esquerda (~200ms, ease-out), empurrando o nome, e a
      fonte cresce de 13 para 15;
    - clicar no check confirma: o check se recolhe, o nome volta à origem com a fonte grande e aparece
      "em uso";
    - clicar de novo no mesmo nome, em outro, ou em qualquer lugar do app fora do ✓, recolhe sem escolher
      (`PreFilterMessage`);
    - **clique duplo no nome abre a edição do nome** (o único jeito; não há ✏️). O 1º clique abre o ✓, que desliza para baixo do
      mouse: um clique no ✓ dentro do tempo de clique duplo do Windows conta como o 2º clique, não confirma.
    - **Na cfg em uso, o mesmo quadrado vem com X** (a fonte não muda e o "EM USO" acompanha o nome):
      confirmar tira a cfg de uso (`ActiveProfile = null`). Sem cfg em uso, o watcher continua ligado e
      `Vcs.Step` devolve na hora a original da conta que já tinha recebido a cfg (com o jogo fechado), para
      o jogo abrir como era. Com o jogo aberto, o nome fica travado como na troca.
    - Código: `ToggleReveal` / `ConfirmReveal` / `CancelReveal` / `Animate`.
  - **Dois modos separados, de propósito** (o dono viu usuário clicar em "Salvar atual" achando que só
    renomeava, e perder a cfg). Não volte a juntar renomear com ações que trocam o conteúdo.
    - **Renomear** (clique duplo no nome): caixa de texto + [Salvar nome] + X. Enter/"Salvar nome" salva só
      o nome; Esc, X ou clicar fora cancela. Os ícones saem deslizando e voltam com a animação inversa, sem
      recriar a lista.
    - **⚙️** (`ToggleTools`): exportar, importar (um arquivo **nesta** cfg) e [Salvar CFG] (a cfg da conta
      logada **nesta** cfg) saem de trás da engrenagem; a 🗑️ some enquanto isso. Importar e Salvar CFG
      sobrescrevem e pedem confirmação no cartão. Engrenagem de novo, clicar fora ou usar uma ação recolhe.
    - Linha normal: 🗑️ e ⚙️ só.
    - Botões de texto da linha são `RowButton` (Label, não Button: Button pega o foco e o LostFocus da caixa
      de nome cancelaria antes do clique).
  - A última linha é "Nova CFG": digitar o nome e apertar **Enter** salva a cfg da conta logada. O único
    ícone dela é importar. **Sem disquete** (o dono tirou).
  - A lista tem sublinhado vermelho e barra de rolagem fina vermelha (`ThinScrollBar`).
  - `RefreshList` recria todas as linhas: congela o desenho (WM_SETREDRAW) enquanto isso, senão a lista pisca.
    Prefira animar a linha existente a recriar a lista.
  - Cada linha é um `SmoothPanel` (WS_EX_COMPOSITED): sem isso, o que desliza nas animações rasga. Itens que
    "saem de trás" de um ícone andam dentro de um painel que corta na borda dele (menu da ⚙️).
- **O cartão é um guia do próximo passo** (`Vcs.Status`, atualizado a cada 3s):
  - estados, em ordem: crie sua primeira cfg → nenhuma cfg em uso → inicie o Riot Client
    → ⚠ VALORANT rodando sem a cfg → jogando com a cfg → cfg carregada (inicie o VALORANT) → pronto/desligado;
  - texto novo de estado vai em `Vcs.Status` (testado), não na UI.
- **Nada de popup (MessageBox), pedido do dono.** Tudo passa pelo cartão:
  - `Flash(título, detalhe, tom)`: aviso passageiro de sucesso ou erro (~4s; erros 7s), depois volta o status ao vivo.
  - `Ask(título, detalhe, "Sim-texto", ação)`: pergunta com botões [ação] [Cancelar] no próprio cartão
    (substituir cfg, excluir, parar com restaurações pendentes).
  - Exceções das ações caem em `Application.ThreadException` → `MainForm.ShowError` → `Flash` vermelho.
  - Só as janelas de arquivo do Windows (abrir/salvar `.json`, escolher o exe) continuam como diálogo.
- **Com o jogo aberto, trocar de cfg é bloqueado** (pedido do dono): os nomes ficam acinzentados e não
  clicáveis, e a 🗑️ da cfg em uso também (excluir = trocar).
  - Renomear, exportar, excluir outras cfgs e salvar/importar continuam liberados. Guardar ajustes feitos no
    jogo: a pergunta ao fechar o jogo, ou ⚙️ → "Salvar CFG".
  - `ApplyLock` só repinta; não recria a lista, para não apagar o que estiver sendo digitado.
- **Tudo que é extra vai no ☰**, que é uma **página** (não popup, pedido do dono): desliza por cima da lista,
  o ☰ vira X para voltar e o **cartão continua à vista** (avisos e perguntas, como "Desligar mesmo assim?",
  aparecem nele). Linhas: jogo (Detectar / Procurar...), ligado (chave), iniciar com o Windows (chave) e pasta
  de dados (Abrir). As chaves (`ToggleSwitch`) só avisam o clique; o estado mostrado é sempre o real
  (`RefreshSettings`, também a cada 3s), porque desligar pode pedir confirmação e a bandeja também desliga.
  A página anda com SWP_NOCOPYBITS: sem isso o Windows repete pedaços dos pixels antigos no meio do slide. Excluir **não** fica no ☰ (o dono tirou; é a 🗑️ da linha). Não há botão Salvar: tudo vale na hora.
  Não adicione botões soltos fora desse padrão.
- **Ícones padronizados (o dono cobrou isso):**
  - ☰ (E700), 🗑️ (E74D), ⚙️ (E713) e X (E711) vêm da fonte `Segoe MDL2 Assets`, pintados com antisserrilhado em cinza
    (`IconLabel`). Sem ClearType, que deixa franjas coloridas.
  - Exportar e importar: desenhos dos ícones Material "Export"/"Import" (Apache 2.0) que o dono escolheu,
    refeitos em vetor no `BoxArrowIcon` (grade 48×48 deles, traço fino dos outros). A caixa é alinhada ao
    pixel (`PixelOffsetMode.Half` + bordas em .5); sem isso as retas viram 2px cinza.
  - Ícones da fonte são centralizados pelo desenho do glifo (`GraphicsPath`), não pela caixa da fonte.
  - Ícone novo: use um desses dois caminhos, com o mesmo tamanho (36×34) e o mesmo traço.
- `AutoScaleMode.None` + `S()`: as linhas são recriadas em tempo de execução e o autoescala do WinForms
  não as alcançaria, então todo tamanho em pixel passa por `S()`.
- **Abas CFGS / MIRAS** no cabeçalho (entre o título e o ☰), texto em `UiBold` (a fonte VALORANT continua só
  no "CFG" e no "EM USO"), sublinhado vermelho que desliza. Clicar numa aba fecha a página do ☰. Janela
  520×520.

## Aba Miras (`Crosshair.cs` + `MainForm.Crosshairs.cs`)

- **Não injeta nada no jogo** (pedido do dono): guarda códigos de perfil de mira, mostra a mira e copia o
  código para o usuário colar no VALORANT (Configurações → Mira → importar código de perfil). Não use a nuvem
  nem a memória do jogo para isso.
- Formato do código, sem documentação oficial (engenharia reversa da comunidade; referência:
  github.com/spartytech/Crosshair-Bridge, `src/lib/valorantCode.ts` e `defaults.ts`):
  - começa com número + ";" e segue em pares chave;valor. `P`, `A` e `S` abrem as seções primária, ADS e
    luneta; antes delas é geral. O jogo **só escreve o que difere do padrão**: chave ausente = padrão.
  - primária: `c` cor (0 branco, 1 verde, 2 verde-amarelado, 3 amarelo-esverdeado, 4 amarelo, 5 ciano,
    6 rosa, 7 vermelho, 8 = `u` RRGGBB[AA]); `h` contorno (1), `t` espessura (1), `o` opacidade (0.5); `d`
    ponto (0), `z` tamanho (2), `a` opacidade (1); linhas internas `0b` (1), `0t` (2), `0l` (6), `0g` liga o
    vertical separado `0v`, `0o` offset (3), `0a` (0.8); externas `1b` (1), `1t` (2), `1l` (2), `1o` (10),
    `1a` (0.35). Os erros de movimento/disparo (`0m`, `0f`...) existem mas não são desenhados.
  - luneta (`S`, armas de longo alcance; chaves tiradas de códigos reais de pros): `d` mostra o ponto (1), `c`
    cor (7 = vermelho), `t` cor própria quando `c` = 8 (na primária é `u`), `s` espessura (1, decimal), `o`
    opacidade (0.75). A seção `A` (ADS) não é desenhada.
- **Guarda o código como veio** (só sem espaços nas pontas) e exporta exatamente ele: chaves que o app não
  entende (ADS, luneta, futuras) não se perdem.
- Prévia: **dois quadrados simultâneos, PRIMÁRIA / LUNETA**, sobre um cenário de Ascent da página oficial
  de mapas da Riot (`src/crosshair-scene.jpg`, embutido no exe). Nome do perfil fica abaixo.
  Mira em **pixels do jogo em 1080p** (não passa por `S()`), tamanho fixo, sem zoom por clique.
  Na luneta, o diâmetro do ponto (4 px por unidade de espessura) e o desenho da luneta são
  estimativas. A linha começa a `offset` px do centro; espessura ímpar põe o centro no meio de um
  pixel; contorno = borda preta em volta de cada parte (sem escurecer por baixo de linha translúcida).
  É simulação: não promete ser idêntica ao jogo.
- Salvas em `crosshairs.json` na pasta de dados (`CrosshairStore`). **Primeiro o nome**, em "Nova mira" +
  Enter ou engrenagem (`Create`); depois o campo da engrenagem recebe o código (`Update`). Perfis sem código
  persistem; os códigos antigos continuam intactos. Nome vazio/repetido e código inválido são recusados no cartão.
- Um clique no nome atualiza as duas prévias e revela um quadrado vermelho à esquerda com **duas folhas**;
  clicar nele copia o código. Reutiliza `CheckButton` / `ToggleReveal` / `CancelReveal` / `Animate` das CFGs,
  com modo de copiar; não ativa cfg nem watcher. Clicar fora recolhe. A engrenagem abre o editor de código
  com prévia ao digitar, Salvar/Enter para gravar, X/Esc/clique fora para cancelar (`tools` compartilhado).
- **Atalho para os dados (CFGS e MIRAS):** ícone de pasta E8B7 no cabeçalho, ao lado do ☰, e **Ctrl+Shift+O**.
  Ambos e o Abrir do ☰ usam `OpenDataFolder()` com `vcs.DataDir` (respeita a pasta legada).

## Distribuição

- **O dono manda só o `VALORANT CFG.exe` para os colegas.** Tudo que o app precisa tem que estar dentro do
  exe (recursos embutidos) ou existir em todo Windows 10/11.
  - Nada de arquivo ao lado do exe, DLL ou fonte que precise ser instalada.
  - Os dados de cada um ficam no `%LOCALAPPDATA%\ValorantCFG` de cada PC.
- O logo "VALORANT" é marca registrada da Riot. A política de fan content da Riot ("Legal Jibber Jabber")
  tolera uso **gratuito e não comercial**: não venda o app nem cobre por ele.
- Exe sem assinatura: o SmartScreen avisa na primeira execução em cada PC (ver README).

## Decisões de plataforma

- **net48, e não .NET 8:** já vem no Windows 10/11, então o exe tem ~40 KB e roda sem instalar runtime.
  - O preço é ficar sem System.Text.Json. O JSON usa `JavaScriptSerializer` (System.Web.Extensions, já
    no Framework), que preserva floats como `decimal` (0.25999999046325686 volta igual; há teste).
  - **Não adicione pacote NuGet de runtime:** quebraria o "arquivo único".
- **Requisito: nenhuma janela de console pode piscar.** O exe é `WinExe`, então nunca cria console. Não
  volte para PowerShell ou `.cmd` em segundo plano: no Windows 11 o `-WindowStyle Hidden` pisca a janela
  (medido em 25/10/2026).
- Controle do watcher sem matar processo:
  - mutex `Local\ValCfgSyncWatcher` = "está rodando" e garante uma instância só;
  - evento `Local\ValCfgSyncStop` = pedido de parada (o watcher termina o ciclo e sai).
- **Como o watcher é ligado (decisão do dono):**
  - ao confirmar uma cfg no ✓, ao abrir o app (depois de a janela aparecer, em `Shown`) e por ☰ → Ligar;
  - **com o Windows, minimizado na bandeja**, se a opção estiver marcada (chave `HKCU\...\Run`,
    `"exe" --watcher`). Ele fica parado e só age quando uma conta loga no Riot Client.
  - **Desligar é lembrado** (`config.json` → `Enabled: false`, por ☰ ou bandeja → Desligar): abrir o app e o
    início com o Windows não religam. Confirmar uma cfg ou ☰ → Ligar religam.
  - Na bandeja: duplo clique abre o menu; o menu de contexto tem Abrir/Desligar. Um balão avisa quando
    aplica ou restaura (`Vcs.Notify`).
  - Não trave o construtor do menu (por exemplo, esperando o watcher subir): se a janela demora a aparecer,
    o Windows a abre atrás das outras.
  - O dono **rejeitou** o atalho "VALORANT (minha cfg)" que abria o jogo. Não traga de volta.
  - Sem admin, não existe jeito limpo de *disparar* o app quando o Riot Client abre. WMI, auditoria de
    processos e IFEO são técnicas de malware: **não use**. Por isso o watcher fica residente.
- O menu regrava a chave `Run` ao abrir, caso o exe tenha mudado de pasta.
- Dados em `%LOCALAPPDATA%\ValorantCFG` (config, profiles, pending, log), para o exe poder morar em
  qualquer pasta.
  - A pasta antiga `%LOCALAPPDATA%\ValCfgSync` é movida para a nova na primeira execução; se não der, o app
    continua usando a antiga.
  - O valor `ValCfgSync` da chave `Run` é trocado por `VALORANT CFG`.
  - Não use `%LOCALAPPDATA%\VALORANT CFG`: confunde com a pasta do próprio jogo (`%LOCALAPPDATA%\VALORANT`).
- `app.manifest`: `asInvoker` (nunca pedir admin) e `dpiAware`.
- O exe não é assinado: o SmartScreen avisa na primeira execução de um exe baixado.

## Limites conhecidos (aceitos)

- Uma conta que logou e não abriu o jogo fica com a cfg do dono na nuvem até o próximo fechamento do jogo
  neste PC. O mesmo vale para PC desligado com o jogo aberto. Se nesse meio-tempo a conta for usada em outro
  PC, lá ela aparece com a cfg do dono.
- Ajustes feitos durante a sessão numa conta de terceiro se perdem na restauração, a menos que o dono
  clique em "Salvar cfg atual".
- Com conta real (25/10/2026) só foi testada a **leitura**: login, token, download e decodificação da
  cfg, pasta de gráficos e geração do perfil. A gravação e o ciclo completo ficam com o checklist do README.

## Limites legais e de escopo (ver `docs/pesquisa-tos.md`)

- Rede permitida: **exatamente** três chamadas.
  - `127.0.0.1/entitlements/v1/token`;
  - `getPreference` e `savePreference` de `Ares.PlayerSettings` no serviço de preferências.
  - Essa rota remota não é API pública oficial: é zona cinzenta, e o dono aceitou ciente. Ver
    `docs/pesquisa-tos.md`.
- **Nunca:**
  - endpoints de pregame/coregame/party (seleção de agente, dados de jogadores). A Riot detecta pelo
    endpoint: "Anything hitting a service allowing instalocking agents or revealing names is bannable
    regardless of who made it" (GamerDoc, anti-cheat, 15/09/2026);
  - memória do jogo, injeção, automação de input, arquivos em `C:\Riot Games`;
  - forjar identidade (caller ID, sessão de produto);
  - guardar login, senha ou token de ninguém. O token é pedido ao Riot Client a cada uso e nunca vai para
    o disco.
- Não adicionar nada que dê vantagem (FOV ou stretched via .ini, por exemplo).
- O uso legítimo é com contas **do próprio usuário**. Jogar na conta de outra pessoa viola os ToS, com
  ou sem a ferramenta. Mantenha esse aviso no README.

## Convenções

- `Core.cs`: regras (sessão, perfil, `Step`), testáveis via `IRiot`.
- `Platform.cs`: Windows e Riot (token local, serviço de preferências, processo, autostart, watcher na bandeja).
- `MainForm.cs`: só UI. Erros para o usuário são `VcsException`, com mensagem amigável que o menu mostra
  como está.
- Estilo "ponytail": a menor mudança completa, sem abstração especulativa. Comente só o porquê.
