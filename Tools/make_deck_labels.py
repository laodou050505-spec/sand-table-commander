"""指挥台上的字（11-16 用户：界面往深空那种西方科幻奇幻风格靠，不要之前的中国风）。
11-16 晚 用户：“按钮旁边的标签现在就要做好，缺细节”→ 第二版：名牌不再是一张平的图，而是一块有厚度的铭牌（模型 deck_plate，
Tools/make_deck_models.py），这里给每块名牌出全套 PBR 贴图：发蓝的钢板拉丝底、四周黄铜凸边、四角铆钉、**刻进去再填象牙白珐琅的字**；
按钮帽顶上的字出“颜色 + 透明度”和“法线”两张，字是凸起来的，会跟着光变亮变暗。
字体：中文思源宋体 Heavy，英文 Cinzel 粗体（都是 OFL 开源字体，只用来出贴图）。
python3 Tools/make_deck_labels.py → Assets/_WarTable/Art/Textures/Deck/Labels/
  plate_<名>_<zh|en>_albedo / _normal / _mask .png   512 × 256（_mask：R 金属度、A 光滑度）；名 = era0–era4、order_charge / hold / retreat
  cap_<名>_<zh|en>_albedo / _normal .png             512 × 512，透明底；名 = ready / fight / immerse / table"""
import pathlib
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

R = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
OUT = R / 'Assets/_WarTable/Art/Textures/Deck/Labels'; F = R / 'ArtSource/Fonts'
OUT.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(11)

def zh(size): return ImageFont.truetype(str(F / 'SourceHanSerifCN-Heavy.otf'), size)
def en(size):
    f = ImageFont.truetype(str(F / 'Cinzel.ttf'), size)
    try: f.set_variation_by_axes([900])
    except Exception: pass
    return f

def text_mask(W, H, lines, font_of, box_w, box_h, start, spacing=0.12, track=0):
    """把几行字放大到刚好装进 box、居中画成一张灰度图（0–1）。track = 字间多加的像素（英文大写拉开一点更像铭牌）。"""
    im = Image.new('L', (W, H), 0); d = ImageDraw.Draw(im)
    size = start
    def width(t, f): return d.textlength(t, font=f) + track * max(0, len(t) - 1)
    while size > 16:
        f = font_of(size)
        bbs = [d.textbbox((0, 0), t, font=f) for t in lines]
        w = max(width(t, f) for t in lines); hs = [b[3] - b[1] for b in bbs]
        gap = int(size * spacing); h = sum(hs) + gap * (len(lines) - 1)
        if w <= box_w and h <= box_h: break
        size -= 3
    y = H // 2 - h // 2
    for t, b, hh in zip(lines, bbs, hs):
        x = W / 2 - width(t, f) / 2
        if track:
            for ch in t: d.text((x, y - b[1]), ch, font=f, fill=255); x += d.textlength(ch, font=f) + track
        else: d.text((x - b[0], y - b[1]), t, font=f, fill=255)
        y += hh + gap
    return np.asarray(im, np.float32) / 255

def blur(a, r): return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32) / 255

