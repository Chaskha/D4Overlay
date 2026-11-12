using System.Globalization;
using System.Text.Json;

namespace D4Overlay.Shared;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>Réglages généraux de l'application (settings.json).</summary>
public class AppSettings
{
    /// <summary>Code de la langue de l'interface : "en", "fr", "pt-BR"…</summary>
    public string Language { get; set; } = "en";

    /// <summary>false = overlay masqué : rien n'est dessiné, les events et les alertes sonores continuent de fonctionner.</summary>
    public bool OverlayVisible { get; set; } = true;

    /// <summary>Thème de la fenêtre de configuration : "system" (celui de Windows), "light" ou "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// Noms des processus (sans .exe) qui indiquent que le jeu tourne, pour l'option « seulement en jeu ».
    /// À modifier ici si l'exécutable du jeu porte un autre nom.
    /// </summary>
    public string[] GameProcesses { get; set; } = new[] { "Diablo IV" };

    /// <summary>Raccourcis clavier globaux.</summary>
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>true = l'overlay démarre avec Windows (entrée HKCU\Run).</summary>
    public bool StartWithWindows { get; set; }
}

public static class SettingsStore
{
    public static string SettingsPath => Path.Combine(ConfigStore.AppDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings LoadOrDefaults()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (s is not null)
                {
                    if (string.IsNullOrWhiteSpace(s.Language)) s.Language = Loc.DefaultCode;
                    if (s.GameProcesses is null || s.GameProcesses.Length == 0) s.GameProcesses = new[] { "Diablo IV" };
                    s.Hotkeys ??= new HotkeySettings();
                    s.Hotkeys.Normalize();
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    /// <summary>Écriture atomique, comme les autres fichiers de réglages.</summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigStore.AppDir);
        string tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, SettingsPath, overwrite: true);
    }
}

/// <summary>
/// Textes de l'interface. Anglais par défaut. Chaque langue est un fichier JSON « clé → texte » :
///  - fournis dans l'exe : ressources « lang:xx.json » (Shared\Assets\Lang) ;
///  - ajoutés / corrigés par l'utilisateur : %AppData%\D4WorldBossOverlay\lang\xx.json
///    (un fichier de l'utilisateur complète et remplace les textes intégrés du même code).
/// Une clé absente d'une langue retombe sur l'anglais, puis sur la clé elle-même : une traduction partielle ne casse rien.
/// </summary>
public static class Loc
{
    public const string DefaultCode = "en";

    private static readonly object Gate = new();
    private static Dictionary<string, string> _base = new();
    private static Dictionary<string, string> _current = new();
    private static bool _initialized;

    public static string Code { get; private set; } = DefaultCode;
    public static string LangDir => Path.Combine(ConfigStore.AppDir, "lang");

