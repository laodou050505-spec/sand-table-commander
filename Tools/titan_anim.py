"""标题机甲的两段动作 + 逐帧穿模检查，连骨骼一起导出 FBX（Unity 里是 Idle / Walk 两个片段）。
  Idle（8 秒）：背壳绕腰部转盘左右环视 ±7°、呼吸起伏（只往上，转盘环不压进盆骨）、两臂慢半拍抬落、炮管匀速自转、链锯怠速抖动、重心微移。
  Walk（3.2 秒一个完整步态，左右各一步）：巡逻步伐，沉重——
    脚：支撑期（60%）脚踩在地上不动、相对胯部匀速后移（Unity 里根节点按同样的速度前进 → 脚不打滑），
        摆动期（40%）抬起 3.5% 身高、往前迈；落地前脚尖微翘，落地后放平；
    腿：两段骨（大腿、小腿）解析 IK，膝盖朝前弯；
    盆骨：整体压低 1.6%（腿微屈显得沉），落脚那一下再往下顿 0.6%，左右轻晃；
    上身：背壳反向扭 ±3°，两臂反向小摆；链锯那条胳膊整体往外撇 9°，让锯身让开左腿（逐帧检查过）。
穿模检查：每 3 帧按骨骼拆开蒙皮网格，数关心的零件对之间相交的三角形对，和静止姿势比；新增超过阈值就报错退出。
用法：Blender -b --python Tools/titan_anim.py -- <titan_rigged.fbx> <输出.fbx>
输出里同时打印 Walk 的前进速度（模型高 = 1 的单位 / 秒），Unity 的 TitanBackdrop 按它 × 高度 推进根节点。"""
import bpy, sys, math, json
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from collections import Counter

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]
FPS = 30

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, automatic_bone_orientation=False)
arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
# FBX 导回来时，头尾相接的骨头会被自动“连接”（Root→Pelvis→Torso），连接的骨头位移不生效 → 盆骨压低、背壳抬起全被吞掉。全部断开
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for eb in arm.data.edit_bones: eb.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
sc = bpy.context.scene; sc.render.fps = FPS
bpy.context.preferences.edit.keyframe_new_interpolation_type = 'LINEAR'
UP = Vector((0, 0, 1)); SIDE = Vector((0, 1, 0)); FWD = Vector((1, 0, 0))   # 机甲面朝 +X，左手边 +Y
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'

def lrot(pb, axis, deg):
    B = pb.bone.matrix_local.to_3x3()
    return (B.inverted() @ Matrix.Rotation(math.radians(deg), 3, axis) @ B).to_quaternion()
def loff(pb, v):
    return pb.bone.matrix_local.to_3x3().inverted() @ v
def key(pb, q=None, loc=None, f=0):
    if q is not None: pb.rotation_quaternion = q; pb.keyframe_insert('rotation_quaternion', frame=f)
    if loc is not None: pb.location = loc; pb.keyframe_insert('location', frame=f)
def reset():
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
def wave(t, period, phase=0.0):
    s = math.sin(2 * math.pi * (t / period + phase)); return math.copysign(abs(s) ** 0.75, s)
bar_ax = (pbs['CannonBarrel'].bone.tail_local - pbs['CannonBarrel'].bone.head_local).normalized()

# ════════════ Idle
IDLE = 8.0
idle = bpy.data.actions.new('Idle'); arm.animation_data_create(); arm.animation_data.action = idle
reset()
for f in range(int(IDLE * FPS) + 1):
    t = f / FPS
    breath = math.sin(2 * math.pi * t / (IDLE / 2))
    key(pbs['Torso'], lrot(pbs['Torso'], UP, 7.0 * wave(t, IDLE)) @ lrot(pbs['Torso'], SIDE, 0.6 * breath),
        loff(pbs['Torso'], Vector((0, 0, 0.0018 + 0.0016 * (breath + 1)))), f)   # 只往上起伏：平时抬 1.8 mm 给转盘留间隙
    key(pbs['ShoulderR'], lrot(pbs['ShoulderR'], SIDE, 3.0 * wave(t, IDLE, -0.08)), f=f)
    key(pbs['ShoulderL'], lrot(pbs['ShoulderL'], SIDE, -2.5 * wave(t, IDLE, 0.15)), f=f)
    key(pbs['CannonBarrel'], lrot(pbs['CannonBarrel'], bar_ax, 720.0 * t / IDLE), f=f)
    key(pbs['SawBlade'], lrot(pbs['SawBlade'], UP, 0.25 * math.sin(2 * math.pi * t * 9)), f=f)
    key(pbs['Pelvis'], lrot(pbs['Pelvis'], FWD, 0.5 * math.sin(2 * math.pi * t / IDLE + 1.0)), f=f)
    for b in ('ThighR', 'ShinR', 'FootR', 'ThighL', 'ShinL', 'FootL', 'Root'): key(pbs[b], (1, 0, 0, 0), f=f)

