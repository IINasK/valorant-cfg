# VALORANT CFG

Usa a **sua** cfg do VALORANT (mira, sensibilidade, teclas, gráficos) em qualquer conta sua neste PC.
Quando o jogo fecha, a cfg original daquela conta volta.

É **um arquivo só**: `VALORANT CFG.exe`. Não tem instalação, e pode ficar em qualquer pasta.

## Como usar

1. Abra o Riot Client logado na conta que tem a cfg que você quer guardar.
2. Abra o `VALORANT CFG.exe`.
   - Na primeira vez o Windows pode mostrar "O Windows protegeu o computador". Clique em
     *Mais informações → Executar assim mesmo*. Isso acontece porque o exe não é assinado.
3. Digite um nome em **Nova CFG** (por exemplo, "NasK") e aperte **Enter**. A cfg da conta logada é salva na
   lista. Repita com outras contas para ter várias cfgs.
4. Clique no nome da cfg que quer usar e depois no **✓** vermelho que aparece. Ela fica marcada como
   **EM USO**, e o app já fica ligado (ícone perto do relógio).
   - Para parar de usar: clique no nome da cfg em uso e depois no **X** vermelho. O VALORANT volta a abrir
     com a cfg original de cada conta (quem já tinha recebido a sua volta à original na hora).
5. Abrir o app também liga. Para ligar sozinho com o Windows, ligue no **☰** a chave **Iniciar com o Windows
   (minimizado na bandeja)**. Ele fica quieto e só age quando uma conta loga no Riot Client.

Para mandar para os amigos, basta o `VALORANT CFG.exe`: o logo já vem dentro dele. O "CFG" e o "EM USO"
usam a fonte "VALORANT" se ela estiver instalada; sem ela, usam a fonte DIN do Windows.

Projeto de fã, gratuito e sem fins lucrativos. VALORANT e o logo VALORANT são marcas da Riot Games; este app não
é endossado pela Riot.

O **cartão no topo diz o que fazer agora**:
- "Inicie o Riot Client";
- "Nenhuma cfg em uso" (o VALORANT abre com a cfg original de cada conta);
- "Cfg carregada — inicie o VALORANT";
- "⚠ VALORANT está rodando sem a cfg" (feche e abra o jogo de novo);
- "Jogando com a cfg …".

Enquanto o app estiver ativo:
- Quando uma conta loga no Riot Client, a cfg em uso é aplicada antes de o jogo abrir. Trocar de cfg com o jogo
  fechado aplica na hora.
- Quando o VALORANT fecha, a original da conta volta.
- **Exceção, a sua própria conta** (a de onde a cfg foi salva): ela recebe a cfg escolhida, mas a "original" não
  é restaurada ao fechar, porque já é a sua conta.
- **Mudou algo no jogo?** (sensibilidade, teclas, mira...) Ao fechar o jogo, o cartão pergunta:
  - **Manter do jogo**: as mudanças passam a fazer parte da cfg usada;
  - **Manter original**: descarta as mudanças; a cfg continua como era antes de abrir o jogo.
- Se o PC desligar com o jogo aberto, a original fica guardada e volta na próxima vez que você fechar o jogo
  nessa conta.
- Um aviso rápido aparece perto do relógio cada vez que a cfg é aplicada ou restaurada.

## Na lista de cfgs

- Com o VALORANT aberto, os nomes ficam **acinzentados**: não dá para trocar de cfg (nem excluir a cfg em uso)
  até fechar o jogo.
- **Dois cliques no nome** renomeiam: **Salvar nome** (ou Enter) salva só o nome; o **X** (ou Esc) cancela.
- **🗑️** exclui a cfg salva. Não mexe em nenhuma conta.
- **⚙️** abre as ações da cfg:
  - **Exportar** (caixa com seta saindo): salva a cfg num arquivo `.json`;
  - **Importar** (caixa com seta entrando): troca o conteúdo dessa cfg pelo de um `.json` (pede confirmação);
  - **Salvar CFG**: troca o conteúdo dessa cfg pela cfg da conta logada agora (pede confirmação).
