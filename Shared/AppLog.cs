namespace D4Overlay.Shared;

/// <summary>Petit journal partagé par l'overlay et le configurateur : %AppData%\D4WorldBossOverlay\shared.log</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogPath => System.IO.Path.Combine(ConfigStore.AppDir, "shared.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.AppDir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 200_000) fi.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
