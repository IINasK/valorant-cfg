// Aba Miras: guarda códigos de mira do VALORANT, mostra como a mira fica e copia o código para colar no jogo
// (Configurações → Mira → importar código de perfil). Não injeta nada no jogo. Lógica em Crosshair.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ValCfgSync;

public sealed partial class MainForm
{
    Label tabCfgs = null!, tabMiras = null!;
    Panel tabLine = null!, crossPage = null!, crossPreview = null!, crossViewport = null!, crossRows = null!;
    ThinScrollBar crossScroll = null!;
    TextBox? crossBox;
    List<(string Name, string Code)> crossList = new();
    string? crossSelected;
    Crosshair? crossDraft;   // código sendo colado: a prévia mostra ele antes de salvar
    static readonly Image CrossScene = LoadPng("crosshair-scene.jpg");

    // ---------- abas CFGS / MIRAS (no cabeçalho, como as abas do menu do jogo) ----------

    void BuildTabs()
    {
        Label Tab(string text, int x)
        {
            var l = Add(new Label { Text = text, Font = new Font(UiBold, 10.5f), AutoSize = true, Cursor = Cursors.Hand, Location = new Point(S(x), S(27)) });
            return l;
        }
        tabCfgs = Tab("CFGS", 306);
        tabMiras = Tab("MIRAS", 0);
        tabMiras.Left = tabCfgs.Right + S(18);
        tabLine = Add(new Panel { BackColor = Accent, Bounds = new Rectangle(tabCfgs.Left, S(49), tabCfgs.Width, S(2)) });
        tabCfgs.Click += (_, _) => SetTab(false);
        tabMiras.Click += (_, _) => SetTab(true);
        tabCfgs.ForeColor = Ink;
        tabMiras.ForeColor = Muted;
    }

    void SetTab(bool miras)
    {
        ShowSettings(false);   // clicar numa aba com a página do ☰ aberta volta para a aba
        var to = miras ? tabMiras : tabCfgs;
        if (to.ForeColor == Ink) return;
        tabCfgs.ForeColor = miras ? Muted : Ink;
        tabMiras.ForeColor = miras ? Ink : Muted;
        int fromX = tabLine.Left, fromW = tabLine.Width;
        Animate(tabLine, e => tabLine.SetBounds(fromX + (int)((to.Left - fromX) * e), tabLine.Top, fromW + (int)((to.Width - fromW) * e), tabLine.Height));
        crossPage.Visible = miras;
        if (miras) { RefreshCrosshairs(); crossPage.BringToFront(); ActiveControl = null; }
        if (settingsOpen) page.BringToFront();
    }

    // ---------- página da aba Miras ----------

    void BuildCrosshairPage()
    {
        // Panel comum (não SmoothPanel): as linhas já são compostas, e WS_EX_COMPOSITED aninhado não é suportado.
        crossPage = Add(new Panel { Bounds = R(0, 164, 520, 356), Visible = false });
        crossPreview = new Panel { Bounds = R(24, 4, ListW, 188) };
        crossPreview.Paint += PaintPreview;
        tips.SetToolTip(crossPreview, "Mira normal e luneta sobre Ascent.\nA mira normal tem o tamanho do jogo em 1080p; a luneta é uma simulação.");
        crossViewport = new Panel { Bounds = R(24, 200, ListW, 152) };
        crossRows = new Panel { Location = Point.Empty, Width = S(ListW) };
        crossViewport.Controls.Add(crossRows);
        crossScroll = new ThinScrollBar { Bounds = R(503, 204, 10, 144) };
        crossScroll.ValueChanged += () => crossRows.Top = -crossScroll.Value;
        crossPage.Controls.AddRange(new Control[] { crossPreview, crossViewport, crossScroll });
    }

