using System.Runtime.InteropServices;

namespace D4Overlay.Shared;

/// <summary>
/// Journal des erreurs graves : %AppData%\D4WorldBossOverlay\crash.log.
///  - toute exception non gérée (interface, tâche, fatale) y est écrite avec sa pile d'appels ;
///  - un fichier « session » détecte les arrêts anormaux (processus tué, coupure…) au démarrage suivant ;
///  - une même erreur qui se répète chaque seconde n'est écrite qu'une fois par minute (pas de fichier géant).
/// </summary>
public static class CrashLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (int Repeats, DateTime LastWritten)> Seen = new();
    private static string _app = "";

    public static string LogPath => Path.Combine(ConfigStore.AppDir, "crash.log");
    private static string SessionPath => Path.Combine(ConfigStore.AppDir, $"session-{_app}.lock");

    /// <summary>
    /// À appeler au tout début de Main (avant toute fenêtre). onUiException(exception, premiereFois) permet à
    /// l'application d'informer l'utilisateur ; l'application continue de tourner après une erreur d'interface.
    /// </summary>
    public static void Install(string appName, Action<Exception, bool>? onUiException = null)
    {
        _app = appName;
        Start();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            bool first = Write("Thread d'interface", e.Exception);
            try { onUiException?.Invoke(e.Exception, first); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("Exception fatale (l'application va s'arrêter)", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Tâche asynchrone non observée", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>À appeler quand l'application s'arrête normalement : efface le marqueur de session.</summary>
    public static void MarkCleanExit()
    {
        try { File.Delete(SessionPath); } catch { }
        Append($"=== Arrêt normal ({_app}) ===");
    }

    /// <summary>Écrit une erreur. Renvoie false si c'est la répétition d'une erreur déjà journalisée il y a moins d'une minute.</summary>
    public static bool Write(string source, Exception exception)
    {
        try
        {
            string key = exception.GetType().FullName + "|" + exception.Message + "|" +
                         (exception.StackTrace?.Split('\n').FirstOrDefault() ?? "");
            lock (Gate)
            {
                if (Seen.TryGetValue(key, out var seen))
                {
                    if (DateTime.Now - seen.LastWritten < TimeSpan.FromMinutes(1))
                    {
                        Seen[key] = (seen.Repeats + 1, seen.LastWritten);
                        return false;
                    }
                    if (seen.Repeats > 0)
                        Append($"    (la même erreur s'est répétée {seen.Repeats} fois depuis la dernière entrée)");
                }
                Seen[key] = (0, DateTime.Now);
            }
            Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{exception}\n");
            return true;
        }
        catch { return true; }
    }

    private static void Start()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.AppDir);
            if (File.Exists(SessionPath))
            {
                string previous = "";
                try { previous = File.ReadAllText(SessionPath).Trim(); } catch { }
                Append($"⚠ La session précédente de {_app} ({previous}) ne s'est pas terminée normalement " +
                       "(processus tué, plantage, coupure de courant…).");
            }
            File.WriteAllText(SessionPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, pid {Environment.ProcessId}");
        }
        catch { }

        Append($"=== Démarrage {_app} {AppInfo.Version} | {Environment.OSVersion} | " +
               $"{RuntimeInformation.FrameworkDescription} | pid {Environment.ProcessId} ===");
    }

    private static void Append(string text)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.AppDir);
                var file = new FileInfo(LogPath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(LogPath, Path.Combine(ConfigStore.AppDir, "crash.old.log"), overwrite: true);
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
            catch { }
        }
    }
}
