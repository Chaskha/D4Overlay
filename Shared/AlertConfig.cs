namespace D4Overlay.Shared;

/// <summary>
/// Un palier d'alerte : "Minutes" avant l'event, joue "Sound".
/// Sound = "none" | "asterisk" | "exclamation" | "hand" | "question" | "beep"
///       | "monfichier.wav" (fichier dans %AppData%\D4WorldBossOverlay\sounds\)
/// </summary>
public class AlertLevel
{
    public int Minutes { get; set; }
    public string Sound { get; set; } = "exclamation";
    /// <summary>Volume de l'alarme, 0-100 (absent d'un ancien alerts.json = 100).</summary>
    public int Volume { get; set; } = 100;
}

/// <summary>
/// Réglages d'un event (World Boss, Helltide, Legion).
/// Visible = false → ligne masquée dans l'overlay ET aucune alarme.
/// Enabled = false → ligne affichée mais aucune alarme.
/// </summary>
public class EventAlertConfig
{
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    /// <summary>true = les alarmes de cet event ne sonnent que si Diablo IV est lancé (sinon elles sonnent toujours).</summary>
    public bool OnlyInGame { get; set; }
    public List<AlertLevel> Levels { get; set; } = new();
}