    void RefreshCrosshairs()
    {
        CancelReveal(animated: false);
        if (tools is { } open) open.Close();
        crossDraft = null;
        crossList = vcs.Crosshairs.List();
        if (crossSelected == null || crossList.All(m => m.Name != crossSelected)) crossSelected = crossList.Select(m => m.Name).FirstOrDefault();
        var shown = crossViewport.IsHandleCreated;
        if (shown) SendMessage(crossViewport.Handle, 0x000B /* WM_SETREDRAW */, IntPtr.Zero, IntPtr.Zero);   // recriar sem piscar
        crossRows.SuspendLayout();
        while (crossRows.Controls.Count > 0) crossRows.Controls[0].Dispose();
        var y = 0;
        foreach (var m in crossList) crossRows.Controls.Add(CrossRow(m.Name, m.Code, y++));
        crossRows.Controls.Add(CrossNewRow(y++));
        crossRows.Height = S(RowH) * y;
        crossRows.ResumeLayout();
        crossScroll.Setup(crossRows.Height, crossViewport.Height);
        crossRows.Top = -crossScroll.Value;
        crossPreview.Invalidate();
        if (!shown) return;
        SendMessage(crossViewport.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
        crossViewport.Refresh();
    }

    // Mesmo botão deslizante das CFGs; aqui ele copia o código, sem ativar nada no jogo.
    Panel CrossRow(string name, string code, int index)
    {
        var row = Row(index);
        row.Tag = name;
        var label = new Label { Text = name, ForeColor = Ink, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Bounds = R(4, 8, 260, 36), Cursor = Cursors.Hand };
        SetNameFont(label, SmallName, bold: false);
        var copy = CheckButton(remove: false, copy: true);
        label.Click += (_, _) =>
        {
            crossSelected = name;
            crossDraft = null;
            crossPreview.Invalidate();
            ToggleReveal(name, copy, label, crosshair: true);
        };
        tips.SetToolTip(label, "Mostrar a mira e abrir o botão de copiar");
        var delete = IconLabel(GlyphDelete, 394, 9, "Excluir esta mira");
        delete.Click += (_, _) => Ask($"Excluir a mira \"{name}\"?", "Só apaga do VALORANT CFG; não mexe no jogo.", "Excluir", () =>
        {
            vcs.Crosshairs.Delete(name);
            RefreshCrosshairs();
        });
        var gear = IconLabel(GlyphGear, 434, 9, "Colar ou editar o código desta mira");
        gear.Click += (_, _) => ToggleCrossTools(row, name, code, label, delete, gear);
        copy.Click += (_, _) => { CopyCrosshair(name, code); CancelReveal(animated: true); };
        row.Controls.AddRange(new Control[] { copy, label, delete, gear });
        WheelAll(row, CrossWheel);
        return row;
    }

    // Primeiro cria o perfil pelo nome; a engrenagem abre o código em um segundo passo.
    Panel CrossNewRow(int index)
    {
        var row = Row(index);
        crossBox = NameBox("", 412);
        crossBox.HandleCreated += (s, _) => SendMessage(((TextBox)s).Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Nova mira — digite um nome");
        crossBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CreateCrosshair(); } };
        tips.SetToolTip(crossBox, "Digite o nome e aperte Enter ou clique na engrenagem. Depois cole o código.");
        var gear = IconLabel(GlyphGear, 434, 9, "Criar o perfil e abrir o campo do código");
        gear.Click += (_, _) => CreateCrosshair();
        row.Controls.AddRange(new Control[] { crossBox, gear });
        WheelAll(row, CrossWheel);
        return row;
    }

    void CrossWheel(object? sender, MouseEventArgs e) => crossScroll.Value -= Math.Sign(e.Delta) * S(RowH);

    void CreateCrosshair()
    {
        string name;
        try { name = vcs.Crosshairs.Create(crossBox!.Text); }
        catch (VcsException ex) { Flash("Confira o nome da mira", ex.Message, Tone.Warn); crossBox!.Focus(); return; }
        crossSelected = name;
        RefreshCrosshairs();
        var row = crossRows.Controls.Cast<Control>().OfType<Panel>().First(r => Equals(r.Tag, name));
        crossScroll.Value = Math.Max(0, row.Bottom - crossViewport.Height);
        var labels = row.Controls.OfType<Label>().ToArray();
        ToggleCrossTools(row, name, "", labels.First(l => l.Text == name), labels.First(l => Equals(l.Tag, GlyphDelete)), labels.First(l => Equals(l.Tag, GlyphGear)));
        Flash($"Perfil \"{name}\" criado", "Cole o código da mira no campo e clique em Salvar (ou aperte Enter).");
    }

