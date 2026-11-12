using D4Overlay.Shared;

namespace D4WorldBossOverlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Une seule instance : sinon les alarmes sonneraient en double et F5 / F8 ne seraient attribués qu'à la première.
        using var instance = new SingleInstance("Overlay");
        if (!instance.IsFirst)
        {
            // Message affiché par la seconde instance elle-même : il ne dépend pas des réglages de notification de Windows
            // (assistant de concentration, mode jeu…) et reste utile quand l'overlay est masqué.
            ApplicationConfiguration.Initialize();
            MessageBox.Show(Loc.T("app.alreadyRunning"), "D4 Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Journal des erreurs : une exception ne doit pas faire disparaître l'overlay sans laisser de trace.
        CrashLog.Install("Overlay");
        ConfigStore.EnsureDefaults();
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new OverlayForm());
        }
        finally
        {
            CrashLog.MarkCleanExit();
        }
    }
}
