"""标题机甲贴图加工（11-20 用户：贴图缺质感、很塑料）。输入官网生成的颜色图 + Blender 烘焙的 AO / 凸起程度，输出 PBR 一套：
  albedo.png        调色（藏青压成定稿的黑蓝、柠檬黄改成偏橙的金、红色压深）+ AO 渍洗（缝里发暗）+ 边缘掉漆露钢 + 细碎划痕
  metal_smooth.png  R = 金属度（金 / 露出的钢高，漆面低），A = 光滑度（金和钢亮、漆面半光、缝里和掉漆处粗）——URP Lit 的 _MetallicGlossMap
  normal.png        细节法线（从颜色的明暗高通里取，只要细小起伏，强度很低）
  emission.png      青色观察窗 / 指示灯发光
用法：python3 Tools/titan_tex.py <目录（含 albedo_src.jpg ao.png curv.png）>"""
import sys, colorsys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

D = sys.argv[1].rstrip('/') + '/'
src = np.asarray(Image.open(D + 'albedo_src.jpg').convert('RGB'), np.float32) / 255
H, W, _ = src.shape
ao = np.asarray(Image.open(D + 'ao.png').convert('L').resize((W, H)), np.float32) / 255
cv = np.asarray(Image.open(D + 'curv.png').convert('L').resize((W, H)), np.float32) / 255
import os
def opt(name):
    return np.asarray(Image.open(D + name).convert('L').resize((W, H)), np.float32) / 255 if os.path.exists(D + name) else None
up = opt('up.png'); hgt_map = opt('height.png')
isl_img = np.asarray(Image.open(D + 'island.png').convert('RGB').resize((W, H), Image.NEAREST)) if os.path.exists(D + 'island.png') else None
# 凸起程度归一化：按中位数拉开
med = np.median(cv); cv = np.clip(0.5 + (cv - med) * 2.2, 0, 1)

r, g, b = src[..., 0], src[..., 1], src[..., 2]
mx = src.max(-1); mn = src.min(-1); v = mx; s = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
h = np.zeros_like(mx)
d = np.maximum(mx - mn, 1e-5)
h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
def band(x, a, b_, soft):
    return np.clip(np.minimum((x - a) / soft + 1, (b_ - x) / soft + 1), 0, 1)
def ramp(x, a, b_): return np.clip((x - a) / (b_ - a), 0, 1)
gold = band(h, 28, 62, 8) * ramp(s, 0.32, 0.5) * ramp(v, 0.25, 0.4)
red = np.maximum(band(h, -20, 14, 8), band(h, 340, 380, 8)) * ramp(s, 0.35, 0.5) * (1 - gold)
glow = band(h, 165, 200, 10) * ramp(s, 0.45, 0.6) * ramp(v, 0.55, 0.75)
steel = (1 - ramp(s, 0.12, 0.25)) * (1 - gold) * (1 - red)
navy = np.clip(1 - gold - red - glow - steel, 0, 1)
print('占比 金 %.2f 红 %.2f 藏青 %.2f 钢 %.2f 发光 %.3f' % tuple(m.mean() for m in (gold, red, navy, steel, glow)))

# ── 串色清理（11-20 用户：左大腿等处串色）：官网生成的颜色在一些零件上把金色“洇”到了藏青装甲上——
#    真正的金边 / 星徽边缘是清楚的，洇出来的金色是一团边缘发虚的斑。按 UV 岛找：以藏青为主的岛里，
#    金色连通块中“半金半蓝的过渡像素”占比高（发虚）的，改回藏青。
if isl_img is not None:
    key = (isl_img[..., 0].astype(np.int32) << 16) | (isl_img[..., 1].astype(np.int32) << 8) | isl_img[..., 2]
    uniq, inv = np.unique(key, return_inverse=True); inv = inv.reshape(H, W)
    cnt = np.bincount(inv.ravel()); gsum = np.bincount(inv.ravel(), gold.ravel()); nsum = np.bincount(inv.ravel(), navy.ravel())
    navy_isl = ((nsum / np.maximum(cnt, 1)) > 0.5)[inv] & (key != 0)
    blobs, nb = ndi.label(gold > 0.35)
    if nb:
        area = np.bincount(blobs.ravel())
        soft = np.bincount(blobs.ravel(), ((gold > 0.15) & (gold < 0.85)).ravel().astype(np.float32)) / np.maximum(area, 1)
        on_navy = np.bincount(blobs.ravel(), navy_isl.ravel().astype(np.float32)) / np.maximum(area, 1)
        bad = (soft > 0.42) & (on_navy > 0.6); bad[0] = False
        fix = bad[blobs]
        fixs = ndi.gaussian_filter(fix.astype(np.float32), 2.0)
        fixs = np.maximum(fixs, fix)
        navy = np.clip(navy + gold * fixs, 0, 1); gold = gold * (1 - fixs)
        print('串色清理：改回藏青的金色斑块 %d 个，占全图 %.2f%%' % (bad.sum(), fix.mean() * 100))
