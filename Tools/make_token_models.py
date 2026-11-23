# 11-11 用户：令牌质感不好、字要在令牌上、注意英文版 → 程序建模的令牌（在 Blender 里跑）：
#   长条牌身（圆拱顶、底部收成钝尖好插地）+ 倒角；正反两面真刻槽（布尔差）、槽里嵌金（Inlay）；
#   上方刻令徽（中英共用：冲锋 = 向上箭头、坚守 = 盾、撤退 = 回头箭头），下方刻字：中文两个字竖排（马善政体），英文一行横排（Cinzel）。
# 输出 Assets/_WarTable/Art/Models/Tokens/token_<令>_<zh|en>.fbx（子物体 Body / Inlay），令徽外形写到 ArtSource/Tokens/emblems.json（下令徽章也用它画）。
# 用法：/Applications/Blender.app/Contents/MacOS/Blender -b -P Tools/make_token_models.py
import bpy, bmesh, json, math, os
from mathutils import Vector

R = '/Users/ld/Documents/ChatGPT/WarTable/'
OUT = R + 'Assets/_WarTable/Art/Models/Tokens/'
FONT = R + 'ArtSource/Fonts/'
os.makedirs(OUT, exist_ok=True); os.makedirs(R + 'ArtSource/Tokens', exist_ok=True)

H, W, T = 0.15, 0.057, 0.015          # 高 / 宽 / 厚（米）
GROOVE, INLAY_TOP = 0.0012, 0.00035   # 槽深；金面比牌面低多少
ORDERS = {'charge': ('冲锋', 'CHARGE'), 'hold': ('坚守', 'HOLD'), 'retreat': ('撤退', 'RETREAT')}


def emblem(key, n=24):
    """令徽外形（单位尺寸，中心在原点，约 1×1）。"""
    if key == 'charge':   # 向上的箭头（矛头 + 杆）
        return [(0, .5), (.34, .1), (.12, .1), (.12, -.5), (-.12, -.5), (-.12, .1), (-.34, .1)]
    if key == 'hold':     # 盾（上平、两侧往下收成尖）
        right = [(.38 * math.sqrt(max(0.0, 1 - (i / n) ** 2.2)), .46 - .96 * i / n) for i in range(n + 1)]   # 上宽、往下收成尖
        return [(-.38, .46)] + right + [(-x, z) for x, z in reversed(right[1:-1])]
    # retreat：回头箭头（右腿往上 → 顶上半圆 → 左腿往下，左腿末端是向下的箭头）
    ro, ri, cy = .36, .19, .06
    pts = [(ro, -.46), (ro, cy)]
    for i in range(1, n):
        a = math.pi * i / n; pts.append((ro * math.cos(a), cy + ro * math.sin(a)))
    pts += [(-ro, cy), (-ro, -.14), (-.52, -.14), (-(ro + ri) / 2, -.5), (-.03, -.14), (-ri, -.14), (-ri, cy)]
    for i in range(1, n):
        a = math.pi - math.pi * i / n; pts.append((ri * math.cos(a), cy + ri * math.sin(a)))
    pts += [(ri, cy), (ri, -.46)]
    return pts


def emblem_obj(key, cz, box_w, box_h, y0, depth, back):
    """书法令徽：ArtSource/Tokens/emblem_<令>.json（外轮廓 + 洞）→ 2D 曲线填充挤出 → 网格；中心在 (0, cz)，等比缩进 box_w × box_h。"""
    D = json.load(open(R + f'ArtSource/Tokens/emblem_{key}.json'))
    x0, y0b, x1, y1 = D['bbox']; S = min(box_w / (x1 - x0), box_h / (y1 - y0b)); cx, cy = (x0 + x1) / 2, (y0b + y1) / 2
    cu = bpy.data.curves.new('em_' + key, 'CURVE'); cu.dimensions = '2D'; cu.fill_mode = 'BOTH'; cu.extrude = depth / 2
    for piece in D['chars'][0]:
        for poly in [piece['outer']] + piece['holes']:
            sp = cu.splines.new('POLY'); sp.points.add(len(poly) - 1)
            for k, (x, y) in enumerate(poly): sp.points[k].co = ((x - cx) * S, (cy - y) * S, 0, 1)
            sp.use_cyclic_u = True
    ob = bpy.data.objects.new('em_' + key, cu); bpy.context.collection.objects.link(ob)
    ob.rotation_euler = (math.radians(90), 0, math.radians(180) if back else 0); ob.location = (0, y0, cz)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True); bpy.ops.object.convert(target='MESH'); ob.select_set(False)
    return ob


def outline():
    """牌身外形（x 宽、z 高），底部钝尖、顶部半圆拱。"""
    r = W / 2; tip = H * 0.06; pts = [(-W * .14, 0), (W * .14, 0), (W / 2, tip), (W / 2, H - r)]
    for k in range(1, 16):
        a = math.pi * k / 16; pts.append((r * math.cos(a), H - r + r * math.sin(a)))
    pts += [(-W / 2, H - r), (-W / 2, tip)]
    return pts


def prism(name, pts2d, depth, y0):
    """xz 平面的多边形，沿 y 从 y0 挤出 depth（y0 到 y0+depth）。"""
    bm = bmesh.new()
    vs = [bm.verts.new((x, y0, z)) for x, z in pts2d]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in ext['geom'] if isinstance(e, bmesh.types.BMVert)]: v.co.y += depth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob); return ob


