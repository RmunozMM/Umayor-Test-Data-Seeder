#!/usr/bin/env python3
"""
make_icons.py - Genera los recursos graficos del plugin Umayor Test Data Seeder.

Salidas (rutas relativas a este script):
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/icon_small_32.png   (SmallImageBase64)
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/icon_big_80.png     (BigImageBase64)
    ../src/Umayor.TestDataSeeder.XrmToolBox/Resources/um-horizontal-color-48.png  (About)

Ademas imprime el base64 de cada icono (para pegarlo en Plugin.cs) y su largo: cada string
DEBE quedar bajo 16383 caracteres (bug de CustomAttributeFormatException, ver
tools/verify_attr_blobs.py del repo DataverseMasterDataMigrator).

El icono NO usa el escudo: el Manual de Marca UMayor 2024 prohibe separar el escudo del texto,
y el isologo completo es ilegible a 32 px. Es un icono propio con la paleta de marca: cuadrado
redondeado Gris 2, "sol" Amarillo, cilindro de base de datos blanco y un brote Amarillo
("seeder"). Se dibuja a 8x y se reduce con LANCZOS.

El logo del About se obtiene del isologo horizontal oficial a color (no versionado en el repo):
recorte al bbox de contenido y reduccion con proporcion bloqueada a 48 px de alto.

Uso:
    python make_icons.py [--logo-src RUTA/um-horizontal-color.png] [--preview-dir DIR]

Sin --logo-src se omite la regeneracion del logo (se conserva el PNG ya versionado).
Requiere Pillow.
"""
import argparse
import base64
import io
import math
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
RES_DIR = os.path.normpath(os.path.join(HERE, "..", "src", "Umayor.TestDataSeeder.XrmToolBox", "Resources"))

# Paleta Manual UMayor 2024
AMARILLO = (0xFE, 0xCE, 0x40, 255)
GRIS2 = (0x34, 0x37, 0x42, 255)
BLANCO = (255, 255, 255, 255)

SUPERSAMPLE = 8
MAX_ATTR_LEN = 16383
LOGO_HEIGHT = 48


def ellipse_polygon(cx, cy, rx, ry, angle_deg, steps=180):
    a = math.radians(angle_deg)
    ca, sa = math.cos(a), math.sin(a)
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    return pts


