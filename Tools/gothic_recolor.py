"""哥特时代的布景换色（11-11 用户选“骨白铜绿 对 煤黑熔橙”）：从中古贴图算出哥特版，写到 Assets/_WarTable/Art/Textures/Gothic/<名>_g.png。
王国帐篷：蓝 → 深铜绿，白 → 骨白；氏族帐篷：红 → 熔橙，其余（皮革、木头）压暗去饱和成煤黑。原图不动，可重复跑。
同时把哥特帅旗 ArtSource/Banners/standard_bell|forge.png 拷到 Art/Textures（BoardBuilder.Tex 从那里读）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/gothic_recolor.py"""
import os, shutil
import numpy as np
from PIL import Image

R = '/Users/ld/Documents/ChatGPT/WarTable/'
P2 = R + 'Assets/_WarTable/Art/Models/P2/'
OUT = R + 'Assets/_WarTable/Art/Textures/Gothic/'
os.makedirs(OUT, exist_ok=True)


def hsv(a):
    mx, mn = a.max(-1), a.min(-1); d = mx - mn
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.zeros_like(mx); m = d > 1e-6
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6; h[gm] = (b - r)[gm] / d[gm] + 2; h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60, np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0), mx


def rgb(h, s, v):
    h = (h % 360) / 60; c = v * s; x = c * (1 - np.abs(h % 2 - 1)); z = np.zeros_like(h); i = np.floor(h).astype(int) % 6
    out = np.zeros(h.shape + (3,))
    for k, (a, b, cc) in enumerate([(c, x, z), (x, c, z), (z, c, x), (z, x, c), (x, z, c), (c, z, x)]):
        m = i == k; out[m] = np.stack([a[m], b[m], cc[m]], -1)
    return out + (v - c)[..., None]


def kingdom(a):
    h, s, v = hsv(a)
    blue = (h > 185) & (h < 265) & (s > 0.2)
    white = (s < 0.18) & (v > 0.6)
    h2 = np.where(blue, 172.0, h); s2 = np.where(blue, s * 0.75, s); v2 = np.where(blue, v * 0.8, v)
    out = rgb(h2, s2, v2)
    out[white] = out[white] * np.array([0.95, 0.91, 0.82])   # 白 → 骨白
    return out


def clan(a):
    h, s, v = hsv(a)
    red = ((h < 22) | (h > 330)) & (s > 0.35)
    hh = np.where(h > 330, h - 360, h)
    h2 = np.where(red, 22 + hh * 0.3, h)
    s2 = np.where(red, np.minimum(1, s * 1.05), s * 0.45)
    v2 = np.where(red, np.minimum(1, v * 1.15), v * 0.5)
    return rgb(h2, s2, v2)


for name, fn in [('Tent_Kingdom_B', kingdom), ('Tent_Clan_A', clan), ('Tent_Clan_B', clan)]:
    src = P2 + name + '/albedo.png'
    if not os.path.exists(src): print('缺', src); continue
    im = Image.open(src); a = np.asarray(im.convert('RGB')).astype(np.float64) / 255
    o = Image.fromarray(np.clip(fn(a) * 255 + 0.5, 0, 255).astype(np.uint8))
    if im.mode == 'RGBA': o.putalpha(im.getchannel('A'))
    o.save(OUT + name + '_g.png'); print('ok', name)
for f in ('standard_bell', 'standard_forge', 'standard_prism', 'standard_swarm'):   # 后两面 = 未来时代（11-12）
    s = R + 'ArtSource/Banners/' + f + '.png'
    if os.path.exists(s): shutil.copy(s, R + 'Assets/_WarTable/Art/Textures/' + f + '.png'); print('copy', f)

