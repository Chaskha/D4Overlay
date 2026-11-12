using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace D4Overlay.Shared;

/// <param name="Source">Origine, en code neutre : "embedded", "folder", "exefolder" ou "userfonts" (traduit à l'affichage).</param>
public sealed record FontLoadResult(string Source, string File, bool Ok, string? Error);

/// <summary>
/// Polices « personnelles » que GDI+ ne voit pas tout seul. Trois sources, chargées dans une collection privée :
///  1. polices embarquées dans l'exe (Shared\Assets\Fonts, ressources « font:* ») ;
///  2. dossier %AppData%\D4WorldBossOverlay\fonts (et un dossier fonts à côté de l'exe) : on y dépose un .ttf/.otf, sans recompiler ;
///  3. polices installées « pour l'utilisateur » (%LocalAppData%\Microsoft\Windows\Fonts) : depuis Windows 10 1803
///     c'est l'installation par défaut, et GDI+ ne les liste pas.
/// Chaque fichier est chargé séparément : un fichier invalide n'empêche pas les autres, et l'échec est visible (LoadResults).
/// </summary>
public static class FontCatalog
{
    private static readonly object Gate = new();
    private static PrivateFontCollection? _collection;
    private static readonly HashSet<string> Seen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<FontLoadResult> Results = new();
    private static readonly Dictionary<string, FontFamily> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string UserFontsDir => Path.Combine(ConfigStore.AppDir, "fonts");

    private static IEnumerable<(string Source, string Dir)> Folders()
    {
        yield return ("folder", UserFontsDir);
        yield return ("exefolder", Path.Combine(AppContext.BaseDirectory, "fonts"));
        yield return ("userfonts", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts"));
    }

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_collection is not null) return;
            _collection = new PrivateFontCollection();
            LoadEmbedded(_collection);
            LoadFolders(_collection);
            LogSummary("chargement initial");
        }
    }

    /// <summary>Relit les dossiers : prend en compte une police ajoutée depuis le démarrage.</summary>
    public static void Rescan()
    {
        EnsureLoaded();
        lock (Gate)
        {
            int before = Results.Count;
            LoadFolders(_collection!);
            if (Results.Count != before) LogSummary("rechargement");
        }
    }

    public static IReadOnlyList<FontLoadResult> LoadResults
    {
        get
        {
            EnsureLoaded();
            lock (Gate) return Results.ToList();
        }
    }

    public static IReadOnlyList<string> PrivateFamilyNames
    {
        get
        {
            EnsureLoaded();
            lock (Gate)
            {
                try
                {
                    return _collection!.Families
                        .Select(f => f.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch { return new List<string>(); }
            }
        }
    }

    /// <summary>Famille personnelle portant ce nom, ou null. À NE PAS libérer (Dispose) : elle est partagée.</summary>
    public static FontFamily? Find(string name)
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            try
            {
                var found = _collection!.Families.FirstOrDefault(
                    f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found is not null) Cache[name] = found;
                return found;
            }
            catch { return null; }
        }
    }

    private static void LoadEmbedded(PrivateFontCollection collection)
    {
        var asm = typeof(FontCatalog).Assembly;
        foreach (var resource in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith("font:", StringComparison.OrdinalIgnoreCase)))
        {
            string file = resource.Substring("font:".Length);
            try
            {
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream is null) throw new InvalidOperationException("ressource illisible");
                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                // GDI+ lit la police directement dans ce tampon : on ne le libère jamais (durée de vie du processus).
                IntPtr ptr = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, ptr, data.Length);
                collection.AddMemoryFont(ptr, data.Length);
                Results.Add(new FontLoadResult("embedded", file, true, null));
            }
            catch (Exception ex)
            {
                Results.Add(new FontLoadResult("embedded", file, false, ex.Message));
            }
        }
    }

    private static void LoadFolders(PrivateFontCollection collection)
    {
        foreach (var (source, dir) in Folders())
        {
            List<string> files;
            try
            {
                if (!Directory.Exists(dir)) continue;
                files = Directory.EnumerateFiles(dir)
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { continue; }

            foreach (var file in files)
            {
                if (!Seen.Add(file)) continue;
                try
                {
                    collection.AddFontFile(file);
                    Results.Add(new FontLoadResult(source, file, true, null));
                }
                catch (Exception ex)
                {
                    // GDI+ répond souvent « fichier introuvable » pour un fichier de police qu'il refuse.
                    Results.Add(new FontLoadResult(source, file, false, ex.Message));
                }
            }
        }
    }

    private static void LogSummary(string moment)
    {
        int ok = Results.Count(r => r.Ok);
        AppLog.Write($"polices ({moment}) : {ok} chargée(s), {Results.Count - ok} échec(s)");
        foreach (var r in Results.Where(r => !r.Ok))
            AppLog.Write($"  échec [{r.Source}] {Path.GetFileName(r.File)} : {r.Error}");
        try
        {
            AppLog.Write("  familles personnelles : " +
                         string.Join(", ", _collection!.Families.Select(f => f.Name)));
        }
        catch (Exception ex) { AppLog.Write("  familles personnelles illisibles : " + ex.Message); }
    }
}
