"""标题页巨型机甲（也是最终 Boss）的骨骼绑定（在 Blender 里运行）。
模型来自 Tripo 官网（智能网格 + 局部编辑 + 纹理生成），是一整块网格、但由 140 个互不相连的零件块组成。
做法：零件块按位置分给骨骼（每个零件整块跟一根骨头走，权重 1，不拉伸变形）；唯一一处粘在一起的
“盆骨 + 左大腿”按 y 平面切开。骨骼层级（机甲面朝 +X，右手 = −Y 一侧的转轮炮）：
  Root ─ Pelvis ─ Torso（腰部转盘，绕竖轴转）─ ShoulderR ─ CannonBarrel（炮管自转）
                                          └ ShoulderL ─ SawBlade
                └ ThighR ─ ShinR ─ FootR
                └ ThighL ─ ShinL ─ FootL
用法：Blender -b --python Tools/titan_rig.py -- <带贴图的模型.fbx> <输出.fbx> <检查图前缀>
输出：带骨骼和蒙皮的 FBX（贴图打包在里面）+ 按骨骼上色的检查图 + 试摆姿势的检查图。"""
import bpy, bmesh, sys, math, json
from mathutils import Vector, Matrix, kdtree

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT, SHOT = argv[0], argv[1], argv[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
for o in list(bpy.context.scene.objects):
    if o != mesh and o.type != 'MESH': bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active = mesh; mesh.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mesh.name = 'Titan'

# ── 先删掉链锯电机下面垂着的几根软管（11-20 用户：连接处线缆要整洁、不要垂着的线）：三根独立的管子，按位置认
bm0 = bmesh.new(); bm0.from_mesh(mesh.data); bm0.verts.ensure_lookup_table()
seen = set(); kill = []
for v in bm0.verts:
    if v.index in seen: continue
    st = [v]; seen.add(v.index); g = []
    while st:
        x = st.pop(); g.append(x)
        for e in x.link_edges:
            w = e.other_vert(x)
            if w.index not in seen: seen.add(w.index); st.append(w)
    if not (120 <= len(g) <= 200): continue
    c = sum((x.co for x in g), Vector()) / len(g)
    if -0.12 < c.x < 0.03 and 0.26 < c.y < 0.33 and -0.08 < c.z < 0.16: kill += g
print('删掉的软管顶点', len(kill))
bmesh.ops.delete(bm0, geom=kill, context='VERTS'); bm0.to_mesh(mesh.data); bm0.free(); mesh.data.update()

# ── 零件块
bm = bmesh.new(); bm.from_mesh(mesh.data); bm.verts.ensure_lookup_table()
lab = [-1] * len(bm.verts); isl = []
for v in bm.verts:
    if lab[v.index] >= 0: continue
    st = [v]; lab[v.index] = len(isl); g = []
    while st:
        x = st.pop(); g.append(x.index)
        for e in x.link_edges:
            w = e.other_vert(x)
            if lab[w.index] < 0: lab[w.index] = len(isl); st.append(w)
    isl.append(g)
co = [v.co.copy() for v in bm.verts]
def stats(g):
    ps = [co[i] for i in g]
    mn = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    mx = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    return sum(ps, Vector()) / len(ps), mn, mx
S = [stats(g) for g in isl]
order = sorted(range(len(isl)), key=lambda i: -len(isl[i]))
print('最大的块', [(len(isl[i]), [round(c, 3) for c in S[i][0]]) for i in order[:16]])

# ── 主要零件按位置认（阈值按这台模型的尺寸：高 1.0，z −0.5..0.5，面朝 +X）
major = {}
torso = order[0]; major[torso] = 'Torso'
for i in order[1:40]:
    n = len(isl[i]); c, mn, mx = S[i]
    if n < 250: continue
    if mx.x > 0.3 and 0.0 < c.y < 0.26 and c.z < 0.02: major[i] = 'SawBlade'
    elif mx.y < -0.26 and mn.x > -0.05 and abs(c.z) < 0.06 and n > 300: major[i] = 'CannonBarrel'
    elif c.y < -0.15 and c.z > 0.0 and n > 1000: major[i] = 'ShoulderR'
    elif c.y > 0.17 and c.z > -0.1 and n > 1000: major[i] = 'ShoulderL'
    elif abs(c.y - 0.06) < 0.1 and -0.15 < c.z < 0.02 and n > 2000: major[i] = 'Pelvis'
    elif c.z > 0.2: major[i] = 'Torso'
    elif c.y < -0.06:
        major[i] = 'ThighR' if c.z > -0.2 else 'ShinR' if c.z > -0.38 else 'FootR'
    elif c.y > 0.06:
        major[i] = 'ThighL' if c.z > -0.2 else 'ShinL' if c.z > -0.38 else 'FootL'
print('主要零件', sorted([(len(isl[i]), b) for i, b in major.items()], key=lambda t: -t[0]))

# ── 其余小块：分给最近的主要零件
trees = {}
for i in major:
    t = kdtree.KDTree(len(isl[i]))
    for k, vi in enumerate(isl[i]): t.insert(co[vi], k)
    t.balance(); trees[i] = t
bone_of_isl = dict(major)
for i in range(len(isl)):
    if i in bone_of_isl: continue
    best, bd = None, 9.0
    c, mn, mx = S[i]
    for j, t in trees.items():
        cj, mnj, mxj = S[j]
        if any(mn[k] > mxj[k] + 0.05 or mnj[k] > mx[k] + 0.05 for k in range(3)): continue
        d = min(t.find(co[vi])[2] for vi in isl[i][::max(1, len(isl[i]) // 40)])
        if d < bd: bd, best = d, j
    bone_of_isl[i] = major[best] if best is not None else 'Torso'

# 顶点 → 骨骼；盆骨块里 y > 切分面的部分是左大腿（两者在模型里粘成了一块）
YSPLIT = 0.076
vbone = [None] * len(co)
for i, g in enumerate(isl):
    b = bone_of_isl[i]
    for vi in g:
        vbone[vi] = 'ThighL' if (b == 'Pelvis' and co[vi].y > YSPLIT) else b

def pts(b): return [co[i] for i in range(len(co)) if vbone[i] == b]
def cen(ps): return sum(ps, Vector()) / len(ps)
def top(ps, frac=0.08):
    z1 = max(p.z for p in ps); z0 = min(p.z for p in ps); h = z1 - z0
    return cen([p for p in ps if p.z > z1 - h * frac])
def bot(ps, frac=0.08):
    z1 = max(p.z for p in ps); z0 = min(p.z for p in ps); h = z1 - z0
    return cen([p for p in ps if p.z < z0 + h * frac])

P = {b: pts(b) for b in set(vbone)}
print('各骨骼顶点数', {b: len(v) for b, v in P.items()})
tors = P['Torso']; pel = P['Pelvis']
waist_z = (min(p.z for p in tors) + max(p.z for p in pel)) / 2
# 转盘的圆心 = 背壳最底下那一圈（转盘环）顶点的平均位置（圆环对称，平均就是圆心）；用盆骨的中心会偏，转起来会蹭
tz0 = min(p.z for p in tors)
ring = [p for p in tors if p.z < tz0 + 0.012]
waist = Vector((cen(ring).x, cen(ring).y, waist_z))
print('转盘圆心', [round(c, 4) for c in waist], '用了', len(ring), '个顶点')
joints = {
    'Root': (Vector((waist.x, waist.y, -0.5)), Vector((waist.x, waist.y, -0.42))),
    'Pelvis': (Vector((waist.x, waist.y, waist_z - 0.12)), waist),
    'Torso': (waist, Vector((waist.x, waist.y, waist_z + 0.25))),
}
for s in 'RL':
    sh = P['Shoulder' + s]; joints['Shoulder' + s] = (top(sh, 0.06), top(sh, 0.06) + Vector((0, 0, -0.18)))
    th = P['Thigh' + s]; sn = P['Shin' + s]; ft = P['Foot' + s]
    hip = top(th, 0.12); knee = (bot(th, 0.1) + top(sn, 0.1)) / 2; ankle = (bot(sn, 0.1) + top(ft, 0.15)) / 2
    joints['Thigh' + s] = (hip, knee); joints['Shin' + s] = (knee, ankle); joints['Foot' + s] = (ankle, Vector((ankle.x + 0.08, ankle.y, min(p.z for p in ft))))
# 炮管：沿最长方向（x）；链锯：沿锯身最长方向
import numpy as np
# 炮管：六根管围一圈，全部顶点的平均点在转轴上；转轴 = 主方向（PCA）
bar = P['CannonBarrel']; B_ = np.array([[p.x, p.y, p.z] for p in bar]); bm_ = B_.mean(0); bu = np.linalg.svd(B_ - bm_)[2][0]
if bu[0] < 0: bu = -bu
bt = (B_ - bm_) @ bu
joints['CannonBarrel'] = (Vector(bm_ + bu * bt.min()), Vector(bm_ + bu * bt.max()))
saw = P['SawBlade']
A = np.array([[p.x, p.y, p.z] for p in saw]); mu = A.mean(0); u = np.linalg.svd(A - mu)[2][0]
t = (A - mu) @ u; ax = Vector(u)
if ax.x < 0: ax = -ax; t = -t
joints['SawBlade'] = (Vector(mu) - ax * abs(t.min()), Vector(mu) + ax * abs(t.max()))
parent = {'Pelvis': 'Root', 'Torso': 'Pelvis', 'ShoulderR': 'Torso', 'ShoulderL': 'Torso', 'CannonBarrel': 'ShoulderR', 'SawBlade': 'ShoulderL',
          'ThighR': 'Pelvis', 'ShinR': 'ThighR', 'FootR': 'ShinR', 'ThighL': 'Pelvis', 'ShinL': 'ThighL', 'FootL': 'ShinL'}
json.dump({k: [list(v[0]), list(v[1])] for k, v in joints.items()}, open(SHOT + '_joints.json', 'w'), indent=1)
print('关节', {k: [round(c, 3) for c in v[0]] for k, v in joints.items()})

# ── 骨架
arm_d = bpy.data.armatures.new('TitanRig'); arm = bpy.data.objects.new('TitanRig', arm_d)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for name in ['Root', 'Pelvis', 'Torso', 'ShoulderR', 'ShoulderL', 'CannonBarrel', 'SawBlade', 'ThighR', 'ShinR', 'FootR', 'ThighL', 'ShinL', 'FootL']:
    b = arm_d.edit_bones.new(name); b.head, b.tail = joints[name]
    if (b.tail - b.head).length < 1e-3: b.tail = b.head + Vector((0, 0, 0.05))
    eb[name] = b
for c, p in parent.items(): eb[c].parent = eb[p]; eb[c].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ── 蒙皮：每个顶点整块跟一根骨头
for name in eb: mesh.vertex_groups.new(name=name)
groups = {}
for i, b in enumerate(vbone): groups.setdefault(b, []).append(i)
for b, ids in groups.items(): mesh.vertex_groups[b].add(ids, 1.0, 'REPLACE')
mesh.parent = arm
mod = mesh.modifiers.new('Armature', 'ARMATURE'); mod.object = arm

# ── 检查图：按骨骼上色（单独一份网格，不影响导出）
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 900; sc.render.resolution_y = 1000
sc.display.shading.light = 'STUDIO'; sc.display.shading.show_cavity = True
cam_d = bpy.data.cameras.new('c'); cam = bpy.data.objects.new('cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam
cam_d.type = 'ORTHO'; cam_d.ortho_scale = 1.15
def shoot(tag, views):
    for k, d in views.items():
        cam.location = Vector((0, 0, 0)) + d.normalized() * 3
        cam.rotation_euler = (-cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = f'{SHOT}_{tag}_{k}.png'; bpy.ops.render.render(write_still=True)
views = {'front': Vector((1, 0, 0.08)), 'right': Vector((0.15, -1, 0.08)), 'back': Vector((-1, 0, 0.08)), 'left': Vector((0.15, 1, 0.08))}
colors = {'Root': (1, 1, 1), 'Pelvis': (.9, .9, .2), 'Torso': (.6, .6, .65), 'ShoulderR': (.9, .3, .2), 'CannonBarrel': (1, .6, .1), 'ShoulderL': (.2, .5, .95),
          'SawBlade': (.1, .9, .9), 'ThighR': (.8, .2, .6), 'ShinR': (.5, .1, .4), 'FootR': (.3, .05, .25), 'ThighL': (.2, .8, .3), 'ShinL': (.1, .5, .2), 'FootL': (.05, .3, .1)}
col_attr = mesh.data.color_attributes.new('bonecol', 'BYTE_COLOR', 'POINT')
for i, b in enumerate(vbone): col_attr.data[i].color = (*colors[b], 1)
sc.display.shading.color_type = 'VERTEX'
shoot('bones', views)
# 试摆姿势：腰转 20°、两肩前后摆、膝盖弯 25°、炮管转 60° —— 看关节处有没有撕开 / 穿模
pb = arm.pose.bones
def rot(b, axis, deg):
    pb[b].rotation_mode = 'XYZ'
    pb[b].rotation_euler = Matrix.Rotation(math.radians(deg), 3, axis).to_euler()
rot('Torso', 'Y', 20); rot('ShoulderR', 'X', 25); rot('ShoulderL', 'X', -20); rot('ThighR', 'X', -20); rot('ShinR', 'X', 30); rot('ThighL', 'X', 15); rot('ShinL', 'X', 25); rot('CannonBarrel', 'Y', 60)
bpy.context.view_layer.update()
shoot('pose', {'front': Vector((1, -0.5, 0.15)), 'side': Vector((0.1, -1, 0.1))})
for b in pb: b.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
mesh.data.color_attributes.remove(mesh.data.color_attributes['bonecol'])

# ── 导出：机甲面朝 +X、Z 向上 → Unity 里面朝 +Z、Y 向上（FBX 轴转换），贴图打包
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                         bake_anim=False, use_armature_deform_only=True, mesh_smooth_type='FACE')
print('导出', OUT)
