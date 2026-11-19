# 11-16 用户：界面往深空那种西方科幻奇幻风格靠（厚重装甲、炮铜 + 黄铜、发光透镜），不要之前的中国风；令牌被别人当成按钮、看不出能拿。
# → 程序建模（在 Blender 里跑）指挥台的实物和三支“令标”：
#   deck_pod_center  大按钮的装甲底座（八角厚盘 + 一圈螺栓 + 警示缺口）      原点 = 顶面中心，往下长
#   deck_cap         蘑菇头按钮帽（平顶，字贴在顶上；颜色由游戏里调）        原点 = 顶面中心
#   deck_switch      小一号的六角按钮帽（沉浸开关）                          原点 = 顶面中心
#   deck_pedestal    风格小人脚下的台座（两级圆台 + 一圈发光环 Glow）        原点 = 顶面中心
#   deck_pod_left    左托架：托着五个台座和沉浸开关的装甲板，前沿一条放名牌的斜面   原点 = 最靠中间那个台座的正下方（顶面）
#   deck_pod_right   右托架：三个插孔的令标架，前沿斜面放名牌                原点 = 顶面，插孔在 x = 0.04 + i × 0.058
#   deck_plate       名牌：36 × 18 mm 的铭牌，四周一圈凸边                      原点 = 底面中心，贴图铺满正面
#   beacon_<令>      令标：下面是插地的钢锥，中间六棱装甲筒 + 发光令徽（Glow），顶上一个大拉环——一看就是“捏住拉环拔出来”
#                    原点 = 锥尖，总高 0.15（游戏里缩到 0.73 倍，和旧令牌同一套尺寸）
# 11-16 晚 用户：“按钮的质感和旁边的标签现在就要做好，缺细节”→ 第二版：
#   · 分材质区（顶点色 Kind：炮铜 / 黄铜 / 黑铁 / 警示漆），多了黄铜包边、齿圈、嵌板、警示斜纹、散热槽、成排螺栓；
#   · 每件烘全套 PBR：颜色（遮蔽压暗缝隙、棱边磨亮、漆面掉漆、缝里积锈）、法线（拉丝 / 细颗粒 / 划痕 / 黄铜锤痕）、
#     金属度 + 光滑度（R = 金属度，A = 光滑度，Unity URP Lit 的 _MetallicGlossMap 格式）；
#   · deck_plate：名牌的实体（带凸边的小铭牌），字和底纹由 Tools/make_deck_labels.py 出贴图。
# 输出 Assets/_WarTable/Art/Models/Deck/*.fbx 和 Art/Textures/Deck/<名>_albedo.png
# 用法：/Applications/Blender.app/Contents/MacOS/Blender -b -P Tools/make_deck_models.py [-- 名字 …]
import bpy, bmesh, math, os, sys, json
from mathutils import Vector, Matrix

