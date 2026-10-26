// Integração com o Windows e o Riot Client: API local, processo do jogo, autostart e o watcher (bandeja).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ValCfgSync;

// Riot Client local (só para saber a conta logada e pegar o token dela) + serviço de preferências da
// Riot (o mesmo que o jogo usa para salvar a cfg). Detalhes e o porquê em AGENTS.md.
public sealed class RiotClient : IRiot
{
    static readonly string Lockfile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Riot Games\Riot Client\Config\lockfile");
    // usw2 verificado com conta BR em 25/10/2026; euc1 devolveu os mesmos dados (armazenamento global).
    const string Prefs = "https://player-preferences-usw2.pp.sgp.pvp.net/playerPref/v3/";
    const string Type = "Ares.PlayerSettings";

    public string? Puuid()
    {
        try { return Token().Str("subject"); }
        catch { return null; }   // fechado ou sem login (a API local responde 404)
    }

    public object GetSettings()
    {
        var b64 = (Json.Parse(Http("GET", Prefs + "getPreference/" + Type, Bearer())) as Dictionary<string, object>)?.Str("data");
        var data = string.IsNullOrEmpty(b64) ? null : Json.Parse(Inflate(b64!));
        if (!Vcs.IsCloudData(data)) throw new VcsException("A Riot devolveu configurações em formato inesperado.");
        return data!;
    }

    public void PutSettings(object data) => Http("PUT", Prefs + "savePreference", Bearer(),
        Json.Write(new Dictionary<string, object> { ["type"] = Type, ["data"] = Deflate(Json.Write(data)) }));

    static string Bearer() => "Bearer " + (Token().Str("accessToken") ?? throw new VcsException("Faça login no Riot Client."));

    // Token da sessão logada, entregue pelo próprio Riot Client local. Nenhuma senha passa por aqui.
    static Dictionary<string, object> Token()
    {
        if (!File.Exists(Lockfile)) throw new VcsException("O Riot Client não está aberto.");
        string text;
        using (var fs = new FileStream(Lockfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))   // o Riot Client mantém aberto
        using (var r = new StreamReader(fs)) text = r.ReadToEnd();
        var p = text.Split(':');   // nome:pid:porta:senha:protocolo
        if (p.Length < 5) throw new VcsException("lockfile do Riot Client em formato inesperado.");
        var basic = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + p[3]));
        return Json.Parse(Http("GET", $"{p[4]}://127.0.0.1:{p[2]}/entitlements/v1/token", basic)) as Dictionary<string, object>
               ?? throw new VcsException("O Riot Client devolveu um token inesperado.");
    }

    static string Http(string method, string url, string auth, string? body = null)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method  = method;
        req.Timeout = 8000;
        req.Headers["Authorization"] = auth;
        // Certificado autoassinado só no Riot Client local; o servidor da Riot passa pela validação normal.
        if (req.RequestUri.IsLoopback) req.ServerCertificateValidationCallback = (_, _, _, _) => true;
        if (body != null)
        {
            req.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(body);
            using var s = req.GetRequestStream();
            s.Write(bytes, 0, bytes.Length);
        }
        using var resp = req.GetResponse();
        using var reader = new StreamReader(resp.GetResponseStream()!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // O serviço guarda o json como deflate cru (sem cabeçalho zlib) em base64.
    public static string Inflate(string b64)
    {
        using var ds = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
        using var r = new StreamReader(ds, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public static string Deflate(string json)
    {
        var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ds.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(ms.ToArray());
    }
}

public static class Game
{
    // A Riot registra a pasta de cada jogo instalado em RiotClientInstalls.json.
    public static string? FindExe()
    {
        var candidates = new List<string>();
        try
        {
            var installs = Json.ReadFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\RiotClientInstalls.json"));
            if (installs?.TryGetValue("associated_client", out var c) == true && c is Dictionary<string, object> clients)
                candidates.AddRange(clients.Keys.Where(k => k.IndexOf("VALORANT", StringComparison.OrdinalIgnoreCase) >= 0)
                                                .Select(k => Path.Combine(k, "VALORANT.exe")));
        }
        catch { /* ausente ou corrompido: cai no caminho padrão */ }
        candidates.Add(@"C:\Riot Games\VALORANT\live\VALORANT.exe");
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    // VALORANT.exe é só o lançador; o jogo de verdade é VALORANT-Win64-Shipping.exe.
    public static string[] ProcessNames(string? exe)
    {
        var names = new List<string> { "VALORANT-Win64-Shipping" };
        if (!string.IsNullOrEmpty(exe)) names.Add(Path.GetFileNameWithoutExtension(exe));
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsRunning(string[] names) => names.Any(n =>
    {
        var procs = Process.GetProcessesByName(n);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    });
}

// Início com o Windows: o watcher sobe minimizado na bandeja e só age quando uma conta loga no Riot Client.
public static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "VALORANT CFG", OldName = "ValCfgSync";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) != null || k?.GetValue(OldName) != null;
    }

    public static void Set(bool enabled, string exe)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        k.DeleteValue(OldName, false);   // nome antigo do programa
        if (enabled) k.SetValue(Name, $"\"{exe}\" --watcher"); else k.DeleteValue(Name, false);
    }
}

