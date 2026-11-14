"""哥特兵种贴图的后期修正（convert_demo_assets.py 重跑会覆盖贴图，跑完它之后再跑一次本脚本）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/gothic_tex_fix.py [兵种...]
幂等：第一次把原图备份到 ArtSource/Gothic/texfix_src/，记下输出的 md5；
再跑时若贴图仍是上次的输出，就从备份重算，不会叠加；若贴图被转换脚本换掉了，就把新图当原图重新备份。"""
import glob, hashlib, json, pathlib, sys
import numpy as np
from PIL import Image

PROJ = pathlib.Path(__file__).resolve().parent.parent
TROOPS = PROJ / 'Assets/_WarTable/Art/Models/Demo/troops'
SRC = PROJ / 'ArtSource/Gothic/texfix_src'


def rgb2hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rm = m & (mx == r)
    gm = m & (mx == g) & ~rm
    bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h * 60, s, mx


def hsv2rgb(h, s, v):
    h = (h % 360) / 60
    c = v * s
    x = c * (1 - np.abs(h % 2 - 1))
    z = np.zeros_like(h)
    i = np.floor(h).astype(int) % 6
    lut = [(c, x, z), (x, c, z), (z, c, x), (z, x, c), (x, z, c), (c, z, x)]
    out = np.zeros(h.shape + (3,))
    for k, (r, g, b) in enumerate(lut):
        m = i == k
        out[m] = np.stack([r[m], g[m], b[m]], -1)
    return out + (v - c)[..., None]


def guardian(a):
    """大红 → 锈铁：红橙色区压饱和度、色相往棕橙挪、略压暗；炉门的高亮橙光保留。"""
    h, s, v = rgb2hsv(a)
    red = ((h < 30) | (h > 340)) & (s > 0.25)
    glow = (v > 0.8) & (h > 18) & (h < 45) & (s > 0.6)   # 炉门 / 镜片发光
    k = red & ~glow
    h2 = np.where(k, np.where(h > 340, h - 360, h) * 0.5 + 12, h)   # 0..30° → 12..27°
    s2 = np.where(k, s * 0.55, s)
    v2 = np.where(k, v * 0.88, v)
    return hsv2rgb(h2, s2, v2)


def obsidian(a):
    """未来时代虚空蜂群（11-12）：P2 贴图把黑甲贴成偏棕 → 深色、不是紫光的部分统一成略带蓝紫的黑曜石色；紫色缝光 / 眼睛不动。"""
    h, s, v = rgb2hsv(a)
    glow = (h > 235) & (h < 325) & (s > 0.3) & (v > 0.22)
    k = ~glow & (s > 0.02)
    return hsv2rgb(np.where(k, 255.0, h), np.where(k, np.minimum(s, 0.2), s), v)


FIXES = {'g_cl_guardian': guardian}   # 未来时代（f_*）统一由 Tools/future_tex_fix.py 处理
KIND = {}   # 默认在 troops 目录


def md5(p):
    return hashlib.md5(p.read_bytes()).hexdigest()


def run(name):
    files = glob.glob(str(TROOPS.parent / KIND.get(name, 'troops') / f'{name}_tex' / 'Color_*.png'))
    if not files:
        print(name, '找不到贴图')
        return
    p = pathlib.Path(files[0])
    SRC.mkdir(parents=True, exist_ok=True)
    bak, rec = SRC / f'{name}.png', SRC / f'{name}.json'
    last = json.loads(rec.read_text()).get('out') if rec.exists() else None
    if not bak.exists() or md5(p) != last:   # 新原图（第一次，或转换脚本覆盖过）
        bak.write_bytes(p.read_bytes())
    im = Image.open(bak)
    a = np.asarray(im.convert('RGB')).astype(np.float64) / 255
    out = np.clip(FIXES[name](a) * 255 + 0.5, 0, 255).astype(np.uint8)
    res = Image.fromarray(out)
    if im.mode == 'RGBA':
        res.putalpha(im.getchannel('A'))
    res.save(p)
    rec.write_text(json.dumps({'out': md5(p)}))
    s = rgb2hsv(out.astype(float) / 255)[1].mean()
    print(name, '→', p.name, '平均饱和度', round(float(s), 2))


if __name__ == '__main__':
    for n in (sys.argv[1:] or list(FIXES)):
        run(n if n.startswith(('g_', 'f_')) else 'g_' + n)
