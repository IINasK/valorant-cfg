// Autoteste da lógica de sessão e do ciclo do watcher, com o Riot Client simulado.
// Rodar: dotnet run --project tests
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ValCfgSync;

var tmp = Path.Combine(Path.GetTempPath(), "vcs-test-" + Guid.NewGuid());
var riot = new FakeRiot();
var vcs = new Vcs(riot, Path.Combine(tmp, "data"), Path.Combine(tmp, "config"));
var failed = 0;

void Assert(bool cond, string name)
{
    Console.ForegroundColor = cond ? ConsoleColor.Gray : ConsoleColor.Red;
    Console.WriteLine((cond ? "ok   " : "FAIL ") + name);
    Console.ResetColor();
    if (!cond) failed++;
}
string IniPath(string who) => Path.Combine(tmp, "config", who + "-br", @"WindowsClient\GameUserSettings.ini");
void WriteIni(string who, string text) { Directory.CreateDirectory(Path.GetDirectoryName(IniPath(who))!); File.WriteAllText(IniPath(who), text); }
string Ini(string who) => File.ReadAllText(IniPath(who));
double PendingSens(string who) => FakeRiot.SensOf(Json.ReadFile(vcs.PendingFile(who))!["Cloud"]);
bool HasPending(string who) => File.Exists(vcs.PendingFile(who));

