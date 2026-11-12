namespace D4OverlayConfig;

/// <summary>
/// Onglets maison : le TabControl de Windows ne se colore pas correctement en thème sombre.
/// Même usage : Add(titre, contenu), SelectedIndex, SelectContent(contenu).
/// </summary>
internal sealed class TabHost : Panel
{
    private readonly FlowLayoutPanel _header = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(10) };
    private readonly List<TabButton> _buttons = new();
    private readonly List<Control> _contents = new();
    private int _selected = -1;

    public TabHost()
    {
        // Le contenu (Fill) d'abord, l'en-tête (Top) ensuite : c'est l'ordre qui donne la bonne mise en page.
        Controls.Add(_body);
        Controls.Add(_header);
    }

    public int Count => _contents.Count;
    public IReadOnlyList<Control> Contents => _contents;

    public int SelectedIndex
    {
        get => _selected;
        set => SelectIndex(value);
    }

    public void Add(string title, Control content)
    {
        int index = _contents.Count;
        content.Dock = DockStyle.Fill;
        content.Visible = false;
        _body.Controls.Add(content);
        _contents.Add(content);

        var button = new TabButton(title);
        button.Click += (_, _) => SelectIndex(index);
        _buttons.Add(button);
        _header.Controls.Add(button);

        if (_selected < 0) SelectIndex(0);
    }

    public void SelectContent(Control content)
    {
        int index = _contents.IndexOf(content);
        if (index >= 0) SelectIndex(index);
    }

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= _contents.Count) return;
        _selected = index;
        for (int i = 0; i < _contents.Count; i++)
        {
            _contents[i].Visible = i == index;
            _buttons[i].Selected = i == index;
        }
        ApplyButtonColors();
    }

    public void ApplyTheme()
    {
        var p = AppTheme.P;
        BackColor = p.Window;
        _body.BackColor = p.Window;
        _header.BackColor = p.Surface;
        ApplyButtonColors();
    }

    private void ApplyButtonColors()
    {
        foreach (var button in _buttons) button.ApplyColors();
    }
}

internal sealed class TabButton : Button
{
    public bool Selected { get; set; }

    public TabButton(string title)
    {
        Text = title;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12, 6, 12, 8);
        Margin = Padding.Empty;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    public void ApplyColors()
    {
        var p = AppTheme.P;
        BackColor = Selected ? p.Window : p.Surface;
        ForeColor = Selected ? p.Text : p.Dim;
        FlatAppearance.MouseOverBackColor = Selected ? p.Window : p.Hover;
        FlatAppearance.MouseDownBackColor = p.Window;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!Selected) return;
        using var brush = new SolidBrush(AppTheme.P.Accent);
        e.Graphics.FillRectangle(brush, 0, Height - 3, Width, 3); // soulignement de l'onglet actif
    }
}
