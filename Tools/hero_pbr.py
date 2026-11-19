"""封面主角（Hero_Statue）质感贴图：合成金属度 / 光滑度 / 遮蔽、法线和底色，拆成两张贴图写进 Assets。
11-19 用户：“把质感做好，不要看着很塑料”（原来只有一张颜色贴图，全身同一个金属度和光滑度、没有法线）；
随后：“现在的贴图很糊”“质量很低” → 贴图坐标重展、从 Tripo 8K 原图重烘、拆成上下两张 2048（Tools/hero_hd_mesh.py 开头有完整说明）。

顺序：
  1. Blender -b -P Tools/hero_hd_mesh.py -- dump ；python3 Tools/hero_hd_atlas.py ；Blender -b -P Tools/hero_hd_mesh.py -- build
  2. python3 Tools/hero_hd_tex.py                        → ArtSource/ModelsP2/Hero_Statue/hd/{albedo_raw,albedo,albedo_cover,emission}.png
  3. python3 Tools/hero_pbr.py [预览图输出目录]            → 本脚本
  4. Unity 里“WarTable/6 搭封面”（CoverHeroBuilder 会自己挂上）

输入都是 4096×2048 的并排图（左半 = 第一张贴图 / 上半身，右半 = 第二张 / 下半身 + 岩座）。
输出（Assets/_WarTable/Art/Models/P2/Hero_Statue/，每种两张，_a = 第一张、_b = 第二张，都是 2048）：
  mask_*.png        R 金属度 / G 遮蔽 / A 光滑度（线性）
  normal_*.png      切线空间法线（绿色朝上，Unity 约定）
  albedo_pbr_*.png  底色：岩座还原成偏暖的深色石头、棱边磨出亮钢、缝里积灰
  emission_*.png    自发光（镜片青光在第一张、熔岩光在第二张）

原则：凡是要看“周围”的运算都在三维空间里做（噪声是三维位置的函数；模糊用三维体素格子），
不在贴图平面上做——贴图被切成很多块，平面上相邻的像素在模型上可能隔得很远，会在块边断开。
盔甲表面的细划痕来自素材库 ange-embed 的 sdxl_chord_silver_metal_scratched（ArtSource/Hero/pbr/），按三个轴向投到模型上。"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

R = '/Users/ld/Documents/ChatGPT/WarTable/'
D = R + 'Assets/_WarTable/Art/Models/P2/Hero_Statue/'
HD = R + 'ArtSource/ModelsP2/Hero_Statue/hd/'
TILE = R + 'ArtSource/Hero/pbr/'
B = '/tmp/sp7/hd/'
PREVIEW = sys.argv[1] if len(sys.argv) > 1 else None
TALL = 0.44                      # 整尊像实际高（米）
UNIT = TALL / 0.9995             # Blender 里像高约 1 → 米


def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def rgb(name):
    return np.asarray(Image.open(HD + name).convert('RGB'), np.float32) / 255


# ───────── 读入，只留有效像素（下面的一维数组都按这个顺序排）
ok = np.load(B + 'mask.npy')[..., 0] > 0.5
p = (np.load(B + 'pos.npy')[..., :3] * UNIT)[ok].astype(np.float64)        # 米；第 2 轴是高度
nr = np.load(B + 'nrm.npy')[..., :3][ok].astype(np.float64)
nr /= np.maximum(np.linalg.norm(nr, axis=1, keepdims=True), 1e-6)
ao = np.clip(np.load(B + 'ao.npy')[..., 0][ok], 0, 1).astype(np.float64)
hgt = p[:, 2] / TALL + 0.5                                                 # 0 脚下 → 1 头顶
col = rgb('albedo_cover.png')[ok].astype(np.float64)
raw = rgb('albedo_raw.png')[ok].astype(np.float64)                         # Tripo 原图：分岩石 / 钢甲用
emi = rgb('emission.png')[ok].astype(np.float64)
print('有效像素', len(p), '占比 %.3f' % ok.mean())

lum = col.mean(1)
mx, mn = col.max(1), col.min(1)
sat = (mx - mn) / np.maximum(mx, 1e-6)
dd = np.maximum(mx - mn, 1e-6)
hue = np.where(mx == col[:, 0], ((col[:, 1] - col[:, 2]) / dd) % 6,
               np.where(mx == col[:, 1], (col[:, 2] - col[:, 0]) / dd + 2, (col[:, 0] - col[:, 1]) / dd + 4)) * 60


# ───────── 三维工具
def noise(cell, seed, octaves=1):
    """三维平滑噪声 0..1。cell = 一格的边长（米）。每多一层频率翻倍、幅度减半。"""
    out = np.zeros(len(p))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        vol = np.random.default_rng(seed + o * 17).random((24, 24, 24))
        out += amp * ndi.map_coordinates(vol, (p / cell * 2 ** o + 100.0).T % 24, order=3, mode='grid-wrap')
        tot += amp
        amp *= 0.5
    return np.clip(out / tot, 0, 1)


def blur3(val, cell, sigma):
    """在三维空间里模糊：像素按位置丢进体素格子、格子上做高斯、再取回来。跨贴图接缝连续。"""
    lo = p.min(0) - cell * (3 * sigma + 2)
    q = (p - lo) / cell
    shape = tuple(np.ceil(q.max(0) + 3 * sigma + 3).astype(int))
    flat = np.ravel_multi_index(np.floor(q + 0.5).astype(int).T, shape)
    size = int(np.prod(shape))
    cnt = ndi.gaussian_filter(np.bincount(flat, minlength=size).reshape(shape).astype(np.float64), sigma)
    val = np.atleast_2d(val.T).T                                           # (n, k)
    out = []
    for k in range(val.shape[1]):
        s = ndi.gaussian_filter(np.bincount(flat, val[:, k], minlength=size).reshape(shape), sigma)
        out.append(ndi.map_coordinates(s / np.maximum(cnt, 1e-9), q.T, order=1, mode='nearest'))
    return np.stack(out, 1) if len(out) > 1 else out[0]


def triplanar(tile, size, shift=0.0):
    """把一张可平铺的灰度图按三个轴向投到模型上，按法线朝向混合。size = 一张图铺多宽（米）。"""
    w = np.abs(nr) ** 4
    w /= w.sum(1, keepdims=True)
    n = tile.shape[0]

    def take(a, b):
        return ndi.map_coordinates(tile, [(a / size * n + shift) % n, (b / size * n + shift * 1.7) % n], order=1, mode='grid-wrap')
    return w[:, 0] * take(p[:, 1], p[:, 2]) + w[:, 1] * take(p[:, 2], p[:, 0]) + w[:, 2] * take(p[:, 0], p[:, 1])


def height_of(normal_png, n=512):
    """从一张可平铺的法线图反推高度图（频域积分），返回“每像素斜率”口径的高度。"""
    a = np.asarray(Image.open(normal_png).convert('RGB').resize((n, n), Image.LANCZOS), np.float64) / 127.5 - 1
    gx = -a[..., 0] / np.maximum(a[..., 2], 0.2)                             # 向右的坡度
    gy = a[..., 1] / np.maximum(a[..., 2], 0.2)                              # 向下的坡度（图片行号朝下，法线绿色朝上）
    fx, fy = np.meshgrid(np.fft.fftfreq(n) * 2 * np.pi, np.fft.fftfreq(n) * 2 * np.pi)
    den = fx ** 2 + fy ** 2
    den[0, 0] = 1
    h = np.real(np.fft.ifft2((-1j * fx * np.fft.fft2(gx) - 1j * fy * np.fft.fft2(gy)) / den))
    return h - h.mean()


# ───────── 分区（每个都是 0..1 的权重）
# 岩石：0.135 以下全是。0.135–0.20 这一层岩顶 / 靴底 / 锤头混在一起：
#   岩顶是一整片朝上的面，最高到 0.165（俯视图 /tmp/sp7/tools/topmap.py 看过）→ 0.172 以下而且朝上的算岩石；
#   其余（竖着的面）看 Tripo 原图的颜色——岩石是中性深灰，钢甲偏蓝、更亮。第一版只看颜色，岩顶被原图画得偏亮偏蓝，整片误判成了钢
raw_rb, raw_lum = raw[:, 0] - raw[:, 2], raw.mean(1)
by_color = ss(-0.055, -0.02, raw_rb) * ss(0.40, 0.28, raw_lum)
rock_top = ss(0.180, 0.165, hgt) * ss(0.25, 0.45, nr[:, 2])
rock = np.where(hgt < 0.135, 1.0, np.where(hgt < 0.20, np.maximum(rock_top, by_color * ss(0.20, 0.17, hgt)), 0.0))
rock = ss(0.35, 0.65, blur3(rock, 0.002, 1.0))                              # 三维里抹掉零星误判
lava = np.clip(emi[:, 0] - emi[:, 2], 0, 1) * rock                           # 熔岩裂缝（自发光图里偏红的）
lens = np.clip(np.minimum(emi[:, 1], emi[:, 2]) - emi[:, 0], 0, 1) / 0.7     # 面罩镜片（偏青的）
lens = np.clip(lens, 0, 1)
orange = (hue > 8) & (hue < 45) & (sat > 0.5) & (mx > 0.38)
paint = orange * (1 - rock) * (hgt > 0.2)                                    # 肩甲 / 锤柄上的橙色警示漆
armor = np.clip(1 - rock - lens - paint, 0, 1)

# 细小部件（手指、腰带扣、锤柄缠带……）：周围 1 cm 内法线朝向乱的地方。11-19 用户：“手的位置没有之前清楚了”——
# 第一版把磨损 / 划痕 / 缝隙积灰一视同仁地加上去，手指之间的分界被一块块明暗盖住了。这些地方要干净：形体靠原图的描线和平滑的受光来读
turn = np.linalg.norm(blur3(nr, 0.003, 3.0), axis=1)                          # 大平面接近 1，手指这种细圆柱只有 0.5 上下
fine = ss(0.70, 0.46, turn)                                                   # 第一版 0.86–0.62 圈进了 44% 的表面，等于把做旧关掉一半
print('法线一致度 P5/25/50/75', np.percentile(turn, [5, 25, 50, 75]).round(2))
band = (hgt > 0.66) & (hgt < 0.82) & (np.abs(p[:, 1]) < 0.07)                  # 双手：这个高度上最靠前的那一团（扶在锤柄顶上）
front = p[band, 0].max()
hand_c = np.median(p[band & (p[:, 0] > front - 0.025)], 0)
hand = ss(0.062, 0.040, np.linalg.norm(p - hand_c, axis=1))
fine = np.clip(np.maximum(fine, hand), 0, 1)
calm = 1 - 0.85 * fine                                                        # 细小部件上各种做旧效果只留一点
print('细小部件占比 %.3f；双手中心（Blender 米）%s，范围内像素 %d' % (fine.mean(), hand_c.round(3), int((hand > 0.5).sum())))

# 凸棱 / 凹缝：像素位置减去“周围表面的重心”，再投到法线上。凸出来的为正（米）
bulge = ((p - blur3(p, 0.0025, 2.0)) * nr).sum(1)
edge = ss(0.0005, 0.0020, bulge) * ss(0.75, 0.95, ao)                        # 露在外面的凸棱
wear = edge * ss(0.30, 0.62, noise(0.006, 3, 2)) * calm                      # 磨损不是一整圈，断断续续
cavity = np.clip(np.maximum(ss(0.68, 0.22, ao), 0.6 * ss(0.0007, 0.0022, -bulge) * ss(0.95, 0.6, ao)), 0, 1) * calm   # 缝隙、甲片交叠处
ink = ss(0.15, 0.06, lum) * armor                                            # 贴图里画的黑描线 → 当成甲片接缝
bare = ss(0.50, 0.72, lum) * ss(0.36, 0.16, sat) * armor                     # 亮而不艳 → 掉漆露出的钢
stain = ss(0.56, 0.80, noise(0.028, 11, 3)) * armor * calm                   # 一片片的旧渍
print('分区占比  岩石 %.3f  熔岩 %.3f  镜片 %.4f  警示漆 %.3f | 磨损 %.3f  缝隙 %.3f  描线 %.3f  露钢 %.3f' % (
    rock.mean(), (lava > 0.2).mean(), (lens > 0.2).mean(), paint.mean(), wear.mean(), cavity.mean(), ink.mean(), bare.mean()))

# ───────── 素材库的划痕钢板 → 粗糙度起伏 + 划痕高度
t_rough = np.asarray(Image.open(TILE + 'scratched_orm.png').convert('RGB').resize((512, 512), Image.LANCZOS), np.float64)[..., 1] / 255
t_rough = (t_rough - t_rough.mean()) / t_rough.std()
t_high = height_of(TILE + 'scratched_normal.png', 512)
t_high -= ndi.gaussian_filter(t_high, 8, mode='wrap')                          # 只留细划痕：反推出来的高度带着大片缓坡，放大后整条手臂像揉皱的锡箔
S1, S2 = 0.16, 0.10                                                          # 两层不同大小、错开叠加，免得看出重复；盔甲贴图一个像素约 0.28 mm，划痕图缩到 512 后一格 0.2–0.3 mm
TN = 512
scr_r = np.clip(0.6 * triplanar(t_rough, S1) + 0.4 * triplanar(t_rough[:, ::-1], S2, 137.0), -2.5, 2.5)   # 第二层左右翻转：两层划痕方向交叉，不会全身一个方向像排线
scr_h = triplanar(t_high, S1) * (S1 / TN) + 0.5 * triplanar(t_high[:, ::-1], S2, 137.0) * (S2 / TN)    # 换算成米

# ───────── 高度（米）→ 法线
dent = noise(0.014, 21, 2) - 0.5                                             # 锻打 / 磕碰的缓起伏
pit = ss(0.80, 0.93, noise(0.0030, 31))                                      # 零星小坑
grain = noise(0.0012, 41) - 0.5                                              # 铸造细颗粒
crag = (noise(0.020, 51, 4) - 0.5) * 0.0030 + (noise(0.0045, 61, 2) - 0.5) * 0.0007      # 岩石的粗细起伏
h = armor * ((dent * 0.00022 + scr_h * 1.6 - pit * 0.00013 + grain * 0.00003) * calm - ink * 0.00030 - bare * 0.00005) \
    + paint * (scr_h * 1.6 - pit * 0.00010) \
    + rock * (crag + (lum - 0.14) * 0.0022 - lava * 0.0006)

H = np.zeros(ok.shape)
H[ok] = h
P3 = np.zeros(ok.shape + (3,))
P3[ok] = p


def slope(axis, sign):
    """沿贴图的一个方向求坡度（高度差 ÷ 实际距离）。只在模型上确实相邻的像素之间算，块边用单侧差分。"""
    a, b = np.roll(H, -1, axis), np.roll(H, 1, axis)
    pa, pb = np.roll(P3, -1, axis), np.roll(P3, 1, axis)
    oa, ob = np.roll(ok, -1, axis) & ok, np.roll(ok, 1, axis) & ok
    da, db = np.linalg.norm(pa - P3, axis=2), np.linalg.norm(P3 - pb, axis=2)
    lim = 4 * np.median(da[oa])
    oa &= (da < lim) & (da > 1e-7)
    ob &= (db < lim) & (db > 1e-7)
    fa = np.where(oa, (a - H) / np.maximum(da, 1e-7), 0.0)
    fb = np.where(ob, (H - b) / np.maximum(db, 1e-7), 0.0)
    cnt = oa.astype(np.float64) + ob
    return sign * (fa + fb) / np.maximum(cnt, 1), np.median(da[oa])


su, texel = slope(1, 1.0)                 # 贴图向右
sv, _ = slope(0, -1.0)                    # 贴图向上（图片行号朝下）
su, sv = np.clip(su, -1.5, 1.5), np.clip(sv, -1.5, 1.5)
nm = np.dstack([-su, -sv, np.ones_like(su)])
nm /= np.linalg.norm(nm, axis=2, keepdims=True)
tilt = np.degrees(np.arccos(np.clip(nm[..., 2][ok], -1, 1)))
print('一个像素约 %.2f mm；法线偏角 中位 %.1f°  P90 %.1f°  P99 %.1f°' % (texel * 1000, np.median(tilt), np.percentile(tilt, 90), np.percentile(tilt, 99)))

# ───────── 金属度 / 光滑度 / 遮蔽
# 钢甲的金属度底数留在 0.79（和没有贴图时的 0.78 一样）：试过 0.90，受主光的漫反射少了一半，躯干和腿整体暗了 15%
metal = armor * (0.79 + 0.18 * np.maximum(bare, wear)) * (1 - 0.45 * ink) * (1 - 0.30 * cavity) + paint * 0.12
smooth_armor = (0.51 + (-0.065 * scr_r + (noise(0.032, 71, 3) - 0.5) * 0.26) * calm + 0.22 * wear + 0.10 * bare
                - 0.22 * cavity - 0.20 * ink - 0.18 * pit * calm - 0.12 * stain)
smooth_rock = 0.09 + 0.12 * noise(0.012, 81, 2) + 0.30 * lava
smooth_paint = 0.30 - 0.06 * scr_r - 0.15 * cavity
smooth = np.clip(armor * np.maximum(smooth_armor, 0.14) + rock * smooth_rock + paint * smooth_paint + lens * 0.93, 0.05, 0.95)
occ = np.clip((0.12 + 0.88 * ao) * (1 - 0.35 * ink) * (1 - 0.25 * pit), 0, 1)   # 留一点底：深色甲在缝里也别死黑，要看得清甲片
occ = occ + fine * 0.6 * (1 - occ)                                          # 细小部件的缝别压死：指缝靠描线读，不靠一团黑
occ = occ * (1 - lens) + lens

# ───────── 底色
out = col.copy()
warm = np.array([1.04, 1.0, 0.95])
stone = (0.045 + 0.36 * lum)[:, None] * warm * (0.75 + 0.5 * noise(0.010, 91, 3))[:, None]   # 岩石：去掉蓝味、压成略偏暖的深色玄武岩（第一版太亮太黄，像陶土），有深浅
keep = np.clip(orange * 1.0 + lava * 2, 0, 1)[:, None]                                  # 熔岩的橙色原样留着
out = np.where(rock[:, None] > 0, out * (1 - rock[:, None]) + (stone * (1 - keep) + col * keep) * rock[:, None], out)
a3 = armor[:, None]
out = out * (1 + a3 * ((1.10 + 0.14 * calm * (noise(0.004, 101, 2) - 0.5))[:, None] - 1))                 # 细微的深浅不匀；整体提一成：加了遮蔽和粗糙以后钢甲比原来暗，深色甲要看得清甲片
steel = np.array([0.66, 0.64, 0.61])
out = out + a3 * (wear * 0.50)[:, None] * (steel - out)                                 # 棱边磨亮
grime = out * 0.42 * np.array([1.18, 0.96, 0.76])
out = out + ((armor + paint) * cavity * 0.60)[:, None] * (grime - out)                  # 缝里积的暗褐色灰
out = out * (1 + a3 * (stain * 0.22)[:, None] * (np.array([1.10, 0.93, 0.76]) - 1))     # 旧渍略偏锈色
out = np.clip(out, 0, 1)


# ───────── 写图：块外的空白用最近的有效像素填上（缩小贴图时不会渗进黑边）；左右两半是两张贴图，各填各的
HALF = ok.shape[1] // 2


def fill(img):
    parts = []
    for sl in (slice(0, HALF), slice(HALF, None)):
        idx = ndi.distance_transform_edt(~ok[:, sl], return_distances=False, return_indices=True)
        parts.append(img[:, sl][idx[0], idx[1]])
    return np.concatenate(parts, 1)


def put(v, k):
    a = np.zeros(ok.shape + (k,))
    a[ok] = v.reshape(len(p), k)
    return fill(a)


def u8(a):
    return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)


def write(name, img):
    for tag, sl in (('a', slice(0, HALF)), ('b', slice(HALF, None))):
        Image.fromarray(img[:, sl]).save(D + name + '_' + tag + '.png')


mk = put(np.stack([metal, occ, wear, smooth], 1), 4)                         # 蓝色通道顺手存磨损，方便调试；材质不用它
nimg = fill(nm) * 0.5 + 0.5
aimg = put(out, 3)
write('mask', u8(mk))
write('normal', u8(nimg))
write('albedo_pbr', u8(aimg))
write('emission', np.asarray(Image.open(HD + 'emission.png').convert('RGB')))
print('金属度 均值 %.2f | 光滑度 钢甲 P10/50/90 %s 岩石中位 %.2f | 遮蔽均值 %.2f' % (
    metal.mean(), np.percentile(smooth[armor > 0.8], [10, 50, 90]).round(2), np.median(smooth[rock > 0.8]), occ.mean()))
print('→', D + '{mask,normal,albedo_pbr,emission}_{a,b}.png')

if PREVIEW:
    q = 4                                                                    # 全貌缩小 4 倍
    sheet = np.concatenate([aimg[::q, ::q], np.repeat(mk[::q, ::q, 3:4], 3, 2), nimg[::q, ::q], np.repeat(put(fine, 1)[::q, ::q], 3, 2)], 0)
    Image.fromarray(u8(sheet)).save(PREVIEW + '/hero_pbr_maps.jpg', quality=88)
    c0 = (slice(700, 1300), slice(500, 1100))                                # 一块原尺寸的局部：原图 / 底色 / 光滑度 / 法线
    crop = np.concatenate([put(col, 3)[c0], aimg[c0], np.repeat(mk[c0][..., 3:4], 3, 2), nimg[c0]], 1)
    Image.fromarray(u8(crop)).save(PREVIEW + '/hero_pbr_crop.jpg', quality=90)
    print('预览 →', PREVIEW)
