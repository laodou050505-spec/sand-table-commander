"""封面主角身上的贴花贴图 → Assets/_WarTable/Art/Models/P2/Hero_Statue/Decals/*.png
11-19 用户：“可以做一些很酷的图案，像纹身一样贴在模型上”“右胸口贴公司标志，不要太大”。
贴到模型上的位置和大小在 Assets/_WarTable/Editor/CoverHeroDecals.cs 的 Specs 表里；这里只管出图。

  logo      公司标志：**只要图标、不带 TRIPO 字**（11-19 用户：“For the logo, just the logo, no text”）。
            ArtSource/Brand/raw/tripo_mark_147.png 原样放进 256×256 的画布——不改色、不做旧、不拉伸、不缩放（品牌规范）；
            底下不垫任何底（用户：“don't keep the background behind it—cut out the logo”）
  crest     翼星长枪团徽（banana_pro 生成，ArtSource/Hero/decals/crest_src.png，白底黑图 → 透明底）
  filigree  彗星羽纹（同上，filigree_src.png）
  num07     编号 07（Outfit 粗体 + 模板喷漆的断口）
  tally     战绩刻痕（手刷的“正”字式五道一组）
  chevron   三道军衔折线
  sun       十二芒日轮（和封面背后的金线日轮呼应）
除标志外都是白色图案 + 透明度，颜色由材质给；做旧（掉漆的小缺口、划痕、边缘毛糙）直接做进透明度。
画布一律是 2 的幂（本工程的编辑器只给 2 的幂贴图做 ASTC 压缩）。用法：python3 Tools/hero_decals.py [预览图.jpg]"""
import sys
import math
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi

R = '/Users/ld/Documents/ChatGPT/WarTable/'
OUT = R + 'Assets/_WarTable/Art/Models/P2/Hero_Statue/Decals/'
SRC = R + 'ArtSource/Hero/decals/'
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1119)


