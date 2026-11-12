using D4Overlay.Shared;

namespace D4OverlayConfig;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new SingleInstance("Config");
        if (!instance.IsFirst)
        {
            instance.SignalFirst(); // la fenêtre déjà ouverte passe au premier plan
            return;
        }

        // Journal des erreurs + message à l'utilisateur (une seule fois par erreur distincte).
        CrashLog.Install("Config", (_, first) =>
        {
            if (first)
                MessageBox.Show(Loc.T("crash.message", CrashLog.LogPath), Loc.T("app.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        });
        ConfigStore.EnsureDefaults();
        ApplicationConfiguration.Initialize();
        Application.AddMessageFilter(new WheelRouter()); // la molette agit sous la souris, pas sur le champ qui a le focus
        AppTheme.SetMode(SettingsStore.LoadOrDefaults().Theme);
        try
        {
            Application.Run(new ConfigForm(instance.ActivateMessage));
        }
        finally
        {
            CrashLog.MarkCleanExit();
        }
    }
}
