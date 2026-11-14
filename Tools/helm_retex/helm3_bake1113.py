# 11-13 封面战盔修正（三）第 3 步（Blender）：给 part0..6 套上 xatlas 的新 UV，然后把三样东西烘到新 UV（2048）：
#   old.png   旧明暗图（按旧 UV 采样 → 新 UV）：骷髅、狮子、角、铆钉沿用定稿的明暗；UV 拉飞的面烘出来是错的，第 4 步用周围的好像素补
#   ao.png    环境光遮蔽（缝隙、甲片叠压处暗）
#   curv.png  曲率（> 0.5 凸棱，< 0.5 凹缝）
# 导出 helm3.fbx（只留新 UV）和 /tmp/h13/uv.npz（各部件新 UV 三角形 + 拉飞标记）。
# 用法：Blender -b -P Tools/helm_retex/helm3_bake1113.py   （先跑第 1、2 步）
import bpy, numpy as np

W = '/Users/ld/Documents/ChatGPT/WarTable/'
OUT = '/tmp/h13/'
DST = W + 'Assets/_WarTable/Art/Models/Cover/Helm3/helm3.fbx'
bpy.ops.wm.open_mainfile(filepath=OUT + 'stepA.blend')
objs = {o.name.split('.')[0]: o for o in bpy.context.scene.objects if o.type == 'MESH'}
nu = np.load(OUT + 'newuv.npz'); mesh = np.load(OUT + 'mesh.npz')
parts = [objs[f'part{k}'] for k in range(7) if f'part{k}' in objs]
for k in range(7):
    ob = objs.get(f'part{k}')
    if ob is None: continue
    me = ob.data; t = nu[f'uv{k}']
    assert len(me.polygons) == len(t), (k, len(me.polygons), len(t))
    me.uv_layers[0].name = 'old'
    new = me.uv_layers.new(name='UVMap', do_init=False)
    flat = np.zeros((len(me.loops), 2), np.float32)
    for i, p in enumerate(me.polygons):
        ls = list(p.loop_indices); assert len(ls) == 3
        for c in range(3): flat[ls[c]] = t[i, c]
    me.uv_layers['UVMap'].data.foreach_set('uv', flat.ravel())
    me.uv_layers.active = me.uv_layers['UVMap']; me.uv_layers['UVMap'].active_render = True
    for p in me.polygons: p.use_smooth = True
    me.update()

sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 96
sc.render.bake.margin = 8; sc.render.bake.use_clear = True
w = bpy.data.worlds.new('bakeworld'); w.use_nodes = True; wnt = w.node_tree
bg = wnt.nodes.get('Background')
if bg is None:      # 打开存档后新建的世界不一定自带节点，自己搭：背景（纯白）→ 输出
    bg = wnt.nodes.new('ShaderNodeBackground'); wo = wnt.nodes.get('World Output') or wnt.nodes.new('ShaderNodeOutputWorld'); wnt.links.new(bg.outputs['Background'], wo.inputs['Surface'])
bg.inputs[0].default_value = (1, 1, 1, 1); bg.inputs[1].default_value = 1.0; sc.world = w
oldimg = bpy.data.images.load(W + 'ArtSource/Cover/helm3_backup_1113/helm3_albedo.png'); oldimg.colorspace_settings.name = 'Non-Color'
for kind in ('old', 'ao', 'curv'):
    img = bpy.data.images.new(kind, 2048, 2048, alpha=False); img.colorspace_settings.name = 'Non-Color'
    for ob in parts:
        ob.data.materials.clear(); m = bpy.data.materials.new(kind + ob.name); m.use_nodes = True; ob.data.materials.append(m); nt = m.node_tree
        outn = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        if kind == 'old':
            uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'old'; tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = oldimg; tx.interpolation = 'Linear'; em = nt.nodes.new('ShaderNodeEmission')
            nt.links.new(uvn.outputs['UV'], tx.inputs['Vector']); nt.links.new(tx.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], outn.inputs['Surface'])
        if kind == 'curv':
            geo = nt.nodes.new('ShaderNodeNewGeometry'); ramp = nt.nodes.new('ShaderNodeValToRGB'); em = nt.nodes.new('ShaderNodeEmission')
            ramp.color_ramp.elements[0].position = 0.44; ramp.color_ramp.elements[1].position = 0.56
            nt.links.new(geo.outputs['Pointiness'], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], outn.inputs['Surface'])
        tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
        for n in nt.nodes: n.select = False
        tn.select = True; nt.nodes.active = tn
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts: ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.bake(type='AO' if kind == 'ao' else 'EMIT')
    img.filepath_raw = OUT + kind + '.png'; img.file_format = 'PNG'; img.save()
    print('BAKED', kind, flush=True)

# 位置图：每个贴图像素对应模型上的三维位置（世界坐标，米）。第 4 步用它在三维空间里生成噪声——
# 在贴图平面上生成的噪声跨 UV 块接不上，穹顶上会出现一块块边界分明的色块（11-13 用户指出）。
pimg = bpy.data.images.new('pos', 2048, 2048, alpha=False, float_buffer=True); pimg.colorspace_settings.name = 'Non-Color'
for ob in parts:
    ob.data.materials.clear(); m = bpy.data.materials.new('pos' + ob.name); m.use_nodes = True; ob.data.materials.append(m); nt = m.node_tree
    outn = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'); geo = nt.nodes.new('ShaderNodeNewGeometry'); em = nt.nodes.new('ShaderNodeEmission')
    add = nt.nodes.new('ShaderNodeVectorMath'); add.operation = 'ADD'; add.inputs[1].default_value = (1.0, 1.0, 1.0)      # 平移到正数（模型在 ±0.5 m 以内）
    nt.links.new(geo.outputs['Position'], add.inputs[0]); nt.links.new(add.outputs['Vector'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], outn.inputs['Surface'])
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = pimg
    for n in nt.nodes: n.select = False
    tn.select = True; nt.nodes.active = tn
bpy.ops.object.select_all(action='DESELECT')
for ob in parts: ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.bake(type='EMIT')
buf = np.empty(2048 * 2048 * 4, np.float32); pimg.pixels.foreach_get(buf)
np.save(OUT + 'pos.npy', (buf.reshape(2048, 2048, 4)[::-1, :, :3] - 1.0).astype(np.float32))     # 第 0 行 = 贴图最上面一行
print('BAKED pos', flush=True)

# 导出：只留新 UV，材质名恢复成部件名（Unity 里按子物体名认部件、材质由 CoverBuilder 另给）
uvout = {}
for k in range(7):
    ob = objs.get(f'part{k}')
    if ob is None: continue
    me = ob.data; me.uv_layers.remove(me.uv_layers['old'])
    me.materials.clear(); me.materials.append(bpy.data.materials.new(f'part{k}'))
    uv = me.uv_layers[0].data
    uvout[f'part{k}'] = np.array([[tuple(uv[l].uv) for l in p.loop_indices] for p in me.polygons], np.float32)
    uvout[f'bad{k}'] = mesh[f'b{k}']
    print(f'UV part{k}: 层 {[l.name for l in me.uv_layers]} 三角形 {len(me.polygons)}', flush=True)
np.savez(OUT + 'uv.npz', **uvout)
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.export_scene.fbx(filepath=DST, use_selection=False, path_mode='COPY', embed_textures=False)
print('EXPORTED', flush=True)
