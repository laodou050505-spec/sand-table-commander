"""标题机甲：待机动作（8 秒一圈，30 帧/秒，首尾相接）+ 逐帧穿模检查，然后连骨骼一起导出 FBX。
动作（都是小幅度、慢的，体现“巨大、沉重”）：
  背壳：绕腰部转盘慢慢左右环视 ±7°，同时呼吸般上下起伏 3 mm、俯仰 ±0.8°；
  右肩（转轮炮）：跟着转身慢半拍抬落 ±3°；炮管匀速自转两圈；
  左肩（链锯）：反相抬落 ±2.5°，锯身轻微抖动（发动机怠速）；
  盆骨：重心左右微移 ±0.5°（腿不动、脚不滑）。
穿模检查：每 4 帧把蒙皮后的网格按骨骼拆开，用 BVH 数“会动的零件”和“不动的腿 / 盆骨”之间相交的三角形对，
和静止姿势的数目比较——多出来的就是新穿模；超过阈值就报错（脚本以 1 退出），把幅度调小再跑。
用法：Blender -b --python Tools/titan_idle.py -- <titan_rigged.fbx> <输出.fbx> [幅度系数]"""
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]
K = float(argv[2]) if len(argv) > 2 else 1.0
FPS, SEC = 30, 8
N = FPS * SEC

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, automatic_bone_orientation=False)
arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
sc = bpy.context.scene; sc.render.fps = FPS; sc.frame_start = 0; sc.frame_end = N
# FBX 导回来后：Unity 约定 Y 向上、−Z 向前 → Blender 里又转回 Z 向上，机甲仍面朝 +X（导入时自动转轴）
up = Vector((0, 0, 1)); side = Vector((0, 1, 0))   # side：机甲的左右方向；绕它转 = 前后俯仰

def local_rot(pb, axis_arm, deg):
    """绕“骨架空间”的某根轴转 deg 度（以骨头根部为支点），换算成这根骨头自己的局部旋转。"""
    B = pb.bone.matrix_local.to_3x3()
    R = Matrix.Rotation(math.radians(deg), 3, axis_arm)
    return (B.inverted() @ R @ B).to_quaternion()
def local_off(pb, v_arm):
    return pb.bone.matrix_local.to_3x3().inverted() @ v_arm

def ease_wave(t, period, phase=0.0):
    """平滑的往返：sin 再过一次 smoothstep，两头停一停（看起来是在“环视”而不是钟摆）。"""
    s = math.sin(2 * math.pi * (t / period + phase))
    return math.copysign(abs(s) ** 0.75, s)

bpy.context.preferences.edit.keyframe_new_interpolation_type = 'LINEAR'
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
act = bpy.data.actions.new('Idle'); arm.animation_data_create(); arm.animation_data.action = act
for f in range(N + 1):
    t = f / FPS
    yaw = 7.0 * K * ease_wave(t, SEC)
    breath = math.sin(2 * math.pi * t / (SEC / 2))
    q = local_rot(pbs['Torso'], up, yaw) @ local_rot(pbs['Torso'], side, 0.6 * K * breath)
    pbs['Torso'].rotation_quaternion = q; pbs['Torso'].keyframe_insert('rotation_quaternion', frame=f)
    pbs['Torso'].location = local_off(pbs['Torso'], Vector((0, 0, 0.0018 + 0.0016 * K * (breath + 1))))   # 只往上起伏：转盘环底面贴着盆骨顶面，往下会压进去；平时抬 1.8 mm 留出转动间隙; pbs['Torso'].keyframe_insert('location', frame=f)
    pbs['ShoulderR'].rotation_quaternion = local_rot(pbs['ShoulderR'], side, 3.0 * K * ease_wave(t, SEC, -0.08))
    pbs['ShoulderR'].keyframe_insert('rotation_quaternion', frame=f)
    pbs['ShoulderL'].rotation_quaternion = local_rot(pbs['ShoulderL'], side, -2.5 * K * ease_wave(t, SEC, 0.15))
    pbs['ShoulderL'].keyframe_insert('rotation_quaternion', frame=f)
    bar_ax = (pbs['CannonBarrel'].bone.tail_local - pbs['CannonBarrel'].bone.head_local).normalized()
    pbs['CannonBarrel'].rotation_quaternion = local_rot(pbs['CannonBarrel'], bar_ax, 720.0 * t / SEC)
    pbs['CannonBarrel'].keyframe_insert('rotation_quaternion', frame=f)
    saw_ax = Vector((0, 0, 1))
    pbs['SawBlade'].rotation_quaternion = local_rot(pbs['SawBlade'], saw_ax, 0.25 * math.sin(2 * math.pi * t * 9))
    pbs['SawBlade'].keyframe_insert('rotation_quaternion', frame=f)
    pbs['Pelvis'].rotation_quaternion = local_rot(pbs['Pelvis'], Vector((1, 0, 0)), 0.5 * K * math.sin(2 * math.pi * t / SEC + 1.0))
    pbs['Pelvis'].keyframe_insert('rotation_quaternion', frame=f)
