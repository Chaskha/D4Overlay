using System.Drawing.Imaging;
using D4Overlay.Shared;

namespace D4OverlayConfig;

/// <summary>Onglet « Apparence » : image, police, espacement, couleurs, position des timers, avec aperçu et glisser-déposer.</summary>
internal sealed class AppearancePanel : TableLayoutPanel
{
    private readonly Action _changed;
    private readonly OverlayRenderer _renderer = new();
    private readonly PreviewBox _preview;

    private readonly ComboBox _skin = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
    private readonly ComboBox _font = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
    private readonly NumericUpDown _size = new() { Minimum = 8, Maximum = 48, Width = 56 };
    private readonly NumericUpDown _spacing = new()
    {
        Minimum = -3, Maximum = 12, DecimalPlaces = 1, Increment = 0.5M, Width = 60
    };
    private readonly CheckBox _bold = new() { Text = Loc.T("ap.bold"), AutoSize = true, Margin = new Padding(8, 5, 3, 3) };
    private readonly NumericUpDown _scale = new() { Minimum = 50, Maximum = 300, Increment = 10, Width = 60 };
    private readonly NumericUpDown _overlayOpacity = new() { Minimum = 20, Maximum = 100, Increment = 5, Width = 60 };
    private readonly Label _scaleInfo = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 2, 3, 4)
    };
    private readonly CheckBox _light = new() { Text = Loc.T("ap.light"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

    private readonly Dictionary<string, NumericUpDown> _x = new();
    private readonly Dictionary<string, NumericUpDown> _y = new();
    private readonly Dictionary<string, ColorButton> _timerColor = new();
    private readonly ColorButton _outline = new();
    private readonly ColorButton _fill = new();
    private readonly NumericUpDown _fillOpacity = new() { Minimum = 0, Maximum = 100, Increment = 5, Width = 60 };

    private readonly Label _fontInfo = new()
    {
        AutoSize = true,
        MaximumSize = new Size(320, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 4, 3, 0)
    };
    private OverlayLayout _layout;
    private bool _loading;

    private readonly Label _skinCleaned = Hint(""); // visible seulement si l'image importée a été nettoyée automatiquement

    public AppearancePanel(OverlayLayout layout, Action changed)
    {
        _changed = changed;
        _layout = layout.Clone();
        _preview = new PreviewBox(_renderer);

        ColumnCount = 2;
        RowCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 345));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Colonne de gauche : défilante, car il y a beaucoup de réglages.
        var left = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Dock = DockStyle.Fill
        };

        // ── Taille de l'overlay à l'écran (pour les écrans haute résolution)
        var scaleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            MaximumSize = new Size(325, 0),
            Margin = new Padding(0, 6, 0, 0)
        };
        scaleRow.Controls.Add(Lbl(Loc.T("ap.scale")));
        scaleRow.Controls.Add(_scale);
        left.Controls.Add(scaleRow);
        left.Controls.Add(_scaleInfo);

        // ── Opacité de tout l'overlay (cadre et texte)
        var opacityRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            MaximumSize = new Size(325, 0),
            Margin = new Padding(0, 4, 0, 0)
        };
        opacityRow.Controls.Add(Lbl(Loc.T("ap.overlayOpacity")));
        opacityRow.Controls.Add(_overlayOpacity);
        left.Controls.Add(opacityRow);
        left.Controls.Add(Hint(Loc.T("ap.overlayOpacity.hint", HotkeySettings.Show(SettingsStore.LoadOrDefaults().Hotkeys.MoveMode))));

        // ── Image de l'overlay
        left.Controls.Add(Title(Loc.T("ap.skin.title")));
        left.Controls.Add(_skin);
        var skinButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(325, 0), Margin = new Padding(0, 4, 0, 0) };
        var import = new Button { Text = Loc.T("ap.skin.import"), AutoSize = true };
        var openFolder = new Button { Text = Loc.T("ap.skin.folder"), AutoSize = true };
        import.Click += (_, _) => ImportSkin();
        openFolder.Click += (_, _) => OpenSkinsFolder();
        skinButtons.Controls.Add(import);
        skinButtons.Controls.Add(openFolder);
        left.Controls.Add(skinButtons);
        left.Controls.Add(Hint(Loc.T("ap.skin.hint")));
        _skinCleaned.Visible = false;
        left.Controls.Add(_skinCleaned);

        // ── Police
        left.Controls.Add(Title(Loc.T("ap.font.title")));
        left.Controls.Add(_font);
        var fontButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(325, 0), Margin = new Padding(0, 4, 0, 0) };
        var fontFolder = new Button { Text = Loc.T("ap.font.folder"), AutoSize = true };
        var fontReload = new Button { Text = Loc.T("ap.font.reload"), AutoSize = true };
        fontFolder.Click += (_, _) => OpenFontsFolder();
        fontReload.Click += (_, _) =>
        {
            FontCatalog.Rescan();
            FillFonts(_layout.FontFamily);
        };
        fontButtons.Controls.Add(fontFolder);
        fontButtons.Controls.Add(fontReload);
        left.Controls.Add(fontButtons);
        left.Controls.Add(_fontInfo);
        var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(325, 0), Margin = new Padding(0, 6, 0, 0) };
        fontRow.Controls.Add(Lbl(Loc.T("ap.size")));
        fontRow.Controls.Add(_size);
        fontRow.Controls.Add(Lbl(Loc.T("ap.spacing")));
        fontRow.Controls.Add(_spacing);
        fontRow.Controls.Add(_bold);
        left.Controls.Add(fontRow);

        // ── Timers : position et couleur
        left.Controls.Add(Title(Loc.T("ap.timers.title")));
        foreach (var label in ConfigStore.EventLabels)
        {
            var nx = new NumericUpDown { Minimum = 0, Maximum = 384, Width = 52 };
            var ny = new NumericUpDown { Minimum = 0, Maximum = 216, Width = 52 };
            var color = new ColorButton();
            _x[label] = nx;
            _y[label] = ny;
            _timerColor[label] = color;

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(new Label
            {
                Text = Loc.EventName(label),
                AutoSize = false,
                Width = 96,
                Height = 22,
                Margin = new Padding(3, 7, 3, 0)
            });
            row.Controls.Add(Lbl("X"));
            row.Controls.Add(nx);
            row.Controls.Add(Lbl("Y"));
            row.Controls.Add(ny);
            row.Controls.Add(color);
            left.Controls.Add(row);

            string captured = label;
            nx.ValueChanged += (_, _) => OnPositionEdited(captured);
            ny.ValueChanged += (_, _) => OnPositionEdited(captured);
            color.ColorChanged += (_, _) =>
            {
                if (_loading) return;
                _layout.TimerColors[captured] = OverlayLayout.ToHex(color.Value);
                _preview.Invalidate();
                _changed();
            };
        }

        // ── Contour et fond
        left.Controls.Add(Title(Loc.T("ap.frame.title")));
        var outlineRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        outlineRow.Controls.Add(new Label { Text = Loc.T("ap.outline"), AutoSize = false, Width = 110, Height = 22, Margin = new Padding(3, 7, 3, 0) });
        outlineRow.Controls.Add(_outline);
        left.Controls.Add(outlineRow);
        var fillRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        fillRow.Controls.Add(new Label { Text = Loc.T("ap.fill"), AutoSize = false, Width = 110, Height = 22, Margin = new Padding(3, 7, 3, 0) });
        fillRow.Controls.Add(_fill);
        fillRow.Controls.Add(Lbl(Loc.T("ap.opacity")));
        fillRow.Controls.Add(_fillOpacity);
        left.Controls.Add(fillRow);

        left.Controls.Add(Hint(Loc.T("ap.hint")));
        left.Controls.Add(_light);

        var reset = new Button { Text = Loc.T("ap.reset"), AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        reset.Click += (_, _) =>
        {
            SetLayout(OverlayLayout.CreateDefault());
            _changed();
        };
        left.Controls.Add(reset);
        left.Controls.Add(new Panel { Width = 1, Height = 16, Margin = Padding.Empty }); // marge basse du défilement

        Controls.Add(left, 0, 0);
        Controls.Add(_preview, 1, 0);

        // ── Évènements
        _skin.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _skin.SelectedItem is not SkinItem item) return;
            SwitchSkin(item.Id);
            _changed();
        };
        _font.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _font.SelectedItem is not string name) return;
            _layout.FontFamily = name;
            _preview.Invalidate();
            _changed();
        };
        _size.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.FontSize = (float)_size.Value;
            _preview.Invalidate();
            _changed();
        };
        _spacing.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.LetterSpacing = (float)_spacing.Value;
            _preview.Invalidate();
            _changed();
        };
        _bold.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.Bold = _bold.Checked;
            _preview.Invalidate();
            _changed();
        };
        _outline.ColorChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.OutlineColor = OverlayLayout.ToHex(_outline.Value);
            _preview.Invalidate();
            _changed();
        };
        _fill.ColorChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.InteriorColor = OverlayLayout.ToHex(_fill.Value);
            _preview.Invalidate();
            _changed();
        };
        _fillOpacity.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.InteriorOpacity = (float)_fillOpacity.Value / 100f;
            _preview.Invalidate();
            _changed();
        };
        _light.CheckedChanged += (_, _) => _preview.Light = _light.Checked;
        _overlayOpacity.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.Opacity = (int)_overlayOpacity.Value;
            _preview.Invalidate();
            _changed();
        };
        _scale.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _layout.Scale = (int)_scale.Value;
            UpdateScaleInfo();
            _changed();
        };
        _preview.PlacementDragged += (label, x, y) =>
        {
            // l'aperçu a déjà mis à jour _layout ; on synchronise les champs sans re-déclencher OnPositionEdited
            _loading = true;
            _x[label].Value = Math.Clamp(x, 0, (int)_x[label].Maximum);
            _y[label].Value = Math.Clamp(y, 0, (int)_y[label].Maximum);
            _loading = false;
            _changed();
        };

        FillFonts(_layout.FontFamily);
        SetLayout(layout);
    }

    public OverlayLayout ToLayout() => _layout.Clone();

    public void SetLayout(OverlayLayout layout)
    {
        _loading = true;
        _layout = layout.Clone();
        _layout.Normalize();
        _preview.AppearanceLayout = _layout;

        _renderer.SetBackground(_layout.BackgroundImage);
        UpdateCleanedNote();
        UpdatePositionRanges();
        FillSkins(_layout.BackgroundImage);

        SelectFont(_layout.FontFamily);

        _size.Value = (decimal)Math.Clamp(_layout.FontSize, 8f, 48f);
        _spacing.Value = (decimal)Math.Clamp(_layout.LetterSpacing, -3f, 12f);
        _bold.Checked = _layout.Bold;
        _scale.Value = Math.Clamp(_layout.Scale, 50, 300);
        _overlayOpacity.Value = Math.Clamp(_layout.Opacity, 20, 100);
        UpdateScaleInfo();

        foreach (var label in ConfigStore.EventLabels)
        {
            var p = _layout.GetPlacement(label);
            _x[label].Value = Math.Clamp(p.X, 0, (int)_x[label].Maximum);
            _y[label].Value = Math.Clamp(p.Y, 0, (int)_y[label].Maximum);
            _timerColor[label].Value = _layout.GetTimerColor(label);
        }
        _outline.Value = _layout.GetOutlineColor();
        _fill.Value = _layout.GetInteriorColor();
        _fillOpacity.Value = (decimal)Math.Clamp(_layout.InteriorOpacity * 100f, 0f, 100f);

        _loading = false;
        _preview.RefreshFrame();
    }

    // ───────────── Polices ─────────────

    /// <summary>Polices personnelles (dossier fonts, embarquées, installées « pour l'utilisateur ») d'abord, puis celles de Windows.</summary>
    private void FillFonts(string selected)
    {
        bool was = _loading;
        _loading = true;
        try
        {
            _font.Items.Clear();
            foreach (var name in FontCatalog.PrivateFamilyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                _font.Items.Add(name);
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                foreach (var f in fonts.Families)
                    if (!_font.Items.Contains(f.Name)) _font.Items.Add(f.Name);
            SelectFont(selected);
        }
        finally
        {
            _loading = was;
        }
        UpdateFontInfo();
    }

    private void SelectFont(string name)
    {
        int idx = _font.Items.IndexOf(name);
        if (idx < 0) idx = _font.Items.Add(name); // police absente de cette machine : on la garde telle quelle
        _font.SelectedIndex = idx;
    }

    /// <summary>Dit clairement ce qui a été chargé, et ce qui a échoué (avec la raison).</summary>
    private void UpdateFontInfo()
    {
        var results = FontCatalog.LoadResults;
        var lines = new List<string>();
        foreach (var group in results.GroupBy(r => r.Source))
            lines.Add(Loc.T("ap.font.group", Loc.T("font.src." + group.Key), group.Count(r => r.Ok)));
        foreach (var failed in results.Where(r => !r.Ok))
            lines.Add(Loc.T("ap.font.failed", Path.GetFileName(failed.File), failed.Error ?? ""));

        _fontInfo.Text = lines.Count == 0
            ? Loc.T("ap.font.none")
            : Loc.T("ap.font.info", string.Join("\n", lines));
    }

    private static void OpenFontsFolder()
    {
        try
        {
            Directory.CreateDirectory(FontCatalog.UserFontsDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(FontCatalog.UserFontsDir) { UseShellExecute = true });
        }
        catch { }
    }

    // ───────────── Image ─────────────

    private void FillSkins(string selectedId)
    {
        bool was = _loading;
        _loading = true;
        try
        {
            _skin.Items.Clear();
            foreach (var b in Skins.BuiltIn) _skin.Items.Add(new SkinItem(b.Id, Skins.LabelFor(b)));
            foreach (var f in ConfigStore.ListSkins()) _skin.Items.Add(new SkinItem(f, f));

            var match = _skin.Items.Cast<SkinItem>()
                .FirstOrDefault(i => string.Equals(i.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                match = new SkinItem(selectedId, Loc.T("common.notFound", selectedId));
                _skin.Items.Add(match);
            }
            _skin.SelectedItem = match;
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>
    /// Change d'image. Taille de police et positions sont propres à chaque image : on mémorise celles
    /// de l'image quittée et on charge celles de la nouvelle (déjà réglées, sinon valeurs adaptées).
    /// </summary>
    private void SwitchSkin(string newId, bool force = false)
    {
        string oldId = _layout.BackgroundImage ?? "";
        bool different = !string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase);
        if (different) _layout.SkinProfiles[oldId] = CaptureProfile();

        _layout.BackgroundImage = newId;
        ApplyBackground(force);
        if (!different) return;

        var profile = _layout.SkinProfiles.TryGetValue(newId, out var saved) && saved is not null
            ? saved.Clone()
            : Skins.DefaultProfileFor(newId, _renderer.FrameSize, _layout.FontSize);
        ApplyProfile(profile);
    }

    private SkinProfile CaptureProfile()
    {
        var profile = new SkinProfile { FontSize = _layout.FontSize };
        foreach (var label in ConfigStore.EventLabels)
        {
            var p = _layout.GetPlacement(label);
            profile.Timers[label] = new TimerPlacement(p.X, p.Y);
        }
        return profile;
    }

    private void ApplyProfile(SkinProfile profile)
    {
        var fallback = OverlayLayout.CreateDefault();
        _loading = true;
        try
        {
            _layout.FontSize = Math.Clamp(profile.FontSize, 8f, 48f);
            _layout.Timers = new Dictionary<string, TimerPlacement>();
            foreach (var label in ConfigStore.EventLabels)
            {
                var p = profile.Timers.TryGetValue(label, out var t) && t is not null ? t : fallback.GetPlacement(label);
                _layout.Timers[label] = new TimerPlacement(p.X, p.Y);
                _x[label].Value = Math.Clamp(p.X, 0, (int)_x[label].Maximum);
                _y[label].Value = Math.Clamp(p.Y, 0, (int)_y[label].Maximum);
            }
            _size.Value = (decimal)_layout.FontSize;
        }
        finally
        {
            _loading = false;
        }
        _preview.Invalidate();
    }

    /// <summary>Quand une image importée a été corrigée automatiquement (voile autour du cadre, fond intégré), on le dit.</summary>
    private void UpdateCleanedNote()
    {
        bool cleaned = _renderer.LastFrameCleaned;
        _skinCleaned.Text = cleaned ? "\u2713 " + Loc.T("ap.skin.cleaned") : "";
        _skinCleaned.Visible = cleaned;
    }

    /// <summary>Recharge l'image dans l'aperçu et adapte les limites des positions à sa taille.</summary>
    private void ApplyBackground(bool force = false)
    {
        _renderer.SetBackground(_layout.BackgroundImage, force);
        UpdateCleanedNote();
        UpdatePositionRanges();
        UpdateScaleInfo();
        _preview.RefreshFrame();
    }

    /// <summary>Taille réelle de l'overlay à l'écran : l'image (qui dépend du cadre choisi) × l'échelle.</summary>
    private void UpdateScaleInfo()
    {
        var frame = _renderer.FrameSize;
        float k = _layout.Scale / 100f;
        _scaleInfo.Text = Loc.T("ap.scale.size", (int)Math.Round(frame.Width * k), (int)Math.Round(frame.Height * k));
    }

    private void UpdatePositionRanges()
    {
        var size = _renderer.FrameSize;
        foreach (var label in ConfigStore.EventLabels)
        {
            _x[label].Maximum = size.Width;
            _y[label].Maximum = size.Height;
        }
    }

    private void ImportSkin()
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("ap.skin.importTitle"),
            Filter = Loc.T("ap.skin.filter")
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            Directory.CreateDirectory(ConfigStore.SkinsDir);
            string name = Path.GetFileName(dlg.FileName);
            string dest = Path.Combine(ConfigStore.SkinsDir, name);
            bool sameFile = string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(dest),
                StringComparison.OrdinalIgnoreCase);

            if (!sameFile)
            {
                if (File.Exists(dest))
                {
                    var answer = MessageBox.Show(FindForm(), Loc.T("dlg.exists.text", name),
                        Loc.T("ap.skin.exists"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) return;
                }
                File.Copy(dlg.FileName, dest, overwrite: true);
            }

            SwitchSkin(name, force: true); // même nom = fichier peut-être remplacé : on recharge vraiment
            FillSkins(name);
            _changed();
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), Loc.T("dlg.importError", ex.Message),
                Loc.T("dlg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenSkinsFolder()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.SkinsDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(ConfigStore.SkinsDir) { UseShellExecute = true });
        }
        catch { }
    }

    // ───────────── Divers ─────────────

    private void OnPositionEdited(string label)
    {
        if (_loading) return;
        var p = _layout.GetPlacement(label);
        p.X = (int)_x[label].Value;
        p.Y = (int)_y[label].Value;
        _preview.Invalidate();
        _changed();
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Margin = new Padding(3, 12, 3, 4)
    };

    private static Label Lbl(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 7, 3, 0)
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(320, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 6, 3, 4)
    };
}