R = '/Users/ld/Documents/ChatGPT/WarTable/'
OUT = R + 'Assets/_WarTable/Art/Models/Deck/'
TEX = R + 'Assets/_WarTable/Art/Textures/Deck/'
os.makedirs(OUT, exist_ok=True); os.makedirs(TEX, exist_ok=True)
ONLY = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
EMB = json.load(open(R + 'ArtSource/Tokens/emblems.json')) if os.path.exists(R + 'ArtSource/Tokens/emblems.json') else {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    return sc


def obj_from(bm, name):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob


def prism(n, r0, r1, z0, z1, rot=0.0, name='p'):
    """n 边棱台：底面半径 r0（高 z0）、顶面 r1（高 z1）。Blender 里 z 朝上。"""
    bm = bmesh.new()
    lo = [bm.verts.new((r0 * math.cos(rot + 2 * math.pi * i / n), r0 * math.sin(rot + 2 * math.pi * i / n), z0)) for i in range(n)]
    hi = [bm.verts.new((r1 * math.cos(rot + 2 * math.pi * i / n), r1 * math.sin(rot + 2 * math.pi * i / n), z1)) for i in range(n)]
    bm.faces.new(reversed(lo)); bm.faces.new(hi)
    for i in range(n): bm.faces.new((lo[i], lo[(i + 1) % n], hi[(i + 1) % n], hi[i]))
    return obj_from(bm, name)


def lathe(n, profile, rot=0.0, name='l', closed=False):
    """把一条轮廓线 [(半径, 高), …] 绕 z 轴转成 n 边的回转体：各段共用顶点、中间没有多余的隔板（布尔运算才不会出错）。
    closed = 轮廓首尾相接（做圆环 / 套筒）；否则两头各封一个盖。"""
    bm = bmesh.new()
    rings = [[bm.verts.new((r * math.cos(rot + 2 * math.pi * i / n), r * math.sin(rot + 2 * math.pi * i / n), z)) for i in range(n)] for r, z in profile]
    m = len(rings)
    for k in range(m if closed else m - 1):
        a, b = rings[k], rings[(k + 1) % m]
        for i in range(n): bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    if not closed: bm.faces.new(reversed(rings[0])); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from(bm, name)


def box(x0, x1, y0, y1, z0, z1, name='b'):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((x0 + (v.co.x + .5) * (x1 - x0), y0 + (v.co.y + .5) * (y1 - y0), z0 + (v.co.z + .5) * (z1 - z0)))
    return obj_from(bm, name)


def torus(R0, r, z, seg=40, ring=12, name='t', rot=None, loc=None):
    bm = bmesh.new()
    vs = [[bm.verts.new(((R0 + r * math.cos(2 * math.pi * j / ring)) * math.cos(2 * math.pi * i / seg), (R0 + r * math.cos(2 * math.pi * j / ring)) * math.sin(2 * math.pi * i / seg), z + r * math.sin(2 * math.pi * j / ring))) for j in range(ring)] for i in range(seg)]
    for i in range(seg):
        for j in range(ring):
            bm.faces.new((vs[i][j], vs[(i + 1) % seg][j], vs[(i + 1) % seg][(j + 1) % ring], vs[i][(j + 1) % ring]))
    ob = obj_from(bm, name)
    if rot: ob.rotation_euler = rot
    if loc: ob.location = loc
    return ob


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(objs) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active; ob.name = name; ob.data.name = name
    return ob


def cut(a, b):
    """a 减去 b（布尔差），b 删掉。"""
    bpy.context.view_layer.update()
    m = a.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.object = b; m.solver = 'EXACT'
    bpy.context.view_layer.objects.active = a; bpy.ops.object.modifier_apply(modifier='cut')
    bpy.data.objects.remove(b, do_unlink=True)


def bevel(ob, w=0.0012, seg=2, angle=35):
    m = ob.modifiers.new('bev', 'BEVEL'); m.width = w; m.segments = seg; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(angle); m.harden_normals = False
    bpy.context.view_layer.objects.active = ob; bpy.ops.object.modifier_apply(modifier='bev')
    for p in ob.data.polygons: p.use_smooth = True
    try:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    except Exception: pass


def bolts(n, r, z, size=0.0028, h=0.0016, rot0=0.0):
    return [_at(prism(6, size, size * 0.86, 0, h, name='bolt'), (r * math.cos(rot0 + 2 * math.pi * i / n), r * math.sin(rot0 + 2 * math.pi * i / n), z)) for i in range(n)]


def _at(ob, loc, rot=None):
    ob.location = loc
    if rot: ob.rotation_euler = rot
    return ob


def emblem_mesh(key, size, thick, name='Glow'):
    """令徽（外形来自 ArtSource/Tokens/emblems.json，和旧令牌、下令徽章同一套）：在 XZ 平面里的一块薄板，正面朝 −Y。"""
    pts = EMB.get(key)
    if not pts: pts = [(-.3, -.4), (.3, -.4), (.3, .4), (-.3, .4)]
    bm = bmesh.new()
    front = [bm.verts.new((x * size, -thick, z * size)) for x, z in pts]
    back = [bm.verts.new((x * size, 0, z * size)) for x, z in pts]
    n = len(pts)
    try: bm.faces.new(front)
    except Exception: pass
    for i in range(n): bm.faces.new((front[i], back[i], back[(i + 1) % n], front[(i + 1) % n]))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], ngon_method='EAR_CLIP')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from(bm, name)


GUN, BRASS, BLACK, PAINT = (0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)


def paint(ob, kind):
    """给整个物体标材质区（顶点色 Kind）：炮铜 GUN / 黄铜 BRASS / 黑铁 BLACK / 警示漆 PAINT。合并、倒角、布尔之后都还在。"""
    me = ob.data
    a = me.color_attributes.get('Kind') or me.color_attributes.new(name='Kind', type='FLOAT_COLOR', domain='CORNER')
    a.data.foreach_set('color', list(kind + (1,)) * len(me.loops))
    return ob


