"""拓扑修复（在 Blender 里运行）：
  Blender -b -P Tools/blender_repair.py -- <model.glb> <out.glb> <面数上限> [体素比例=0.0028] [贴图=2048]

Tripo 模型常见问题：开放边界（眼洞/嘴洞只有单层壳、漏面）、非流形边、法线翻转、内部碎片。
修复流程（保证结果是封闭流形，从任何角度看都没有露面/破面）：
  1. 合并为一个物体，复制一份作为“烘焙源”
  2. 向内 Solidify（厚度 = 1.5 体素）：单层壳和开口处长出厚度，体素化时不会丢失或穿孔
  3. Voxel Remesh（体素 = 包围盒最长边 × 比例）：输出必然水密、无自交、法线一致
  4. 删除小于 0.5% 面数的连通块（内部气泡/碎屑）
  5. Decimate 到面数上限，再三角化
  6. Smart UV 展开，从原模型烘焙 Base Color 到新贴图（Cycles，选中到活动）
  7. 审计：边界边、非流形、零面积、翻转面必须全为 0，否则退出码 2
"""
import bpy, bmesh, sys, os, json, math
argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, cap = argv[0], argv[1], int(argv[2])
vox_ratio = float(argv[3]) if len(argv) > 3 else 0.0028
tex_res = int(argv[4]) if len(argv) > 4 else 2048
bake_mode = argv[5] if len(argv) > 5 else 'ray'   # 'nearest' = 不打光线，按“离原模型表面最近的点”取色（见下面 nearest_bake）
hard = float(argv[6]) if len(argv) > 6 else 0.0   # > 0：硬表面件——夹角大于这么多度的边标成硬边，法线按面积加权（平板看起来是平的）。
# （11-13 试过减面前先“平面合并”共面三角：钻井架平台被并坏、出现开放边，比不做更糟，别再试）

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
srcobj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != srcobj:
        bpy.data.objects.remove(o, do_unlink=True)
srcobj.name = 'bake_src'
dims = srcobj.dimensions
vox = max(dims) * vox_ratio
src_dims = tuple(dims)
def stage(tag, ob):
    # 每一步的面数和包围盒（11-13：岗楼 / 集装箱在修复里被吃得只剩碎片，报告却写通过 → 留痕 + 末尾校验尺寸）
    bpy.context.view_layer.update()
    print('STAGE', tag, len(ob.data.polygons), [round(v, 4) for v in ob.dimensions], flush=True)

# ---- 新物体：几何重建 ----
new = srcobj.copy(); new.data = srcobj.data.copy(); new.name = 'repaired'
bpy.context.scene.collection.objects.link(new)
bpy.ops.object.select_all(action='DESELECT'); new.select_set(True); bpy.context.view_layer.objects.active = new
bm = bmesh.new(); bm.from_mesh(new.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=vox * 0.05)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(new.data); bm.free()
m = new.modifiers.new('sol', 'SOLIDIFY'); m.thickness = vox * 1.5; m.offset = -1.0; m.use_even_offset = False; m.use_quality_normals = True
bpy.ops.object.modifier_apply(modifier=m.name)
m = new.modifiers.new('vox', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = vox; m.adaptivity = 0.0; m.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier=m.name)

def strip_islands(ob, frac):
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    seen = set(); comps = []
    for f in bm.faces:
        if f.index in seen: continue
        st = [f]; seen.add(f.index); c = []
        while st:
            g = st.pop(); c.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        comps.append(c)
    nf = len(bm.faces); kill = [f for c in comps if len(c) < nf * frac for f in c]
    if kill: bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(ob.data); bm.free()
    return len(comps), len(kill)
stage('remesh', new)
islands, killed = strip_islands(new, 0.005)
stage('strip', new)
faces_remesh = len(new.data.polygons)
if faces_remesh > cap:
    m = new.modifiers.new('dec', 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = cap / (faces_remesh * 2.0); m.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=m.name)
