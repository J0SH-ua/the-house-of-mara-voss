#!/usr/bin/env python3
"""
Hedge-maze art pipeline for The House of Mara Voss.

The hedge maze (scripts/world/room6/HedgeMaze.cs) is a first-person
raycaster that Mara wakes up inside: box-hedge walls, a night sky, a
grass floor and stone pillars. Until now every one of those surfaces
was a flat colour plus a little code-drawn detail, which read as
"coloured rectangles", not as the garden the rest of the game draws.

This script turns the painted references in assets/images/hedge/source/
into the pixel-art tiles the maze actually projects:

  * hand_flashlight.png  -> the separate "hands holding a torch" sprite
                             (assets/images/core/, next to hand_palm.png)
  * hedge_wall.png       -> the hedge wall face, mirrored into a seamless tile
  * night_sky.png        -> the sky above the hedge tops
  * grass_ground.png     -> the grass floor she walks on
  * stone_tiles.png      -> the stone pillars and the dry fountain

Everything is chunked to the same block size and palette-quantised so
the maze, the torch in her hand and the painted rooms of the house all
sit in the same art style.

Run it from the repository root:
    python3 tools/build_hedge_art.py
Needs Pillow (pip install pillow).
"""

import os
import sys

try:
    from PIL import Image, ImageChops
except ImportError:  # pragma: no cover - convenience for contributors
    print("This script needs Pillow: pip install pillow", file=sys.stderr)
    raise

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE_DIR = os.path.join(ROOT, "assets", "images", "hedge", "source")
HEDGE_DIR = os.path.join(ROOT, "assets", "images", "hedge")
CORE_DIR = os.path.join(ROOT, "assets", "images", "core")

# Block size of the pixel art, in source pixels. Everything is chunked
# to the same grid so a hedge leaf and a torch ember are the same size.
BLOCK = 4

SOURCES = {
    "hands": "hands_flashlight.webp",
    "hedge": "hedge.webp",
    "sky": "night_sky.webp",
    "grass": "grass_ground.webp",
    "stone": "stone_tiles.webp",
}


def load(name):
    path = os.path.join(SOURCE_DIR, SOURCES[name])
    if not os.path.exists(path):
        raise SystemExit(f"missing source image: {path}")
    return Image.open(path).convert("RGBA")


