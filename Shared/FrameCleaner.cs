using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace D4Overlay.Shared;

/// <summary>Image de cadre nettoyée, avec le masque de son fond intérieur (null si le cadre n'est pas fermé).</summary>
public sealed class FrameCleanResult
{
    public FrameCleanResult(Bitmap frame, Bitmap? interior, bool changed, string notes)
    {
        Frame = frame;
        Interior = interior;
        Changed = changed;
        Notes = notes;
    }

    public Bitmap Frame { get; }
    public Bitmap? Interior { get; }

    /// <summary>true si l'image d'origine a été corrigée (voile extérieur supprimé et/ou fond intérieur intégré rendu transparent).</summary>
    public bool Changed { get; }

    /// <summary>Description de ce qui a été corrigé, pour le journal.</summary>
    public string Notes { get; }
}

/// <summary>
/// Nettoie une image de cadre importée (retouchée dans GIMP, générée par IA…) : voile semi-transparent autour du cadre,
/// fond intérieur intégré, petits résidus. Une image déjà propre n'est pas modifiée. L'algorithme est dans FrameCleanerCore ;
/// ici seulement la conversion depuis et vers les Bitmap.
/// </summary>
public static class FrameCleaner
{
    public static FrameCleanResult Clean(Bitmap source)
    {
        int w = source.Width, h = source.Height;
        var data = FrameCleanerCore.Run(ReadBgra(source, w, h), w, h);

        var notes = new List<string>();
        if (data.VeilRemoved) notes.Add($"voile extérieur supprimé (≈ {data.VeilOpacity:P0})");
        if (data.InteriorFlattened) notes.Add($"fond intérieur intégré rendu transparent (≈ {data.InteriorOpacity:P0})");
        if (data.SpecksRemoved > 0) notes.Add($"{data.SpecksRemoved} résidu(s) intérieur(s) retiré(s)");

        var frame = FromBgra(data.Frame, w, h);
        Bitmap? interior = data.InteriorAlpha is null ? null : MaskBitmap(data.InteriorAlpha, w, h);
        return new FrameCleanResult(frame, interior, data.Changed, string.Join(", ", notes));
    }

    private static byte[] ReadBgra(Bitmap source, int w, int h)
    {
        var bits = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), bytes, y * w * 4, w * 4);
            return bytes;
        }
        finally { source.UnlockBits(bits); }
    }

    private static Bitmap FromBgra(byte[] bgra, int w, int h)
    {
        var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(bgra, y * w * 4, IntPtr.Add(bits.Scan0, y * bits.Stride), w * 4);
        }
        finally { bitmap.UnlockBits(bits); }
        return bitmap;
    }

    /// <summary>Image noire dont seul l'alpha varie : c'est le fond intérieur, dessiné sous le cadre avec la couleur et l'opacité choisies.</summary>
    private static Bitmap MaskBitmap(byte[] alpha, int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (int i = 0; i < alpha.Length; i++) bgra[i * 4 + 3] = alpha[i];
        return FromBgra(bgra, w, h);
    }
}