    private static void EnsureInit()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;
            _base = LoadLanguage(DefaultCode);
            _current = _base;
            Apply(SettingsStore.LoadOrDefaults().Language);
        }
    }

    public static string T(string key)
    {
        EnsureInit();
        if (_current.TryGetValue(key, out var text)) return text;
        return _base.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>Texte avec paramètres : {0}, {1}, {0:HH:mm:ss}…</summary>
    public static string T(string key, params object?[] args)
    {
        string format = T(key);
        try { return string.Format(CultureInfo.CurrentCulture, format, args); }
        catch (FormatException) { return format; } // un fichier de langue mal édité ne doit pas faire planter
    }

    /// <summary>Nom affiché d'un event ("World Boss", "Helltide", "Legion" restent les clés techniques des fichiers de réglages).</summary>
    public static string EventName(string label) => label switch
    {
        "World Boss" => T("event.worldboss"),
        "Helltide" => T("event.helltide"),
        "Legion" => T("event.legion"),
        _ => label
    };

    public static void SetLanguage(string code)
    {
        EnsureInit();
        lock (Gate) Apply(code);
    }

    private static void Apply(string? code)
    {
        code = (code ?? "").Trim();
        Dictionary<string, string> dict = new();
        if (code.Length > 0 && code.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            dict = LoadLanguage(code);

        if (dict.Count == 0)
        {
            code = DefaultCode;
            dict = _base;
        }
        _current = dict;
        Code = code;
    }

    /// <summary>Langues disponibles : celles de l'exe plus les fichiers du dossier lang (les noms commençant par _ sont ignorés).</summary>
    public static IReadOnlyList<LanguageInfo> Available()
    {
        EnsureInit();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in typeof(Loc).Assembly.GetManifestResourceNames())
                if (name.StartsWith("lang:", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    codes.Add(name.Substring(5, name.Length - 10));
        }
        catch { }
        try
        {
            if (Directory.Exists(LangDir))
                foreach (var file in Directory.EnumerateFiles(LangDir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    if (!code.StartsWith("_")) codes.Add(code);
                }
        }
        catch { }

        return codes
            .Select(c =>
            {
                var d = LoadLanguage(c);
                return new LanguageInfo(c, d.TryGetValue("_language", out var n) && n.Length > 0 ? n : c);
            })
            .OrderBy(l => string.Equals(l.Code, DefaultCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Prépare le dossier des langues pour qui veut traduire ou corriger, et renvoie ce dossier :
    ///  - _template.json : copie de l'anglais, point de départ d'une NOUVELLE langue ;
    ///  - _reference\ : un fichier par langue intégrée (en, fr, it…), à copier à côté pour en corriger une ;
    ///  - README.txt : le mode d'emploi.
    /// Les fichiers de référence sont réécrits à chaque appel : ils reflètent toujours la version installée.
    /// </summary>
    public static string ExportReferenceFiles()
    {
        Directory.CreateDirectory(LangDir);
        try
        {
            string reference = Path.Combine(LangDir, "_reference");
            Directory.CreateDirectory(reference);

            var asm = typeof(Loc).Assembly;
            foreach (var resource in asm.GetManifestResourceNames()
                         .Where(n => n.StartsWith("lang:", StringComparison.OrdinalIgnoreCase)
                                     && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = resource.Substring("lang:".Length);
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream is null) continue;

                using (var output = File.Create(Path.Combine(reference, fileName)))
                    stream.CopyTo(output);

                if (string.Equals(fileName, DefaultCode + ".json", StringComparison.OrdinalIgnoreCase))
                {
                    stream.Position = 0;
                    using var template = File.Create(Path.Combine(LangDir, "_template.json"));
                    stream.CopyTo(template);
                }
            }

            File.WriteAllText(Path.Combine(LangDir, "README.txt"),
                "D4 Overlay - languages / langues\r\n" +
                "================================\r\n\r\n" +
                "ADD A LANGUAGE / AJOUTER UNE LANGUE\r\n" +
                "  Copy _template.json to <code>.json in this folder (for example pt-BR.json or nl.json).\r\n" +
                "  Set \"_language\" to the name shown in the picker, then translate the values (keep {0}, {1}... as they are).\r\n" +
                "  Copiez _template.json vers <code>.json dans ce dossier, renseignez \"_language\" puis traduisez les valeurs.\r\n\r\n" +
                "CORRECT A BUILT-IN LANGUAGE / CORRIGER UNE LANGUE INTEGREE\r\n" +
                "  Copy its file from the _reference folder to THIS folder, keeping the same name (for example fr.json).\r\n" +
                "  You can keep only the lines you want to change: any missing line uses the built-in text.\r\n" +
                "  Copiez son fichier du dossier _reference vers CE dossier, sous le meme nom (par exemple fr.json).\r\n" +
                "  Vous pouvez ne garder que les lignes a modifier : toute ligne absente utilise le texte integre.\r\n\r\n" +
                "APPLY / APPLIQUER\r\n" +
                "  Choose the language again in the picker (bottom right of the settings window).\r\n" +
                "  Choisissez a nouveau la langue dans le selecteur (en bas a droite de la fenetre de configuration).\r\n\r\n" +
                "Files starting with _ and the _reference folder are ignored by the application.\r\n");
        }
        catch (Exception ex) { AppLog.Write("fichiers de référence des langues : " + ex.Message); }
        return LangDir;
    }

    private static Dictionary<string, string> LoadLanguage(string code)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"lang:{code}.json");
            if (stream is not null) Merge(result, stream);
        }
        catch (Exception ex) { AppLog.Write($"langue '{code}' (intégrée) illisible : {ex.Message}"); }

        try
        {
            string path = Path.Combine(LangDir, code + ".json");
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                Merge(result, file);
            }
        }
        catch (Exception ex) { AppLog.Write($"langue '{code}' (fichier utilisateur) illisible : {ex.Message}"); }

        return result;
    }

    private static void Merge(Dictionary<string, string> target, Stream stream)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(stream, options);
        foreach (var property in doc.RootElement.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
                target[property.Name] = property.Value.GetString() ?? "";
    }
}
