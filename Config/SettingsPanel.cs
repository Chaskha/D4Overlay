using System.Runtime.InteropServices;
using D4Overlay.Shared;

namespace D4OverlayConfig;

/// <summary>Onglet « Configuration » : démarrage avec Windows, puis les raccourcis clavier (une touche ou combinaison par action, avec détection des doublons et des conflits).</summary>
internal sealed class SettingsPanel : FlowLayoutPanel
{
    private static readonly (string Name, string LabelKey)[] Actions =
    {
        ("MoveMode", "hk.move"),
        ("Refresh", "hk.refresh"),
        ("Mute", "hk.mute"),
        ("OpacityUp", "hk.opacityUp"),
        ("OpacityDown", "hk.opacityDown"),
    };

    private readonly Action _changed;
    private readonly CheckBox _showOverlay = new()
    {
        Text = Loc.T("set.show"),
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 0)
    };
    private readonly CheckBox _startup = new()
    {
        Text = Loc.T("set.startup"),
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 0)
    };
    private readonly Label _startupWarning = new()
    {
        AutoSize = true,
        MaximumSize = new Size(690, 0),
        ForeColor = Color.Firebrick,
        Margin = new Padding(24, 0, 3, 6),
        Visible = false
    };
    private readonly Dictionary<string, HotkeyBox> _boxes = new();
    private readonly Dictionary<string, Label> _warnings = new();
    private readonly HashSet<string> _rejected = new(); // touche refusée parce qu'elle exige Ctrl ou Alt
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    public SettingsPanel(HotkeySettings settings, bool overlayVisible, bool startWithWindows, Action changed)
    {
        _changed = changed;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(6);

        // ── Général : affichage de l'overlay, puis démarrage avec Windows
        Controls.Add(Title(Loc.T("set.general.title")));
        _showOverlay.Checked = overlayVisible;
        Controls.Add(_showOverlay);
        Controls.Add(new Label
        {
            Text = Loc.T("set.show.hint"),
            AutoSize = true,
            MaximumSize = new Size(690, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(24, 0, 3, 8)
        });
        _showOverlay.CheckedChanged += (_, _) => _changed();

        _startup.Checked = startWithWindows;
        Controls.Add(_startup);
        Controls.Add(new Label
        {
            Text = Loc.T("set.startup.hint", Loc.T("set.show")),
            AutoSize = true,
            MaximumSize = new Size(690, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(24, 0, 3, 4)
        });
        Controls.Add(_startupWarning);
        _startup.CheckedChanged += (_, _) =>
        {
            _changed();
            RefreshWarnings();
        };

        // ── Raccourcis clavier
        Controls.Add(Title(Loc.T("set.shortcuts.title")));
        Controls.Add(new Label
        {
            Text = Loc.T("hk.hint"),
            AutoSize = true,
            MaximumSize = new Size(690, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(3, 2, 3, 14)
        });

        foreach (var (name, labelKey) in Actions)
        {
            var box = new HotkeyBox();
            var clear = new Button { Text = Loc.T("hk.clear"), AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
            clear.Click += (_, _) =>
            {
                box.Value = "";
                _changed();
                RefreshWarnings();
            };
            box.ValueChanged += (_, _) =>
            {
                _rejected.Remove(name);
                _changed();
                RefreshWarnings();
            };
            box.Rejected += (_, _) =>
            {
                _rejected.Add(name);
                RefreshWarnings();
            };

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(new Label
            {
                Text = Loc.T(labelKey),
                AutoSize = false,
                Width = 330,
                Height = 22,
                Margin = new Padding(3, 7, 3, 0)
            });
            row.Controls.Add(box);
            row.Controls.Add(clear);
            Controls.Add(row);

            var warning = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                ForeColor = Color.Firebrick,
                Margin = new Padding(336, 0, 3, 8),
                Visible = false
            };
            Controls.Add(warning);

            _boxes[name] = box;
            _warnings[name] = warning;
        }

        var reset = new Button { Text = Loc.T("hk.reset"), AutoSize = true, Margin = new Padding(3, 14, 3, 3) };
        reset.Click += (_, _) =>
        {
            SetSettings(new HotkeySettings());
            _changed();
        };
        Controls.Add(reset);
        Controls.Add(new Panel { Width = 1, Height = 16, Margin = Padding.Empty }); // marge basse du défilement

        SetSettings(settings);

        // L'overlay publie les raccourcis que Windows a refusés ; on les affiche en direct.
        _timer.Tick += (_, _) => RefreshWarnings();
        _timer.Start();
        Disposed += (_, _) => _timer.Dispose();
    }

    /// <summary>Case « Afficher l'overlay à l'écran » (réglage de settings.json) : décochée, seules les alertes sonores restent.</summary>
    public bool OverlayVisible
    {
        get => _showOverlay.Checked;
        set => _showOverlay.Checked = value;
    }

    /// <summary>Case « Lancer l'overlay au démarrage de Windows » (réglage de settings.json).</summary>
    public bool StartWithWindows
    {
        get => _startup.Checked;
        set => _startup.Checked = value;
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Margin = new Padding(3, 10, 3, 4)
    };

    public HotkeySettings ToSettings()
    {
        var settings = new HotkeySettings();
        foreach (var (name, _) in Actions) settings.Set(name, _boxes[name].Value);
        return settings;
    }

    public void SetSettings(HotkeySettings settings)
    {
        foreach (var (name, _) in Actions) _boxes[name].Value = settings.Get(name);
        RefreshWarnings();
    }

    /// <summary>La même touche pour deux actions : l'une des deux ne fonctionnerait pas.</summary>
    public bool HasDuplicates() => DuplicateValues().Count > 0;

    private HashSet<string> DuplicateValues()
        => _boxes.Values.Select(b => b.Value).Where(v => v.Length > 0)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void RefreshWarnings()
    {
        // Windows peut avoir désactivé l'entrée de démarrage (Gestionnaire des tâches) : on le dit.
        bool blocked = _startup.Checked && StartupRegistration.IsBlockedByWindows();
        _startupWarning.Text = blocked ? Loc.T("set.startup.blocked") : "";
        _startupWarning.Visible = blocked;

        var duplicates = DuplicateValues();

        // Conflit avec une autre application : on ne se fie qu'à ce que l'overlay a réellement essayé d'enregistrer
        // (les touches enregistrées dans settings.json), pas à une saisie en cours.
        var failed = new HashSet<string>();
        if (StatusStore.TryRead(out var status) && status is not null && StatusStore.IsRunning(status))
        {
            var saved = SettingsStore.LoadOrDefaults().Hotkeys;
            foreach (var name in status.HotkeyFailed)
                if (_boxes.TryGetValue(name, out var box)
                    && string.Equals(box.Value, saved.Get(name), StringComparison.OrdinalIgnoreCase))
                    failed.Add(name);
        }

        foreach (var (name, _) in Actions)
        {
            string value = _boxes[name].Value;
            string text = _rejected.Contains(name) ? Loc.T("hk.needsModifier")
                        : value.Length > 0 && duplicates.Contains(value) ? Loc.T("hk.duplicate")
                        : failed.Contains(name) ? Loc.T("hk.conflict")
                        : "";
            _warnings[name].Text = text;
            _warnings[name].Visible = text.Length > 0;
        }
    }
}

/// <summary>Champ qui capture la touche pressée (avec Ctrl / Alt / Maj) au lieu d'écrire du texte.</summary>
internal sealed class HotkeyBox : TextBox
{
    private string _value = "";
    private bool _awaiting; // le champ a le focus et attend une touche
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 40 };
    private DateTime _pulseStart;

    public event EventHandler? ValueChanged;

    /// <summary>Touche pressée mais refusée (par exemple une lettre seule).</summary>
    public event EventHandler? Rejected;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Center;
        Width = 170;
        Cursor = Cursors.Hand;
        ShowValue();

        _pulse.Tick += (_, _) => UpdatePulse();
        Disposed += (_, _) => _pulse.Dispose();
    }

    // Repère visuel pour les utilisateurs peu habitués : tant que le champ attend une touche, il pulse doucement
    // et affiche « Appuie sur une touche… ». Dès qu'il perd le focus, tout redevient normal.
    [DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Le champ est en lecture seule, mais Windows lui dessine quand même un curseur clignotant : on le masque chaque
        // fois qu'il pourrait réapparaître (focus, clic, changement de texte ou de sélection, frappe, repeinture).
        if (m.Msg is 0x0007 or 0x000C or 0x000F or 0x00B1 or 0x0100 or 0x0101 or 0x0201 or 0x0202)
            HideCaret(Handle);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _awaiting = true;
        ShowValue();
        SelectionStart = 0;
        SelectionLength = 0; // pas de texte surligné : il masquerait l'effet
        _pulseStart = DateTime.UtcNow;
        _pulse.Start();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _awaiting = false;
        _pulse.Stop();
        BackColor = AppTheme.P.Input;
        ShowValue();
    }

    private void UpdatePulse()
    {
        double t = (DateTime.UtcNow - _pulseStart).TotalSeconds;
        double k = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 1.4); // 0 → 1 → 0 en 1,4 s, sans à-coup
        BackColor = Blend(AppTheme.P.Input, AppTheme.P.Accent, 0.28 * k); // « légèrement » : au plus 28 % de la couleur d'accent
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    /// <summary>« F8 », « Ctrl+Alt+M »… ou vide. Le changer par code ne déclenche pas ValueChanged.</summary>
    public string Value
    {
        get => _value;
        set
        {
            _value = value ?? "";
            ShowValue();
        }
    }

    private void ShowValue()
        => Text = _awaiting ? Loc.T("hk.press") : _value.Length == 0 ? Loc.T("hk.none") : _value;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return true; // modificateur seul : on attend la touche suivante

        // Tab (et Maj+Tab) restent la navigation au clavier de la fenêtre.
        if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift)) return base.ProcessCmdKey(ref msg, keyData);

        if (keyData == Keys.Back || keyData == Keys.Delete)
        {
            Apply("");
            return true;
        }

        string candidate = HotkeySpec.Format(keyData);
        if (HotkeySpec.TryParse(candidate, out _)) Apply(candidate);
        else Rejected?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Apply(string value)
    {
        _awaiting = false; // la touche est prise en compte : on montre sa valeur (le champ continue de pulser tant qu'il a le focus)
        _value = value;
        ShowValue();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
