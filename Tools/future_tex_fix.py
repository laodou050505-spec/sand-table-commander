"""未来时代贴图修正（11-12 用户：“注意别做成一片漆黑什么都看不见”）。convert_demo_assets.py 重跑会覆盖贴图，跑完它之后再跑一次本脚本。
对象：Assets/_WarTable/Art/Models/Demo/<troops|mounts|props>/f_*_tex/Color_*.png
  虚空蜂群（f_cl_*、f_wolf、氏族一方的武器 / 盾）：
    1. 甲片从近乎纯黑（亮度中位 0.17–0.24）提到深靛紫（约 0.37），保留明暗层次：v' = 0.14 + 0.95 · v^0.8，色相 255°、饱和度 0.32；
    2. 紫色缝光 / 眼睛加亮加饱和；
    3. 另出一张自发光遮罩 Emis_<同名>.png（只有紫光处有颜色，轻微模糊），DemoFigures.Mat 挂到 _EmissionMap——不受光照影响，暗处也亮。
  光棱军团（f_kd_*、f_horse、王国一方的武器 / 盾）：颜色不动，只出青色自发光遮罩（面罩光缝、关节灯、硬光刃 / 光盾）。
幂等：原图备份在 ArtSource/Future/texfix_src/<名>.png，记下输出的 md5；再跑时若贴图仍是上次的输出就从备份重算，被转换脚本换掉了就重新备份。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/future_tex_fix.py [名字…]（不带参数 = 全部 f_*）"""
import glob, hashlib, json, pathlib, sys
import numpy as np
from PIL import Image, ImageFilter

PROJ = pathlib.Path(__file__).resolve().parent.parent
DEMO = PROJ / 'Assets/_WarTable/Art/Models/Demo'
SRC = PROJ / 'ArtSource/Future/texfix_src'
CLAN_PROPS = {'f_shield_round_clan', 'f_hatchet', 'f_clan_axe', 'f_javelin', 'f_glaive', 'f_war_axe2', 'f_maul', 'f_war_axe', 'f_bow', 'f_long_bow_clan', 'f_shield_clan'}


def rgb2hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1); d = mx - mn
    h = np.zeros_like(mx); m = d > 1e-6
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6; h[gm] = (b - r)[gm] / d[gm] + 2; h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60, np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0), mx


def hsv2rgb(h, s, v):
    h = (h % 360) / 60; c = v * s; x = c * (1 - np.abs(h % 2 - 1)); z = np.zeros_like(h); i = np.floor(h).astype(int) % 6
    out = np.zeros(h.shape + (3,))
    for k, (r, g, b) in enumerate([(c, x, z), (x, c, z), (z, c, x), (z, x, c), (x, z, c), (c, z, x)]):
        m = i == k; out[m] = np.stack([r[m], g[m], b[m]], -1)
    return out + (v - c)[..., None]


def noise(n, scale, seed):
    """可平铺的噪声（频域里只留低频），均值 0、标准差 1。"""
    r = np.random.default_rng(seed); fy = np.fft.fftfreq(n)[:, None] * n; fx = np.fft.fftfreq(n)[None, :] * n
    x = np.real(np.fft.ifft2((r.normal(size=(n, n)) + 1j * r.normal(size=(n, n))) * np.exp(-(fx ** 2 + fy ** 2) / scale ** 2)))
    return (x - x.mean()) / (x.std() + 1e-9)


def baked(name, size):
    """Tools/future_bake.py 烘的环境光遮蔽和曲率（没有就返回 None）。ao：1 = 敞亮，0 = 缝里；edge / crev：凸棱 / 凹缝的强度 0–1。"""
    d = PROJ / 'ArtSource/Future/bake' / name
    if not (d / 'AO.png').exists(): return None
    ao = np.asarray(Image.open(d / 'AO.png').convert('L').resize((size, size), Image.BILINEAR)).astype(np.float64) / 255
    cv = np.asarray(Image.open(d / 'Curv.png').convert('L').resize((size, size), Image.BILINEAR).filter(ImageFilter.GaussianBlur(0.6))).astype(np.float64) / 255
    return ao, np.clip((cv - 0.62) / 0.38, 0, 1), np.clip((0.38 - cv) / 0.38, 0, 1)