def bake_pbr(ob, name, size=1024, neutral=False, paint_col=(0.78, 0.55, 0.06), stripes=True):
    """烘全套 PBR 到自动展开的 UV：<名>_albedo.png、_normal.png、_mask.png（R 金属度、A 光滑度）。
    neutral = 按钮帽：颜色由游戏里乘上去 → 颜色图烘成中性浅灰，只带缝隙的暗和棱边的亮。"""
    import numpy as np
    if not ob.data.color_attributes.get('Kind'): paint(ob, GUN)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.012)
    bpy.ops.object.mode_set(mode='OBJECT')
    mat = bpy.data.materials.new(name + '_bake'); mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    N = nt.nodes.new; L = nt.links.new

    def math_(op, a=None, b=None, c=None, clamp=False):
        n = N('ShaderNodeMath'); n.operation = op; n.use_clamp = clamp
        for i, v in enumerate((a, b, c)):
            if v is None: continue
            if isinstance(v, (int, float)): n.inputs[i].default_value = v
            else: L(v, n.inputs[i])
        return n.outputs[0]

    def mix(fac, a, b, blend='MIX'):
        n = N('ShaderNodeMixRGB'); n.blend_type = blend
        for i, v in enumerate((fac, a, b)):
            if isinstance(v, (int, float)): n.inputs[i].default_value = v if i == 0 else (v, v, v, 1)   # 第 0 个是混合系数，后两个是颜色口
            elif isinstance(v, tuple): n.inputs[i].default_value = (*v, 1)
            else: L(v, n.inputs[i])
        return n.outputs[0]

    def noise(scale, detail=4.0, stretch=None):
        n = N('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        if stretch:
            co = N('ShaderNodeTexCoord'); mp = N('ShaderNodeMapping'); mp.inputs['Scale'].default_value = stretch
            L(co.outputs['Object'], mp.inputs['Vector']); L(mp.outputs['Vector'], n.inputs['Vector'])
        return n.outputs['Fac']

    out = N('ShaderNodeOutputMaterial'); em = N('ShaderNodeEmission'); bsdf = N('ShaderNodeBsdfPrincipled')
    # 区域
    vc = N('ShaderNodeVertexColor'); vc.layer_name = 'Kind'; sep = N('ShaderNodeSeparateColor'); L(vc.outputs['Color'], sep.inputs[0])
    kBrass, kBlack, kPaint = sep.outputs[0], sep.outputs[1], sep.outputs[2]
    # 遮蔽、棱边
    ao = N('ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.inputs['Distance'].default_value = 0.01
    geo = N('ShaderNodeNewGeometry'); bev = N('ShaderNodeBevel'); bev.samples = 8; bev.inputs['Radius'].default_value = 0.0014
    dot = N('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'; L(geo.outputs['Normal'], dot.inputs[0]); L(bev.outputs['Normal'], dot.inputs[1])
    nEdge = noise(260, 3)
    edge = math_('MULTIPLY', math_('MULTIPLY', math_('SUBTRACT', 1.0, dot.outputs['Value']), 11.0, clamp=True), math_('MULTIPLY_ADD', nEdge, 0.9, 0.45), clamp=True)   # 棱边磨损（不均匀）
    crev = math_('SUBTRACT', 1.0, ao.outputs['AO'], clamp=True)                                                    # 缝隙
    nBig, nFine = noise(34, 4), noise(520, 6)
    nBrush = noise(90, 2, stretch=(1, 60, 1))        # 拉丝（沿 x 方向的细纹）
    nScr = math_('GREATER_THAN', noise(26, 1, stretch=(1, 28, 1)), 0.72)   # 稀疏的长划痕
    vor = N('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 190   # 黄铜的锤痕
    # 警示斜纹（漆区）：沿 x + z 的波纹取阈值
    wave = N('ShaderNodeTexWave'); wave.inputs['Scale'].default_value = 55; wave.bands_direction = 'DIAGONAL'; wave.inputs['Distortion'].default_value = 0
    wco = N('ShaderNodeTexCoord'); L(wco.outputs['Object'], wave.inputs['Vector'])   # 用物体坐标（米）：斜纹宽度不随零件大小变
    stripe = math_('GREATER_THAN', wave.outputs['Fac'], 0.5) if stripes else None
    chip = math_('GREATER_THAN', math_('ADD', noise(150, 5), math_('MULTIPLY', edge, 0.6)), 0.62)   # 掉漆：噪声高的地方 + 棱边
    # ── 颜色
    if neutral:
        base = mix(math_('MULTIPLY_ADD', nFine, 0.08, 0.0), (0.86, 0.86, 0.86), (0.93, 0.93, 0.93))
        col = mix(math_('MULTIPLY', crev, 0.75, clamp=True), base, (0.25, 0.25, 0.25))
        col = mix(math_('MULTIPLY', edge, 0.55), col, (1, 1, 1))
        metal_v = 0.0; smooth = math_('MULTIPLY_ADD', nFine, -0.08, 0.84)
    else:
        gun = mix(math_('MULTIPLY_ADD', nBig, 0.7, -0.2, clamp=True), (0.115, 0.125, 0.145), (0.17, 0.165, 0.16))
        gun = mix(math_('MULTIPLY_ADD', nBrush, 0.5, 0.0), gun, (0.2, 0.21, 0.235))
        brass = mix(math_('MULTIPLY_ADD', nBig, 0.8, -0.1, clamp=True), (0.5, 0.33, 0.11), (0.66, 0.47, 0.17))
        black = mix(nFine, (0.028, 0.028, 0.032), (0.05, 0.05, 0.055))
        pcol = mix(stripe, (0.03, 0.03, 0.03), paint_col) if stripes else mix(0.0, paint_col, paint_col)
        col = mix(kBrass, gun, brass); col = mix(kBlack, col, black)
        paintMask = math_('MULTIPLY', kPaint, math_('SUBTRACT', 1.0, chip), clamp=True)     # 漆区里没掉漆的地方
        col = mix(paintMask, col, pcol)
        # 缝里积灰 / 锈
        col = mix(math_('MULTIPLY', crev, 0.85, clamp=True), col, (0.035, 0.028, 0.022))
        rust = math_('MULTIPLY', math_('MULTIPLY', crev, math_('GREATER_THAN', nBig, 0.5)), 0.5)
        col = mix(rust, col, (0.28, 0.14, 0.06))
        # 棱边磨亮：炮铜露出亮钢，黄铜更亮
        edgeCol = mix(kBrass, (0.6, 0.6, 0.58), (0.95, 0.8, 0.42))
        col = mix(math_('MULTIPLY', edge, 0.85), col, edgeCol)
        col = mix(math_('MULTIPLY', nScr, 0.18), col, (0.5, 0.5, 0.5))
        metal_n = math_('SUBTRACT', 1.0, math_('MULTIPLY', paintMask, 0.92), clamp=True)
        metal_n = math_('SUBTRACT', metal_n, math_('MULTIPLY', crev, 0.5), clamp=True)
        sm = mix(kBrass, 0.4, 0.62); sm = mix(kBlack, sm, 0.3); sm = mix(paintMask, sm, 0.5)
        smooth = math_('ADD', sm, math_('MULTIPLY', edge, 0.22), clamp=True)
        smooth = math_('SUBTRACT', smooth, math_('MULTIPLY', crev, 0.3), clamp=True)
        smooth = math_('SUBTRACT', smooth, math_('MULTIPLY', nBrush, 0.1), clamp=True)
    # ── 凹凸（烘成法线）
    if neutral:
        h = math_('MULTIPLY', nFine, 0.12)
    else:
        h = math_('ADD', math_('MULTIPLY', nFine, 0.22), math_('MULTIPLY', nBrush, 0.3))
        h = math_('SUBTRACT', h, math_('MULTIPLY', nScr, 0.5))
        h = math_('ADD', h, math_('MULTIPLY', math_('MULTIPLY', kBrass, vor.outputs['Distance']), 0.7))
        h = math_('ADD', h, math_('MULTIPLY', paintMask, 0.55))      # 漆有厚度：掉漆的地方凹下去
    bump = N('ShaderNodeBump'); bump.inputs['Strength'].default_value = 1.0; bump.inputs['Distance'].default_value = 0.00055   # 再大表面就发“砂”
    L(h, bump.inputs['Height']); L(bump.outputs['Normal'], bsdf.inputs['Normal'])

    ob.data.materials.clear(); ob.data.materials.append(mat)
    bpy.context.scene.render.bake.margin = 8

    def bake(kind, img_name, src=None, data=False):
        img = bpy.data.images.new(img_name, size, size, alpha=False)
        if data: img.colorspace_settings.name = 'Non-Color'
        tex = N('ShaderNodeTexImage'); tex.image = img; nt.nodes.active = tex
        for l in list(out.inputs[0].links): nt.links.remove(l)
        if kind == 'EMIT':
            for l in list(em.inputs[0].links): nt.links.remove(l)
            if isinstance(src, (int, float)): em.inputs[0].default_value = (src, src, src, 1)
            else: L(src, em.inputs[0])
            L(em.outputs[0], out.inputs[0]); bpy.ops.object.bake(type='EMIT')
        else:
            L(bsdf.outputs[0], out.inputs[0]); bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
        return img

    a = bake('EMIT', name + '_albedo', col); a.filepath_raw = TEX + name + '_albedo.png'; a.file_format = 'PNG'; a.save()
    nm = bake('NORMAL', name + '_normal', data=True); nm.filepath_raw = TEX + name + '_normal.png'; nm.file_format = 'PNG'; nm.save()
    im = bake('EMIT', name + '_m', metal_v if neutral else metal_n, data=True); isg = bake('EMIT', name + '_s', smooth, data=True)
    pm = np.empty(size * size * 4, np.float32); ps = np.empty(size * size * 4, np.float32); im.pixels.foreach_get(pm); isg.pixels.foreach_get(ps)
    pm = pm.reshape(-1, 4); ps = ps.reshape(-1, 4)
    mask = bpy.data.images.new(name + '_mask', size, size, alpha=True); mask.colorspace_settings.name = 'Non-Color'; mask.alpha_mode = 'STRAIGHT'
    o = np.zeros((size * size, 4), np.float32); o[:, 0] = pm[:, 0]; o[:, 1] = 1.0; o[:, 3] = ps[:, 0]
    mask.pixels.foreach_set(o.ravel()); mask.filepath_raw = TEX + name + '_mask.png'; mask.file_format = 'PNG'; mask.save()


def export(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    # Blender z 朝上 → Unity y 朝上。实测导进 Unity 后绕竖轴转了 180°（Unity x = −Blender x，z = −Blender y）→ ConsoleDeck.Part 里把模型再转 180° 抵掉，
    # 所以这里的坐标按“x = 玩家右手、−y = 朝玩家”来写就行
    bpy.ops.export_scene.fbx(filepath=OUT + name + '.fbx', use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, colors_type='NONE')
    print('DECK', name, sum(len(o.data.polygons) for o in objs), 'faces')


def want(name): return not ONLY or name in ONLY


def brass_bolts(n, r, z, **kw): return [paint(b, BRASS) for b in bolts(n, r, z, **kw)]


def bolt_row(x0, x1, y, z, n, size=0.0022):
    return [paint(_at(prism(6, size, size * 0.86, 0, 0.0013, name='bolt'), (x0 + (x1 - x0) * i / (n - 1), y, z)), BRASS) for i in range(n)]


# ------------------------------------------------------------ 大按钮底座
if want('deck_pod_center'):
    reset()
    r = 0.052
    body = paint(lathe(8, [(r * 0.94, -0.034), (r, -0.012), (r, -0.005), (r * 0.92, 0.0)], rot=math.pi / 8, name='Body'), GUN)
    cut(body, paint(prism(32, 0.0375, 0.0375, -0.006, 0.01, name='well'), BLACK))            # 帽子坐进去的井
    cut(body, paint(lathe(8, [(r * 1.02, -0.0235), (r * 0.985, -0.0235), (r * 0.985, -0.0215), (r * 1.02, -0.0215)], rot=math.pi / 8, name='groove', closed=True), BLACK))   # 腰上一圈嵌线
    bevel(body, 0.0011)
    # 顶面八块斜面刷警示斜纹：盖一圈薄薄的漆板（八边形环）
    hazard = paint(lathe(8, [(r * 0.925, 0.0002), (r * 0.995, -0.0046), (r * 0.995, -0.0052), (r * 0.925, -0.0004)], rot=math.pi / 8, name='hazard', closed=True), PAINT)
    collar = paint(lathe(40, [(0.0378, -0.001), (0.0422, -0.001), (0.0422, 0.0034), (0.0408, 0.0046), (0.0378, 0.0046)], name='collar', closed=True), BRASS)   # 帽子外面的黄铜护环
    bevel(collar, 0.0005)
    # 齿圈：护环外面一圈 20 个黑铁齿（像一只大齿轮托着按钮）
    teeth = []
    for i in range(20):
        a = 2 * math.pi * i / 20
        t = paint(box(-0.0022, 0.0022, -0.0016, 0.0016, -0.0006, 0.003, 'tooth'), BLACK); t.location = (0.0447 * math.cos(a), 0.0447 * math.sin(a), 0); t.rotation_euler = (0, 0, a + math.pi / 2)
        teeth.append(t)
    body = join([body, hazard, collar] + teeth + brass_bolts(8, 0.0489, -0.0046, rot0=math.pi / 8, size=0.0021, h=0.0014), 'Body')
    bake_pbr(body, 'deck_pod_center'); export([body], 'deck_pod_center')

# ------------------------------------------------------------ 按钮帽 / 小帽
for nm, n, rr, hh in (('deck_cap', 48, 0.036, 0.018), ('deck_switch', 6, 0.0235, 0.015)):
    if not want(nm): continue
    reset()
    # 顶面微微鼓起（中心比边缘高 0.6 mm）、外圈一道环槽，边缘圆角
    prof = [(rr * 0.9, -hh), (rr, -hh * 0.45), (rr, -0.0026), (rr * 0.965, -0.0008), (rr * 0.9, -0.0006), (rr * 0.87, -0.0011), (rr * 0.84, -0.0005), (rr * 0.5, -0.0001), (rr * 0.02, 0.0)]
    cap = paint(lathe(n, prof, name='Body'), GUN)
    bevel(cap, 0.0006)
    bake_pbr(cap, nm, 512, neutral=True); export([cap], nm)

# ------------------------------------------------------------ 风格台座
if want('deck_pedestal'):
    reset()
    r = 0.021
    ped = paint(lathe(24, [(r * 1.12, -0.012), (r * 1.12, -0.0085), (r * 1.02, -0.0065), (r * 1.02, -0.0035), (r * 0.97, -0.0035), (r * 0.97, 0.0)], name='Body'), GUN)
    bevel(ped, 0.0006)
    trim = paint(lathe(24, [(r * 1.035, -0.0062), (r * 1.06, -0.0062), (r * 1.06, -0.0038), (r * 1.035, -0.0038)], name='trim', closed=True), BRASS)   # 腰上一道黄铜箍
    bevel(trim, 0.0003)
    studs = [paint(_at(prism(6, 0.0013, 0.0011, 0, 0.0009, name='stud'), (r * 1.07 * math.cos(2 * math.pi * i / 8), r * 1.07 * math.sin(2 * math.pi * i / 8), -0.0086)), BRASS) for i in range(8)]
    ped = join([ped, trim] + studs, 'Body')
    bake_pbr(ped, 'deck_pedestal', 512)
    glow = torus(r * 0.86, 0.0011, 0.0003, name='Glow')                            # 顶面一圈发光环（当前风格亮金色）
    export([ped, glow], 'deck_pedestal')


def pod_block(x0, x1, depth, name):
    """托架主体：顶面平台 + 朝玩家（−y）一条往下斜的名牌面。"""
    block = box(x0, x1, -0.03, 0.032, -depth, 0.0, 'block')
    lip = box(x0, x1, -0.062, -0.03, -depth, 0.0, 'lip')
    for v in lip.data.vertices:
        if v.co.y < -0.05 and v.co.z > -0.01: v.co.z = -0.024      # 把外上棱压下去 → 斜面
    body = join([block, lip], name)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=1e-5); bpy.ops.object.mode_set(mode='OBJECT')
    return paint(body, GUN)


def pod_trim(x0, x1, depth):
    """托架共用的细节：两端刷警示漆的加强肋、顶面后沿一条黄铜压条和一排螺栓、背面的散热槽。"""
    parts = []
    for x in (x0 + 0.0035, x1 - 0.0035):
        parts.append(paint(box(x - 0.0045, x + 0.0045, -0.03, 0.033, -depth, 0.0038, 'rib'), PAINT))
        parts.append(paint(box(x - 0.0047, x + 0.0047, -0.0625, -0.03, -depth, -0.0005, 'ribfoot'), GUN))
    parts.append(paint(box(x0 + 0.01, x1 - 0.01, 0.0265, 0.0295, -0.001, 0.0012, 'strip'), BRASS))
    parts += bolt_row(x0 + 0.016, x1 - 0.016, 0.028, 0.0012, max(3, int((x1 - x0) / 0.03)))
    return parts


# ------------------------------------------------------------ 左托架（五个台座 + 放大开关）
# 11-17 用户：风格小兵人小一点更精致、整体往中间靠 → 台座间距 4 cm（原 5.2），台座直径 3 cm（模型在游戏里缩到 0.72 倍）
if want('deck_pod_left'):
    reset()
    x0, x1 = -0.236, 0.027
    body = pod_block(x0, x1, 0.03, 'Body')
    for i in range(5):                                                           # 台座坐进去的浅井
        w = paint(prism(24, 0.0182, 0.0182, -0.003, 0.01, name='well'), BLACK); w.location = (-(i * 0.04), 0, 0); cut(body, w)
    w = paint(prism(6, 0.0215, 0.0215, -0.003, 0.01, name='well'), BLACK); w.location = (-(5 * 0.04 + 0.006), 0, 0); cut(body, w)
    for i in range(7):                                                           # 背面一排散热槽
        cut(body, paint(_at(box(-0.004, 0.004, 0.0305, 0.034, -0.024, -0.008, 'vent'), (x0 + 0.03 + i * 0.034, 0, 0)), BLACK))
    bevel(body, 0.0012)
    body = join([body] + pod_trim(x0, x1, 0.03), 'Body')
    bake_pbr(body, 'deck_pod_left'); export([body], 'deck_pod_left')

# ------------------------------------------------------------ 军令托架（三个按钮）
# 11-17 用户：令标还是难捏 → 换成按钮，按哪个就是哪道令。三个圆井，每个井口一圈黄铜护环 + 一圈黑铁齿（和大按钮一个语言，小一号）
if want('deck_pod_orders'):
    reset()
    x0, x1 = 0.002, 0.174
    body = pod_block(x0, x1, 0.03, 'Body')
    extra = []
    for i in range(3):
        x = 0.036 + i * 0.052
        w = paint(prism(32, 0.0222, 0.0222, -0.006, 0.01, name='well'), BLACK); w.location = (x, 0, 0); cut(body, w)
        c = paint(lathe(32, [(0.0224, -0.001), (0.0252, -0.001), (0.0252, 0.0026), (0.0242, 0.0036), (0.0224, 0.0036)], name='collar', closed=True), BRASS); c.location = (x, 0, 0)
        bevel(c, 0.0004); extra.append(c)
        for k in range(12):
            a = 2 * math.pi * k / 12
            t = paint(box(-0.0015, 0.0015, -0.0011, 0.0011, -0.0004, 0.0022, 'tooth'), BLACK); t.location = (x + 0.0268 * math.cos(a), 0.0268 * math.sin(a), 0); t.rotation_euler = (0, 0, a + math.pi / 2)
            extra.append(t)
    for i in range(4):
        cut(body, paint(_at(box(-0.004, 0.004, 0.0305, 0.034, -0.024, -0.008, 'vent'), (x0 + 0.03 + i * 0.038, 0, 0)), BLACK))
    bevel(body, 0.0012)
    body = join([body] + extra + pod_trim(x0, x1, 0.03), 'Body')
    bake_pbr(body, 'deck_pod_orders'); export([body], 'deck_pod_orders')

# ------------------------------------------------------------ 右托架（三个插孔的令标架）
if want('deck_pod_right'):
    reset()
    x0, x1 = 0.002, 0.196
    body = pod_block(x0, x1, 0.052, 'Body')
    collars = []
    for i in range(3):
        x = 0.04 + i * 0.058
        w = paint(prism(6, 0.0125, 0.0105, -0.046, 0.01, name='socket'), BLACK); w.location = (x, 0, 0); cut(body, w)
        c = paint(lathe(6, [(0.0128, -0.0005), (0.019, -0.0005), (0.019, 0.003), (0.0165, 0.0048), (0.0128, 0.0048)], name='collar', closed=True), BRASS); c.location = (x, 0, 0)
        bevel(c, 0.0005); collars.append(c)
    for i in range(5):
        cut(body, paint(_at(box(-0.004, 0.004, 0.0305, 0.034, -0.04, -0.012, 'vent'), (x0 + 0.03 + i * 0.034, 0, 0)), BLACK))
    bevel(body, 0.0012)
    body = join([body] + collars + pod_trim(x0, x1, 0.052), 'Body')
    bake_pbr(body, 'deck_pod_right'); export([body], 'deck_pod_right')

# ------------------------------------------------------------ 名牌（铭牌实体；字和底纹是贴图）
if want('deck_plate'):
    reset()
    bm = bmesh.new()
    hw, hh, t = 0.018, 0.009, 0.0014
    # 正面一整块（贴图铺满）+ 四周斜边 + 侧壁：正面在 z = t，底在 z = 0；Blender 里牌面朝 +z，x 向右，y 向上
    f0 = [bm.verts.new((sx * (hw - 0.0006), sy * (hh - 0.0006), t)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    f1 = [bm.verts.new((sx * hw, sy * hh, t - 0.0006)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    f2 = [bm.verts.new((sx * hw, sy * hh, 0)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bm.faces.new(f0)
    for i in range(4):
        bm.faces.new((f0[i], f1[i], f1[(i + 1) % 4], f0[(i + 1) % 4])); bm.faces.new((f1[i], f2[i], f2[(i + 1) % 4], f1[(i + 1) % 4]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uv = bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = ((l.vert.co.x / (hw * 2) + 0.5), (l.vert.co.y / (hh * 2) + 0.5))      # 侧壁取贴图最边上一圈像素
    plate = obj_from(bm, 'Plate')
    for p_ in plate.data.polygons: p_.use_smooth = False
    export([plate], 'deck_plate')

# ------------------------------------------------------------ 令标 ×3
for key in ('charge', 'hold', 'retreat'):
    nm = 'beacon_' + key
    if not want(nm) and not want('beacons'): continue
    reset()
    body = paint(lathe(6, [(0.0115, 0.048), (0.0135, 0.056), (0.0135, 0.104), (0.0105, 0.112)], name='Body'), GUN)   # 六棱装甲筒（平面朝前后）
    for s_ in (-1, 1):                                                           # 正反两面各开一个放令徽的浅窗
        cut(body, paint(box(-0.0058, 0.0058, s_ * 0.0132 - 0.003, s_ * 0.0132 + 0.003, 0.064, 0.099, 'win'), BLACK))
    for a_ in (60, 120, 240, 300):                                               # 另外四个面各两道散热槽
        for z_ in (0.072, 0.088):
            v_ = paint(box(-0.004, 0.004, 0.0105, 0.0135, z_ - 0.0022, z_ + 0.0022, 'vent'), BLACK); v_.rotation_euler = (0, 0, math.radians(a_ - 90)); cut(body, v_)
    bevel(body, 0.0007)
    guard = paint(lathe(6, [(0.0086, 0.0425), (0.012, 0.0425), (0.012, 0.0485), (0.0086, 0.0485)], name='guard', closed=True), BRASS)   # 锥根的黄铜护盘
    bevel(guard, 0.0004)
    cap_ = paint(lathe(6, [(0.0108, 0.1115), (0.0118, 0.1115), (0.0118, 0.1135), (0.0108, 0.1135)], name='capring', closed=True), BRASS)  # 肩上一道黄铜箍
    spike = paint(prism(4, 0.0005, 0.0064, 0.0, 0.0435, rot=math.pi / 4, name='spike'), GUN)        # 四棱钢锥（插地的那头）
    neck = paint(prism(12, 0.0062, 0.0062, 0.1115, 0.1215, name='neck'), BLACK)
    ring = paint(torus(0.0125, 0.0032, 0.0, seg=40, ring=12, name='ring', rot=(math.pi / 2, 0, 0), loc=(0, 0, 0.1345)), BRASS)   # 顶上的大拉环（立着，正对玩家）：黄铜，最显眼
    body = join([body, guard, cap_, spike, neck, ring] + brass_bolts(6, 0.0096, 0.1128, size=0.0013, h=0.0009), 'Body')
    bake_pbr(body, nm, 1024, stripes=False)
    glows = []
    for s_ in (-1, 1):
        e = emblem_mesh(key, 0.0108, 0.0012); e.location = (0, s_ * 0.0102, 0.0815)
        if s_ > 0: e.rotation_euler = (0, 0, math.pi)
        glows.append(e)
    band = lathe(6, [(0.0137, 0.0575), (0.0142, 0.0575), (0.0142, 0.0605), (0.0137, 0.0605)], name='band', closed=True)   # 筒底一圈色带（远看也分得出是哪一支）
    glow = join(glows + [band], 'Glow')
    export([body, glow], nm)
print('DECK done')