# 线性插值（逐帧都打了关键帧），炮管转两圈正好回到原位，首尾相接

# ── 穿模检查
weights = {}
for v in mesh.data.vertices:
    g = max(v.groups, key=lambda g: g.weight).group if v.groups else -1
    weights[v.index] = mesh.vertex_groups[g].name if g >= 0 else '?'
STATIC = {'Pelvis', 'ThighR', 'ShinR', 'FootR', 'ThighL', 'ShinL', 'FootL'}
MOVING = {'Torso', 'ShoulderR', 'ShoulderL', 'CannonBarrel', 'SawBlade'}
PAIRS = [('SawBlade', 'STATIC'), ('ShoulderL', 'STATIC'), ('ShoulderR', 'STATIC'), ('CannonBarrel', 'STATIC'), ('Torso', 'STATIC'),
         ('ShoulderR', 'Torso'), ('ShoulderL', 'Torso'), ('CannonBarrel', 'ShoulderR'), ('SawBlade', 'ShoulderL')]
def trees(frame):
    sc.frame_set(frame)
    dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    vs = [mesh.matrix_world @ v.co for v in me.vertices]
    polys = {}
    for p in me.polygons:
        b = weights[p.vertices[0]]
        key = 'STATIC' if b in STATIC else b
        polys.setdefault(key, []).append(list(p.vertices))
    out = {k: BVHTree.FromPolygons(vs, ps) for k, ps in polys.items()}
    ev.to_mesh_clear()
    return out
def count(tr):
    return {f'{a}-{b}': len(tr[a].overlap(tr[b])) for a, b in PAIRS if a in tr and b in tr}
base = count(trees(0))
print('静止时相交对（模型本来就互相插着的接口处）', base)
worst = {}
for f in range(0, N + 1, 4):
    c = count(trees(f))
    for k, v in c.items():
        extra = v - base[k]
        if extra > worst.get(k, (0, 0))[0]: worst[k] = (extra, f)
print('动起来以后新增的相交对（最多的那一帧）', worst)
def where(frame, a, b):
    tr = trees(frame); pr = tr[a].overlap(tr[b])
    sc.frame_set(frame); dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    return pr
for k, (n, f) in worst.items():
    if n <= 30: continue
    a_, b_ = k.split('-')
    sc.frame_set(f); dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    vs = [mesh.matrix_world @ v.co for v in me.vertices]
    pa, pbz = [], []
    for p in me.polygons:
        bn = weights[p.vertices[0]]; key = 'STATIC' if bn in STATIC else bn
        if key == a_: pa.append(p)
        elif key == b_: pbz.append(p)
    ta = BVHTree.FromPolygons(vs, [list(p.vertices) for p in pa]); tb = BVHTree.FromPolygons(vs, [list(p.vertices) for p in pbz])
    hits = ta.overlap(tb)
    from collections import Counter
    cb = Counter(weights[pbz[j].vertices[0]] for i, j in hits)
    pts = [sum((vs[x] for x in pa[i].vertices), Vector()) / len(pa[i].vertices) for i, j in hits]
    c = sum(pts, Vector()) / max(1, len(pts))
    print('  ', k, '第', f, '帧', len(hits), '对；碰到', dict(cb), '；位置中心', [round(x, 3) for x in c],
          'z 范围', round(min(p.z for p in pts), 3), round(max(p.z for p in pts), 3))
    ev.to_mesh_clear()
bad = {k: v for k, v in worst.items() if v[0] > 30}
sc.frame_set(0)

# ── 导出（动画烘进 FBX）
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                         bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.0, use_armature_deform_only=True, mesh_smooth_type='FACE')
print('导出', OUT)
if bad:
    print('【穿模】', bad); sys.exit(1)
print('穿模检查通过')
