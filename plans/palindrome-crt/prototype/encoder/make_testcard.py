#!/usr/bin/env python3
"""Generate a 768x576 test card (square-pixel 4:3) for the PAL encoder probe."""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

W, H = 768, 576
img = Image.new("RGB", (W, H), (20, 20, 28))
d = ImageDraw.Draw(img)


def font(size):
    for p in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf",
              "/System/Library/Fonts/Helvetica.ttc",
              "/Library/Fonts/Arial.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            pass
    return ImageFont.load_default()


# grid
for x in range(0, W, 48):
    d.line([(x, 0), (x, H)], fill=(90, 90, 90), width=1)
for y in range(0, H, 48):
    d.line([(0, y), (W, y)], fill=(90, 90, 90), width=1)

# 75% EBU colour bars
bars = [(191, 191, 191), (191, 191, 0), (0, 191, 191), (0, 191, 0),
        (191, 0, 191), (191, 0, 0), (0, 0, 191), (0, 0, 0)]
bw = (W - 96) // 8
for i, c in enumerate(bars):
    d.rectangle([48 + i * bw, 48, 48 + (i + 1) * bw - 1, 239], fill=c)

# 100% saturated primaries strip
prim = [(255, 255, 255), (255, 255, 0), (0, 255, 255), (0, 255, 0),
        (255, 0, 255), (255, 0, 0), (0, 0, 255), (0, 0, 0)]
for i, c in enumerate(prim):
    d.rectangle([48 + i * bw, 240, 48 + (i + 1) * bw - 1, 287], fill=c)

# grey ramp + 11-step staircase
ramp = np.linspace(0, 255, W - 96).astype(np.uint8)
ramp_img = Image.fromarray(np.stack([np.tile(ramp, (40, 1))] * 3, axis=-1))
img.paste(ramp_img, (48, 288))
for i in range(11):
    gval = int(round(i * 25.5))
    x0 = 48 + i * (W - 96) // 11
    d.rectangle([x0, 328, 48 + (i + 1) * (W - 96) // 11 - 1, 367], fill=(gval,) * 3)

# multiburst: vertical gratings of increasing frequency (px period 16,8,6,4,3,2)
x = 48
for period in (16, 8, 6, 4, 3, 2):
    wseg = (W - 96) // 6
    xs = np.arange(wseg)
    wave = ((np.sin(2 * np.pi * xs / period) * 0.5 + 0.5) * 255).astype(np.uint8)
    seg = Image.fromarray(np.stack([np.tile(wave, (48, 1))] * 3, axis=-1))
    img.paste(seg, (x, 368))
    x += wseg

# circle for geometry + centre cross
d.ellipse([W // 2 - 250, H // 2 - 250, W // 2 + 250, H // 2 + 250], outline=(255, 255, 255), width=2)
d.line([(W // 2 - 30, H // 2), (W // 2 + 30, H // 2)], fill=(255, 255, 255), width=1)
d.line([(W // 2, H // 2 - 30), (W // 2, H // 2 + 30)], fill=(255, 255, 255), width=1)

# captions
d.rectangle([48, 424, W - 49, 527], fill=(0, 0, 0))
d.text((64, 430), "UNSCIENCE TV", font=font(44), fill=(255, 255, 255))
d.text((64, 484), "KSA  PAL-I 625/50  CH 21   the quick brown fox 0123456789", font=font(20), fill=(255, 220, 60))
d.text((430, 436), "fine text: Kitten Space Agency", font=font(13), fill=(120, 255, 160))
d.text((430, 456), "orbital velocity 7.8 km/s  Ap 412 km", font=font(11), fill=(200, 200, 255))

img.save(sys.argv[1] if len(sys.argv) > 1 else "testcard.png")
print("ok")