lum = src @ np.array([0.299, 0.587, 0.114], np.float32)
detail = lum / (ndi.gaussian_filter(lum, 3) + 1e-3)          # 原图里的细节（划痕、纹样）按比例保留
detail = np.clip(detail, 0.6, 1.5)[..., None]
def col(*c): return np.array(c, np.float32)
# ── 调色：各区域换成目标色 × 原图细节
tgt_navy = col(0.07, 0.085, 0.13)        # 定稿的黑里带蓝（11-20 第二版提亮一点，掉漆和划痕才看得出）
tgt_gold = col(0.70, 0.46, 0.17)        # 金：偏橙、不是柠檬黄（金属的颜色就是反射颜色）；第一版 0.80/0.56/0.22 渲出来还是发柠檬黄
tgt_red = col(0.36, 0.05, 0.045)
tgt_steel = col(0.36, 0.36, 0.37)
shade = np.clip(lum / (ndi.gaussian_filter(lum, 40) + 1e-3), 0.55, 1.45)[..., None]   # 原图里大块明暗（画上去的高光阴影）留一半
out = (navy[..., None] * tgt_navy * (0.6 + 0.4 * shade) * detail
       + gold[..., None] * tgt_gold * (0.55 + 0.45 * shade) * detail
       + red[..., None] * tgt_red * (0.6 + 0.4 * shade) * detail
       + steel[..., None] * (tgt_steel * 0.75 + src * 0.25) * detail   # 原图的灰色部分斑驳得像大理石 → 只留四分之一
       + glow[..., None] * col(0.25, 0.9, 1.0))

