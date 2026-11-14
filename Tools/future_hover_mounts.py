# 未来时代的悬浮载具（11-12 用户：都到未来了，不一定还骑马，可以骑高科技坐骑）：f_bike（光棱军团的悬浮摩托）、f_skimmer（虚空蜂群的甲虫飞行器）。
# 它们没有腿、不绑骨，不走原型 tools/build/mounts.ts（那个靠骨骼判朝向和鞍点）。这里把 Tripo 出的 raw/<名>/model.glb 摆成和原型坐骑一样的约定：
#   原点在脚下地面正中，车头朝 glTF −Z（Blender +Y），车身缩放到 LENGTH（游戏单位 ≈ 码），
#   座位顶面抬到原版坐骑的鞍高（马 1.55 / 狼 1.45）——骑手的坐姿和位置都不用改；车底离地的高度就是悬浮高度。
# 输出 ~/sandtable-xr/public/assets/mounts/<名>.glb（之后照常 convert_demo_assets.py → make_demo_lods.py），并渲染一张顶视 + 侧视预览核对车头方向。
# 用法：Blender -b -P Tools/future_hover_mounts.py -- <名>[:flip] …    （车头反了就在名字后面加 :flip）
import bpy, sys, os, math, json
from mathutils import Vector, Matrix

RAW = os.path.expanduser('~/sandtable-xr/tools/tripo/raw/')
OUT = os.path.expanduser('~/sandtable-xr/public/assets/mounts/')
# length = 车身长；saddle = 座位顶面的高度（原版鞍高 马 1.55 / 狼 1.45，减去 DemoFigures.HoverDrop 里登记的下降量——悬浮摩托整车连人降 0.35，不然离地太高）；
# seat = 骑手坐的位置离车身中点多远（缩放后的单位，负 = 靠车尾）：悬浮摩托的座垫在中点后面，不挪的话人会坐到车头上
SPEC = {'f_bike': dict(length=2.7, saddle=1.2, seat=-0.3), 'f_skimmer': dict(length=2.6, saddle=1.45, seat=0.0),
        # 11-13 现代时代的摩托：ground=True = 带轮子、轮底贴地（不按鞍高抬起来）；跑完看打印的 seat_h（座垫顶面高度），
        # DemoFigures.HoverDrop 里填“原版鞍高（马 1.55 / 狼 1.45）− seat_h”，骑手就坐在座垫上
        'm_moto': dict(length=2.35, ground=True, seat=-0.2), 'm_dirtbike': dict(length=2.25, ground=True, seat=-0.15),
        # 11-13 深空：星枪团的重型悬浮冲击艇（悬浮，鞍高 1.25 = 马 1.55 − HoverDrop 0.3）、余烬矿帮的履带矿车（贴地，HoverDrop 按打印的 seat_h 填）
        's_strider': dict(length=2.8, saddle=1.25, seat=-0.15), 's_crawler': dict(length=2.5, ground=True, seat=-0.1)}
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else list(SPEC)
report = {}

for arg in args:
    name, flip = (arg.split(':')[0], arg.endswith(':flip'))
    sp = SPEC[name]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=RAW + name + '/model.glb')
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o != ob: bpy.data.objects.remove(o, do_unlink=True)
    me = ob.data
    co = [v.co.copy() for v in me.vertices]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co))); hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    dx, dy = hi.x - lo.x, hi.y - lo.y
    # 车身长轴转到 Y；默认认为车头在长轴的正方向（提示词要求头朝右），不对就 :flip
    rot = Matrix.Rotation(math.radians(90), 4, 'Z') if dx > dy else Matrix.Identity(4)
    if flip: rot = Matrix.Rotation(math.radians(180), 4, 'Z') @ rot
    me.transform(rot); me.update()
    co = [v.co.copy() for v in me.vertices]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co))); hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    length = hi.y - lo.y; k = sp['length'] / length
    cy = (lo.y + hi.y) / 2 + sp.get('seat', 0.0) / k; cx = (lo.x + hi.x) / 2
    # 座位：骑手身下那一段（长度 ±7%、宽度 ±6% 以内）的最高点
    seat = max((c.z for c in co if abs(c.y - cy) < length * 0.07 and abs(c.x - cx) < length * 0.06), default=hi.z)
    if sp.get('ground'):
        me.transform(Matrix.Scale(k, 4) @ Matrix.Translation(Vector((-cx, -cy, -lo.z)))); me.update()
    else:
        me.transform(Matrix.Translation(Vector((0, 0, sp['saddle']))) @ Matrix.Scale(k, 4) @ Matrix.Translation(Vector((-cx, -cy, -seat)))); me.update()
    zs = [v.co.z for v in me.vertices]; ys = [v.co.y for v in me.vertices]; xs = [v.co.x for v in me.vertices]
    report[name] = dict(length=round(max(ys) - min(ys), 3), width=round(max(xs) - min(xs), 3), height=round(max(zs) - min(zs), 3), hover=round(min(zs), 3), top=round(max(zs), 3), flip=flip, seat_h=round((seat - lo.z) * k, 3) if sp.get('ground') else sp['saddle'])
    for p in me.polygons: p.use_smooth = True
    ob.name = name; me.name = name
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=OUT + name + '.glb', export_format='GLB', use_selection=True, export_yup=True, export_apply=True)
    # 预览：左 = 顶视（上方 = 车头 +Y），右 = 侧视（右边 = 车头）
    sc = bpy.context.scene
    try: sc.render.engine = 'BLENDER_EEVEE'
    except Exception: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    w = bpy.data.worlds.new('w'); w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.5, 0.5, 0.52, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.6; sc.world = w
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3; sun.rotation_euler = (math.radians(40), 0, math.radians(30)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.4
    sc.render.resolution_x = 520; sc.render.resolution_y = 520
    for tag, loc, rotv in (('top', (0, 0, 6), (0, 0, 0)), ('side', (-6, 0, 1.0), (math.radians(90), 0, math.radians(-90)))):
        cam.location = loc; cam.rotation_euler = rotv
        sc.render.filepath = f'/tmp/sp2/hover_{name}_{tag}.png'; bpy.ops.render.render(write_still=True)
print('HOVER', json.dumps(report))