def worn(alpha, chips=0.10, scratches=10, seed=0):
    """做旧：alpha 是 0..1 的数组。掉漆的小缺口（两种大小）+ 几道划痕 + 边缘毛糙。"""
    r = np.random.default_rng(seed)
    h, w = alpha.shape
    big = ndi.gaussian_filter(r.random((h, w)), w / 70)
    small = ndi.gaussian_filter(r.random((h, w)), w / 300)
    keep = (big > np.quantile(big, chips)) & (small > np.quantile(small, chips * 0.6))
    line = Image.new('L', (w, h), 255)
    d = ImageDraw.Draw(line)
    for _ in range(scratches):
        x, y, a, n = r.random() * w, r.random() * h, r.random() * math.pi, (0.15 + 0.35 * r.random()) * w
        d.line([(x, y), (x + math.cos(a) * n, y + math.sin(a) * n)], fill=0, width=int(1 + r.integers(0, max(2, w // 300))))
    rough = ndi.gaussian_filter(r.random((h, w)), 1.2) - 0.5                 # 边缘毛糙：把透明度的门槛抖一抖
    a = np.clip((alpha + rough * 0.16 - 0.5) * 3 + 0.5, 0, 1)            # 第一版 0.5：刻痕的笔画糊成一团
    a = a * ndi.gaussian_filter(keep.astype(np.float64), 0.8) * ndi.gaussian_filter(np.asarray(line, np.float64) / 255, 0.6)
    return a * (0.86 + 0.14 * ndi.gaussian_filter(r.random((h, w)), w / 40) / 0.5).clip(0, 1)   # 漆有厚有薄


def save(name, alpha, rgb=None):
    """白色图案 + 透明度；rgb 不给就是纯白。四周留 2 像素全透明（贴图是夹边取样，边上不能有东西）。"""
    a = np.clip(alpha, 0, 1).copy()
    a[:2] = a[-2:] = 0
    a[:, :2] = a[:, -2:] = 0
    h, w = a.shape
    px = np.zeros((h, w, 4), np.uint8)
    px[..., :3] = 255 if rgb is None else rgb
    px[..., 3] = (a * 255 + 0.5).astype(np.uint8)
    Image.fromarray(px).save(OUT + name + '.png')
    print(f'{name:9} {w}x{h}  覆盖 {a.mean() * 100:4.1f}%')
    return px


def from_art(file, size, fit=0.90):
    """生成图（黑底白图）→ 居中放进 size×size 的画布，亮度当透明度。"""
    im = Image.open(SRC + file).convert('L')
    a = np.asarray(im, np.float64) / 255
    ys, xs = np.where(a > 0.35)
    im = im.crop((xs.min() - 8, ys.min() - 8, xs.max() + 9, ys.max() + 9))
    k = size * fit / max(im.size)
    im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
    canvas = Image.new('L', (size, size), 0)
    canvas.paste(im, ((size - im.width) // 2, (size - im.height) // 2))
    a = np.asarray(canvas, np.float64) / 255
    return np.clip((a - 0.30) / 0.40, 0, 1)


def draw(w, h, painter, ss=4):
    """按 4 倍画再缩小（抗锯齿）。painter(d, W, H) 用白色在黑底上画。"""
    im = Image.new('L', (w * ss, h * ss), 0)
    painter(ImageDraw.Draw(im), w * ss, h * ss)
    return np.asarray(im.resize((w, h), Image.LANCZOS), np.float64) / 255


out = {}

# ── 公司标志：原样
logo = Image.open(R + 'ArtSource/Brand/raw/tripo_mark_147.png').convert('RGBA')
canvas = Image.new('RGBA', (256, 256), (255, 255, 255, 0))
canvas.alpha_composite(logo, ((256 - logo.width) // 2, (256 - logo.height) // 2))
px = np.asarray(canvas).copy()
solid = px[..., 3] > 0                                                        # 透明处的颜色填成最近的不透明像素，缩小贴图时边上不发灰
idx = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
px[..., :3] = px[..., :3][idx[0], idx[1]]
Image.fromarray(px).save(OUT + 'logo.png')
print(f'logo      256x256  原样（图标本体 {logo.width}x{logo.height}）')
out['logo'] = px

# ── 生成的两张
out['crest'] = save('crest', worn(from_art('crest_src.png', 512), 0.03, 6, 1))
out['filigree'] = save('filigree', worn(from_art('filigree_src.png', 512, 0.94), 0.03, 5, 2))


# ── 编号 07
def num(d, W, H):
    f = ImageFont.truetype(R + 'ArtSource/Fonts/Outfit[wght].ttf', int(H * 0.86))
    f.set_variation_by_axes([900])
    box = d.textbbox((0, 0), '07', font=f)
    x, y = (W - (box[2] - box[0])) / 2 - box[0] - W * 0.04, (H - (box[3] - box[1])) / 2 - box[1]
    d.text((x, y), '07', font=f, fill=255)
    for cx in (0.335, 0.655):                                                 # 模板喷漆的断口：每个数字竖着断一道
        d.rectangle([W * cx - H * 0.016, 0, W * cx + H * 0.016, H], fill=0)
    d.rectangle([W * 0.86, H * 0.16, W * 0.90, H * 0.84], fill=255)           # 右边一条竖杠
    d.rectangle([W * 0.92, H * 0.16, W * 0.935, H * 0.84], fill=255)


out['num07'] = save('num07', worn(draw(512, 256, num), 0.05, 6, 3))


# ── 战绩刻痕：五道一组，手刷的，粗细长短都不齐
def tally(d, W, H):
    r = np.random.default_rng(7)

    def stroke(x0, y0, x1, y1, wd):
        n = 14
        pts = [(x0 + (x1 - x0) * i / n + r.normal(0, wd * 0.10), y0 + (y1 - y0) * i / n + r.normal(0, wd * 0.10)) for i in range(n + 1)]
        for (ax, ay), (bx, by), i in zip(pts, pts[1:], range(n)):
            wv = wd * (0.75 + 0.5 * math.sin(math.pi * (i + 0.5) / n))        # 两头细中间粗，像一笔刷下来
            d.line([(ax, ay), (bx, by)], fill=255, width=max(1, int(wv)))
            d.ellipse([bx - wv / 2, by - wv / 2, bx + wv / 2, by + wv / 2], fill=255)
    x = W * 0.075
    for group in (5, 5, 3):
        gx = x
        for i in range(min(group, 4)):
            stroke(x + r.normal(0, W * 0.004), H * (0.20 + r.normal(0, 0.02)), x + r.normal(0, W * 0.006), H * (0.80 + r.normal(0, 0.02)), H * 0.048)
            x += W * 0.068
        if group == 5:
            stroke(gx - W * 0.035, H * 0.68, x - W * 0.030, H * 0.32, H * 0.045)
        x += W * 0.055


out['tally'] = save('tally', worn(draw(512, 256, tally), 0.04, 4, 4))


# ── 三道军衔折线
def chevron(d, W, H):
    for i in range(3):
        y = H * (0.20 + 0.215 * i)
        t = H * 0.105
        d.polygon([(W * 0.16, y + H * 0.20), (W * 0.5, y), (W * 0.84, y + H * 0.20), (W * 0.84, y + H * 0.20 + t), (W * 0.5, y + t), (W * 0.16, y + H * 0.20 + t)], fill=255)


out['chevron'] = save('chevron', worn(draw(256, 256, chevron), 0.05, 4, 5))


# ── 十二芒日轮
def sun(d, W, H):
    c = W / 2
    for i in range(12):
        a = i * math.pi / 6
        r0, r1, hw = W * 0.215, W * (0.46 if i % 2 == 0 else 0.37), 0.115
        d.polygon([(c + r0 * math.cos(a - hw), c + r0 * math.sin(a - hw)), (c + r1 * math.cos(a), c + r1 * math.sin(a)), (c + r0 * math.cos(a + hw), c + r0 * math.sin(a + hw))], fill=255)
    d.ellipse([c - W * 0.175, c - W * 0.175, c + W * 0.175, c + W * 0.175], outline=255, width=int(W * 0.028))
    d.ellipse([c - W * 0.085, c - W * 0.085, c + W * 0.085, c + W * 0.085], fill=255)


out['sun'] = save('sun', worn(draw(512, 512, sun), 0.04, 6, 6))

if len(sys.argv) > 1:                                                         # 预览：深钢底上排一排
    cells = []
    for k, px in out.items():
        im = Image.fromarray(px)
        im = im.resize((round(im.width * 300 / im.height), 300), Image.LANCZOS)
        bg = Image.new('RGBA', im.size, (58, 60, 64, 255))
        tint = Image.new('RGBA', im.size, (255, 255, 255, 255) if k == 'logo' else (219, 209, 184, 255))
        bg.alpha_composite(Image.composite(Image.merge('RGBA', (*[c for c in Image.merge('RGB', im.split()[:3]).split()], im.split()[3])) if k == 'logo' else Image.merge('RGBA', (*tint.split()[:3], im.split()[3])), Image.new('RGBA', im.size, (0, 0, 0, 0)), im.split()[3]))
        cells.append(bg.convert('RGB'))
    sheet = Image.new('RGB', (sum(c.width for c in cells) + 8 * (len(cells) - 1), 300), (20, 20, 20))
    x = 0
    for c in cells:
        sheet.paste(c, (x, 0))
        x += c.width + 8
    sheet.save(sys.argv[1], quality=90)
    print('预览 →', sys.argv[1], sheet.size)
