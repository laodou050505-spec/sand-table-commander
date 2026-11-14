"""白底图 → 带透明通道的贴图（python3 Tools/ink_alpha.py <输入.png> <输出.png> <模式> [最长边=1024]）
模式：
  ink    —— 墨迹 / 笔刷：透明度 = 墨的浓淡（比纸暗多少），颜色统一成白（材质颜色再着色），飞白自然半透明；
  color  —— 朱砂墨迹这类彩色墨：透明度同上，保留原色（去掉纸色混入，颜色按透明度反推）；
  paper  —— 行军地图残片：透明度 = 和白底的差（羊皮纸偏米黄，和纯白差在蓝通道），边缘羽化 2 px，保留原色。
最后裁到内容范围、四周留 8 px。"""
import sys
import numpy as np, cv2
from PIL import Image

src, out, mode = sys.argv[1], sys.argv[2], sys.argv[3]
maxside = int(sys.argv[4]) if len(sys.argv) > 4 else 1024
a = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255.0
lum = a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114
paper = np.percentile(lum, 95)
if mode in ('ink', 'color'):
    # 纸色按局部估计（大核膨胀 = 附近最亮处）：纸有纹理、暗角，用全局纸色会把整张纸当成淡墨，抠出一圈灰框
    k = int(min(lum.shape) * 0.35) | 1   # 取样范围要比最宽的笔画还宽，否则粗笔画中间被当成纸
    local = cv2.dilate(lum, np.ones((k, k), np.uint8))
    local = cv2.GaussianBlur(local, (0, 0), 25)
    al = np.clip((local - lum) / max(1e-3, paper - np.percentile(lum, 0.5)), 0, 1)
    al = np.clip((al - 0.12) / 0.88, 0, 1) ** 0.85
    if mode == 'ink':
        rgb = np.ones_like(a)
    else:
        # 合成前的墨色：c = a·ink + (1−a)·paper → ink = (c − (1−a)·paper) / a
        pc = np.array([np.percentile(a[..., k], 95) for k in range(3)], np.float32)
        rgb = np.clip((a - (1 - al[..., None]) * pc) / np.maximum(al[..., None], 1e-3), 0, 1)
else:
    # 羊皮纸偏黄（红 > 蓝），纸下的投影是中性灰：按“偏黄程度”取纸，再并上纸面上的深色墨线 / 焦边，填洞后只留最大一块
    yel = a[..., 0] - a[..., 2]
    al = np.clip((yel - 0.05) / 0.05, 0, 1)
    al = np.maximum(al, np.clip((0.45 - lum) / 0.1, 0, 1))
    m = (al > 0.5).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
    n, lab, st, _ = cv2.connectedComponentsWithStats(m)
    if n > 1:
        keep = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
        m = (lab == keep).astype(np.uint8)
    cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    m = np.zeros_like(m); cv2.drawContours(m, cs, -1, 1, -1)
    al = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.0)
    rgb = a
rgba = np.dstack([rgb, al])
ys, xs = np.nonzero(al > 0.03)
y0, y1, x0, x1 = max(0, ys.min() - 8), min(al.shape[0], ys.max() + 9), max(0, xs.min() - 8), min(al.shape[1], xs.max() + 9)
im = Image.fromarray((rgba[y0:y1, x0:x1] * 255).astype(np.uint8))
im.thumbnail((maxside, maxside), Image.LANCZOS)
im.save(out)
print(out, im.size)
