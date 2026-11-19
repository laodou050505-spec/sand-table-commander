"""深空风格的地表贴图（11-13，样图 C：小行星战场）。
来源：Tools/space_ground_gen.py 出的俯拍沙盘地表照片 ArtSource/Space/ground/<名>_src.jpg → 做成可平铺 → Assets/_WarTable/Art/Textures/
  space_regolith.png  炭灰小行星风化层（带小撞击坑），替换“青草”层
  space_ore.png       锈橙铁矿砂，替换“干草”层
  space_deck.png      钢甲板路（防滑纹钢板），替换“土路”层
做法同 make_modern_ground.py（去大尺度明暗 → 平移半幅 + 羽化盖缝）。地面要比棋子暗：风化层压到平均亮度 ~95 / 255，白搪瓷甲和焦红甲都衬得出来，也不过曝。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_space_ground.py"""
import numpy as np
from PIL import Image, ImageFilter

R = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = R + 'ArtSource/Space/ground/'; OUT = R + 'Assets/_WarTable/Art/Textures/'
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


def to_mean(a, target):
    """把平均亮度调到 target（0–1）。"""
    return np.clip(a * (target / max(a.mean(), 1e-3)), 0, 1)


save(to_mean(grade(tileable(flatten(load('regolith'), 120)), 0.7, 1.0, (0.98, 0.99, 1.03)), 0.37), 'space_regolith')
save(to_mean(grade(tileable(flatten(load('ore'), 110)), 0.5, 1.0), 0.33), 'space_ore')   # 原图橙得刺眼：饱和度减半、压暗，只当风化层里的锈色矿砂斑
# 甲板是规则网格：不能用“平移半幅 + 羽化”（网格会错位糊掉）→ 只去明暗，靠素材本身的网格平铺（四边裁到整格由素材决定，接缝处是板缝，看不出）
save(to_mean(grade(flatten(load('deck'), 160), 0.6, 1.0, (0.98, 0.99, 1.02)), 0.36), 'space_deck')
