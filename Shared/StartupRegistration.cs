using Microsoft.Win32;

namespace D4Overlay.Shared;

/// <summary>
/// Démarrage de l'overlay avec Windows : une valeur dans HKCU\...\Run (le profil de l'utilisateur : aucun droit
/// administrateur, rien n'est installé ailleurs). Décocher l'option supprime la valeur.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "D4Overlay";

    /// <summary>
    /// Inscrit (ou retire) le démarrage automatique. Idempotent : peut être appelé à chaque lancement pour recaler
    /// le chemin si le dossier de l'application a été déplacé. exePath : l'exe de l'overlay (jamais dotnet.exe).
    /// </summary>
    public static void Apply(bool enabled, string? exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(exePath)) return;
        // Lancé par « dotnet run » : le processus est dotnet.exe, l'inscrire démarrerait n'importe quoi.
        if (string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase)) return;

        string command = $"\"{exePath}\"";
        if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, command);
    }

    /// <summary>
    /// L'utilisateur peut désactiver l'entrée dans le Gestionnaire des tâches (Applications de démarrage) : Windows
    /// garde alors la valeur Run mais ne la lance plus. On le détecte pour l'expliquer au lieu de laisser croire que ça marche.
    /// </summary>
    public static bool IsBlockedByWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1; // 02/06 = actif, 03/07 = désactivé
        }
        catch { return false; }
    }
}
