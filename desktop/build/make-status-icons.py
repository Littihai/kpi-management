"""Draws the status icons for the system tray and the taskbar overlay into desktop/build/status/.

  ok.png            green circle, white check          nothing is waiting for the person
  attention-1..9    red circle, white number           work is waiting (attention-9plus.png shows "9+")
  connecting.png    amber circle, three white dots     connecting or reconnecting
  offline.png       grey circle, white cross           not connected to the server

The same 32x32 files serve both uses (Windows scales them). The colours match the status dot in the web app.
Needs Pillow and a bold Windows font. Run it from anywhere: python desktop/build/make-status-icons.py
The generated PNGs are committed; run this only when the design changes.
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent / "status"
SIZE = 32
SCALE = 8            # drawn at 8x and shrunk, for crisp anti-aliased edges
BIG = SIZE * SCALE
WHITE = (255, 255, 255, 255)

GREEN = (47, 179, 68, 255)    # --tone-ok-solid
AMBER = (245, 159, 0, 255)    # --tone-warn-solid
RED = (214, 57, 57, 255)      # --tone-stop-solid
GREY = (133, 147, 163, 255)

FONTS = [r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf"]


def font(size):
    for path in FONTS:
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    raise SystemExit("No bold font found; expected one of: " + ", ".join(FONTS))


def disc(color):
    image = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    ImageDraw.Draw(image).ellipse((0, 0, BIG - 1, BIG - 1), fill=color)
    return image


def stroke(draw, points, width):
    """A polyline with round ends and joints."""
    draw.line(points, fill=WHITE, width=width, joint="curve")
    for x, y in (points[0], points[-1]):
        draw.ellipse((x - width / 2, y - width / 2, x + width / 2, y + width / 2), fill=WHITE)


def check():
    image = disc(GREEN)
    draw = ImageDraw.Draw(image)
    stroke(draw, [(BIG * 0.27, BIG * 0.52), (BIG * 0.43, BIG * 0.69), (BIG * 0.75, BIG * 0.33)], round(BIG * 0.115))
    return image


def cross():
    image = disc(GREY)
    draw = ImageDraw.Draw(image)
    width = round(BIG * 0.105)
    stroke(draw, [(BIG * 0.32, BIG * 0.32), (BIG * 0.68, BIG * 0.68)], width)
    stroke(draw, [(BIG * 0.68, BIG * 0.32), (BIG * 0.32, BIG * 0.68)], width)
    return image


def dots():
    image = disc(AMBER)
    draw = ImageDraw.Draw(image)
    radius = BIG * 0.075
    for x in (0.26, 0.5, 0.74):
        draw.ellipse((BIG * x - radius, BIG * 0.5 - radius, BIG * x + radius, BIG * 0.5 + radius), fill=WHITE)
    return image


def number(text):
    image = disc(RED)
    draw = ImageDraw.Draw(image)
    face = font(round(BIG * (0.74 if len(text) == 1 else 0.56)))
    left, top, right, bottom = draw.textbbox((0, 0), text, font=face)
    draw.text((BIG / 2 - (left + right) / 2, BIG / 2 - (top + bottom) / 2), text, font=face, fill=WHITE)
    return image


def save(name, image):
    OUT.mkdir(parents=True, exist_ok=True)
    image.resize((SIZE, SIZE), Image.LANCZOS).save(OUT / f"{name}.png", optimize=True)


save("ok", check())
save("connecting", dots())
save("offline", cross())
for n in range(1, 10):
    save(f"attention-{n}", number(str(n)))
save("attention-9plus", number("9+"))
print("wrote", len(list(OUT.glob("*.png"))), "icons to", OUT)