def paint(col, name, wash, dry, wash_k, dry_k):
    """模型涂装的两步（11-12 用户：“角色不能完全没有质感”）：渍洗（凹处 / 缝隙压上深色）+ 干扫（凸棱提亮），再加一点颗粒。"""
    n = col.shape[0]; bk = baked(name, n)
    g = 1 + 0.045 * noise(n, 6, 11) + 0.03 * noise(n, 90, 12)          # 大块深浅 + 细颗粒
    col = col * g[..., None]
    if bk is None: return np.clip(col, 0, 1)
    ao, edge, crev = bk
    w = np.clip((1 - ao) * 1.5 + crev * 0.6, 0, 1) * wash_k
    col = col * (1 - w[..., None]) + np.array(wash) * col * w[..., None] * 1.0 + np.array(wash) * w[..., None] * 0.12
    d = edge * dry_k
    col = col * (1 - d[..., None]) + np.array(dry) * d[..., None]
    return np.clip(col, 0, 1)


def swarm(a, name):
    h, s, v = rgb2hsv(a)
    glow = (h > 240) & (h < 325) & (s > 0.42) & (v > 0.3)          # 只留真正的缝光（第一版阈值太松，紫光糊成一块块）
    # 甲片：深靛蓝紫，色相留一点原图的变化（不压成单一色），亮度中位约 0.3——靠渍洗 / 干扫 / 高光出层次，而不是整片提亮
    hh = np.where((h > 200) & (h < 330), 255 + (h - 265) * 0.35, 252.0)
    v2 = 0.09 + 0.8 * np.power(v, 0.75)
    col = hsv2rgb(np.where(glow, h, hh), np.where(glow, np.minimum(1, s * 1.1), 0.26), np.clip(np.where(glow, np.minimum(1, 0.5 + v * 1.2), v2), 0, 1))
    body = paint(col, name, wash=(0.05, 0.05, 0.12), dry=(0.56, 0.55, 0.78), wash_k=0.85, dry_k=0.42)
    col = np.where(glow[..., None], col, body)
    emis = hsv2rgb(np.where(glow, h, 0.0), np.where(glow, np.minimum(1, s * 1.1), 0.0), np.where(glow, 1.0, 0.0))
    return col, emis


def prism(a, name, troop):
    h, s, v = rgb2hsv(a)
    glow = (h > 150) & (h < 215) & (s > (0.35 if troop else 0.25)) & (v > 0.45)
    gold = (h > 25) & (h < 60) & (s > 0.35)
    body = paint(a, name, wash=(0.5, 0.56, 0.68), dry=(1.0, 1.0, 1.0), wash_k=0.9, dry_k=0.3) if troop else a
    col = np.where((glow | gold)[..., None], a, body)
    emis = hsv2rgb(np.where(glow, h, 0.0), np.where(glow, np.minimum(1, s * 1.2), 0.0), np.where(glow, v, 0.0))
    return col, emis


def md5(p): return hashlib.md5(p.read_bytes()).hexdigest()


def run(kind, name):
    files = glob.glob(str(DEMO / kind / f'{name}_tex' / 'Color_*.png'))
    if not files: print(name, '找不到贴图'); return
    p = max((pathlib.Path(f) for f in files), key=lambda f: f.stat().st_mtime)
    SRC.mkdir(parents=True, exist_ok=True)
    bak, rec = SRC / f'{name}.png', SRC / f'{name}.json'
    last = json.loads(rec.read_text()).get('out') if rec.exists() else None
    if not bak.exists() or md5(p) != last: bak.write_bytes(p.read_bytes())
    im = Image.open(bak); a = np.asarray(im.convert('RGB')).astype(np.float64) / 255
    clan = name.startswith('f_cl_') or name in ('f_wolf', 'f_skimmer') or name in CLAN_PROPS
    col, emis = swarm(a, name) if clan else prism(a, name, kind != 'props')
    res = Image.fromarray(np.clip(col * 255 + 0.5, 0, 255).astype(np.uint8))
    if im.mode == 'RGBA': res.putalpha(im.getchannel('A'))
    res.save(p); rec.write_text(json.dumps({'out': md5(p)}))
    e = Image.fromarray(np.clip(emis * 255 + 0.5, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.5))
    e.save(p.with_name(p.name.replace('Color_', 'Emis_')))
    v = rgb2hsv(col)[2]
    print(f'{name:22s} {"蜂群" if clan else "光棱"} 亮度中位 {np.median(v):.2f} 发光像素 {100 * (emis.max(-1) > 0.05).mean():.1f}%')


