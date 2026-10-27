// Miras: o código de perfil que o próprio VALORANT importa e exporta (Configurações → Mira → importar/exportar).
// O app não injeta nada no jogo: guarda os códigos, mostra como a mira fica e copia o código para colar no jogo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ValCfgSync;

// Formato (engenharia reversa da comunidade; parser de referência: github.com/spartytech/Crosshair-Bridge,
// src/lib/valorantCode.ts e defaults.ts): "0;" + pares chave;valor separados por ";". Os marcadores P, A e S
// abrem as seções mira primária, mira com mira (ADS) e luneta; o que vem antes é geral. O jogo só escreve o que
// difere do padrão, então chave ausente = valor padrão abaixo. Desenhadas: a primária e o ponto da luneta (S).
// Luneta (S), em exemplos reais de pros: d mostra o ponto (1), c cor (7 = vermelho), t cor própria quando c = 8,
// s espessura (1, aceita decimais), o opacidade (0.75).
public sealed class Crosshair
{
    public sealed class Lines
    {
        public bool Show;
        public double Thickness, Length, VLength, Offset, Opacity;
    }

    // Paleta do jogo por índice "c"; 8 = cor própria em "u" (RRGGBB ou RRGGBBAA).
    static readonly (byte R, byte G, byte B)[] Palette =
        { (255, 255, 255), (0, 255, 0), (127, 255, 0), (223, 255, 0), (255, 255, 0), (0, 255, 255), (255, 0, 255), (255, 0, 0) };
    public static readonly string[] ColorNames = { "Branco", "Verde", "Verde-amarelado", "Amarelo-esverdeado", "Amarelo", "Ciano", "Rosa", "Vermelho", "Personalizada" };

    public string Code = "";
    public int ColorIndex;
    public (byte R, byte G, byte B) Rgb;
    public bool Outlines, Dot;
    public double OutlineThickness, OutlineOpacity, DotThickness, DotOpacity;
    public Lines Inner = new(), Outer = new();
    public bool ScopeDot;
    public int ScopeColorIndex;
    public (byte R, byte G, byte B) ScopeRgb;
    public double ScopeThickness, ScopeOpacity;

    public static Crosshair Parse(string? text)
    {
        var code = (text ?? "").Trim();
        if (code.Length == 0 || code.Length > 2000 || !Regex.IsMatch(code, @"^\d+(;|$)"))
            throw new VcsException("Isso não é um código de mira do VALORANT. Ele começa com um número e ponto e vírgula, como 0;P;c;5;...");

        var sections = new Dictionary<string, Dictionary<string, string>> { ["G"] = new(), ["P"] = new(), ["A"] = new(), ["S"] = new() };
        var section = "G";
        var tokens = code.Split(';');
        for (var i = 1; i < tokens.Length;)
        {
            var tok = tokens[i].Trim();
            if (tok is "P" or "A" or "S") { section = tok; i++; continue; }
            if (tok == "") { i++; continue; }
            sections[section][tok] = i + 1 < tokens.Length ? tokens[i + 1].Trim() : "";
            i += 2;
        }
        var p = sections["P"];

        double Num(string key, double def) => NumIn(p, key, def);
        static double NumIn(Dictionary<string, string> d, string key, double def)
        {
            if (!d.TryGetValue(key, out var s)) return def;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 && v < 1000) return v;
            throw new VcsException($"Código de mira com valor inválido em \"{key}\": \"{s}\".");
        }
        static (byte, byte, byte) Color(int index, Dictionary<string, string> d, string customKey) =>
            index < 8 ? Palette[index]
            : d.TryGetValue(customKey, out var hex) && Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}")
                ? (Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16))
                : Palette[0];
        Lines ReadLines(string k, double t, double l, double o, double a)
        {
            var len = Num(k + "l", l);
            return new Lines
            {
                Show = Num(k + "b", 1) != 0,
                Thickness = Num(k + "t", t),
                Length = len,
                VLength = Num(k + "g", 0) != 0 ? Num(k + "v", len) : len,   // "g" liga o comprimento vertical separado
                Offset = Num(k + "o", o),
                Opacity = Math.Min(1, Num(k + "a", a)),
            };
        }

        var x = new Crosshair
        {
            Code = code,
            ColorIndex = (int)Math.Min(8, Num("c", 0)),
            Outlines = Num("h", 1) != 0,
            OutlineThickness = Num("t", 1),
            OutlineOpacity = Math.Min(1, Num("o", 0.5)),
            Dot = Num("d", 0) != 0,
            DotThickness = Num("z", 2),
            DotOpacity = Math.Min(1, Num("a", 1)),
            Inner = ReadLines("0", 2, 6, 3, 0.8),
            Outer = ReadLines("1", 2, 2, 10, 0.35),
        };
        x.Rgb = Color(x.ColorIndex, p, "u");
        var sc = sections["S"];
        x.ScopeDot = NumIn(sc, "d", 1) != 0;
        x.ScopeColorIndex = (int)Math.Min(8, NumIn(sc, "c", 7));
        x.ScopeRgb = Color(x.ScopeColorIndex, sc, "t");
        x.ScopeThickness = Math.Min(10, NumIn(sc, "s", 1));
        x.ScopeOpacity = Math.Min(1, NumIn(sc, "o", 0.75));
        return x;
    }
}

// Miras salvas: %LOCALAPPDATA%\ValorantCFG\crosshairs.json, em ordem de inclusão.
public sealed class CrosshairStore(string file)
{
    public const string Format = "ValCfgSync/miras/1";

    public List<(string Name, string Code)> List()
    {
        var items = Json.ReadFile(file) is { } d && d.TryGetValue("Items", out var v) ? v as System.Collections.IEnumerable : null;
        return items?.OfType<Dictionary<string, object>>()
            .Select(m => (Name: m.Str("Name") ?? "", Code: m.Str("Code") ?? ""))
            .Where(m => m.Name != "")
            .ToList() ?? new();
    }

    // Valida o código (lança VcsException se não for de mira) e guarda. Sem nome: "Mira 1", "Mira 2"...
    public string Add(string code, string? name = null)
    {
        var parsed = Crosshair.Parse(code);
        var list = List();
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
            for (var n = list.Count + 1; ; n++) { name = $"Mira {n}"; if (list.All(m => m.Name != name)) break; }
        ValidateName(list, name!);
        list.Add((name!, parsed.Code));
        Write(list);
        return name!;
    }

    public void Delete(string name) => Write(List().Where(m => m.Name != name).ToList());

    public string Create(string name)
    {
        name = name.Trim();
        var list = List();
        ValidateName(list, name);
        list.Add((name, ""));
        Write(list);
        return name;
    }

    public void Update(string name, string code)
    {
        var parsed = Crosshair.Parse(code);
        var list = List();
        var index = list.FindIndex(m => m.Name == name);
        if (index < 0) throw new VcsException("Esta mira não existe mais. Crie um perfil com nome primeiro.");
        list[index] = (name, parsed.Code);
        Write(list);
    }

    static void ValidateName(List<(string Name, string Code)> list, string name)
    {
        if (name == "") throw new VcsException("Digite um nome para a mira.");
        if (list.Any(m => m.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
            throw new VcsException($"Já existe uma mira chamada \"{name}\".");
    }

    void Write(List<(string Name, string Code)> list) =>
        Json.WriteFile(file, new Dictionary<string, object>
        {
            ["Format"] = Format,
            ["Items"] = list.Select(m => new Dictionary<string, object> { ["Name"] = m.Name, ["Code"] = m.Code }).ToArray(),
        });
}
