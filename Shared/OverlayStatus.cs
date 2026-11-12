using System.Diagnostics;
using System.Text.Json;

namespace D4Overlay.Shared;

/// <summary>État de l'overlay, publié dans status.json pour que le configurateur puisse l'afficher.</summary>
public class OverlayStatus
{
    public int Pid { get; set; }
    /// <summary>"API" | "cache" | "cadence"</summary>
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    public bool EditMode { get; set; }
    /// <summary>false = overlay masqué (alertes sonores seules).</summary>
    public bool OverlayVisible { get; set; } = true;
    /// <summary>true = sourdine rapide active (menu de l'icône de notification).</summary>
    public bool AlertsMuted { get; set; }
    /// <summary>Jeu détecté ? null = pas utilisé (aucun event n'a l'option « seulement en jeu »).</summary>
    public bool? GameRunning { get; set; }
    /// <summary>Dernière fois (UTC) où l'overlay a relu et appliqué la configuration à la demande du configurateur.</summary>
    public DateTime? ConfigAppliedAt { get; set; }
    /// <summary>Raccourcis que Windows a refusés (déjà pris par une autre application) : "MoveMode", "Refresh", "Mute", "OpacityUp", "OpacityDown".</summary>
    public string[] HotkeyFailed { get; set; } = Array.Empty<string>();
    public DateTime? LastApiSuccess { get; set; }
}

public static class StatusStore
{
    public static string StatusPath => Path.Combine(ConfigStore.AppDir, "status.json");

    public static void Write(OverlayStatus status)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.AppDir);
            string tmp = StatusPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(status));
            File.Move(tmp, StatusPath, overwrite: true);
        }
        catch { }
    }

    public static bool TryRead(out OverlayStatus? status)
    {
        status = null;
        try
        {
            if (!File.Exists(StatusPath)) return false;
            status = JsonSerializer.Deserialize<OverlayStatus>(File.ReadAllText(StatusPath));
            return status is not null;
        }
        catch { return false; }
    }

    public static void Clear()
    {
        try { File.Delete(StatusPath); } catch { }
    }

    /// <summary>Le fichier peut survivre à un plantage : on vérifie que le processus existe encore.</summary>
    public static bool IsRunning(OverlayStatus status)
    {
        try
        {
            using var p = Process.GetProcessById(status.Pid);
            return !p.HasExited;
        }
        catch { return false; }
    }
}
