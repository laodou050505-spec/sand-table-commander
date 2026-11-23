"""拼合双面战盔：骑士半盔（y<0.03）+ 兽人半盔（y>0.03，已放大 1.3 倍）+ 旧战盔顶上的金狮 → 焊接合并 → 导出给 xatlas。"""
import bpy, bmesh, mathutils, numpy as np
from mathutils.bvhtree import BVHTree
W = '/Users/ld/Documents/ChatGPT/WarTable/ArtSource/ModelsP2'
bpy.ops.wm.open_mainfile(filepath='/tmp/helm2/combined.blend')
for o in list(bpy.data.objects):
    if o.type != 'MESH': bpy.data.objects.remove(o)
K = bpy.data.objects['Knight']; O = bpy.data.objects['Orc']

# 狮子：从旧战盔切下 z>0.31 的部分
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=f'{W}/Cover_SplitHelm/repaired.glb')
new = [o for o in bpy.data.objects if o not in before]
L = [o for o in new if o.type == 'MESH'][0]
for o in new:
    if o != L: bpy.data.objects.remove(o)
L.name = 'Lion'
bpy.ops.object.select_all(action='DESELECT'); L.select_set(True); bpy.context.view_layer.objects.active = L
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bm = bmesh.new(); bm.from_mesh(L.data)
bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0.31), plane_no=(0, 0, 1), clear_inner=True)
bm.to_mesh(L.data); bm.free()
vs = [L.matrix_world @ v.co for v in L.data.vertices]
lb = mathutils.Vector((sum(v.x for v in vs) / len(vs), sum(v.y for v in vs) / len(vs), min(v.z for v in vs)))
lh = max(v.z for v in vs) - lb.z
print('LION verts', len(vs), 'base', lb, 'h', round(lh, 3))

# 新盔顶：中缝附近最高点
dg = bpy.context.evaluated_depsgraph_get()
top = None
for o in (K, O):
    b = BVHTree.FromObject(o, dg)
    for x in np.linspace(-0.15, 0.15, 31):
        h = b.ray_cast(mathutils.Vector((x, 0.03, 3)), mathutils.Vector((0, 0, -1)))
        if h[0] and (top is None or h[0].z > top.z): top = h[0].copy()
print('TOP', top)
S = 1.15
L.scale = (S, S, S); bpy.ops.object.transform_apply(scale=True)
L.location = mathutils.Vector((top.x, 0.03, top.z - 0.02)) - lb * S
bpy.ops.object.transform_apply(location=True)

# 每件先焊接，再合并
for o in (K, O, L):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    o.data.uv_layers[0].name = 'SrcUV'
    while len(o.data.uv_layers) > 1: o.data.uv_layers.remove(o.data.uv_layers[1])
    for m in o.data.materials:
        if m: m.name = o.name + '_src'
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=1e-5); bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
for o in (K, O, L): o.select_set(True)
bpy.context.view_layer.objects.active = K; bpy.ops.object.join()
H = bpy.context.active_object; H.name = 'Helm'
me = H.data
print('JOINED', len(me.vertices), len(me.polygons), [m.name for m in me.materials if m])
v = np.array([x.co[:] for x in me.vertices], np.float32); f = np.array([p.vertices[:] for p in me.polygons], np.uint32)
np.savez('/tmp/helm/mesh.npz', v=v, f=f)
bb = [mathutils.Vector(c) for c in H.bound_box]
print('BB', [round(min(q[i] for q in bb), 3) for i in range(3)], [round(max(q[i] for q in bb), 3) for i in range(3)])
bpy.ops.wm.save_as_mainfile(filepath='/tmp/helm/weld.blend')