# 结算页的竖幅小旗（和 ArtSource/Cover/v6/alpha/banner_kingdom.png 同尺寸 512×926）：从哥特帅旗中间裁一条竖幅，四周补一道阵营色边
from PIL import ImageDraw
A = R + 'ArtSource/Cover/v6/alpha/'
for f, out, edge in (('standard_bell', 'banner_bell', (34, 104, 92)), ('standard_forge', 'banner_forge', (226, 108, 30)),
                     ('standard_prism', 'banner_prism', (40, 176, 214)), ('standard_swarm', 'banner_swarm', (150, 80, 232))):
    s = R + 'ArtSource/Banners/' + f + '.png'
    if not os.path.exists(s): continue
    im = Image.open(s).convert('RGB'); w, h = im.size; cw = int(h * 512 / 926)
    v = im.crop(((w - cw) // 2, 0, (w + cw) // 2, h)).resize((512, 926), Image.LANCZOS)
    d = ImageDraw.Draw(v); d.rectangle([0, 0, 511, 925], outline=edge, width=22); d.rectangle([22, 22, 489, 903], outline=(20, 18, 16), width=4)
    v.save(A + out + '.png'); print('banner', out)

# ── 11-12 未来时代（样图 A）：王国帐篷 蓝 → 天青、白保持；氏族帐篷 红 → 紫，其余压成黑曜石色 → Art/Textures/Future/<名>_f.png
OUTF = R + 'Assets/_WarTable/Art/Textures/Future/'
os.makedirs(OUTF, exist_ok=True)


def kingdom_f(a):
    h, s, v = hsv(a)
    blue = (h > 185) & (h < 265) & (s > 0.2)
    return rgb(np.where(blue, 190.0, h), np.where(blue, s * 0.8, s * 0.6), np.where(blue, np.minimum(1, v * 1.25), v))


def clan_f(a):
    h, s, v = hsv(a)
    red = ((h < 22) | (h > 330)) & (s > 0.35)
    return rgb(np.where(red, 272.0, 255.0), np.where(red, np.minimum(1, s * 0.9), 0.16), np.where(red, np.minimum(1, v * 1.1), v * 0.42))


for name, fn in [('Tent_Kingdom_B', kingdom_f), ('Tent_Clan_A', clan_f), ('Tent_Clan_B', clan_f)]:
    src = P2 + name + '/albedo.png'
    if not os.path.exists(src): continue
    im = Image.open(src); a = np.asarray(im.convert('RGB')).astype(np.float64) / 255
    Image.fromarray(np.clip(fn(a) * 255 + 0.5, 0, 255).astype(np.uint8)).save(OUTF + name + '_f.png'); print('ok future', name)

# 光棱军团的结算小旗：棱镜图案很宽，按中间竖裁会切掉一半 → 把整个图案（彩色部分的外框）缩小后摆进竖幅正中
def fit_banner(f, out, edge):
    im = Image.open(R + 'ArtSource/Banners/' + f + '.png').convert('RGB'); w, h = im.size
    a = np.asarray(im).astype(np.float64) / 255; mx, mn = a.max(-1), a.min(-1); sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    core = np.zeros_like(sat, bool); core[int(h * .14):int(h * .86), int(w * .12):int(w * .88)] = True
    from scipy import ndimage as ndi   # 只取最大的那一块彩色（棱镜本体 + 三道光），四角的小六边形不要
    lab, n = ndi.label(ndi.binary_dilation((sat > 0.3) & core, iterations=12)); big = 1 + int(np.argmax(ndi.sum(np.ones_like(lab), lab, range(1, n + 1))))
    ys, xs = np.where(lab == big); pad = 10
    em = im.crop((xs.min() - pad, ys.min() - pad, xs.max() + pad, ys.max() + pad))
    field = tuple(int(v) for v in np.median(np.asarray(im)[int(h * .2):int(h * .3), int(w * .15):int(w * .25)].reshape(-1, 3), 0))
    v = Image.new('RGB', (512, 926), field)
    k = 430 / em.width; em = em.resize((430, int(em.height * k)), Image.LANCZOS); v.paste(em, (41, (926 - em.height) // 2))
    d = ImageDraw.Draw(v); d.rectangle([0, 0, 511, 925], outline=edge, width=22); d.rectangle([22, 22, 489, 903], outline=(196, 160, 84), width=4)
    v.save(A + out + '.png'); print('banner(fit)', out)


fit_banner('standard_prism', 'banner_prism', (40, 176, 214))

# ── 11-13 现代时代（样图 A：蓝盾特勤 对 赤砂佣兵）
# 帐篷：王国帐篷 蓝 → 藏青、白 → 浅灰帆布；氏族帐篷整体压成沙色帆布、红色保留（红围巾的红）→ Art/Textures/Modern/<名>_m.png
OUTM = R + 'Assets/_WarTable/Art/Textures/Modern/'
os.makedirs(OUTM, exist_ok=True)


def kingdom_m(a):
    h, s, v = hsv(a)
    blue = (h > 185) & (h < 265) & (s > 0.2)
    return rgb(np.where(blue, 222.0, h), np.where(blue, np.minimum(1, s * 0.9), s * 0.35), np.where(blue, v * 0.6, v * 0.82))


def clan_m(a):
    h, s, v = hsv(a)
    red = ((h < 22) | (h > 330)) & (s > 0.35)
    return rgb(np.where(red, 4.0, 38.0), np.where(red, np.minimum(1, s * 0.95), 0.34), np.where(red, np.minimum(1, v * 1.05), np.minimum(1, 0.22 + v * 0.75)))


for name, fn in [('Tent_Kingdom_B', kingdom_m), ('Tent_Clan_A', clan_m), ('Tent_Clan_B', clan_m)]:
    src = P2 + name + '/albedo.png'
    if not os.path.exists(src): continue
    im = Image.open(src); a = np.asarray(im.convert('RGB')).astype(np.float64) / 255
    Image.fromarray(np.clip(fn(a) * 255 + 0.5, 0, 255).astype(np.uint8)).save(OUTM + name + '_m.png'); print('ok modern', name)
# 帅旗（Tools/modern_ui_gen.py 出的 ArtSource/Banners/standard_shield|sand.png）拷进工程 + 结算小旗
for f, out, edge in (('standard_shield', 'banner_shield', (170, 214, 40)), ('standard_sand', 'banner_sand', (196, 48, 36)),
                     ('standard_lancer', 'banner_lancer', (236, 240, 244)), ('standard_ember', 'banner_ember', (248, 168, 52))):   # 深空（11-13）：星枪团 / 余烬矿帮
    sp = R + 'ArtSource/Banners/' + f + '.png'
    if not os.path.exists(sp): print('还没有', f); continue
    shutil.copy(sp, R + 'Assets/_WarTable/Art/Textures/' + f + '.png')
    # 结算小旗：按帅旗中间竖裁（徽记在正中、大小合适）；fit_banner 那种“抠出徽记再缩放”在这两面旗上会把徽记缩得很小
    im = Image.open(sp).convert('RGB'); w, h = im.size; cw = int(h * 512 / 926)
    v = im.crop(((w - cw) // 2, 0, (w + cw) // 2, h)).resize((512, 926), Image.LANCZOS)
    d = ImageDraw.Draw(v); d.rectangle([0, 0, 511, 925], outline=edge, width=22); d.rectangle([22, 22, 489, 903], outline=(20, 18, 16), width=4)
    v.save(A + out + '.png'); print('banner', out)

