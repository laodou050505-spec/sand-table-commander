"""封面主角高清贴图第 1 / 3 步（Blender）。11-19 用户：“现在的贴图很糊”“质量很低”。
根源：Tripo 原始贴图是 8192×8192（约 0.15 mm 一个像素），P2 收尾把它缩到 2048、又烘进一套只用了 18% 面积的贴图坐标（0.79 mm 一个像素）。
修法：给 final/lod0 重新展紧凑的贴图坐标，拆成上下两张 2048（工程规则：单张贴图不超过 2K），从 8K 原图重烘。

  Blender -b -P Tools/hero_hd_mesh.py -- dump     导出网格 → /tmp/sp7/hd/mesh.npz
  python3 Tools/hero_hd_atlas.py                  xatlas 展开 → /tmp/sp7/hd/newuv.npz
  Blender -b -P Tools/hero_hd_mesh.py -- build    套上新坐标、分两个材质槽、导出 final/lod0.fbx；
                                                  再把原图颜色 / 遮蔽 / 位置 / 法线 / 掩码烘到新坐标 → /tmp/sp7/hd/*.npy
  之后：python3 Tools/hero_hd_tex.py → python3 Tools/hero_pbr.py → Unity 里“WarTable/6 搭封面”

烘焙用的是一张 4096×2048 的“并排图”：左半 = 第一张贴图（上半身），右半 = 第二张（下半身 + 岩座）；数组第 0 行在上。
原来的 final/lod0.fbx 备份在 final/lod0_olduv.fbx（每次都从它出发，可重复跑）；lod1–3 和 final/albedo.png 仍是旧坐标，封面不用它们。"""
import bpy, sys, os
import numpy as np
R = '/Users/ld/Documents/ChatGPT/WarTable/'
P2 = R + 'ArtSource/ModelsP2/Hero_Statue/'
OUT = '/tmp/sp7/hd/'
W, H = 4096, 2048
mode = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else 'dump'
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=P2 + 'final/lod0_olduv.fbx')
ob = max([o for o in bpy.context.scene.objects if o.type == 'MESH'], key=lambda o: len(o.data.polygons))
me = ob.data
assert all(len(p.vertices) == 3 for p in me.polygons), '网格不是纯三角形'
T = len(me.polygons)
tri = np.empty(T * 3, np.int32); me.polygons.foreach_get('vertices', tri); tri = tri.reshape(T, 3)
co = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
mw = np.array(ob.matrix_world)
world = co @ mw[:3, :3].T + mw[:3, 3]
print('网格', ob.name, '顶点', len(co), '三角', T, '世界包围盒', world.min(0).round(3), world.max(0).round(3), flush=True)

if mode == 'dump':
    np.savez(OUT + 'mesh.npz', v=world, f=tri)
    print('已导出', OUT + 'mesh.npz')
    sys.exit(0)

# ───────── build：套新坐标
d = np.load(OUT + 'newuv.npz')
uv, tile = d['uv'].astype(np.float64), d['tile'].astype(np.int32)        # T×3×2（各自 0..1）、T
assert len(uv) == T
while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
real = me.uv_layers.new(name='UVMap'); real.data.foreach_set('uv', uv.reshape(-1))
side = uv.copy(); side[..., 0] = (side[..., 0] + tile[:, None]) / 2        # 并排图：左半第一张、右半第二张
bake_uv = me.uv_layers.new(name='bake'); bake_uv.data.foreach_set('uv', side.reshape(-1))
me.materials.clear()
for n in ('hero_a', 'hero_b'): me.materials.append(bpy.data.materials.new(n))
me.polygons.foreach_set('material_index', tile)
me.update()

# 导出 final/lod0.fbx：只带真正的那套坐标（参数和 Tools/p2_lods.py 一致；物体的变换原样留着）
me.uv_layers.active = real; real.active_render = True
me.uv_layers.remove(me.uv_layers['bake'])
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
bpy.ops.export_scene.fbx(filepath=P2 + 'final/lod0.fbx', use_selection=True, path_mode='STRIP', embed_textures=False, mesh_smooth_type='OFF',
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_mesh_modifiers=True, add_leaf_bones=False)
print('已导出 final/lod0.fbx', flush=True)
bake_uv = me.uv_layers.new(name='bake'); bake_uv.data.foreach_set('uv', side.reshape(-1)); me.uv_layers.active = bake_uv; bake_uv.active_render = True
for p in me.polygons: p.use_smooth = True

