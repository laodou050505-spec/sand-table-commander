"""从水密修复版导出 LOD（Blender 里运行）：Blender -b -P Tools/p2_lods.py -- <repaired.glb> <输出目录>
lod0 原样，lod1/2/3 = 40% / 15% / 5%（Decimate Collapse，保持 UV）。每级导出前审计（合并重复点后）：
边界边、非流形边必须为 0；不合格就把减面比例微调后重试（最多 6 次）。贴图另存 albedo.png（≤2048），并记录锐度。
输出一行：LODS {"lods": {"lod0": {faces, boundary, nonmanifold}, ...}, "texture": "..."}"""
import bpy, bmesh, json, math, os, sys
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
HARD = float(argv[2]) if len(argv) > 2 else 0.0   # 硬边角度（度），0 = 照旧按 43° 自动平滑
os.makedirs(out, exist_ok=True)
RATIOS = [('lod0', 1.0), ('lod1', 0.4), ('lod2', 0.15), ('lod3', 0.05)]


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in ms: o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # glTF 导入会在 UV 接缝处拆开顶点；不先焊上，减面时接缝两侧各自塌缩就会裂开（UV 存在面角上，焊接不影响贴图）
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max(o.dimensions) * 1e-6)
    bm.to_mesh(o.data); bm.free()
    return o


def audit(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max(o.dimensions) * 1e-6)
    bd = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    nm = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    nf = len(bm.faces); bm.free()
    return {'faces': nf, 'boundary': bd, 'nonmanifold': nm}


report, tex = {}, None
def build(r):
    o = load()
    if r < 1:
        m = o.modifiers.new('dec', 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = r; m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name)
    return o, audit(o)


for name, ratio in RATIOS:
    tries = [ratio] if ratio == 1 else [ratio * (1 + 0.07 * ((k + 1) // 2) * (1 if k % 2 else -1)) for k in range(6)]
    best_r, best_bad, o, a = None, None, None, None
    for r in tries:
        o, a = build(r)
        bad = a['boundary'] + a['nonmanifold']
        if bad == 0:
            break
        if best_bad is None or bad < best_bad:
            best_r, best_bad = r, bad
        o = None
    if o is None:           # 没有一次完全合格：用问题最少的比例重建（报告里会显示 FAIL）
        o, a = build(best_r)
    if HARD > 0:
        # 硬表面件（specs 里 "hard": 角度）：硬边 + 面积加权法线，平板看起来是平的（11-13 深空的钻井架 / 棚屋：只按角度平滑时，
        # 减面后大小不一的三角把平板法线拉歪，看着皱巴巴）
        for p in o.data.polygons: p.use_smooth = True
        o.data.set_sharp_from_angle(angle=math.radians(HARD))
        wm = o.modifiers.new('wn', 'WEIGHTED_NORMAL'); wm.keep_sharp = True; wm.weight = 100; wm.mode = 'FACE_AREA'
    else:
        try:
            bpy.ops.object.shade_auto_smooth(angle=0.75)
        except Exception:
            bpy.ops.object.shade_smooth()
    if name == 'lod0':
        for mat in o.data.materials:
            if mat and mat.use_nodes:
                for node in mat.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image:
                        img = node.image
                        if max(img.size) > 2048: img.scale(min(img.size[0], 2048), min(img.size[1], 2048))
                        img.filepath_raw = os.path.join(out, 'albedo.png'); img.file_format = 'PNG'; img.save()
                        try:
                            import numpy as np
                            px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)[..., :3].mean(-1)
                            lap = px[1:-1, 1:-1] * 4 - px[:-2, 1:-1] - px[2:, 1:-1] - px[1:-1, :-2] - px[1:-1, 2:]
                            tex = f'{img.size[0]}x{img.size[1]} sharp={float(lap.var()) * 1e4:.2f}'
                        except Exception:
                            tex = f'{img.size[0]}x{img.size[1]}'
                        break
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + '.fbx'), use_selection=True, path_mode='STRIP', embed_textures=False,
                             mesh_smooth_type='OFF', apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=True, add_leaf_bones=False)
    report[name] = a
print('LODS ' + json.dumps({'lods': report, 'texture': tex}))
