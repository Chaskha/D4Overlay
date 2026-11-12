using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace D4OverlayConfig;

internal sealed record Palette(Color Window, Color Surface, Color Input, Color Text, Color Dim,
    Color Border, Color Accent, Color Hover);

/// <summary>
/// Thème de la fenêtre de configuration : "system" (suit Windows, y compris en direct), "light" ou "dark".
/// WinForms (.NET 8) n'a pas de mode sombre intégré : on colore les contrôles nous-mêmes. Les boîtes de dialogue
/// de Windows (sélecteur de couleur, d'ouverture de fichier, messages) restent, elles, dans le thème clair.
/// </summary>
internal static class AppTheme
{
    private static readonly Palette Light = new(
        Window: Color.FromArgb(243, 243, 243), Surface: Color.FromArgb(230, 230, 230), Input: Color.White,
        Text: Color.FromArgb(28, 28, 28), Dim: Color.FromArgb(105, 105, 105), Border: Color.FromArgb(190, 190, 190),
        Accent: Color.FromArgb(0, 103, 192), Hover: Color.FromArgb(215, 215, 215));

    private static readonly Palette Dark = new(
        Window: Color.FromArgb(32, 32, 32), Surface: Color.FromArgb(45, 45, 45), Input: Color.FromArgb(56, 56, 56),
        Text: Color.FromArgb(240, 240, 240), Dim: Color.FromArgb(165, 165, 165), Border: Color.FromArgb(90, 90, 90),
        Accent: Color.FromArgb(76, 160, 255), Hover: Color.FromArgb(66, 66, 66));

    public static string Mode { get; private set; } = "system";
    public static bool IsDark { get; private set; }
    public static Palette P { get; private set; } = Light;

    public static void SetMode(string? mode)
    {
        Mode = mode is "light" or "dark" ? mode : "system";
        IsDark = Mode == "dark" || (Mode == "system" && SystemUsesDark());
        P = IsDark ? Dark : Light;
    }

    /// <summary>Réglage « Mode des applications » de Windows (Paramètres → Personnalisation → Couleurs).</summary>
    public static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }

    /// <summary>Colore un arbre de contrôles tout juste construit (on reconstruit l'interface à chaque changement de thème).</summary>
    public static void Apply(Control control)
    {
        if (control is TabHost host)
        {
            host.ApplyTheme();
            foreach (var content in host.Contents) Apply(content);
            return;
        }

        Style(control);
        foreach (Control child in control.Controls) Apply(child);
    }

    private static void Style(Control c)
    {
        switch (c)
        {
            case PreviewBox:
            case ColorButton:
                return; // ils gèrent leurs propres couleurs
            case Button button:
                if (IsDark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = P.Surface;
                    button.ForeColor = P.Text;
                    button.FlatAppearance.BorderColor = P.Border;
                    button.FlatAppearance.MouseOverBackColor = P.Hover;
                    button.FlatAppearance.MouseDownBackColor = P.Border;
                }
                break;
            case TextBox box:
                box.BackColor = P.Input;
                box.ForeColor = P.Text;
                if (IsDark) box.BorderStyle = BorderStyle.FixedSingle;
                break;
            case CheckBox check:
                check.ForeColor = P.Text;
                if (IsDark) check.FlatStyle = FlatStyle.Flat;
                break;
            case ComboBox combo:
                combo.BackColor = P.Input;
                combo.ForeColor = P.Text;
                if (IsDark) combo.FlatStyle = FlatStyle.Flat;
                NativeDark(combo, "DarkMode_CFD");
                break;
            case NumericUpDown number:
                number.BackColor = P.Input;
                number.ForeColor = P.Text;
                NativeDark(number, "DarkMode_CFD");
                break;
            case TrackBar track:
                track.BackColor = P.Window;
                break;
            case LinkLabel link:
                link.ForeColor = P.Text;
                link.LinkColor = P.Accent;
                link.ActiveLinkColor = P.Accent;
                link.VisitedLinkColor = P.Accent;
                break;
            case Label label:
                // Les textes secondaires sont créés en gris (DimGray) : on les remplace par le gris du thème.
                // Rouge (avertissements) : conservé, mais éclairci en thème sombre pour rester lisible.
                label.ForeColor = label.ForeColor == Color.Firebrick ? (IsDark ? Color.FromArgb(255, 120, 110) : Color.Firebrick)
                                : label.ForeColor == Color.DimGray ? P.Dim : P.Text;
                break;
            case Form form:
                form.BackColor = P.Window;
                form.ForeColor = P.Text;
                break;
            case ScrollableControl scrollable:
                scrollable.BackColor = P.Window;
                scrollable.ForeColor = P.Text;
                NativeDark(scrollable, "DarkMode_Explorer"); // barres de défilement sombres
                break;
            default:
                c.ForeColor = P.Text;
                break;
        }
    }

    // ───────────── Windows : barre de titre et thèmes natifs ─────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    /// <summary>Barre de titre claire ou sombre (Windows 10 1809 et plus récent).</summary>
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int)); // anciennes versions de Windows 10
        }
        catch { }
    }

    private static void NativeDark(Control control, string theme)
    {
        if (!IsDark) return;

        void Set()
        {
            try { SetWindowTheme(control.Handle, theme, null); } catch { }
        }

        if (control.IsHandleCreated) Set();
        else control.HandleCreated += (_, _) => Set();
    }
}
