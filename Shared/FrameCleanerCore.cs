namespace D4Overlay.Shared;

/// <summary>Résultat brut du nettoyage d'un cadre (aucune dépendance à System.Drawing : testable seul).</summary>
internal sealed class FrameCleanData
{
    /// <summary>Image nettoyée, BGRA non prémultiplié, w * h * 4 octets.</summary>
    public byte[] Frame = Array.Empty<byte>();

    /// <summary>Masque du fond intérieur (un octet d'alpha par pixel), ou null si aucun intérieur fermé n'a été trouvé.</summary>
    public byte[]? InteriorAlpha;

    public bool VeilRemoved;
    public float VeilOpacity;
    public bool InteriorFlattened;
    public float InteriorOpacity;
    public int SpecksRemoved;

    /// <summary>true si l'image d'origine a été modifiée.</summary>
    public bool Changed => VeilRemoved || InteriorFlattened;
}

/// <summary>
/// Nettoie un cadre d'overlay retouché à la main ou généré par IA. Trois corrections, appliquées seulement si elles sont nécessaires :
///  1. VOILE EXTÉRIEUR : le fond autour du cadre n'est pas totalement transparent (par exemple un noir à 20-40 %). On le repère
///     (zone sombre et peu opaque connectée au bord de l'image), on le supprime, et on « démélange » les pixels de bord
///     (cadre posé sur ce voile) pour garder des contours lisses, sans halo.
///  2. FOND INTÉRIEUR INTÉGRÉ : un aplat sombre semi-opaque à l'intérieur est rendu transparent : l'application le remplace
///     par un fond dont la couleur et l'opacité se règlent. Un intérieur plein (opaque) ou irrégulier est laissé tel quel.
///  3. RÉSIDUS : de minuscules taches isolées à l'intérieur (poussières de retouche).
/// Une image déjà propre (extérieur transparent, intérieur transparent) ressort inchangée, avec son masque d'intérieur.
/// </summary>
internal static class FrameCleanerCore
{
    // Seuils (voir les commentaires des étapes).
    private const float DarkLimit = 70f;          // un pixel « sombre » : tous ses canaux sous cette valeur
    private const float VeilMaxAlpha = 0.78f;     // le voile est plus transparent que ça
    private const float VeilMargin = 35f;         // tolérance (sur 255) autour de l'opacité du voile
    private const float VeilMinOpacity = 0.02f;   // en dessous : l'extérieur est déjà transparent, rien à corriger
    private const int BakedMinAlpha8 = 60;        // en dessous (sur 255) : intérieur déjà transparent, on n'y touche pas
    private const float OpaqueAlpha = 0.97f;      // au dessus : panneau plein, on n'y touche pas
    private const int BakedTolerance8 = 4;        // écart d'alpha (sur 255) toléré dans un aplat
    private const int MaxSpeckPixels = 40;

