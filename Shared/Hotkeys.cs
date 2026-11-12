using System.Text;

namespace D4Overlay.Shared;

/// <summary>Raccourcis clavier choisis par l'utilisateur (settings.json). Chaîne vide = aucune touche.</summary>
public class HotkeySettings
{
    public string MoveMode { get; set; } = "F8";
    public string Refresh { get; set; } = "F5";
    public string Mute { get; set; } = "";
    /// <summary>Actifs seulement pendant le mode déplacement.</summary>
    public string OpacityUp { get; set; } = "PageUp";
    public string OpacityDown { get; set; } = "PageDown";

    public HotkeySettings Clone() => (HotkeySettings)MemberwiseClone();

    public bool SameAs(HotkeySettings other)
        => MoveMode == other.MoveMode && Refresh == other.Refresh && Mute == other.Mute
           && OpacityUp == other.OpacityUp && OpacityDown == other.OpacityDown;

    public string Get(string action) => action switch
    {
        "MoveMode" => MoveMode,
        "Refresh" => Refresh,
        "Mute" => Mute,
        "OpacityUp" => OpacityUp,
        "OpacityDown" => OpacityDown,
        _ => ""
    };

    public void Set(string action, string value)
    {
        switch (action)
        {
            case "MoveMode": MoveMode = value; break;
            case "Refresh": Refresh = value; break;
            case "Mute": Mute = value; break;
            case "OpacityUp": OpacityUp = value; break;
            case "OpacityDown": OpacityDown = value; break;
        }
    }

    /// <summary>Texte à afficher dans l'interface : « — » quand aucune touche n'est attribuée.</summary>
    public static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public void Normalize()
    {
        MoveMode = (MoveMode ?? "").Trim();
        Refresh = (Refresh ?? "").Trim();
        Mute = (Mute ?? "").Trim();
        OpacityUp = (OpacityUp ?? "").Trim();
        OpacityDown = (OpacityDown ?? "").Trim();
    }
}

/// <summary>Une touche + ses modificateurs, au format attendu par RegisterHotKey.</summary>
public readonly record struct HotkeySpec(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModNoRepeat = 0x4000; // pas de répétition quand on garde la touche enfoncée

    // Certains noms de touches existent en double dans WinForms ; on affiche le plus parlant.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["Prior"] = "PageUp",
        ["Next"] = "PageDown",
        ["Return"] = "Enter",
        ["Capital"] = "CapsLock",
        ["Snapshot"] = "PrintScreen",
    };

    /// <summary>
    /// Touches acceptables SANS Ctrl ni Alt : fonction, navigation, pavé numérique. Un raccourci global sur une lettre,
    /// Échap, Entrée… volerait cette touche à tous les autres programmes (impossible de taper !).
    /// </summary>
    public static bool AllowedAlone(Keys key)
        => (key >= Keys.F1 && key <= Keys.F24)
           || (key >= Keys.NumPad0 && key <= Keys.Divide)
           || key is Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Insert
                   or Keys.Pause or Keys.Scroll or Keys.PrintScreen;

    /// <summary>« F8 », « PageUp », « Ctrl+Alt+M »… (false si le texte n'est pas un raccourci valide et sûr).</summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint modifiers = 0;
        Keys key = Keys.None;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= ModControl; continue;
                case "alt": modifiers |= ModAlt; continue;
                case "shift": modifiers |= ModShift; continue;
            }

            if (key != Keys.None) return false; // une seule touche principale
            if (!Enum.TryParse<Keys>(part, true, out var parsed) || !Enum.IsDefined(typeof(Keys), parsed)) return false;
            int code = (int)parsed;
            if (code < 8 || code > 0xFFFF) return false; // boutons de souris, ou drapeaux de modificateurs
            if (parsed is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return false;
            key = parsed;
        }

        if (key == Keys.None) return false;
        bool hasCtrlOrAlt = (modifiers & (ModControl | ModAlt)) != 0; // Maj seul ne suffit pas : Maj+lettre sert à écrire
        if (!hasCtrlOrAlt && !AllowedAlone(key)) return false;
        spec = new HotkeySpec(modifiers, (uint)key);
        return true;
    }

    /// <summary>Texte d'une touche pressée (KeyData d'un évènement clavier), par exemple « Ctrl+Alt+M ».</summary>
    public static string Format(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var sb = new StringBuilder();
        if ((keyData & Keys.Control) != 0) sb.Append("Ctrl+");
        if ((keyData & Keys.Alt) != 0) sb.Append("Alt+");
        if ((keyData & Keys.Shift) != 0) sb.Append("Shift+");
        string name = key.ToString();
        sb.Append(Aliases.TryGetValue(name, out var alias) ? alias : name);
        return sb.ToString();
    }
}
