"""把一张材质照片做成无缝平铺贴图：python3 Tools/make_tileable.py <输入.png> <输出.png> [边长=1024] [--flatten 0.8]
1. 先压平大尺度明暗（照片常有中间亮四周暗），否则平铺后一格一格看得出来；
2. 和错开半张的自己交叉淡化：原图的边缘处用错开那张（它的中间），错开那张的接缝处用原图。"""
import sys
import numpy as np
from PIL import Image, ImageFilter

src, dst = sys.argv[1], sys.argv[2]
size = int(sys.argv[3]) if len(sys.argv) > 3 and not sys.argv[3].startswith('--') else 1024
flat = float(sys.argv[sys.argv.index('--flatten') + 1]) if '--flatten' in sys.argv else 0.8

im = Image.open(src).convert('RGB')
w, h = im.size
s = min(w, h)
im = im.crop(((w - s) // 2, (h - s) // 2, (w - s) // 2 + s, (h - s) // 2 + s)).resize((size, size), Image.LANCZOS)
a = np.asarray(im).astype(np.float32)
# 大尺度明暗压平：除以强模糊后的亮度，再乘回平均亮度
blur = np.asarray(im.filter(ImageFilter.GaussianBlur(size / 8))).astype(np.float32)
lum = blur.mean(2, keepdims=True) + 1e-3
if '--hard' in sys.argv:
    # 颗粒类材质连色相一起压平（大块的灰绿 / 黄绿斑平铺后一眼就看出重复，大尺度变化交给地形着色器的噪声）
    blur = np.asarray(im.filter(ImageFilter.GaussianBlur(size / 40))).astype(np.float32)   # 斑块只有一两百像素，模糊半径要小
    a = a * ((blur.reshape(-1, 3).mean(0) / (blur + 1e-3)) ** flat)
else:
    a = a * ((lum.mean() / lum) ** flat)
shift = np.roll(np.roll(a, size // 2, 0), size // 2, 1)
yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
d = np.minimum(np.minimum(xx, size - 1 - xx), np.minimum(yy, size - 1 - yy)) / (size / 2)
wgt = np.clip((d - 0.08) / 0.5, 0, 1)
wgt = wgt * wgt * (3 - 2 * wgt)
if '--hard' in sys.argv:
    # 颗粒类材质（草皮、碎石）：交叉淡化会把两层颗粒叠成一片发白的雾，改成按团块噪声硬切——
    # 每个像素只取其中一张，接缝藏在团块边缘
    rng = np.random.default_rng(7)
    noise = Image.fromarray((rng.random((size // 16, size // 16)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
    noise = np.asarray(noise.filter(ImageFilter.GaussianBlur(size / 96))).astype(np.float32) / 255
    noise = (noise - noise.min()) / (noise.max() - noise.min() + 1e-6)
    pick = (wgt + (noise - 0.5) * 0.9) > 0.5
    out = np.where(pick[..., None], a, shift)
else:
    out = a * wgt[..., None] + shift * (1 - wgt[..., None])
Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(dst)
print('ok', dst, size)
