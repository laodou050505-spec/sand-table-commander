"""拼贴风格的小件共用工具（11-17）：战场上的旗牌、状态牌、下令牌、把手、主将牌、暂停键这些小东西，
和结算板 / 暂停菜单是同一套“纸片”——纸纹、撕边、细金线、自带软阴影。大件（结算板等）在 make_collage_ui.py 里直接用生图原件合成；
这里的小件贴图只有一两百像素，纸纹取羊皮纸原件的明暗，撕边用噪声程序生成。
用法（各 make_*.py 里）：import collage_kit as ck
  ck.slip(W, H, rgb, torn='lr')      一张纸条（RGBA，四周自带阴影边）；torn 里写哪几条边是撕的（l r t b）
  ck.pennant(W, H, rgb, rim, hot)    一面尖底小旗（部队旗牌）
  ck.disc(S, rgb)                    一枚圆纸片：细金双圈 + 一圈刻度
配色常量：NAVY 藏青、GOLD 黄铜、GOLD_D 暗金、IVORY 象牙白、INK 墨蓝。"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage as ndi

R = '/Users/ld/Documents/ChatGPT/WarTable/'
NAVY = (17, 24, 42); GOLD = (226, 188, 116); GOLD_D = (150, 120, 72); IVORY = (238, 229, 208); INK = (22, 28, 44)
_grain = None

def grain(w, h, seed=0):
    """纸纹：羊皮纸原件中间一块的明暗（围绕 1 上下浮动），按 seed 取不同的位置。"""
    global _grain
    if _grain is None:
        g = np.asarray(Image.open(R + 'ArtSource/UI/gen/collage/src_paper.png').convert('L'), np.float32)
        H0, W0 = g.shape; g = g[int(H0 * 0.25):int(H0 * 0.75), int(W0 * 0.2):int(W0 * 0.8)]
        _grain = np.clip(g / (ndi.gaussian_filter(g, 40) + 1e-3), 0.8, 1.2)
    H0, W0 = _grain.shape; rng = np.random.default_rng(seed)
    cw, ch = min(W0, max(w, 300)), min(H0, max(h, 200)); x0 = int(rng.integers(0, W0 - cw + 1)); y0 = int(rng.integers(0, H0 - ch + 1))
    im = Image.fromarray((_grain[y0:y0 + ch, x0:x0 + cw] * 100).astype(np.uint8)).resize((w, h), Image.LANCZOS)
    return np.asarray(im, np.float32) / 100

def _wobble(n, amp, seed):
    """一条撕边的进出量（像素）：一段慢的起伏 + 细碎的毛边。"""
    rng = np.random.default_rng(seed)
    slow = np.interp(np.arange(n), np.linspace(0, n, max(3, n // 22)), rng.random(max(3, n // 22)))
    return (slow * 0.7 + rng.random(n) * 0.3) * amp

def shadowed(rgb, a, pad, sh=(3, 5), blur=4, op=0.5):
    """纸片（rgb, a 都是 h×w 浮点）→ 四周加 pad 的 RGBA 图，垫一圈软阴影。"""
    h, w = a.shape; H, W = h + pad * 2, w + pad * 2
    A_ = np.zeros((H, W), np.float32); A_[pad:pad + h, pad:pad + w] = a
    s = ndi.gaussian_filter(np.roll(A_, (sh[1], sh[0]), axis=(0, 1)), blur) * op
    C = np.zeros((H, W, 3), np.float32); C[pad:pad + h, pad:pad + w] = rgb
    out = np.zeros((H, W, 4), np.float32)
    out[..., :3] = C * A_[..., None] + np.array([4, 5, 9], np.float32) * (1 - A_[..., None]); out[..., 3] = (A_ + s * (1 - A_)) * 255
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), 'RGBA')

def _paper(w, h, rgb, seed, grad=0.16):
    """一块染了色的纸面：上深下浅一点 + 纸纹 + 颗粒。"""
    t = (np.arange(h, dtype=np.float32) / max(1, h - 1))[:, None, None]
    base = np.array(rgb, np.float32) * (1 - grad + grad * 2 * t)
    g = grain(w, h, seed)[..., None] ** 2.0
    return base * g + np.random.default_rng(seed + 7).normal(0, 2.0, (h, w, 1)).astype(np.float32)

def slip(W, H, rgb, torn='lr', pad=10, seed=1, amp=None, rim=True):
    """一张纸条。返回 (RGBA 图, 纸面矩形 (x0, y0, x1, y1))——字和线画在纸面矩形里。"""
    w, h = W - pad * 2, H - pad * 2; amp = amp or max(3.0, h * 0.09)
    a = np.ones((h, w), np.float32); yy, xx = np.mgrid[0:h, 0:w].astype(np.float32); edge = np.zeros((h, w), np.float32)
    for side in torn:
        if side in 'lr':
            n = _wobble(h, amp, seed + ord(side))[:, None]; dist = (xx - n) if side == 'l' else ((w - 1 - xx) - n)
        else:
            n = _wobble(w, amp, seed + ord(side))[None, :]; dist = (yy - n) if side == 't' else ((h - 1 - yy) - n)
        a = a * np.clip(dist + 0.5, 0, 1); edge = np.maximum(edge, np.clip(1 - dist / 2.2, 0, 1))
    col = _paper(w, h, rgb, seed)
    if rim:                                                                    # 撕开的边露出一线浅色纸芯
        k = (edge * (a > 0.3))[..., None] * 0.6; col = col * (1 - k) + np.array([176, 178, 184], np.float32) * k
    return shadowed(col, a, pad, sh=(max(2, pad // 4), max(3, pad // 2)), blur=max(2.0, pad * 0.38)), (pad, pad, W - pad, H - pad)

def pennant(W, H, rgb, rim, hot=False, pad=12, seed=1, tip=0.2, band=0.0, band_rgb=None):
    """一面尖底小旗：上边和两侧是裁的，下面收成一个尖（指着部队）。rim = 细线和字的颜色；hot = 被指着（外圈一道金光）。
    band > 0 时最上面一截换成 band_rgb（写番号的那一条）。返回 RGBA 图。"""
    S = 3; w, h = W - pad * 2, H - pad * 2
    m = Image.new('L', (w * S, h * S), 0); d = ImageDraw.Draw(m); ty = h * (1 - tip)
    pts = [(0, 0), (w, 0), (w, ty), (w * 0.5, h), (0, ty)]
    d.polygon([(x * S, y * S) for x, y in pts], fill=255)
    a = np.asarray(m.resize((w, h), Image.LANCZOS), np.float32) / 255
    col = _paper(w, h, rgb, seed, grad=0.1)
    if band > 0:
        bh = int(h * band); col[:bh] = _paper(w, bh, band_rgb or tuple(int(v * 0.62) for v in rgb), seed + 3, grad=0.0)
    lay = Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0)); d = ImageDraw.Draw(lay); i = 7                 # 往里缩一圈的细线，在两条斜边中间断开
    ins = [(i, i), (w - i, i), (w - i, ty - i * 0.4), (w * 0.5, h - i * 1.7), (i, ty - i * 0.4)]
    P = lambda p: (p[0] * S, p[1] * S)
    def seg(p, q, f0=0.0, f1=1.0): d.line([P((p[0] + (q[0] - p[0]) * f0, p[1] + (q[1] - p[1]) * f0)), P((p[0] + (q[0] - p[0]) * f1, p[1] + (q[1] - p[1]) * f1))], fill=tuple(rim) + (235,), width=2 * S)
    seg(ins[0], ins[1]); seg(ins[1], ins[2]); seg(ins[4], ins[0]); seg(ins[2], ins[3], 0.0, 0.38); seg(ins[2], ins[3], 0.7, 1.0); seg(ins[3], ins[4], 0.0, 0.3); seg(ins[3], ins[4], 0.62, 1.0)
    d.polygon([P((w * 0.5, h - i * 1.7 - 9)), P((w * 0.5 + 5, h - i * 1.7 - 4)), P((w * 0.5, h - i * 1.7 + 1)), P((w * 0.5 - 5, h - i * 1.7 - 4))], fill=tuple(rim) + (255,))
    L = np.asarray(lay.resize((w, h), Image.LANCZOS), np.float32); la = L[..., 3:] / 255; col = col * (1 - la) + L[..., :3] * la
    edge = np.clip(1 - ndi.gaussian_filter(a, 1.2), 0, 1) * a                                               # 纸的裁边：一线亮边，离远了也分得出轮廓
    col = col + edge[..., None] * 3.2 * (np.array(rim, np.float32) - col) * 0.5
    im = shadowed(col, a, pad, sh=(3, 5), blur=4.5, op=0.6)
    if hot:
        g = np.zeros((H, W), np.float32); g[pad:pad + h, pad:pad + w] = a; halo = np.clip(ndi.gaussian_filter(g, 5.5) * 1.5, 0, 1) * (1 - g)
        arr = np.asarray(im, np.float32); k = halo[..., None]
        arr[..., :3] = arr[..., :3] * (1 - k) + np.array([255, 214, 120], np.float32) * k; arr[..., 3] = np.maximum(arr[..., 3], halo * 235)
        im = Image.fromarray(arr.clip(0, 255).astype(np.uint8), 'RGBA')
    return im

def disc(S, rgb=NAVY, ring=GOLD, ticks=48, pad=None, seed=2, inner=True):
    """一枚圆纸片：纸纹底，细金双圈，外圈一周刻度。返回 S×S 的 RGBA 图（含阴影边）；中心 (S/2, S/2)，纸片半径 S/2 - pad。"""
    pad = pad or int(S * 0.07); n = S - pad * 2; K = 3
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32); r = np.hypot(xx - n / 2 + 0.5, yy - n / 2 + 0.5) / (n / 2)
    a = np.clip((1 - r) * (n / 2) + 0.5, 0, 1)
    col = _paper(n, n, rgb, seed, grad=0.14) + np.exp(-(r / 0.55) ** 2)[..., None] * np.array([16, 12, 5], np.float32)
    lay = Image.new('RGBA', (n * K, n * K), (0, 0, 0, 0)); d = ImageDraw.Draw(lay); c = n * K / 2
    def circ(f, wd, al=255): d.ellipse((c - c * f, c - c * f, c + c * f, c + c * f), outline=tuple(ring) + (al,), width=wd)
    circ(0.93, max(2, int(n * K * 0.012))); circ(0.86, max(1, int(n * K * 0.004)), 190)
    if inner: circ(0.66, max(1, int(n * K * 0.004)), 150)
    for k in range(ticks):
        t = 2 * math.pi * k / ticks; r0, r1 = c * 0.87, c * (0.915 if k % 4 else 0.925)
        d.line([(c + math.cos(t) * r0, c + math.sin(t) * r0), (c + math.cos(t) * r1, c + math.sin(t) * r1)], fill=tuple(ring) + (200,), width=max(1, int(n * K * 0.004)))
    L = np.asarray(lay.resize((n, n), Image.LANCZOS), np.float32); la = L[..., 3:] / 255; col = col * (1 - la) + L[..., :3] * la
    return shadowed(col, a, pad, sh=(max(2, pad // 5), max(3, pad // 3)), blur=max(2.5, pad * 0.3), op=0.55)

def star4(d, cx, cy, r, fill):
    k = r * 0.2
    d.polygon([(cx, cy - r), (cx + k, cy - k), (cx + r, cy), (cx + k, cy + k), (cx, cy + r), (cx - k, cy + k), (cx - r, cy), (cx - k, cy - k)], fill=fill)

def diamond(d, cx, cy, r, fill): d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=fill)


# ================================================================ 用生图原件做的“真纸”小件
# 11-17 晚用户看了第一版小件（上面那些程序画的纯色纸片）说“没有之前那种感觉了，有点素”——结算板的感觉来自三样东西：
# 真的纸（撕边的纤维、折痕）、铜版画线稿（日轮）、象牙白压藏青的叠层。下面这些函数把同一批原件用到小件上。
_cache = {}

def _cut(name, flip=False, quarter=0, maxdim=1500):
    """黑底原件 → (rgb, a)，裁到内容、缩到 maxdim 以内；quarter = 逆时针转几个 90°。结果缓存。"""
    key = (name, flip, quarter, maxdim)
    if key in _cache: return _cache[key]
    im = Image.open(R + 'ArtSource/UI/gen/collage/' + name).convert('RGB')
    if flip: im = im.transpose(Image.ROTATE_180)
    for _ in range(quarter): im = im.transpose(Image.ROTATE_90)
    k = maxdim / max(im.size); im = im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)
    rgb = np.asarray(im, np.float32); mx = rgb.max(-1) / 255
    a = ndi.gaussian_filter(np.clip((mx - 0.10) / 0.14, 0, 1), 0.6)
    lab, n = ndi.label(a > 0.5)
    if n > 1: a = a * (lab == 1 + int(np.argmax(ndi.sum(a > 0.5, lab, range(1, n + 1)))))
    ys, xs = np.where(a > 0.05); rgb, a = rgb[ys.min():ys.max() + 1, xs.min():xs.max() + 1], a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    w = ndi.gaussian_filter((a > 0.9).astype(np.float32), 4)[..., None] + 1e-4
    fill = np.stack([ndi.gaussian_filter(rgb[..., c] * (a > 0.9), 4) for c in range(3)], -1) / w
    _cache[key] = (np.where((a > 0.9)[..., None], rgb, fill), a); return _cache[key]

def _fit(rgb, a, w, h):
    """把一块纸缩放到 w×h：两头（或四边）的撕边按原比例保留，只拉伸中段。"""
    H0, W0 = a.shape; k = h / H0; e = int(min(W0 * 0.2, w / k * 0.3))
    def rs(x, ww, hh):
        ch3 = x.ndim == 3
        im = Image.fromarray(x.clip(0, 255).astype(np.uint8) if ch3 else (x * 255).clip(0, 255).astype(np.uint8)).resize((max(1, ww), hh), Image.LANCZOS)
        return np.asarray(im, np.float32) / (1 if ch3 else 255)
    ew = max(1, int(round(e * k))); mid = w - 2 * ew
    if mid < 4: return rs(rgb, w, h), rs(a, w, h)
    return (np.concatenate([rs(rgb[:, :e], ew, h), rs(rgb[:, e:W0 - e], mid, h), rs(rgb[:, W0 - e:], ew, h)], 1),
            np.concatenate([rs(a[:, :e], ew, h), rs(a[:, e:W0 - e], mid, h), rs(a[:, W0 - e:], ew, h)], 1))

def tint(rgb, a, color, grad=0.3, seed=0):
    """把纸染成 color（象牙白时只做调色）：留下折痕的明暗，上深下浅，撕开的边露出浅色纸芯。"""
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32) / 255
    if tuple(color) == IVORY:
        flat = lum[..., None] * np.array(IVORY, np.float32) / 0.93; return (rgb * 0.4 + flat * 0.6) * 0.95
    loc = np.clip(lum / (ndi.gaussian_filter(lum, 14) + 1e-3), 0.8, 1.2) ** 2.6
    h = a.shape[0]; t = (np.arange(h, dtype=np.float32) / max(1, h - 1))[:, None, None]
    out = np.array(color, np.float32) * (1 - grad + grad * 2 * t) * loc[..., None]
    out += np.random.default_rng(seed).normal(0, 2.2, out.shape[:2] + (1,)).astype(np.float32)
    rim = np.clip((0.9 - ndi.gaussian_filter(a, 1.6)) / 0.45, 0, 1) * (a > 0.2)
    return out * (1 - rim[..., None] * 0.7) + np.array([176, 180, 188], np.float32) * rim[..., None] * 0.7 * lum[..., None]

def real_strip(W, H, color, pad=10, flip=False, cut=''):
    """真纸条（原件 src_strip：上下是裁的、两头是撕的）。cut 里写 l / r = 那一头剪齐。返回 (rgb, a, 纸面矩形)，还没加阴影——
    画完字和线以后用 finish() 收尾。"""
    rgb, a = _cut('src_strip.png', flip); rgb, a = _fit(rgb, a, W - pad * 2, H - pad * 2); w = a.shape[1]
    if 'l' in cut: a = a * np.clip((np.arange(w) - w * 0.035) / 1.5, 0, 1)[None, :]
    if 'r' in cut: a = a * np.clip((w * 0.965 - np.arange(w)) / 1.5, 0, 1)[None, :]
    return tint(rgb, a, color, seed=W + H), a, (pad, pad, W - pad, H - pad)

def real_sheet(W, H, color, pad=14, flip=False, quarter=0):
    """真纸片（原件 src_paper：四边都是撕的）。quarter=1 竖过来用。返回 (rgb, a, 纸面矩形)。"""
    rgb, a = _cut('src_paper.png', flip, quarter)
    def rs(x):
        ch3 = x.ndim == 3
        im = Image.fromarray(x.clip(0, 255).astype(np.uint8) if ch3 else (x * 255).clip(0, 255).astype(np.uint8)).resize((W - pad * 2, H - pad * 2), Image.LANCZOS)
        return np.asarray(im, np.float32) / (1 if ch3 else 255)
    rgb, a = rs(rgb), rs(a)
    return tint(rgb, a, color, grad=0.22, seed=W), a, (pad, pad, W - pad, H - pad)

def finish(rgb, a, pad, sh=None, blur=None, op=0.5):
    """纸片 → 带阴影边的 RGBA 图。"""
    return shadowed(rgb, a, pad, sh=sh or (max(2, pad // 4), max(3, pad // 2)), blur=blur or max(2.0, pad * 0.38), op=op)

def put(rgb, a, lay, clip=True):
    """把一张 RGBA 图层（和纸同尺寸，或整数倍大）印到纸上。"""
    lay = lay.resize((rgb.shape[1], rgb.shape[0]), Image.LANCZOS); L = np.asarray(lay, np.float32); la = L[..., 3:] / 255
    return rgb * (1 - la) + L[..., :3] * la, (a if clip else np.maximum(a, la[..., 0]))

def sun_lines(S, r0=0.0, r1=1.0):
    """铜版画日轮的线稿（0–1 的透明度图，S×S），只留半径 r0–r1 之间的部分：0–0.62 是光芒，0.62–0.95 是刻度圈和小星。"""
    key = ('sun', S)
    if key not in _cache:
        im = Image.open(R + 'ArtSource/UI/gen/collage/src_sun.png').convert('RGB').resize((S, S), Image.LANCZOS)
        _cache[key] = np.clip((np.asarray(im, np.float32).max(-1) / 255 - 0.2) / 0.5, 0, 1)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32); r = np.hypot(xx - S / 2 + 0.5, yy - S / 2 + 0.5) / (S / 2)
    return _cache[key] * (r >= r0) * (r <= r1)

def ring_disc(S, center=NAVY, ring=GOLD, pad=None, seed=2, rays=0.0, field=0.6):
    """一枚圆纸片，边上一圈是真正的铜版画刻度圈（取自日轮原件的外圈），中间 field 半径以内是 center 色的空地（放图标）。
    rays > 0 时空地里透出淡淡的日轮光芒。返回 S×S 的 RGBA 图（含阴影边）。"""
    pad = pad or int(S * 0.07); n = S - pad * 2
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32); r = np.hypot(xx - n / 2 + 0.5, yy - n / 2 + 0.5) / (n / 2)
    a = np.clip((1 - r) * (n / 2) + 0.5, 0, 1)
    col = _paper(n, n, NAVY, seed, grad=0.14)
    if tuple(center) != NAVY:
        k = np.clip((field - r) * (n / 2) + 0.5, 0, 1)[..., None]; col = col * (1 - k) + _paper(n, n, center, seed + 1, grad=0.12) * k
    col = col + np.exp(-(r / 0.5) ** 2)[..., None] * np.array([14, 10, 4], np.float32)
    # 日轮原件的刻度圈在半径 0.60–0.95：整张按 0.95 → 0.965 放大一点贴满纸片
    L = sun_lines(int(n / 0.965 * 0.95 / 0.95), 0.0, 0.96); off = (L.shape[0] - n) // 2; L = L[off:off + n, off:off + n]
    band = L * (r > field + 0.02) + L * (r <= field) * rays
    g = np.array(ring, np.float32) * (0.85 + 0.25 * (1 - yy / n))[..., None]
    col = col * (1 - band[..., None]) + g * band[..., None]
    edge = np.clip(1 - np.abs(r - field) * (n / 2) / 1.6, 0, 1)[..., None] * 0.9                     # 空地边上一道细金线
    col = col * (1 - edge) + np.array(ring, np.float32) * edge
    return shadowed(col, a, pad, sh=(max(2, pad // 5), max(3, pad // 3)), blur=max(2.5, pad * 0.3), op=0.55)

def dots(shape, cell, amount):
    """半调网点：amount（0–1 的图）越大点越大。返回 0–1 的图。"""
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]].astype(np.float32)
    dx = (xx % cell) - cell / 2; dy = ((yy + (np.floor(xx / cell) % 2) * cell / 2) % cell) - cell / 2
    return np.clip((amount * cell * 0.46 - np.sqrt(dx * dx + dy * dy)) / 1.2 + 0.5, 0, 1)

def corner_dots(rgb, cell, cx, cy, rx, ry, color, strength=0.6):
    """在纸上某个角印一片由密到疏的网点（cx, cy = 最密处，rx, ry = 铺开的范围，都是像素）。"""
    h, w = rgb.shape[:2]; yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    amt = np.clip(1 - np.hypot((xx - cx) / rx, (yy - cy) / ry), 0, 1) ** 1.2
    d = dots((h, w), cell, amt)[..., None] * strength
    return rgb * (1 - d) + np.array(color, np.float32) * d

def paste_rot(base, im, cx, cy, deg):
    """把一张 RGBA 小图转 deg 度后，中心对准 (cx, cy) 贴到 base 上（会伸出去的部分照贴，不裁）。"""
    if deg: im = im.rotate(deg, resample=Image.BICUBIC, expand=True)
    base.alpha_composite(im, (int(cx - im.width / 2), int(cy - im.height / 2)))

def pennant2(W, H, rgb, rim, hot=False, pad=12, top=26, tip=0.2, seed=1, icon_y=None):
    """尖底小旗的真纸版：染了队伍色的真纸（带折痕），下面两条斜边是撕的；纸上透出一轮淡淡的铜版画日轮（圆心在 icon_y，图标压在它上面），
    旗尖一带印着网点，往里缩一圈的细线只走下半截。top = 旗面上沿离画布顶多远（上面留给斜贴的番号签）。返回 RGBA 图（W×H）。"""
    w, h = W - pad * 2, H - top - pad; S = 3
    m = Image.new('L', (w * S, h * S), 0); ty = h * (1 - tip)
    ImageDraw.Draw(m).polygon([(0, 0), (w * S, 0), (w * S, ty * S), (w * S * 0.5, h * S), (0, ty * S)], fill=255)
    a = np.asarray(m.resize((w, h), Image.LANCZOS), np.float32) / 255
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    rng = np.random.default_rng(seed); nz = np.asarray(Image.fromarray((rng.random((h // 5 + 2, w // 5 + 2)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
    torn = np.clip((yy - ty + 14) / 14, 0, 1)                                                     # 只有斜边那一段是撕的
    a = np.clip((ndi.gaussian_filter(a, 1.4) + (nz - 0.5) * 0.5 * torn - 0.5) * 6 + 0.5, 0, 1) * (a > 0.02)
    src, sa = _cut('src_paper.png', seed % 2 == 1); H0, W0 = sa.shape                                # 真纸中间一块（带折痕）
    crop = src[int(H0 * 0.22):int(H0 * 0.78), int(W0 * 0.3):int(W0 * 0.3) + int(H0 * 0.56 * w / h)]
    tex = np.asarray(Image.fromarray(crop.clip(0, 255).astype(np.uint8)).resize((w, h), Image.LANCZOS), np.float32)
    col = tint(tex, a, rgb, grad=0.14, seed=seed)
    cy = (icon_y - top) if icon_y else h * 0.48; D = int(w * 1.06)                                     # 日轮水印
    L = sun_lines(D, 0.0, 0.96); wm = np.zeros((h, w), np.float32); y0 = int(cy - D / 2); x0 = (w - D) // 2
    ys0, ys1 = max(0, y0), min(h, y0 + D); xs0, xs1 = max(0, x0), min(w, x0 + D)
    wm[ys0:ys1, xs0:xs1] = L[ys0 - y0:ys1 - y0, xs0 - x0:xs1 - x0]
    lum = (np.array(rgb, np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)) / 255
    col = col + wm[..., None] * (np.array(rim, np.float32) - col) * (0.3 if lum < 0.25 else 0.24)
    col = corner_dots(col, 9, w * 0.5, h * 1.02, w * 0.75, h * 0.36, tuple(int(v * 0.45) for v in rgb), 0.7)
    lay = Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0)); d = ImageDraw.Draw(lay); i = 7
    ins = [(i, h * 0.42), (i, ty - i * 0.4), (w * 0.5, h - i * 1.8), (w - i, ty - i * 0.4), (w - i, h * 0.42)]
    P = lambda p: (p[0] * S, p[1] * S)
    def seg(p, q, f0=0.0, f1=1.0): d.line([P((p[0] + (q[0] - p[0]) * f0, p[1] + (q[1] - p[1]) * f0)), P((p[0] + (q[0] - p[0]) * f1, p[1] + (q[1] - p[1]) * f1))], fill=tuple(rim) + (235,), width=2 * S)
    seg(ins[0], ins[1]); seg(ins[4], ins[3]); seg(ins[1], ins[2], 0.0, 0.34); seg(ins[1], ins[2], 0.66, 1.0); seg(ins[3], ins[2], 0.0, 0.34); seg(ins[3], ins[2], 0.66, 1.0)
    diamond(d, w * 0.5 * S, (h - i * 1.8 - 5) * S, 5 * S, tuple(rim) + (255,))
    col, _ = put(col, a, lay)
    body = shadowed(col, a, pad, sh=(3, 5), blur=4.5, op=0.6)                                          # (h + 2 pad) × W，贴到画布的 top - pad 处
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); im.alpha_composite(body, (0, top - pad))
    if hot:
        g = np.asarray(im, np.float32)[..., 3] / 255; solid = (g > 0.9).astype(np.float32)
        halo = np.clip(ndi.gaussian_filter(solid, 5.5) * 1.6, 0, 1) * (1 - solid)
        arr = np.asarray(im, np.float32).copy(); k = halo[..., None]
        arr[..., :3] = arr[..., :3] * (1 - k) + np.array([255, 214, 120], np.float32) * k; arr[..., 3] = np.maximum(arr[..., 3], halo * 235)
        im = Image.fromarray(arr.clip(0, 255).astype(np.uint8), 'RGBA')
    return im

def chip(text, font, color=IVORY, ink=INK, padx=20, h=None, pad=7, cut='l', flip=False):
    """一小片写着字的纸签（真纸条）：返回 RGBA 图。字在纸面正中。"""
    d0 = ImageDraw.Draw(Image.new('RGBA', (8, 8))); bb = d0.textbbox((0, 0), text, font=font); tw = bb[2] - bb[0]
    h = h or int((bb[3] - bb[1]) * 1.55) + pad * 2; W = int(tw + padx * 2 + pad * 2)
    rgb, a, (x0, y0, x1, y1) = real_strip(W, h, color, pad=pad, cut=cut, flip=flip); im = finish(rgb, a, pad)
    ImageDraw.Draw(im).text(((x0 + x1) / 2, (y0 + y1) / 2), text, font=font, fill=tuple(ink) + (255,), anchor='mm')
    return im
