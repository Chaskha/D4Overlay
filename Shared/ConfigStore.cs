using System.Text.Json;

namespace D4Overlay.Shared;

/// <summary>
/// Lecture / écriture de alerts.json, partagé entre l'overlay et le configurateur.
/// L'ancien format est compatible : "Visible" absent = true.
/// </summary>
public static class ConfigStore
{
    public static readonly string[] EventLabels = { "World Boss", "Helltide", "Legion" };

    public static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "D4WorldBossOverlay");

    public static string SoundsDir => Path.Combine(AppDir, "sounds");
    public static string AlertsPath => Path.Combine(AppDir, "alerts.json");
    /// <summary>Images de fond (cadres) choisies par l'utilisateur.</summary>
    public static string SkinsDir => Path.Combine(AppDir, "skins");

    private static readonly string[] SkinExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    /// <summary>
    /// Première utilisation : copie dans %AppData% ce qui manque depuis le dossier "defaults" placé à côté
    /// de l'exe (alerts.json, layout.json, skins\, sounds\ préparés pour la distribution).
    /// Ne remplace JAMAIS un fichier existant : les réglages de l'utilisateur sont intouchables.
    /// </summary>
    public static void EnsureDefaults()
    {
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "defaults");
            if (!Directory.Exists(source)) return;

            // defaults-seeded.txt : liste des fichiers par défaut déjà proposés à cet utilisateur. Un fichier qu'il a
            // supprimé ensuite (un son dont il ne veut pas…) ne revient donc pas à chaque lancement, alors qu'un fichier
            // ajouté par une nouvelle version, lui, est bien copié.
            string markerPath = Path.Combine(AppDir, "defaults-seeded.txt");
            var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(markerPath))
                foreach (var line in File.ReadAllLines(markerPath))
                    if (line.Length > 0) seeded.Add(line);

            int before = seeded.Count;
            SeedFolder(source, AppDir, "", seeded);
            if (seeded.Count != before)
            {
                Directory.CreateDirectory(AppDir);
                File.WriteAllLines(markerPath, seeded.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch { }
    }

    private static void SeedFolder(string from, string to, string relative, HashSet<string> seeded)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            string name = Path.GetFileName(file);
            if (!seeded.Add(relative + name)) continue; // déjà proposé une fois

            string dest = Path.Combine(to, name);
            if (!File.Exists(dest)) File.Copy(file, dest); // ne remplace JAMAIS un fichier de l'utilisateur
        }
        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            string name = Path.GetFileName(dir);
            SeedFolder(dir, Path.Combine(to, name), relative + name + "/", seeded);
        }
    }

    public static IReadOnlyList<string> ListSkins()
    {
        try
        {
            if (!Directory.Exists(SkinsDir)) return new List<string>();
            return Directory.EnumerateFiles(SkinsDir)
                .Where(p => SkinExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .Select(p => Path.GetFileName(p))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Dictionary<string, EventAlertConfig> CreateDefaults() => new()
    {
        ["World Boss"] = Make(8, "asterisk", 4, "exclamation", 2, "hand"),
        ["Helltide"] = Make(8, "asterisk", 4, "exclamation", 2, "hand"),
        ["Legion"] = Make(4, "exclamation", 2, "hand"),
    };

    private static EventAlertConfig Make(params object[] minutesAndSounds)
    {
        var cfg = new EventAlertConfig();
        for (int i = 0; i + 1 < minutesAndSounds.Length; i += 2)
            cfg.Levels.Add(new AlertLevel
            {
                Minutes = (int)minutesAndSounds[i],
                Sound = (string)minutesAndSounds[i + 1]
            });
        return cfg;
    }

    /// <summary>true si le fichier existe et a été lu correctement (3 essais : il peut être en cours d'écriture).</summary>
    public static bool TryLoad(out Dictionary<string, EventAlertConfig> map)
    {
        map = CreateDefaults();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(AlertsPath)) return false;
                var loaded = JsonSerializer.Deserialize<Dictionary<string, EventAlertConfig>>(
                    File.ReadAllText(AlertsPath));
                if (loaded is null) return false;
                foreach (var label in EventLabels)
                {
                    if (loaded.TryGetValue(label, out var cfg) && cfg is not null)
                    {
                        cfg.Levels ??= new List<AlertLevel>();
                        map[label] = cfg;
                    }
                }
                return true;
            }
            catch (IOException) { Thread.Sleep(100); }
            catch { return false; } // JSON cassé
        }
        return false;
    }

    public static Dictionary<string, EventAlertConfig> LoadOrDefaults()
    {
        TryLoad(out var map);
        return map;
    }

    public static void EnsureFileExists()
    {
        try
        {
            if (!File.Exists(AlertsPath)) Save(CreateDefaults());
        }
        catch { }
    }

    /// <summary>Écriture atomique (fichier temporaire puis remplacement) pour que l'overlay ne lise jamais un fichier à moitié écrit.</summary>
    public static void Save(Dictionary<string, EventAlertConfig> map)
    {
        Directory.CreateDirectory(AppDir);
        string tmp = AlertsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(map, JsonOpts));
        File.Move(tmp, AlertsPath, overwrite: true);
    }
}