def text_obj(name, s, font, size, x, z, y0, depth, back, offset=0.0, max_w=None):
    """刻字用的文字实体：正面（back=False）贴在 y = −T/2 面上朝 −y；背面朝 +y、左右翻转后从背面看是正的。"""
    cu = bpy.data.curves.new(name, 'FONT'); cu.body = s; cu.font = bpy.data.fonts.load(font, check_existing=True)
    cu.size = size; cu.align_x = 'CENTER'; cu.align_y = 'CENTER'; cu.extrude = depth / 2; cu.offset = offset; cu.resolution_u = 3
    ob = bpy.data.objects.new(name, cu); bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.update()
    if max_w:
        w = ob.dimensions.x
        if w > max_w: cu.size = size * max_w / w
    ob.rotation_euler = (math.radians(90), 0, math.radians(180) if back else 0)
    ob.location = (x if not back else -x, y0, z)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.convert(target='MESH'); ob.select_set(False)
    return ob


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join(); objs[0].name = name; return objs[0]


def marks(key, lang, y_face, depth, back):
    """一面的全部刻痕（令徽 + 字），y_face = 该面的 y，depth = 实体厚度（以 y_face 为中心）。"""
    zh, en = ORDERS[key]; s = 1 if back else -1   # 背面 x 翻转在 text_obj / 下面处理
    parts = []
    # 11-12：令徽换成书法笔意的剪影（Tools/token_emblems.py 提的轮廓），放进拱顶下方 宽 ≤ 62% 牌宽、高 ≤ 3.6 cm 的框里
    parts.append(emblem_obj(key, H - W / 2 - 0.0005, W * 0.62, 0.032, y_face, depth, back))   # 上提、高 3.2 cm：不碰下面的字
    if lang == 'zh':
        for i, ch in enumerate(zh):
            parts.append(text_obj('ch%d' % i, ch, FONT + 'MaShanZheng.ttf', 0.034, 0, 0.082 - i * 0.036, y_face, depth, back, offset=0.00038))
    else:
        # 英文用 Marcellus（OFL 静态字体，轮廓不重叠；Cinzel 可变字体的轮廓互相重叠，刻出来字母会缺块）
        parts.append(text_obj('w', en, FONT + 'Marcellus-Regular.ttf', 0.0175, 0, 0.066, y_face, depth, back, offset=0.0003, max_w=W * 0.8))
        for k, zz in enumerate((0.084, 0.049)):   # 字上下两道细刻线，中间一颗菱形
            parts.append(prism('rule%d' % k, [(-W * .3, zz - .0004), (W * .3, zz - .0004), (W * .3, zz + .0004), (-W * .3, zz + .0004)], depth, y_face - depth / 2))
        d = .0028; parts.append(prism('dia', [(0, .036 + d), (d, .036), (0, .036 - d), (-d, .036)], depth, y_face - depth / 2))
    return parts


def planar_uv(o):
    """物体空间投影 UV（11-12：漆面刷痕要顺着牌身长度走）：正反面沿厚度方向投（u = x、v = z），侧面从侧边投（u = y + x、v = z）。
    贴图一个周期 = 15 cm（整支令牌高）。"""
    me = o.data
    if not me.uv_layers: me.uv_layers.new()
    uv = me.uv_layers.active.data; k = 1 / H
    for p in me.polygons:
        side = abs(p.normal.y) < 0.6
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = ((co.y * 3 + co.x) * k if side else co.x * k, co.z * k)


def build(key, lang):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    body = prism('Body', outline(), T, -T / 2)
    bev = body.modifiers.new('bev', 'BEVEL'); bev.width = 0.0014; bev.segments = 3; bev.limit_method = 'ANGLE'; bev.angle_limit = math.radians(40)
    bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier='bev')
    # 字体轮廓的笔画彼此重叠（自相交），合成一个刀具做布尔会把牌身吃掉 → 每个部件（令徽 / 每个字 / 刻线）单独切，开自相交容错
    n0 = len(body.data.polygons)
    for cut in marks(key, lang, -T / 2, GROOVE * 2, False) + marks(key, lang, T / 2, GROOVE * 2, True):
        b = body.modifiers.new('bool', 'BOOLEAN'); b.operation = 'DIFFERENCE'; b.object = cut; b.solver = 'EXACT'
        b.use_self = True; b.use_hole_tolerant = True
        bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier='bool')
        bpy.data.objects.remove(cut)
    bpy.context.view_layer.update()
    print('BODY', key, lang, 'faces', n0, '->', len(body.data.polygons), 'dims', tuple(round(x, 4) for x in body.dimensions))
    # 金：槽底往上填到比牌面低 INLAY_TOP
    th = GROOVE - INLAY_TOP
    inl = join(marks(key, lang, -T / 2 + GROOVE - th / 2, th, False) + marks(key, lang, T / 2 - GROOVE + th / 2, th, True), 'Inlay')
    for o in (body, inl):
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.remove_doubles(threshold=1e-6); bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        planar_uv(o)
        for p in o.data.polygons: p.use_smooth = False
    # 牌身的大面做平滑着色、倒角保持硬边：按 30° 自动平滑
    body.data.set_sharp_from_angle(angle=math.radians(30)) if hasattr(body.data, 'set_sharp_from_angle') else None
    for p in body.data.polygons: p.use_smooth = True
    bpy.ops.object.select_all(action='SELECT')
    path = OUT + f'token_{key}_{lang}.fbx'
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True)
    print('TOKEN', key, lang, 'body tris', sum(len(p.vertices) - 2 for p in body.data.polygons), 'inlay tris', sum(len(p.vertices) - 2 for p in inl.data.polygons), '->', path)


for k in ORDERS:
    for l in ('zh', 'en'):
        build(k, l)
json.dump({k: emblem(k) for k in ORDERS}, open(R + 'ArtSource/Tokens/emblems.json', 'w'))
print('done')