try
{
    riot.Cloud["amigo"] = FakeRiot.NewCloud(0.9m);
    riot.Cloud["eu"] = FakeRiot.NewCloud(0.38m);
    WriteIni("amigo", "graficos-amigo");
    WriteIni("eu", "graficos-eu");

    riot.Current = "eu";
    vcs.SaveProfileFromCurrent("Minha");
    Assert(vcs.Profile() != null && vcs.ActiveProfile == "Minha", "primeira cfg salva vira a cfg em uso");

    var notes = new List<string>();
    vcs.Notify += notes.Add;
    riot.Current = "amigo";
    vcs.StartSession("amigo");
    Assert(riot.Sens("amigo") == 0.38, "sessão aplica minha sensibilidade");
    Assert(notes.Count == 1, "aplicar avisa o usuário (balão da bandeja)");
    Assert(Ini("amigo") == "graficos-eu", "sessão aplica meus gráficos");
    Assert(HasPending("amigo"), "original guardado em pending");

    vcs.StartSession("amigo");   // watcher reiniciou com a sessão ativa
    Assert(PendingSens("amigo") == 0.9, "pending NÃO é sobrescrito pela minha cfg");

    riot.PutFails = true;
    try { vcs.RestoreSession("amigo"); } catch { }
    Assert(HasPending("amigo"), "falha na restauração mantém pending");
    riot.PutFails = false;

    vcs.RestoreSession("amigo");
    Assert(riot.Sens("amigo") == 0.9, "restaura a sensibilidade original");
    Assert(Ini("amigo") == "graficos-amigo", "restaura os gráficos originais");
    Assert(!HasPending("amigo"), "pending apagado após restaurar");

    vcs.StartSession("amigo");
    vcs.SaveProfileFromCurrent("Ajustada");
    Assert(vcs.Profile("Ajustada")!.Str("SourcePuuid") == "eu", "salvar durante sessão mantém a dona da cfg");
    Assert(vcs.ActiveProfile == "Minha", "salvar outra cfg não troca a cfg em uso");
    vcs.RestoreSession("amigo");
    vcs.DeleteProfile("Ajustada");

    // conta sem pasta de config neste PC: gráficos não são aplicados (não haveria o que devolver)
    riot.Current = "novo";
    riot.Cloud["novo"] = FakeRiot.NewCloud(1.0m);
    vcs.StartSession("novo");
    Assert(riot.Sens("novo") == 0.38, "conta nova recebe minha sensibilidade");
    Directory.CreateDirectory(Path.GetDirectoryName(IniPath("novo"))!);
    vcs.StartSession("novo");   // reinício com a pasta já criada
    Assert(!File.Exists(IniPath("novo")), "sem original de gráficos, não escreve .ini");
    vcs.RestoreSession("novo");
    Assert(riot.Sens("novo") == 1.0, "conta nova volta ao original");

    // conta dona: recebe a cfg (para alternar entre várias cfgs da mesma conta) e nada é restaurado depois
    riot.Current = "eu";
    riot.Cloud["eu"] = FakeRiot.NewCloud(0.6m);   // o dono ajustou no jogo
    vcs.StartSession("eu");
    Assert(riot.Sens("eu") == 0.38, "conta dona recebe a cfg escolhida");
    vcs.RestoreSession("eu");
    Assert(riot.Sens("eu") == 0.38 && !HasPending("eu"), "conta dona: nada é restaurado ao fechar o jogo");

    // --- ciclo do watcher: aplica no login, restaura quando o jogo fecha ---
    var s = new WatchState();
    riot.Current = "amigo";
    vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.38, "watcher: aplica no login, com o jogo fechado");
    vcs.Step(s, true); vcs.Step(s, true);
    Assert(riot.Sens("amigo") == 0.38, "watcher: jogo aberto mantém minha cfg");
    vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.9 && !HasPending("amigo"), "watcher: restaura quando o jogo fecha");
    vcs.Step(s, false); vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.9, "watcher: parado depois de restaurar não reaplica");
    vcs.Step(s, true);
    Assert(riot.Sens("amigo") == 0.38, "watcher: jogo reaberto no mesmo login reaplica");
    vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.9, "watcher: restaura de novo ao fechar");

    // tirar a cfg de uso (X na cfg em uso) com ela já carregada na conta: a original volta na hora
    s = new WatchState(); vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.38 && HasPending("amigo"), "watcher: novo login carrega a cfg");
    vcs.ActiveProfile = null; vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.9 && !HasPending("amigo"), "watcher: tirar a cfg de uso devolve a original na hora");
    vcs.Step(s, true); vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.9 && !HasPending("amigo"), "watcher: sem cfg em uso, o jogo abre e fecha com a original");
    vcs.ActiveProfile = "Minha";

    // desligamento forçado: aplicou no login, PC caiu (Riot Client some), conta loga de novo
    s = new WatchState(); vcs.Step(s, false);
    riot.Current = null; vcs.Step(s, false);
    riot.Current = "amigo"; s = new WatchState(); vcs.Step(s, false);
    Assert(PendingSens("amigo") == 0.9 && riot.Sens("amigo") == 0.38, "watcher: após queda, original preservado e minha cfg segue aplicada");
    vcs.Step(s, true);
    riot.PutFails = true;
    try { vcs.Step(s, false); } catch { }
    Assert(HasPending("amigo"), "watcher: restauração que falha mantém pending");
    riot.PutFails = false;
    vcs.Step(s, false);
    Assert(riot.Sens("amigo") == 0.9 && !HasPending("amigo"), "watcher: tenta de novo e restaura");

    // o caso do dono (25/10/2026): duas cfgs salvas da MESMA conta; escolher uma deve valer nessa conta
    riot.Current = "eu"; s = new WatchState();
    riot.Cloud["eu"] = FakeRiot.NewCloud(0.48m);
    vcs.SaveProfileFromCurrent("NasK");          // ajustou no jogo e salvou como NasK
    Assert(vcs.ActiveProfile == "Minha", "salvar a NasK não troca a cfg em uso");
    vcs.Step(s, false);
    Assert(riot.Sens("eu") == 0.38, "watcher: na própria conta, a cfg em uso (Minha) é aplicada");
    vcs.Step(s, true); riot.Cloud["eu"] = FakeRiot.NewCloud(0.7m); vcs.Step(s, false);   // joga, ajusta, fecha
    Assert(riot.Sens("eu") == 0.7 && !HasPending("eu"), "watcher: na conta dona, ajustes feitos no jogo ficam");
    vcs.ActiveProfile = "NasK";
    vcs.Step(s, false);
    Assert(riot.Sens("eu") == 0.48, "watcher: trocar a cfg em uso com o jogo fechado aplica na hora");
    vcs.Step(s, false);
    Assert(riot.Sens("eu") == 0.48, "watcher: e não fica reaplicando sem necessidade");
    vcs.ActiveProfile = "Minha";
    vcs.Step(s, true); vcs.Step(s, false);
    vcs.DeleteProfile("NasK");

    // várias cfgs: a em uso é a que o watcher aplica
    riot.Current = "vitao";
    riot.Cloud["vitao"] = FakeRiot.NewCloud(0.5m);
    vcs.SaveProfileFromCurrent("Vitão");
    Assert(string.Join(",", vcs.ProfileNames()) == "Minha,Vitão", "lista de cfgs em ordem alfabética");
    vcs.ActiveProfile = "Vitão";
    riot.Current = "amigo";
    vcs.StartSession("amigo");
    Assert(riot.Sens("amigo") == 0.5, "sessão aplica a cfg escolhida na lista");
    vcs.RestoreSession("amigo");
    vcs.DeleteProfile("Vitão");
    Assert(vcs.ActiveProfile == null && vcs.Profile() == null, "excluir a cfg em uso deixa nenhuma escolhida");
    vcs.StartSession("amigo");
    Assert(!HasPending("amigo") && riot.Sens("amigo") == 0.9, "sem cfg escolhida, nada é aplicado");
    vcs.ActiveProfile = "Minha";

    // importar / exportar
    var bad = Path.Combine(tmp, "ruim.json");
    File.WriteAllText(bad, "{\"foo\":1}");
    var threw = false;
    try { vcs.Import(bad); } catch (VcsException) { threw = true; }
    Assert(threw, "importação rejeita arquivo inválido");
    var exp = Path.Combine(tmp, "NasK.json");
    vcs.Export("Minha", exp);
    Assert(vcs.ImportName(exp, " ") == "Minha" && vcs.ImportName(exp, "Outra") == "Outra", "nome da importação é conhecido antes (para perguntar se substitui)");
    Assert(vcs.Import(exp, "") == "Minha" && vcs.Import(exp, "NasK") == "NasK", "importar usa o nome digitado, senão o do arquivo");
    Assert(vcs.Profile("NasK")!.Str("SourcePuuid") == "eu", "exportar + importar preserva a cfg");
    threw = false;
    try { vcs.SaveProfileFromCurrent("  "); } catch (VcsException) { threw = true; }
    Assert(threw, "salvar exige um nome");

    // migração da cfg única das versões antigas (profile.json)
    var legacyDir = Path.Combine(tmp, "legado");
    var old = new Vcs(riot, legacyDir, Path.Combine(tmp, "config"));
    Directory.CreateDirectory(legacyDir);
    File.Copy(exp, Path.Combine(legacyDir, "profile.json"));
    old.MigrateLegacyProfile();
    Assert(old.ActiveProfile == "Minha cfg" && old.Profile() != null && !File.Exists(Path.Combine(legacyDir, "profile.json")),
        "profile.json antigo vira a cfg \"Minha cfg\", em uso");

    // renomear
    vcs.RenameProfile("Minha", "Principal");
    Assert(vcs.ActiveProfile == "Principal" && vcs.Profile() != null && vcs.Profile("Minha") == null, "renomear a cfg em uso mantém ela em uso");
    threw = false;
    try { vcs.RenameProfile("NasK", "Principal"); } catch (VcsException) { threw = true; }
    Assert(threw && vcs.Profile("NasK") != null, "renomear para um nome que já existe é recusado e não perde nada");
    vcs.RenameProfile("NasK", "nask");
    Assert(vcs.Profile("nask")!.Str("Name") == "nask" && vcs.ProfileNames().Contains("nask"), "renomear só a caixa das letras funciona");

    // cartão de status: guia o próximo passo
    var st = new Vcs(riot, Path.Combine(tmp, "status"), Path.Combine(tmp, "config"));
    string Title(bool game = false, bool watcher = false) => st.Status(game, watcher).Title;
    Assert(Title() == "Crie sua primeira cfg", "status: sem cfg pede para criar");
    riot.Current = "eu";
    st.SaveProfileFromCurrent("Minha");
    riot.Current = null;
    Assert(Title() == "Inicie o Riot Client", "status: Riot Client fechado");
    riot.Current = "eu";
    var wo = new WatchState();
    st.Step(wo, false);
    Assert(Title(watcher: true) == "Cfg carregada", "status: conta dona também mostra cfg carregada");
    st.Step(wo, true);
    Assert(st.Status(true, true).Detail.Contains("Conta dona"), "status: jogando na conta dona avisa que os ajustes ficam");
    st.Step(wo, false);
    riot.Current = "amigo";
    Assert(Title() == "Desligado" && Title(watcher: true) == "Pronto", "status: conta logada, aguardando aplicar");
    var w = new WatchState();
    st.Step(w, false);
    Assert(Title(watcher: true) == "Cfg carregada", "status: cfg carregada, inicie o VALORANT");
    st.Step(w, true);
    Assert(Title(true, true) == "Jogando com a cfg \"Minha\"", "status: jogando com a cfg");
    st.Step(w, false);   // fechou: restaura

    // watcher ligado com o jogo já aberto: aplica, mas o jogo já leu a nuvem
    w = new WatchState();
    st.Step(w, true);
    Assert(st.Status(true, true).Tone == Tone.Alert, "status: avisa VALORANT rodando sem a cfg (aplicada tarde)");
    st.Step(w, false);   // fecha: restaura
    st.Step(w, true);    // reabre: agora chega a tempo
    Assert(Title(true, true) == "Jogando com a cfg \"Minha\"", "status: reabrir o jogo carrega a cfg");
    st.Step(w, false);
    st.ActiveProfile = null;
    Assert(Title() == "Nenhuma cfg em uso", "status: nenhuma cfg escolhida");

    // mudanças feitas dentro do jogo: ao fechar, o menu pergunta "manter do jogo" ou "manter original"
    var mud = new Vcs(riot, Path.Combine(tmp, "mud"), Path.Combine(tmp, "config"));
    riot.Current = "eu"; riot.Cloud["eu"] = FakeRiot.NewCloud(0.38m);
    mud.SaveProfileFromCurrent("Minha");
    riot.Current = "amigo"; riot.Cloud["amigo"] = FakeRiot.NewCloud(0.9m);
    var wm = new WatchState();
    mud.Step(wm, false); mud.Step(wm, true); mud.Step(wm, false);   // joga e fecha sem mexer em nada
    Assert(mud.SessionChanges() == null, "sem mudanças no jogo, nada a perguntar");
    mud.Step(wm, true); riot.Cloud["amigo"] = FakeRiot.NewCloud(0.55m); mud.Step(wm, false);   // mexeu na sensibilidade
    Assert(mud.SessionChanges()?.Profile == "Minha" && riot.Sens("amigo") == 0.9, "mudou no jogo: pergunta, e a conta volta ao original");
    mud.KeepSessionChanges("amigo");
    Assert(FakeRiot.SensOf(mud.Profile("Minha")!["Cloud"]) == 0.55 && mud.SessionChanges() == null, "manter do jogo: as mudanças viram a cfg");
    mud.Step(wm, true);
    Assert(riot.Sens("amigo") == 0.55, "a cfg atualizada é a aplicada na próxima abertura");
    riot.Cloud["amigo"] = FakeRiot.NewCloud(0.1m); mud.Step(wm, false);
    mud.DiscardSessionChanges("amigo");
    Assert(FakeRiot.SensOf(mud.Profile("Minha")!["Cloud"]) == 0.55 && mud.SessionChanges() == null, "manter original: a cfg não muda");
    var c1 = Json.Parse("{\"floatSettings\":[{\"settingEnum\":\"A\",\"value\":0.5},{\"settingEnum\":\"B\",\"value\":1}],\"actionMappings\":[]}")!;
    var c2 = Json.Parse("{\"floatSettings\":[{\"settingEnum\":\"B\",\"value\":1.0},{\"settingEnum\":\"A\",\"value\":0.500000001}],\"actionMappings\":[],\"roamingSetttingsVersion\":9}")!;
    Assert(Vcs.SameSettings(c1, c2), "comparação ignora ordem, ruído de float e campos internos do jogo");

    // ligado/desligado é lembrado (abrir o app só liga se estiver ligado)
    Assert(st.Enabled, "começa ligado");
    st.Enabled = false;
    Assert(!new Vcs(riot, Path.Combine(tmp, "status"), Path.Combine(tmp, "config")).Enabled && st.ActiveProfile == null,
        "desligar fica gravado e não mexe no resto da config");

    // o json do jogo tem floats como 0.25999999046325686: não pode perder precisão no caminho
    var rt = Json.Write(Json.Parse("{\"v\":0.25999999046325686,\"a\":[1]}"));
    Assert(rt == "{\"v\":0.25999999046325686,\"a\":[1]}", "json preserva floats e arrays de 1 item");

    // formato do serviço de preferências: deflate cru + base64, com acentos (nome de mira "Cruz é")
    var doc = "{\"stringSettings\":[{\"value\":\"Cruz é\"}]}";
    Assert(RiotClient.Inflate(RiotClient.Deflate(doc)) == doc, "deflate/inflate do serviço é ida e volta (UTF-8)");}