    void ToggleCrossTools(Panel row, string name, string code, Control label, Control delete, Label gear)
    {
        if (tools is { } open) { open.Close(); if (open.Inside[0] == gear) return; }
        CancelReveal(animated: false);
        crossSelected = name;
        var box = NameBox(code, 316);
        box.AccessibleName = "Código da mira";
        box.HandleCreated += (_, _) => SendMessage(box.Handle, 0x1501, (IntPtr)1, "Cole o código da mira (Ctrl+V)");
        box.TextChanged += (_, _) => { crossDraft = TryParse(box.Text); crossPreview.Invalidate(); };
        var save = RowButton("Salvar", "Salvar o código neste perfil (Enter)");
        save.Left = gear.Left - S(8) - save.Width;
        box.Width = save.Left - box.Left - S(12);
        label.Visible = delete.Visible = false;
        row.Controls.AddRange(new Control[] { box, save });
        SetGlyph(gear, GlyphCancel, "Fechar edição do código (Esc)");
        void Close()
        {
            tools = null;
            crossDraft = null;
            box.Dispose();
            save.Dispose();
            label.Visible = delete.Visible = true;
            SetGlyph(gear, GlyphGear, "Colar ou editar o código desta mira");
            crossPreview.Invalidate();
        }
        void Save()
        {
            try { vcs.Crosshairs.Update(name, box.Text); }
            catch (VcsException ex) { Flash("Código de mira inválido", ex.Message, Tone.Warn); box.Focus(); return; }
            Close();
            RefreshCrosshairs();
            Flash($"Mira \"{name}\" salva", "Clique no nome e no botão de copiar à esquerda para levar o código ao VALORANT.");
        }
        tools = (new Control[] { gear, box, save }, Close);
        save.Click += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        };
        crossDraft = TryParse(code);
        crossPreview.Invalidate();
        box.Focus();
        box.SelectAll();
    }

    void CopyCrosshair(string name, string code)
    {
        if (TryParse(code) == null) { Flash("Adicione o código da mira", "Clique na engrenagem deste perfil, cole um código válido e salve.", Tone.Warn); return; }
        try { Clipboard.SetText(code); }
        catch (ExternalException) { Flash("Não deu para copiar", "Outro programa está usando a área de transferência. Tente de novo.", Tone.Warn); return; }
        Flash($"Código da mira \"{name}\" copiado", "No VALORANT: Configurações → Mira → importar código de perfil, e cole com Ctrl+V.");
    }

    // ---------- prévia ----------

    // Duas prévias quadradas simultâneas, com o nome do perfil abaixo.
    void PaintPreview(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        int size = S(164), gap = S(12), left = (crossPreview.Width - 2 * size - gap) / 2;
        var shown = crossDraft ?? crossList.Where(m => m.Name == crossSelected).Select(m => TryParse(m.Code)).FirstOrDefault();
        using var bold = new Font(UiBold, 9f);
        using var font = new Font(Ui, 9f);
        for (var i = 0; i < 2; i++)
        {
            var square = new Rectangle(left + i * (size + gap), 0, size, size);
            DrawScene(g, square);
            var area = new Rectangle(square.X, square.Y, size, size - S(24));
            var state = g.Save();
            g.SetClip(area);
            if (shown != null)
            {
                if (i == 1) DrawScope(g, shown, area);
                else
                {
                    g.TranslateTransform(area.X + area.Width / 2, area.Y + area.Height / 2);
                    DrawCrosshair(g, shown);
                }
            }
            else TextRenderer.DrawText(g, "Sem código", font, area, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            g.Restore(state);
            var caption = new Rectangle(square.X, square.Bottom - S(24), size, S(24));
            using (var shade = new SolidBrush(Color.FromArgb(220, 12, 14, 18))) g.FillRectangle(shade, caption);
            TextRenderer.DrawText(g, i == 0 ? "PRIMÁRIA" : "LUNETA", bold, caption, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            using var line = new SolidBrush(Accent);
            g.FillRectangle(line, square.X, square.Bottom - S(2), size, S(2));
        }
        var title = crossDraft != null ? "Prévia do código colado" : crossSelected ?? "Crie um perfil na linha Nova mira";
        var titleArea = new Rectangle(0, size + S(2), crossPreview.Width, S(22));
        TextRenderer.DrawText(g, title, font, titleArea, Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    static void DrawScene(Graphics g, Rectangle area)
    {
        var side = Math.Min(CrossScene.Width, CrossScene.Height);
        var crop = new Rectangle((CrossScene.Width - side) / 2, (CrossScene.Height - side) / 2, side, side);
        g.DrawImage(CrossScene, area, crop, GraphicsUnit.Pixel);
    }

    // Visão da luneta: anel preto, cenário dentro, linhas finas da luneta e o ponto central do código (seção S).
    // shortcut: o desenho da luneta é genérico e o diâmetro do ponto (4 px por unidade de espessura) é estimado;
    // comparar com print do jogo se precisar de precisão.
    static void DrawScope(Graphics g, Crosshair x, Rectangle area)
    {
        int cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2, r = Math.Min(area.Width, area.Height) / 2 - 6;
        using (var housing = new SolidBrush(Color.FromArgb(10, 11, 13)))
        using (var lens = new GraphicsPath())
        using (var outside = new Region(area))
        {
            lens.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
            outside.Exclude(lens);
            g.FillRegion(housing, outside);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var ring = new Pen(Color.FromArgb(10, 11, 13), 3)) g.DrawEllipse(ring, cx - r, cy - r, 2 * r, 2 * r);
        using (var reticle = new Pen(Color.FromArgb(200, 0, 0, 0), 1))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.DrawLine(reticle, cx - r, cy, cx + r, cy);
            g.DrawLine(reticle, cx, cy - r, cx, cy + r);
        }
        if (!x.ScopeDot || x.ScopeOpacity <= 0 || x.ScopeThickness <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var d = (float)Math.Max(1.5, x.ScopeThickness * 4);
        using var dot = new SolidBrush(Color.FromArgb((int)(x.ScopeOpacity * 255), x.ScopeRgb.R, x.ScopeRgb.G, x.ScopeRgb.B));
        g.FillEllipse(dot, cx - d / 2f, cy - d / 2f, d, d);
    }

    static Crosshair? TryParse(string code)
    {
        try { return Crosshair.Parse(code); } catch (VcsException) { return null; }
    }

    // Desenha a mira primária centrada na origem atual, em pixels do jogo (1080p). Modelo do parser de
    // referência (Crosshair-Bridge): a linha começa a "offset" px do centro; o contorno preto envolve cada parte.
    // shortcut: sem erro de movimento/disparo nem os modos ADS/luneta; a prévia é a mira parada.
    static void DrawCrosshair(Graphics g, Crosshair x)
    {
        static int Px(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        static float Snap(double v) => (float)Math.Floor(v + 0.5);
        var parts = new List<(RectangleF Rect, double Alpha)>();
        // espessura ímpar: o centro fica no meio de um pixel, como no jogo, e as linhas ficam nítidas
        var main = x.Inner.Show ? Px(x.Inner.Thickness) : x.Outer.Show ? Px(x.Outer.Thickness) : Px(x.DotThickness);
        var c = main % 2 == 1 ? 0.5 : 0.0;
        void Add(double x0, double y0, double w, double h, double a) =>
            parts.Add((RectangleF.FromLTRB(Snap(x0), Snap(y0), Snap(x0 + w), Snap(y0 + h)), a));
        void Lines(Crosshair.Lines l)
        {
            int t = Px(l.Thickness), len = Px(l.Length), v = Px(l.VLength), o = Px(l.Offset);
            if (!l.Show || t <= 0 || l.Opacity <= 0) return;
            var half = t / 2.0;
            if (len > 0) { Add(c + o, c - half, len, t, l.Opacity); Add(c - o - len, c - half, len, t, l.Opacity); }
            if (v > 0) { Add(c - half, c + o, t, v, l.Opacity); Add(c - half, c - o - v, t, v, l.Opacity); }
        }
        Lines(x.Inner);
        Lines(x.Outer);
        if (x.Dot && x.DotOpacity > 0)
        {
            var z = Math.Max(1, Px(x.DotThickness));
            Add(c - z / 2.0, c - z / 2.0, z, z, x.DotOpacity);
        }
        if (parts.Count == 0) return;

        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        if (x.Outlines && x.OutlineOpacity > 0)
        {
            var ow = Math.Max(1, Px(x.OutlineThickness));
            using var outline = new Region(RectangleF.Empty);
            foreach (var p in parts) outline.Union(RectangleF.Inflate(p.Rect, ow, ow));
            foreach (var p in parts) outline.Exclude(p.Rect);   // só a borda: linha translúcida não escurece por baixo
            using var black = new SolidBrush(Color.FromArgb((int)(x.OutlineOpacity * 255), 0, 0, 0));
            g.FillRegion(black, outline);
        }
        foreach (var p in parts)
        {
            using var fill = new SolidBrush(Color.FromArgb((int)(p.Alpha * 255), x.Rgb.R, x.Rgb.G, x.Rgb.B));
            g.FillRectangle(fill, p.Rect);
        }
        g.Restore(state);
    }
}
