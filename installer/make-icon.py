"""Draws the app icon (src/LmuCareer.App/app.ico): a dark badge with the red corner stripe and
"FS" (Factory Seat) in Barlow Condensed, matching the app's own look. Original artwork, nothing from the game.

    python installer/make-icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT = ROOT / "src/LmuCareer.App/wwwroot/fonts/BarlowCondensed-Bold.ttf"
OUT = ROOT / "src/LmuCareer.App/app.ico"

SIZE = 1024
NAVY = (11, 15, 26, 255)
RED = (227, 25, 47, 255)
DARK_RED = (227, 25, 47, 140)
WHITE = (242, 244, 248, 255)


def draw() -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=200, fill=255)

    badge = Image.new("RGBA", (SIZE, SIZE), NAVY)
    d = ImageDraw.Draw(badge)
    # Corner stripes, as on the app's backdrop.
    d.polygon([(0, 0), (520, 0), (0, 520)], fill=RED)
    d.polygon([(600, 0), (700, 0), (0, 700), (0, 600)], fill=DARK_RED)
    # The lettering.
    font = ImageFont.truetype(str(FONT), 640)
    d.text((SIZE / 2 + 40, SIZE / 2 + 70), "FS", font=font, fill=WHITE, anchor="mm")

    img.paste(badge, (0, 0), mask)
    return img


if __name__ == "__main__":
    big = draw()
    sizes = [16, 24, 32, 48, 64, 128, 256]
    big.resize((256, 256), Image.LANCZOS).save(OUT, sizes=[(s, s) for s in sizes])
    print(f"wrote {OUT}")
