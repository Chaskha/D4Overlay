using System.Runtime.InteropServices;

namespace D4Overlay.Shared;

/// <summary>
/// Message Windows envoyé par le configurateur à l'overlay juste après un enregistrement : l'overlay relit sa configuration
/// tout de suite, sans attendre son contrôle périodique (10 s). L'overlay répond en publiant l'heure d'application
/// (OverlayStatus.ConfigAppliedAt), ce qui permet au configurateur d'afficher une vraie confirmation.
/// </summary>
public static class OverlayMessages
{
    private const int HwndBroadcast = 0xFFFF;

    public static readonly uint Reload = RegisterWindowMessage("D4Overlay.Overlay.Reload");

    /// <summary>Demande à l'overlay (s'il tourne) de relire sa configuration maintenant.</summary>
    public static void SignalReload()
    {
        try { PostMessage((IntPtr)HwndBroadcast, Reload, IntPtr.Zero, IntPtr.Zero); }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
