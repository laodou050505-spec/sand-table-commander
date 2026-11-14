# 未来时代的程序建模布景（11-12 用户：“注意别把场景做得太空”——未来时代藏掉了树 / 灌木 / 篝火，要有东西顶上）：
#   F_Crystal_A / F_Crystal_B      月面水晶簇（高 / 矮），冰蓝——顶替树和灌木；
#   F_Crystal_A_V / F_Crystal_B_V  同形、紫色——虚空蜂群那半边场地用；
#   F_Beacon / F_Beacon_V          能量信标（光棱：白陶瓷座 + 天青光球；蜂群：黑曜座 + 紫光球）——顶替篝火。
# 水晶是干净的棱柱，程序建模比 AI 生成更利落（棱线笔直、没有破面）；贴图也是程序画的（沿晶体由深到亮的渐变 + 晶体内部的纹路）。
# 输出成和 P2 管线一样的目录结构，WTArt.ImportP2 直接接：ArtSource/ModelsP2/<名>/final/lod0..3.fbx + albedo.png + report.json；
# 自发光图直接写到 Assets/_WarTable/Art/Models/P2/<名>/emis.png（future_tex_fix.py 不处理这几件）。
# 用法：Blender -b -P Tools/make_future_crystals.py
import bpy, bmesh, math, random, json, pathlib
import numpy as np
from mathutils import Vector, Matrix

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
SRC = PROJ / 'ArtSource/ModelsP2'
DST = PROJ / 'Assets/_WarTable/Art/Models/P2'
N = 512
COLS = 8            # 贴图左边 8 条竖带：每个晶面随机挑一条（深浅略不同，晶面之间有区分）
CW = 0.09           # 每条宽（UV）
# 其它材质的竖带（u 起点, 宽）：v 方向都是“下 → 上”
ROCK = (0.74, 0.25)
TILE = {'shell': (0.0, 0.2), 'trim': (0.22, 0.12), 'glow': (0.36, 0.2), 'dark': (0.58, 0.14)}


def tnoise(n, scale, seed):
    r = np.random.default_rng(seed); fy = np.fft.fftfreq(n)[:, None] * n; fx = np.fft.fftfreq(n)[None, :] * n
    x = np.real(np.fft.ifft2((r.normal(size=(n, n)) + 1j * r.normal(size=(n, n))) * np.exp(-(fx ** 2 + fy ** 2) / scale ** 2)))
    return (x - x.mean()) / (x.std() + 1e-9)


def save_png(arr, path):
    """arr：H×W×3，0–1，第 0 行 = 贴图最上面一行。"""
    h, w, _ = arr.shape
    img = bpy.data.images.new('tmp', w, h, alpha=False)
    px = np.ones((h, w, 4), np.float32); px[..., :3] = np.clip(arr[::-1], 0, 1)
    img.pixels.foreach_set(px.ravel()); img.filepath_raw = str(path); img.file_format = 'PNG'; img.save(); bpy.data.images.remove(img)


def lerp3(a, b, c, t):
    a, b, c = np.array(a), np.array(b), np.array(c); t = t[..., None]
    return np.where(t < 0.5, a + (b - a) * (t * 2), b + (c - b) * (t * 2 - 1))


