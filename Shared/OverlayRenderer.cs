using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace D4Overlay.Shared;

/// <summary>Une ligne de timer à dessiner : texte, couleur, position (pixels de l'image du cadre).</summary>
public readonly record struct TimerLine(string Text, Color Color, int X, int Y);

/// <summary>
/// Dessine l'overlay (fond semi-opaque, cadre, timers). Utilisé tel quel par l'overlay
/// et par l'aperçu du configurateur, pour que les deux soient identiques.
/// </summary>
public sealed class OverlayRenderer
{
    private const float HitBoxWidth = 240f;

    private Bitmap _frame;
    private Bitmap? _interior;
    private string _backgroundName = "";

    /// <summary>true si l'image personnalisée affichée a été corrigée automatiquement (voile extérieur, fond intégré).</summary>
    public bool LastFrameCleaned { get; private set; }
    private string? _familyName;
    private FontFamily? _family;
    private bool _familyShared;
    private string? _lastFontError;

    public OverlayRenderer()
    {
        _frame = LoadResource("overlay-bg.png") ?? CreateFallbackFrame();
        _interior = LoadResource("interior-fill.png");
    }

    public Size FrameSize => _frame.Size;

    public static float LineHeight(OverlayLayout layout) => layout.FontSize * 1.5f;

    /// <summary>Zone cliquable d'un timer (pour le glisser-déposer de l'aperçu).</summary>
    public static RectangleF HitBox(OverlayLayout layout, int x, int y)
        => new(x, y, HitBoxWidth, LineHeight(layout));

    // ───────────── Image de fond ─────────────

    /// <summary>
    /// Choisit l'image du cadre : "" = cadre lave intégré, sinon un fichier du dossier skins.
    /// Retourne true si l'image a été rechargée (la taille peut avoir changé).
    /// </summary>
    public bool SetBackground(string? name, bool force = false)
    {
        name ??= "";
        if (!force && string.Equals(name, _backgroundName, StringComparison.OrdinalIgnoreCase)) return false;

        Bitmap? frame = null;
        Bitmap? interior = null;
        bool cleaned = false;
        var builtIn = Skins.Find(name);
        if (builtIn is not null)
        {
            frame = LoadResource(builtIn.FrameResource);
            if (frame is not null && builtIn.InteriorResource is not null)
                interior = LoadResource(builtIn.InteriorResource);
        }
        else if (name.Length > 0)
        {
            var loaded = LoadFile(name);
            if (loaded is not null)
            {
                // Nettoyage automatique : voile semi-transparent autour du cadre, fond intérieur intégré, résidus. Une image déjà
                // propre n'est pas modifiée. En cas de problème, on retombe sur le comportement d'origine (image telle quelle).
                try
                {
                    var result = FrameCleaner.Clean(loaded);
                    frame = result.Frame;
                    interior = result.Interior;
                    cleaned = result.Changed;
                    if (result.Changed) AppLog.Write($"cadre « {name} » nettoyé : {result.Notes}");
                    loaded.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Write($"nettoyage du cadre « {name} » impossible, image utilisée telle quelle : {ex.Message}");
                    frame = loaded;
                    interior = BuildInteriorMask(loaded);
                }
            }
        }
        if (frame is null)
        {
            // Fichier introuvable / illisible : on retombe sur le cadre par défaut.
            var fallback = Skins.Find(Skins.DefaultId)!;
            name = fallback.Id;
            frame = LoadResource(fallback.FrameResource) ?? CreateFallbackFrame();
            interior = fallback.InteriorResource is null ? null : LoadResource(fallback.InteriorResource);
        }

        var oldFrame = _frame;
        var oldInterior = _interior;
        _frame = frame;
        _interior = interior;
        _backgroundName = name;
        LastFrameCleaned = cleaned;
        oldFrame.Dispose();
        oldInterior?.Dispose();
        return true;
    }

    private static Bitmap? LoadFile(string name)
    {
        try
        {
            string path = Path.IsPathRooted(name) ? name : Path.Combine(ConfigStore.SkinsDir, name);
            if (!File.Exists(path)) return null;
            using var raw = new Bitmap(path);
            return new Bitmap(raw); // copie : ne garde pas le fichier ouvert
        }
        catch { return null; }
    }