// O watcher é este mesmo exe com --watcher: ícone na bandeja + laço de 2s numa thread.
// Mutex = "está rodando" (instância única); evento nomeado = pedido de parada vindo do menu.
public static class Watcher
{
    public const string MutexName = @"Local\ValCfgSyncWatcher", StopName = @"Local\ValCfgSyncStop";

    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    public static void Start(string exe, bool wait = true)
    {
        Process.Start(exe, "--watcher")?.Dispose();
        for (var i = 0; wait && i < 30 && !IsRunning(); i++) Thread.Sleep(100);
    }

    public static void Stop()
    {
        if (EventWaitHandle.TryOpenExisting(StopName, out var e)) using (e) e.Set();
        for (var i = 0; i < 50 && IsRunning(); i++) Thread.Sleep(100);   // espera soltar o mutex antes de um novo Start
    }

    public static void Run(Vcs vcs, string selfExe)
    {
        if (!vcs.Enabled) return;   // desligado no ☰/bandeja: nem o início com o Windows liga
        using var mutex = new Mutex(false, MutexName);
        try { if (!mutex.WaitOne(0)) return; }   // já existe um watcher rodando
        catch (AbandonedMutexException) { }        // o anterior morreu sem soltar: agora é nosso
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName);
        stop.Reset();

        Application.EnableVisualStyles();
        var ui = new Control();
        ui.CreateControl();   // para a thread do laço falar com a bandeja (BeginInvoke)
        using var tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(selfExe),
            Text = "VALORANT CFG - ativo",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add("Abrir VALORANT CFG", null, (_, _) => Process.Start(selfExe)?.Dispose());
        tray.ContextMenuStrip.Items.Add("Desligar", null, (_, _) => { vcs.Enabled = false; stop.Set(); });
        tray.DoubleClick += (_, _) => Process.Start(selfExe)?.Dispose();
        vcs.Notify += msg => ui.BeginInvoke(new Action(() => tray.ShowBalloonTip(4000, "VALORANT CFG", msg, ToolTipIcon.Info)));

        var loop = new Thread(() =>
        {
            var names = Game.ProcessNames(vcs.GameExe);
            vcs.Log($"watcher iniciado (processos: {string.Join(", ", names)})");
            var state = new WatchState();
            string? lastError = null;
            while (!stop.WaitOne(2000))
            {
                try { vcs.Step(state, Game.IsRunning(names)); }
                catch (Exception ex)
                {
                    // falhas tentam de novo a cada ciclo; loga só quando o erro muda
                    if (ex.Message != lastError) vcs.Log("ERRO: " + ex.Message);
                    lastError = ex.Message;
                }
            }
            vcs.Log("watcher parado");
            ui.BeginInvoke(new Action(Application.ExitThread));
        }) { IsBackground = true };
        loop.Start();
        Application.Run();
        tray.Visible = false;
    }
}