internal sealed class SkinItem
{
    public string Id { get; }
    public string Label { get; }

    public SkinItem(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public override string ToString() => Label;
}

/// <summary>Bouton-pastille qui ouvre le sélecteur de couleur de Windows.</summary>
internal sealed class ColorButton : Button
{
    private Color _value = Color.White;

    public event EventHandler? ColorChanged;

    public ColorButton()
    {
        Size = new Size(40, 24);
        Margin = new Padding(6, 3, 3, 3);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = Color.Gray;
        UseVisualStyleBackColor = false;
        BackColor = _value;
    }

    public Color Value
    {
        get => _value;
        set
        {
            _value = value;
            BackColor = value;
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        using var dlg = new ColorDialog { Color = _value, FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        Value = dlg.Color;
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Aperçu de l'overlay (même rendu que le vrai), timers déplaçables à la souris.</summary>
internal sealed class PreviewBox : Control
{
    private const float MaxScale = 1.5f;
    private const float MaxWidth = 385f;
    private const float MaxHeight = 300f;

    // Le World Boss affiche le nom du boss (donné par l'API) ; les autres, le nom de l'event.
    private static string SampleText(string label) => label switch
    {
        "World Boss" => "Avarice: 02:08:29",
        "Helltide" => $"{Loc.EventName(label)}: 00:17:52",
        "Legion" => $"{Loc.EventName(label)}: 00:08:30",
        _ => label
    };

    private readonly OverlayRenderer _renderer;
    private float _scale = MaxScale;
    private string? _dragLabel;
    private string? _hoverLabel;
    private PointF _grab;
    private bool _light;

    public OverlayLayout AppearanceLayout { get; set; } = OverlayLayout.CreateDefault();

    public bool Light
    {
        get => _light;
        set { _light = value; Invalidate(); }
    }

    /// <summary>(event, x, y) : appelé pendant le glisser ; AppearanceLayout est déjà à jour.</summary>
    public event Action<string, int, int>? PlacementDragged;

    public PreviewBox(OverlayRenderer renderer)
    {
        _renderer = renderer;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0, 8, 0, 8);
        RefreshFrame();
    }

    /// <summary>À rappeler quand l'image change : l'aperçu s'adapte à sa taille.</summary>
    public void RefreshFrame()
    {
        var s = _renderer.FrameSize;
        _scale = Math.Min(MaxScale, Math.Min(MaxWidth / s.Width, MaxHeight / s.Height));
        Size = new Size((int)(s.Width * _scale), (int)(s.Height * _scale));
        Invalidate();
    }

    private List<TimerLine> BuildLines()
    {
        var lines = new List<TimerLine>();
        foreach (var label in ConfigStore.EventLabels)
        {
            var p = AppearanceLayout.GetPlacement(label);
            lines.Add(new TimerLine(SampleText(label), AppearanceLayout.GetTimerColor(label), p.X, p.Y));
        }
        return lines;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // Faux décor de jeu : sombre ou clair, pour juger la lisibilité des deux cas.
        g.Clear(_light ? Color.FromArgb(235, 235, 225) : Color.FromArgb(30, 34, 30));
        if (AppearanceLayout.Opacity >= 100)
        {
            _renderer.Draw(g, AppearanceLayout, BuildLines(), _scale);
        }
        else
        {
            // Même effet que sur le vrai overlay : tout est dessiné d'abord, puis l'ensemble est rendu semi-transparent.
            using var layer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (var layerGraphics = Graphics.FromImage(layer))
            {
                layerGraphics.Clear(Color.Transparent);
                _renderer.Draw(layerGraphics, AppearanceLayout, BuildLines(), _scale);
            }
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = AppearanceLayout.Opacity / 100f },
                ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(layer, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel, attrs);
        }

        string? highlighted = _dragLabel ?? _hoverLabel;
        if (highlighted is not null)
        {
            var p = AppearanceLayout.GetPlacement(highlighted);
            var box = OverlayRenderer.HitBox(AppearanceLayout, p.X, p.Y);
            using var pen = new Pen(Color.Cyan, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawRectangle(pen, box.X * _scale, box.Y * _scale, box.Width * _scale, box.Height * _scale);
        }
    }

    private string? HitTest(Point location)
    {
        float nx = location.X / _scale, ny = location.Y / _scale;
        var labels = ConfigStore.EventLabels;
        for (int i = labels.Length - 1; i >= 0; i--) // le dernier dessiné est au-dessus
        {
            var p = AppearanceLayout.GetPlacement(labels[i]);
            if (OverlayRenderer.HitBox(AppearanceLayout, p.X, p.Y).Contains(nx, ny)) return labels[i];
        }
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var label = HitTest(e.Location);
        if (label is null) return;

        var p = AppearanceLayout.GetPlacement(label);
        _dragLabel = label;
        _grab = new PointF(e.X / _scale - p.X, e.Y / _scale - p.Y);
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragLabel is not null)
        {
            var size = _renderer.FrameSize;
            int x = Math.Clamp((int)Math.Round(e.X / _scale - _grab.X), 0, Math.Max(0, size.Width - 10));
            int y = Math.Clamp((int)Math.Round(e.Y / _scale - _grab.Y), 0, Math.Max(0, size.Height - 10));
            var p = AppearanceLayout.GetPlacement(_dragLabel);
            if (p.X != x || p.Y != y)
            {
                p.X = x;
                p.Y = y;
                Invalidate();
                PlacementDragged?.Invoke(_dragLabel, x, y);
            }
            return;
        }

        var hover = HitTest(e.Location);
        if (hover != _hoverLabel)
        {
            _hoverLabel = hover;
            Cursor = hover is null ? Cursors.Default : Cursors.SizeAll;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragLabel is null) return;
        _dragLabel = null;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverLabel is null) return;
        _hoverLabel = null;
        Cursor = Cursors.Default;
        Invalidate();
    }
}
