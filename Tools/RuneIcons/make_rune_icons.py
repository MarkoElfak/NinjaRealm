#!/usr/bin/env python3
"""
Draws the Shrine's rune icons: a faceted low-poly standing stone with a glowing carved glyph.

    python3 Tools/RuneIcons/make_rune_icons.py

Writes one 256x256 PNG per rune to Assets/_Project/Art/Runes/Resources/RuneIcons/<name>.png,
where <name> is the rune's ID without "rune." (RuneDefinition.IconName). The game loads the
icons by that name, so a new rune needs a new entry in RUNES and a run of this script.
Needs Pillow (pip install pillow). The colours match PrototypeContent's rune colours.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
SCALE = 4  # drawn at 4x, then scaled down for smooth edges
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "_Project", "Art", "Runes", "Resources", "RuneIcons")

BLADE = (242, 92, 77)
SHELL = (143, 189, 235)
SPIRIT = (189, 133, 255)
FORTUNE = (255, 209, 89)
FIRE = (232, 84, 41)
WATER = (46, 140, 235)
EARTH = (176, 135, 92)  # Earth's trait colour, lifted so it glows on dark stone

RARITY = {
    "Common": (156, 163, 176),
    "Rare": (74, 163, 255),
    "Epic": (176, 97, 255),
    "Legendary": (255, 184, 48),
}


def arc(cx, cy, r, start, end, steps=24):
    """Points on a circle, angles in degrees, 0 = right, counter-clockwise, y down in the glyph box."""
    pts = []
    for i in range(steps + 1):
        a = math.radians(start + (end - start) * i / steps)
        pts.append((cx + r * math.cos(a), cy - r * math.sin(a)))
    return pts


def wave(x0, x1, y, amp, waves=2, steps=40):
    return [(x0 + (x1 - x0) * i / steps, y + amp * math.sin(i / steps * waves * 2 * math.pi)) for i in range(steps + 1)]


# Each glyph is a list of strokes; each stroke a list of points in a 0..1 box (y down).
GLYPHS = {
    "ember_fang": [[(0.38, 0.18), (0.62, 0.42), (0.52, 0.62), (0.44, 0.86)], [(0.58, 0.20), (0.70, 0.34)], [(0.30, 0.50), (0.46, 0.56)]],
    "hummingbird_sigil": [[(0.20, 0.50), (0.50, 0.42), (0.82, 0.30)], [(0.50, 0.42), (0.36, 0.20)], [(0.50, 0.42), (0.66, 0.68), (0.50, 0.84)]],
    "oak_heart": [[(0.50, 0.86), (0.50, 0.30)], arc(0.50, 0.34, 0.20, 200, -20), [(0.50, 0.58), (0.30, 0.46)], [(0.50, 0.66), (0.70, 0.54)]],
    "iron_bark": [[(0.28, 0.20), (0.28, 0.82)], [(0.72, 0.20), (0.72, 0.82)], [(0.28, 0.36), (0.72, 0.50)], [(0.28, 0.56), (0.72, 0.70)]],
    "still_pond": [arc(0.50, 0.50, 0.30, 0, 360, 40), [(0.30, 0.56), (0.70, 0.56)], [(0.38, 0.68), (0.62, 0.68)]],
    "dawn_coal": [arc(0.50, 0.66, 0.22, 0, 180), [(0.22, 0.66), (0.78, 0.66)], [(0.50, 0.38), (0.50, 0.20)], [(0.30, 0.46), (0.22, 0.34)], [(0.70, 0.46), (0.78, 0.34)]],
    "coin_lantern": [arc(0.50, 0.54, 0.26, 0, 360, 40), [(0.40, 0.44), (0.60, 0.44), (0.60, 0.64), (0.40, 0.64), (0.40, 0.44)], [(0.50, 0.28), (0.50, 0.14)]],
    "hawk_eye": [arc(0.50, 0.72, 0.36, 30, 150), arc(0.50, 0.28, 0.36, 210, 330), arc(0.50, 0.50, 0.08, 0, 360, 16)],
    "split_moon": [arc(0.50, 0.50, 0.30, 70, 290), arc(0.62, 0.50, 0.24, 110, 250), [(0.62, 0.20), (0.40, 0.82)]],
    "paper_crane_step": [[(0.18, 0.62), (0.50, 0.30), (0.82, 0.62)], [(0.50, 0.30), (0.50, 0.80)], [(0.34, 0.80), (0.66, 0.80)]],
    "ink_storm": [[(0.58, 0.14), (0.36, 0.50), (0.60, 0.50), (0.40, 0.86)], wave(0.18, 0.40, 0.30, 0.04), wave(0.60, 0.82, 0.70, 0.04)],
    "lotus_breath": [[(0.50, 0.24), (0.38, 0.52), (0.50, 0.70), (0.62, 0.52), (0.50, 0.24)], [(0.22, 0.44), (0.30, 0.66), (0.50, 0.70)], [(0.78, 0.44), (0.70, 0.66), (0.50, 0.70)], [(0.30, 0.82), (0.70, 0.82)]],
    "scholars_brush": [[(0.30, 0.82), (0.66, 0.24)], [(0.66, 0.24), (0.74, 0.18)], [(0.26, 0.86), (0.20, 0.78), (0.30, 0.82)], [(0.48, 0.70), (0.76, 0.70)]],
    "kindling_seal": [[(0.50, 0.16), (0.30, 0.52), (0.40, 0.80), (0.50, 0.62), (0.60, 0.80), (0.70, 0.52), (0.50, 0.16)], [(0.24, 0.86), (0.76, 0.86)]],
    "tide_seal": [wave(0.16, 0.84, 0.36, 0.07), wave(0.16, 0.84, 0.56, 0.07), wave(0.24, 0.76, 0.76, 0.05)],
    "bedrock_seal": [[(0.14, 0.78), (0.38, 0.34), (0.52, 0.56), (0.64, 0.42), (0.86, 0.78), (0.14, 0.78)], [(0.30, 0.86), (0.70, 0.86)]],
    "leech_fang": [[(0.32, 0.20), (0.44, 0.56), (0.36, 0.84)], [(0.68, 0.20), (0.56, 0.56), (0.64, 0.84)], arc(0.50, 0.52, 0.06, 0, 360, 12)],
    "twin_current": [wave(0.16, 0.84, 0.38, 0.08, 1.5), wave(0.16, 0.84, 0.62, -0.08, 1.5), [(0.74, 0.28), (0.86, 0.38), (0.74, 0.48)]],
    "mountain_bell": [[(0.34, 0.30), (0.30, 0.66), (0.20, 0.74), (0.80, 0.74), (0.70, 0.66), (0.66, 0.30)], arc(0.50, 0.30, 0.16, 0, 180), arc(0.50, 0.82, 0.06, 0, 360, 12)],
    "phoenix_ash": [[(0.50, 0.84), (0.50, 0.40)], [(0.50, 0.52), (0.20, 0.30), (0.30, 0.56), (0.50, 0.60)], [(0.50, 0.52), (0.80, 0.30), (0.70, 0.56), (0.50, 0.60)], arc(0.50, 0.32, 0.08, 0, 360, 12)],
    "heart_of_the_realm": [[(0.50, 0.14), (0.78, 0.50), (0.50, 0.86), (0.22, 0.50), (0.50, 0.14)], [(0.50, 0.30), (0.62, 0.50), (0.50, 0.70), (0.38, 0.50), (0.50, 0.30)], [(0.10, 0.50), (0.22, 0.50)], [(0.78, 0.50), (0.90, 0.50)]],
    "thunder_script": [[(0.60, 0.12), (0.34, 0.52), (0.56, 0.50), (0.40, 0.88)], [(0.22, 0.24), (0.38, 0.24)], [(0.66, 0.76), (0.82, 0.76)]],
}

# name: (rarity, glyph colour)
RUNES = {
    "ember_fang": ("Common", BLADE),
    "hummingbird_sigil": ("Common", BLADE),
    "oak_heart": ("Common", SHELL),
    "iron_bark": ("Common", SHELL),
    "still_pond": ("Common", SHELL),
    "dawn_coal": ("Common", SPIRIT),
    "coin_lantern": ("Common", FORTUNE),
    "hawk_eye": ("Rare", BLADE),
    "split_moon": ("Rare", BLADE),
    "paper_crane_step": ("Rare", SHELL),
    "ink_storm": ("Rare", SPIRIT),
    "lotus_breath": ("Rare", SPIRIT),
    "scholars_brush": ("Rare", FORTUNE),
    "kindling_seal": ("Rare", FIRE),
    "tide_seal": ("Rare", WATER),
    "bedrock_seal": ("Rare", EARTH),
    "leech_fang": ("Epic", SPIRIT),
    "twin_current": ("Epic", BLADE),
    "mountain_bell": ("Epic", FORTUNE),
    "phoenix_ash": ("Legendary", (255, 140, 51)),
    "heart_of_the_realm": ("Legendary", (255, 237, 178)),
    "thunder_script": ("Legendary", (115, 230, 255)),
}


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def stone_outline(rng):
    """A standing stone: narrower at the top, flat base, a little uneven."""
    s = SIZE * SCALE
    pts = [
        (0.30, 0.90), (0.20, 0.62), (0.24, 0.30), (0.40, 0.10), (0.60, 0.09), (0.76, 0.28), (0.80, 0.62), (0.70, 0.90),
    ]
    out = []
    for x, y in pts:
        out.append(((x + rng.uniform(-0.025, 0.025)) * s, (y + rng.uniform(-0.02, 0.02)) * s))
    return out


def draw_stone(img, outline, rng, rarity_color, legendary):
    s = SIZE * SCALE
    draw = ImageDraw.Draw(img)
    # Facets: fan triangles from an off-centre point, shaded by a light from the upper left.
    cx, cy = s * rng.uniform(0.44, 0.52), s * rng.uniform(0.40, 0.48)
    base = (58, 63, 74)
    n = len(outline)
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        mx, my = (a[0] + b[0]) / 2 - cx, (a[1] + b[1]) / 2 - cy
        length = math.hypot(mx, my) or 1
        light = (-mx / length * 0.7 - my / length * 0.7)  # -1..1, upper left is bright
        shade = 0.85 + 0.42 * light + rng.uniform(-0.05, 0.05)
        color = tuple(max(0, min(255, int(c * shade))) for c in base)
        draw.polygon([a, b, (cx, cy)], fill=color + (255,))
    # Rim in the rarity colour, and a dark ground shadow.
    width = int(SCALE * (7 if legendary else 5))
    draw.line(outline + [outline[0]], fill=rarity_color + (255,), width=width, joint="curve")


def draw_glyph(size, strokes, color, box):
    """Strokes drawn into their own layer: a wide soft glow, then the carved line."""
    x0, y0, x1, y1 = box
    def p(pt):
        return (x0 + pt[0] * (x1 - x0), y0 + pt[1] * (y1 - y0))

    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for stroke in strokes:
        gd.line([p(q) for q in stroke], fill=color + (200,), width=int(SCALE * 16), joint="curve")
    glow = glow.filter(ImageFilter.GaussianBlur(SCALE * 7))

    line = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ld = ImageDraw.Draw(line)
    core = mix(color, (255, 255, 255), 0.45)
    for stroke in strokes:
        pts = [p(q) for q in stroke]
        ld.line(pts, fill=color + (255,), width=int(SCALE * 9), joint="curve")
        ld.line(pts, fill=core + (255,), width=int(SCALE * 4), joint="curve")
        for q in (pts[0], pts[-1]):
            r = SCALE * 4.5
            ld.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=color + (255,))
    return glow, line


def make_icon(name, rarity, color):
    rng = random.Random(name)
    s = SIZE * SCALE
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # A soft shadow under the stone.
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse([s * 0.22, s * 0.86, s * 0.78, s * 0.97], fill=(0, 0, 0, 120))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(SCALE * 6)))

    outline = stone_outline(rng)
    legendary = rarity == "Legendary"
    if legendary:
        halo = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(halo).polygon(outline, fill=RARITY[rarity] + (150,))
        img.alpha_composite(halo.filter(ImageFilter.GaussianBlur(SCALE * 10)))

    draw_stone(img, outline, rng, RARITY[rarity], legendary)

    glow, line = draw_glyph(s, GLYPHS[name], color, (s * 0.25, s * 0.20, s * 0.75, s * 0.80))
    # Keep the glow on the stone so the icon edge stays clean.
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).polygon(outline, fill=255)
    glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", (s, s), 0), mask))
    img.alpha_composite(glow)
    img.alpha_composite(line)

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    only = set(sys.argv[1:])
    missing = set(RUNES) - set(GLYPHS)
    if missing:
        raise SystemExit(f"No glyph for: {', '.join(sorted(missing))}")
    for name, (rarity, color) in RUNES.items():
        if only and name not in only:
            continue
        path = os.path.join(OUT, name + ".png")
        make_icon(name, rarity, color).save(path, optimize=True)
        print("wrote", os.path.relpath(path))


if __name__ == "__main__":
    main()