def pixelate(im, block=BLOCK, colors=0, dither=None):
    """Chunk an image down to `block`-sized pixels and quantise it."""
    w, h = im.size
    small = im.resize((max(1, w // block), max(1, h // block)), Image.BOX)
    if colors:
        if dither is None:
            dither = Image.Dither.NONE
        method = Image.Quantize.FASTOCTREE if small.mode == "RGBA" else Image.Quantize.MEDIANCUT
        small = small.quantize(colors=colors, method=method, dither=dither).convert("RGBA")
    return small


def mirror_tile(im):
    """Make an image tile seamlessly by mirroring it on both axes.

    The maze projects these tiles over and over, so a hard seam would
    read as a grid line across the grass or along a hedge. A mirrored
    2x2 tile has no seam at all and the eye stops noticing the repeat.
    """
    w, h = im.size
    out = Image.new("RGBA", (w * 2, h * 2))
    out.paste(im, (0, 0))
    out.paste(im.transpose(Image.FLIP_LEFT_RIGHT), (w, 0))
    out.paste(im.transpose(Image.FLIP_TOP_BOTTOM), (0, h))
    out.paste(im.transpose(Image.Transpose.FLIP_LEFT_RIGHT).transpose(Image.FLIP_TOP_BOTTOM), (w, h))
    return out


def trim_light_bands(im, threshold=225):
    """Strip the studio-white bands the painter leaves on the canvas edge."""
    w, h = im.size
    px = im.load()
    top = 0
    while top < h:
        row = [px[x, top] for x in range(0, w, 32)]
        if sum(1 for c in row if min(c[:3]) >= threshold) >= len(row) * 0.75:
            top += 1
        else:
            break
    bottom = h
    while bottom > top:
        row = [px[x, bottom - 1] for x in range(0, w, 32)]
        if sum(1 for c in row if min(c[:3]) >= threshold) >= len(row) * 0.75:
            bottom -= 1
        else:
            break
    if top or bottom != h:
        print(f"  trimmed light band {top}..{bottom}")
    return im.crop((0, top, w, bottom))


def chroma_key_magenta(im):
    """Cut the magenta studio background out of the torch sprite.

    The painted background is a magenta *gradient* (hot pink in the
    middle, dusty pink at the edges), so the key is measured as
    "how much more red and blue than green" instead of a distance to
    one flat colour. Anything that still smells of magenta after the
    cut is dropped as well, which keeps the black outline clean.
    """
    px = im.load()
    w, h = im.size
    out = im.copy()
    op = out.load()

    def magenta_amount(c):
        r, g, b = c[:3]
        return min(r, b) - g

    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            amount = magenta_amount((r, g, b))
            if amount >= 70:
                op[x, y] = (0, 0, 0, 0)
            elif amount >= 26:
                # feather the rim so the outline does not fringe pink
                t = (amount - 26) / 44.0
                op[x, y] = (r, g, b, int(a * (1.0 - t)))

    # second pass: erase the half-transparent magenta rim outright and
    # let the black outline of the art carry the shape
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = op[x, y]
            if a == 0:
                continue
            if magenta_amount((r, g, b)) >= 20:
                op[x, y] = (0, 0, 0, 0)

    return out


def measure_anchors(im):
    """Where the torch's lens and Mara's grip sit inside the sprite."""
    px = im.load()
    w, h = im.size
    lens_x = lens_y = lens_n = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                continue
            # the ember: the one strongly saturated orange in the art,
            # not the warm skin or the copper band around the head.
            if r > 170 and g < 175 and b < 100 and (r - b) > 110:
                lens_x += x
                lens_y += y
                lens_n += 1
    if lens_n:
        lens = (lens_x / lens_n, lens_y / lens_n)
    else:
        lens = (w * 0.78, h * 0.13)

    # the grip: the middle of her hands, which is where the torch is
    # actually held and therefore where the sprite turns.
    grip_x = grip_y = grip_n = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                continue
            if r > 200 and 140 < g < 210 and 110 < b < 190 and (r - g) > 35 and (g - b) > 25:
                grip_x += x
                grip_y += y
                grip_n += 1
    if grip_n:
        grip = (grip_x / grip_n, grip_y / grip_n)
    else:
        grip = (w * 0.6, h * 0.6)
    return lens, grip


def save(im, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path, optimize=True)
    print(f"wrote {os.path.relpath(path, ROOT)} {im.size}")


def main():
    # ---------------------------------------------------- the torch in hand
    hands = load("hands")
    hands = chroma_key_magenta(hands)
    hands = hands.crop(hands.getbbox())
    hands = pixelate(hands, block=4, colors=32)
    save(hands, os.path.join(CORE_DIR, "hand_flashlight.png"))

    lens, grip = measure_anchors(hands)
    print(f"  torch lens at {lens[0]:.1f},{lens[1]:.1f}  grip at {grip[0]:.1f},{grip[1]:.1f}"
          f"  (sprite {hands.size[0]}x{hands.size[1]})")
    print(f"  lens fraction {lens[0] / hands.size[0]:.4f},{lens[1] / hands.size[1]:.4f}")
    print(f"  grip fraction {grip[0] / hands.size[0]:.4f},{grip[1] / hands.size[1]:.4f}")

    # ---------------------------------------------------------- the maze
    hedge = trim_light_bands(load("hedge"))
    hedge = pixelate(hedge, block=BLOCK, colors=36)
    # the painting is a wall of bushes over a band of roots and grass;
    # the wall face uses the bushes, the roots stay for the base trim
    bush_band = hedge.crop((0, 0, hedge.size[0], hedge.size[1] - 18))
    save(mirror_tile(bush_band), os.path.join(HEDGE_DIR, "hedge_wall.png"))
    save(hedge.crop((0, hedge.size[1] - 18, hedge.size[0], hedge.size[1])),
         os.path.join(HEDGE_DIR, "hedge_base.png"))

    sky = pixelate(load("sky"), block=BLOCK, colors=48, dither=Image.Dither.FLOYDSTEINBERG)
    save(mirror_tile(sky), os.path.join(HEDGE_DIR, "night_sky.png"))

    grass = pixelate(load("grass"), block=BLOCK, colors=40)
    save(mirror_tile(grass), os.path.join(HEDGE_DIR, "grass_ground.png"))

    stone = pixelate(load("stone"), block=BLOCK, colors=40)
    save(mirror_tile(stone), os.path.join(HEDGE_DIR, "stone_tiles.png"))


if __name__ == "__main__":
    main()
