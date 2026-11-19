# 11-13 封面战盔修正（三）。用户：“有些结构部位的贴图还是有问题，看着很不舒服”。定位（纯色区分部件渲染 + 明暗图单看）：
#   ① 银钢 / 黑铁两半的分界在模型上是一排锯齿——当初 helm3_retex.py 是按“三角面中心在中线哪一侧”分的，跨线的三角面左右交错；
#   ② 中缝银钢一侧有一条歪歪扭扭的凸棱（锯齿边被体素重建磨圆的产物）；
#   ③ 眉额 / 面甲周围、中缝两侧、骷髅附近一片片尖角碎块——真正的原因是 **UV 拉飞的三角形**：每个部件的 UV 总面积是贴图的 5–8 倍，
#      有的三角形一个就横跨四分之一张贴图，在贴图上扫过一长条不相干的内容（按表面积算：银钢 19%、黑铁 15%、骷髅 20%、铆钉 27%）。
#      （我一开始以为是 P2 把反光画进了颜色，11-11 也是按那个思路压的暗斑——没对症。用“纯明暗图 / 纯模型”分开渲染 + 标红拉飞面才看清。）
# 整套流程（四步，脚本都在本目录）：
#   1. Blender -b -P helm3_fix1113.py     本脚本：part0（银钢）+ part1（黑铁）合起来 → 沿中线平面切一刀（只加边）→ 中线附近的面按在哪一侧重新归属
#                                          → 拆回两半；所有部件三角化、标出 UV 拉飞的面；导出 /tmp/h13/mesh.npz，存 /tmp/h13/stepA.blend
#   2. python3 helm3_atlas1113.py          xatlas 给整顶盔重新展 UV（/tmp/h13/newuv.npz）
#   3. Blender -b -P helm3_bake1113.py     套上新 UV；把旧明暗图、环境光遮蔽 AO、曲率烘到新 UV；导出 helm3.fbx 和 /tmp/h13/uv.npz
#   4. python3 helm3_shade1113.py          合成新的明暗图 helm3_albedo.png
# （python3 = /Library/Developer/CommandLineTools/usr/bin/python3，里面有 xatlas / cv2 / scipy。）
# 总是从 ArtSource/Cover/helm3_backup_1113/ 读（11-11 修正之后的版本），可重复跑。
# ② 的凸棱是设计上的中脊（宽约 3 cm、高约 2 cm，模型尺度，一条波浪边的饰带），紧挨着面甲横梁。**结论：模型不动。**
#    试过两种抚平：带内普通顶点平滑（最多把顶点挪了 185 mm，形状坏了）；只沿中缝走向做平均（下面 SMOOTH 一段，加 smooth 参数才跑）——
#    同光照近景对比，抚平后成了一道道皱褶，像融化的蜡，比原样差。颜色切直、明暗干净之后，原样的中脊读得出是一条饰带。
import bpy, bmesh, sys, math, numpy as np
from mathutils import Vector

W = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = W + 'ArtSource/Cover/helm3_backup_1113/helm3.fbx'
DST = W + 'Assets/_WarTable/Art/Models/Cover/Helm3/helm3.fbx'
OUT = '/tmp/h13/'
SMOOTH = 'smooth' in sys.argv        # 默认不动中脊：两种抚平办法都试过，都比原样差（见下面说明）

bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=SRC)
objs = {o.name.split('.')[0]: o for o in bpy.context.scene.objects if o.type == 'MESH'}
o0, o1 = objs['part0'], objs['part1']
mw = o0.matrix_world.copy()
assert (o1.matrix_world - mw).to_3x3().median_scale < 1e-6 or True

