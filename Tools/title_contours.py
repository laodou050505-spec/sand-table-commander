"""字形图 → 干净的轮廓（python3 Tools/title_contours.py <字形图.png> <输出.json> <分组数> [开运算半径 px=4]）

字形图：透明底黑字，或白纸黑字（书法照片）。给 Tools/blender_title3d.py 挤出成立体字。
书法的飞白、毛边会让轮廓极复杂、自相交，Blender 曲线填充遇到自相交就填不上（10-31 封面 v5 第一次出现大片缺面），所以先清理：
  1. 轻微模糊再二值化：边缘顺滑，去掉纸纹噪点；
  2. 用 shapely 修成合法多边形（buffer(0)），再做开运算（先收 r 再放 r）抹掉比倒角还细的飞白细丝——细丝在倒角时会翻转；
  3. 简化到 0.8 px 误差，丢掉很小的碎块。
按 x 把多边形分到 n 组（中文按字、英文按单词；单字图 n = 1）。
"""
import json, sys
import numpy as np, cv2
from PIL import Image
from shapely.geometry import Polygon, MultiPolygon
from shapely.ops import unary_union

src, out, n = sys.argv[1], sys.argv[2], int(sys.argv[3])
r_open = float(sys.argv[4]) if len(sys.argv) > 4 else 4.0
a = np.array(Image.open(src).convert('RGBA')).astype(np.float32)
gray = a[:, :, :3].mean(axis=2)
alpha = a[:, :, 3]
if alpha.min() < 250:                       # 透明底：墨 = 不透明且深色
    ink = np.where(alpha > 128, 255 - gray, 0)
else:                                       # 白纸黑字 / 红印：墨 = 比纸暗
    paper = np.percentile(gray, 90)
    ink = np.clip((paper - gray) / max(1.0, paper - gray.min()) * 255, 0, 255)
ink = cv2.GaussianBlur(ink, (0, 0), 1.6)
m = (ink > 110).astype(np.uint8) * 255
m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))

cs, hier = cv2.findContours(m, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
polys = []
if hier is not None:
    hier = hier[0]
    for i, c in enumerate(cs):
        if hier[i][3] != -1 or len(c) < 3: continue
        holes = []
        k = hier[i][2]
        while k != -1:
            if len(cs[k]) >= 3: holes.append(cs[k][:, 0, :].tolist())
            k = hier[k][0]
        p = Polygon(c[:, 0, :].tolist(), holes).buffer(0)
        if not p.is_empty: polys.append(p)
shape = unary_union(polys)
shape = shape.buffer(-r_open).buffer(r_open)          # 开运算：去掉细丝
shape = shape.simplify(0.8, preserve_topology=True).buffer(0)
geoms = [g for g in (shape.geoms if isinstance(shape, MultiPolygon) else [shape]) if g.area > 120]

# 按 x 分组
geoms.sort(key=lambda g: g.centroid.x)
spans = []
for g in geoms:
    x0, _, x1, _ = g.bounds
    if spans and x0 < spans[-1][1] - 2: spans[-1][1] = max(spans[-1][1], x1); spans[-1][2].append(g)
    else: spans.append([x0, x1, [g]])
while len(spans) > n:
    gaps = [spans[i + 1][0] - spans[i][1] for i in range(len(spans) - 1)]
    i = int(np.argmin(gaps))
    spans[i] = [spans[i][0], max(spans[i][1], spans[i + 1][1]), spans[i][2] + spans[i + 1][2]]
    del spans[i + 1]

def ring(r): return [[float(x), float(y)] for x, y in list(r.coords)[:-1]]
chars = [[{'outer': ring(g.exterior), 'holes': [ring(h) for h in g.interiors if Polygon(h).area > 20]} for g in s[2]] for s in spans]
allb = unary_union(geoms).bounds
json.dump({'w': m.shape[1], 'h': m.shape[0], 'bbox': [allb[0], allb[1], allb[2], allb[3]], 'chars': chars}, open(out, 'w'))
print(f'{len(chars)} 组：', [sum(1 + len(p["holes"]) for p in c) for c in chars], '个轮廓')
