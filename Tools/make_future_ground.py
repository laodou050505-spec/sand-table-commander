"""未来时代（样图 A：月面基地）的地表贴图。11-12 用户：“这个地面的效果不太好看”（第一版只是把焦土换色成灰颗粒，路还是土路）。
来源：Tripo seedream_v5 俯拍的沙盘地表照片 ArtSource/Future/ground/<名>_src.jpg → 做成可平铺 → Assets/_WarTable/Art/Textures/
  moon_turf.png    月壤（带小陨石坑），替换“青草”层
  moon_basalt.png  深色玄武岩碎屑，替换“干草”层
  moon_road.png    六边形地砖路面，替换“土路”层
  moon_gravel.png  冷灰碎石（由玄武岩提亮），替换“河床”层
可平铺的做法：把图平移半幅（接缝移到正中），再用同一张图的另一块、按到接缝的距离做羽化盖住接缝（offset + feather）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_future_ground.py"""
import numpy as np
from PIL import Image, ImageFilter

R = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = R + 'ArtSource/Future/ground/'; OUT = R + 'Assets/_WarTable/Art/Textures/'
N = 1024


def tileable(a, feather=0.18):
    n = a.shape[0]; h = n // 2
    sh = np.roll(np.roll(a, h, 0), h, 1)                      # 接缝移到正中的十字上
    yy, xx = np.mgrid[0:n, 0:n]
    d = np.minimum(np.abs(xx - h), np.abs(yy - h)) / (n * feather)   # 离十字接缝的距离
    w = np.clip(1 - d, 0, 1) ** 2
    w = np.asarray(Image.fromarray((w * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6))).astype(np.float64) / 255
    patch = np.roll(np.roll(a, n // 3, 0), n // 5, 1)[::-1, ::-1]   # 同一张图的另一块（翻转一下避免重复感），原图中部本来就没有接缝
    return sh * (1 - w[..., None]) + patch * w[..., None]


def load(name):
    return np.asarray(Image.open(SRC + name + '_src.jpg').convert('RGB').resize((N, N), Image.LANCZOS)).astype(np.float64) / 255


def grade(a, sat, gain, tint=(1, 1, 1), lift=0.0):
    g = a.mean(-1, keepdims=True)
    return np.clip(((g + (a - g) * sat) * gain + lift) * np.array(tint), 0, 1)


def save(a, name):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT + name + '.png')
    print(name, '平均色', (a.reshape(-1, 3).mean(0) * 255).round().astype(int))


save(grade(tileable(load('regolith')), 0.45, 0.78, (0.98, 0.99, 1.03)), 'moon_turf')            # 原图偏蓝偏亮：去一半饱和度、压暗（防过曝）
bas = tileable(load('basalt'))
save(grade(bas, 0.6, 1.0, (0.96, 0.97, 1.04)), 'moon_basalt')
save(grade(bas, 0.4, 1.35, (0.97, 0.99, 1.05), 0.12), 'moon_gravel')
save(grade(tileable(load('hexroad'), 0.1), 0.5, 0.74, (0.98, 1.0, 1.04)), 'moon_road')
