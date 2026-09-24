#!/usr/bin/env python3
"""
make_icons.py - Genera los recursos graficos del plugin Umayor Test Data Seeder a partir del
logo de la aplicacion provisto por el autor (escudo + texto "Umayor / TEST DATA SEEDER").

Salidas (rutas relativas a este script):
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/icon_small_32.png    (SmallImageBase64)
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/icon_big_80.png      (BigImageBase64)
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/app-logo-64.png      (About, EmbeddedResource)
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/app-logo-source.png  (fuente versionada;
                                                                             solo con --app-logo-src)

Fuente: con --app-logo-src se lee ese archivo (p. ej. el .webp original) y se guarda en el repo
una copia recortada al contenido + margen como app-logo-source.png; sin el argumento se usa ese
PNG versionado, de modo que la regeneracion es reproducible y da los mismos PNG.

- Iconos: SOLO el escudo (componente izquierdo, separado del texto por una franja de columnas
  blancas). El fondo blanco exterior se vuelve transparente: la silueta del escudo es el
  contenido (L < 235) cerrado morfologicamente (SEAL_SIZE, sella el hueco del borde por donde
  sale la flecha) y con los agujeros rellenos, de modo que los blancos interiores quedan
  opacos. La mascara se calcula a resolucion completa; al reducir, el color va con LANCZOS y
  el alfa con BOX (cobertura exacta, borde con antialiasing y sin anillos de rebote). Se centra
  en un lienzo cuadrado con ~6% de margen. El icono de 32 px lleva un enfoque suave solo en el
  color de los pixeles opacos.
- Los iconos se guardan como PNG de paleta (256 colores con alfa por entrada, tRNS): paleta
  inicial FASTOCTREE refinada con k-means (Lloyd) determinista; los pixeles opacos solo usan
  entradas opacas (alfa 255 exacto) y los transparentes el indice 0. Deja el base64 del icono de
  80 px en ~3700 caracteres con error medio ~2 por canal (se imprime).
- Logo del About: logo completo recortado al contenido, 64 px de alto, fondo blanco.

Imprime el base64 de cada icono y su largo: cada string DEBE quedar bajo 16383 caracteres (bug
de CustomAttributeFormatException, ver tools/verify_attr_blobs.py del repo
DataverseMasterDataMigrator).

Uso:
    python make_icons.py [--app-logo-src RUTA/app-logo-source.webp] [--preview-dir DIR]

Requiere Pillow.
"""
import argparse
import base64
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RES_DIR = os.path.normpath(os.path.join(HERE, "..", "src", "Umayor.TestDataSeeder.XrmToolBox", "Resources"))
REPO_SOURCE = os.path.join(RES_DIR, "app-logo-source.png")

MAX_ATTR_LEN = 16383
CONTENT_L_THRESHOLD = 235   # L < 235 = contenido; el resto es fondo blanco
SEAL_SIZE = 9               # cierre morfologico (px de la fuente) que sella la silueta del escudo
PALETTE_COLORS = 256        # entradas de la paleta de los iconos (incluida la transparente)
KMEANS_ITERS = 30           # iteraciones maximas de Lloyd sobre la paleta
SOURCE_MARGIN = 16          # margen blanco alrededor del contenido en app-logo-source.png
ICON_MARGIN = 0.06          # margen del escudo dentro del lienzo cuadrado del icono
LOGO_HEIGHT = 64            # alto del logo del About


def png_bytes(img, transparency=None):
    img = img.copy()
    img.info = {}  # sin metadata (el .webp trae un perfil ICC que se heredaria)
    buf = io.BytesIO()
    kw = {"transparency": transparency} if transparency is not None else {}
    img.save(buf, format="PNG", optimize=True, compress_level=9, **kw)
    return buf.getvalue()


def save_png(img, path, transparency=None):
    data = png_bytes(img, transparency)
    with open(path, "wb") as f:
        f.write(data)
    return data


def content_bbox(rgb):
    """bbox de los pixeles con L < umbral (el fondo es blanco casi puro)."""
    return rgb.convert("L").point(lambda v: 255 if v < CONTENT_L_THRESHOLD else 0).getbbox()


def shield_bbox(logo):
    """logo = recorte al contenido. El escudo es el componente izquierdo: termina donde empieza la
    primera franja de columnas completamente blancas; luego se ajusta su bbox vertical."""
    w, h = logo.size
    content = logo.convert("L").point(lambda v: 255 if v < CONTENT_L_THRESHOLD else 0)
    px = content.load()
    right = w
    for x in range(w):
        if not any(px[x, y] for y in range(h)):
            right = x
            break
    return content.crop((0, 0, right, h)).getbbox()