# ════════════ Power（7 秒，11-20 用户：不走路，固定一处；平时待机，每隔十几秒来一次大动作，要沉、慢、有力量）
#   0–1.4 s  蓄力：盆骨往下沉（两腿弯，脚不动——同一套两段 IK），背壳后仰、往一侧拧；
#   1.4–3.0  发力：背壳慢慢扭回并前压，右臂转轮炮抬起平举对准前方、炮管加速狂转；左臂链锯抬起、锯身剧烈抖动；
#   3.0–4.4  定住：炮口连续后坐的小震动，整个上身跟着微颤；
#   4.4–7.0  收：缓缓回到待机姿势（带一点过冲再稳住），盆骨升回原位。
P = 7.0; NP = int(P * FPS)
legs = {}
def ang(v): return math.atan2(v.x, -v.z)
for s_ in 'RL':
    H = pbs['Thigh' + s_].bone.head_local.copy(); Kn = pbs['Shin' + s_].bone.head_local.copy(); A = pbs['Foot' + s_].bone.head_local.copy()
    legs[s_] = dict(H=H, A=A, l1=math.hypot((Kn - H).x, (Kn - H).z), l2=math.hypot((A - Kn).x, (A - Kn).z), a1=ang(Kn - H), a2=ang(A - Kn))
def ss(a, b, x):
    k = max(0.0, min(1.0, (x - a) / (b - a))); return k * k * (3 - 2 * k)
def settle(x):
    """收回：带一点过冲的缓动（0→1）。"""
    k = max(0.0, min(1.0, x)); return 1 - (1 - k) ** 3 * (1 + 2.2 * k) if k < 1 else 1.0
power = bpy.data.actions.new('Power'); arm.animation_data.action = power
reset()
spin = 0.0
for f in range(NP + 1):
    t = f / FPS
    up_ = ss(0.0, 1.4, t)                         # 蓄力进度
    go = ss(1.2, 3.0, t)                          # 发力进度
    back = settle((t - 4.4) / 2.6) if t > 4.4 else 0.0   # 收回进度
    hold = 1.0 if 3.0 <= t <= 4.4 else 0.0
    w_charge = up_ * (1 - go)                     # 蓄力姿势的权重
    w_fire = go * (1 - back)                      # 发力姿势的权重
    shake = hold * math.sin(2 * math.pi * t * 11) * math.exp(-((t - 3.0) % 0.35) * 6)   # 连射后坐：每 0.35 s 一下
    dz = -(0.014 * w_charge + 0.010 * w_fire) - 0.002 * abs(shake)
    key(pbs['Pelvis'], lrot(pbs['Pelvis'], FWD, 0.0), loff(pbs['Pelvis'], Vector((0, 0, dz))), f)
    for s_ in 'RL':
        d = legs[s_]
        hip = d['H'] + Vector((0, 0, dz))
        v = d['A'] - hip; dx, dzv = v.x, v.z
        D = min(math.hypot(dx, dzv), d['l1'] + d['l2'] - 1e-4); base_ = math.atan2(dx, -dzv)
        al = math.acos(max(-1, min(1, (d['l1'] ** 2 + D ** 2 - d['l2'] ** 2) / (2 * d['l1'] * D))))
        a1 = base_ + al; kx = math.sin(a1) * d['l1']; kz = -math.cos(a1) * d['l1']; a2 = math.atan2(dx - kx, -(dzv - kz))
        th = -math.degrees(a1 - d['a1']); sh = -math.degrees((a2 - d['a2']) - (a1 - d['a1']))
        # 静止时腿本来就是这个解：减掉 dz = 0 时的解，只留弯曲的增量（不然会把原本的站姿“拉直”）
        key(pbs['Thigh' + s_], lrot(pbs['Thigh' + s_], SIDE, th - legs[s_].setdefault('th0', th if f == 0 else legs[s_].get('th0', th))), f=f)
        key(pbs['Shin' + s_], lrot(pbs['Shin' + s_], SIDE, sh - legs[s_].setdefault('sh0', sh if f == 0 else legs[s_].get('sh0', sh))), f=f)
        key(pbs['Foot' + s_], lrot(pbs['Foot' + s_], SIDE, -((th - legs[s_]['th0']) + (sh - legs[s_]['sh0']))), f=f)
    yaw = 10.0 * w_charge - 4.0 * w_fire
    pitch = -2.5 * w_charge + 3.0 * w_fire + 0.4 * shake
    key(pbs['Torso'], lrot(pbs['Torso'], UP, yaw) @ lrot(pbs['Torso'], SIDE, pitch), loff(pbs['Torso'], Vector((0, 0, 0.0018 + 0.002 * w_charge))), f)
    key(pbs['ShoulderR'], lrot(pbs['ShoulderR'], FWD, -7.0 * w_fire) @ lrot(pbs['ShoulderR'], SIDE, -15.0 * w_fire + 1.0 * shake), f=f)   # 炮臂抬起、略往外撇（−Y），不蹭背壳
    key(pbs['ShoulderL'], lrot(pbs['ShoulderL'], FWD, 6.0 * w_fire) @ lrot(pbs['ShoulderL'], SIDE, -16.0 * w_fire), f=f)   # 链锯抬起、往外撇
    rate = 90.0 + 1350.0 * w_fire                 # 炮管转速：待机慢转 → 狂转
    spin += rate / FPS
    key(pbs['CannonBarrel'], lrot(pbs['CannonBarrel'], bar_ax, spin), f=f)
    key(pbs['SawBlade'], lrot(pbs['SawBlade'], UP, (0.25 + 1.6 * w_fire) * math.sin(2 * math.pi * t * 13)), f=f)
    key(pbs['Root'], (1, 0, 0, 0), f=f)
