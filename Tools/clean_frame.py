#!/usr/bin/env python3
"""
Nettoie un cadre d'overlay (PNG) pour D4 Overlay.

  python clean_frame.py mon_cadre.png                -> mon_cadre.clean.png  +  mon_cadre.fill.png
  python clean_frame.py mon_cadre.png dossier_sortie

Ce que ça corrige (typique des images retouchées dans GIMP / générées par IA) :
  1. VOILE EXTERIEUR : le fond autour du cadre n'est pas totalement transparent (par exemple un noir à 20-40 %).
     On le repère, on le supprime, et on « démélange » les pixels de bord pour garder des contours lisses, sans halo.
  2. FOND INTERIEUR INTEGRE : si l'intérieur est un aplat sombre semi-opaque, on le rend transparent (l'application
     le remplace par un fond dont la couleur et l'opacité se règlent dans l'onglet Apparence).
  3. RESIDUS : petites taches isolées à l'intérieur du cadre.

NOTE : depuis cette version, le configurateur fait ce même nettoyage tout seul à l'import d'une image (« Importer une image »).
Cet outil sert surtout à préparer les cadres intégrés à l'exe et à comprendre ce qui est corrigé.

Deux fichiers sortent :
  *.clean.png : le cadre à importer dans le configurateur (« Importer une image »).
  *.fill.png  : le masque du fond intérieur (pour intégrer le cadre à l'exe ; inutile pour une image importée,
                car l'application le recalcule toute seule).

Prérequis : Python 3, `pip install pillow numpy`.
"""
import sys, os
from collections import deque
import numpy as np
from PIL import Image


def flood(ok, seeds):
    h, w = ok.shape
    seen = np.zeros((h, w), bool)
    q = deque()
    for (x, y) in seeds:
        if ok[y, x] and not seen[y, x]:
            seen[y, x] = True
            q.append((x, y))
    while q:
        x, y = q.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny, nx] and ok[ny, nx]:
                seen[ny, nx] = True
                q.append((nx, ny))
    return seen


def shifted(m, dy, dx):
    """m décalé de (dy, dx) SANS retour à l'autre bord de l'image (ce que fait np.roll)."""
    h, w = m.shape
    out = np.zeros_like(m)
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = m[ys, xs]
    return out


def dilate(m, r):
    out = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out |= shifted(m, dy, dx)
    return out


def box_blur_u8(mask):
    acc = np.zeros(mask.shape, float)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            acc += shifted(mask.astype(float), dy, dx)
    return np.rint(acc / 9 * 255).astype(np.uint8)


def components(mask):
    h, w = mask.shape
    seen = np.zeros((h, w), bool)
    result = []
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not seen[y, x]:
                q = deque([(x, y)])
                seen[y, x] = True
                pts = []
                while q:
                    cx, cy = q.popleft()
                    pts.append((cx, cy))
                    for dx in (-1, 0, 1):
                        for dy in (-1, 0, 1):
                            nx, ny = cx + dx, cy + dy
                            if (dx or dy) and 0 <= nx < w and 0 <= ny < h and mask[ny, nx] and not seen[ny, nx]:
                                seen[ny, nx] = True
                                q.append((nx, ny))
                result.append(pts)
    return result


def touches_border(m):
    return bool(m[0, :].any() or m[-1, :].any() or m[:, 0].any() or m[:, -1].any())


