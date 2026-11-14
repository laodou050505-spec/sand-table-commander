"""11-13 封面战盔修正（三）第 4 步：在新 UV 上合成明暗图 helm3_albedo.png（灰度、线性；Unity 里乘各部件的底色）。
输入（/tmp/h13/，第 3 步 helm3_bake1113.py 烘的）：old.png 旧明暗图搬到新 UV、ao.png 环境光遮蔽、uv.npz 各部件新 UV 三角形 + “旧 UV 拉飞”标记。
  银钢 part0 / 黑铁 part1：不用旧图（旧图在这两块上碎得最厉害），明暗 = 中性灰 × 渍洗（AO：缝隙、甲片叠压处压暗）× 锻打起伏 + 细划痕。
      **凡是带“邻域”的运算（噪声、模糊、高通）都要在三维空间里做，不能在贴图平面上做**——贴图被切成上千个 UV 块，
      在贴图平面上算的东西到了块边就断，贴回模型上是一块块边缘笔直的色块（详见下面“两半盔壳”一段的三个坑）。
      棱边磨亮试过两种（曲率图、AO 高通）都不行，已去掉：曲率逐顶点插值、带三角形渐变；AO 高通在 UV 块接缝处被切断。
  鎏金 part2 / 角 part3 / 骷髅 part4 / 铆钉 part5、6：沿用旧明暗（定稿的样子）；旧 UV 拉飞的面烘出来是一长条不相干的内容，
      这些像素用同部件里最近的好像素补上，再按 AO 的局部起伏加回一点形体。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/helm_retex/helm3_shade1113.py   （先跑前三步；可重复跑）"""
import numpy as np, cv2
from scipy import ndimage as ndi

import sys
W = '/Users/ld/Documents/ChatGPT/WarTable/'
S = 2048
EDGE_T = float(sys.argv[1]) if len(sys.argv) > 1 else 0.14     # AO 比周围平均亮这么多 = 棱边磨亮拉满
uv = np.load('/tmp/h13/uv.npz')
ID = np.full((S, S), 255, np.uint8); BAD = np.zeros((S, S), np.uint8)
for k in range(7):
    t = uv[f'part{k}']; pts = np.round(np.stack([t[..., 0] * S, (1 - t[..., 1]) * S], -1) * 4).astype(np.int32)
    cv2.fillPoly(ID, list(pts), int(k), lineType=cv2.LINE_8, shift=2)
    b = uv[f'bad{k}']
    if b.any(): cv2.fillPoly(BAD, list(pts[b]), 1, lineType=cv2.LINE_8, shift=2)
