import numpy as np, xatlas
d = np.load('/tmp/helm/mesh.npz'); v, f = d['v'], d['f']
atlas = xatlas.Atlas(); atlas.add_mesh(v, f)
co = xatlas.ChartOptions(); co.max_iterations = 4; co.normal_deviation_weight = 1.0; co.roundness_weight = 0.02; co.straightness_weight = 4.0; co.normal_seam_weight = 2.0
po = xatlas.PackOptions(); po.resolution = 2048; po.padding = 6; po.bilinear = True; po.blockAlign = False; po.bruteForce = False
atlas.generate(co, po)
vmap, idx, uvs = atlas[0]
print('charts', atlas.chart_count, 'atlas', atlas.width, atlas.height, 'util', atlas.utilization)
np.save('/tmp/helm/loop_uv.npy', uvs[idx].reshape(-1, 2).astype(np.float32))