# ───────── 烘焙
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'
w = bpy.data.worlds.new('w'); sc.world = w
height = float(world[:, 2].max() - world[:, 2].min())
w.light_settings.distance = height * 0.08          # 像高的 8%（约 3.5 cm）：甲片交叠、关节缝里才算被挡住


def bake(name, kind, margin, samples, build=None, s2a=False):
    mat = bpy.data.materials.new(name); mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    o = nt.nodes.new('ShaderNodeOutputMaterial')
    sh = nt.nodes.new('ShaderNodeEmission' if build else 'ShaderNodeBsdfDiffuse'); nt.links.new(sh.outputs[0], o.inputs[0])
    if build: build(nt, sh)
    img = bpy.data.images.new(name, W, H, float_buffer=True, alpha=True); img.colorspace_settings.name = 'Non-Color'
    it = nt.nodes.new('ShaderNodeTexImage'); it.image = img; nt.nodes.active = it
    me.materials.clear(); me.materials.append(mat); me.polygons.foreach_set('material_index', np.zeros(T, np.int32))
    sc.cycles.samples = samples; b = sc.render.bake; b.margin = margin; b.use_selected_to_active = s2a
    if s2a: b.cage_extrusion = 0.012 * height; b.max_ray_distance = 0.035 * height
    bpy.ops.object.bake(type=kind)
    a = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(a)
    a = a.reshape(H, W, 4)[::-1].copy()
    np.save(OUT + name + '.npy', a); print('saved', name, a.shape, float(a[..., :3].min()), float(a[..., :3].max()), flush=True)
    bpy.data.images.remove(img)


def white(nt, em): em.inputs[0].default_value = (1, 1, 1, 1)
def position(nt, em): nt.links.new(nt.nodes.new('ShaderNodeNewGeometry').outputs['Position'], em.inputs[0])
def normal(nt, em): nt.links.new(nt.nodes.new('ShaderNodeNewGeometry').outputs['Normal'], em.inputs[0])
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
bake('mask', 'EMIT', 0, 1, white)
bake('pos', 'EMIT', 8, 1, position)
bake('nrm', 'EMIT', 8, 1, normal)
if 'noao' not in sys.argv: bake('ao', 'AO', 8, 96)

# 原图颜色：Tripo 原始模型（8K 贴图）→ 新坐标。原模型的材质换成“把贴图当自发光”，这样烘出来就是贴图本身的颜色（线性），不带光照
bpy.ops.import_scene.gltf(filepath=P2 + 'model.glb')
src = [o for o in bpy.context.selected_objects if o.type == 'MESH']
sv = np.concatenate([np.array([o.matrix_world @ v.co for v in o.data.vertices]) for o in src])
print('原模型', [o.name for o in src], '世界包围盒', sv.min(0).round(3), sv.max(0).round(3), '重心偏移', (sv.mean(0) - world.mean(0)).round(4), flush=True)
for o in src:
    for m in o.data.materials:
        nt = m.node_tree; tex = next(n for n in nt.nodes if n.type == 'TEX_IMAGE'); out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        print('原贴图', tex.image.size[:], tex.image.colorspace_settings.name, flush=True)
        em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(tex.outputs['Color'], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs['Surface'])
bpy.ops.object.select_all(action='DESELECT')
for o in src: o.select_set(True)
ob.select_set(True); bpy.context.view_layer.objects.active = ob


def from_src():
    mat = bpy.data.materials.new('rawbake'); mat.use_nodes = True; nt = mat.node_tree
    img = bpy.data.images.new('raw', W, H, float_buffer=True, alpha=True); img.colorspace_settings.name = 'Non-Color'
    it = nt.nodes.new('ShaderNodeTexImage'); it.image = img; nt.nodes.active = it
    me.materials.clear(); me.materials.append(mat); me.polygons.foreach_set('material_index', np.zeros(T, np.int32))
    sc.cycles.samples = 1; b = sc.render.bake; b.margin = 0; b.use_selected_to_active = True; b.use_clear = True
    b.cage_extrusion = 0.012 * height; b.max_ray_distance = 0.035 * height
    bpy.ops.object.bake(type='EMIT')
    a = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(a)
    a = a.reshape(H, W, 4)[::-1].copy()
    np.save(OUT + 'raw.npy', a); print('saved raw', a.shape, '命中率(透明度>0.5) %.3f' % float((a[..., 3] > 0.5).mean()), flush=True)


from_src()
print('完成 height=', height)
