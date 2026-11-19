"""封面主角高清贴图第 3 / 3 步：从烘好的原图颜色出发，做出底色、自发光（见 Tools/hero_hd_mesh.py 的说明）。
输入 /tmp/sp7/hd/{raw,mask,pos}.npy（4096×2048 并排图：左半 = 第一张贴图，右半 = 第二张）。
输出 ArtSource/ModelsP2/Hero_Statue/hd/（都是并排图，Tools/hero_pbr.py 再拆成两张写进 Assets）：
  albedo_raw.png    Tripo 8K 原图烘到新坐标（没打到原模型的像素用三维上最近的像素补）
  albedo.png        贴图增强：和 Tools/hero_tex_enrich.py 同一套做法（11-18 用户：“把贴图整的丰富一些，注意不要串色”），尺度按新的贴图密度换算
  albedo_cover.png  封面用底色：镜片处压暗（和原来的 Tools/hero_emission.py 一样）
  emission.png      自发光：面罩镜片青光 + 岩座裂缝熔岩光
用法：python3 Tools/hero_hd_tex.py"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from scipy.spatial import cKDTree

R = '/Users/ld/Documents/ChatGPT/WarTable/'
B = '/tmp/sp7/hd/'
HD = R + 'ArtSource/ModelsP2/Hero_Statue/hd/'
os.makedirs(HD, exist_ok=True)
K = 0.79 / 0.28                                          # 新坐标的贴图密度是旧的几倍（旧 0.79 mm / 像素，新 0.28）
SIZE = np.array([0.282, 0.440, 0.233])                   # 整尊像的包围盒（米）
LENS = np.array([0.541, 0.898, 0.639])                   # 镜片中心（包围盒里的归一化坐标，11-19 从贴图里量的）

ok = np.load(B + 'mask.npy')[..., 0] > 0.5
TH, TW = ok.shape
raw = np.load(B + 'raw.npy')
pos = np.load(B + 'pos.npy')[..., :3].astype(np.float64)
lin = np.clip(raw[..., :3], 0, 1).astype(np.float64)
srgb = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)
hit = ok & (raw[..., 3] > 0.5) & (lin.sum(-1) > 1e-5)
miss = ok & ~hit
print('有效像素 %d（占 %.1f%%），没打到原模型的 %d（%.2f%%）' % (ok.sum(), ok.mean() * 100, miss.sum(), miss.sum() / ok.sum() * 100))
if miss.any():                                           # 修补过的地方（补的洞、删掉的碎块）原模型上没有对应的面：用三维上最近的像素补
    hy, hx = np.where(hit)
    step = max(1, len(hy) // 1500000)
    tree = cKDTree(pos[hy[::step], hx[::step]])
    _, j = tree.query(pos[miss])
    srgb[miss] = srgb[hy[::step][j], hx[::step][j]]


def halves(fn, img):
    """两张贴图各算各的（并排图的左右两半互不相干）。"""
    return np.concatenate([fn(img[:, :TW // 2], ok[:, :TW // 2]), fn(img[:, TW // 2:], ok[:, TW // 2:])], 1)


def fill(img, m):
    """块外的空白用最近的有效像素填上。"""
    idx = ndi.distance_transform_edt(~m, return_distances=False, return_indices=True)
    return img[idx[0], idx[1]]


def save(name, a):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(HD + name)


srgb = halves(fill, srgb)
save('albedo_raw.png', srgb)

# ───────── 贴图增强（逐条对应 Tools/hero_tex_enrich.py；只由像素自身的颜色驱动）
a0 = (srgb * 255).astype(np.float32)
a = a0.copy()
r, g, b = a[..., 0], a[..., 1], a[..., 2]
lum = a.mean(-1)
warm = ok & (r > b + 18)
cool_hi = ok & (b > r + 10) & (lum > 150)
armour = ok & ~warm & ~cool_hi
f = int(round(K))                                        # 局部亮度水平：缩小到旧的贴图密度再做 9 像素的中值滤波（直接在大图上做要 25 像素见方，太慢）
small = np.asarray(Image.fromarray(lum).resize((TW // f, TH // f), Image.BOX))
local = np.asarray(Image.fromarray(ndi.median_filter(small, size=9)).resize((TW, TH), Image.BILINEAR))
rel = np.clip((lum - local) / 26.0, -1.5, 1.5)
STEEL_D, STEEL_L = np.array([20, 27, 41], np.float32), np.array([74, 90, 118], np.float32)
BARE, GRIME = np.array([150, 154, 164], np.float32), np.array([58, 44, 34], np.float32)
t = np.clip((lum - 26) / 128.0, 0, 1)[..., None] ** 0.9
a = np.where(armour[..., None], a * 0.34 + (STEEL_D + (STEEL_L - STEEL_D) * t) * 0.66, a)
rng = np.random.default_rng(7)


def grain(cell):
    n = (rng.random((int(TH / cell), int(TW / cell))) * 255).astype(np.uint8)
    return np.asarray(Image.fromarray(n).resize((TW, TH), Image.BICUBIC), np.float32) / 255


speck = grain(2 * K)
chip = np.clip(rel, 0, 1.5) ** 1.6 * armour * np.clip((speck - 0.42) / 0.3, 0, 1)
a = a * (1 - chip[..., None] * 0.55) + BARE * (chip[..., None] * 0.55)
dirt = np.clip(-rel, 0, 1.5) ** 1.3 * armour * (0.55 + 0.45 * speck)
a = a * (1 - dirt[..., None] * 0.5) + GRIME * (dirt[..., None] * 0.5)
a = a + (grain(6 * K) - 0.5)[..., None] * 13.0 * armour[..., None]
a = np.where(warm[..., None], np.clip(a0 + (a0 - a0.mean(-1, keepdims=True)) * 0.85, 0, 255) * 1.06, a)
a = np.where(cool_hi[..., None], np.clip(a0 * 1.06 + np.array([0, 6, 14], np.float32), 0, 255), a)
al = halves(fill, np.clip(a, 0, 255).astype(np.float64) / 255)
save('albedo.png', al)
print('增强：饱和度中位 %.0f → %.0f' % (np.median((a0.max(-1) - a0.min(-1))[ok]), np.median((a.max(-1) - a.min(-1))[ok])))

# ───────── 自发光 + 封面底色（对应原来的 Tools/hero_emission.py）
p = pos[ok]
unity = np.stack([-p[:, 1], p[:, 2], p[:, 0]], 1)        # Blender（前 x、左右 y、上 z）→ 封面里的 x 右 / y 上 / z 前
P = (unity - unity.min(0)) / (unity.max(0) - unity.min(0))
c = al[ok]
mx, mn = c.max(1), c.min(1)
dd = np.maximum(mx - mn, 1e-6)
sat = dd / np.maximum(mx, 1e-6)
hue = np.where(mx == c[:, 0], ((c[:, 1] - c[:, 2]) / dd) % 6, np.where(mx == c[:, 1], (c[:, 2] - c[:, 0]) / dd + 2, (c[:, 0] - c[:, 1]) / dd + 4)) * 60
rock = P[:, 1] < 0.20
lava = rock & (hue > 8) & (hue < 42) & (sat > 0.5) & (mx > 0.4)
# 熔岩往外晕一圈：在三维里晕（体素格子上做高斯），不会沿着贴图平面漏到旁边不相干的块上
cell, sig = 0.0008, 1.6
m3 = unity * (SIZE / (unity.max(0) - unity.min(0)))
q = (m3 - m3.min(0)) / cell + 6
shape = tuple(np.ceil(q.max(0) + 7).astype(int))
flat = np.ravel_multi_index(np.floor(q + 0.5).astype(int).T, shape)
num = ndi.gaussian_filter(np.bincount(flat, lava.astype(np.float64), minlength=int(np.prod(shape))).reshape(shape), sig)
den = ndi.gaussian_filter(np.bincount(flat, minlength=int(np.prod(shape))).reshape(shape).astype(np.float64), sig)
soft = ndi.map_coordinates(num / np.maximum(den, 1e-9), q.T, order=1, mode='nearest') * rock
lava_w = np.clip(lava * 1.0 + soft * 1.6, 0, 1)
dist = np.sqrt((((P - LENS) * SIZE) ** 2).sum(1))
lens = (dist < 0.015) & (mx > 0.55) & (c[:, 2] > c[:, 0] + 0.03) & (c[:, 1] > c[:, 0] + 0.03)
lens_w = lens * (0.55 + 0.45 * np.clip((0.015 - dist) / 0.004, 0, 1)) * np.clip((mx - 0.5) / 0.3, 0.4, 1)
em = np.zeros((TH, TW, 3))
em[ok] = lava_w[:, None] * np.array([1.0, 0.40, 0.07]) + lens_w[:, None] * np.array([0.30, 1.0, 0.90])
save('emission.png', halves(fill, em))
cov = al.copy()
cov[ok] = c * (1 - lens_w[:, None] * 0.9) + lens_w[:, None] * 0.9 * np.array([0.02, 0.07, 0.07])
save('albedo_cover.png', halves(fill, cov))
print('熔岩像素 %d（晕开后 %d） 镜片像素 %d；镜片所在的贴图：%s' % (lava.sum(), (lava_w > 0.05).sum(), lens.sum(),
      '第一张' if np.where(ok)[1][lens].mean() < TW / 2 else '第二张'))
print('→', HD)
