"""把一行字按字距渲染成透明底黑字 PNG（给 Tools/title_contours.py → blender_title3d.py 挤出成立体字）。
python3 Tools/render_text.py <字体.ttf> <输出.png> "<文字>" [字号=220] [字距=0.32 em] [字重=400]
封面英文用 ArtSource/Fonts/Cinzel.ttf（SIL OFL，可随包分发）：经典罗马大写、拉开字距，高级标题的常规排法。"""
import sys
from PIL import Image, ImageDraw, ImageFont

font_path, out, text = sys.argv[1], sys.argv[2], sys.argv[3]
size = int(sys.argv[4]) if len(sys.argv) > 4 else 220
track = float(sys.argv[5]) if len(sys.argv) > 5 else 0.32
weight = int(sys.argv[6]) if len(sys.argv) > 6 else 400
f = ImageFont.truetype(font_path, size)
try:
    f.set_variation_by_axes([weight])
except Exception:
    pass
adv = [f.getlength(c) for c in text]
W = int(sum(adv) + track * size * (len(text) - 1) + size)
H = int(size * 1.6)
im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
x = size / 2
for c, a in zip(text, adv):
    d.text((x, size * 0.3), c, font=f, fill=(0, 0, 0, 255))
    x += a + track * size
im = im.crop(im.getbbox())
pad = Image.new('RGBA', (im.width + 40, im.height + 40), (0, 0, 0, 0))
pad.paste(im, (20, 20))
pad.save(out)
print(out, pad.size)
