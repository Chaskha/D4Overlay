using System.Runtime.InteropServices;

namespace D4OverlayConfig;

/// <summary>
/// Molette : elle agit sur ce qui est SOUS LA SOURIS, pas sur ce qui a le focus.
/// Par défaut, Windows envoie la molette au contrôle qui a le focus : après un clic dans une liste ou un champ numérique, faire défiler
/// la fenêtre avec la molette changeait la valeur de ce champ au lieu de faire défiler. Règle appliquée ici :
///  - molette sur un champ (liste, champ numérique, curseur) qui a le focus (clic explicite) : il l'utilise normalement ;
///  - ailleurs : on fait défiler le panneau sous la souris, et le champ qui a le focus n'est pas touché.
/// </summary>
internal sealed class WheelRouter : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int StepPixels = 60;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;

        IntPtr hwnd = WindowFromPoint(Cursor.Position);
        var control = hwnd == IntPtr.Zero ? null : Control.FromHandle(hwnd);
        // Fenêtre native (liste déroulante ouverte…) ou boîte de dialogue : comportement normal de Windows.
        if (control is null) return false;
        var form = control.FindForm();
        if (form is null || Form.ActiveForm != form) return false;

        // Sur un champ qui a le focus (clic explicite) : on le laisse utiliser la molette.
        var adjustable = FindAdjustable(control);
        if (adjustable is not null && adjustable.ContainsFocus) return false;

        // Sinon : défilement du panneau sous la souris (s'il y en a un). Dans tous les cas la molette est consommée,
        // pour qu'elle n'atteigne jamais un champ qui a le focus ailleurs dans la fenêtre.
        var scroller = FindScroller(control);
        if (scroller is not null)
        {
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            var pos = scroller.AutoScrollPosition; // coordonnées négatives : on repasse en positif pour la modifier
            scroller.AutoScrollPosition = new Point(Math.Abs(pos.X), Math.Abs(pos.Y) - Math.Sign(delta) * StepPixels);
        }
        return true;
    }

    private static Control? FindAdjustable(Control? c)
    {
        for (; c is not null; c = c.Parent)
            if (c is ComboBox or NumericUpDown or TrackBar) return c;
        return null;
    }

    private static ScrollableControl? FindScroller(Control? c)
    {
        for (; c is not null; c = c.Parent)
            if (c is ScrollableControl { AutoScroll: true, VerticalScroll.Visible: true } scroller && scroller is not Form)
                return scroller;
        return null;
    }
}
