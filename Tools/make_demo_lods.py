"""给原型角色 / 坐骑 / 武器做远处用的低模（LOD1）：只对网格减面，骨骼、权重、UV、贴图都不变，角色外形不改。
用法：Blender -b -P Tools/make_demo_lods.py -- [比例=0.3] [名称...]
输出：Assets/_WarTable/Art/Models/Demo/<troops|mounts>/<名>_lod1.fbx（不带动画；Unity 里把它的蒙皮网格挂到原模的同名骨骼上，动作由原模驱动）。"""
import bpy, pathlib, sys

SRC = pathlib.Path('/Users/ld/sandtable-xr/public/assets')
DST = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Models/Demo')
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ratio = float(args[0]) if args and args[0].replace('.', '', 1).isdigit() else 0.3
want = [a for a in args if not a.replace('.', '', 1).isdigit()]

WEAPONS = ['arming_sword', 'short_sword', 'claymore', 'warhammer', 'halberd', 'spear', 'lance', 'long_bow', 'bow', 'crossbow',
           'hatchet', 'clan_axe', 'war_axe', 'war_axe2', 'maul', 'glaive', 'javelin', 'shield_round', 'shield_crest', 'shield_clan']

# 11-11 哥特时代的武器 / 盾 / 令牌（原型 public/assets/props/g_*.glb、order_token.glb）
WEAPONS = WEAPONS + sorted(p.stem for p in (SRC / 'props').glob('g_*.glb')) + sorted(p.stem for p in (SRC / 'props').glob('f_*.glb')) + sorted(p.stem for p in (SRC / 'props').glob('m_*.glb')) + sorted(p.stem for p in (SRC / 'props').glob('s_*.glb'))   # m_ = 现代时代（11-13）
jobs = [('troops', p.stem) for p in sorted((SRC / 'troops').glob('*.glb'))] + [('mounts', p.stem) for p in sorted((SRC / 'mounts').glob('*.glb'))] + [('props', n) for n in WEAPONS]
for kind, name in jobs:
    if want and name not in want: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(SRC / kind / f'{name}.glb'))
    for o in list(bpy.context.scene.objects):
        if o.type == 'MESH' and o.name.startswith('Icosphere') and o.parent is None:
            bpy.data.objects.remove(o, do_unlink=True)
    before = after = 0
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or (kind != 'props' and not any(m.type == 'ARMATURE' for m in o.modifiers)): continue
        before += len(o.data.polygons)
        bpy.context.view_layer.objects.active = o
        d = o.modifiers.new('lod', 'DECIMATE')
        d.decimate_type = 'COLLAPSE'; d.ratio = ratio; d.use_collapse_triangulate = True
        # 减面修改器要排在骨骼修改器前面（在静止姿势上减面，权重随顶点合并）
        while o.modifiers.find('lod') > 0:
            bpy.ops.object.modifier_move_up(modifier='lod')
        bpy.ops.object.modifier_apply(modifier='lod')
        after += len(o.data.polygons)
    # 只要蒙皮网格 + 骨架；挂点下的道具、动作都不要
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    bpy.ops.export_scene.fbx(filepath=str(DST / kind / f'{name}_lod1.fbx'), use_selection=False, apply_scale_options='FBX_SCALE_ALL',
                             add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE', embed_textures=False,
                             object_types={'ARMATURE', 'MESH'} if kind != 'props' else {'EMPTY', 'MESH'})
    print('LOD1', kind, name, before, '->', after, flush=True)
