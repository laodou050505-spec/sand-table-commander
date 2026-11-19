"""把模型坏掉的一侧换成好的一侧的镜像（在 Blender 里运行）。11-13：集装箱（两只摞着）的 Tripo 原始模型背面有个破口，正面是好的。
  Blender -b -P Tools/p2_mirror_half.py -- <in.glb> <out.glb> <轴 x|y> <留哪一侧 +|-> [按高度分几段=1]
做法：按高度分成几段（两只箱子各一段——上面那只是错开摆的，各自绕自己的中线镜像才不会变宽变窄），每段沿“自己包围盒的中线”切开，
删掉坏的一侧，把好的一侧复制一份镜像过去（UV 跟着复制，贴图也就镜像了），接缝处合并顶点。坐标是 Blender 的（Z 朝上）。"""
import bpy, bmesh, sys
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
src, dst, axis, keep = a[0], a[1], 'xy'.index(a[2]), 1.0 if a[3] == '+' else -1.0
parts = int(a[4]) if len(a) > 4 else 1
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in obs: o.select_set(True)
bpy.context.view_layer.objects.active = obs[0]
if len(obs) > 1: bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bm = bmesh.new(); bm.from_mesh(ob.data)
zs = [v.co.z for v in bm.verts]; z0, z1 = min(zs), max(zs)
no = Vector((0, 0, 0)); no[axis] = keep
for k in range(parts):
    za, zb = z0 + (z1 - z0) * k / parts, z0 + (z1 - z0) * (k + 1) / parts
    faces = [f for f in bm.faces if za <= f.calc_center_median().z < zb or (k == parts - 1 and f.calc_center_median().z >= zb)]
    vs = {v for f in faces for v in f.verts}
    c = (min(v.co[axis] for v in vs) + max(v.co[axis] for v in vs)) / 2
    co = Vector((0, 0, 0)); co[axis] = c
    geom = list(vs) + list({e for f in faces for e in f.edges}) + faces
    res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=co, plane_no=no, clear_inner=True)   # 删掉法线反方向（坏）的一侧
    kept = [g for g in res['geom'] if isinstance(g, bmesh.types.BMFace) and g.is_valid]
    kept += [f for f in faces if f.is_valid and f not in kept]
    kept = [f for f in set(kept) if f.calc_center_median()[axis] * keep >= c * keep - 1e-6]
    dup = bmesh.ops.duplicate(bm, geom=kept)
    nf = [g for g in dup['geom'] if isinstance(g, bmesh.types.BMFace)]; nv = {v for f in nf for v in f.verts}
    for v in nv: v.co[axis] = 2 * c - v.co[axis]
    bmesh.ops.reverse_faces(bm, faces=nf)
    print(f'MIRROR 段 {k}: 高度 {za:.3f}–{zb:.3f} 中线 {c:.4f} 留下 {len(kept)} 面', flush=True)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.to_mesh(ob.data); bm.free()
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=dst, use_selection=True, export_format='GLB')