def crystal_tex(base, mid, tip, seed):
    v = np.linspace(1, 0, N)[:, None] * np.ones((1, N)); u = np.linspace(0, 1, N)[None, :] * np.ones((N, 1))
    col = np.zeros((N, N, 3)); emis = np.zeros((N, N, 3))
    cidx = np.minimum((u / CW).astype(int), COLS - 1)
    bright = np.array([0.82, 1.0, 0.9, 1.1, 0.86, 1.04, 0.94, 1.14])[cidx]
    # 晶体内部的纹路：斜向的亮带 + 细碎的云絮
    streak = 0.5 + 0.5 * np.sin((u * 60 + v * 9 + cidx * 1.7) * 2 * math.pi / 3.2)
    cloud = tnoise(N, 14, seed); fine = tnoise(N, 70, seed + 1)
    t = np.clip(v ** 1.15 + 0.07 * cloud, 0, 1)
    c = lerp3(base, mid, tip, t) * (bright * (0.9 + 0.12 * streak ** 3) + 0.035 * fine)[..., None]
    cm = u < COLS * CW + 0.005
    col[cm] = c[cm]
    e = np.array(mid)[None, None, :] * (0.1 + 0.42 * v ** 1.6 + 0.08 * streak ** 3 * v)[..., None]
    emis[cm] = e[cm]
    # 石座：月面玄武岩，深灰带颗粒
    rk = np.array([0.34, 0.35, 0.38])[None, None, :] * (1 + 0.16 * tnoise(N, 9, seed + 2) + 0.1 * tnoise(N, 60, seed + 3))[..., None]
    rm = u >= ROCK[0] - 0.005
    col[rm] = rk[rm]
    return col, emis


def beacon_tex(shell, trim, glow, dark, seed):
    v = np.linspace(1, 0, N)[:, None] * np.ones((1, N)); u = np.linspace(0, 1, N)[None, :] * np.ones((N, 1))
    grain = 1 + 0.05 * tnoise(N, 8, seed) + 0.03 * tnoise(N, 80, seed + 1)
    col = np.zeros((N, N, 3)); emis = np.zeros((N, N, 3))
    for key, c in (('shell', shell), ('trim', trim), ('dark', dark)):
        u0, w = TILE[key]; m = (u >= u0 - 0.005) & (u < u0 + w + 0.005)
        col[m] = (np.array(c)[None, None, :] * ((0.78 + 0.26 * v) * grain)[..., None])[m]     # 下暗上亮：假的环境光遮蔽
    u0, w = TILE['glow']; m = (u >= u0 - 0.005) & (u < u0 + w + 0.005)
    g = np.array(glow)[None, None, :] * (0.8 + 0.2 * np.sin(v * math.pi))[..., None] + 0.12 * (1 - np.abs(v - 0.5) * 2)[..., None]
    col[m] = g[m]; emis[m] = (np.array(glow)[None, None, :] * 0.62 * np.ones((N, N, 1)))[m]
    return col, emis


