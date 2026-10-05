"""Draws src/DbScanner/app.ico: a charm gem inside a scanner frame.

    python3 tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "DbScanner", "app.ico")

N = 1024  # drawn large, then scaled down for smooth edges
BG_TOP, BG_BOTTOM = (44, 56, 50), (24, 30, 27)
FRAME = (237, 225, 187)
GEM_LIGHT, GEM_MID, GEM_DARK = (150, 236, 255), (1, 201, 251), (0, 104, 160)
SHINE = (255, 255, 255)


def draw():
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))

    # Rounded tile with a soft vertical gradient.
    grad = Image.new("RGBA", (N, N))
    gd = ImageDraw.Draw(grad)
    for y in range(N):
        t = y / (N - 1)
        c = tuple(round(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        gd.line([(0, y), (N, y)], fill=c + (255,))
    mask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(mask).rounded_rectangle([24, 24, N - 24, N - 24], radius=190, fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([24, 24, N - 24, N - 24], radius=190, outline=(93, 116, 102, 255), width=14)

    # Scanner frame: four corner brackets.
    m, L, w = 170, 190, 54
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        x0 = m if sx == 0 else N - m
        y0 = m if sy == 0 else N - m
        dx = 1 if sx == 0 else -1
        dy = 1 if sy == 0 else -1
        d.rounded_rectangle(sorted_box(x0, y0, x0 + dx * L, y0 + dy * w), radius=w // 2, fill=FRAME)
        d.rounded_rectangle(sorted_box(x0, y0, x0 + dx * w, y0 + dy * L), radius=w // 2, fill=FRAME)

    # The gem: a cut diamond seen from the front.
    cx, cy = N // 2, N // 2 + 10
    top, mid, bot, half, crown = cy - 230, cy - 70, cy + 250, 250, 140
    girdle_l, girdle_r = (cx - half, mid), (cx + half, mid)
    table_l, table_r = (cx - crown, top), (cx + crown, top)
    tip = (cx, bot)
    d.polygon([table_l, table_r, girdle_r, tip, girdle_l], fill=GEM_MID)
    d.polygon([table_l, (cx, mid), girdle_l], fill=GEM_LIGHT)
    d.polygon([table_l, table_r, (cx, mid)], fill=(90, 222, 255))
    d.polygon([table_r, girdle_r, (cx, mid)], fill=GEM_MID)
    d.polygon([girdle_l, (cx, mid), tip], fill=GEM_MID)
    d.polygon([(cx, mid), girdle_r, tip], fill=GEM_DARK)
    d.line([table_l, table_r, girdle_r, tip, girdle_l, table_l], fill=(0, 70, 110), width=14, joint="curve")
    d.ellipse([cx - 120, top + 40, cx - 60, top + 100], fill=SHINE)

    # Scan line across the gem.
    d.rounded_rectangle([m + 40, cy + 40, N - m - 40, cy + 70], radius=15, fill=(248, 221, 69, 235))
    return img


def sorted_box(x0, y0, x1, y1):
    return [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]


def main():
    big = draw()
    sizes = [16, 24, 32, 48, 64, 128, 256]
    big.resize((256, 256), Image.LANCZOS).save(OUT, format="ICO", sizes=[(s, s) for s in sizes])
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(ROOT, "docs", "icon.png"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
