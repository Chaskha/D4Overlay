using System.Text.Json;

namespace D4Overlay.Shared;

public class TimerPlacement
{
    public int X { get; set; }
    public int Y { get; set; }

    public TimerPlacement() { }
    public TimerPlacement(int x, int y) { X = x; Y = y; }
}

/// <summary>
/// Apparence des timers : police, taille, et position (coin haut-gauche du texte, en pixels
/// de l'image du cadre, 384×216) de chaque event. Stocké dans layout.json.
/// </summary>
public class OverlayLayout
{
    /// <summary>Police par défaut : Comic Sans MS, fournie avec Windows, lisible sur les écrans haute résolution.</summary>
    public const string DefaultFontFamily = "Comic Sans MS";

    public string FontFamily { get; set; } = DefaultFontFamily;
    /// <summary>Taille en pixels (16 px ≈ 12 pt).</summary>
    public float FontSize { get; set; } = 16f;
    public bool Bold { get; set; } = true;
    /// <summary>Espace ajouté entre les lettres, en pixels (0 = normal, peut être négatif).</summary>
    public float LetterSpacing { get; set; } = 0f;
    public Dictionary<string, TimerPlacement> Timers { get; set; } = new();

    /// <summary>Couleur du texte par event, "#RRGGBB" (absent = couleur par défaut de l'event).</summary>
    public Dictionary<string, string> TimerColors { get; set; } = new();
    public string OutlineColor { get; set; } = "#000000";
    /// <summary>Fond à l'intérieur du cadre : couleur et opacité (0 = aucun fond, 1 = opaque).</summary>
    public string InteriorColor { get; set; } = "#000000";
    public float InteriorOpacity { get; set; } = 0.8f;
    /// <summary>"" = cadre lave intégré ; sinon nom d'un fichier du dossier skins.</summary>
    public string BackgroundImage { get; set; } = Skins.DefaultId;
    /// <summary>Taille de l'overlay à l'écran, en % de l'image (écrans haute résolution) : 50 à 300.</summary>
    public int Scale { get; set; } = 100;
    /// <summary>Opacité de tout l'overlay (cadre et texte), en % : 20 à 100. Toujours 100 % en mode déplacement (F8).</summary>
    public int Opacity { get; set; } = 100;
    /// <summary>Taille de police et positions mémorisées pour les images qu'on a quittées (clé = id de l'image).</summary>
    public Dictionary<string, SkinProfile> SkinProfiles { get; set; } = new();

    /// <summary>Réglages d'une installation neuve : cadre par défaut (Métal), avec sa taille de police et ses positions.</summary>
    public static OverlayLayout CreateDefault()
    {
        var profile = Skins.Find(Skins.DefaultId)!.Defaults;
        var layout = new OverlayLayout { BackgroundImage = Skins.DefaultId, FontSize = profile.FontSize };
        foreach (var kv in profile.Timers)
            layout.Timers[kv.Key] = new TimerPlacement(kv.Value.X, kv.Value.Y);
        return layout;
    }

    public static Color ColorFor(string label) => label switch
    {
        "World Boss" => Color.OrangeRed,
        "Helltide" => Color.MediumPurple,
        "Legion" => Color.DeepSkyBlue,
        _ => Color.White
    };

    public Color GetTimerColor(string label)
        => TimerColors.TryGetValue(label, out var hex) && TryParseColor(hex, out var c) ? c : ColorFor(label);

    public Color GetOutlineColor() => TryParseColor(OutlineColor, out var c) ? c : Color.Black;
    public Color GetInteriorColor() => TryParseColor(InteriorColor, out var c) ? c : Color.Black;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParseColor(string? text, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            color = ColorTranslator.FromHtml(text.Trim());
            return !color.IsEmpty;
        }
        catch { return false; }
    }

    /// <summary>Position d'un event (celle par défaut si le fichier ne la contient pas).</summary>
    public TimerPlacement GetPlacement(string label)
    {
        if (Timers.TryGetValue(label, out var p) && p is not null) return p;
        var def = CreateDefault();
        return def.Timers.TryGetValue(label, out var d) ? d : new TimerPlacement(50, 84);
    }

    /// <summary>Corrige un fichier incomplet ou saisi à la main.</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = DefaultFontFamily;
        FontSize = Math.Clamp(FontSize, 8f, 48f);
        LetterSpacing = Math.Clamp(LetterSpacing, -3f, 12f);
        InteriorOpacity = Math.Clamp(InteriorOpacity, 0f, 1f);
        Scale = Math.Clamp(Scale, 50, 300);
        Opacity = Math.Clamp(Opacity, 20, 100);
        BackgroundImage ??= "";
        TimerColors ??= new Dictionary<string, string>();
        SkinProfiles ??= new Dictionary<string, SkinProfile>();
        foreach (var key in SkinProfiles.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
            SkinProfiles.Remove(key);
        if (!TryParseColor(OutlineColor, out _)) OutlineColor = "#000000";
        if (!TryParseColor(InteriorColor, out _)) InteriorColor = "#000000";
        Timers ??= new Dictionary<string, TimerPlacement>();
        foreach (var kv in CreateDefault().Timers)
            if (!Timers.TryGetValue(kv.Key, out var cur) || cur is null)
                Timers[kv.Key] = kv.Value;
    }

    public OverlayLayout Clone()
    {
        var c = new OverlayLayout
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            Bold = Bold,
            LetterSpacing = LetterSpacing,
            OutlineColor = OutlineColor,
            InteriorColor = InteriorColor,
            InteriorOpacity = InteriorOpacity,
            BackgroundImage = BackgroundImage,
            Scale = Scale,
            Opacity = Opacity
        };
        foreach (var kv in TimerColors) c.TimerColors[kv.Key] = kv.Value;
        foreach (var kv in SkinProfiles) c.SkinProfiles[kv.Key] = kv.Value.Clone();
        foreach (var kv in Timers)
            c.Timers[kv.Key] = new TimerPlacement(kv.Value?.X ?? 0, kv.Value?.Y ?? 0);
        return c;
    }
}

public static class LayoutStore
{
    public static string LayoutPath => Path.Combine(ConfigStore.AppDir, "layout.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static bool TryLoad(out OverlayLayout layout)
    {
        layout = OverlayLayout.CreateDefault();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(LayoutPath)) return false;
                var loaded = JsonSerializer.Deserialize<OverlayLayout>(File.ReadAllText(LayoutPath));
                if (loaded is null) return false;
                loaded.Normalize();
                layout = loaded;
                return true;
            }
            catch (IOException) { Thread.Sleep(100); }
            catch { return false; } // JSON cassé
        }
        return false;
    }

    public static OverlayLayout LoadOrDefaults()
    {
        TryLoad(out var layout);
        return layout;
    }

    /// <summary>Écriture atomique, comme alerts.json.</summary>
    public static void Save(OverlayLayout layout)
    {
        Directory.CreateDirectory(ConfigStore.AppDir);
        string tmp = LayoutPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(layout, JsonOpts));
        File.Move(tmp, LayoutPath, overwrite: true);
    }
}