def new_obj(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob, bmesh.new()


def add_crystal(bm, uvl, M, r, L, tip, rng):
    a0 = rng.uniform(0, math.pi / 3)
    b = [bm.verts.new(M @ Vector((math.cos(a0 + k * math.pi / 3) * r, math.sin(a0 + k * math.pi / 3) * r, 0))) for k in range(6)]
    t = [bm.verts.new(M @ Vector((math.cos(a0 + k * math.pi / 3) * r * 0.9, math.sin(a0 + k * math.pi / 3) * r * 0.9, L))) for k in range(6)]
    off = Vector((rng.uniform(-0.25, 0.25) * r, rng.uniform(-0.25, 0.25) * r, L + tip))      # 尖顶稍微偏心，不那么规整
    ap = bm.verts.new(M @ off)
    vt = L / (L + tip) * 0.94 + 0.02
    for k in range(6):
        c = rng.randrange(COLS); u0 = c * CW + 0.012; u1 = u0 + CW - 0.024
        f = bm.faces.new((b[k], b[(k + 1) % 6], t[(k + 1) % 6], t[k])); f.smooth = False
        for lp, uv in zip(f.loops, ((u0, 0.02), (u1, 0.02), (u1, vt), (u0, vt))): lp[uvl].uv = uv
        f = bm.faces.new((t[k], t[(k + 1) % 6], ap)); f.smooth = False
        for lp, uv in zip(f.loops, ((u0, vt), (u1, vt), ((u0 + u1) / 2, 0.985))): lp[uvl].uv = uv


def add_rock(bm, uvl, R, h, rng):
    nv, nf = len(bm.verts), len(bm.faces)      # 新加的几何按“序号在原数量之后”认（拿 BMVert 当集合元素比对不可靠：会把旧的也当成新的）
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    vs = list(bm.verts)[nv:]
    for v in vs:
        k = rng.uniform(0.84, 1.14)
        v.co = Vector((v.co.x * R * k, v.co.y * R * k, max(0.0, v.co.z * h * rng.uniform(0.8, 1.25))))
    for f in list(bm.faces)[nf:]:
        if True:
            f.smooth = True
            for lp in f.loops:
                lp[uvl].uv = (ROCK[0] + 0.01 + (ROCK[1] - 0.02) * min(1, max(0, lp.vert.co.x / R * 0.42 + 0.5)), min(0.98, max(0.02, lp.vert.co.y / R * 0.42 + 0.5)))


def add_lathe(bm, uvl, prof, sides, tile, z0=0.0, smooth=True):
    """旋转体：prof = [(半径, 高度)…] 自下而上；贴到 tile 竖带上（v 按高度）。上下两端封口。"""
    u0, w = TILE[tile]; zs = [p[1] for p in prof]; lo, hi = min(zs), max(zs)
    rings = [[bm.verts.new(Vector((math.cos(k * 2 * math.pi / sides) * r, math.sin(k * 2 * math.pi / sides) * r, z0 + z))) for k in range(sides)] for r, z in prof]
    V = lambda z: 0.03 + 0.94 * ((z - lo) / max(hi - lo, 1e-6))
    for i in range(len(prof) - 1):
        for k in range(sides):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k])); f.smooth = smooth
            ua = u0 + 0.01 + (w - 0.02) * (k / sides); ub = u0 + 0.01 + (w - 0.02) * ((k + 1) / sides)
            for lp, uv in zip(f.loops, ((ua, V(prof[i][1])), (ub, V(prof[i][1])), (ub, V(prof[i + 1][1])), (ua, V(prof[i + 1][1])))): lp[uvl].uv = uv
    for ring, flip, z in ((rings[0], True, prof[0][1]), (rings[-1], False, prof[-1][1])):
        f = bm.faces.new(list(reversed(ring)) if flip else ring); f.smooth = False
        for lp in f.loops: lp[uvl].uv = (u0 + w / 2, V(z))


def add_orb(bm, uvl, c, r, tile):
    u0, w = TILE[tile]; nv, nf = len(bm.verts), len(bm.faces)
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r)
    for v in list(bm.verts)[nv:]: v.co += Vector(c)
    for f in list(bm.faces)[nf:]:
        if True:
            f.smooth = True
            for lp in f.loops:
                d = (lp.vert.co - Vector(c)) / r
                lp[uvl].uv = (u0 + 0.01 + (w - 0.02) * (math.atan2(d.y, d.x) / (2 * math.pi) + 0.5), 0.03 + 0.94 * (d.z * 0.5 + 0.5))


def finish(ob, bm, name, col, emis):
    bm.normal_update(); bm.to_mesh(ob.data); bm.free()
    faces = len(ob.data.polygons)
    out = SRC / name / 'final'; out.mkdir(parents=True, exist_ok=True); (DST / name).mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    for lod in range(4):
        bpy.ops.export_scene.fbx(filepath=str(out / f'lod{lod}.fbx'), use_selection=True, path_mode='STRIP', embed_textures=False,
                                 mesh_smooth_type='OFF', apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                                 bake_space_transform=True, use_mesh_modifiers=True, add_leaf_bones=False)
    save_png(col, out / 'albedo.png'); save_png(emis, DST / name / 'emis.png')
    (out / 'report.json').write_text(json.dumps({'pass': True, 'procedural': 'Tools/make_future_crystals.py', 'faces': faces}, indent=1))
    print('MADE', name, faces, 'faces', flush=True)


ICE = ((0.13, 0.36, 0.5), (0.42, 0.78, 0.92), (0.86, 0.97, 1.0))
VIO = ((0.2, 0.11, 0.4), (0.54, 0.36, 0.92), (0.9, 0.82, 1.0))


