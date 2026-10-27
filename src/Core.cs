// Regras de negócio da sessão (aplicar / restaurar). Invariantes em AGENTS.md: leia antes de mudar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ValCfgSync;

public sealed class VcsException(string message) : Exception(message);

// Cfg da nuvem via Riot Client. Interface só para os testes trocarem por um falso.
public interface IRiot
{
    string? Puuid();               // null = Riot Client fechado ou sem login
    object GetSettings();          // objeto "data" de Ares.PlayerSettings
    void PutSettings(object data);
}

public static class Json
{
    static readonly JavaScriptSerializer S = new() { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

    public static object? Parse(string s) => S.DeserializeObject(s);
    public static string Write(object? o) => S.Serialize(o);

    public static Dictionary<string, object>? ReadFile(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object> : null;

    // Escrita atômica (tmp + replace): um desligamento no meio nunca deixa json pela metade.
    public static void WriteFile(string path, object o)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Write(o), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }

    public static string? Str(this Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;
}

public sealed class WatchState
{
    public string? Login;
    public bool LoginDone, RunDone, GameSeen;
    public bool GameWasClosed;   // já vimos o jogo fechado: se ele abrir agora, a cfg chega a tempo
    public string? Applied;      // cfg aplicada neste login: trocar a cfg em uso com o jogo fechado aplica na hora
}

public enum Tone { Idle, Ok, Warn, Alert }

public sealed class Vcs
{
    public const string Format = "ValCfgSync/1";

    public readonly string DataDir, ProfilesDir, ChangesDir, ConfigFile, PendingDir, LogFile, GameConfigDir;
    readonly IRiot riot;

    public event Action<string>? Notify;   // aviso ao usuário (balão da bandeja) quando aplica ou restaura

    public CrosshairStore Crosshairs => new(Path.Combine(DataDir, "crosshairs.json"));   // aba Miras

    public Vcs(IRiot riot, string? dataDir = null, string? gameConfigDir = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.riot     = riot;
        DataDir       = dataDir ?? DefaultDataDir(local);
        GameConfigDir = gameConfigDir ?? Path.Combine(local, @"VALORANT\Saved\Config");
        ProfilesDir   = Path.Combine(DataDir, "profiles");
        ChangesDir    = Path.Combine(DataDir, "changes");
        ConfigFile    = Path.Combine(DataDir, "config.json");
        PendingDir    = Path.Combine(DataDir, "pending");
        LogFile       = Path.Combine(DataDir, "log.txt");
    }

    // O programa se chamava ValCfgSync: move a pasta antiga para o nome novo; se não der, segue usando a antiga.
    static string DefaultDataDir(string local)
    {
        string dir = Path.Combine(local, "ValorantCFG"), old = Path.Combine(local, "ValCfgSync");
        if (Directory.Exists(dir) || !Directory.Exists(old)) return dir;
        try { Directory.Move(old, dir); return dir; } catch { return old; }
    }

    public void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 1 << 20)
            {
                File.Copy(LogFile, LogFile + ".old", true);
                File.Delete(LogFile);
            }
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}\r\n", Encoding.UTF8);
        }
        catch { /* log nunca derruba o watcher */ }
    }

    static string Short(string puuid) => puuid.Length > 8 ? puuid.Substring(0, 8) : puuid;

    // ---------- config ----------

    Dictionary<string, object> Config()
    {
        try { return Json.ReadFile(ConfigFile) ?? new(); } catch { return new(); }
    }

    void SetConfig(string key, object? value)
    {
        var c = Config();
        c[key] = value!;
        Json.WriteFile(ConfigFile, c);
    }

    // Ligado = o watcher deve rodar. Abrir o app liga se estiver ligado; "Desligar" no ☰ ou na bandeja é lembrado.
    public bool Enabled
    {
        get => !Config().TryGetValue("Enabled", out var v) || v is not false;
        set => SetConfig("Enabled", value);
    }

    public string GameExe
    {
        get => Config().Str("GameExe") ?? "";
        set => SetConfig("GameExe", value);
    }

    // Nome da cfg em uso: é ela que o watcher aplica.
    public string? ActiveProfile
    {
        get => Config().Str("ActiveProfile");
        set => SetConfig("ActiveProfile", value);
    }

    // ---------- gráficos: GameUserSettings.ini local, por conta ----------

    // Pasta da conta = Config\<puuid>-<região>. null se a conta nunca abriu o jogo neste PC.
    public string? AccountIni(string puuid)
    {
        if (!Directory.Exists(GameConfigDir)) return null;
        var dir = Directory.GetDirectories(GameConfigDir, puuid + "*").FirstOrDefault();
        return dir == null ? null : Path.Combine(dir, @"WindowsClient\GameUserSettings.ini");
    }

    // Bytes em base64: não depende do encoding do .ini.
    static string? ReadB64(string? path) => path != null && File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;

    static void WriteB64(string path, string? b64)
    {
        if (b64 == null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Convert.FromBase64String(b64));
    }

    // ---------- cfgs salvas (profiles\<nome>.json) ----------

    public static bool IsCloudData(object? o) =>
        o is Dictionary<string, object> d && d.ContainsKey("floatSettings") && d.ContainsKey("actionMappings");

    public static bool IsProfile(Dictionary<string, object>? p) =>
        p != null && p.Str("Format") == Format && p.TryGetValue("Cloud", out var c) && IsCloudData(c) && p.Str("SourcePuuid") != null;

    string ProfilePath(string name) =>
        Path.Combine(ProfilesDir, string.Concat(name.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) + ".json");

    public Dictionary<string, object>? Profile(string name)
    {
        try { var p = Json.ReadFile(ProfilePath(name)); return IsProfile(p) ? p : null; } catch { return null; }
    }

    // A cfg em uso (a que o watcher aplica), ou null.
    public Dictionary<string, object>? Profile() => ActiveProfile is { } name ? Profile(name) : null;

    public List<string> ProfileNames() =>
        (Directory.Exists(ProfilesDir) ? Directory.GetFiles(ProfilesDir, "*.json") : new string[0])
            .Select(f => { try { var p = Json.ReadFile(f); return IsProfile(p) ? p!.Str("Name") ?? Path.GetFileNameWithoutExtension(f) : null; } catch { return null; } })
            .OfType<string>()
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    static string CheckName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? throw new VcsException("Digite um nome para a cfg.") : name!.Trim();

    // Grava a cfg; a primeira salva vira a cfg em uso.
    void WriteProfile(string name, Dictionary<string, object> p)
    {
        p["Name"] = name;
        Json.WriteFile(ProfilePath(name), p);
        if (Profile() == null) ActiveProfile = name;
    }

    // Captura a cfg da conta logada agora. Requer Riot Client logado.
    public void SaveProfileFromCurrent(string name)
    {
        name = CheckName(name);
        var puuid = riot.Puuid() ?? throw new VcsException("Abra o Riot Client e faça login na conta que tem a cfg.");
        // Com sessão ativa nesta conta, o que está nela é a cfg em uso (talvez ajustada): a dona continua a mesma.
        var active = Profile();
        var source = File.Exists(PendingFile(puuid)) && active != null ? active.Str("SourcePuuid")! : puuid;
        WriteProfile(name, new Dictionary<string, object>
        {
            ["Format"]           = Format,
            ["SavedAt"]          = DateTime.Now.ToString("s"),
            ["SourcePuuid"]      = source,
            ["Cloud"]            = riot.GetSettings(),
            ["GameUserSettings"] = ReadB64(AccountIni(puuid))!,
        });
        Log($"cfg \"{name}\" salva a partir da conta {Short(puuid)} (dona: {Short(source)})");
    }

    static Dictionary<string, object> ReadImport(string path)
    {
        Dictionary<string, object>? p;
        try { p = Json.ReadFile(path); } catch { p = null; }
        return IsProfile(p) ? p! : throw new VcsException("Arquivo inválido: não é uma cfg exportada pelo VALORANT CFG.");
    }

    // Nome que a importação vai usar: o digitado; sem ele, o guardado no arquivo (ou o nome do arquivo).
    public string ImportName(string path, string? name) =>
        CheckName(string.IsNullOrWhiteSpace(name) ? ReadImport(path).Str("Name") ?? Path.GetFileNameWithoutExtension(path) : name);

    // Devolve o nome usado.
    public string Import(string path, string? name = null)
    {
        var p = ReadImport(path);
        name = ImportName(path, name);
        WriteProfile(name, p);
        Log($"cfg \"{name}\" importada de {path}");
        return name;
    }

    public void Export(string name, string path)
    {
        if (Profile(name) == null) throw new VcsException($"A cfg \"{name}\" não existe mais.");
        File.Copy(ProfilePath(name), path, true);
        Log($"cfg \"{name}\" exportada para {path}");
    }

    public void RenameProfile(string oldName, string newName)
    {
        newName = CheckName(newName);
        if (newName == oldName) return;
        var p = Profile(oldName) ?? throw new VcsException($"A cfg \"{oldName}\" não existe mais.");
        // mesmo arquivo com outra caixa (ex.: "nask" -> "NasK") é renomear, não conflito
        var samePath = ProfilePath(newName).Equals(ProfilePath(oldName), StringComparison.OrdinalIgnoreCase);
        if (Profile(newName) != null && !samePath) throw new VcsException($"Já existe uma cfg \"{newName}\".");
        p["Name"] = newName;
        Json.WriteFile(ProfilePath(newName), p);   // grava antes de apagar: falha no meio não perde a cfg
        if (!samePath) File.Delete(ProfilePath(oldName));
        if (ActiveProfile == oldName) ActiveProfile = newName;
        Log($"cfg \"{oldName}\" renomeada para \"{newName}\"");
    }

    // Pendências não dependem da cfg: apagar a cfg em uso no meio de uma sessão ainda restaura o original.
    public void DeleteProfile(string name)
    {
        File.Delete(ProfilePath(name));
        if (ActiveProfile == name) ActiveProfile = null;
        Log($"cfg \"{name}\" excluída");
    }

    // Versões até a 2.0 guardavam uma cfg só em profile.json.
    public void MigrateLegacyProfile()
    {
        var legacy = Path.Combine(DataDir, "profile.json");
        if (!File.Exists(legacy)) return;
        try { Import(legacy, "Minha cfg"); File.Delete(legacy); }
        catch (Exception ex) { Log("migração de profile.json falhou: " + ex.Message); }
    }

    // ---------- sessão ----------
    // Invariante: o original da conta vai para pending\<puuid>.json ANTES de aplicar,
    // nunca é sobrescrito, e só é apagado depois de restaurado com sucesso.

    public string PendingFile(string puuid) => Path.Combine(PendingDir, puuid + ".json");

    public int PendingCount() => Directory.Exists(PendingDir) ? Directory.GetFiles(PendingDir, "*.json").Length : 0;

    // late = o jogo já estava aberto antes de aplicarmos: ele provavelmente já leu a cfg da nuvem e só vai
    // usar a nova se reabrir. Fica no pending para o cartão de status avisar.
    public void StartSession(string puuid, bool late = false)
    {
        var id = Short(puuid);
        var profile = Profile();
        if (profile == null) { Log("nenhuma cfg salva - nada a aplicar"); return; }
        // Conta dona (a cfg foi salva dela): aplica também, para dar para alternar entre várias cfgs da mesma
        // conta, mas sem guardar original. É a conta do dono: o que ele ajustar no jogo fica nela.
        var owner = profile.Str("SourcePuuid") == puuid;

        var pending = PendingFile(puuid);
        if (Json.ReadFile(pending) is { } existing)
        {
            Log($"conta {id}: original já guardado de uma sessão anterior - mantido");
            // só os metadados mudam; o original (Cloud/Ini) nunca é tocado
            existing["Late"] = late;
            existing["Profile"] = ActiveProfile!;
            Json.WriteFile(pending, existing);
        }
        else
        {
            var ini = AccountIni(puuid);
            var p = new Dictionary<string, object?>
            {
                ["Puuid"]     = puuid,
                ["CreatedAt"] = DateTime.Now.ToString("s"),
                ["Profile"]   = ActiveProfile,
                ["Late"]      = late,
                ["IniPath"]   = ini,
            };
            // Sem "Cloud" no pending = nada a restaurar (conta dona).
            if (!owner) { p["Cloud"] = riot.GetSettings(); p["Ini"] = ReadB64(ini); }
            Json.WriteFile(pending, p);
        }

        riot.PutSettings(profile["Cloud"]);
        // gráficos só onde o pending registrou a pasta (IniPath do pending, não o de agora)
        var iniPath = Json.ReadFile(pending)!.Str("IniPath");
        var mine = profile.Str("GameUserSettings");
        if (iniPath != null && mine != null) WriteB64(iniPath, mine);
        Log($"conta {id}: cfg \"{ActiveProfile}\" aplicada" + (owner ? " (conta dona: nada a restaurar depois)" : ""));
        Notify?.Invoke($"Cfg \"{ActiveProfile}\" aplicada nesta conta.");
    }

    // Só chame com o jogo fechado e com ESTA conta logada no Riot Client.
    public void RestoreSession(string puuid)
    {
        var pending = PendingFile(puuid);
        var p = Json.ReadFile(pending);
        if (p == null) return;
        if (!p.ContainsKey("Cloud"))   // conta dona: não há original, os ajustes feitos no jogo ficam
        {
            File.Delete(pending);
            Log($"conta {Short(puuid)}: conta dona - nada a restaurar");
            return;
        }
        riot.PutSettings(p["Cloud"]);
        var iniPath = p.Str("IniPath");
        if (iniPath != null) WriteB64(iniPath, p.Str("Ini"));
        File.Delete(pending);
        Log($"conta {Short(puuid)}: cfg original restaurada");
        Notify?.Invoke("A cfg original da conta voltou.");
    }

    // Um ciclo do watcher. Aplica no LOGIN (antes de o jogo abrir, que é quando ele lê a nuvem) e
    // restaura quando o jogo FECHA (com o Riot Client ainda logado: depois do logout a API some).
    public void Step(WatchState s, bool gameRunning)
    {
        if (!gameRunning) s.GameWasClosed = true;
        var puuid = riot.Puuid();
        if (puuid != s.Login) { s.Login = puuid; s.LoginDone = s.RunDone = s.GameSeen = false; }
        if (puuid == null) return;
        var pending = File.Exists(PendingFile(puuid));

        if (gameRunning)
        {
            s.GameSeen = true;
            // Jogo reaberto no mesmo login depois de restaurar: detectado em até 2s da abertura, chega a
            // tempo. Se o watcher já encontrou o jogo aberto, é tarde (late) e o cartão avisa.
            if (!s.RunDone && !pending) { StartSession(puuid, late: !s.GameWasClosed); s.RunDone = true; s.Applied = ActiveProfile; }
            return;
        }
        if (s.GameSeen)
        {
            if (pending)
            {
                CaptureSessionChanges(puuid);   // antes de restaurar: depois a conta já não tem o que o jogo gravou
                RestoreSession(puuid);          // se lançar erro, GameSeen fica e o próximo ciclo tenta de novo
            }
            s.GameSeen = s.RunDone = false;
            s.LoginDone = true;                   // não reaplica parado; só se o jogo reabrir (ou a cfg mudar)
            return;
        }
        if (!s.LoginDone || s.Applied != ActiveProfile)
        {
            // Sem cfg em uso (o usuário a tirou no X): a conta que já tinha recebido a cfg volta à original
            // agora, para o jogo abrir como era. Se falhar, Applied não muda e o próximo ciclo tenta de novo.
            if (Profile() == null) { if (pending) RestoreSession(puuid); }
            else StartSession(puuid);
            s.LoginDone = true; s.Applied = ActiveProfile;
        }
    }

    // ---------- mudanças feitas dentro do jogo ----------
    // Ao fechar o jogo, se a conta não está mais igual à cfg aplicada, o usuário mexeu nas configurações no
    // jogo. Guardamos o que o jogo gravou em changes\<puuid>.json e o menu pergunta: manter do jogo (vira a
    // cfg) ou manter original (descarta; a cfg volta a valer na próxima abertura do jogo).

    public string ChangesFile(string puuid) => Path.Combine(ChangesDir, puuid + ".json");

    public void CaptureSessionChanges(string puuid)
    {
        var pending = Json.ReadFile(PendingFile(puuid));
        var name = pending?.Str("Profile");
        if (name == null || Profile(name) is not { } profile) return;
        var now = riot.GetSettings();
        if (SameSettings(now, profile["Cloud"])) return;
        Json.WriteFile(ChangesFile(puuid), new Dictionary<string, object?>
        {
            ["Puuid"]   = puuid,
            ["Profile"] = name,
            ["At"]      = DateTime.Now.ToString("s"),
            ["Cloud"]   = now,
            // o jogo grava os gráficos ao fechar, então o .ini de agora já tem o que foi mudado
            ["Ini"]     = ReadB64(pending!.Str("IniPath")),
        });
        Log($"conta {Short(puuid)}: cfg \"{name}\" foi alterada no jogo - aguardando decisão no menu");
        Notify?.Invoke($"Você mudou a cfg \"{name}\" no jogo. Abra o VALORANT CFG para decidir se mantém.");
    }

    // A mudança mais recente que ainda espera resposta, ou null.
    public (string Puuid, string Profile)? SessionChanges()
    {
        if (!Directory.Exists(ChangesDir)) return null;
        foreach (var f in Directory.GetFiles(ChangesDir, "*.json").OrderByDescending(File.GetLastWriteTime))
            try { if (Json.ReadFile(f) is { } c && c.Str("Profile") is { } p) return (c.Str("Puuid") ?? "", p); } catch { }
        return null;
    }

    // "Manter do jogo": o que o jogo gravou passa a ser a cfg.
    public void KeepSessionChanges(string puuid)
    {
        var c = Json.ReadFile(ChangesFile(puuid));
        if (c?.Str("Profile") is { } name && Profile(name) is { } profile)
        {
            profile["Cloud"] = c["Cloud"];
            if (c.Str("Ini") is { } ini) profile["GameUserSettings"] = ini;
            profile["SavedAt"] = DateTime.Now.ToString("s");
            Json.WriteFile(ProfilePath(name), profile);
            Log($"cfg \"{name}\": mudanças feitas no jogo mantidas");
        }
        File.Delete(ChangesFile(puuid));
    }

    // "Manter original": descarta; a cfg continua como estava e é aplicada de novo na próxima abertura do jogo.
    public void DiscardSessionChanges(string puuid)
    {
        if (Json.ReadFile(ChangesFile(puuid))?.Str("Profile") is { } name) Log($"cfg \"{name}\": mudanças feitas no jogo descartadas");
        File.Delete(ChangesFile(puuid));
    }

    // Compara item a item (ordem das listas não importa; floats com tolerância). O resto do documento
    // (versões, perfis internos) o jogo mexe sozinho e não conta como mudança do usuário.
    public static bool SameSettings(object a, object b)
    {
        var x = Flatten(a);
        var y = Flatten(b);
        return x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    static Dictionary<string, string> Flatten(object cloud)
    {
        var map = new Dictionary<string, string>();
        if (cloud is not Dictionary<string, object> d) return map;
        foreach (var list in new[] { "floatSettings", "intSettings", "boolSettings", "stringSettings" })
            if (d.TryGetValue(list, out var items) && items is System.Collections.IEnumerable e)
                foreach (var it in e.OfType<Dictionary<string, object>>())
                    map[list + ":" + it.Str("settingEnum")] = Norm(it.TryGetValue("value", out var v) ? v : null);
        if (d.TryGetValue("actionMappings", out var binds) && binds is System.Collections.IEnumerable be)
            foreach (var it in be.OfType<Dictionary<string, object>>())
                map[$"bind:{it.Str("name")}:{it.Str("characterName")}:{Norm(it.TryGetValue("bindIndex", out var i) ? i : null)}"] = Json.Write(it);
        return map;
    }

    static string Norm(object? v) => v is int or long or decimal or double or float
        ? Convert.ToDecimal(v).ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)   // 1 == 1.0; ruído além de 5 casas some
        : Json.Write(v);

    // O que o cartão do menu mostra: o estado atual e o próximo passo do usuário.
    public (string Title, string Detail, Tone Tone) Status(bool gameRunning, bool watcherRunning)
    {
        if (ProfileNames().Count == 0)
            return ("Crie sua primeira cfg", "Entre no Riot Client com a conta da sua cfg, digite um nome em \"Nova CFG\" e aperte Enter.", Tone.Warn);
        var profile = Profile();
        if (profile == null)
            return ("Nenhuma cfg em uso", "O VALORANT abre com a cfg original de cada conta. Para usar uma cfg, clique no nome dela e no ✓.", Tone.Idle);

        var then = watcherRunning ? "" : " O VALORANT CFG está desligado: ☰ → Ligar.";
        var puuid = riot.Puuid();
        if (puuid == null) return ("Inicie o Riot Client", "Abra o Riot Client e entre na conta em que vai jogar." + then, Tone.Idle);

        Dictionary<string, object>? pending;
        try { pending = Json.ReadFile(PendingFile(puuid)); } catch { pending = null; }
        var name = pending?.Str("Profile") ?? ActiveProfile;
        var others = PendingCount() - (pending != null ? 1 : 0);
        var note = others > 0 ? $"  {others} outra(s) conta(s) aguardando a cfg original voltar." : "";

        if (gameRunning && (pending == null || pending.TryGetValue("Late", out var late) && late is true))
            return ("VALORANT está rodando sem a cfg",
                    (watcherRunning ? $"Feche o jogo e abra de novo para carregar \"{name}\"." : "Ligue o VALORANT CFG (☰ → Ligar), feche o jogo e abra de novo.") + note, Tone.Alert);
        if (gameRunning)
            return ($"Jogando com a cfg \"{name}\"", (pending!.ContainsKey("Cloud")
                ? "Ao fechar o VALORANT, a cfg original desta conta volta."
                : "Conta dona da cfg: o que você ajustar no jogo fica nesta conta. Ao fechar, o app pergunta se guarda na cfg.") + note, Tone.Ok);
        if (pending != null) return ("Cfg carregada", $"A cfg \"{name}\" está nesta conta. Inicie o VALORANT." + note, Tone.Ok);
        return watcherRunning
            ? ("Pronto", $"A cfg \"{name}\" será carregada nesta conta ao iniciar o VALORANT." + note, Tone.Ok)
            : ("Desligado", $"Ligue o VALORANT CFG (☰ → Ligar) para carregar \"{name}\" nesta conta." + note, Tone.Idle);
    }
}
