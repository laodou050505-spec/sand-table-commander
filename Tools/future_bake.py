# 未来时代贴图加质感用的两张烘焙图（11-12 用户：“角色不能完全没有质感”）。在 Blender 里跑：
#   Blender -b -P Tools/future_bake.py -- [名字…]      （不带名字 = raw/ 里所有 f_* 兵种和坐骑）
# 从原型 ~/sandtable-xr/tools/tripo/raw/<名>/model.glb（T 字姿势、和贴图同一套 UV）烘焙：
#   AO.png    环境光遮蔽：缝隙 / 凹处暗（模型涂装里的“渍洗”）
#   Curv.png  曲率（Pointiness）：0.5 = 平，> 0.5 = 凸棱（“干扫”提亮），< 0.5 = 凹缝
# 输出到 ArtSource/Future/bake/<名>/，Tools/future_tex_fix.py 合成进颜色贴图。
import bpy, sys, os, glob

RAW = os.path.expanduser('~/sandtable-xr/tools/tripo/raw/')
OUT = '/Users/ld/Documents/ChatGPT/WarTable/ArtSource/Future/bake/'
want = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
names = want or sorted(os.path.basename(os.path.dirname(p)) for p in glob.glob(RAW + 'f_*/rig.glb'))
SIZE = 1024


def bake(name):
    src = RAW + name + '/model.glb'
    if not os.path.exists(src): print('SKIP', name, 'no model.glb'); return
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 48
    sc.render.bake.margin = 12; sc.render.bake.use_clear = True
    w = bpy.data.worlds.new('w'); w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1); sc.world = w
    os.makedirs(OUT + name, exist_ok=True)
    for kind in ('AO', 'Curv'):
        img = bpy.data.images.new(kind, SIZE, SIZE, alpha=False); img.colorspace_settings.name = 'Non-Color'
        for m in ob.data.materials:
            m.use_nodes = True; nt = m.node_tree
            tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img; tn.name = 'BAKE_' + kind
            for n in nt.nodes: n.select = False
            tn.select = True; nt.nodes.active = tn
            if kind == 'Curv':
                geo = nt.nodes.new('ShaderNodeNewGeometry'); ramp = nt.nodes.new('ShaderNodeValToRGB'); em = nt.nodes.new('ShaderNodeEmission')
                ramp.color_ramp.elements[0].position = 0.42; ramp.color_ramp.elements[1].position = 0.58   # 把 0.5 附近拉开
                out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
                nt.links.new(geo.outputs['Pointiness'], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
        bpy.ops.object.bake(type='AO' if kind == 'AO' else 'EMIT')
        img.filepath_raw = OUT + name + '/' + kind + '.png'; img.file_format = 'PNG'; img.save()
    print('BAKED', name, len(ob.data.polygons), 'faces')


for n in names: bake(n)
print('done', len(names))
