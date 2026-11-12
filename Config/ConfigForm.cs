using System.Diagnostics;
using D4Overlay.Shared;
using Microsoft.Win32;

namespace D4OverlayConfig;

public sealed class ConfigForm : Form
{
    private static string BaseTitle => Loc.T("app.title");

    private TabHost _tabs = new();
    private readonly Dictionary<string, EventPanel> _panels = new();
    private Label _status = new();
    private AppearancePanel _appearance = null!;
    private SettingsPanel _settingsTab = null!;
    private bool _dirty;

    // Retour d'information après « Enregistrer » : « application en cours… » (clignotant) → « ✓ appliqué » → message final.
    private enum ApplyPhase { None, Waiting, Confirmed }
    private readonly System.Windows.Forms.Timer _applyTimer = new() { Interval = 350 };
    private ApplyPhase _phase = ApplyPhase.None;
    private DateTime _savedAt;        // heure locale, pour l'affichage
    private DateTime _saveDoneUtc;    // fin de l'enregistrement : l'accusé de l'overlay doit être postérieur
    private DateTime _phaseUntilUtc;  // fin du délai d'attente, puis de l'affichage « appliqué »
    private bool _blink;

    // Message Windows envoyé par une seconde instance qu'on vient de refuser (0 = aucun).
    private readonly uint _activateMessage;

    public ConfigForm(uint activateMessage = 0)
    {
        _activateMessage = activateMessage;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 640);
        MinimumSize = new Size(760, 560);
        FormClosing += OnFormClosing;