    public static FrameCleanData Run(byte[] bgra, int w, int h)
    {
        int n = w * h;
        var r = new float[n];
        var g = new float[n];
        var b = new float[n];
        var a = new float[n];
        var a8 = new byte[n]; // alpha en octets : les comparaisons de seuils se font en entiers (résultat identique quelle que soit la précision des flottants)
        for (int i = 0; i < n; i++)
        {
            b[i] = bgra[i * 4];
            g[i] = bgra[i * 4 + 1];
            r[i] = bgra[i * 4 + 2];
            a8[i] = bgra[i * 4 + 3];
            a[i] = a8[i] / 255f;
        }
        var nr = (float[])r.Clone();
        var ng = (float[])g.Clone();
        var nb = (float[])b.Clone();
        var na = (float[])a.Clone();
        var data = new FrameCleanData();

        // ── 1. Voile extérieur ─────────────────────────────────────────────────────────────────────────────
        var dark = new bool[n];
        for (int i = 0; i < n; i++) dark[i] = Math.Max(r[i], Math.Max(g[i], b[i])) < DarkLimit;

        int ringCount = 0;
        var candidates = new List<float>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (x >= 3 && y >= 3 && x < w - 3 && y < h - 3) continue;
                ringCount++;
                int i = y * w + x;
                if (dark[i] && a8[i] < VeilMaxAlpha * 255f) candidates.Add(a8[i]);
            }
        }

        if (candidates.Count > 0.2 * ringCount)
        {
            float threshold = Median(candidates) + VeilMargin; // candidates est en octets d'alpha (0..255)
            var ok = new bool[n];
            for (int i = 0; i < n; i++) ok[i] = a8[i] < threshold && dark[i];

            var outside = Flood(ok, w, h, BorderSeeds(w, h));
            int outCount = Count(outside);
            if (outCount > 0 && outCount < 0.9 * n)
            {
                var values = new List<float>(outCount);
                double sumR = 0, sumG = 0, sumB = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!outside[i]) continue;
                    values.Add(a[i]);
                    sumR += r[i];
                    sumG += g[i];
                    sumB += b[i];
                }
                float av = Median(values);
                // Extérieur déjà transparent (voile quasi nul) : rien à corriger, l'image reste telle quelle.
                if (av > VeilMinOpacity)
                {
                    float cvR = (float)(sumR / outCount), cvG = (float)(sumG / outCount), cvB = (float)(sumB / outCount);

                    // Pixels de bord : le cadre vu à travers le voile. On retrouve l'opacité réelle de l'objet (ao), puis sa couleur.
                    var grown = Dilate(outside, w, h, 2);
                    for (int i = 0; i < n; i++)
                    {
                        if (!grown[i] || outside[i] || a[i] >= 0.98f) continue;
                        float ao = Math.Clamp((a[i] - av) / (1f - av), 0f, 1f);
                        float safe = Math.Max(ao, 1e-3f);
                        float veilShare = (1f - ao) * av;
                        nr[i] = Math.Clamp((r[i] * a[i] - cvR * veilShare) / safe, 0f, 255f);
                        ng[i] = Math.Clamp((g[i] * a[i] - cvG * veilShare) / safe, 0f, 255f);
                        nb[i] = Math.Clamp((b[i] * a[i] - cvB * veilShare) / safe, 0f, 255f);
                        na[i] = ao;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        if (outside[i]) na[i] = 0f;
                        if (na[i] < 0.04f) na[i] = 0f; // miettes quasi invisibles
                    }
                    data.VeilRemoved = true;
                    data.VeilOpacity = av;
                }
            }
        }

        // ── 2. Intérieur ───────────────────────────────────────────────────────────────────────────────────
        int centerX = w / 2, centerY = h / 2;
        int centerAlpha8 = a8[centerY * w + centerX];
        float centerAlpha = centerAlpha8 / 255f;
        bool[]? inner = null;

        if (centerAlpha8 >= BakedMinAlpha8)
        {
            // Fond sombre intégré à l'image (alpha constant) : on le rend transparent. Garde-fous : pas pour un panneau plein,
            // et la zone trouvée doit être fermée (ne pas toucher le bord) et assez grande, sinon on laisse l'image telle quelle.
            if (centerAlpha < OpaqueAlpha)
            {
                var ok = new bool[n];
                for (int i = 0; i < n; i++) ok[i] = Math.Abs(a8[i] - centerAlpha8) <= BakedTolerance8;
                var region = Flood(ok, w, h, new[] { centerY * w + centerX });
                if (Count(region) >= 0.05 * n && !TouchesBorder(region, w, h))
                {
                    inner = region;
                    for (int i = 0; i < n; i++)
                        if (inner[i]) na[i] = 0f;
                    data.InteriorFlattened = true;
                    data.InteriorOpacity = centerAlpha;
                }
            }
        }
        else
        {
            // Intérieur déjà transparent : zone peu opaque connectée au centre (ou au pixel transparent le plus proche).
            var low = new bool[n];
            for (int i = 0; i < n; i++) low[i] = MathF.Round(na[i] * 255f) < 60f;
            int start = NearestSeed(low, w, h, centerX, centerY);
            if (start >= 0)
            {
                var region = Flood(low, w, h, new[] { start });
                // Si la zone touche le bord de l'image, le cadre n'est pas fermé : remplir déborderait.
                if (!TouchesBorder(region, w, h) && Count(region) > 0) inner = region;
            }
        }

        // ── 3. Résidus (seulement quand on a nous-mêmes vidé l'intérieur) ──────────────────────────────────
        if (inner is not null && data.InteriorFlattened)
        {
            int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (inner[y * w + x])
                    {
                        if (x < bx0) bx0 = x;
                        if (x > bx1) bx1 = x;
                        if (y < by0) by0 = y;
                        if (y > by1) by1 = y;
                    }

            var seen = new bool[n];
            var stack = new int[n];
            var points = new List<int>();
            for (int start = 0; start < n; start++)
            {
                if (seen[start] || na[start] <= 0.03f) continue;
                points.Clear();
                int sp = 0;
                stack[sp++] = start;
                seen[start] = true;
                int minX = w, maxX = -1, minY = h, maxY = -1;
                while (sp > 0)
                {
                    int idx = stack[--sp];
                    points.Add(idx);
                    int x = idx % w, y = idx / w;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int ni = ny * w + nx;
                            if (seen[ni] || na[ni] <= 0.03f) continue;
                            seen[ni] = true;
                            stack[sp++] = ni;
                        }
                    }
                }
                if (points.Count < MaxSpeckPixels && minX > bx0 && maxX < bx1 && minY > by0 && maxY < by1)
                {
                    foreach (int p in points)
                    {
                        na[p] = 0f;
                        inner[p] = true;
                    }
                    data.SpecksRemoved++;
                }
            }
        }

        // ── Sorties ────────────────────────────────────────────────────────────────────────────────────────
        var frame = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            frame[i * 4] = ToByte(nb[i]);
            frame[i * 4 + 1] = ToByte(ng[i]);
            frame[i * 4 + 2] = ToByte(nr[i]);
            frame[i * 4 + 3] = ToByte(na[i] * 255f);
        }
        data.Frame = frame;

        if (inner is not null)
        {
            // Léger débord sous le cadre (3 px), puis adoucissement des bords.
            data.InteriorAlpha = BoxBlur(Dilate(inner, w, h, 3), w, h);
        }
        return data;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v), 0f, 255f);

    private static float Median(List<float> values)
    {
        values.Sort();
        int c = values.Count;
        return c % 2 == 1 ? values[c / 2] : (values[c / 2 - 1] + values[c / 2]) / 2f;
    }

    private static int Count(bool[] mask)
    {
        int c = 0;
        foreach (bool m in mask)
            if (m) c++;
        return c;
    }

    private static IEnumerable<int> BorderSeeds(int w, int h)
    {
        for (int x = 0; x < w; x++)
        {
            yield return x;
            yield return (h - 1) * w + x;
        }
        for (int y = 1; y < h - 1; y++)
        {
            yield return y * w;
            yield return y * w + (w - 1);
        }
    }

    private static bool TouchesBorder(bool[] mask, int w, int h)
    {
        for (int x = 0; x < w; x++)
            if (mask[x] || mask[(h - 1) * w + x]) return true;
        for (int y = 0; y < h; y++)
            if (mask[y * w] || mask[y * w + (w - 1)]) return true;
        return false;
    }

    /// <summary>Remplissage par propagation (4 voisins), sans récursion, à partir des points de départ qui satisfont ok.</summary>
    private static bool[] Flood(bool[] ok, int w, int h, IEnumerable<int> seeds)
    {
        var seen = new bool[w * h];
        var stack = new int[w * h];
        int sp = 0;
        foreach (int s in seeds)
        {
            if (ok[s] && !seen[s])
            {
                seen[s] = true;
                stack[sp++] = s;
            }
        }
        while (sp > 0)
        {
            int idx = stack[--sp];
            int x = idx % w, y = idx / w;
            if (x > 0 && ok[idx - 1] && !seen[idx - 1]) { seen[idx - 1] = true; stack[sp++] = idx - 1; }
            if (x < w - 1 && ok[idx + 1] && !seen[idx + 1]) { seen[idx + 1] = true; stack[sp++] = idx + 1; }
            if (y > 0 && ok[idx - w] && !seen[idx - w]) { seen[idx - w] = true; stack[sp++] = idx - w; }
            if (y < h - 1 && ok[idx + w] && !seen[idx + w]) { seen[idx + w] = true; stack[sp++] = idx + w; }
        }
        return seen;
    }

    /// <summary>Le centre s'il convient, sinon le pixel qui convient le plus proche (balayage en anneaux). -1 si aucun.</summary>
    private static int NearestSeed(bool[] ok, int w, int h, int cx, int cy)
    {
        for (int r = 0; r <= Math.Max(w, h) / 2; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    if (ok[y * w + x]) return y * w + x;
                }
            }
        }
        return -1;
    }

    /// <summary>Dilatation carrée de rayon r (sans retour à l'autre bord de l'image).</summary>
    private static bool[] Dilate(bool[] src, int w, int h, int r)
    {
        var horizontal = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (src[y * w + x])
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx;
                        if (nx >= 0 && nx < w) horizontal[y * w + nx] = true;
                    }

        var result = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (horizontal[y * w + x])
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int ny = y + dy;
                        if (ny >= 0 && ny < h) result[ny * w + x] = true;
                    }
        return result;
    }

    /// <summary>Moyenne 3 x 3 d'un masque, en octets 0..255 (le hors-image compte pour 0).</summary>
    private static byte[] BoxBlur(bool[] mask, int w, int h)
    {
        var result = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[ny * w + nx]) sum++;
                    }
                }
                result[y * w + x] = ToByte(sum / 9f * 255f);
            }
        }
        return result;
    }
}
