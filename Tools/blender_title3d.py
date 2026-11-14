"""铜铸立体标题字（Blender 里跑）：Blender -b -P Tools/blender_title3d.py -- <轮廓.json> <输出.fbx> [厚度] [倒角]

读 Tools/title_contours.py 的轮廓（带镂空），每个字一个物体 Char_00..（从左到右），整幅字宽归一为 1 米（Unity 里再缩放），
原点都在整幅字中心。用 Blender 曲线填充（外轮廓 + 镂空自动按奇偶规则挖空）→ 挤出 → 圆角倒边（像铸件的边，不是锐利的刀切）→ 转网格。
正面朝 Blender −Y（前视图看到的就是字）；两个材质槽：0 = 正面（鎏金）、1 = 侧面和倒角（古铜）。UV = 正面平面投影（侧面沿用轮廓边的坐标，铜锈贴图在侧面拉成竖纹，正好像铸造的边）。
"""
import bpy, json, sys, os

args = sys.argv[sys.argv.index('--') + 1:]
src, out = args[0], args[1]
DEPTH = float(args[2]) if len(args) > 2 else 0.05
BEV = float(args[3]) if len(args) > 3 else 0.006
D = json.load(open(src))
x0, y0, x1, y1 = D['bbox']
S = 1.0 / (x1 - x0)
cx, cy = (x0 + x1) / 2, (y0 + y1) / 2

bpy.ops.wm.read_factory_settings(use_empty=True)
objs = []
for i, ch in enumerate(D['chars']):
    cu = bpy.data.curves.new(f'Char_{i:02d}', 'CURVE')
    cu.dimensions = '2D'; cu.fill_mode = 'BOTH'
    cu.extrude = DEPTH / 2 - BEV
    cu.bevel_depth = BEV; cu.bevel_resolution = 3
    cu.offset = 0   # 不往里收：书法细笔画往里收会翻转（缺面），宁可字略胖 1%
    for piece in ch:
        for poly in [piece['outer']] + piece['holes']:
            sp = cu.splines.new('POLY')
            sp.points.add(len(poly) - 1)
            for k, (x, y) in enumerate(poly):
                sp.points[k].co = ((x - cx) * S, (cy - y) * S, 0, 1)
            sp.use_cyclic_u = True
    ob = bpy.data.objects.new(f'Char_{i:02d}', cu)
    bpy.context.collection.objects.link(ob)
    # 曲线在 XY 平面、挤出沿 Z：转到 XZ 平面（字立起来），正面朝 −Y
    ob.rotation_euler = (1.5707963, 0, 0)
    objs.append(ob)

bpy.ops.object.select_all(action='DESELECT')
for ob in objs:
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
bpy.ops.object.convert(target='MESH')
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
face_mat = bpy.data.materials.new('TitleFace')   # 正面：抛光鎏金
side_mat = bpy.data.materials.new('TitleSide')   # 侧面 + 倒角：深色古铜（铸件的层次）
for ob in bpy.context.selected_objects:
    me = ob.data
    me.materials.append(face_mat); me.materials.append(side_mat)
    for p in me.polygons:
        p.use_smooth = True
        p.material_index = 0 if p.normal.y < -0.92 else 1
    # 平面投影 UV：x、z（字面坐标）
    uv = me.uv_layers.new(name='UVMap')
    for l in me.loops:
        v = me.vertices[l.vertex_index].co
        uv.data[l.index].uv = (v.x + 0.5, v.z + 0.5)
    ob.modifiers.new('WN', 'WEIGHTED_NORMAL').keep_sharp = True
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier='WN')
    print(ob.name, len(me.polygons), '面')

os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
print('导出', out)
