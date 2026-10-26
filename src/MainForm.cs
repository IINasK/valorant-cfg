// Menu. Só UI: toda regra fica em Core.cs / Platform.cs. Layout segue o desenho do dono:
// título + ☰ (tudo que é extra), cartão de status (guia do próximo passo) e lista de cfgs com "Nova CFG"
// no fim. Sem botão "Usar cfg": confirmar uma cfg no ✓ liga o watcher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ValCfgSync;

public sealed class MainForm : Form, IMessageFilter
{
    // Clicar em qualquer lugar do app fora do ✓ aberto (e do nome dele) recolhe o ✓; fora do menu da ⚙️, recolhe
    // o menu. Clicar no próprio nome/engrenagem já recolhe pelo Toggle; em outro, recolhe aqui e o clique abre o dele.
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != 0x0201 /* WM_LBUTTONDOWN */) return false;
        var target = Control.FromHandle(m.HWnd);
        if (revealed is { } r && target != r.Check && target != r.Label) CancelReveal(animated: true);
        if (tools is { } t && Array.IndexOf(t.Inside, target) < 0) t.Close();
        return false;
    }

    internal const string AppName = "VALORANT CFG";
    internal static readonly Color Accent = Color.FromArgb(255, 70, 85), Ink = Color.FromArgb(31, 35, 40), Muted = Color.FromArgb(107, 114, 128),
                                   Disabled = Color.FromArgb(175, 180, 188);
    const string IconFont = "Segoe MDL2 Assets";   // vem com Windows 10/11
    const string GlyphMenu = "", GlyphDelete = "", GlyphCancel = "\uE711", GlyphGear = "\uE713";
    const int RowH = 52, ListW = 472, ListH = 260;   // lista de 24 a 496: margens iguais dos dois lados

    // Fonte do VALORANT (a fan font "VALORANT", se estiver instalada no Windows): SÓ no título "VALORANT CFG"
    // e no "EM USO" (decisão do dono). Ela só tem maiúsculas, daí o Caps(). Não é embutida no exe (licença de
    // fan font). Sem ela, cai na fonte da interface (UiBold).
    static readonly bool HasValorantFont = FontFamily.Families.Any(f => f.Name == "VALORANT");
    static Font Display(float size) => new(HasValorantFont ? "VALORANT" : UiBold, size);

    // O resto da interface usa Bahnschrift: a DIN que vem com o Windows 10/11, parecida com a DIN Next dos textos
    // do VALORANT (o dono pediu "essa fonte ou parecido"). Sem ela (Windows antigo), Segoe UI.
    static readonly bool HasDin = FontFamily.Families.Any(f => f.Name == "Bahnschrift");
    static readonly string Ui = HasDin ? "Bahnschrift" : "Segoe UI", UiBold = HasDin ? "Bahnschrift SemiBold" : "Segoe UI Semibold";
    static string Caps(string s) => HasValorantFont ? s.ToUpper(new System.Globalization.CultureInfo("pt-BR")) : s;

    readonly Vcs vcs;
    readonly string selfExe = Application.ExecutablePath;
    readonly float scale;   // AutoScaleMode.None: as linhas da lista são recriadas em tempo de execução, então escalamos à mão
    readonly Panel card, cardBar, viewport, rows;
    readonly Label statusTitle, statusDetail, menuButton, backButton;
    readonly ThinScrollBar scroll;
    readonly Button yesButton, noButton;
    readonly Timer flashTimer = new();
    bool cardBusy;      // cartão mostrando aviso ou pergunta: o status ao vivo espera
    Action? onYes, onNo;
    readonly ToolTip tips = new();
    // ☰ abre uma página (não um popup, pedido do dono) que cobre a lista; o cartão continua à vista para os avisos.
    readonly SmoothPanel page;
    readonly Label gamePath, dataPath;
    readonly ToggleSwitch watcherSwitch, autoSwitch;
    bool settingsOpen;
    TextBox? newName;
    bool editing;   // renomeando uma cfg: não recria a lista no meio da edição
    // Com o jogo aberto, trocar de cfg é bloqueado: a troca só valeria na próxima abertura e confundiria.
    bool gameRunning;
    readonly List<(Label Name, Label? Trash, bool Active)> lockables = new();
    // Escolher cfg em dois passos (desenho do dono): clicar no nome revela o check; clicar no check confirma.
    const float SmallName = 13f, BigName = 15f;
    const int CheckW = 32, NameShift = 40;
    (string Name, Panel Check, Label Label)? revealed;
    int revealedAt;   // Environment.TickCount de quando o ✓ abriu (2º clique rápido no ✓ = clique duplo)
    (Control[] Inside, Action Close)? tools;   // menu da ⚙️ aberto: Inside[0] é a engrenagem

    public MainForm(Vcs vcs)
    {
        this.vcs = vcs;
        scale = DeviceDpi / 96f;
        Text = AppName;
        Icon = Icon.ExtractAssociatedIcon(selfExe);
        Font = new Font(Ui, 9.5f);
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(520), S(444));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Add(AppLogo(22, 21, 30));   // 30 de altura, centrado com o título (y 36)
        Add(BrandTitle(54, 16, 380, 40));
        menuButton = Add(IconLabel(GlyphMenu, 466, 18, "Configurações", 17f));
        backButton = Add(IconLabel(GlyphCancel, 466, 18, "Voltar para a lista de cfgs"));
        backButton.Visible = false;
        menuButton.Click += (_, _) => ShowSettings(true);
        backButton.Click += (_, _) => ShowSettings(false);

        card = Add(new Panel { Bounds = R(16, 70, 488, 84) });   // 3 linhas de detalhe: cabe a pergunta das mudanças
        cardBar = new Panel { Dock = DockStyle.Left, Width = S(5) };
        statusTitle = new Label { Font = new Font(UiBold, 11f), Bounds = R(16, 7, 460, 22) };
        statusDetail = new Label { ForeColor = Ink, Bounds = R(16, 30, 460, 52) };
        yesButton = CardButton(300, Accent, Color.White);
        noButton = CardButton(392, Color.FromArgb(229, 231, 235), Ink);
        noButton.Text = "Cancelar";
        yesButton.Click += (_, _) => { var action = onYes; EndCard(); action?.Invoke(); };
        noButton.Click += (_, _) => { var action = onNo; EndCard(); action?.Invoke(); };
        flashTimer.Tick += (_, _) => EndCard();
        card.Controls.AddRange(new Control[] { cardBar, statusTitle, statusDetail, yesButton, noButton });

        viewport = Add(new Panel { Bounds = R(24, 168, ListW, ListH) });
        rows = new Panel { Location = Point.Empty, Width = S(ListW) };
        viewport.Controls.Add(rows);
        scroll = Add(new ThinScrollBar { Bounds = R(503, 172, 10, ListH - 8) });
        scroll.ValueChanged += () => rows.Top = -scroll.Value;

        page = Add(new SmoothPanel { Bounds = R(0, 164, 520, 280), Visible = false });
        var game = SettingsRow(0, "Jogo", "Executável do VALORANT que o app vigia.");
        gamePath = game.Detail;
        var browse = RowButton("Procurar...", "Escolher o VALORANT.exe", ghost: true);
        var detect = RowButton("Detectar", "Achar o VALORANT sozinho", ghost: true);
        browse.Click += (_, _) => BrowseGameExe();
        detect.Click += (_, _) => SetGameExe(Game.FindExe());
        PlaceRight(game, browse, detect);
        var on = SettingsRow(1, $"{AppName} ligado", "Aplica a cfg em uso quando uma conta entra no Riot Client.");
        watcherSwitch = new ToggleSwitch();
        watcherSwitch.Toggled += ToggleWatcher;
        PlaceRight(on, watcherSwitch);
        var auto = SettingsRow(2, "Iniciar com o Windows", "Fica minimizado na bandeja, perto do relógio.");
        autoSwitch = new ToggleSwitch();
        autoSwitch.Toggled += () => { Autostart.Set(!Autostart.IsEnabled(), selfExe); RefreshSettings(); };
        PlaceRight(auto, autoSwitch);
        var data = SettingsRow(3, "Pasta de dados", vcs.DataDir);
        dataPath = data.Detail;
        var open = RowButton("Abrir", "Abrir a pasta no Explorer", ghost: true);
        open.Click += (_, _) => { Directory.CreateDirectory(vcs.DataDir); Process.Start("explorer.exe", $"\"{vcs.DataDir}\"")?.Dispose(); };
        PlaceRight(data, open);

        if (vcs.GameExe == "" && Game.FindExe() is { } found) vcs.GameExe = found;   // primeira vez: detecta sozinho
        if (Autostart.IsEnabled()) Autostart.Set(true, selfExe);                      // o exe pode ter sido movido de pasta
        gameRunning = Game.IsRunning(Game.ProcessNames(vcs.GameExe));
        RefreshList();
        RefreshView();
        Application.AddMessageFilter(this);   // clicar fora recolhe o ✓ (PreFilterMessage)
        // Sem botão "Usar cfg" (o dono tirou): abrir o app liga o watcher, a menos que tenha sido desligado no ☰.
        // Só depois de a janela aparecer: travar o construtor faz o Windows abrir a janela atrás das outras.
        Shown += (_, _) =>
        {
            if (vcs.Enabled && vcs.Profile() != null && File.Exists(vcs.GameExe) && !Watcher.IsRunning()) Watcher.Start(selfExe, wait: false);
        };
        var timer = new Timer { Interval = 3000, Enabled = true };   // status ao vivo (Riot Client, jogo, watcher)
        timer.Tick += (_, _) => RefreshView();
    }

    // Barra superior limpa (pedido do dono): o Windows não desenha ícone nem título ali, mas o Text continua
    // valendo para a barra de tarefas e o Alt+Tab. O logo fica ao lado do título grande (AppLogo).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var opts = new WtaOptions { Flags = 3, Mask = 3 };   // WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON
        SetWindowThemeAttribute(Handle, 1 /* WTA_NONCLIENT */, ref opts, Marshal.SizeOf(opts));
        // Windows 11: barra da mesma cor do app (branca), emendada no conteúdo. No Windows 10 é ignorado.
        var white = 0x00FFFFFF;   // COLORREF 0x00BBGGRR
        DwmSetWindowAttribute(Handle, 35 /* DWMWA_CAPTION_COLOR */, ref white, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    struct WtaOptions { public uint Flags, Mask; }

    [DllImport("uxtheme.dll")]
    static extern int SetWindowThemeAttribute(IntPtr hwnd, int attribute, ref WtaOptions options, int size);

    // Prancheta (logo.png, mesma imagem do app.ico, escolha do dono) ao lado do título.
    Panel AppLogo(int x, int y, int h)
    {
        var p = new Panel { Bounds = R(x, y, (int)Math.Ceiling(h * (float)Logo.Width / Logo.Height), h) };
        p.Paint += (_, e) =>
        {
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(Logo, 0, 0, p.Height * (float)Logo.Width / Logo.Height, p.Height);
        };
        return p;
    }

    // Logo "VALORANT" e prancheta (recortes dos PNGs do dono) EMBUTIDOS no exe: os colegas recebem só o exe.
    static readonly Image Brand = LoadPng("brand.png"), Logo = LoadPng("logo.png");

    static Image LoadPng(string name)
    {
        using var s = typeof(MainForm).Assembly.GetManifestResourceStream(name)!;
        using var img = Image.FromStream(s);
        return new Bitmap(img);   // cópia: Image.FromStream exige o stream aberto enquanto a imagem viver
    }

    // Título: logo + "CFG" com as letras exatamente da altura das do logo.
    Panel BrandTitle(int x, int y, int w, int h)
    {
        var p = new Panel { Bounds = R(x, y, w, h) };
        p.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float logoH = S(22), logoW = logoH * Brand.Width / Brand.Height, top = (p.Height - logoH) / 2;
            g.DrawImage(Brand, 0, top, logoW, logoH);

            using var font = Display(20f);
            using var cfg = new System.Drawing.Drawing2D.GraphicsPath();
            cfg.AddString("CFG", font.FontFamily, (int)font.Style, 100, PointF.Empty, StringFormat.GenericTypographic);
            var ink = cfg.GetBounds();
            var k = logoH / ink.Height;   // escala para a altura das letras do logo; espaço de ~40% da altura
            using var place = new System.Drawing.Drawing2D.Matrix(k, 0, 0, k, logoW + 0.4f * logoH - ink.X * k, top - ink.Y * k);
            cfg.Transform(place);
            using var brush = new SolidBrush(Ink);
            g.FillPath(brush, cfg);
        };
        return p;
    }

    int S(int v) => (int)Math.Round(v * scale);
    Rectangle R(int x, int y, int w, int h) => new(S(x), S(y), S(w), S(h));
    T Add<T>(T c) where T : Control { Controls.Add(c); return c; }

    Label IconLabel(string glyph, int x, int y, string tip, float size = 15f)
    {
        // Tag = glifo desenhado: trocar o Tag (e Invalidate) troca o ícone (ex.: ⚙️ vira X com o menu aberto).
        var l = new Label { Font = new Font(IconFont, size), ForeColor = Ink, Bounds = R(x, y, 36, 34), Cursor = Cursors.Hand, Tag = glyph };
        // Centraliza pelo desenho do glifo, não pela caixa da fonte (que deixa lápis/lixeira mais altos que as
        // setas desenhadas). Antisserrilhado em cinza, igual ao dos ícones desenhados.
        if (glyph != "") l.Paint += (_, e) =>
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddString((string)l.Tag, l.Font.FontFamily, (int)l.Font.Style, l.Font.SizeInPoints * l.DeviceDpi / 72f, PointF.Empty, StringFormat.GenericTypographic);
            var ink = path.GetBounds();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform(l.Width / 2f - ink.X - ink.Width / 2f, l.Height / 2f - ink.Y - ink.Height / 2f);
            using var brush = new SolidBrush(l.ForeColor);
            e.Graphics.FillPath(brush, path);
        };
        tips.SetToolTip(l, tip);
        return l;
    }

    void SetGlyph(Label icon, string glyph, string tip)
    {
        icon.Tag = glyph;
        icon.Invalidate();
        tips.SetToolTip(icon, tip);
    }

    const string GearTip = "Exportar, importar ou salvar a cfg da conta logada nesta cfg";

    // Exportar e importar: os ícones Material "Export"/"Import" (Apache 2.0) que o dono escolheu, redesenhados em
    // vetor na grade 48x48 deles, com o traço fino dos outros ícones. Caixa aberta no canto de cima e seta curva
    // saindo (exportar) ou entrando (importar).
    Label BoxArrowIcon(int x, int y, string tip, bool arrowIn)
    {
        var l = IconLabel("", x, y, tip);
        l.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // A caixa (unidades 8 a 40) mede S(17)px, e o desenho todo ~19px, a altura do lápis e da lixeira.
            // As bordas caem no meio de um pixel: traço nítido, sem virar 2px cinza.
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;   // centro do pixel em .5
            float box = S(17), k = box / 32;
            float Snap(float center) => (float)Math.Floor(center - box / 2) + 0.5f + box / 2;
            g.TranslateTransform(Snap(l.Width / 2f), Snap(l.Height / 2f));
            g.ScaleTransform(k, k);
            g.TranslateTransform(-24, -24);
            using var pen = new Pen(Ink, 1.3f * S(1) / k) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddLines(new PointF[] { new(20, 8), new(8, 8), new(8, 40), new(40, 40), new(40, arrowIn ? 15 : 24) });
            path.StartFigure();
            if (arrowIn)
            {
                path.AddLine(40, 8, 30, 8);
                path.AddArc(24, 8, 12, 12, 270, -90);
                path.AddLine(24, 14, 24, 27);
                path.StartFigure();
                path.AddLines(new PointF[] { new(18, 21), new(24, 27), new(30, 21) });
            }
            else
            {
                path.AddLine(24, 28, 24, 18);
                path.AddArc(24, 12, 12, 12, 180, 90);
                path.AddLine(30, 12, 41, 12);
                path.StartFigure();
                path.AddLines(new PointF[] { new(35, 6), new(41, 12), new(35, 18) });
            }
            g.DrawPath(pen, path);
        };
        return l;
    }

    // ---------- cartão: status ao vivo, avisos e perguntas. Nada de popup (pedido do dono). ----------

    Button CardButton(int x, Color back, Color fore)
    {
        var b = new Button { Bounds = R(x, 26, 84, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = new Font(UiBold, 10f), Visible = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    void SetCard(string title, string detail, Tone tone)
    {
        var (mark, color) = tone switch
        {
            Tone.Ok    => ("✓ ", Color.FromArgb(22, 163, 74)),
            Tone.Warn  => ("", Color.FromArgb(217, 119, 6)),
            Tone.Alert => ("⚠ ", Accent),
            _          => ("○ ", Muted),
        };
        statusTitle.Text = mark + title;
        statusTitle.ForeColor = cardBar.BackColor = color;
        statusDetail.Text = detail;
        card.BackColor = Color.FromArgb(18, color.R, color.G, color.B).BlendOnWhite();
        CenterCardText();
    }

    // Título + detalhe centralizados na vertical do cartão (que tem altura para 3 linhas de pergunta).
    void CenterCardText()
    {
        var titleH = TextRenderer.MeasureText(statusTitle.Text, statusTitle.Font).Height;
        var detailH = statusDetail.Text == "" ? 0
            : TextRenderer.MeasureText(statusDetail.Text, statusDetail.Font, new Size(statusDetail.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
        var gap = detailH == 0 ? 0 : S(2);
        statusTitle.Bounds = new Rectangle(statusTitle.Left, Math.Max(0, (card.Height - titleH - gap - detailH) / 2), statusTitle.Width, titleH);
        statusDetail.Bounds = new Rectangle(statusDetail.Left, statusTitle.Bottom + gap, statusDetail.Width, Math.Max(detailH, 1));
    }

    // Aviso passageiro no cartão; depois volta o status ao vivo.
    void Flash(string title, string detail = "", Tone tone = Tone.Ok, int ms = 4000)
    {
        HideQuestion();
        cardBusy = true;
        SetCard(title, detail, tone);
        flashTimer.Stop();
        flashTimer.Interval = ms;
        flashTimer.Start();
    }

    // Pergunta no próprio cartão, com dois botões nele. Fica até responder (ou outra ação tomar o lugar).
    // Os botões crescem conforme o texto; o texto da pergunta ocupa o que sobra à esquerda.
    void Ask(string title, string detail, string yes, Action action, string no = "Cancelar", Action? noAction = null,
             string? yesTip = null, string? noTip = null)
    {
        flashTimer.Stop();
        cardBusy = true;
        SetCard(title, detail, Tone.Warn);
        yesButton.Text = yes;
        noButton.Text = no;
        foreach (var b in new[] { yesButton, noButton }) b.Width = Math.Max(S(84), TextRenderer.MeasureText(b.Text, b.Font).Width + S(22));
        noButton.Left = card.Width - S(12) - noButton.Width;
        yesButton.Left = noButton.Left - S(8) - yesButton.Width;
        statusTitle.Width = statusDetail.Width = yesButton.Left - S(24);
        CenterCardText();   // a largura mudou: o detalhe pode ter quebrado em mais linhas
        tips.SetToolTip(yesButton, yesTip);
        tips.SetToolTip(noButton, noTip);
        onYes = action;
        onNo = noAction;
        yesButton.Visible = noButton.Visible = true;
    }

    void HideQuestion()
    {
        yesButton.Visible = noButton.Visible = false;
        statusTitle.Width = statusDetail.Width = S(460);
        onYes = onNo = null;
    }

    // Mudou configurações dentro do jogo: o cartão pergunta o que fazer (o dono pediu; texto curto e claro).
    void AskSessionChanges((string Puuid, string Profile) ch) =>
        Ask("Você mudou a cfg no jogo",
            $"Guardar na cfg \"{ch.Profile}\" o que você mudou, ou deixá-la como era antes de abrir o jogo?",
            "Manter do jogo", () => { vcs.KeepSessionChanges(ch.Puuid); Flash($"Cfg \"{ch.Profile}\" atualizada", "As mudanças feitas no jogo foram guardadas."); },
            "Manter original", () => { vcs.DiscardSessionChanges(ch.Puuid); Flash($"Cfg \"{ch.Profile}\" mantida", "Na próxima vez que abrir o jogo, ela volta como era."); },
            $"Salva na cfg \"{ch.Profile}\" o que você mudou no jogo (sensibilidade, teclas, mira...).",
            "Descarta o que mudou no jogo: a cfg continua como estava antes de abrir o jogo.");

    void EndCard()
    {
        HideQuestion();
        flashTimer.Stop();
        cardBusy = false;
        RefreshView();
    }

    // Erros das ações (ex.: "Abra o Riot Client...") também aparecem no cartão.
    public void ShowError(Exception ex) =>
        Flash("Não deu certo", ex is VcsException ? ex.Message : "Erro inesperado: " + ex.Message, Tone.Alert, 7000);

    // ---------- lista de cfgs ----------

    void RefreshList()
    {
        editing = false;
        revealed = null;
        tools = null;
        lockables.Clear();
        var shown = viewport.IsHandleCreated;   // no construtor ainda não há janela para congelar
        if (shown) SendMessage(viewport.Handle, 0x000B /* WM_SETREDRAW */, IntPtr.Zero, IntPtr.Zero);   // sem isso a lista pisca vazia
        rows.SuspendLayout();
        rows.Controls.Clear();
        var y = 0;
        foreach (var name in vcs.ProfileNames()) rows.Controls.Add(ProfileRow(name, y++));
        rows.Controls.Add(NewRow(y++));
        rows.Height = S(RowH) * y;
        rows.ResumeLayout();
        scroll.Setup(rows.Height, viewport.Height);
        rows.Top = -scroll.Value;
        ApplyLock();
        if (!shown) return;
        SendMessage(viewport.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
        viewport.Refresh();   // redesenha tudo de uma vez
    }

    // Só repinta (não recria a lista), para não perder o que estiver sendo digitado.
    void ApplyLock()
    {
        if (gameRunning) CancelReveal(animated: false);
        foreach (var (label, trash, active) in lockables)
        {
            label.ForeColor = gameRunning ? Disabled : Ink;
            label.Cursor = gameRunning ? Cursors.Default : Cursors.Hand;
            tips.SetToolTip(label, gameRunning ? "Feche o VALORANT para trocar de cfg" : active ? "Cfg em uso" : "Clique para usar esta cfg");
            if (trash == null) continue;
            trash.ForeColor = gameRunning ? Disabled : Ink;
            trash.Cursor = gameRunning ? Cursors.Default : Cursors.Hand;
            tips.SetToolTip(trash, gameRunning ? "Feche o VALORANT para excluir a cfg em uso" : "Excluir esta cfg");
            trash.Invalidate();
        }
    }

    SmoothPanel Row(int index)
    {
        var row = new SmoothPanel { Bounds = R(0, RowH * index, ListW, RowH) };
        row.Controls.Add(new Panel { BackColor = Accent, Bounds = R(0, RowH - 3, ListW, 2) });
        return row;
    }

    static void WheelAll(Control parent, MouseEventHandler h)
    {
        parent.MouseWheel += h;
        foreach (Control c in parent.Controls) c.MouseWheel += h;
    }

    // Ascendente como o GDI (que desenha os Labels) a vê. As métricas do GDI+ (FontFamily.GetCellAscent) não
    // batem na Bahnschrift nem na VALORANT, e erravam a linha de base em ~4px.
    static int Ascent(Font f)
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var hdc = g.GetHdc();
        var hfont = f.ToHfont();
        var old = SelectObject(hdc, hfont);
        var tm = new byte[64];   // TEXTMETRICW: tmHeight, tmAscent, ...
        GetTextMetricsW(hdc, tm);
        SelectObject(hdc, old);
        DeleteObject(hfont);
        g.ReleaseHdc(hdc);
        return BitConverter.ToInt32(tm, 4);
    }

    // Mesmo topo do texto dos nomes (centralizado em label de 36 a partir de y=8), para as linhas de base baterem.
    TextBox NameBox(string text, int width)
    {
        var font = new Font(Ui, SmallName);
        var top = S(8) + (S(36) - TextRenderer.MeasureText("Ag", font).Height) / 2;
        return new() { Text = text, BorderStyle = BorderStyle.None, Font = font, ForeColor = Ink, Bounds = new Rectangle(S(6), top, S(width), S(30)) };
    }

    Panel ProfileRow(string name, int index)
    {
        var row = Row(index);
        var active = name == vcs.ActiveProfile;
        var label = new Label { Text = name, ForeColor = Ink, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Bounds = R(4, 8, 260, 36), Cursor = Cursors.Hand };
        SetNameFont(label, active ? BigName : SmallName, bold: active);
        var check = CheckButton(remove: active);   // na cfg em uso, o quadrado tem X: tira a cfg de uso
        label.Click += (_, _) => { if (!gameRunning) ToggleReveal(name, check, label); };
        row.Controls.Add(check);
        row.Controls.Add(label);
        if (active)
        {
            // "EM USO" na mesma linha de base do nome (o nome é centralizado na vertical dentro do label).
            var tagFont = HasValorantFont ? Display(7.5f) : new Font(Ui, 8.5f);
            var baseline = label.Top + (label.Height - TextRenderer.MeasureText(name, label.Font).Height) / 2f + Ascent(label.Font);
            var tag = new Label { Text = Caps("em uso"), ForeColor = Accent, Font = tagFont, AutoSize = true, Location = new Point(label.Right + S(8), (int)(baseline - Ascent(tagFont))) };
            label.LocationChanged += (_, _) => tag.Left = label.Right + S(8);   // acompanha o nome quando o X o empurra
            row.Controls.Add(tag);
        }

        var delete = IconLabel(GlyphDelete, 394, 9, "Excluir esta cfg");
        delete.Click += (_, _) => { if (!(gameRunning && active)) DeleteClick(name); };   // excluir a em uso = trocar de cfg
        lockables.Add((label, active ? delete : null, active));
        var gear = IconLabel(GlyphGear, 434, 9, GearTip);
        gear.Click += (_, _) => ToggleTools(row, name, delete, gear);
        var icons = new Control[] { delete, gear };
        // Renomear só por dois cliques no nome (o dono separou: renomear nunca mexe no conteúdo). O 1º clique abre
        // o ✓, que desliza para baixo do mouse: um clique nele dentro do tempo de clique duplo é o 2º clique.
        void EditByDoubleClick() { CancelReveal(animated: false); StartRename(row, name, icons); }
        label.DoubleClick += (_, _) => EditByDoubleClick();
        check.Click += (_, _) =>
        {
            if (Environment.TickCount - revealedAt < SystemInformation.DoubleClickTime) EditByDoubleClick();
            else if (!gameRunning) ConfirmReveal(name, check, label);
        };
        row.Controls.AddRange(icons);
        WheelAll(row, OnWheel);
        return row;
    }

    // ---------- escolher a cfg: check animado ----------

    void SetNameFont(Label label, float size, bool bold)
    {
        label.Font = new Font(bold ? UiBold : Ui, size);
        label.Width = Math.Min(S(260), TextRenderer.MeasureText(label.Text, label.Font).Width + S(4));
    }

    // Quadrado vermelho com check branco (ou X, na cfg em uso). Começa com largura 0; o desenho acompanha a
    // borda direita, então desliza para dentro junto com o quadrado.
    Panel CheckButton(bool remove)
    {
        var p = new Panel { Bounds = R(0, 8, 0, 36), BackColor = Accent, Visible = false, Cursor = Cursors.Hand };
        tips.SetToolTip(p, remove ? "Tirar esta cfg de uso: o VALORANT abre com a cfg original de cada conta" : "Usar esta cfg");
        p.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float u = S(1), cx = p.Width - S(CheckW) / 2f, cy = p.Height / 2f;
            using var pen = new Pen(Color.White, 2.4f * u) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            if (remove)
            {
                e.Graphics.DrawLine(pen, cx - 5 * u, cy - 5 * u, cx + 5 * u, cy + 5 * u);
                e.Graphics.DrawLine(pen, cx - 5 * u, cy + 5 * u, cx + 5 * u, cy - 5 * u);
            }
            else e.Graphics.DrawLines(pen, new[] { new PointF(cx - 6 * u, cy), new PointF(cx - 2 * u, cy + 4 * u), new PointF(cx + 6 * u, cy - 4 * u) });
        };
        return p;
    }

    // Animação curta com desaceleração (ease-out). Para sozinha se a linha for recriada no meio.
    void Animate(Control owner, Action<float> step, Action? done = null)
    {
        var timer = new Timer { Interval = 15 };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (owner.IsDisposed) { timer.Dispose(); return; }
            var p = Math.Min(1f, clock.ElapsedMilliseconds / 200f);
            step(1 - (1 - p) * (1 - p) * (1 - p));
            if (p < 1) return;
            timer.Dispose();
            done?.Invoke();
        };
        timer.Start();
    }

    // e: 0 = recolhido (nome na origem), 1 = check aberto (nome empurrado).
    void PlaceReveal(Panel check, Label label, float e)
    {
        check.Width = (int)(S(CheckW) * e);
        check.Visible = check.Width > 0;
        check.Invalidate();
        label.Left = S(4) + (int)(S(NameShift) * e);
    }

    void ToggleReveal(string name, Panel check, Label label)
    {
        var same = revealed?.Name == name;
        CancelReveal(animated: true);
        if (same) return;   // clicar de novo no mesmo nome recolhe
        revealed = (name, check, label);
        revealedAt = Environment.TickCount;
        var grow = name != vcs.ActiveProfile;   // a cfg em uso já está com a fonte grande
        Animate(label, e =>
        {
            if (revealed?.Check != check) return;   // recolhido no meio (ex.: clique duplo): não reabre
            PlaceReveal(check, label, e);
            if (grow) SetNameFont(label, SmallName + (BigName - SmallName) * e, bold: true);
        });
    }

    // Recolhe sem escolher: o nome volta para a origem e para a fonte normal.
    void CancelReveal(bool animated)
    {
        if (revealed is not { } r) return;
        revealed = null;
        var shrink = r.Name != vcs.ActiveProfile;
        void Step(float e)
        {
            PlaceReveal(r.Check, r.Label, 1 - e);
            if (shrink) SetNameFont(r.Label, BigName - (BigName - SmallName) * e, bold: e < 1);
        }
        if (animated) Animate(r.Label, Step); else Step(1);
    }

    // Confirma: o check se fecha, o nome volta para a origem mantendo a fonte grande e aparece "em uso".
    // Na cfg em uso (X): ela sai de uso, e quem já tinha recebido a cfg volta à original (Vcs.Step).
    void ConfirmReveal(string name, Panel check, Label label)
    {
        revealed = null;
        var remove = name == vcs.ActiveProfile;
        vcs.ActiveProfile = remove ? null : name;
        Animate(label, e => PlaceReveal(check, label, 1 - e), () =>
        {
            RefreshList();
            if (remove) { vcs.Log($"cfg \"{name}\" tirada de uso"); RefreshView(); }
            else StartWatcher();
        });
    }

    Panel NewRow(int index)
    {
        var row = Row(index);
        newName = NameBox("", 412);
        newName.HandleCreated += (s, _) => SendMessage(((TextBox)s).Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Nova CFG");
        newName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveClick(); } };
        tips.SetToolTip(newName, "Digite um nome e aperte Enter para salvar a cfg da conta logada agora no Riot Client.");
        var import = BoxArrowIcon(434, 9, "Importar uma cfg de um arquivo", arrowIn: true);
        import.Click += (_, _) => ImportClick();
        row.Controls.AddRange(new Control[] { newName, import });
        WheelAll(row, OnWheel);
        return row;
    }

    // Botão vermelho de texto dentro da linha. Label, não Button: não pega o foco, então clicar nele não
    // dispara o LostFocus da caixa de nome (que cancela a edição) antes do clique.
    // ghost: cinza, para ações secundárias (as da página de configurações).
    Label RowButton(string text, string tip, bool ghost = false)
    {
        var b = new Label { Text = text, Font = new Font(UiBold, 9.5f), ForeColor = ghost ? Ink : Color.White, BackColor = ghost ? Color.FromArgb(229, 231, 235) : Accent, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
        b.Size = new Size(TextRenderer.MeasureText(text, b.Font).Width + S(20), S(28));
        b.Top = S(12);
        tips.SetToolTip(b, tip);
        return b;
    }

    // ⚙️: exportar, importar e "Salvar CFG" saem de trás da engrenagem, deslizando para a esquerda; a lixeira some
    // enquanto isso. A engrenagem de novo, clicar fora (PreFilterMessage) ou usar uma das ações recolhe.
    void ToggleTools(Panel row, string name, Control delete, Label gear)
    {
        if (tools is { } open) { open.Close(); if (open.Inside[0] == gear) return; }
        var export = BoxArrowIcon(434, 9, "Exportar esta cfg para um arquivo", arrowIn: false);
        var import = BoxArrowIcon(434, 9, $"Importar um arquivo para a cfg \"{name}\" (substitui o conteúdo dela)", arrowIn: true);
        var save = RowButton("Salvar CFG", $"Salva na cfg \"{name}\" a cfg da conta logada agora no Riot Client (substitui o conteúdo dela).");
        var items = new Control[] { export, import, save };
        // Os itens andam dentro de uma faixa que termina na borda da engrenagem e corta o que passa dela: saem
        // "de trás" dela sem vazar (o Salvar CFG é mais largo que a engrenagem e aparecia dos dois lados).
        int saveAt = S(354) - S(6) - save.Width;
        var clip = new Panel { Bounds = new Rectangle(saveAt, S(9), gear.Left - saveAt, S(34)) };
        foreach (var c in items) c.Top -= clip.Top;
        var targets = new[] { S(394), S(354), saveAt }.Select(x => x - saveAt).ToArray();
        void Place(float e) { for (var i = 0; i < items.Length; i++) items[i].Left = clip.Width + (int)((targets[i] - clip.Width) * e); }
        Place(0);
        clip.Controls.AddRange(items);
        row.Controls.Add(clip);
        clip.BringToFront();   // por cima do nome, se ele for comprido
        delete.Visible = false;
        SetGlyph(gear, GlyphCancel, "Fechar");   // com o menu aberto, a engrenagem vira X
        Animate(row, Place);
        void Close()
        {
            tools = null;
            SetGlyph(gear, GlyphGear, GearTip);
            Animate(row, e => Place(1 - e), () =>
            {
                row.Controls.Remove(clip);
                clip.Dispose();   // leva os itens junto
                delete.Visible = true;
            });
        }
        tools = (new Control[] { gear, clip, export, import, save }, Close);
        export.Click += (_, _) => { Close(); ExportClick(name); };
        import.Click += (_, _) => { Close(); ImportInto(name); };
        save.Click += (_, _) =>
        {
            Close();
            Ask($"Substituir a cfg \"{name}\"?", "Ela passa a ser a cfg da conta logada agora no Riot Client.", "Substituir", () => SaveCurrent(name));
        };
    }

    // Troca o nome por uma caixa de texto na própria linha. Enter ou "Salvar nome" salva; Esc, o X ou clicar
    // fora cancela. Os ícones da linha deslizam para fora pela direita e entram o "Salvar nome" e o X.
    void StartRename(Panel row, string name, Control[] icons)
    {
        if (editing) return;
        editing = true;
        var hidden = row.Controls.OfType<Label>().Where(c => c.Visible && Array.IndexOf(icons, c) < 0).ToArray();   // nome e "em uso"
        foreach (var c in hidden) c.Visible = false;
        var box = NameBox(name, 280);
        var cancel = IconLabel(GlyphCancel, ListW, 9, "Cancelar (Esc)");
        var update = RowButton("Salvar nome", "Salva só o nome novo (Enter). A cfg não muda.");
        int cancelAt = S(434), updateAt = cancelAt - S(6) - update.Width;
        update.Left = row.Width;
        row.Controls.AddRange(new Control[] { box, update, cancel });
        var starts = icons.Select(c => c.Left).ToArray();
        Animate(row, e =>
        {
            for (var i = 0; i < icons.Length; i++) icons[i].Left = starts[i] + (int)((row.Width - starts[0]) * e);
            cancel.Left = cancelAt + (int)((row.Width - cancelAt) * (1 - e));
            update.Left = updateAt + (int)((row.Width - updateAt) * (1 - e));
        });
        box.Focus();
        box.SelectAll();
        var done = false;
        // Sai com a animação inversa, sem recriar a lista (recriar pisca). Só um nome novo recria, para reordenar.
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            var renamed = false;
            try { if (save && box.Text.Trim() != name) { vcs.RenameProfile(name, box.Text); renamed = true; } }
            catch (VcsException ex) { BeginInvoke(new Action(() => ShowError(ex))); }   // nome vazio ou repetido: avisa e cancela
            BeginInvoke(new Action(() =>   // não mexe na caixa dentro do próprio evento dela
            {
                box.Visible = false;
                foreach (var c in hidden) c.Visible = true;
                Animate(row, e =>
                {
                    for (var i = 0; i < icons.Length; i++) icons[i].Left = starts[i] + (int)((row.Width - starts[0]) * (1 - e));
                    cancel.Left = cancelAt + (int)((row.Width - cancelAt) * e);
                    update.Left = updateAt + (int)((row.Width - updateAt) * e);
                }, () =>
                {
                    foreach (var c in new Control[] { box, update, cancel }) { row.Controls.Remove(c); c.Dispose(); }
                    editing = false;
                    if (renamed) RefreshList();
                    RefreshView();
                });
            }));
        }
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
        };
        box.LostFocus += (_, _) => Finish(false);
        cancel.Click += (_, _) => Finish(false);   // Label não pega foco: o clique não dispara o LostFocus antes
        update.Click += (_, _) => Finish(true);
    }

    void OnWheel(object? sender, MouseEventArgs e) => scroll.Value -= Math.Sign(e.Delta) * S(RowH);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool GetTextMetricsW(IntPtr hdc, byte[] tm);

    // ---------- status ----------

    void RefreshView()
    {
        var running = Watcher.IsRunning();
        var game = Game.IsRunning(Game.ProcessNames(vcs.GameExe));
        if (game != gameRunning) { gameRunning = game; ApplyLock(); }
        if (settingsOpen) RefreshSettings();
        if (cardBusy) { }
        else if (!game && vcs.SessionChanges() is { } changes) AskSessionChanges(changes);   // pergunta até responder
        else
        {
            var (title, detail, tone) = vcs.Status(game, running);
            SetCard(title, detail, tone);
        }
    }

    // ---------- ☰: página de configurações (tudo que é extra) ----------

    // Linha da página: título, detalhe cinza e o sublinhado vermelho da lista. Os controles vão à direita (PlaceRight).
    (int Top, Label Detail) SettingsRow(int index, string title, string detail)
    {
        var top = S(8 + index * 66);
        page.Controls.Add(new Label { Text = title, Font = new Font(UiBold, 11f), ForeColor = Ink, AutoSize = true, Location = new Point(S(24), top + S(10)) });
        var d = new Label { Text = detail, ForeColor = Muted, AutoEllipsis = true, Bounds = new Rectangle(S(24), top + S(34), S(300), S(20)) };
        page.Controls.Add(d);
        page.Controls.Add(new Panel { BackColor = Accent, Bounds = new Rectangle(S(24), top + S(62), S(ListW), S(2)) });
        return (top, d);
    }

    // Da direita para a esquerda, terminando onde termina o sublinhado, centralizados na altura da linha. O
    // detalhe vai até eles (texto comprido termina em "...").
    void PlaceRight((int Top, Label Detail) row, params Control[] controls)
    {
        var right = S(24 + ListW);
        foreach (var c in controls)
        {
            c.Location = new Point(right - c.Width, row.Top + (S(62) - c.Height) / 2);
            right = c.Left - S(8);
            page.Controls.Add(c);
        }
        row.Detail.Width = right - S(4) - row.Detail.Left;
    }

    void ShowSettings(bool open)
    {
        if (open == settingsOpen) return;
        settingsOpen = open;
        menuButton.Visible = !open;
        backButton.Visible = open;
        var w = ClientSize.Width;
        if (open)
        {
            RefreshSettings();
            page.Left = w;
            page.Visible = true;
            page.BringToFront();
            ActiveControl = null;   // Enter não pode ir para a "Nova CFG" escondida
        }
        // SWP_NOCOPYBITS: sem isso o Windows reaproveita os pixels antigos ao mover e aparecem pedaços repetidos
        void Move(float x) => SetWindowPos(page.Handle, IntPtr.Zero, (int)x, page.Top, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0100);
        Animate(page, e => Move(open ? w * (1 - e) : w * e), () => { if (!settingsOpen) page.Visible = false; });
    }

    // Estado real (não o último clique): o watcher pode ter sido desligado pela bandeja, o exe pode ter mudado.
    void RefreshSettings()
    {
        gamePath.Text = File.Exists(vcs.GameExe) ? vcs.GameExe : "Não encontrado: use Detectar ou Procurar.";
        gamePath.ForeColor = File.Exists(vcs.GameExe) ? Muted : Accent;
        dataPath.Text = vcs.DataDir;
        watcherSwitch.On = Watcher.IsRunning();
        autoSwitch.On = Autostart.IsEnabled();
        tips.SetToolTip(gamePath, gamePath.Text);
        tips.SetToolTip(dataPath, dataPath.Text);
    }

    void SetGameExe(string? exe)
    {
        if (exe == null) { Flash("VALORANT não encontrado", "Use ☰ → Jogo → Procurar... e escolha o VALORANT.exe.", Tone.Warn); return; }
        vcs.GameExe = exe;
        if (Watcher.IsRunning()) { Watcher.Stop(); Watcher.Start(selfExe); }   // o watcher lê o executável ao iniciar
        Flash("Jogo definido", exe);
        RefreshSettings();
    }

    void BrowseGameExe()
    {
        using var dlg = new OpenFileDialog { Title = "Executável do VALORANT", Filter = "Executável (*.exe)|*.exe" };
        if (File.Exists(vcs.GameExe)) dlg.InitialDirectory = Path.GetDirectoryName(vcs.GameExe);
        if (dlg.ShowDialog(this) == DialogResult.OK) SetGameExe(dlg.FileName);
    }

    // ---------- ações da lista ----------

    // Se o nome já existe, pergunta no cartão antes; senão faz direto.
    void IfFreeOrConfirmed(string name, string what, Action action)
    {
        if (vcs.Profile(name) == null) action();
        else Ask($"Já existe a cfg \"{name}\"", $"Substituir {what}?", "Substituir", action);
    }

    void SaveClick()
    {
        var name = newName!.Text.Trim();
        if (name == "") { Flash("Digite um nome", "Escreva o nome em \"Nova CFG\" e aperte Enter.", Tone.Warn); newName.Focus(); return; }
        IfFreeOrConfirmed(name, "pela cfg da conta logada agora", () => SaveCurrent(name));
    }

    // Salva a cfg da conta logada agora com esse nome ("Nova CFG" e o "Salvar atual" do modo edição).
    void SaveCurrent(string name)
    {
        vcs.SaveProfileFromCurrent(name);
        RefreshList();
        Flash($"Cfg \"{name}\" salva", Game.IsRunning(Game.ProcessNames(vcs.GameExe))
            ? "O jogo está aberto: os gráficos só vão para o disco quando ele fecha. Salve de novo depois para incluí-los."
            : "");
    }

    void ImportClick()
    {
        using var dlg = new OpenFileDialog { Title = "Importar cfg", Filter = $"cfg {AppName} (*.json)|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var file = dlg.FileName;
        var name = vcs.ImportName(file, newName!.Text);
        IfFreeOrConfirmed(name, "pela cfg do arquivo", () =>
        {
            vcs.Import(file, name);
            RefreshList();
            Flash($"Cfg \"{name}\" importada");
        });
    }

    // ⚙️ → importar: carrega um arquivo nesta cfg (substitui o conteúdo, mantém o nome).
    void ImportInto(string name)
    {
        using var dlg = new OpenFileDialog { Title = $"Importar para a cfg \"{name}\"", Filter = $"cfg {AppName} (*.json)|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var file = dlg.FileName;
        Ask($"Substituir a cfg \"{name}\"?", "Ela passa a ser a cfg do arquivo escolhido.", "Substituir", () =>
        {
            vcs.Import(file, name);
            RefreshList();
            Flash($"Cfg \"{name}\" importada");
        });
    }

    void ExportClick(string name)
    {
        using var dlg = new SaveFileDialog { Title = "Exportar cfg", Filter = $"cfg {AppName} (*.json)|*.json", FileName = name + ".json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        vcs.Export(name, dlg.FileName);
        Flash($"Cfg \"{name}\" exportada", Path.GetFileName(dlg.FileName));
    }

    void DeleteClick(string name) =>
        Ask($"Excluir a cfg \"{name}\"?", "Não mexe em nenhuma conta; só apaga a cfg salva.", "Excluir", () =>
        {
            // o jogo pode ter aberto enquanto a pergunta estava na tela
            if (gameRunning && name == vcs.ActiveProfile) { Flash("Feche o VALORANT", "A cfg em uso não pode ser excluída com o jogo aberto.", Tone.Warn); return; }
            vcs.DeleteProfile(name);
            RefreshList();
        });

    // ☰ → Ligar/Desligar. Desligar é lembrado (abrir o app não religa).
    void ToggleWatcher()
    {
        if (!Watcher.IsRunning()) { StartWatcher(); return; }
        void Stop() { vcs.Enabled = false; Watcher.Stop(); vcs.Log("watcher desligado pelo menu"); RefreshView(); }
        var pending = vcs.PendingCount();
        if (pending == 0) Stop();
        else Ask($"{pending} conta(s) esperando a original", $"Ela só volta com o {AppName} ligado. Desligar mesmo assim?", "Desligar", Stop);
    }

    // Chamado ao confirmar uma cfg no ✓ e por ☰ → Ligar.
    void StartWatcher()
    {
        vcs.Enabled = true;
        if (vcs.Profile() == null) { Flash("Escolha uma cfg", "Clique no nome de uma cfg da lista e depois no ✓ (ou crie uma em \"Nova CFG\").", Tone.Warn); return; }
        if (!File.Exists(vcs.GameExe))
        {
            if (Game.FindExe() is { } found) vcs.GameExe = found;
            else { Flash("VALORANT não encontrado", "Defina o executável no menu ☰.", Tone.Warn); return; }
        }
        if (!Watcher.IsRunning()) { Watcher.Start(selfExe); vcs.Log("watcher ligado pelo menu"); }
        RefreshView();
    }
}

// Barra de rolagem fina no estilo do desenho: contorno e polegar na cor de destaque.
// Linha da lista: WS_EX_COMPOSITED pinta a linha inteira (com os filhos) de uma vez, em buffer. Sem isso, os
// controles que deslizam nas animações rasgam e deixam rastro, porque cada um se redesenha separado.
sealed class SmoothPanel : Panel
{
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }
    }
}

// Chave liga/desliga da página de configurações. O clique só avisa (Toggled); quem decide o estado é o dono,
// que depois põe On com o estado real (ex.: desligar pode pedir confirmação no cartão antes). A bolinha desliza.
sealed class ToggleSwitch : Control
{
    bool on;
    float knob;   // 0 = desligada, 1 = ligada
    readonly Timer timer = new() { Interval = 15 };
    public event Action? Toggled;

    public ToggleSwitch()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Size = new Size((int)(44 * DeviceDpi / 96f), (int)(24 * DeviceDpi / 96f));
        timer.Tick += (_, _) =>
        {
            var target = on ? 1f : 0f;
            knob = Math.Abs(target - knob) < 0.2f ? target : knob + Math.Sign(target - knob) * 0.2f;
            if (knob == target) timer.Stop();
            Invalidate();
        };
    }

    public bool On
    {
        get => on;
        set
        {
            if (on == value) return;
            on = value;
            if (IsHandleCreated && Visible) timer.Start(); else { knob = on ? 1 : 0; Invalidate(); }
        }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Toggled?.Invoke(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.White);
        float h = Height - 1, w = Width - 1;
        using var track = new System.Drawing.Drawing2D.GraphicsPath();
        track.AddArc(0, 0, h, h, 90, 180);
        track.AddArc(w - h, 0, h, h, 270, 180);
        track.CloseFigure();
        Color off = Color.FromArgb(209, 213, 219), lit = MainForm.Accent;
        using var fill = new SolidBrush(Color.FromArgb((int)(off.R + (lit.R - off.R) * knob), (int)(off.G + (lit.G - off.G) * knob), (int)(off.B + (lit.B - off.B) * knob)));
        g.FillPath(fill, track);
        float pad = h * 0.14f, d = h - 2 * pad;
        g.FillEllipse(Brushes.White, pad + knob * (w - h), pad, d, d);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

sealed class ThinScrollBar : Control
{
    int max, page, pos, dragOffset = -1;
    public event Action? ValueChanged;

    public ThinScrollBar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public void Setup(int content, int view)
    {
        max = Math.Max(0, content - view);
        page = view;
        Visible = max > 0;
        Value = pos;   // reajusta ao novo máximo
        Invalidate();
    }

    public int Value
    {
        get => pos;
        set
        {
            var v = Math.Max(0, Math.Min(max, value));
            if (v == pos) return;
            pos = v;
            Invalidate();
            ValueChanged?.Invoke();
        }
    }

    Rectangle Thumb
    {
        get
        {
            var h = Math.Max(Width * 2, Height * page / Math.Max(1, page + max));
            return new Rectangle(0, max == 0 ? 0 : (Height - h) * pos / max, Width, h);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new SolidBrush(MainForm.Accent);
        using var pen = new Pen(MainForm.Accent);
        e.Graphics.FillRectangle(brush, Thumb);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var t = Thumb;
        if (t.Contains(e.Location)) dragOffset = e.Y - t.Y;
        else Value += e.Y < t.Y ? -page : page;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var range = Height - Thumb.Height;
        if (dragOffset >= 0 && range > 0) Value = (e.Y - dragOffset) * max / range;
    }

    protected override void OnMouseUp(MouseEventArgs e) => dragOffset = -1;
}

static class ColorExt
{
    // Mistura uma cor translúcida sobre branco (WinForms não pinta fundo translúcido em Panel).
    public static Color BlendOnWhite(this Color c)
    {
        int Mix(int v) => 255 - (255 - v) * c.A / 255;
        return Color.FromArgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }
}
