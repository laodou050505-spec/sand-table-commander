"""标题机甲：在 Blender 里烘焙两张辅助贴图（按模型自带的 UV，4096²）：
  ao.png      环境光遮蔽（缝里、零件重叠处发暗 → 做“渍洗”脏污）
  curv.png    凸起程度（pointiness：棱边亮、凹处暗 → 做边缘掉漆 / 干扫高光）
  up.png      朝上程度（法线 z：朝上的面积灰）
  height.png  高度（越低越脏：脚和小腿蒙尘）
用法：Blender -b --python Tools/titan_bake.py -- <带贴图的模型.fbx> <输出目录> [分辨率]"""
import bpy, sys
argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]
RES = int(argv[2]) if len(argv) > 2 else 4096
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
o = [x for x in bpy.context.scene.objects if x.type == 'MESH'][0]
bpy.context.view_layer.objects.active = o; o.select_set(True)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 48
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'METAL'; prefs.get_devices()
    for d in prefs.devices: d.use = True
    sc.cycles.device = 'GPU'
except Exception as e: print('GPU 不可用，用 CPU', e)
sc.render.bake.margin = 8
mat = o.data.materials[0]; nt = mat.node_tree
def target(name):
    img = bpy.data.images.new(name, RES, RES, alpha=False, float_buffer=False)
    n = nt.nodes.new('ShaderNodeTexImage'); n.image = img
    for x in nt.nodes: x.select = False
    n.select = True; nt.nodes.active = n
    return img, n
# AO
img, node = target('ao')
bpy.ops.object.bake(type='AO')
img.filepath_raw = OUT + '/ao.png'; img.file_format = 'PNG'; img.save(); print('AO 完成')
nt.nodes.remove(node)
# 凸起程度：临时把发光接成 Geometry.Pointiness，烘 EMIT
out = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
old = out.inputs['Surface'].links[0].from_socket if out.inputs['Surface'].links else None
geo = nt.nodes.new('ShaderNodeNewGeometry'); em = nt.nodes.new('ShaderNodeEmission')
ramp = nt.nodes.new('ShaderNodeMapRange'); ramp.inputs['From Min'].default_value = 0.42; ramp.inputs['From Max'].default_value = 0.58
nt.links.new(geo.outputs['Pointiness'], ramp.inputs['Value']); nt.links.new(ramp.outputs['Result'], em.inputs['Color'])
nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
img, node = target('curv')
bpy.ops.object.bake(type='EMIT')
img.filepath_raw = OUT + '/curv.png'; img.file_format = 'PNG'; img.save(); print('凸起程度 完成')

# 朝上程度 / 高度：同样用发光烘
def bake_emit(name, socket_from):
    nt.links.new(socket_from, em.inputs['Color'])
    img, node = target(name)
    bpy.ops.object.bake(type='EMIT')
    img.filepath_raw = OUT + '/' + name + '.png'; img.file_format = 'PNG'; img.save(); print(name, '完成')
    nt.nodes.remove(node)
sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(geo.outputs['Normal'], sep.inputs['Vector'])
mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['From Min'].default_value = -1; mr.inputs['From Max'].default_value = 1
nt.links.new(sep.outputs['Z'], mr.inputs['Value']); bake_emit('up', mr.outputs['Result'])
sep2 = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(geo.outputs['Position'], sep2.inputs['Vector'])
zs = [ (o.matrix_world @ v.co).z for v in o.data.vertices ]
mr2 = nt.nodes.new('ShaderNodeMapRange'); mr2.inputs['From Min'].default_value = min(zs); mr2.inputs['From Max'].default_value = max(zs)
nt.links.new(sep2.outputs['Z'], mr2.inputs['Value']); bake_emit('height', mr2.outputs['Result'])
# UV 岛编号（每个岛一个随机颜色）：贴图加工时按岛判断“这个岛主要是什么材质”
bm = __import__('bmesh').new(); bm.from_mesh(o.data); uvl = bm.loops.layers.uv.active
import random
bm.faces.ensure_lookup_table()
# 按 UV 连通（共用 UV 坐标的边）分岛
from collections import defaultdict
parent = list(range(len(bm.faces)))
def find(a):
    while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
    return a
for e in bm.edges:
    if len(e.link_faces) != 2: continue
    f1, f2 = e.link_faces
    def uvs(f):
        return {l.vert.index: tuple(round(c, 5) for c in l[uvl].uv) for l in f.loops if l.vert in e.verts}
    if uvs(f1) == uvs(f2): parent[find(f1.index)] = find(f2.index)
ids = {}
col = o.data.color_attributes.new('island', 'BYTE_COLOR', 'CORNER')
rnd = random.Random(1)
for f in bm.faces:
    r = find(f.index)
    if r not in ids: ids[r] = (rnd.random(), rnd.random(), rnd.random(), 1)
for poly in o.data.polygons:
    c = ids[find(poly.index)]
    for li in poly.loop_indices: col.data[li].color = c
print('UV 岛', len(ids))
attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'island'
sc.render.bake.margin = 0
bake_emit('island', attr.outputs['Color'])