def crystals(name, tall, pal, seed):
    ob, bm = new_obj(name); uvl = bm.loops.layers.uv.new('UVMap'); rng = random.Random(seed)
    R = 0.4 if tall else 0.46
    add_rock(bm, uvl, R, 0.1 if tall else 0.085, rng)
    # 主晶 + 一圈向外倒的小晶
    if tall: add_crystal(bm, uvl, Matrix.Rotation(math.radians(4), 4, 'X') @ Matrix.Translation(Vector((0, 0, -0.03))), 0.115, 0.82, 0.24, rng)
    n = 7 if tall else 6
    for i in range(n):
        a = i * 2 * math.pi / n + rng.uniform(-0.3, 0.3)
        d = rng.uniform(0.12, 0.24) if tall else rng.uniform(0.05, 0.25)
        tilt = math.radians(rng.uniform(12, 34) if tall else rng.uniform(18, 48))
        L = rng.uniform(0.28, 0.6) if tall else rng.uniform(0.2, 0.42)
        r = rng.uniform(0.055, 0.085) if tall else rng.uniform(0.06, 0.095)
        M = Matrix.Translation(Vector((math.cos(a) * d, math.sin(a) * d, -0.03))) @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(tilt, 4, 'Y')
        add_crystal(bm, uvl, M, r, L, L * rng.uniform(0.3, 0.45), rng)
    col, emis = crystal_tex(*pal, seed)
    finish(ob, bm, name, col, emis)


def beacon(name, swarm, seed):
    ob, bm = new_obj(name); uvl = bm.loops.layers.uv.new('UVMap')
    body = 'dark' if swarm else 'shell'
    add_lathe(bm, uvl, [(0.34, 0.0), (0.34, 0.05), (0.3, 0.09)], 6, body, smooth=False)                       # 六角底板
    add_lathe(bm, uvl, [(0.17, 0.09), (0.12, 0.3), (0.1, 0.5)], 12, body)                                       # 立柱
    add_lathe(bm, uvl, [(0.15, 0.27), (0.16, 0.3), (0.15, 0.33)], 12, 'trim')                                   # 柱腰金环
    add_lathe(bm, uvl, [(0.1, 0.5), (0.2, 0.56), (0.24, 0.66), (0.21, 0.67)], 12, 'trim')                       # 托盘
    add_lathe(bm, uvl, [(0.135, 0.185), (0.14, 0.2), (0.135, 0.215)], 12, 'glow')                               # 柱身光环
    add_orb(bm, uvl, (0, 0, 0.84), 0.2, 'glow')                                                                 # 光球
    for k in range(3):                                                                                          # 三只护爪
        a = k * 2 * math.pi / 3 + 0.5
        nv = len(bm.verts)
        add_lathe(bm, uvl, [(0.035, 0.0), (0.03, 0.2), (0.012, 0.36)], 5, body)
        M = Matrix.Translation(Vector((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.62))) @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.radians(-16), 4, 'Y')
        for v in list(bm.verts)[nv:]: v.co = M @ v.co
    if swarm: col, emis = beacon_tex((0.2, 0.19, 0.27), (0.4, 0.3, 0.6), (0.72, 0.42, 1.0), (0.2, 0.19, 0.27), seed)
    else: col, emis = beacon_tex((0.88, 0.87, 0.84), (0.8, 0.64, 0.3), (0.3, 0.88, 1.0), (0.3, 0.32, 0.36), seed)
    finish(ob, bm, name, col, emis)


crystals('F_Crystal_A', True, ICE, 11); crystals('F_Crystal_B', False, ICE, 12)
crystals('F_Crystal_A_V', True, VIO, 11); crystals('F_Crystal_B_V', False, VIO, 12)
beacon('F_Beacon', False, 21); beacon('F_Beacon_V', True, 22)
print('done')
