"""现代时代的地表贴图（11-13 用户：“注意地面的样子和场景的布置”——第一版现代时代的地面原封不动沿用中古的青草土路）。
来源：Tools/modern_ground_gen.py 出的俯拍沙盘地表照片 ArtSource/Modern/ground/<名>_src.jpg → 做成可平铺 → Assets/_WarTable/Art/Textures/
  mod_steppe.png    夏末干草原（橄榄绿夹枯黄、露出沙土），替换“青草”层
  mod_camp.png      踩实的干土（营地空地），替换“干草”层
  mod_gravel.png    压实的碎石路（带车辙印），替换“土路”层
可平铺的做法同 make_future_ground.py（平移半幅 + 羽化盖缝）。碎石路素材里有两个黑坑洞（看着像窟窿）→ 用旁边的碎石盖掉。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_modern_ground.py"""
import numpy as np
from PIL import Image, ImageFilter

R = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = R + 'ArtSource/Modern/ground/'; OUT = R + 'Assets/_WarTable/Art/Textures/'
N = 1024


def tileable(a, feather=0.18):
    n = a.shape[0]; h = n // 2
    sh = np.roll(np.roll(a, h, 0), h, 1)
    yy, xx = np.mgrid[0:n, 0:n]
    d = np.minimum(np.abs(xx - h), np.abs(yy - h)) / (n * feather)
    w = np.clip(1 - d, 0, 1) ** 2
    w = np.asarray(Image.fromarray((w * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6))).astype(np.float64) / 255
    patch = np.roll(np.roll(a, n // 3, 0), n // 5, 1)[::-1, ::-1]
    return sh * (1 - w[..., None]) + patch * w[..., None]


def load(name, n=N):
    return np.asarray(Image.open(SRC + name + '_src.jpg').convert('RGB').resize((n, n), Image.LANCZOS)).astype(np.float64) / 255


def blur(m, r):
    return np.asarray(Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))).astype(np.float64) / 255


def flatten(a, r=90):
    """去掉大尺度的明暗 / 色偏（照片一边亮一边暗，平铺后会看出一格一格）：每个通道除以自己的大半径模糊再乘回平均值。"""
    out = np.empty_like(a)
    for c in range(3):
        low = blur(a[..., c], r); out[..., c] = a[..., c] / np.maximum(low, 1e-3) * a[..., c].mean()
    return np.clip(out, 0, 1)


def fill_dark(a, thr=0.3):
    """把很暗的斑块（坑洞）用错开半幅的同一张图盖掉。"""
    g = a.mean(-1); m = blur((blur(g, 3) < thr).astype(np.float64), 10) > 0.02
    m = blur(m.astype(np.float64), 6)[..., None]
    other = np.roll(np.roll(a, a.shape[0] // 2, 0), a.shape[1] // 3, 1)
    print('  暗斑占比', round(float((m > 0.5).mean()), 4))
    return a * (1 - m) + other * m


def grade(a, sat, gain, tint=(1, 1, 1), lift=0.0):
    g = a.mean(-1, keepdims=True)
    return np.clip(((g + (a - g) * sat) * gain + lift) * np.array(tint), 0, 1)


def save(a, name):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT + name + '.png')
    print(name, '平均色', (a.reshape(-1, 3).mean(0) * 255).round().astype(int))


save(grade(tileable(flatten(load('steppe'), 140)), 0.9, 1.0), 'mod_steppe')
save(grade(tileable(flatten(load('camp'))), 0.85, 0.86), 'mod_camp')
g = fill_dark(fill_dark(load('gravel')))
save(grade(tileable(flatten(g, 70), 0.09), 0.9, 0.88, (1.0, 0.985, 0.95)), 'mod_gravel')   # 羽化带窄一些：宽了会看出一条发糊的横带