def building(name):
    """未来时代的 P2 建筑（Art/Models/P2/F_*/albedo.png）：出自发光遮罩 emis.png（WTArt.ImportP2 会挂上）；蜂巢塔另外按蜂群的办法提亮。"""
    p = PROJ / 'Assets/_WarTable/Art/Models/P2' / name / 'albedo.png'
    if not p.exists(): return
    if name.startswith(('F_Crystal', 'F_Beacon')): return   # 程序建模的（Tools/make_future_crystals.py）自带贴图和自发光图，不处理
    if name.startswith(('F_Rock', 'F_Standing')): return    # 去苔藓的岩石（Tools/make_future_rocks.py）：不发光，不处理
    SRC.mkdir(parents=True, exist_ok=True)
    bak, rec = SRC / f'{name}.png', SRC / f'{name}.json'
    last = json.loads(rec.read_text()).get('out') if rec.exists() else None
    if not bak.exists() or md5(p) != last: bak.write_bytes(p.read_bytes())
    a = np.asarray(Image.open(bak).convert('RGB')).astype(np.float64) / 255   # 颜色图保持原分辨率（2K），遮罩缩到 1K 就够
    if name == 'F_Barrier':
        # 能量围栏：修拓扑后重新烘焙时光板上有个三角面没烘到，留了黑洞 → 黑像素一律用最近的有效颜色填（没用到的贴图空白处填了也无妨）；
        # 光板本来偏白（淡青），加饱和度，让它读得出是“硬光”
        from scipy import ndimage as ndi
        # 洞的边缘有一圈抗锯齿出来的深灰（亮度 0.06–0.3）：只把纯黑算洞的话，“最近的有效颜色”取到的正是这圈深灰，等于拿深灰把洞填了
        # → 深灰也算洞，再往外扩 3 个像素，取到的才是干净的本色（这件东西本身没有深色的部位）
        hole = ndi.binary_dilation(a.max(-1) < 0.3, iterations=3)
        iy, ix = ndi.distance_transform_edt(hole, return_distances=False, return_indices=True)
        a = a[iy, ix]
    h, s, v = rgb2hsv(a)
    if name == 'F_Barrier':
        slab = (h > 160) & (h < 215) & (s > 0.07) & (v > 0.6)
        glow = slab
        col = hsv2rgb(np.where(slab, 186.0, h), np.where(slab, np.clip(0.38 + s * 1.2, 0, 0.7), s), np.where(slab, np.minimum(1, v * 1.02), v))
    elif name in ('F_Hive', 'F_Spikes'):
        glow = (h > 240) & (h < 325) & (s > 0.4) & (v > 0.28)
        col = hsv2rgb(np.where(glow, h, 252.0), np.where(glow, np.minimum(1, s * 1.1), 0.24), np.clip(np.where(glow, np.minimum(1, 0.5 + v * 1.2), 0.1 + 0.8 * np.power(v, 0.75)), 0, 1))
    else:
        glow = (h > 150) & (h < 215) & (s > 0.3) & (v > 0.5); col = a
    emis = hsv2rgb(np.where(glow, h, 0.0), np.where(glow, np.minimum(1, s * 1.15), 0.0), np.where(glow, 1.0, 0.0))
    if name == 'F_Barrier': emis = hsv2rgb(np.where(glow, 186.0, 0.0), np.where(glow, 0.75, 0.0), np.where(glow, 0.55, 0.0))   # 整块光板都发光，压一点别过曝
    Image.fromarray(np.clip(col * 255 + 0.5, 0, 255).astype(np.uint8)).save(p); rec.write_text(json.dumps({'out': md5(p)}))
    Image.fromarray(np.clip(emis * 255 + 0.5, 0, 255).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6)).save(p.with_name('emis.png'))
    print(f'{name:22s} 建筑 亮度中位 {np.median(rgb2hsv(col)[2]):.2f} 发光像素 {100 * glow.mean():.1f}%')


if __name__ == '__main__':
    want = sys.argv[1:]
    for kind in ('troops', 'mounts', 'props'):
        for d in sorted((DEMO / kind).glob('f_*_tex')):
            n = d.name[:-4]
            if not want or n in want: run(kind, n)
    for d in sorted((PROJ / 'Assets/_WarTable/Art/Models/P2').glob('F_*')):
        if not want or d.name in want: building(d.name)