    /// <summary>
    /// Pour une image personnalisée : repère l'intérieur du cadre (la zone transparente connectée au
    /// centre) pour y poser le fond. Si l'image n'a pas de cadre fermé, pas de fond (null).
    /// </summary>
    private static Bitmap? BuildInteriorMask(Bitmap frame)
    {
        try
        {
            int w = frame.Width, h = frame.Height;
            var rect = new Rectangle(0, 0, w, h);

            var src = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = src.Stride;
            var px = new byte[Math.Abs(stride) * h];
            Marshal.Copy(src.Scan0, px, 0, px.Length);
            frame.UnlockBits(src);

            bool IsLow(int x, int y) => px[y * stride + x * 4 + 3] < 60;

            // Point de départ : le centre, ou le pixel transparent le plus proche (balayage en anneaux).
            int sx = -1, sy = -1, cx = w / 2, cy = h / 2;
            for (int r = 0; r <= Math.Max(w, h) / 2 && sx < 0; r++)
            {
                for (int dy = -r; dy <= r && sx < 0; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        if (IsLow(x, y)) { sx = x; sy = y; break; }
                    }
                }
            }
            if (sx < 0) return null;

            // Remplissage par propagation (sans récursion).
            var seen = new bool[w * h];
            var stack = new int[w * h];
            int sp = 0, count = 0;
            bool touchesEdge = false;
            stack[sp++] = sy * w + sx;
            seen[sy * w + sx] = true;
            while (sp > 0)
            {
                int idx = stack[--sp];
                int x = idx % w, y = idx / w;
                count++;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) touchesEdge = true;

                if (x > 0 && !seen[idx - 1] && IsLow(x - 1, y)) { seen[idx - 1] = true; stack[sp++] = idx - 1; }
                if (x < w - 1 && !seen[idx + 1] && IsLow(x + 1, y)) { seen[idx + 1] = true; stack[sp++] = idx + 1; }
                if (y > 0 && !seen[idx - w] && IsLow(x, y - 1)) { seen[idx - w] = true; stack[sp++] = idx - w; }
                if (y < h - 1 && !seen[idx + w] && IsLow(x, y + 1)) { seen[idx + w] = true; stack[sp++] = idx + w; }
            }
            // La zone touche le bord de l'image : le cadre n'est pas fermé, remplir déborderait.
            if (touchesEdge || count == 0) return null;