rd = lambda n: cv2.imread(f'/tmp/h13/{n}.png', cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255
old, ao = rd('old'), rd('ao')
assert old.shape == (S, S)


def tnoise(n, scale, seed):
    r = np.random.default_rng(seed); fy = np.fft.fftfreq(n)[:, None] * n; fx = np.fft.fftfreq(n)[None, :] * n
    x = np.real(np.fft.ifft2((r.normal(size=(n, n)) + 1j * r.normal(size=(n, n))) * np.exp(-(fx ** 2 + fy ** 2) / scale ** 2)))
    return (x - x.mean()) / (x.std() + 1e-9)


out = np.zeros((S, S), np.float32)
# ---- 两半盔壳：AO 渍洗 + 锻打起伏（都在三维空间里算，跨 UV 块连续）
MED = 0.7
pos = np.load('/tmp/h13/pos.npy')                      # 每个像素在模型上的三维位置（米；第 3 步烘的）
ao_s = cv2.GaussianBlur(ao, (0, 0), 1.6)
shell0 = np.isin(ID, [0, 1]); lab, nlab = ndi.label(shell0)
medao = ndi.median(ao, lab, np.arange(1, nlab + 1)) if nlab else np.zeros(0)
vis = shell0 & np.concatenate([[False], medao > 0.25])[lab]          # 朝外的 UV 块（藏在盔壳里面的夹层面整块 AO 接近 0，不算）
# 11-13 用户看图指出眉额还不对，逐项贴到模型上查，三个坑：
#  1) 在贴图平面上生成的噪声跨 UV 块接不上 → 穹顶上一块块阶梯色块（UV 块边界沿体素重建留下的台阶走）→ 噪声改在三维空间里生成；
#  2) “棱边磨亮”（AO 高通）在金边阴影旁边大片提亮，又在 UV 块接缝处被切断 → 一块边缘笔直的浅色矩形、沿块边的亮纹 → 这一项整个去掉；
#  3) 开阔表面上很浅的遮蔽（体素台阶、相邻 UV 块之间百分之几的差）被 occ^0.8 放大 → 加死区，并且浅遮蔽改用“三维空间里平均过的 AO”。
H = 0.004                                              # 体素边长 4 mm（模型高约 1 m）
pmin = pos[vis].min(0) - 3 * H; dims = np.ceil((pos[vis].max(0) + 3 * H - pmin) / H).astype(int) + 1
ci = np.clip(((pos - pmin) / H).astype(np.int64), 0, dims - 1); flat = (ci[..., 0] * dims[1] + ci[..., 1]) * dims[2] + ci[..., 2]
ssum = np.bincount(flat[vis], weights=ao_s[vis], minlength=int(dims.prod())).reshape(dims).astype(np.float32)
scnt = np.bincount(flat[vis], minlength=int(dims.prod())).reshape(dims).astype(np.float32)
g = ndi.gaussian_filter(ssum, 1.5) / np.maximum(ndi.gaussian_filter(scnt, 1.5), 1e-4)
fc = ((pos - pmin) / H - 0.5).reshape(-1, 3).T
ao3 = ndi.map_coordinates(g, fc, order=1, mode='nearest').reshape(S, S)          # 三线性取样：平滑到约 6 mm 的 AO，跨 UV 块连续
OCC0 = 0.16
occ_lo = np.clip((np.clip(1 - ao3, 0, 1) - OCC0) / (1 - OCC0), 0, 1)             # 浅遮蔽：用三维平均的，低于 OCC0 的当 0
raw = np.clip(1 - ao_s, 0, 1)
occ_hi = np.clip((raw - OCC0) / (1 - OCC0), 0, 1) * np.clip((raw - 0.3) / 0.2, 0, 1)   # 深缝（遮蔽 > 0.3–0.5）：用逐像素的，保持锐利
occ_hi *= np.clip((1 - ao3 - 0.04) / 0.08, 0, 1)                                    # 且三维平均里也得有遮蔽：UV 块边上单像素宽的黑边（烘焙留边）不算
occ = np.maximum(occ_lo, occ_hi)
wash = 1 - 0.62 * np.power(occ, 0.8)
def noise3(wavelength, seed, K=40):
    # 随机傅里叶特征：K 个随机方向、波长在 wavelength 附近的余弦波相加 ≈ 平滑的三维随机场，均值 0、标准差约 1
    r = np.random.default_rng(seed); w = r.normal(size=(K, 3)) * (2 * np.pi / wavelength) * 0.6; ph = r.uniform(0, 2 * np.pi, K)
    o = np.zeros((S, S), np.float32)
    for k in range(K): o += np.cos(pos[..., 0] * w[k, 0] + pos[..., 1] * w[k, 1] + pos[..., 2] * w[k, 2] + ph[k])
    return o * np.sqrt(2.0 / K)
grain = 1 + 0.05 * noise3(0.16, 3) + 0.028 * noise3(0.045, 5) + 0.016 * noise3(0.008, 4)     # 大块深浅 + 锤印大小的起伏 + 细颗粒（波长：米）
edge = np.zeros((S, S), np.float32)                    # 棱边磨亮已去掉（见上面第 2 条），留着变量给下面的式子用
# 细划痕：随机方向的短线，亮的多、暗的少，稍微糊一点
rs = np.random.default_rng(11); scr = np.zeros((S, S), np.float32)
for _ in range(900):
    x, y = rs.uniform(0, S, 2); a = rs.uniform(0, np.pi); L = rs.uniform(8, 34); v = rs.choice([0.055, 0.04, -0.03])     # 第一版又亮又长（0.10、70 像素），穹顶上像大理石纹
    cv2.line(scr, (int(x), int(y)), (int(x + np.cos(a) * L), int(y + np.sin(a) * L)), float(v), 1, cv2.LINE_AA)
scr = cv2.GaussianBlur(scr, (0, 0), 0.6)
if len(sys.argv) > 2 and sys.argv[2] == 'debug':      # 调试：把各项单独存出来，贴到模型上看是哪一项带出的图案（/tmp/sp2/tex_view.py）
    for nm, arr in (('wash', wash), ('edge', edge), ('grain', (grain - 0.8) / 0.4), ('vis', vis.astype(np.float32))): cv2.imwrite(f'/tmp/h13/dbg_{nm}.png', np.round(np.clip(arr, 0, 1) * 255).astype(np.uint8))
shell = np.isin(ID, [0, 1])
out[shell] = np.clip(MED * wash * grain * (1 + 0.24 * edge) + scr * wash, 0.14, 0.96)[shell]
# ---- 其他部件：旧明暗，拉飞的面补洞
badd = cv2.dilate(BAD, np.ones((5, 5), np.uint8)) > 0
ao_n = np.clip(ao_s / np.maximum(cv2.GaussianBlur(ao_s, (0, 0), 14), 1e-3), 0.72, 1.15)
rep = {}
for k in (2, 3, 4, 5, 6):
    m = ID == k; hole = m & badd; good = m & ~badd
    if not good.any(): out[m] = old[m]; continue
    iy, ix = ndi.distance_transform_edt(~good, return_distances=False, return_indices=True)
    filled = old.copy(); filled[hole] = old[iy[hole], ix[hole]]
    # 最近像素补出来的是一道道条纹 → 只在洞里做一次“归一化模糊”（只取同部件的像素），抹成平缓的过渡，再乘 AO 的局部起伏
    wgt = m.astype(np.float32); num = cv2.GaussianBlur(filled * wgt, (0, 0), 4); den = cv2.GaussianBlur(wgt, (0, 0), 4)
    sm = num / np.maximum(den, 1e-4)
    filled[hole] = np.clip(sm[hole] * ao_n[hole], 0.05, 1.0)
    out[m] = filled[m]
    rep[k] = (100 * hole.sum() / max(m.sum(), 1), float(np.median(old[good])), float(np.median(filled[hole])) if hole.any() else 0)
# ---- UV 块之间的空隙：用最近的有效像素填（避免采样渗进黑边）
cov = ID != 255
iy, ix = ndi.distance_transform_edt(~cov, return_distances=False, return_indices=True)
out = out[iy, ix]
cv2.imwrite(W + 'Assets/_WarTable/Art/Models/Cover/Helm3/helm3_albedo.png', np.round(np.clip(out, 0, 1) * 255).astype(np.uint8))
names = {0: '银钢', 1: '黑铁', 2: '鎏金', 3: '角', 4: '骷髅', 5: '黄铜铆钉', 6: '古铜铆钉'}
print(f'贴图覆盖率 {100 * cov.mean():.1f}%')
for k in (0, 1):
    m = ID == k; ext = m & (ao > 0.5)
    print(f'  {names[k]}：{100 * m.mean():.1f}% 贴图面积；朝外的部分（AO > 0.5）中位 {np.median(out[ext]):.2f}，5%–95% {np.percentile(out[ext], 5):.2f}–{np.percentile(out[ext], 95):.2f}')
for k, (pct, mg, mh) in rep.items():
    print(f'  {names[k]}：{100 * (ID == k).mean():.1f}% 贴图面积；补洞 {pct:.1f}%，好像素中位 {mg:.2f}，补出来的中位 {mh:.2f}')
