"""封面主角高清贴图第 2 / 3 步：xatlas 重新展贴图坐标，拆成两张 2048（见 Tools/hero_hd_mesh.py 的说明）。
  第一张 = 上半身的盔甲；第二张 = 下半身的盔甲 + 岩座。岩座细节少，展开前先缩小一半（贴图密度是盔甲的一半），把像素让给盔甲。
  上下分界的高度自动找：让两张图里盔甲的贴图密度一样。
输入 /tmp/sp7/hd/mesh.npz，输出 /tmp/sp7/hd/newuv.npz：uv = 每个三角形三个角的新坐标（T×3×2，各自 0..1），tile = 每个三角形在第几张（0 / 1）。
用法：python3 Tools/hero_hd_atlas.py"""
import numpy as np
import xatlas

D = '/tmp/sp7/hd/'
RES, ROCK = 2048, 0.5
TALL = 0.44                                               # 像实际高（米）；网格里像高是 1
d = np.load(D + 'mesh.npz')
v, f = d['v'], d['f'].astype(np.int64)
a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
cr = np.cross(b - a, c - a)
area = np.linalg.norm(cr, axis=1) / 2
nz = cr[:, 2] / np.maximum(np.linalg.norm(cr, axis=1), 1e-12)
hgt = (a[:, 2] + b[:, 2] + c[:, 2]) / 3 - v[:, 2].min()
rock = (hgt < 0.135) | ((hgt < 0.172) & (nz > 0.35))       # 和 Tools/hero_pbr.py 里按像素分岩石的规则一致
# 找分界：上半盔甲面积 = 下半盔甲面积 + 岩座面积 × 缩小倍数²
order = np.argsort(-hgt)
arm = ~rock[order]
cum = np.cumsum(area[order] * arm)
want = (cum[-1] + area[rock].sum() * ROCK ** 2) / 2
split = hgt[order][np.searchsorted(cum, want)]
upper = ~rock & (hgt >= split)
lower = ~rock & ~upper
print('分界高度 %.3f；面积（单位高）上半盔甲 %.3f  下半盔甲 %.3f  岩座 %.3f' % (split, area[upper].sum(), area[lower].sum(), area[rock].sum()))


def unwrap(groups):
    """groups = [(三角形下标, 缩放)]，放进同一张图集。返回 {三角形下标: 3×2 坐标}"""
    at = xatlas.Atlas()
    for idx, k in groups:
        used, inv = np.unique(f[idx], return_inverse=True)
        at.add_mesh((v[used] * k).astype(np.float32), inv.reshape(-1, 3).astype(np.uint32))
    co = xatlas.ChartOptions(); co.max_iterations = 2
    po = xatlas.PackOptions(); po.resolution = RES; po.padding = 5; po.bilinear = True; po.blockAlign = False; po.bruteForce = True
    at.generate(co, po)
    print('  图块', at.chart_count, '图集', at.width, 'x', at.height, '利用率 %.3f' % float(at.utilization))
    out = {}
    for i, (idx, k) in enumerate(groups):
        vmap, ind, uvs = at[i]
        used, inv = np.unique(f[idx], return_inverse=True)
        assert len(ind) == len(idx) and (vmap[ind] == inv.reshape(-1, 3)).all(), '三角形顺序变了'
        out[i] = uvs[ind].astype(np.float64)
    return out


uv = np.zeros((len(f), 3, 2))
tile = np.zeros(len(f), np.int32)
iu, il, ir = np.where(upper)[0], np.where(lower)[0], np.where(rock)[0]
print('第一张（上半身）'); r = unwrap([(iu, 1.0)]); uv[iu] = r[0]
print('第二张（下半身 + 岩座）'); r = unwrap([(il, 1.0), (ir, ROCK)]); uv[il] = r[0]; uv[ir] = r[1]; tile[il] = 1; tile[ir] = 1
ua = np.abs((uv[:, 1, 0] - uv[:, 0, 0]) * (uv[:, 2, 1] - uv[:, 0, 1]) - (uv[:, 2, 0] - uv[:, 0, 0]) * (uv[:, 1, 1] - uv[:, 0, 1])) / 2
for name, m in (('上半盔甲', upper), ('下半盔甲', lower), ('岩座', rock)):
    mm = np.sqrt(area[m].sum() * TALL ** 2 / (ua[m].sum() * RES ** 2)) * 1000
    print('%s：三角形 %d，占所在贴图 %.1f%%，一个像素约 %.2f mm' % (name, m.sum(), ua[m].sum() * 100, mm))
assert uv.min() >= 0 and uv.max() <= 1
np.savez(D + 'newuv.npz', uv=uv, tile=tile)
print('→', D + 'newuv.npz')