        // Thème « Système » : on suit Windows en direct (l'utilisateur change son mode clair/sombre pendant que la fenêtre est ouverte).
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        FormClosed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _applyTimer.Dispose();
        };
        _applyTimer.Tick += (_, _) => OnApplyTick();

        // Icône de l'application (celle de l'exe) dans la barre de titre et la barre des tâches.
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var startup = SettingsStore.LoadOrDefaults();
        if (startup.StartWithWindows) ApplyStartup(true); // recale le chemin si le dossier a été déplacé
        BuildUi(ConfigStore.LoadOrDefaults(), LayoutStore.LoadOrDefaults(),
            startup.OverlayVisible, startup.Hotkeys, startup.StartWithWindows, 0, false);
    }

    protected override void WndProc(ref Message m)
    {
        if (_activateMessage != 0 && (uint)m.Msg == _activateMessage)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AppTheme.ApplyTitleBar(this);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || AppTheme.Mode != "system" || !IsHandleCreated) return;
        if (AppTheme.SystemUsesDark() == AppTheme.IsDark) return; // un autre réglage de Windows a changé : rien à faire
        BeginInvoke(new Action(() => Rebuild(() => AppTheme.SetMode("system"))));
    }

    /// <summary>(Re)construit toute l'interface (langue et thème courants), à partir de l'état fourni.</summary>
    private void BuildUi(Dictionary<string, EventAlertConfig> cfg, OverlayLayout layout, bool overlayVisible, HotkeySettings hotkeys, bool startWithWindows, int selectedTab, bool dirty)
    {
        SuspendLayout();
        foreach (var old in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(old);
            old.Dispose();
        }
        _panels.Clear();

        _tabs = new TabHost { Dock = DockStyle.Fill };
        _status = new Label
        {
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            ForeColor = Color.DimGray
        };

        foreach (var label in ConfigStore.EventLabels)
        {
            var panel = new EventPanel(label, cfg[label], MarkDirty);
            _tabs.Add(Loc.EventName(label), panel);
            _panels[label] = panel;
        }
        _appearance = new AppearancePanel(layout, MarkDirty);
        _tabs.Add(Loc.T("tab.appearance"), _appearance);
        _settingsTab = new SettingsPanel(hotkeys, overlayVisible, startWithWindows, MarkDirty);
        _tabs.Add(Loc.T("tab.settings"), _settingsTab);
        _tabs.Add(Loc.T("tab.info"), new InfoPanel());

        // Une seule ligne : les libellés sont courts (la croix de la fenêtre sert à fermer).
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(6)
        };
        buttons.Controls.Add(MakeButton(Loc.T("btn.save"), (_, _) => Save()));
        buttons.Controls.Add(MakeButton(Loc.T("btn.importSound"), (_, _) => ImportSounds()));
        buttons.Controls.Add(MakeButton(Loc.T("btn.soundsFolder"), (_, _) => OpenSoundsFolder()));
        buttons.Controls.Add(MakeButton(Loc.T("btn.defaults"), (_, _) => ResetDefaults()));
        buttons.Controls.Add(BuildPickers());

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_tabs, 0, 0);
        root.Controls.Add(_status, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        AppTheme.Apply(this);
        ResumeLayout(true);
        AppTheme.ApplyTitleBar(this);

        _tabs.SelectedIndex = Math.Clamp(selectedTab, 0, _tabs.Count - 1);
        _dirty = dirty;
        Text = dirty ? BaseTitle + " *" : BaseTitle;
        _status.Text = dirty ? Loc.T("status.dirty") : Loc.T("status.none");
    }

    /// <summary>
    /// Langue (🌐) et thème (◐), toujours visibles. Langue : seul le code est affiché dans la zone fermée (EN, FR…)
    /// pour gagner de la place ; la liste déroulante, plus large, montre le nom complet.
    /// </summary>
    private Control BuildPickers()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(14, 0, 0, 0)
        };

        panel.Controls.Add(new Label { Text = "🌐", AutoSize = true, Margin = new Padding(0, 9, 0, 0) });
        var language = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 17,
            Width = 70,
            DropDownWidth = 220,
            Margin = new Padding(2, 6, 10, 3)
        };
        foreach (var lang in Loc.Available())
            language.Items.Add(new LanguageItem(lang.Code, lang.Name));
        language.SelectedItem = language.Items.Cast<LanguageItem>()
            .FirstOrDefault(i => string.Equals(i.Code, Loc.Code, StringComparison.OrdinalIgnoreCase));
        language.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            if (language.Items[e.Index] is not LanguageItem item) return;
            bool inEditArea = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool hot = !inEditArea && (e.State & DrawItemState.Selected) != 0;
            var back = hot ? (AppTheme.IsDark ? AppTheme.P.Accent : SystemColors.Highlight) : AppTheme.P.Input;
            var fore = hot ? Color.White : AppTheme.P.Text;
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, inEditArea ? item.Short : item.Long, language.Font, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        language.SelectedIndexChanged += (_, _) =>
        {
            if (language.SelectedItem is not LanguageItem item
                || string.Equals(item.Code, Loc.Code, StringComparison.OrdinalIgnoreCase)) return;
            string code = item.Code;
            // Pas pendant l'évènement : ce sélecteur va être détruit et reconstruit.
            BeginInvoke(new Action(() => ChangeLanguage(code)));
        };
        panel.Controls.Add(language);

        panel.Controls.Add(new Label { Text = "◐", AutoSize = true, Margin = new Padding(0, 9, 0, 0) });
        var theme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            Margin = new Padding(2, 6, 3, 3)
        };
        theme.Items.Add(new ThemeItem("system", Loc.T("theme.system")));
        theme.Items.Add(new ThemeItem("light", Loc.T("theme.light")));
        theme.Items.Add(new ThemeItem("dark", Loc.T("theme.dark")));
        theme.SelectedItem = theme.Items.Cast<ThemeItem>().FirstOrDefault(i => i.Code == AppTheme.Mode);
        theme.SelectedIndexChanged += (_, _) =>
        {
            if (theme.SelectedItem is not ThemeItem item || item.Code == AppTheme.Mode) return;
            string mode = item.Code;
            BeginInvoke(new Action(() => ChangeTheme(mode)));
        };
        panel.Controls.Add(theme);

        return panel;
    }

    /// <summary>Reconstruit l'interface sans rien perdre : l'état en cours (même non enregistré) est repris tel quel.</summary>
    private void Rebuild(Action change)
    {
        var map = new Dictionary<string, EventAlertConfig>();
        foreach (var label in ConfigStore.EventLabels) map[label] = _panels[label].ToConfig();
        var layout = _appearance.ToLayout();
        bool overlayVisible = _settingsTab.OverlayVisible; // choix en cours, même non enregistré
        var hotkeys = _settingsTab.ToSettings();
        bool startWithWindows = _settingsTab.StartWithWindows;
        int tab = _tabs.SelectedIndex;
        bool dirty = _dirty;

        change();
        BuildUi(map, layout, overlayVisible, hotkeys, startWithWindows, tab, dirty);
    }

    private void ChangeLanguage(string code) => Rebuild(() =>
    {
        Loc.SetLanguage(code);
        SaveSetting(s => s.Language = Loc.Code);
    });

    private void ChangeTheme(string mode) => Rebuild(() =>
    {
        AppTheme.SetMode(mode);
        SaveSetting(s => s.Theme = AppTheme.Mode);
    });

    /// <summary>
    /// Inscrit ou retire le démarrage de l'overlay avec Windows. L'exe de l'overlay doit se trouver à côté de celui-ci
    /// (paquet distribué) ; sinon l'overlay s'inscrira lui-même, avec son propre chemin, en relisant settings.json.
    /// </summary>
    private static void ApplyStartup(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                StartupRegistration.Apply(false, null);
                return;
            }
            string overlay = Path.Combine(AppContext.BaseDirectory, "D4WorldBossOverlay.exe");
            if (File.Exists(overlay)) StartupRegistration.Apply(true, overlay);
        }
        catch (Exception ex) { AppLog.Write("démarrage avec Windows : " + ex.Message); }
    }

    // ───────────── Retour d'information après l'enregistrement ─────────────

    private static bool OverlayIsRunning()
        => StatusStore.TryRead(out var status) && status is not null && StatusStore.IsRunning(status);

    private string SavedFallbackText()
        => Loc.T("status.saved", _savedAt, HotkeySettings.Show(_settingsTab.ToSettings().Refresh));

    /// <summary>
    /// Enregistré : on demande à l'overlay d'appliquer TOUT DE SUITE (au lieu d'attendre son contrôle périodique), puis on
    /// affiche « application en cours » en clignotant jusqu'à ce qu'il confirme. Sans ce retour, un raccourci modifié puis testé
    /// aussitôt semblerait « ne pas marcher ».
    /// </summary>
    private void BeginApplyFeedback()
    {
        _savedAt = DateTime.Now;
        _saveDoneUtc = DateTime.UtcNow;

        if (!OverlayIsRunning())
        {
            StopApplyFeedback();
            _status.Text = Loc.T("status.savedOnly", _savedAt);
            return;
        }

        _phase = ApplyPhase.Waiting;
        _phaseUntilUtc = _saveDoneUtc.AddSeconds(6);
        _blink = false;
        _status.Text = Loc.T("status.applying");
        _applyTimer.Start();
        OverlayMessages.SignalReload();
    }

    private void StopApplyFeedback()
    {
        _phase = ApplyPhase.None;
        _applyTimer.Stop();
        _status.ForeColor = AppTheme.P.Dim; // couleur normale de la ligne d'état
    }

    private void OnApplyTick()
    {
        switch (_phase)
        {
            case ApplyPhase.Waiting:
                // Clignotement doux : le texte alterne entre la couleur discrète et la couleur d'accent.
                _blink = !_blink;
                _status.ForeColor = _blink ? AppTheme.P.Accent : AppTheme.P.Dim;

                if (StatusStore.TryRead(out var status) && status?.ConfigAppliedAt is DateTime applied && applied >= _saveDoneUtc)
                {
                    _phase = ApplyPhase.Confirmed;
                    _phaseUntilUtc = DateTime.UtcNow.AddSeconds(2);
                    _status.ForeColor = AppTheme.IsDark ? Color.FromArgb(110, 210, 130) : Color.FromArgb(30, 135, 60);
                    _status.Text = Loc.T("status.applied");
                }
                else if (DateTime.UtcNow > _phaseUntilUtc)
                {
                    // Pas de confirmation : l'overlay est peut-être occupé ; il appliquera au plus tard dans 10 s.
                    StopApplyFeedback();
                    _status.Text = SavedFallbackText();
                }
                break;

            case ApplyPhase.Confirmed:
                if (DateTime.UtcNow >= _phaseUntilUtc)
                {
                    StopApplyFeedback();
                    _status.Text = Loc.T("status.savedApplied", _savedAt);
                }
                break;
        }
    }

    /// <summary>On relit settings.json avant d'y écrire : jamais d'écrasement des autres réglages enregistrés.</summary>
    private static void SaveSetting(Action<AppSettings> edit)
    {
        try
        {
            var settings = SettingsStore.LoadOrDefaults();
            edit(settings);
            SettingsStore.Save(settings);
        }
        catch { }
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(4)
        };
        b.Click += onClick;
        return b;
    }

    private void MarkDirty()
    {
        _dirty = true;
        Text = BaseTitle + " *";
        StopApplyFeedback(); // une nouvelle modification : l'ancien message n'a plus lieu d'être
        _status.Text = Loc.T("status.dirty");
    }

    private bool Save()
    {
        if (_settingsTab.HasDuplicates())
        {
            _tabs.SelectContent(_settingsTab);
            MessageBox.Show(this, Loc.T("hk.duplicate"), Loc.T("tab.settings"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var map = new Dictionary<string, EventAlertConfig>();
        foreach (var label in ConfigStore.EventLabels)
        {
            var cfg = _panels[label].ToConfig();
            var dups = cfg.Levels.GroupBy(l => l.Minutes)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dups.Count > 0)
            {
                _tabs.SelectContent(_panels[label]);
                MessageBox.Show(this,
                    Loc.T("dlg.dup.text", Loc.EventName(label), string.Join(", ", dups)),
                    Loc.T("dlg.dup.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            map[label] = cfg;
        }

        try
        {
            ConfigStore.Save(map);
            LayoutStore.Save(_appearance.ToLayout());
            var settings = SettingsStore.LoadOrDefaults();
            settings.OverlayVisible = _settingsTab.OverlayVisible;
            settings.Hotkeys = _settingsTab.ToSettings();
            settings.StartWithWindows = _settingsTab.StartWithWindows;
            SettingsStore.Save(settings);
            ApplyStartup(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("dlg.saveError", ex.Message),
                Loc.T("dlg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _dirty = false;
        Text = BaseTitle;
        BeginApplyFeedback();
        return true;
    }

    private void ImportSounds()
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("dlg.import.title"),
            Filter = Loc.T("dlg.import.filter"),
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Directory.CreateDirectory(ConfigStore.SoundsDir);
            int copied = 0;
            foreach (var src in dlg.FileNames)
            {
                string dest = Path.Combine(ConfigStore.SoundsDir, Path.GetFileName(src));
                if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest),
                        StringComparison.OrdinalIgnoreCase))
                    continue; // déjà dans le dossier des sons

                if (File.Exists(dest))
                {
                    var answer = MessageBox.Show(this,
                        Loc.T("dlg.exists.text", Path.GetFileName(src)),
                        Loc.T("dlg.exists.sound"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) continue;
                }
                File.Copy(src, dest, overwrite: true);
                copied++;
            }

            foreach (var p in _panels.Values) p.RefreshSounds();
            _status.Text = Loc.T("status.imported", copied);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("dlg.importError", ex.Message),
                Loc.T("dlg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenSoundsFolder()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.SoundsDir);
            Process.Start(new ProcessStartInfo(ConfigStore.SoundsDir) { UseShellExecute = true });
        }
        catch { }
    }

    private void ResetDefaults()
    {
        var answer = MessageBox.Show(this,
            Loc.T("dlg.defaults.text"),
            Loc.T("btn.defaults"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        var defaults = ConfigStore.CreateDefaults();
        foreach (var label in ConfigStore.EventLabels)
            _panels[label].SetConfig(defaults[label]);
        _appearance.SetLayout(OverlayLayout.CreateDefault());
        _settingsTab.OverlayVisible = true;
        _settingsTab.SetSettings(new HotkeySettings());
        _settingsTab.StartWithWindows = false;
        MarkDirty();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty) return;
        var answer = MessageBox.Show(this, Loc.T("dlg.quit"),
            BaseTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) e.Cancel = true;
        else if (answer == DialogResult.Yes && !Save()) e.Cancel = true;
    }
}

/// <summary>Contenu d'un onglet : affichage, alarmes, liste de paliers.</summary>
internal sealed class EventPanel : TableLayoutPanel
{
    private readonly Action _changed;
    private readonly CheckBox _visible = new()
    {
        Text = Loc.T("ev.visible"),
        AutoSize = true
    };
    private readonly CheckBox _alarms = new()
    {
        Text = Loc.T("ev.alarms"),
        AutoSize = true
    };
    private readonly CheckBox _onlyInGame = new()
    {
        Text = Loc.T("ev.onlyInGame"),
        AutoSize = true,
        Margin = new Padding(24, 3, 3, 0)
    };
    private readonly Label _onlyInGameHint = new()
    {
        Text = Loc.T("ev.onlyInGame.hint"),
        AutoSize = true,
        MaximumSize = new Size(690, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(24, 0, 3, 2)
    };
    private readonly Label _hint = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 4, 3, 4)
    };
    private readonly FlowLayoutPanel _rows = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true
    };
    private readonly Button _add = new()
    {
        Text = Loc.T("ev.addLevel"),
        AutoSize = true,
        Margin = new Padding(3, 8, 3, 3)
    };
    private bool _loading;

    public EventPanel(string label, EventAlertConfig cfg, Action changed)
    {
        _changed = changed;
        ColumnCount = 1;
        RowCount = 4;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        header.Controls.Add(_visible);
        header.Controls.Add(_alarms);
        header.Controls.Add(_onlyInGame);
        header.Controls.Add(_onlyInGameHint);
        header.Controls.Add(_hint);

        var title = new Label
        {
            Text = Loc.T("ev.levels", Loc.EventName(label)),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(3, 12, 3, 6)
        };

        Controls.Add(header, 0, 0);
        Controls.Add(title, 0, 1);
        Controls.Add(_rows, 0, 2);
        Controls.Add(_add, 0, 3);

        _visible.CheckedChanged += (_, _) => { UpdateEnabledState(); Changed(); };
        _alarms.CheckedChanged += (_, _) => { UpdateEnabledState(); Changed(); };
        _onlyInGame.CheckedChanged += (_, _) => Changed();
        _add.Click += (_, _) =>
        {
            AddRow(new AlertLevel { Minutes = 5, Sound = "exclamation" });
            Changed();
        };

        SetConfig(cfg);
    }

    private void Changed()
    {
        if (!_loading) _changed();
    }

    public void SetConfig(EventAlertConfig cfg)
    {
        _loading = true;
        _visible.Checked = cfg.Visible;
        _alarms.Checked = cfg.Enabled;
        _onlyInGame.Checked = cfg.OnlyInGame;

        foreach (var c in _rows.Controls.Cast<Control>().ToList())
        {
            _rows.Controls.Remove(c);
            c.Dispose();
        }
        foreach (var lvl in cfg.Levels.OrderByDescending(l => l.Minutes))
            AddRow(lvl);

        _loading = false;
        UpdateEnabledState();
    }

    public EventAlertConfig ToConfig() => new()
    {
        Visible = _visible.Checked,
        Enabled = _alarms.Checked,
        OnlyInGame = _onlyInGame.Checked,
        Levels = _rows.Controls.OfType<LevelRow>()
            .Select(r => r.ToLevel())
            .OrderByDescending(l => l.Minutes)
            .ToList()
    };

    public void RefreshSounds()
    {
        foreach (var r in _rows.Controls.OfType<LevelRow>()) r.RefreshSounds();
    }

    private void AddRow(AlertLevel level)
    {
        var row = new LevelRow(level, Changed, r =>
        {
            _rows.Controls.Remove(r);
            r.Dispose();
            Changed();
        });
        _rows.Controls.Add(row);
    }

    private void UpdateEnabledState()
    {
        _alarms.Enabled = _visible.Checked;
        bool active = _visible.Checked && _alarms.Checked;
        _onlyInGame.Enabled = active;
        _rows.Enabled = active;
        _add.Enabled = active;
        _hint.Text = !_visible.Checked
            ? Loc.T("ev.hint.hidden")
            : !_alarms.Checked
                ? Loc.T("ev.hint.silent")
                : "";
    }
}

internal sealed class SoundItem
{
    public string Id { get; }
    public string Label { get; }

    public SoundItem(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public override string ToString() => Label;
}

/// <summary>Une ligne : « Prévenir [N] min avant  [son ▾]  ▶ Tester  ✕ ».</summary>
internal sealed class LevelRow : FlowLayoutPanel
{
    private readonly NumericUpDown _minutes = new() { Minimum = 1, Maximum = 240, Width = 64 };
    private readonly ComboBox _sound = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 160
    };
    private readonly TrackBar _volume = new()
    {
        Minimum = 0,
        Maximum = 100,
        SmallChange = 5,
        LargeChange = 10,
        TickStyle = TickStyle.None,
        AutoSize = false,
        Size = new Size(80, 28),
        Margin = new Padding(3, 0, 3, 0)
    };
    private readonly Label _volumeText = new()
    {
        AutoSize = false,
        Size = new Size(40, 20),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 6, 3, 0)
    };
    private readonly Action _changed;
    private bool _filling;

    public LevelRow(AlertLevel level, Action changed, Action<LevelRow> remove)
    {
        _changed = changed;
        AutoSize = true;
        WrapContents = false;
        FlowDirection = FlowDirection.LeftToRight;
        Margin = new Padding(0, 2, 0, 2);

        _minutes.Value = Math.Clamp(level.Minutes, 1, 240);
        FillSounds(level.Sound);
        _volume.Value = Math.Clamp(level.Volume, 0, 100);
        _volumeText.Text = $"{_volume.Value} %";

        var test = new Button { Text = Loc.T("lvl.test"), AutoSize = true };
        var delete = new Button { Text = "✕", AutoSize = true };

        Controls.Add(Lbl(Loc.T("lvl.warn")));
        Controls.Add(_minutes);
        Controls.Add(Lbl(Loc.T("lvl.minBefore")));
        Controls.Add(_sound);
        Controls.Add(Lbl(Loc.T("lvl.vol")));
        Controls.Add(_volume);
        Controls.Add(_volumeText);
        Controls.Add(test);
        Controls.Add(delete);

        _minutes.ValueChanged += (_, _) => _changed();
        _sound.SelectedIndexChanged += (_, _) => { if (!_filling) _changed(); };
        _volume.ValueChanged += (_, _) =>
        {
            _volumeText.Text = $"{_volume.Value} %";
            _changed();
        };
        test.Click += (_, _) => AlertSounds.Play(SoundId, _volume.Value);
        delete.Click += (_, _) => remove(this);
    }

    private static Label Lbl(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 7, 3, 0)
    };

    private string SoundId => (_sound.SelectedItem as SoundItem)?.Id ?? "none";

    public AlertLevel ToLevel() => new()
    {
        Minutes = (int)_minutes.Value,
        Sound = SoundId,
        Volume = _volume.Value
    };

    public void RefreshSounds() => FillSounds(SoundId);

    private void FillSounds(string? selectedId)
    {
        _filling = true;
        try
        {
            if (string.IsNullOrWhiteSpace(selectedId)) selectedId = "none";

            _sound.Items.Clear();
            foreach (var b in AlertSounds.BuiltIns)
                _sound.Items.Add(new SoundItem(b.Id, AlertSounds.LabelFor(b.Id)));
            foreach (var wav in AlertSounds.ListSoundFiles())
                _sound.Items.Add(new SoundItem(wav, "♪ " + wav));

            var match = _sound.Items.Cast<SoundItem>()
                .FirstOrDefault(i => string.Equals(i.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                // Valeur inconnue (wav supprimé, ancien alias…) : on la garde plutôt que de la perdre en silence.
                match = new SoundItem(selectedId, Loc.T("common.notFound", selectedId));
                _sound.Items.Add(match);
            }
            _sound.SelectedItem = match;
        }
        finally
        {
            _filling = false;
        }
    }
}

internal sealed class LanguageItem
{
    public string Code { get; }
    public string Name { get; }

    public LanguageItem(string code, string name)
    {
        Code = code;
        Name = name;
    }

    /// <summary>Affichage compact (zone fermée du sélecteur) : EN, FR, PT-BR…</summary>
    public string Short => Code.ToUpperInvariant();

    /// <summary>Affichage dans la liste déroulante : « FR — Français ».</summary>
    public string Long => $"{Short} — {Name}";

    public override string ToString() => Name;
}

internal sealed class ThemeItem
{
    public string Code { get; }
    public string Name { get; }

    public ThemeItem(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public override string ToString() => Name;
}