walk = power   # 下面的检查沿用这个名字
NW = NP
# ════════════ 穿模检查
weights = {}
for v in mesh.data.vertices:
    g = max(v.groups, key=lambda g: g.weight).group if v.groups else -1
    weights[v.index] = mesh.vertex_groups[g].name if g >= 0 else '?'
LEGS_R = {'ThighR', 'ShinR', 'FootR'}; LEGS_L = {'ThighL', 'ShinL', 'FootL'}
def grp(b):
    return 'LegR' if b in LEGS_R else 'LegL' if b in LEGS_L else b
PAIRS = [('SawBlade', 'LegL'), ('SawBlade', 'LegR'), ('SawBlade', 'Pelvis'), ('ShoulderL', 'LegL'), ('ShoulderR', 'LegR'), ('CannonBarrel', 'LegR'),
         ('LegR', 'LegL'), ('LegR', 'Pelvis'), ('LegL', 'Pelvis'), ('Torso', 'Pelvis'), ('Torso', 'LegL'), ('Torso', 'LegR'),
         ('ShoulderR', 'Torso'), ('ShoulderL', 'Torso'), ('CannonBarrel', 'ShoulderR'), ('SawBlade', 'ShoulderL')]
def trees(action, frame):
    arm.animation_data.action = action; sc.frame_set(frame)
    dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    vs = [mesh.matrix_world @ v.co for v in me.vertices]
    polys = {}
    for p in me.polygons: polys.setdefault(grp(weights[p.vertices[0]]), []).append(list(p.vertices))
    out = {k: BVHTree.FromPolygons(vs, ps) for k, ps in polys.items()}
    ev.to_mesh_clear(); return out
def count(tr): return {f'{a}-{b}': len(tr[a].overlap(tr[b])) for a, b in PAIRS if a in tr and b in tr}
arm.animation_data.action = None; reset(); bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
vs = [mesh.matrix_world @ v.co for v in me.vertices]; polys = {}
for p in me.polygons: polys.setdefault(grp(weights[p.vertices[0]]), []).append(list(p.vertices))
base = count({k: BVHTree.FromPolygons(vs, ps) for k, ps in polys.items()}); ev.to_mesh_clear()
print('静止时相交对', base)
bad = {}
for act, n in ((idle, int(IDLE * FPS)), (power, NP)):
    worst = {}
    for f in range(0, n + 1, 3):
        for k, v in count(trees(act, f)).items():
            e = v - base[k]
            if e > worst.get(k, (0, 0))[0]: worst[k] = (e, f)
    print(act.name, '新增相交（最多的那一帧）', worst)
    for k, v in worst.items():
        if v[0] > 30: bad[act.name + ':' + k] = v
json.dump({'power_period': P, 'idle_period': IDLE}, open(OUT + '.json', 'w'))

# ════════════ 导出
for a in (idle, power): a.use_fake_user = True
arm.animation_data.action = idle; sc.frame_set(0)
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.0, use_armature_deform_only=True, mesh_smooth_type='FACE')
print('导出', OUT)
if bad: print('【穿模】', bad); sys.exit(1)
print('穿模检查通过')