def normal_from(h, strength):
    """高度图 → 切线空间法线图（Unity 约定：绿色朝上）。"""
    gx = np.zeros_like(h); gy = np.zeros_like(h)
    gx[:, 1:-1] = (h[:, 2:] - h[:, :-2]) * 0.5; gy[1:-1] = (h[2:] - h[:-2]) * 0.5
    n = np.stack([-gx * strength, gy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)

def streaks(H, W, amount):
    """水平拉丝：每一行一个随机亮度，再沿横向加一点细碎变化。"""
    rows = rng.normal(0, 1, (H, 1)); rows = (rows + np.roll(rows, 1, 0) + np.roll(rows, -1, 0)) / 3
    fine = blur(rng.random((H, W)).astype(np.float32), 0.6) - 0.5
    long = np.asarray(Image.fromarray((rng.random((H, W // 24 + 1)) * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR), np.float32) / 255 - 0.5
    return (rows * 0.6 + fine * 0.5 + long * 0.5) * amount

def plate(name, text, font_of, W=512, H=256, latin=False):
    S = 2; w, h = W * S, H * S
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))        # 离最近一条边多远
    rimW, bevel = 30.0, 9.0
    rim = np.clip((rimW - edge) / bevel, 0, 1)                                       # 凸边：1 = 边上，往里 bevel 像素落到 0
    rim = rim * rim * (3 - 2 * rim)
    inner = np.clip((edge - rimW) / 26.0, 0, 1)                                      # 凸边内侧的阴影带（遮蔽）
    # 铆钉：四角，凸边里面一点
    riv = np.zeros((h, w), np.float32); rivAO = np.zeros((h, w), np.float32)
    for cx in (62, w - 62):
        for cy in (62, h - 62):
            dd = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
            riv = np.maximum(riv, np.clip(1 - (dd / 15.0) ** 2, 0, 1) ** 0.5 * (dd < 15))
            rivAO = np.maximum(rivAO, np.clip(1 - (dd - 15) / 10.0, 0, 1) * (dd >= 15))
    txt = text_mask(w, h, [text], font_of, int(w * (0.7 if latin else 0.62)), int(h * 0.5), 300, track=10 if latin else 26)
    txt_soft = blur(txt, 1.6); txt_wide = blur(txt, 5.0)
    st = streaks(h, w, 1.0)
    # 高度：底板 0（拉丝细纹）、凸边 +1、铆钉 +1.3、字刻进去 −0.55（底是平的，填了珐琅）
    height = st * 0.035 + rim * 1.0 + riv * 1.3 - txt_soft * 0.55
    # 颜色（sRGB）
    steel = np.array([36, 39, 47], np.float32) + st[..., None] * 9
    patina = blur(rng.random((h // 16, w // 16)).repeat(16, 0).repeat(16, 1)[:h, :w].astype(np.float32), 22)[..., None]
    steel = steel * (0.86 + 0.28 * patina)
    brass = np.array([200, 156, 82], np.float32) * (0.88 + 0.2 * patina) + st[..., None] * 10
    ivory = np.array([238, 228, 200], np.float32) * (0.93 + 0.07 * blur(rng.random((h, w)).astype(np.float32), 3)[..., None])
    col = steel * (0.55 + 0.45 * inner[..., None])                                   # 凸边脚下压暗
    col = col * (1 - 0.5 * rivAO[..., None])
    rimMask = np.clip((rimW - edge) / 3.0, 0, 1)[..., None]
    col = col * (1 - rimMask) + brass * rimMask
    col = col * (1 - riv[..., None] * 0.999 * (riv[..., None] > 0)) + brass * 1.08 * (riv[..., None] > 0)
    # 字：珐琅填色；刻槽的边缘（字的外沿一圈）压暗，像槽壁的阴影
    groove = np.clip(txt_wide * 1.6 - txt_soft, 0, 1)[..., None]
    col = col * (1 - 0.55 * groove)
    inner_shadow = np.clip(txt_soft - blur(txt, 3.2) * 0.92, 0, 1)[..., None] * txt[..., None]
    col = col * (1 - txt_soft[..., None]) + (ivory * (1 - 0.35 * inner_shadow)) * txt_soft[..., None]
    # 凸边的高光 / 暗边（光从上方来）：上沿亮、下沿暗，给平面图一点立体感（真正的明暗交给法线）
    col = np.clip(col, 0, 255)
    metal = np.ones((h, w), np.float32) * (1 - txt_soft * 0.95)
    smooth = 0.46 + st * 0.05; smooth = smooth * (1 - rimMask[..., 0]) + 0.64 * rimMask[..., 0]
    smooth = np.where(riv > 0, 0.7, smooth); smooth = smooth * (1 - txt_soft) + 0.38 * txt_soft
    smooth -= 0.18 * (1 - inner) * (1 - rimMask[..., 0])                             # 边角积灰
    def down(a, mode='RGB'): return Image.fromarray(a.astype(np.uint8), mode).resize((W, H), Image.LANCZOS)
    down(col).save(OUT / f'plate_{name}_albedo.png')
    Image.fromarray(normal_from(blur_f(height, 1.0), 2.6), 'RGB').resize((W, H), Image.LANCZOS).save(OUT / f'plate_{name}_normal.png')
    m = np.zeros((h, w, 4), np.float32); m[..., 0] = metal; m[..., 1] = 1; m[..., 3] = np.clip(smooth, 0, 1)
    down(m * 255, 'RGBA').save(OUT / f'plate_{name}_mask.png')

def blur_f(a, r):
    """浮点高度图的模糊（范围不限于 0–1）。"""
    lo, hi = float(a.min()), float(a.max())
    return blur((a - lo) / max(1e-6, hi - lo), r) * (hi - lo) + lo

def cap(name, text, font_of, S=512):
    """按钮帽顶上的字：象牙白、凸起（法线），外面一圈细环；透明底。"""
    k = 2; s = S * k
    lines = text.split('\n')
    txt = text_mask(s, s, lines, font_of, int(s * 0.62), int(s * 0.5), 360, track=0)
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32); rr = np.sqrt((xx - s / 2) ** 2 + (yy - s / 2) ** 2)
    ring = np.clip(1 - np.abs(rr - s * 0.455) / 4.5, 0, 1)
    soft = blur(txt, 1.4); halo = blur(txt, 7.0)
    alpha = np.clip(soft + np.clip(halo * 1.7 - soft, 0, 1) * 0.5 + ring * 0.55, 0, 1)      # 字 + 字外一圈深色晕（压在亮帽子上也看得清）+ 细环
    ivory = np.array([246, 238, 216], np.float32); dark = np.array([10, 9, 9], np.float32)
    col = dark[None, None] * (1 - soft[..., None]) + ivory[None, None] * soft[..., None]
    col = col * (1 - ring[..., None] * (1 - soft[..., None])) + ivory[None, None] * 0.9 * ring[..., None] * (1 - soft[..., None])
    out = np.concatenate([np.clip(col, 0, 255), alpha[..., None] * 255], -1).astype(np.uint8)
    Image.fromarray(out, 'RGBA').resize((S, S), Image.LANCZOS).save(OUT / f'cap_{name}_albedo.png')
    height = blur(txt, 2.2) * 1.0 + ring * 0.5
    Image.fromarray(normal_from(height, 3.2), 'RGB').resize((S, S), Image.LANCZOS).save(OUT / f'cap_{name}_normal.png')

def cap_emblem(name, pts, S=512):
    """军令按钮帽顶上的令徽（冲锋 = 向上的箭头、坚守 = 盾、撤退 = 回头箭头；外形来自 ArtSource/Tokens/emblems.json）：象牙白、凸起，外面一圈细环。"""
    from PIL import ImageDraw as _D
    k = 2; s = S * k
    im = Image.new('L', (s, s), 0); d = _D.Draw(im)
    sc = s * 0.5
    if name.endswith('hold'):
        # 盾的轮廓数据是“左上角 + 右半边 + 左半边（不含首尾）”，直接连成多边形会缺半边 → 取右半边再镜像，保证左右对称
        right = sorted({(round(x, 4), round(z, 4)) for x, z in pts if x >= 0}, key=lambda q: -q[1])
        pts = right + [(-x, z) for x, z in reversed(right) if x > 0]
    d.polygon([(s / 2 + x * sc, s / 2 - z * sc) for x, z in pts], fill=255)
    txt = np.asarray(im, np.float32) / 255
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32); rr = np.sqrt((xx - s / 2) ** 2 + (yy - s / 2) ** 2)
    ring = np.clip(1 - np.abs(rr - s * 0.455) / 5.0, 0, 1)
    soft = blur(txt, 1.4); halo = blur(txt, 8.0)
    alpha = np.clip(soft + np.clip(halo * 1.7 - soft, 0, 1) * 0.5 + ring * 0.6, 0, 1)
    ivory = np.array([246, 238, 216], np.float32); dark = np.array([10, 9, 9], np.float32)
    col = dark[None, None] * (1 - soft[..., None]) + ivory[None, None] * soft[..., None]
    col = col * (1 - ring[..., None] * (1 - soft[..., None])) + ivory[None, None] * 0.9 * ring[..., None] * (1 - soft[..., None])
    out = np.concatenate([np.clip(col, 0, 255), alpha[..., None] * 255], -1).astype(np.uint8)
    Image.fromarray(out).resize((S, S), Image.LANCZOS).save(OUT / f'cap_{name}_albedo.png')
    Image.fromarray(normal_from(blur(txt, 2.4) + ring * 0.5, 3.2)).resize((S, S), Image.LANCZOS).save(OUT / f'cap_{name}_normal.png')

# 11-17 用户：文字要更有格调 → 就位 / 开战 / 亲临战阵 / 回到沙盘
CAPS = {'ready': ('就位', 'READY'), 'fight': ('开战', 'ENGAGE'), 'immerse': ('亲临\n战阵', 'TAKE THE\nFIELD'), 'table': ('回到\n沙盘', 'WAR\nTABLE')}
# 风格名和 Tools/make_era_words.py 的 NAMES 一致（那个脚本一导入就会重出字模，这里抄一份）
NAMES = [('jiandun', '剑盾', 'BLADES'), ('tieshi', '铁誓', 'IRON OATH'), ('xiaoyan', '硝烟', 'GUNSMOKE'), ('xingchen', '星尘', 'STARDUST'), ('shenkong', '深空', 'DEEP SPACE')]
ORDERS = {'charge': ('冲锋', 'CHARGE'), 'hold': ('坚守', 'HOLD'), 'retreat': ('撤退', 'RETREAT')}
for key, (z, e) in CAPS.items():
    cap(f'{key}_zh', z, zh); cap(f'{key}_en', e, en)
for i, (_, z, e) in enumerate(NAMES):
    plate(f'era{i}_zh', z, zh); plate(f'era{i}_en', e, en, latin=True)
for key, (z, e) in ORDERS.items():
    plate(f'order_{key}_zh', z, zh); plate(f'order_{key}_en', e, en, latin=True)
import json
EMB = json.load(open(R / 'ArtSource/Tokens/emblems.json'))
for key in ORDERS: cap_emblem(f'order_{key}', EMB[key])
print('ok', len(list(OUT.glob('*.png'))))