def shield_alpha(rgb):
    """Mascara L: 255 = escudo (incluidos los blancos interiores), 0 = fondo exterior.
    Silueta = contenido cerrado morfologicamente (sella los huecos del borde, p. ej. donde sale
    la flecha) con los agujeros rellenos (flood fill desde fuera sobre la version cerrada)."""
    w, h = rgb.size
    content = rgb.convert("L").point(lambda v: 255 if v < CONTENT_L_THRESHOLD else 0)
    pad = SEAL_SIZE + 2
    padded = Image.new("L", (w + 2 * pad, h + 2 * pad), 0)
    padded.paste(content, (pad, pad))
    closed = padded.filter(ImageFilter.MaxFilter(SEAL_SIZE)).filter(ImageFilter.MinFilter(SEAL_SIZE))
    ImageDraw.floodfill(closed, (0, 0), 128, thresh=0)
    return closed.point(lambda v: 0 if v == 128 else 255).crop((pad, pad, pad + w, pad + h))


def quantize_rgba(img):
    """RGBA -> (imagen P, bytes tRNS). Indice 0 = transparente; los pixeles opacos solo se
    asignan a entradas opacas (alfa 255 exacto) y los semitransparentes a entradas
    semitransparentes. Paleta inicial FASTOCTREE + Lloyd (determinista)."""
    w, h = img.size
    alpha = img.getchannel("A")
    clean = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    clean.paste(img, (0, 0), alpha.point(lambda a: 255 if a else 0))
    hist = sorted((c, n) for n, c in clean.getcolors(w * h) if c[3] > 0)

    # Paleta inicial: FASTOCTREE por separado sobre los colores opacos y los semitransparentes,
    # con las 255 entradas repartidas en proporcion a sus colores distintos.
    groups = ([(c, n) for c, n in hist if c[3] == 255], [(c, n) for c, n in hist if c[3] < 255])
    budget = PALETTE_COLORS - 1
    n_opaque = min(len(groups[0]), max(1, round(budget * len(groups[0]) / len(hist))))
    centers = []
    for group, colors in zip(groups, (n_opaque, budget - n_opaque)):
        if not group:
            continue
        if len(group) <= colors:
            centers += [c for c, _ in group]
            continue
        strip = Image.new("RGBA", (sum(n for _, n in group), 1))
        strip.putdata([c for c, n in group for _ in range(n)])
        q = strip.quantize(colors=colors, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
        pal = q.getpalette(rawmode="RGBA")[:4 * colors]
        opaque = group[0][0][3] == 255
        for i in range(0, len(pal), 4):
            c = tuple(pal[i:i + 4])
            centers.append(c[:3] + (255,) if opaque else c[:3] + (min(254, max(1, c[3])),))
    centers = list(dict.fromkeys(centers))

    def nearest(c):
        opaque = c[3] == 255
        best, best_d = None, None
        for i, k in enumerate(centers):
            if (k[3] == 255) != opaque:
                continue
            d = (c[0] - k[0]) ** 2 + (c[1] - k[1]) ** 2 + (c[2] - k[2]) ** 2 + (c[3] - k[3]) ** 2
            if best_d is None or d < best_d:
                best, best_d = i, d
        return best

    for _ in range(KMEANS_ITERS):
        acc = [[0, 0, 0, 0, 0] for _ in centers]
        for c, n in hist:
            a = acc[nearest(c)]
            a[4] += n
            for k in range(4):
                a[k] += n * c[k]
        new = [tuple((a[k] + a[4] // 2) // a[4] for k in range(4)) if a[4] else centers[i]
               for i, a in enumerate(acc)]
        if new == centers:
            break
        centers = new

    index = {c: 1 + nearest(c) for c, _ in hist}
    out = Image.new("P", (w, h), 0)
    op, cp = out.load(), clean.load()
    for y in range(h):
        for x in range(w):
            c = cp[x, y]
            if c[3]:
                op[x, y] = index[c]
    full = [(0, 0, 0, 0)] + centers
    out.putpalette([v for c in full for v in c[:3]])
    return out, bytes(c[3] for c in full)


def quant_error(ref, data):
    """(error medio por canal RGBA, error maximo) sobre los pixeles con alfa > 0."""
    back = Image.open(io.BytesIO(data)).convert("RGBA")
    rp, bp = ref.load(), back.load()
    total = n = worst = 0
    for y in range(ref.size[1]):
        for x in range(ref.size[0]):
            r, b = rp[x, y], bp[x, y]
            if r[3] or b[3]:
                d = [abs(r[i] - b[i]) for i in range(4)]
                total += sum(d)
                n += 4
                worst = max(worst, max(d))
    return total / n, worst


def make_icons(logo):
    sb = shield_bbox(logo)
    shield = logo.crop(sb)
    alpha = shield_alpha(shield)
    rgba = shield.convert("RGBA")
    rgba.putalpha(alpha)

    w, h = rgba.size
    side = int(round(max(w, h) / (1 - 2 * ICON_MARGIN)))
    canvas = Image.new("RGBA", (side, side), (255, 255, 255, 0))
    canvas.paste(rgba, ((side - w) // 2, (side - h) // 2), rgba)

    big = downscale(canvas, 80)
    small = downscale(canvas, 32)
    # Enfoque suave solo del color de los pixeles opacos: aplicado sobre RGBA completo, el
    # UnsharpMask tambien enfoca el alfa y el color no premultiplicado del borde (halo claro).
    sharp = small.filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=2))
    small.paste(sharp.convert("RGB"), (0, 0), small.getchannel("A").point(lambda a: 255 if a == 255 else 0))
    return big, small, sb


def downscale(canvas, size):
    """Color con LANCZOS (Pillow premultiplica el alfa: sin halo del fondo transparente) y alfa
    con BOX (cobertura exacta: sin el anillo ni los huecos de 250-254 que deja el rebote de
    LANCZOS en la mascara binaria)."""
    img = canvas.resize((size, size), Image.LANCZOS)
    img.putalpha(canvas.getchannel("A").resize((size, size), Image.BOX))
    return img


def make_about_logo(logo):
    w, h = logo.size
    new_w = int(round(w * LOGO_HEIGHT / h))
    return logo.resize((new_w, LOGO_HEIGHT), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--app-logo-src", help="logo de la aplicacion original (p. ej. app-logo-source.webp)")
    ap.add_argument("--preview-dir", help="carpeta para previews ampliados (fuera del repo)")
    args = ap.parse_args()
    os.makedirs(RES_DIR, exist_ok=True)

    src_path = args.app_logo_src or REPO_SOURCE
    src = Image.open(src_path).convert("RGB")
    bbox = content_bbox(src)
    print(f"fuente: {src_path} {src.size[0]}x{src.size[1]}, bbox de contenido {bbox}")

    if args.app_logo_src:
        x0, y0, x1, y1 = bbox
        m = SOURCE_MARGIN
        crop = (max(0, x0 - m), max(0, y0 - m), min(src.size[0], x1 + m), min(src.size[1], y1 + m))
        save_png(src.crop(crop), REPO_SOURCE)
        print(f"app-logo-source.png: recorte {crop} -> {crop[2] - crop[0]}x{crop[3] - crop[1]}")

    logo = src.crop(bbox)
    big, small, sb = make_icons(logo)
    print(f"escudo: bbox {sb} relativo al contenido ({sb[2] - sb[0]}x{sb[3] - sb[1]}), "
          f"absoluto en la fuente ({bbox[0] + sb[0]}, {bbox[1] + sb[1]}, {bbox[0] + sb[2]}, {bbox[1] + sb[3]})")

    ok = True
    for name, img in (("icon_small_32.png", small), ("icon_big_80.png", big)):
        pal_img, trns = quantize_rgba(img)
        data = save_png(pal_img, os.path.join(RES_DIR, name), trns)
        b64 = base64.b64encode(data).decode("ascii")
        mean, worst = quant_error(img, data)
        print(f"{name}: {len(data)} bytes, base64 {len(b64)} chars "
              f"(paleta: error medio {mean:.2f}, maximo {worst} por canal)")
        if len(b64) > MAX_ATTR_LEN:
            print(f"  ERROR: supera {MAX_ATTR_LEN} caracteres")
            ok = False

    about = make_about_logo(logo)
    data = save_png(about, os.path.join(RES_DIR, "app-logo-64.png"))
    print(f"app-logo-64.png: {about.size[0]}x{about.size[1]}, {len(data)} bytes")

    if args.preview_dir:
        os.makedirs(args.preview_dir, exist_ok=True)
        for name, size, preview in (("icon_big_80.png", 320, "icon_preview_big.png"),
                                    ("icon_small_32.png", 128, "icon_preview_small.png")):
            icon = Image.open(os.path.join(RES_DIR, name)).convert("RGBA")
            icon.resize((size, size), Image.NEAREST).save(os.path.join(args.preview_dir, preview))
        about.save(os.path.join(args.preview_dir, "about_logo_preview.png"))

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