def draw_icon(final_size, simplified):
    S = final_size * SUPERSAMPLE

    # Mascara del cuadrado redondeado (todo lo demas queda transparente)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(0.18 * S), fill=255)

    art = Image.new("RGBA", (S, S), GRIS2)
    d = ImageDraw.Draw(art)

    # "Sol" amarillo arriba a la derecha (se recorta con la mascara)
    sun_r = 0.21 * S
    sun_c = (0.72 * S, 0.28 * S)
    d.ellipse((sun_c[0] - sun_r, sun_c[1] - sun_r, sun_c[0] + sun_r, sun_c[1] + sun_r), fill=AMARILLO)

    # Cilindro de base de datos
    line = (0.065 if simplified else 0.035) * S
    discs = 2 if simplified else 3
    cx = 0.45 * S
    w = 0.50 * S
    rx = w / 2
    ry = 0.075 * S
    top_y = 0.47 * S
    bottom_y = 0.80 * S
    left, right = cx - rx, cx + rx

    # Cuerpo blanco + base
    d.rectangle((left, top_y, right, bottom_y), fill=BLANCO)
    d.ellipse((left, bottom_y - ry, right, bottom_y + ry), fill=BLANCO)

    # Separadores de discos (medio arco inferior en Gris 2)
    for i in range(1, discs):
        y = top_y + (bottom_y - top_y) * i / discs
        d.arc((left, y - ry, right, y + ry), start=0, end=180, fill=GRIS2, width=max(1, int(line)))

    # Tapa superior: elipse blanca con borde Gris 2
    d.ellipse((left, top_y - ry, right, top_y + ry), fill=BLANCO, outline=GRIS2, width=max(1, int(line)))

    # Brote de dos hojas (con contorno Gris 2 para separarlo del sol amarillo)
    stem_w = (0.05 if simplified else 0.035) * S
    outline = (0.045 if simplified else 0.022) * S
    stem_bottom = top_y
    stem_top = top_y - (0.12 if simplified else 0.14) * S
    leaf_rx = (0.10 if simplified else 0.095) * S
    leaf_ry = (0.05 if simplified else 0.042) * S
    if simplified:
        # En 32 px las hojas se abren mas en "V" para no fundirse con el sol
        leaves = [
            (cx - 0.06 * S, stem_top - 0.07 * S, 55),   # hoja izquierda, eje arriba-izquierda
            (cx + 0.06 * S, stem_top - 0.07 * S, -55),  # hoja derecha, eje arriba-derecha
        ]
    else:
        leaves = [
            (cx - 0.075 * S, stem_top + 0.005 * S, -35),   # hoja izquierda
            (cx + 0.080 * S, stem_top - 0.030 * S, 35 - 180),  # hoja derecha (mas alta)
        ]
    # contorno
    d.line((cx, stem_bottom, cx, stem_top - 0.01 * S), fill=GRIS2, width=int(stem_w + 2 * outline))
    for lx, ly, ang in leaves:
        d.polygon(ellipse_polygon(lx, ly, leaf_rx + outline, leaf_ry + outline, ang), fill=GRIS2)
    # relleno
    d.line((cx, stem_bottom, cx, stem_top - 0.01 * S), fill=AMARILLO, width=int(stem_w))
    for lx, ly, ang in leaves:
        d.polygon(ellipse_polygon(lx, ly, leaf_rx, leaf_ry, ang), fill=AMARILLO)
    # base del tallo sobre la tapa: pequeña elipse amarilla para que "brote" de ella
    d.ellipse((cx - stem_w, stem_bottom - stem_w * 0.45, cx + stem_w, stem_bottom + stem_w * 0.45), fill=AMARILLO)

    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(art, (0, 0), mask)
    return out.resize((final_size, final_size), Image.LANCZOS)


def png_bytes(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)  # sin pnginfo -> sin metadata
    return buf.getvalue()


def save_png(img, path):
    data = png_bytes(img)
    with open(path, "wb") as f:
        f.write(data)
    return data


def make_logo(src):
    img = Image.open(src).convert("RGBA")
    bbox = img.getchannel("A").getbbox()
    img = img.crop(bbox)
    w, h = img.size
    new_w = int(round(w * LOGO_HEIGHT / h))
    return img.resize((new_w, LOGO_HEIGHT), Image.LANCZOS), bbox


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--logo-src", help="um-horizontal-color.png oficial (isologo horizontal a color)")
    ap.add_argument("--preview-dir", help="carpeta para previews ampliados (fuera del repo)")
    args = ap.parse_args()

    os.makedirs(RES_DIR, exist_ok=True)
    big = draw_icon(80, simplified=False)
    small = draw_icon(32, simplified=True)
    ok = True
    for name, img in (("icon_small_32.png", small), ("icon_big_80.png", big)):
        data = save_png(img, os.path.join(RES_DIR, name))
        b64 = base64.b64encode(data).decode("ascii")
        print(f"{name}: {len(data)} bytes, base64 {len(b64)} chars")
        if len(b64) > MAX_ATTR_LEN:
            print(f"  ERROR: supera {MAX_ATTR_LEN} caracteres")
            ok = False

    if args.preview_dir:
        os.makedirs(args.preview_dir, exist_ok=True)
        big.resize((320, 320), Image.NEAREST).save(os.path.join(args.preview_dir, "icon_preview_big.png"))
        small.resize((128, 128), Image.NEAREST).save(os.path.join(args.preview_dir, "icon_preview_small.png"))

    if args.logo_src:
        logo, bbox = make_logo(args.logo_src)
        save_png(logo, os.path.join(RES_DIR, "um-horizontal-color-48.png"))
        print(f"um-horizontal-color-48.png: bbox {bbox} -> {logo.size[0]}x{logo.size[1]}")
    else:
        print("Sin --logo-src: se conserva um-horizontal-color-48.png existente.")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