def nearest_seed(ok, cx, cy):
    h, w = ok.shape
    for r in range(0, max(w, h) // 2 + 1):
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if max(abs(dx), abs(dy)) != r:
                    continue
                x, y = cx + dx, cy + dy
                if 0 <= x < w and 0 <= y < h and ok[y, x]:
                    return (x, y)
    return None


# Mêmes seuils que Shared/FrameCleanerCore.cs (l'application fait ce même nettoyage à l'import d'une image).
DARK_LIMIT = 70
VEIL_MAX_ALPHA = 0.78
VEIL_MARGIN = 35
BAKED_MIN_ALPHA8 = 60
OPAQUE_ALPHA = 0.97
BAKED_TOLERANCE8 = 4
MAX_SPECK_PIXELS = 40


def clean(path):
    im = Image.open(path).convert("RGBA")
    a = np.array(im).astype(np.float64)
    h, w = a.shape[:2]
    alpha8 = a[:, :, 3]                       # alpha en octets : seuils comparés en entiers
    alpha = alpha8 / 255.0
    rgb = a[:, :, :3]
    dark = rgb.max(axis=2) < DARK_LIMIT
    new_rgb = rgb.copy()
    new_alpha = alpha.copy()
    report = []

    # 1. voile extérieur : estimé sur l'anneau de 3 px au bord de l'image
    ring = np.zeros((h, w), bool)
    ring[:3, :] = ring[-3:, :] = ring[:, :3] = ring[:, -3:] = True
    candidates = ring & dark & (alpha8 < VEIL_MAX_ALPHA * 255)
    if candidates.sum() > 0.2 * ring.sum():
        threshold = float(np.median(alpha8[candidates])) + VEIL_MARGIN
        ok = (alpha8 < threshold) & dark
        border = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)] + [(0, y) for y in range(1, h - 1)] + [(w - 1, y) for y in range(1, h - 1)]
        outside = flood(ok, border)
        if 0 < outside.sum() < 0.9 * w * h:
            av = float(np.median(alpha[outside]))
            if av <= 0.02:
                outside = np.zeros_like(outside)  # extérieur déjà transparent : rien à corriger
        if 0 < outside.sum() < 0.9 * w * h:
            cv = rgb[outside].mean(axis=0)
            band = dilate(outside, 2) & ~outside & (alpha < 0.98)
            ao = np.clip((alpha - av) / (1 - av), 0, 1)
            safe = np.maximum(ao, 1e-3)[..., None]
            co = np.clip((rgb * alpha[..., None] - cv * ((1 - ao) * av)[..., None]) / safe, 0, 255)
            new_rgb[band] = co[band]
            new_alpha[band] = ao[band]
            new_alpha[outside] = 0
            new_alpha[new_alpha < 0.04] = 0
            report.append(f"voile extérieur supprimé (opacité d'origine ≈ {av * 100:.0f} %, {int(outside.sum())} px)")
    if not report:
        report.append("pas de voile extérieur détecté")

    # 2. intérieur
    cx, cy = w // 2, h // 2
    center_alpha8 = alpha8[cy, cx]
    center_alpha = center_alpha8 / 255.0
    inner = None
    flattened = False
    if center_alpha8 >= BAKED_MIN_ALPHA8:
        if center_alpha < OPAQUE_ALPHA:
            region = flood(np.abs(alpha8 - center_alpha8) <= BAKED_TOLERANCE8, [(cx, cy)])
            if region.sum() >= 0.05 * w * h and not touches_border(region):
                inner = region
                new_alpha[inner] = 0
                flattened = True
                report.append(f"fond intérieur intégré rendu transparent (opacité d'origine ≈ {center_alpha * 100:.0f} %)")
        if not flattened:
            report.append("intérieur plein ou irrégulier : laissé tel quel (pas de fond réglable pour cette image)")
    else:
        low = np.rint(new_alpha * 255) < 60
        seed = nearest_seed(low, cx, cy)
        if seed is not None:
            region = flood(low, [seed])
            if not touches_border(region) and region.sum() > 0:
                inner = region
        report.append("l'intérieur était déjà transparent" if inner is not None else "cadre non fermé : pas de fond intérieur")

    # 3. résidus (seulement quand l'intérieur a été vidé par ce script)
    if inner is not None and flattened:
        ys, xs = np.where(inner)
        bx0, by0, bx1, by1 = xs.min(), ys.min(), xs.max(), ys.max()
        removed = 0
        for pts in components(new_alpha > 0.03):
            px = [p[0] for p in pts]
            py = [p[1] for p in pts]
            if len(pts) < MAX_SPECK_PIXELS and bx0 < min(px) and max(px) < bx1 and by0 < min(py) and max(py) < by1:
                for (qx, qy) in pts:
                    new_alpha[qy, qx] = 0
                    inner[qy, qx] = True
                removed += 1
        if removed:
            report.append(f"{removed} petit(s) résidu(s) intérieur(s) supprimé(s)")

    frame = np.dstack([new_rgb, new_alpha * 255])
    frame = np.rint(frame).clip(0, 255).astype(np.uint8)

    fill = np.zeros((h, w, 4), np.uint8)
    if inner is not None:
        fill[:, :, 3] = box_blur_u8(dilate(inner, 3))
    return Image.fromarray(frame, "RGBA"), Image.fromarray(fill, "RGBA"), report, (w, h)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    src = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.abspath(src))
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.splitext(os.path.basename(src))[0]
    frame, fill, report, size = clean(src)
    frame.save(os.path.join(out_dir, base + ".clean.png"), optimize=True)
    fill.save(os.path.join(out_dir, base + ".fill.png"), optimize=True)
    print(f"{src} ({size[0]}x{size[1]})")
    for line in report:
        print("  -", line)
    print("  ->", os.path.join(out_dir, base + ".clean.png"), "et", base + ".fill.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())