- **Importar** em "Nova CFG" cria uma cfg nova a partir de um `.json`. Usa o nome digitado; sem nome, usa o
  do arquivo.

**☰** abre a página de configurações (o **X** volta para a lista): jogo (**Detectar** ou **Procurar...** o
executável), **VALORANT CFG ligado**, **Iniciar com o Windows** e a pasta de dados
(`%LOCALAPPDATA%\ValorantCFG`, botão **Abrir**).

**Desligar:** ☰ → desligue a chave **VALORANT CFG ligado**, ou botão direito no ícone da bandeja → Desligar.
Fica desligado até você confirmar uma cfg ou ligar a chave de novo. Para remover tudo: desligue, desligue
**Iniciar com o Windows** no ☰ e apague o exe.

## Aba Miras

Guarda códigos de mira para você colar no VALORANT. O app **não mexe na mira do jogo**: ele mostra como a mira
fica e copia o código.

- **Criar:** digite o nome em **Nova mira** e aperte Enter ou clique na engrenagem. No campo que abrir,
  cole o código exportado do VALORANT e clique em **Salvar** (ou Enter). A engrenagem permite editar o código depois.
- **Usar no jogo:** clique uma vez no nome e depois no botão vermelho de **copiar** (duas folhas) à esquerda.
  No VALORANT: Configurações → Mira → importar código de perfil → Ctrl+V.
- **Prévia:** dois quadrados mostram **PRIMÁRIA** e **LUNETA** ao mesmo tempo, sobre um cenário de Ascent.
  A mira normal usa pixels de 1080p e tem tamanho fixo; a luneta é uma aproximação.
  A mira de ADS segue preservada no código, mas não aparece na prévia.
- Cenário de Ascent: imagem da Riot Games, disponível na [página oficial de mapas](https://playvalorant.com/en-us/maps/),
  embutida no executável para funcionar sem internet.
- **Arquivos locais:** clique no ícone de pasta ao lado do ☰ ou pressione **Ctrl+Shift+O**, em qualquer aba,
  para abrir a pasta de dados. As CFGs ficam em `profiles` e as miras em `crosshairs.json`.

## Importante

- Use com **contas suas**. Jogar na conta de outra pessoa viola os termos da Riot, com ou sem esta
  ferramenta. Detalhes em [docs/pesquisa-tos.md](docs/pesquisa-tos.md).
- A ferramenta não toca no jogo. Só mexe nas preferências da conta logada, pelo mesmo serviço da Riot que
  o menu do jogo usa, e no `GameUserSettings.ini` da conta.
  - Não pede nem guarda senha: usa o login que já está no Riot Client.
  - Esse serviço não é uma API pública oficial da Riot (zona cinzenta dos termos; ver a pesquisa).
- Os gráficos são gravados pelo jogo ao fechar. Para incluí-los ao salvar a cfg, salve com o jogo fechado.

## Compilar

`build.cmd`: roda os testes e gera `dist\VALORANT CFG.exe`. Precisa do .NET SDK só para compilar; para
rodar, não precisa de nada.

## Checklist de teste manual (precisa de duas contas)

1. Antes de ativar, anote a sensibilidade da conta B. Na conta A: salve em "Nova CFG", clique no nome dela e
   depois no ✓.
2. Logue a B no Riot Client. O cartão deve mostrar **"Cfg carregada"**. Abra o jogo: a sensibilidade da A
   deve aparecer, e o cartão muda para **"Jogando com a cfg"**.
3. Feche o jogo. Em até 2s a original volta (aviso na bandeja). Desligue no ☰ e abra o jogo: a B está como
   antes.
4. Com o app ativo, abra o jogo na B e mate o jogo e o Riot Client pelo Gerenciador de Tarefas. Logue a B de
   novo, abra e feche o jogo: a original volta.