finally
{
    try { Directory.Delete(tmp, true); } catch { }
}

Console.WriteLine(failed == 0 ? "todos os testes passaram" : $"{failed} falha(s)");
return failed == 0 ? 0 : 1;

// Riot Client falso: uma "nuvem" por conta; passa tudo por json como a API real.
sealed class FakeRiot : IRiot
{
    public readonly Dictionary<string, object> Cloud = new();
    public string? Current;
    public bool PutFails;

    public string? Puuid() => Current;
    public object GetSettings() => Json.Parse(Json.Write(Cloud[Current!]))!;
    public void PutSettings(object data)
    {
        if (PutFails) throw new VcsException("PUT falhou");
        Cloud[Current!] = Json.Parse(Json.Write(data))!;
    }

    public double Sens(string who) => SensOf(Cloud[who]);

    public static double SensOf(object cloud) =>
        Convert.ToDouble(((Dictionary<string, object>)((IList)((Dictionary<string, object>)cloud)["floatSettings"])[0]!)["value"]);

    public static object NewCloud(decimal sens) => Json.Parse(Json.Write(new Dictionary<string, object>
    {
        ["floatSettings"] = new[] { new Dictionary<string, object> { ["settingEnum"] = "EAresFloatSettingName::MouseSensitivity", ["value"] = sens } },
        ["actionMappings"] = new object[0],
    }))!;
}
