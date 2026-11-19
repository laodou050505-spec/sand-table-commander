"""应用图标（11-19 晚 用户：“图标也要更新一下”）：和现在的封面一套——重甲骑士半身像（面罩青光）+ 身后金线日轮 + 深藏青底。
原来的图标是旧封面的双面战盔 + 中式帅旗，已经不对了。

主角半身像来自 Unity 封面实拍（封面灯光），黑底 / 白底各拍一张，差分抠图：
  ArtSource/Icon/hero_bust_k.png、hero_bust_w.png（在 Unity 里把封面上除主角以外的渲染器都关掉，WTCapture.Shot 1600×1600，见交接文档）
日轮：ArtSource/Cover/v6/alpha/fx_halo.png（封面同一张）。

输出 Assets/_WarTable/Art/Icon/（WTBuild.SetIcon 读）：
  icon_1024.png  方形完整图标（带细黄铜边）
  icon_round.png 圆形图标（画面缩到 86%）
  icon_bg.png / icon_fg.png  安卓自适应图标的背景 / 前景两层（前景缩进中间 66% 的安全区）
用法：python3 Tools/make_app_icon.py [预览.jpg]"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

R = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = R + 'ArtSource/Icon/'
OUT = R + 'Assets/_WarTable/Art/Icon/'
N = 1024


def cutout():
    k = np.asarray(Image.open(SRC + 'hero_bust_k.png').convert('RGB'), np.float32) / 255
    w = np.asarray(Image.open(SRC + 'hero_bust_w.png').convert('RGB'), np.float32) / 255
    a = np.clip(1 - (w - k).mean(-1), 0, 1)                                       # 黑底白底之差 = 1 − 不透明度
    rgb = np.where(a[..., None] > 1e-3, k / np.maximum(a[..., None], 1e-3), 0)
    return Image.fromarray((np.dstack([np.clip(rgb, 0, 1), a]) * 255 + 0.5).astype(np.uint8))


def background(n):
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) / n
    r = np.hypot(xx - 0.5, (yy - 0.40) * 1.1)
    base = np.array([8, 11, 22], np.float32) + (np.array([26, 34, 58], np.float32) - np.array([8, 11, 22], np.float32)) * np.clip(1 - r / 0.75, 0, 1)[..., None] ** 1.4
    warm = np.clip(1 - np.hypot(xx - 0.5, yy - 0.36) / 0.36, 0, 1) ** 2.2                   # 头后一团暖光（封面的 BackGlow）
    base = base + warm[..., None] * np.array([120, 70, 30], np.float32) * 0.55
    base += np.random.default_rng(7).normal(0, 2.2, (n, n, 1))                             # 细颗粒，免得渐变出色带
    return Image.fromarray(np.clip(base, 0, 255).astype(np.uint8)).convert('RGBA')


def halo(n, size, cy, alpha):
    h = Image.open(R + 'ArtSource/Cover/v6/alpha/fx_halo.png').convert('RGBA').resize((size, size), Image.LANCZOS)
    a = np.asarray(h, np.float32); a[..., 3] *= alpha; h = Image.fromarray(a.astype(np.uint8))
    lay = Image.new('RGBA', (n, n), (0, 0, 0, 0)); lay.alpha_composite(h, ((n - size) // 2, int(cy - size / 2)))
    return lay


def hero_layer(n, scale, cy_top):
    """半身像：宽占画面 scale，头盔顶在 cy_top 处，下边出血。"""
    c = cutout(); a = np.asarray(c)[..., 3]
    ys, xs = np.where(a > 20); c = c.crop((xs.min(), ys.min(), xs.max() + 1, c.height))
    k = n * scale / c.width; c = c.resize((int(c.width * k), int(c.height * k)), Image.LANCZOS)
    lay = Image.new('RGBA', (n, n), (0, 0, 0, 0)); lay.alpha_composite(c, ((n - c.width) // 2, int(cy_top)))
    # 脚下压暗一点：半身像下沿融进底色，不是一刀切
    g = np.asarray(lay, np.float32); yy = np.arange(n, dtype=np.float32)[:, None] / n
    g[..., :3] *= np.clip(1 - (yy - 0.80) / 0.25, 0.35, 1)[..., None]
    return Image.fromarray(g.astype(np.uint8))


def separate(n, halo_lay, hero):
    """11-19 晚 用户：“图标现在头部和背景粘在一起了，需要分开一点”——头盔正落在日轮放射线的中心，金线贴着头盔边缘穿过去。
    → 人物轮廓外留一圈空隙（约 2% 画面宽），日轮的线在这圈里断开；再垫一层很淡的暗影把人物托出来。返回（处理过的日轮层，暗影层）。"""
    from scipy import ndimage as ndi
    a = np.asarray(hero, np.float32)[..., 3] / 255
    gap = ndi.gaussian_filter(ndi.maximum_filter(a, size=int(n * 0.045)), n * 0.008)          # 轮廓外扩约 2.2%、边缘柔和
    h = np.asarray(halo_lay, np.float32).copy(); h[..., 3] *= np.clip(1 - gap * 1.3, 0, 1)
    shade = ndi.gaussian_filter(ndi.maximum_filter(a, size=int(n * 0.03)), n * 0.018)
    sh = np.zeros((n, n, 4), np.float32); sh[..., :3] = (4, 6, 12); sh[..., 3] = np.clip(shade, 0, 1) * 255 * 0.55
    return Image.fromarray(h.astype(np.uint8)), Image.fromarray(sh.astype(np.uint8))


def compose(n, s):
    """s = 画面缩放（1 = 满幅，圆形 / 自适应前景用小一点）。"""
    img = background(n)
    hero = hero_layer(n, 0.74 * s, n * (0.5 - (0.5 - 0.17) * s))   # 0.86 / 0.10 时人物塞满、日轮几乎被挡完
    hl, sh = separate(n, halo(n, int(n * 0.98 * s), n * (0.5 - (0.5 - 0.40) * s), 0.55), hero)
    img.alpha_composite(hl); img.alpha_composite(sh); img.alpha_composite(hero)
    return img


def border(img):
    d = ImageDraw.Draw(img); n = img.width; m = int(n * 0.035)
    d.rounded_rectangle([m, m, n - m - 1, n - m - 1], radius=int(n * 0.16), outline=(196, 158, 92, 230), width=max(2, n // 340))
    return img


icon = border(compose(N, 1.0)).convert('RGB'); icon.save(OUT + 'icon_1024.png')
round_ = compose(N, 0.86)
mask = Image.new('L', (N, N), 0); ImageDraw.Draw(mask).ellipse([0, 0, N - 1, N - 1], fill=255)
round_.putalpha(mask); round_.save(OUT + 'icon_round.png')
background(N).save(OUT + 'icon_bg.png')
fg = Image.new('RGBA', (N, N), (0, 0, 0, 0)); fhero = hero_layer(N, 0.5, N * 0.28); fhl, fsh = separate(N, halo(N, int(N * 0.62), N * 0.45, 0.6), fhero)
fg.alpha_composite(fhl); fg.alpha_composite(fsh); fg.alpha_composite(fhero)
fa = np.asarray(fg, np.float32); yy = np.arange(N, dtype=np.float32)[:, None] / N; fa[..., 3] *= np.clip((0.86 - yy) / 0.08, 0, 1)   # 前景下沿渐隐（自适应图标的遮罩会切边）
Image.fromarray(fa.astype(np.uint8)).save(OUT + 'icon_fg.png')
print('→', OUT)
if len(sys.argv) > 1:
    ims = [Image.open(OUT + f).convert('RGBA').resize((400, 400), Image.LANCZOS) for f in ('icon_1024.png', 'icon_round.png')]
    ad = background(N); ad.alpha_composite(Image.open(OUT + 'icon_fg.png')); am = Image.new('L', (N, N), 0); ImageDraw.Draw(am).rounded_rectangle([N * 0.17, N * 0.17, N * 0.83, N * 0.83], radius=int(N * 0.12), fill=255)
    ad.putalpha(am); ims.append(ad.crop((int(N * 0.17), int(N * 0.17), int(N * 0.83), int(N * 0.83))).resize((400, 400), Image.LANCZOS))
    small = Image.open(OUT + 'icon_1024.png').convert('RGBA').resize((96, 96), Image.LANCZOS)
    o = Image.new('RGBA', (1330, 420), (70, 70, 74, 255)); [o.alpha_composite(im, (10 + k * 410, 10)) for k, im in enumerate(ims)]; o.alpha_composite(small, (1232, 10))
    o.convert('RGB').save(sys.argv[1], quality=90); print('预览 →', sys.argv[1])
