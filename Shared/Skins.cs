namespace D4Overlay.Shared;

/// <summary>Réglages propres à une image : taille de police et position des timers.</summary>
public class SkinProfile
{
    public float FontSize { get; set; } = 16f;
    public Dictionary<string, TimerPlacement> Timers { get; set; } = new();

    public SkinProfile Clone()
    {
        var c = new SkinProfile { FontSize = FontSize };
        foreach (var kv in Timers)
            c.Timers[kv.Key] = new TimerPlacement(kv.Value?.X ?? 0, kv.Value?.Y ?? 0);
        return c;
    }
}

/// <summary>Image intégrée à l'exe. InteriorResource = masque du fond intérieur (null : l'image a déjà son fond).</summary>
public sealed record BuiltInSkin(string Id, string Label, string FrameResource, string? InteriorResource,
    SkinProfile Defaults);

public static class Skins
{
    /// <summary>Id vide = cadre lave (valeur historique : un ancien layout.json reste valide).</summary>
    public const string LavaId = "";
    public const string MetalId = "builtin:metal";

    /// <summary>Cadre choisi pour une nouvelle installation (et par « Réinitialiser l'apparence »).</summary>
    public const string DefaultId = MetalId;

    public static readonly BuiltInSkin[] BuiltIn =
    {
        new(MetalId, "Métal sobre", "skin-metal.png", "interior-fill-metal.png",
            // Ordre : World Boss, Helltide, Legion. Légion au milieu, Helltide en bas : même ordre que les icônes du cadre Lava.
            Profile(12f, 40, 64, 40, 106, 40, 85)),
        new(LavaId, "Cadre lave", "overlay-bg.png", "interior-fill.png",
            Profile(16f, 50, 84, 50, 112, 50, 140)),
    };

    /// <summary>Libellé (dans la langue courante) d'une image intégrée.</summary>
    public static string LabelFor(BuiltInSkin skin)
        => Loc.T("skin." + (skin.Id.Length == 0 ? "lava" : skin.Id.Replace("builtin:", "")));

    public static BuiltInSkin? Find(string? id)
        => BuiltIn.FirstOrDefault(s => string.Equals(s.Id, id ?? "", StringComparison.OrdinalIgnoreCase));

    // Paires x,y dans l'ordre de ConfigStore.EventLabels (World Boss, Helltide, Legion).
    private static SkinProfile Profile(float fontSize, params int[] xy)
    {
        var p = new SkinProfile { FontSize = fontSize };
        for (int i = 0; i < ConfigStore.EventLabels.Length && 2 * i + 1 < xy.Length; i++)
            p.Timers[ConfigStore.EventLabels[i]] = new TimerPlacement(xy[2 * i], xy[2 * i + 1]);
        return p;
    }

    /// <summary>
    /// Réglages de départ quand on choisit une image : ceux prévus pour une image intégrée ;
    /// pour une image perso, les positions du cadre lave mises à l'échelle de sa taille.
    /// </summary>
    public static SkinProfile DefaultProfileFor(string id, Size frame, float currentFontSize)
    {
        var builtIn = Find(id);
        if (builtIn is not null) return builtIn.Defaults.Clone();

        var lava = Find(LavaId)!.Defaults;
        var p = new SkinProfile { FontSize = currentFontSize };
        foreach (var kv in lava.Timers)
        {
            int x = (int)Math.Round(kv.Value.X * frame.Width / 384.0);
            int y = (int)Math.Round(kv.Value.Y * frame.Height / 216.0);
            p.Timers[kv.Key] = new TimerPlacement(
                Math.Clamp(x, 0, Math.Max(0, frame.Width - 10)),
                Math.Clamp(y, 0, Math.Max(0, frame.Height - 10)));
        }
        return p;
    }
}