m = new.modifiers.new('tri', 'TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=m.name)

def clean(ob, sides=64):
    # 减面后偶发的非流形/零面积：删退化、再封小孔、重算法线
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=vox * 0.02)
    bmesh.ops.dissolve_degenerate(bm, dist=vox * 0.01, edges=bm.edges)
    bad = [e for e in bm.edges if len(e.link_faces) > 2]
    if bad:
        bmesh.ops.delete(bm, geom=list({f for e in bad for f in e.link_faces}), context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.holes_fill(bm, edges=bm.edges, sides=sides)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
def kill_spikes(ob):
    import statistics
    bm = bmesh.new(); bm.from_mesh(ob.data)
    med = statistics.median(e.calc_length() for e in bm.edges)
    # 尖刺 = 边特别长、而且又细又窄的三角。11-13：只看边长会把“大平面减面后留下的大三角”（集装箱壁、岗楼墙）也当成尖刺删掉，
    # 洞大到封不上 → 兜底删面一圈圈吃下去，整个模型只剩碎片。加上“面积 < 最长边² × 2%”这一条（细长条）才算
    # 还要“翘起来”（和邻面夹角 > 100°）：平墙上减面留下的细长三角和邻面共面，是正常的，不能删
    def spike(f):
        L = max(e.calc_length() for e in f.edges)
        if not (L > med * 30 and f.calc_area() < L * L * 0.02): return False
        return any(len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(100) for e in f.edges)
    bad = [f for f in bm.faces if spike(f)]
    if bad:
        bmesh.ops.delete(bm, geom=bad, context='FACES')
        bmesh.ops.holes_fill(bm, edges=bm.edges, sides=64)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free(); return len(bad)
stage('decimate', new)
kill_spikes(new)
stage('spikes', new)
def topo_ok(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    ok = all(len(e.link_faces) == 2 for e in bm.edges) and all(f.calc_area() > 1e-12 for f in bm.faces)
    bm.free(); return ok
for _ in range(3):
    # 11-13：已经干净就别再清理——清理里的“合并近点”会把集装箱波纹板这种贴得很近的两层壳粘在一起，反而造出非流形边，
    # 之后兜底删面一圈圈吃下去（集装箱从 2 万面删到 90 面，拓扑审计照样全 0）
    if topo_ok(new): break
    clean(new)
# 兜底：仍有问题的局部（多为减面塌缩处）→ 删掉问题边周围两圈面再封孔，最多 6 轮
for _ in range(6):
    if topo_ok(new):
        break
    bm = bmesh.new(); bm.from_mesh(new.data)
    bad = {v for e in bm.edges if len(e.link_faces) != 2 for v in e.verts}
    for _r in range(2):
        bad |= {w for v in list(bad) for e in v.link_edges for w in e.verts}
    fs = list({f for v in bad for f in v.link_faces})
    bmesh.ops.delete(bm, geom=fs, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(new.data); bm.free()
    clean(new, 0)   # 兜底删出来的洞常常超过 64 条边：不设上限全部封上，否则洞封不住、下一轮接着删，越删越少（11-13 岗楼 / 集装箱被删得只剩碎片）

stage('clean', new)
for p in new.data.polygons: p.use_smooth = True
if hard > 0:
    # 11-13 深空的钻井架 / 棚屋：全部平滑着色时，减面后大小不一的三角把平板的法线拉歪，看着皱巴巴（几何其实差不多是平的）。
    # 硬边 + 面积加权法线：大平面主导法线方向，棱上断开
    new.data.set_sharp_from_angle(angle=math.radians(hard))
    m = new.modifiers.new('wn', 'WEIGHTED_NORMAL'); m.keep_sharp = True; m.weight = 100; m.mode = 'FACE_AREA'
    bpy.ops.object.modifier_apply(modifier=m.name)
# ---- UV + 烘焙 ----
while new.data.uv_layers: new.data.uv_layers.remove(new.data.uv_layers[0])
new.data.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')
img = bpy.data.images.new('albedo', tex_res, tex_res, alpha=False)
mat = bpy.data.materials.new('repaired_mat'); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get('Principled BSDF')
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
new.data.materials.clear(); new.data.materials.append(mat)
nt.nodes.active = tn
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'METAL'; prefs.get_devices()
    for d in prefs.devices: d.use = True
    sc.cycles.device = 'GPU'
except Exception:
    pass
sc.render.bake.use_selected_to_active = True
sc.render.bake.cage_extrusion = vox * 6
sc.render.bake.max_ray_distance = vox * 30
sc.render.bake.margin = 12
def nearest_bake():
    """按最近点取色（11-13，集装箱）：Cycles 的“选中到活动”烘焙是从新表面沿法线打光线去找原模型，波纹板 + 减面后法线乱，
    光线从波纹缝里穿进去打到箱子内壁（灰白），外壁贴图被撕成一条条碎片、红漆带碎成像字母的小块。
    这里不打光线：每个贴图像素算出它在新表面上的三维位置 → 找原模型上离它最近的点 → 取那一点的原贴图颜色。
    新表面本来就贴着原模型的外表面（体素重建只往里加厚），最近点一定落在外壁上。"""
    import numpy as np
    from mathutils.bvhtree import BVHTree
    sme = srcobj.data; sme.calc_loop_triangles()
    sv = np.array([v.co[:] for v in sme.vertices], dtype=np.float64)
    suv_l = np.array([d.uv[:] for d in sme.uv_layers.active.data], dtype=np.float64)
    tris = [t for t in sme.loop_triangles]
    tv = np.array([t.vertices[:] for t in tris], dtype=np.int64); tl = np.array([t.loops[:] for t in tris], dtype=np.int64)
    tmat = np.array([t.material_index for t in tris], dtype=np.int64)
    bvh = BVHTree.FromPolygons([tuple(v) for v in sv], [tuple(t) for t in tv])
    # 原模型每个材质的 Base Color 贴图
    simgs = {}
    for mi, sl in enumerate(srcobj.material_slots):
        im = next((n.image for n in sl.material.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and any(l.to_socket.name == 'Base Color' for l in n.outputs[0].links)), None) if sl.material and sl.material.use_nodes else None
        if im is None and sl.material and sl.material.use_nodes: im = next((n.image for n in sl.material.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
        if im: simgs[mi] = np.array(im.pixels[:], dtype=np.float32).reshape(im.size[1], im.size[0], im.channels)[..., :3]
    nme = new.data; nme.calc_loop_triangles()
    nv = np.array([v.co[:] for v in nme.vertices], dtype=np.float64); nuv = np.array([d.uv[:] for d in nme.uv_layers.active.data], dtype=np.float64)
    R = tex_res; out = np.zeros((R, R, 3), dtype=np.float32); done = np.zeros((R, R), dtype=bool)
    P = []; XY = []
    for t in nme.loop_triangles:
        a, b, c = (nuv[l] * R for l in t.loops); pa, pb, pc = (nv[v] for v in t.vertices)
        x0, x1 = int(max(0, np.floor(min(a[0], b[0], c[0])) - 1)), int(min(R - 1, np.ceil(max(a[0], b[0], c[0])) + 1))
        y0, y1 = int(max(0, np.floor(min(a[1], b[1], c[1])) - 1)), int(min(R - 1, np.ceil(max(a[1], b[1], c[1])) + 1))
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        det = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(det) < 1e-12: continue
        w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / det
        w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / det
        w2 = 1 - w0 - w1
        inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)       # 稍微放宽一点，三角形边上的像素也算（边距靠后面的外扩）
        if not inside.any(): continue
        w0, w1, w2 = w0[inside], w1[inside], w2[inside]
        P.append(w0[:, None] * pa + w1[:, None] * pb + w2[:, None] * pc)
        XY.append(np.stack([xs[inside].astype(np.int64), ys[inside].astype(np.int64)], 1))
    P = np.concatenate(P); XY = np.concatenate(XY)
    loc = np.empty_like(P); fi = np.empty(len(P), dtype=np.int64)
    for i in range(len(P)):
        l, _n, f, _d = bvh.find_nearest(P[i].tolist())
        loc[i] = l; fi[i] = f
    A, B, C = sv[tv[fi, 0]], sv[tv[fi, 1]], sv[tv[fi, 2]]
    v0, v1, v2 = B - A, C - A, loc - A
    d00 = (v0 * v0).sum(1); d01 = (v0 * v1).sum(1); d11 = (v1 * v1).sum(1); d20 = (v2 * v0).sum(1); d21 = (v2 * v1).sum(1)
    den = np.maximum(d00 * d11 - d01 * d01, 1e-20)
    bv = np.clip((d11 * d20 - d01 * d21) / den, 0, 1); bw = np.clip((d00 * d21 - d01 * d20) / den, 0, 1); bu = np.clip(1 - bv - bw, 0, 1)
    uv = bu[:, None] * suv_l[tl[fi, 0]] + bv[:, None] * suv_l[tl[fi, 1]] + bw[:, None] * suv_l[tl[fi, 2]]
    col = np.zeros((len(P), 3), dtype=np.float32)
    for mi, px in simgs.items():
        sel = tmat[fi] == mi
        if not sel.any(): continue
        h, w = px.shape[:2]
        col[sel] = px[np.clip((uv[sel, 1] % 1.0 * h).astype(np.int64), 0, h - 1), np.clip((uv[sel, 0] % 1.0 * w).astype(np.int64), 0, w - 1)]
    out[XY[:, 1], XY[:, 0]] = col; done[XY[:, 1], XY[:, 0]] = True
    for _ in range(12):                                             # 往空白处外扩 12 像素（防接缝漏底色）
        acc = np.zeros_like(out); cnt = np.zeros((R, R), dtype=np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            acc += np.roll(out * done[..., None], (dy, dx), (0, 1)); cnt += np.roll(done, (dy, dx), (0, 1))
        grow = ~done & (cnt > 0); out[grow] = acc[grow] / cnt[grow][:, None]; done |= grow
    rgba = np.concatenate([out, np.ones((R, R, 1), dtype=np.float32)], -1)
    img.pixels.foreach_set(rgba.ravel())
    print('NEAREST_BAKE texels', len(P), flush=True)

if bake_mode == 'nearest':
    nearest_bake()
else:
    bpy.ops.object.select_all(action='DESELECT'); srcobj.select_set(True); new.select_set(True); bpy.context.view_layer.objects.active = new
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
png = os.path.splitext(dst)[0] + '_albedo.png'
img.filepath_raw = png; img.file_format = 'PNG'; img.save()
img.pack()

# ---- 审计 ----
bm = bmesh.new(); bm.from_mesh(new.data)
before = [f.normal.copy() for f in bm.faces]
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
rep = dict(faces=len(bm.faces), boundary=sum(1 for e in bm.edges if len(e.link_faces) == 1),
           nonmanifold=sum(1 for e in bm.edges if len(e.link_faces) > 2), zero=sum(1 for f in bm.faces if f.calc_area() < 1e-12),
           flipped=sum(1 for f, n in zip(bm.faces, before) if f.normal.dot(n) < 0), islands_remesh=islands, killed=killed,
           voxel=round(vox, 5), faces_remesh=faces_remesh)
bm.free()
# 尺寸校验：修复后的包围盒每个轴都不能比原模型小 8% 以上（薄壳被体素重建 / 兜底删面吃掉时，拓扑审计照样全 0）
bpy.context.view_layer.update()
rep['keep'] = round(min(n / max(s0, 1e-9) for n, s0 in zip(new.dimensions, src_dims)), 3)
rep['pass'] = rep['boundary'] == 0 and rep['nonmanifold'] == 0 and rep['zero'] == 0 and rep['flipped'] == 0 and rep['keep'] >= 0.92
bpy.data.objects.remove(srcobj, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT'); new.select_set(True)
bpy.ops.export_scene.gltf(filepath=dst, use_selection=True, export_format='GLB')
open(os.path.splitext(dst)[0] + '_repair.json', 'w').write(json.dumps(rep, ensure_ascii=False, indent=2))
print('REPAIR', json.dumps(rep, ensure_ascii=False))
sys.exit(0 if rep['pass'] else 2)
