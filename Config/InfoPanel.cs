using System.Diagnostics;
using D4Overlay.Shared;

namespace D4OverlayConfig;

/// <summary>Onglet « Infos » : état de l'overlay, raccourcis, crédits, contact, soutien et langues.</summary>
internal sealed class InfoPanel : FlowLayoutPanel
{
    private readonly Label _overlayInfo = new()
    {
        AutoSize = true,
        MaximumSize = new Size(690, 0),
        Margin = new Padding(3, 2, 3, 2)
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    public InfoPanel()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(6);

        // ── État de l'overlay (rafraîchi toutes les 2 s)
        Controls.Add(Title(Loc.T("info.overlay.title")));
        Controls.Add(_overlayInfo);

        // ── À propos
        Controls.Add(Title(Loc.T("info.about.title")));
        Controls.Add(Para(Loc.T("info.version", AppInfo.Version)));
        Controls.Add(Para(Loc.T("info.credit")));
        // Une seule ligne de texte dont seul le nom du site est cliquable : même alignement que les autres lignes.
        string dataBy = Loc.T("info.dataBy") + " " + AppInfo.DataSourceName;
        var sourceLink = new LinkLabel
        {
            Text = dataBy,
            LinkArea = new LinkArea(dataBy.Length - AppInfo.DataSourceName.Length, AppInfo.DataSourceName.Length),
            AutoSize = true,
            MaximumSize = new Size(690, 0),
            Margin = new Padding(3, 2, 3, 2)
        };
        sourceLink.LinkClicked += (_, _) => Open(AppInfo.DataSourceUrl);
        Controls.Add(sourceLink);

        // ── Mentions légales : non-affiliation et licences
        Controls.Add(Title(Loc.T("info.legal.title")));
        Controls.Add(Para(Loc.T("info.legal.text")));
        var licenses = new Button
        {
            Text = Loc.T("info.legal.button"),
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3)
        };
        licenses.Click += (_, _) => OpenLicenses();
        Controls.Add(licenses);

        // ── Contact
        Controls.Add(Title(Loc.T("info.contact.title")));
        Controls.Add(Para(Loc.T("info.contact.text")));
        var mail = new LinkLabel { Text = AppInfo.ContactEmail, AutoSize = true, Margin = new Padding(3, 0, 3, 2) };
        mail.LinkClicked += (_, _) => Open("mailto:" + AppInfo.ContactEmail);
        Controls.Add(mail);

        // ── Soutien
        Controls.Add(Title(Loc.T("info.donate.title")));
        Controls.Add(Para(Loc.T("info.donate.text")));
        var donate = new Button
        {
            Text = Loc.T("info.donate.button"),
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(3, 6, 3, 3)
        };
        donate.Click += (_, _) => Open(AppInfo.DonateUrl);
        Controls.Add(donate);

        // ── Diagnostic : accès aux journaux, à joindre à un message en cas de problème
        Controls.Add(Title(Loc.T("info.logs.title")));
        Controls.Add(Para(Loc.T("info.logs.text")));
        var logsFolder = new Button
        {
            Text = Loc.T("info.logs.button"),
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3)
        };
        logsFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(ConfigStore.AppDir);
            Open(ConfigStore.AppDir);
        };
        Controls.Add(logsFolder);

        // ── Langues : comment en ajouter une
        Controls.Add(Title(Loc.T("info.lang.title")));
        Controls.Add(Para(Loc.T("info.lang.text")));
        var langFolder = new Button
        {
            Text = Loc.T("info.lang.button"),
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3)
        };
        langFolder.Click += (_, _) => Open(Loc.ExportReferenceFiles());
        Controls.Add(langFolder);
        Controls.Add(new Panel { Width = 1, Height = 16, Margin = Padding.Empty }); // marge basse : le dernier bouton n'est plus rogné en bas du défilement

        _timer.Tick += (_, _) => RefreshOverlayInfo();
        _timer.Start();
        Disposed += (_, _) => _timer.Dispose();
        RefreshOverlayInfo();
    }

    /// <summary>État de l'overlay (publié par l'overlay dans status.json) + rappel des raccourcis.</summary>
    private void RefreshOverlayInfo()
    {
        string overlay, source;
        string? game = null;
        var keys = SettingsStore.LoadOrDefaults().Hotkeys;
        if (StatusStore.TryRead(out var st) && st is not null && StatusStore.IsRunning(st))
        {
            overlay = Loc.T(!st.OverlayVisible ? "info.state.hidden"
                            : st.EditMode ? "info.state.editing" : "info.state.running",
                            HotkeySettings.Show(keys.MoveMode));
            if (st.AlertsMuted) overlay += " · " + Loc.T("info.state.muted");
            if (st.GameRunning is bool running)
                game = Loc.T("info.game", Loc.T(running ? "info.game.yes" : "info.game.no"));
            source = st.Source switch
            {
                "API" => Loc.T("info.source.api")
                         + (st.LastApiSuccess is DateTime t ? Loc.T("info.source.api.time", t) : ""),
                "cache" => Loc.T("info.source.cache"),
                _ => Loc.T("info.source.cadence")
            };
        }
        else
        {
            overlay = Loc.T("info.state.stopped");
            source = "—";
        }

        _overlayInfo.Text =
            Loc.T("info.status", overlay) + "\n" +
            Loc.T("info.source", source) + "\n" +
            (game is null ? "" : game + "\n") +
            Loc.T("info.keys", HotkeySettings.Show(keys.MoveMode), HotkeySettings.Show(keys.Refresh));
    }

    private static void OpenLicenses()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "LICENSES.txt");
        if (File.Exists(path)) Open(path);
        else MessageBox.Show(Loc.T("info.legal.missing"), Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(Loc.T("info.openFail", target),
                Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Margin = new Padding(3, 14, 3, 4)
    };

    private static Label Para(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(690, 0),
        Margin = new Padding(3, 2, 3, 2)
    };
}