            // Léger débord sous le cadre (3 px) puis adoucissement des bords.
            var grown = Dilate(seen, w, h, 3);
            var alpha = BoxBlur(grown, w, h);

            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var dst = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var outPx = new byte[Math.Abs(dst.Stride) * h]; // RGB = 0 (noir), seul l'alpha varie
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    outPx[y * dst.Stride + x * 4 + 3] = alpha[y * w + x];
            Marshal.Copy(outPx, 0, dst.Scan0, outPx.Length);
            result.UnlockBits(dst);
            return result;
        }
        catch { return null; }
    }

    private static bool[] Dilate(bool[] src, int w, int h, int radius)
    {
        var tmp = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool on = false;
                for (int k = Math.Max(0, x - radius); k <= Math.Min(w - 1, x + radius) && !on; k++)
                    on = src[y * w + k];
                tmp[y * w + x] = on;
            }

        var dst = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool on = false;
                for (int k = Math.Max(0, y - radius); k <= Math.Min(h - 1, y + radius) && !on; k++)
                    on = tmp[k * w + x];
                dst[y * w + x] = on;
            }
        return dst;
    }

    private static byte[] BoxBlur(bool[] mask, int w, int h)
    {
        var res = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sum = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        n++;
                        if (mask[yy * w + xx]) sum++;
                    }
                res[y * w + x] = (byte)(255 * sum / Math.Max(1, n));
            }
        return res;
    }

    // ───────────── Dessin ─────────────

    /// <summary>Dessine sur g (fond non effacé). scale &gt; 1 pour un aperçu agrandi.</summary>
    public void Draw(Graphics g, OverlayLayout layout, IEnumerable<TimerLine> lines, float scale = 1f)
    {
        var state = g.Save();
        try
        {
            g.ScaleTransform(scale, scale);
            if (Math.Abs(scale - 1f) > 0.001f)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }
            int w = _frame.Width, h = _frame.Height;

            // 1. fond (couleur + opacité réglables) à l'intérieur du cadre, sous l'image qui recouvre ses bords
            if (_interior is not null && layout.InteriorOpacity > 0.001f)
            {
                var c = layout.GetInteriorColor();
                // Le masque est noir avec un alpha : on remplace sa couleur par celle choisie et on module l'alpha.
                var matrix = new ColorMatrix(new[]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, layout.InteriorOpacity, 0 },
                    new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 },
                });
                using var attrs = new ImageAttributes();
                attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(_interior, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, attrs);
            }

            // 2. le cadre
            g.DrawImage(_frame, 0, 0, w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 3. les timers
            var family = GetFamily(layout.FontFamily);
            var style = ResolveStyle(family, layout.Bold);
            var outlineColor = layout.GetOutlineColor();
            foreach (var line in lines)
            {
                float width = Math.Max(40f, w - line.X - 12f);
                var box = new RectangleF(line.X, line.Y, width, LineHeight(layout));
                try
                {
                    DrawOutlinedText(g, line.Text, family, style, layout.FontSize, layout.LetterSpacing,
                        line.Color, outlineColor, box);
                }
                catch (Exception ex)
                {
                    // Police que GDI+ refuse (style absent, fichier défectueux…) : on ne perd pas l'affichage.
                    if (_lastFontError != layout.FontFamily)
                    {
                        _lastFontError = layout.FontFamily;
                        AppLog.Write($"police '{layout.FontFamily}' inutilisable ({ex.Message}) : repli sur Segoe UI");
                    }
                    using var fallback = new FontFamily("Segoe UI");
                    DrawOutlinedText(g, line.Text, fallback, FontStyle.Bold, layout.FontSize, layout.LetterSpacing,
                        line.Color, outlineColor, box);
                }
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private FontFamily GetFamily(string name)
    {
        if (_family is not null && string.Equals(_familyName, name, StringComparison.OrdinalIgnoreCase))
            return _family;

        if (!_familyShared) _family?.Dispose();
        _familyShared = false;

        var embedded = FontCatalog.Find(name);
        if (embedded is not null)
        {
            _family = embedded;
            _familyShared = true; // partagée : on ne la libère pas
        }
        else
        {
            try { _family = new FontFamily(name); }
            catch
            {
                try { _family = new FontFamily("Segoe UI"); }
                catch { _family = new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif); }
            }
        }
        _familyName = name;
        return _family;
    }

    /// <summary>Certaines polices n'ont pas de variante grasse : on retombe sur un style disponible.</summary>
    private static FontStyle ResolveStyle(FontFamily family, bool bold)
    {
        var wanted = bold ? FontStyle.Bold : FontStyle.Regular;
        foreach (var s in new[] { wanted, FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic })
            if (family.IsStyleAvailable(s)) return s;
        return FontStyle.Regular;
    }

    /// <summary>Texte avec contour : lisible quel que soit le décor du jeu derrière.</summary>
    private static void DrawOutlinedText(Graphics g, string text, FontFamily family, FontStyle style,
        float emSizePx, float spacing, Color color, Color outlineColor, RectangleF rect)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using var path = new GraphicsPath();
        if (Math.Abs(spacing) < 0.01f)
            path.AddString(text, family, (int)style, emSizePx, rect, fmt);
        else
            AddSpacedString(g, path, text, family, style, emSizePx, spacing, rect, fmt);

        using var outline = new Pen(Color.FromArgb(230, outlineColor), 3f) { LineJoin = LineJoin.Round };
        g.DrawPath(outline, path);
        using var fill = new SolidBrush(color);
        g.FillPath(fill, path);
    }

    /// <summary>GDI+ n'a pas de crénage réglable : on place les lettres une à une, avec un écart en plus.</summary>
    private static void AddSpacedString(Graphics g, GraphicsPath path, string text, FontFamily family,
        FontStyle style, float emSizePx, float spacing, RectangleF rect, StringFormat fmt)
    {
        using var font = new Font(family, emSizePx, style, GraphicsUnit.Pixel);
        using var measureFmt = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap
        };
        for (int i = 0; i < text.Length; i++)
        {
            float prefix = i == 0 ? 0f : g.MeasureString(text.Substring(0, i), font, PointF.Empty, measureFmt).Width;
            var origin = new PointF(rect.X + prefix + i * spacing, rect.Y);
            path.AddString(text[i].ToString(), family, (int)style, emSizePx, origin, fmt);
        }
    }

    private static Bitmap? LoadResource(string name)
    {
        try
        {
            using var stream = typeof(OverlayRenderer).Assembly.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var raw = new Bitmap(stream);
            return new Bitmap(raw); // copie détachée du flux
        }
        catch { return null; }
    }

    /// <summary>Image introuvable : panneau sombre translucide, l'overlay reste utilisable.</summary>
    private static Bitmap CreateFallbackFrame()
    {
        var fallback = new Bitmap(384, 216, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(fallback);
        g.Clear(Color.FromArgb(190, 12, 12, 16));
        return fallback;
    }
}
