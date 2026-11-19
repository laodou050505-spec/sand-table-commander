"""把 Three.js 原型（~/sandtable-xr/public/assets）里的角色、坐骑、武器原样转成 Unity 能读的 FBX。
用户要求：角色沿用原型，不重做。所以这里只换格式，不改模型、骨骼、动画、贴图。
用法：Blender -b -P Tools/convert_demo_assets.py -- [名称...]（不给名称就全部转）
输出：Assets/_WarTable/Art/Models/Demo/<troops|mounts|props>/<名>.fbx，贴图在同目录 <名>_tex/。
  - 角色：Mixamo 骨骼 + 50 段动作（Stand/Run/各种攻击、格挡、死亡…）+ 挂点 GripR / GripL / ShieldL
  - 坐骑：马、狼各 6 段动作
  - 道具：武器（握点在原点、沿 −Z）、盾（正面 +Z）、弓箭、军旗"""
import bpy, bmesh, pathlib, sys


def drop_debris(min_faces=40):
    """哥特时代兵种（g_*，P2 从定妆图生成）：删掉面数很少的孤立小块（P2 偶尔留下几十块碎片，审计里的 debris），
    蒙皮网格上直接删顶点，权重 / 骨骼 / 贴图不受影响。原型兵种不走这里（原样转换）。
    连通按顶点“位置”算：glTF 导入后 UV 接缝处的顶点是断开的，按边连通会把一片片 UV 当成碎片删掉。"""
    n = 0
    for o in bpy.context.scene.objects:
        if o.type != 'MESH': continue
        bm = bmesh.new(); bm.from_mesh(o.data)
        parent = {}
        def find(k):
            while parent.setdefault(k, k) != k:
                parent[k] = parent[parent[k]]; k = parent[k]
            return k
        key = lambda v: (round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5))
        for f in bm.faces:
            ks = [find(key(v)) for v in f.verts]
            for k in ks[1:]: parent[find(k)] = find(ks[0])
        size = {}
        for f in bm.faces:
            r = find(key(f.verts[0])); size[r] = size.get(r, 0) + 1
        kill = [f for f in bm.faces if size[find(key(f.verts[0]))] < min_faces]
        if kill:
            verts = {v for f in kill for v in f.verts}
            bmesh.ops.delete(bm, geom=list(verts), context='VERTS'); n += len(kill)
            bm.to_mesh(o.data); o.data.update()
        bm.free()
    return n

SRC = pathlib.Path('/Users/ld/sandtable-xr/public/assets')
DST = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Models/Demo')
PROPS = ['arming_sword', 'short_sword', 'claymore', 'warhammer', 'halberd', 'spear', 'lance', 'long_bow', 'bow', 'crossbow',
         'hatchet', 'clan_axe', 'war_axe', 'war_axe2', 'maul', 'glaive', 'javelin', 'arrow',
         'shield_round', 'shield_crest', 'shield_clan', 'banner_kingdom', 'banner_clan']
want = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


# 11-11 哥特时代的武器 / 盾 / 令牌（原型 public/assets/props/g_*.glb、order_token.glb）
PROPS = PROPS + sorted(p.stem for p in (SRC / 'props').glob('g_*.glb')) + sorted(p.stem for p in (SRC / 'props').glob('f_*.glb')) + sorted(p.stem for p in (SRC / 'props').glob('m_*.glb')) + sorted(p.stem for p in (SRC / 'props').glob('s_*.glb')) + ['order_token']   # 11-12：f_ = 未来时代
jobs = [('troops', p.stem) for p in sorted((SRC / 'troops').glob('*.glb'))] + [('mounts', p.stem) for p in sorted((SRC / 'mounts').glob('*.glb'))] + [('props', n) for n in PROPS]
for kind, name in jobs:
    if want and name not in want: continue
    src = SRC / kind / f'{name}.glb'
    if not src.exists(): print('MISSING', src); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(src))
    # glTF 导入器多出来的一个没挂父物体的 Icosphere（原型里没有用），删掉
    for o in list(bpy.context.scene.objects):
        if o.type == 'MESH' and o.name.startswith('Icosphere') and o.parent is None:
            bpy.data.objects.remove(o, do_unlink=True)
    if name.startswith(('g_', 'f_', 'm_', 's_')) and kind == 'troops': print('debris faces removed', drop_debris(), flush=True)   # 只清兵种；载具上的小零件（把手、灯）不能当碎片删
    out = DST / kind
    tex = out / f'{name}_tex'
    tex.mkdir(parents=True, exist_ok=True)
    for im in bpy.data.images:
        if im.size[0] == 0: continue
        p = tex / f'{im.name}.png'
        im.filepath_raw = str(p); im.file_format = 'PNG'; im.save()
    anim = kind in ('troops', 'mounts')
    bpy.ops.export_scene.fbx(filepath=str(out / f'{name}.fbx'), use_selection=False, apply_scale_options='FBX_SCALE_ALL',
                             add_leaf_bones=False, bake_anim=anim, bake_anim_use_all_actions=anim, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode='RELATIVE', embed_textures=False)
    print('OK', kind, name, len(bpy.data.actions), 'actions', flush=True)
