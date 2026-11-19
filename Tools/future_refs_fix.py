"""未来时代定妆图统一校色（11-12）：AI 出的图我军有的偏粉米色、有的偏冷白，敌军有的偏棕黑、有的偏蓝黑 → 做 3D 前统一。
我军（kd_）：浅色暖调的甲片（色相 0–55°、饱和度 0.04–0.34、够亮）→ 去掉大部分饱和度、略提亮 = 中性珍珠白；金边（饱和度高）和青光不动。
敌军（cl_）：深色、不是紫光的部分 → 统一成略带蓝紫的黑曜石色（色相 255°、饱和度 0.18）；紫色缝光不动。
原图备份在 ArtSource/Future/refs/orig/，每次从备份重算（可重复跑）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/future_refs_fix.py"""
import colorsys, pathlib, shutil
import numpy as np
from PIL import Image

REFS = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/ArtSource/Future/refs')
ORIG = REFS / 'orig'; ORIG.mkdir(exist_ok=True)
rgb2hsv = np.vectorize(colorsys.rgb_to_hsv); hsv2rgb = np.vectorize(colorsys.hsv_to_rgb)

for p in sorted(REFS.glob('*.png')):
    bak = ORIG / p.name
    if not bak.exists() or p.stat().st_mtime > bak.stat().st_mtime + 1 and not (REFS / (p.stem + '.fixed')).exists(): shutil.copy(p, bak)
    a = np.asarray(Image.open(bak).convert('RGB')).astype(np.float64) / 255
    h, s, v = rgb2hsv(a[..., 0], a[..., 1], a[..., 2]); hd = h * 360
    if p.stem.startswith('kd_'):
        m = ((hd < 55) | (hd > 330)) & (s > 0.04) & (s < 0.34) & (v > 0.5)
        s = np.where(m, s * 0.22, s); v = np.where(m, np.minimum(1, v * 1.04 + 0.02), v)
        dark = (v <= 0.5) & (s < 0.4) & ((hd < 70) | (hd > 300))          # 内衬：棕灰 → 中性炭灰
        s = np.where(dark, s * 0.3, s)
    else:
        glow = (hd > 235) & (hd < 325) & (s > 0.3) & (v > 0.22)
        m = (v < 0.5) & ~glow & (s > 0.02)
        h = np.where(m, 255 / 360, h); s = np.where(m, 0.18, s)
    r, g, b = hsv2rgb(h, s, v)
    Image.fromarray((np.clip(np.stack([r, g, b], -1), 0, 1) * 255 + 0.5).astype(np.uint8)).save(p)
    (REFS / (p.stem + '.fixed')).touch()
    print(p.stem, 'ok')