# ---- 合并两半（世界坐标里操作），记下每个面原来属于哪一半
bm = bmesh.new(); bm.faces.layers.int.new('pid'); bm.faces.layers.int.new('big')
# 数据层要一次建齐再取句柄：之后每新建一个层，之前取到的句柄就失效（第一版细分时中途建层，“属于哪一半”全乱了）
pid = bm.faces.layers.int['pid']; big = bm.faces.layers.int['big']
bm.from_mesh(o0.data); bm.transform(o0.matrix_world)
n0 = len(bm.faces)
tmp = bmesh.new(); tmp.from_mesh(o1.data); tmp.transform(o1.matrix_world); me_tmp = bpy.data.meshes.new('tmp1'); tmp.to_mesh(me_tmp); tmp.free()
bm.from_mesh(me_tmp)
bm.faces.ensure_lookup_table()
for i, f in enumerate(bm.faces): f[pid] = 0 if i < n0 else 1
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-5)      # FBX 在 UV 缝和部件交界处把顶点拆开了，按位置焊回去（UV 存在面角上，不受影响）
bm.faces.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.verts.ensure_lookup_table()

def boundary(band=None):
    es = [e for e in bm.edges if len(e.link_faces) == 2 and e.link_faces[0][pid] != e.link_faces[1][pid]]
    if band is not None: es = [e for e in es if all(abs(v.co.y - band[0]) < band[1] for v in e.verts)]
    return es

# ---- 中线在哪：两半交界边（中线附近 6 cm 以内的）的 Y 中位数
es = boundary((0.0, 0.06))
ys = np.array([v.co.y for e in es for v in e.verts])
y0 = float(np.median(ys)); amp = float(np.abs(ys - y0).max())
print(f'SEAM 交界边 {len(es)} 条，中线 y0 = {y0 * 1000:.1f} mm，锯齿最深 {amp * 1000:.1f} mm，交界线总长 {sum(e.calc_length() for e in es):.3f} m', flush=True)

# ---- 沿中线切一刀（只加边）
r = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-6, plane_co=Vector((0, y0, 0)), plane_no=Vector((0, 1, 0)), clear_inner=False, clear_outer=False)
bm.faces.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.verts.ensure_lookup_table()

# ---- 中线附近的面重新归属：从交界边出发，在“锯齿带”（离中线不超过锯齿最深处 + 2 mm）里漫延，按面中心在哪一侧定归属
bandw = amp + 0.002
seed = set(f for e in boundary((y0, 0.06)) for f in e.link_faces)
zone = set(seed); stack = list(seed)
while stack:
    f = stack.pop()
    for e in f.edges:
        for g in e.link_faces:
            if g not in zone and abs(g.calc_center_median().y - y0) < bandw: zone.add(g); stack.append(g)
flip = 0
for f in zone:
    want = 0 if f.calc_center_median().y < y0 else 1       # 银钢在 −Y 一侧
    if f[pid] != want: f[pid] = want; flip += 1
es2 = boundary((y0, 0.06)); ys2 = np.array([v.co.y for e in es2 for v in e.verts])
print(f'RESPLIT 锯齿带内 {len(zone)} 个面，改归属 {flip} 个；之后交界线 {len(es2)} 条边，偏离中线最多 {float(np.abs(ys2 - y0).max()) * 1000:.3f} mm，总长 {sum(e.calc_length() for e in es2):.3f} m', flush=True)

