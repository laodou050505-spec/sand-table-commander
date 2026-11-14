"""11-13 封面战盔修正（三）第 2 步：xatlas 给整顶盔（part0..6）重新展 UV。每个部件作为一个独立网格放进同一张图集（UV 块不会跨部件）。
输入 /tmp/h13/mesh.npz（helm3_fix1113.py），输出 /tmp/h13/newuv.npz：uv<k> = 每个三角形三个角的新 UV（T×3×2，和输入三角形同序）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/helm_retex/helm3_atlas1113.py"""
import numpy as np, xatlas

d = np.load('/tmp/h13/mesh.npz')
ks = [k for k in range(7) if f'v{k}' in d]
atlas = xatlas.Atlas()
# xatlas 按表面积平均分贴图面积：两半盔壳面积大但明暗平缓，骷髅 / 狮子金边 / 铆钉面积小但细节多（不加权时骷髅只分到 3%）
# → 展开前把细节多的部件放大（面积按平方涨），等于给它们更高的贴图密度
DENSITY = {2: 1.8, 4: 2.0, 5: 1.6, 6: 1.6}
for k in ks: atlas.add_mesh((d[f'v{k}'] * DENSITY.get(k, 1.0)).astype(np.float32), d[f'f{k}'].astype(np.uint32))
co = xatlas.ChartOptions(); co.max_iterations = 4; co.normal_deviation_weight = 1.0; co.roundness_weight = 0.02; co.straightness_weight = 4.0; co.normal_seam_weight = 2.0
po = xatlas.PackOptions(); po.resolution = 2048; po.padding = 6; po.bilinear = True; po.blockAlign = False; po.bruteForce = False
atlas.generate(co, po)
print('charts', atlas.chart_count, 'atlas', atlas.width, atlas.height, 'util', round(float(atlas.utilization), 3))
out = {}
for i, k in enumerate(ks):
    vmap, idx, uvs = atlas[i]
    assert len(idx) == len(d[f'f{k}']), (k, len(idx), len(d[f'f{k}']))
    assert (vmap[idx] == d[f'f{k}']).all(), f'part{k} 三角形顺序变了'
    t = uvs[idx].astype(np.float32)                      # T×3×2，0–1
    a = 0.5 * np.abs((t[:, 1, 0] - t[:, 0, 0]) * (t[:, 2, 1] - t[:, 0, 1]) - (t[:, 2, 0] - t[:, 0, 0]) * (t[:, 1, 1] - t[:, 0, 1]))
    print(f'part{k}: 三角形 {len(t)}，UV 总面积 {a.sum():.3f}，范围 {t.min():.3f}–{t.max():.3f}')
    out[f'uv{k}'] = t
np.savez('/tmp/h13/newuv.npz', **out)
print('total uv area', round(sum(float((0.5 * np.abs((t[:, 1, 0] - t[:, 0, 0]) * (t[:, 2, 1] - t[:, 0, 1]) - (t[:, 2, 0] - t[:, 0, 0]) * (t[:, 1, 1] - t[:, 0, 1]))).sum()) for t in out.values()), 3))
