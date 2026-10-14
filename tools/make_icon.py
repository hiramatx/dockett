"""Generates src/Dockett/Assets/dockett.ico and dockett.png: a tilted folder on a blue tile.

Run from the repo root: python tools/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

SS = 4  # supersampling factor
SIZE = 256
BLUE = (31, 111, 235, 255)
WHITE = (255, 255, 255, 255)
TILT_DEGREES = 14  # counterclockwise

out_dir = Path(__file__).resolve().parent.parent / "src" / "Dockett" / "Assets"


def folder_layer(size: int) -> Image.Image:
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    s = size / 24  # draw on a 24-unit grid
    stroke = 1.9 * s
    r = 1.6 * s

    # Back of folder with tab, then the open front body.
    left, right, top, bottom = 3 * s, 21 * s, 5 * s, 19 * s
    tab_right, tab_drop = 10 * s, 7 * s
    d.rounded_rectangle((left, tab_drop, right, bottom), radius=r, outline=WHITE, width=round(stroke))
    d.polygon(
        [(left, tab_drop + stroke), (left, top + r), (left + r, top), (tab_right - s, top),
         (tab_right + s, tab_drop), (tab_right + s, tab_drop + stroke), (left, tab_drop + stroke)],
        fill=WHITE,
    )
    d.rectangle((left, tab_drop + 2.6 * s, right, tab_drop + 2.6 * s + stroke * 0.8), fill=WHITE)
    return layer


def render(size: int) -> Image.Image:
    big = size * SS
    tile = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(tile).rounded_rectangle((0, 0, big - 1, big - 1), radius=big * 0.22, fill=BLUE)

    folder = folder_layer(int(big * 0.78)).rotate(TILT_DEGREES, resample=Image.BICUBIC, expand=False)
    offset = (big - folder.width) // 2
    tile.alpha_composite(folder, (offset, offset))
    return tile.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    out_dir.mkdir(parents=True, exist_ok=True)
    master = render(SIZE)
    master.save(out_dir / "dockett.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [render(s) for s in sizes]
    frames[-1].save(out_dir / "dockett.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    print(f"Wrote {out_dir / 'dockett.ico'}")