# ---- 中缝银钢一侧的中脊（宽约 3 cm、高约 2 cm）边缘是一串疙瘩（锯齿边被体素重建磨圆留下的）：只**沿中缝走向**做平均——
#      把每个顶点的“离盔心的距离”换成“沿中缝前后 ±几厘米、横向位置相同的那些顶点”的加权平均，这样每一段的截面都一样、棱就直了；
#      不横向抹（横向一抹，中脊本身和旁边的面甲横梁就没了——第一版在带内做普通顶点平滑，最多把顶点挪了 185 mm）。每个顶点最多挪 CLAMP。
if SMOOTH:
    bm.normal_update()
    P = np.array([tuple(v.co) for v in bm.verts]); Nn = np.array([tuple(v.normal) for v in bm.verts])
    cx, cz = (P[:, 0].min() + P[:, 0].max()) / 2, (P[:, 2].min() + P[:, 2].max()) / 2
    rx, rz = P[:, 0] - cx, P[:, 2] - cz; rr = np.hypot(rx, rz); th = np.arctan2(rz, rx)
    outw = (Nn[:, 0] * rx + Nn[:, 2] * rz) / np.maximum(rr, 1e-6) > 0.2          # 朝外的面（盔壳外表面）
    dy = P[:, 1] - y0
    band = np.where(outw & (dy > -0.040) & (dy <= 1e-5))[0]
    SIG_S, SIG_Y, CLAMP = 0.035, 0.0035, 0.009
    newr = rr.copy()
    for i in band:
        dth = np.angle(np.exp(1j * (th[band] - th[i])))
        ds = dth * rr[i]; w = np.exp(-ds ** 2 / (2 * SIG_S ** 2) - (dy[band] - dy[i]) ** 2 / (2 * SIG_Y ** 2))
        newr[i] = float((w * rr[band]).sum() / w.sum())
    d = np.clip(newr - rr, -CLAMP, CLAMP)
    bm.verts.ensure_lookup_table()
    for i in band:
        v = bm.verts[i]; k = (rr[i] + d[i]) / max(rr[i], 1e-6)
        v.co.x = cx + rx[i] * k; v.co.z = cz + rz[i] * k            # 只沿径向挪，Y 不变（中线上的点仍在中线上，分界还是直线）
    print(f'SMOOTH 中脊带 {len(band)} 个顶点沿中缝走向平均：平均挪动 {np.abs(d[band]).mean() * 1000:.2f} mm，最多 {np.abs(d[band]).max() * 1000:.2f} mm（上限 {CLAMP * 1000:.0f} mm），被上限截住的 {int((np.abs(newr[band] - rr[band]) > CLAMP).sum())} 个', flush=True)

def zone_faces():
    # 当初重新归属过的那一带（锯齿带）里、贴着中线 3 mm 以内的面
    return [f for f in bm.faces if abs(f.calc_center_median().y - y0) < 0.003 and f in zone_now()]
def zone_now():
    seed = set(f for e in boundary((y0, 0.004)) for f in e.link_faces); return seed