rng = np.random.default_rng(7)
def noise(scale, seed):
    n = np.random.default_rng(seed).random((H // scale + 2, W // scale + 2)).astype(np.float32)
    return np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((W + 2 * scale, H + 2 * scale), Image.BICUBIC), np.float32)[scale:scale + H, scale:scale + W] / 255
n_big, n_mid, n_fine = noise(64, 1), noise(12, 2), noise(3, 3)
# ── 边缘掉漆：棱边 × 斑驳噪声，漆面（藏青 / 红）上露出钢
edge = ramp(cv, 0.62, 0.86)
chip = np.clip(edge * (0.6 + 1.0 * n_mid) - 0.38, 0, 1) * 2.4               # 11-20：做旧不够 → 掉漆更多
chip = np.clip(chip + (n_fine > 0.9) * edge * 0.7 + (n_fine > 0.985) * 0.5, 0, 1)   # 碎小的磕点（棱上多、平面上零星几个）
# 划痕：随机的细短线（每个 UV 岛够大了，方向随意也像真的刮痕）
from PIL import ImageDraw
scr = Image.new('L', (W, H), 0); sd = ImageDraw.Draw(scr); rs = np.random.default_rng(11)
for _ in range(5200):
    x, y = rs.uniform(0, W), rs.uniform(0, H); ang = rs.uniform(0, np.pi); L = rs.uniform(8, 60) * (W / 4096)
    sd.line((x, y, x + np.cos(ang) * L, y + np.sin(ang) * L), fill=int(rs.uniform(90, 255)), width=1)
scratch = np.asarray(scr, np.float32) / 255 * ramp(n_big, 0.35, 0.8)
chip = np.clip(chip + scratch * 0.75, 0, 1)
paint = navy + red
chip_p = chip * paint
bare = col(0.48, 0.47, 0.46) * (0.9 + 0.2 * n_fine[..., None])
out = out * (1 - chip_p[..., None]) + bare * chip_p[..., None]
# 金边棱上磨亮
gold_hi = edge * gold * 0.35
out = out * (1 + gold_hi[..., None])
# ── AO 渍洗：缝里发暗、偏一点油污的棕
wash = np.clip(1 - ao, 0, 1) ** 1.3
out = out * (1 - 0.72 * wash[..., None]) + col(0.05, 0.035, 0.025) * (0.35 * wash[..., None])
# 积尘：越低越脏（脚、小腿），朝上的面更多；灰土色，带斑驳
if hgt_map is not None:
    low = np.clip(1 - hgt_map / 0.32, 0, 1) ** 1.5
    upw = ramp(up, 0.6, 0.85) if up is not None else 0
    dust = np.clip(low * (0.55 + 0.6 * n_mid) + upw * 0.35 * n_big, 0, 1) * 0.75
    out = out * (1 - dust[..., None]) + col(0.33, 0.29, 0.23) * (0.8 + 0.4 * n_fine[..., None]) * dust[..., None]
# 大块的使用痕迹：漆面上轻微的明暗不匀
out = out * (0.9 + 0.2 * n_big[..., None] * paint[..., None] + 0.1 * (1 - paint[..., None]))
out = np.clip(out, 0, 1)

# ── 金属度 / 光滑度
metal = gold * 0.7 + steel * 0.55 + navy * 0.12 + red * 0.08 + glow * 0.0   # 11-20 用户：不要反光 → 金属度整体压低
metal = metal * (1 - chip_p) + 0.9 * chip_p
smooth = gold * 0.5 + steel * 0.38 + navy * 0.34 + red * 0.3 + glow * 0.8   # 漆面半哑光
smooth = smooth * (1 - chip_p) + 0.42 * chip_p
smooth = smooth * (1 - 0.55 * wash) * (0.88 + 0.24 * n_mid)                 # 缝里脏、粗糙；表面有不均匀
if hgt_map is not None: smooth = smooth * (1 - 0.7 * dust); metal = metal * (1 - 0.8 * dust)   # 积尘处不反光
ms = np.zeros((H, W, 4), np.float32); ms[..., 0] = metal; ms[..., 1] = metal; ms[..., 2] = metal; ms[..., 3] = np.clip(smooth, 0.05, 0.9)

# ── 细节法线：颜色明暗的高通当高度（只要小起伏），加掉漆处的凹陷
hgt = ndi.gaussian_filter(lum, 0.8) - ndi.gaussian_filter(lum, 5)
hgt = hgt * 1.0 - chip_p * 0.06
gy, gx = np.gradient(hgt)
k = 9.0   # 11-23：细节法线加强一点，甲面的小起伏在头显里看得出
nx, ny, nz = -gx * k, gy * k, np.ones_like(hgt)
ln = np.sqrt(nx * nx + ny * ny + nz * nz)
nrm = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5

# 11-23 用户：机甲在头显里不够清楚 → 发光遮罩收紧边缘（原来是官网颜色图里一团发虚的青色，发光后更糊）
glow_e = np.clip((glow - 0.35) / 0.4, 0, 1)
em = (glow_e[..., None] * col(0.3, 0.95, 1.0))
# 颜色图锐化（反锐化掩膜：原图 + 0.7 ×（原图 − 1.2 像素模糊））：官网生成的颜色本身偏软，只拉细节不改大块明暗；
# 先按亮度做，避免彩色镶边
lum_o = out @ np.array([0.299, 0.587, 0.114], np.float32)
usm = (lum_o - ndi.gaussian_filter(lum_o, 1.2)) * 0.7
out = np.clip(out + usm[..., None], 0, 1)

def save(name, arr, mode='RGB'):
    Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(D + name); print('写出', D + name)
save('albedo.png', out); save('metal_smooth.png', ms, 'RGBA'); save('normal.png', nrm); save('emission.png', em)