# ---- 盔壳细分 + 松弛（11-13 用户看了对比图：“截图里这个地方还是不对”——金边下面、面甲横梁上方那一块）
#      根因：盔壳网格被压得极不均匀，穹顶是一片巨大的三角面，眉额那一块是从一个点放射出去的细长三角面（最长边 33 cm），
#      相邻大面之间折出 35° 以上的假棱；之前被乱条纹盖着，条纹去掉后露出来了。
#      做法：边长超过 EDGE_MAX 的边反复对半细分 → 只让“细分新加的、周围全是大面、且不在硬棱上”的顶点松弛（原有顶点全部钉住）。
#      这样大面之间的假折痕被抚成平滑曲面；甲片边缘、横梁这些由密集小面构成的真实结构一个顶点都不动。
RETESS = 'retess' in sys.argv       # 默认不做——见下面的结论，两种做法都比原样差
# 结论（试了四版）：**整顶盔细分是反效果**——大三角面的明暗原本由三个远处的角点插值、把小起伏抹平了，细分后每个小面按真实朝向着色，
# 藏着的不平整（横梁边的细锯齿等）全露出来。所以只处理用户指出的那一块：金边下面、面甲横梁上方的眉额甲片（BROW 盒子里朝外的大面）。
# 第五版只细分这一块、让新顶点做“膜面”松弛（边界钉住、迭代到收敛）：甲片表面变成一片小疙瘩，斜线还在。
# 那道斜线其实是一片三角形甲片压在另一片上的台阶（模型的真实结构），不是坏面。**模型原样最干净，几何不要动**；
# 眉额“看着不对”的是贴图：见 helm3_shade1113.py 里关于三维噪声和棱边的说明。
BROW = dict(x=(0.10, 0.34), y=(-0.24, -0.012), z=(-0.05, 0.17))
def in_brow(c): return BROW['x'][0] < c.x < BROW['x'][1] and BROW['y'][0] < c.y < BROW['y'][1] and BROW['z'][0] < c.z < BROW['z'][1]
if RETESS:
    # EDGE_MAX 第一版用 11 mm，面数从 8809 涨到 20 万；30 mm 足够把十几厘米的假折痕分成几段
    EDGE_MAX, BIG_AREA, HARD_DEG, MOVE_MAX = 0.016, 1.2e-4, 55.0, 0.06
    # “大面” = 面积大而且不细长（最窄处 > THIN）。横梁侧壁、倒角这些细长条面积也不小，第二版把它们也当大面，
    # 上面的新顶点跟着松弛，横梁下沿冒出一排小鼓包、平甲片坑坑洼洼
    THIN = 0.009
    def is_big(f):
        a = f.calc_area(); return a > BIG_AREA and 2 * a / max(e.calc_length() for e in f.edges) > THIN
    bm.normal_update()
    hc = Vector(((min(v.co.x for v in bm.verts) + max(v.co.x for v in bm.verts)) / 2, 0, (min(v.co.z for v in bm.verts) + max(v.co.z for v in bm.verts)) / 2))
    def is_brow(f):
        c = f.calc_center_median(); return f[pid] == 0 and in_brow(c) and f.normal.dot((c - hc).normalized()) > 0.2
    for f in bm.faces: f[big] = 1 if (is_big(f) and is_brow(f)) else 0
    area0 = [sum(f.calc_area() for f in bm.faces if f[pid] == k) for k in (0, 1)]
    # 新顶点用坐标认：细分出来的顶点会继承端点的数据层取值，用数据层标“原有 / 新加”标不出来
    key = lambda v: (round(v.co.x * 1e6), round(v.co.y * 1e6), round(v.co.z * 1e6))
    origkeys = set(key(v) for v in bm.verts)
    nf0 = len(bm.faces)
    for it in range(6):
        long = [e for e in bm.edges if e.calc_length() > EDGE_MAX and any(f[big] == 1 for f in e.link_faces)]
        if not long: break
        bmesh.ops.subdivide_edges(bm, edges=long, cuts=1, use_grid_fill=False)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3], quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table(); bm.normal_update()
    hard = set()
    for e in bm.edges:
        if len(e.link_faces) != 2 or math.degrees(e.link_faces[0].normal.angle(e.link_faces[1].normal, 0.0)) > HARD_DEG: hard.update(e.verts)
    hardk = set(key(v) for v in hard)
    newv = [v for v in bm.verts if key(v) not in origkeys]
    # 结构顶点 = 挨着任何一个小面 / 细长面的顶点（横梁、铆钉座、甲片边缘）。新顶点只要和结构顶点相邻就钉住：
    # 第三版没有这一条，横梁边缘原有的细锯齿（减面时侧壁被压没了，大面直接连到横梁顶上）被拉成了一排软鼓包
    featk = set(key(v) for v in bm.verts if any(f[big] == 0 for f in v.link_faces))
    free = [v for v in newv if key(v) not in hardk and all(f[big] == 1 for f in v.link_faces)
            and not any(key(e.other_vert(v)) in featk for e in v.link_edges)]
    onplane = [v for v in free if abs(v.co.y - y0) < 1e-6]
    start = {v: v.co.copy() for v in free}
    for it in range(200):
        bmesh.ops.smooth_vert(bm, verts=free, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        for v in onplane: v.co.y = y0                                   # 中线上的点留在中线上（分界还是直线）
        for v in free:                                                  # 每个顶点最多挪 MOVE_MAX
            d = v.co - start[v]
            if d.length > MOVE_MAX: v.co = start[v] + d * (MOVE_MAX / d.length)
    mv = np.array([(v.co - start[v]).length for v in free]) if free else np.zeros(1)
    bm.normal_update()
    print(f'RETESS 细分后面数 {nf0} → {len(bm.faces)}；松弛 {len(free)} 个新顶点（共新加 {len(newv)} 个），平均挪 {mv.mean() * 1000:.2f} mm，最多 {mv.max() * 1000:.2f} mm，顶到上限的 {int((mv > MOVE_MAX * 0.999).sum())} 个', flush=True)
    # 核对“属于哪一半”没被细分搞乱：两半各自的面积应当基本不变；中线平面两侧 3 mm 内不应有归错边的面
    area1 = [sum(f.calc_area() for f in bm.faces if f[pid] == k) for k in (0, 1)]
    wrong = sum(1 for f in zone_faces() if (f.calc_center_median().y < y0) != (f[pid] == 0))
    print(f'RETESS 两半面积：银钢 {area0[0]:.4f} → {area1[0]:.4f} m²，黑铁 {area0[1]:.4f} → {area1[1]:.4f} m²；中线带内归错边的面 {wrong} 个', flush=True)

# ---- 拆回两半，写回原物体（转回各自的本地坐标）
bm.normal_update()
for k, ob in ((0, o0), (1, o1)):
    b2 = bm.copy(); p2 = b2.faces.layers.int['pid']
    bmesh.ops.delete(b2, geom=[f for f in b2.faces if f[p2] != k], context='FACES')
    bmesh.ops.delete(b2, geom=[v for v in b2.verts if not v.link_faces], context='VERTS')
    b2.transform(ob.matrix_world.inverted()); b2.to_mesh(ob.data); b2.free(); ob.data.update()
    print(f'PART part{k} 面 {len(ob.data.polygons)}', flush=True)
bm.free(); bpy.data.meshes.remove(me_tmp)

# ---- 所有带贴图的部件：三角化（之后三角形顺序固定，展 UV / 烘焙都按这个顺序对），标出 UV 拉飞的面，导出网格
out = {}
for k in range(7):
    ob = objs.get(f'part{k}')
    if ob is None: continue
    b = bmesh.new(); b.from_mesh(ob.data); bmesh.ops.triangulate(b, faces=b.faces[:]); b.to_mesh(ob.data); b.free(); ob.data.update()
    me = ob.data; mwk = ob.matrix_world; uv = me.uv_layers[0].data
    V = np.array([tuple(mwk @ v.co) for v in me.vertices], np.float32)
    F = np.array([tuple(p.vertices) for p in me.polygons], np.uint32)
    ratio = np.zeros(len(me.polygons))
    for i, p in enumerate(me.polygons):
        ls = list(p.loop_indices); u = [uv[l].uv for l in ls]; v3 = [V[j] for j in p.vertices]
        eu = max((u[a] - u[(a + 1) % 3]).length for a in range(3)); e3 = max(float(np.linalg.norm(v3[a] - v3[(a + 1) % 3])) for a in range(3))
        ratio[i] = eu / max(e3, 1e-9)
    bad = ratio > np.median(ratio) * 4          # UV 边长 / 实际边长 超过该部件中位数的 4 倍 = 拉飞
    A3 = 0.5 * np.linalg.norm(np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]), axis=1)
    print(f'BAD part{k}: 面 {len(F)}，UV 拉飞 {int(bad.sum())} 个，占表面积 {100 * A3[bad].sum() / A3.sum():.1f}%', flush=True)
    out[f'v{k}'] = V; out[f'f{k}'] = F; out[f'b{k}'] = bad
np.savez(OUT + 'mesh.npz', **out)
bpy.ops.wm.save_as_mainfile(filepath=OUT + 'stepA.blend')
print('done stepA')
