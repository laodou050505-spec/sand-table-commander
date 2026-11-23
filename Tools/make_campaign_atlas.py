"""战役界面的图集（11-19）：作战地图上运行时叠加的东西 + 奖励 / 补给卡片上的图标和字。
输出 Assets/_WarTable/Resources/Campaign/：
  atlas.png + atlas.json   ——白色（可着色）的油性笔军标、圈、叉、箭头、手写数字、按钮符号；彩色的纹章徽章、星晶、卡片底；中英文字标签
  route_dash.png / route_solid.png ——航线条带的贴图（横向平铺：虚线 / 实线），运行时沿折线拉成条带
画法：先在 2 倍大小上画，再缩小（线条边缘柔和）；油性笔的蜡质感 = 透明度乘一层细噪声。
用法：python3 Tools/make_campaign_atlas.py"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(__file__))
import collage_kit as ck
from make_campaign_map import TROOP_NAMES, TROOPS, troop_mark, FONTS, OUT, star4

rng = np.random.default_rng(11)
NAVY, GOLD, IVORY, INK = ck.NAVY, ck.GOLD, ck.IVORY, ck.INK
WHITE = (255, 255, 255)


def font(name, size): return ImageFont.truetype(FONTS + name, size)
def F_HAND(size): f = font('Caveat[wght].ttf', size); f.set_variation_by_axes([700]); return f
def F_ZH(size, heavy=False): return font('SourceHanSerifCN-Heavy.otf' if heavy else 'SourceHanSerifCN-Bold.otf', size)
def F_TYPE(size, bold=True): return font('CourierPrime-Bold.ttf' if bold else 'CourierPrime-Regular.ttf', size)
def F_CAPS(size): return font('Cinzel.ttf', size)
def F_HZH(size): return font('LXGWWenKai-Regular.ttf', size)


def wax(a, seed=0, amt=0.35):
    """油性笔的蜡质感：透明度乘细噪声（笔画里有一点点漏白）。a: 0–1 浮点。"""
    n = ndi.gaussian_filter(np.random.default_rng(seed).random(a.shape).astype(np.float32), 0.8)
    n = (n - n.mean()) / (n.std() + 1e-6)
    return np.clip(a * (1 - amt * 0.5 + amt * 0.5 * np.tanh(n * 1.5)), 0, 1)


class Pen:
    """在 2 倍画布上用手抖的线条画（白色），to_rgba() 缩回 1 倍并加蜡质感。"""
    def __init__(self, w, h, ss=2):
        self.ss = ss; self.w, self.h = w, h
        self.im = Image.new('L', (w * ss, h * ss), 0); self.d = ImageDraw.Draw(self.im)

    def stroke(self, pts, width, jitter=0.6, overshoot=0.0, seed=0):
        """一笔：沿点列画，线宽两头略收、带一点弯；overshoot = 两端多冲出去的比例（手画直线的样子）。"""
        r = np.random.default_rng(seed)
        P = np.array(pts, np.float32) * self.ss
        if overshoot and len(P) == 2:
            v = P[1] - P[0]; P = np.array([P[0] - v * overshoot * r.uniform(0.3, 1), P[1] + v * overshoot * r.uniform(0.3, 1)])
        # 细分 + 轻微弯曲
        if len(P) == 2:
            t = np.linspace(0, 1, 24)[:, None]; n = np.array([-(P[1] - P[0])[1], (P[1] - P[0])[0]]); n = n / (np.linalg.norm(n) + 1e-6)
            bow = r.uniform(-1, 1) * np.linalg.norm(P[1] - P[0]) * 0.02
            P = P[0] + (P[1] - P[0]) * t + n * (np.sin(t * math.pi) * bow)
        L = len(P)
        for i in range(L - 1):
            u = i / max(1, L - 2)
            wv = width * self.ss * (0.75 + 0.25 * math.sin(u * math.pi)) * r.uniform(0.92, 1.05)
            a = P[i] + r.normal(0, jitter * 0.3, 2); b = P[i + 1] + r.normal(0, jitter * 0.3, 2)
            self.d.line((a[0], a[1], b[0], b[1]), fill=255, width=max(1, int(wv)))
            self.d.ellipse((b[0] - wv / 2, b[1] - wv / 2, b[0] + wv / 2, b[1] + wv / 2), fill=255)

    def ring(self, cx, cy, rx, ry, width, seed=0, turns=1.12):
        """手画的圈：起笔和收笔错开一点、不闭合得很准。"""
        r = np.random.default_rng(seed); a0 = r.uniform(0, 2 * math.pi)
        t = np.linspace(0, 2 * math.pi * turns, 90)
        wob = 1 + 0.035 * np.sin(t * 2 + r.uniform(0, 6)) + 0.02 * np.sin(t * 5 + r.uniform(0, 6))
        grow = 1 + 0.06 * (t / t[-1])
        pts = np.stack([cx + rx * wob * grow * np.cos(a0 + t), cy + ry * wob * grow * np.sin(a0 + t)], 1)
        self.stroke(pts, width, seed=seed)

    def rgba(self, seed=0, rgb=WHITE, amt=0.35):
        a = np.asarray(self.im.resize((self.w, self.h), Image.LANCZOS), np.float32) / 255
        a = wax(a, seed, amt)
        out = np.zeros((self.h, self.w, 4), np.uint8); out[..., :3] = rgb; out[..., 3] = (a * 255).astype(np.uint8)
        return Image.fromarray(out)


ITEMS = {}   # 名字 → RGBA 图


def add(name, im): ITEMS[name] = im


# ═════════ 节点纹章（11-20 v2：西式奇幻——印刷的纹章 / 封蜡色，不再是油性笔军标；彩色，运行时只调透明度） ═════════
# 敌军 = 朱红菱形（里面一对交叉的剑），精英加一道金边和小王冠，Boss 深红大菱形 + 金冠；
# 补给 / 军械 = 藏青纹章盾（帐篷 / 交叉锤），遗迹 = 象牙白圆章里一根断柱；我军 = 藏青三角小旗插在金杆上。
VERM = (156, 58, 38); CRIM = (112, 28, 24); SEPIA = (78, 52, 34); GOLD_D = ck.GOLD_D
from make_campaign_map import shield_pts, heraldic, star4


def hi(S): return Image.new('RGBA', (S * 2, S * 2), (0, 0, 0, 0))


def down(im, S): return im.resize((S, S), Image.LANCZOS)


def lozenge(d, cx, cy, r): return [(cx, cy - r), (cx + r * 0.82, cy), (cx, cy + r), (cx - r * 0.82, cy)]


def swords(d, cx, cy, r, col, lw):
    """一对交叉的剑（刃 + 护手 + 柄头）。"""
    for sg in (1, -1):
        ax, ay = cx - r * 0.62 * sg, cy + r * 0.62; bx, by = cx + r * 0.62 * sg, cy - r * 0.62
        d.line((ax, ay, bx, by), fill=col, width=lw)
        ux, uy = (bx - ax), (by - ay); L = math.hypot(ux, uy); ux, uy = ux / L, uy / L; px, py = -uy, ux
        gx, gy = ax + ux * r * 0.32, ay + uy * r * 0.32
        d.line((gx - px * r * 0.22, gy - py * r * 0.22, gx + px * r * 0.22, gy + py * r * 0.22), fill=col, width=lw)
        d.ellipse((ax - lw * 1.1, ay - lw * 1.1, ax + lw * 1.1, ay + lw * 1.1), fill=col)


def crown(d, cx, cy, w, col):
    h = w * 0.62
    d.polygon([(cx - w / 2, cy + h / 2), (cx - w / 2, cy - h * 0.15), (cx - w * 0.25, cy + h * 0.12), (cx, cy - h / 2), (cx + w * 0.25, cy + h * 0.12), (cx + w / 2, cy - h * 0.15), (cx + w / 2, cy + h / 2)], fill=col)


def node_symbol(kind):
    S = 260 if kind == 'boss' else 220; im = hi(S); d = ImageDraw.Draw(im); c = S; s = 2
    if kind in ('battle', 'elite', 'boss'):
        r = S * (0.62 if kind == 'boss' else 0.56); cy = c + S * 0.08
        fill = CRIM if kind == 'boss' else VERM
        if kind != 'battle': d.polygon(lozenge(d, c, cy, r * 1.16), fill=GOLD + (255,))
        if kind != 'battle': d.polygon(lozenge(d, c, cy, r * 1.08), fill=(40, 22, 18, 255))
        d.polygon(lozenge(d, c, cy, r), fill=fill + (255,))
        d.line(lozenge(d, c, cy, r * 0.86) + [lozenge(d, c, cy, r * 0.86)[0]], fill=IVORY + (170,), width=2 * s)
        swords(d, c, cy, r * 0.56, IVORY + (255,), 9 * s)   # 远看要认得出：剑画粗
        if kind == 'elite': crown(d, c, cy - r * 1.16 - S * 0.06, S * 0.3, GOLD + (255,))
        if kind == 'boss':
            crown(d, c, cy - r * 1.16 - S * 0.08, S * 0.42, GOLD + (255,))
            for a in (-35, 35): ra = math.radians(a - 90); d.line((c + math.cos(ra) * S * 0.36, cy - r * 1.16 - S * 0.12 + math.sin(ra) * S * 0.0, c + math.cos(ra) * S * 0.5, cy - r * 1.16 - S * 0.12 + math.sin(ra) * S * 0.2), fill=GOLD + (200,), width=3 * s)
    elif kind in ('supply', 'armory'):
        w = S * 0.98; h = w * 1.18; cy = c + S * 0.06
        d.polygon(shield_pts(c, cy, w, h), fill=(22, 32, 58, 255))
        pts = shield_pts(c, cy, w, h); d.line(pts + [pts[0]], fill=GOLD + (255,), width=6 * s)
        pts = shield_pts(c, cy, w * 0.82, h * 0.82); d.line(pts + [pts[0]], fill=GOLD + (120,), width=2 * s)
        col = IVORY + (255,)
        if kind == 'supply':      # 帐篷
            d.polygon([(c, cy - h * 0.26), (c + w * 0.27, cy + h * 0.14), (c - w * 0.27, cy + h * 0.14)], fill=col)
            d.polygon([(c, cy - h * 0.06), (c + w * 0.07, cy + h * 0.14), (c - w * 0.07, cy + h * 0.14)], fill=(22, 32, 58, 255))
            d.line((c, cy - h * 0.26, c, cy - h * 0.38), fill=col, width=3 * s); d.polygon([(c, cy - h * 0.38), (c + w * 0.12, cy - h * 0.34), (c, cy - h * 0.3)], fill=GOLD + (255,))
        else:                     # 交叉锤
            for sg in (1, -1):
                ax, ay = c - w * 0.22 * sg, cy + h * 0.2; bx, by = c + w * 0.16 * sg, cy - h * 0.16
                d.line((ax, ay, bx, by), fill=col, width=5 * s)
                ux, uy = bx - ax, by - ay; L = math.hypot(ux, uy); ux, uy = ux / L, uy / L; px, py = -uy, ux; hl, hw = w * 0.13, w * 0.06
                d.polygon([(bx + px * hl + ux * hw, by + py * hl + uy * hw), (bx - px * hl + ux * hw, by - py * hl + uy * hw), (bx - px * hl - ux * hw, by - py * hl - uy * hw), (bx + px * hl - ux * hw, by + py * hl - uy * hw)], fill=col)
    elif kind == 'ruins':         # 象牙白圆章 + 一根断柱
        r = S * 0.5; cy = c + S * 0.04
        d.ellipse((c - r, cy - r, c + r, cy + r), fill=(232, 222, 198, 255), outline=SEPIA + (255,), width=5 * s)
        d.ellipse((c - r * 0.84, cy - r * 0.84, c + r * 0.84, cy + r * 0.84), outline=SEPIA + (150,), width=2 * s)
        col = SEPIA + (255,); cw_ = r * 0.36
        d.rectangle((c - cw_ * 0.75, cy + r * 0.42, c + cw_ * 0.75, cy + r * 0.52), fill=col)
        d.polygon([(c - cw_ / 2, cy + r * 0.42), (c + cw_ / 2, cy + r * 0.42), (c + cw_ / 2, cy - r * 0.08), (c + cw_ * 0.1, cy - r * 0.2), (c - cw_ * 0.15, cy - r * 0.02), (c - cw_ / 2, cy - r * 0.32)], fill=col)
        for k in (-0.25, 0, 0.25): d.line((c + cw_ * k * 1.4, cy + r * 0.4, c + cw_ * k * 1.4, cy - r * 0.0), fill=(232, 222, 198, 255), width=2 * s)
        d.polygon([(c + r * 0.22, cy + r * 0.52), (c + r * 0.5, cy + r * 0.52), (c + r * 0.44, cy + r * 0.38), (c + r * 0.28, cy + r * 0.36)], fill=col)
    out = down(im, S)
    # 软阴影（纹章贴在纸上）
    a = np.asarray(out, np.float32)[..., 3] / 255
    sh = ndi.gaussian_filter(np.roll(a, (4, 3), (0, 1)), 3) * 0.45
    O = np.asarray(out, np.float32).copy(); A2 = a + sh * (1 - a)
    O[..., :3] = np.where(a[..., None] > 0, O[..., :3], 10); O[..., 3] = A2 * 255
    return Image.fromarray(O.clip(0, 255).astype(np.uint8))


for k in ('battle', 'elite', 'boss', 'supply', 'armory', 'ruins'):
    add('node_' + k, node_symbol(k))


def white(im_l):
    a = np.asarray(im_l, np.float32) / 255
    out = np.zeros(a.shape + (4,), np.uint8); out[..., :3] = 255; out[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(out)


def gold_ring(S, seed):
    """能去的节点外面一圈：两道细圈 + 一圈刻度 + 四个方位的小菱形（白色，运行时染金）。"""
    im = Image.new('L', (S * 2, S * 2), 0); d = ImageDraw.Draw(im); c = S; r1, r2 = S * 0.86, S * 0.74
    d.ellipse((c - r1, c - r1, c + r1, c + r1), outline=255, width=12); d.ellipse((c - r2, c - r2, c + r2, c + r2), outline=230, width=6)
    for k in range(0, 360, 6 + seed * 2):
        t = math.radians(k + seed * 7); a_, b_ = r2 + 4, r2 + (30 if k % 30 == 0 else 18)
        d.line((c + math.cos(t) * a_, c + math.sin(t) * a_, c + math.cos(t) * b_, c + math.sin(t) * b_), fill=240, width=5)
    for k in range(4):
        t = math.radians(k * 90 + 45 * (seed % 2)); x, y = c + math.cos(t) * r1, c + math.sin(t) * r1
        d.polygon([(x, y - 26), (x + 16, y), (x, y + 26), (x - 16, y)], fill=255)
    return white(im.resize((S, S), Image.LANCZOS))


for i in range(3): add(f'ring_{i}', gold_ring(240, i))


def ink_strokes(S, segs, lw):
    im = Image.new('L', (S * 2, S * 2), 0); d = ImageDraw.Draw(im)
    for seg in segs: d.line([(x * 2, y * 2) for x, y in seg], fill=255, width=lw * 2, joint='curve')
    return white(im.resize((S, S), Image.LANCZOS))


add('cross', ink_strokes(200, [[(40, 40), (160, 160)], [(46, 160), (160, 46)], [(52, 34), (166, 148)]], 7))     # 打过的：墨笔划掉（叉 + 一道细的平行线）
add('check', ink_strokes(200, [[(36, 108), (82, 152), (170, 46)]], 9))
im = Image.new('L', (240, 240), 0); ImageDraw.Draw(im).polygon([(20, 30), (220, 120), (20, 210), (70, 120)], fill=255); add('arrow', white(im.resize((120, 120), Image.LANCZOS)))

# 我军：藏青三角小旗（真纸、金边、一颗星）插在金杆上
fl = ck.pennant(150, 200, (34, 62, 132), IVORY, pad=10, seed=3, tip=0.24); fd = ImageDraw.Draw(fl); star4(fd, 75, 86, 26, IVORY + (255,))
fl = fl.rotate(90, expand=True, resample=Image.BICUBIC)    # 旗面横过来，旗尖朝右
army = Image.new('RGBA', (300, 300), (0, 0, 0, 0)); ad = ImageDraw.Draw(army)
army.alpha_composite(fl.resize((190, 142), Image.LANCZOS), (64, 26))
ad.line((62, 28, 62, 286), fill=GOLD + (255,), width=7); ad.ellipse((54, 16, 70, 32), fill=GOLD + (255,))
ad.ellipse((40, 276, 84, 292), fill=(10, 12, 20, 110))
add('node_army', army)


# 加减钮：藏青圆章、金边、象牙白符号（编成表在藏青纸上，金边分得出来）
def pm_button(plus):
    S = 120; im = Image.new('RGBA', (S * 2, S * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(im); c = S
    d.ellipse((c - 104, c - 104, c + 104, c + 104), fill=(14, 20, 36, 255), outline=GOLD + (255,), width=10)
    d.ellipse((c - 84, c - 84, c + 84, c + 84), outline=GOLD + (110,), width=3)
    d.rectangle((c - 48, c - 9, c + 48, c + 9), fill=IVORY + (255,))
    if plus: d.rectangle((c - 9, c - 48, c + 9, c + 48), fill=IVORY + (255,))
    return im.resize((S, S), Image.LANCZOS)


add('minus', pm_button(False)); add('plus', pm_button(True))

# ═════════ 数字（Cinzel 粗体：铜版印刷的字，白色，运行时着色） ═════════
GLYPHS = '0123456789+-/×'
for ch in GLYPHS:
    f = font('Cinzel.ttf', 150) if ch.isdigit() else font('Marcellus-Regular.ttf', 150)
    try:
        if ch.isdigit(): f.set_variation_by_axes([700])
    except Exception: pass
    sym = {'-': '−'}.get(ch, ch)
    bb = ImageDraw.Draw(Image.new('L', (1, 1))).textbbox((0, 0), sym, font=f)
    w = bb[2] - bb[0] + 24; h = 170
    im = Image.new('L', (w, h), 0)
    ImageDraw.Draw(im).text((12 - bb[0], 140), sym, font=f, fill=255, anchor='ls')
    add('d_' + {'+': 'plus', '-': 'minus', '/': 'slash', '×': 'times'}.get(ch, ch), white(im))

# ═════════ 兵种纹章盾（彩色：藏青底、金边、象牙白图案） ═════════
for tid in TROOPS:
    S = 200; im = Image.new('RGBA', (S * 2, S * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    heraldic(d, S, S + 10, S * 1.3, tid, (22, 32, 58, 255), GOLD + (255,), IVORY + (255,))
    add('troop_' + tid, im.resize((S, S), Image.LANCZOS))


# ═════════ 纹章徽章（彩色：藏青圆章 + 金边 + 象牙白图案） ═════════
def badge(kind, S=300):
    ss = 2; im = Image.new('RGBA', (S * ss, S * ss), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = S * ss / 2; R = S * ss * 0.46
    d.ellipse((c - R, c - R, c + R, c + R), fill=NAVY + (255,))
    d.ellipse((c - R, c - R, c + R, c + R), outline=GOLD + (255,), width=int(S * ss * 0.03))
    d.ellipse((c - R * 0.86, c - R * 0.86, c + R * 0.86, c + R * 0.86), outline=GOLD + (200,), width=int(S * ss * 0.008))
    s = S * ss; col = IVORY + (255,)
    def shield(cx, cy, w, h, fill=col, outline=None):
        pts = [(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h * 0.05)]
        pts += [(cx + w / 2 * math.cos(t) , cy + h * 0.05 + h * 0.45 * math.sin(t)) for t in np.linspace(0, math.pi, 20)]
        pts += [(cx - w / 2, cy + h * 0.05)]
        d.polygon(pts, fill=fill, outline=outline)
    if kind == 'bulwark':      # 坚甲：一面大盾 + 中线
        shield(c, c, s * 0.36, s * 0.42); d.line((c, c - s * 0.21, c, c + s * 0.2), fill=NAVY, width=int(s * 0.02))
    elif kind == 'shieldwall': # 盾墙：三面并排的盾
        for dx in (-0.16, 0, 0.16): shield(c + dx * s, c + abs(dx) * s * 0.15, s * 0.17, s * 0.24, outline=NAVY)
    elif kind == 'ironhorse':  # 铁骑：马蹄铁 + 钉孔
        d.arc((c - s * 0.2, c - s * 0.22, c + s * 0.2, c + s * 0.2), 160, 380, fill=col, width=int(s * 0.07))
        for a in (190, 230, 270, 310, 350): ra = math.radians(a); d.ellipse((c + math.cos(ra) * s * 0.165 - 5, c + math.sin(ra) * s * 0.165 - 5, c + math.cos(ra) * s * 0.165 + 5, c + math.sin(ra) * s * 0.165 + 5), fill=NAVY)
    elif kind == 'quiver':     # 满囊：三支交叉的轨枪弹 / 箭
        for a in (-25, 0, 25):
            ra = math.radians(a - 90); x0, y0 = c - math.cos(ra) * s * 0.22, c - math.sin(ra) * s * 0.22; x1, y1 = c + math.cos(ra) * s * 0.22, c + math.sin(ra) * s * 0.22
            d.line((x0, y0, x1, y1), fill=col, width=int(s * 0.03))
            px, py = -math.sin(ra), math.cos(ra)
            d.polygon([(x1 + math.cos(ra) * s * 0.06, y1 + math.sin(ra) * s * 0.06), (x1 + px * s * 0.035, y1 + py * s * 0.035), (x1 - px * s * 0.035, y1 - py * s * 0.035)], fill=col)
    elif kind == 'resolve':    # 军心：一团三股的火焰
        def flame(cx, by, w, h, k):
            pts = [(cx - w, by), (cx - w * 1.05, by - h * 0.35), (cx - w * 0.55, by - h * 0.62), (cx - w * 0.6, by - h * 0.42 * k),
                   (cx - w * 0.15, by - h * 0.78), (cx, by - h), (cx + w * 0.2, by - h * 0.7), (cx + w * 0.55, by - h * 0.86 * k),
                   (cx + w * 0.75, by - h * 0.5), (cx + w * 1.05, by - h * 0.3), (cx + w, by)]
            pts += [(cx + w * math.cos(t), by + w * 0.55 * math.sin(t)) for t in np.linspace(0, math.pi, 12)]
            return pts
        d.polygon(flame(c, c + s * 0.12, s * 0.16, s * 0.36, 1.0), fill=col)
        d.polygon(flame(c, c + s * 0.15, s * 0.08, s * 0.19, 0.9), fill=GOLD + (255,))
    elif kind == 'veterans':   # 老兵：五角星 + 底下两道横杠
        pts = [(c + s * (0.17 if k % 2 == 0 else 0.07) * math.sin(math.radians(k * 36)), c - s * 0.05 - s * (0.17 if k % 2 == 0 else 0.07) * math.cos(math.radians(k * 36))) for k in range(10)]
        d.polygon(pts, fill=col)
        for k in range(2): d.rectangle((c - s * 0.17, c + s * 0.16 + k * s * 0.065, c + s * 0.17, c + s * 0.195 + k * s * 0.065), fill=col)
    elif kind == 'medics':     # 医官：蓝底上的白十字放在小圆里（不用红十字标志）
        d.ellipse((c - s * 0.2, c - s * 0.2, c + s * 0.2, c + s * 0.2), fill=col)
        d.rectangle((c - s * 0.045, c - s * 0.14, c + s * 0.045, c + s * 0.14), fill=NAVY); d.rectangle((c - s * 0.14, c - s * 0.045, c + s * 0.14, c + s * 0.045), fill=NAVY)
    elif kind == 'promote':    # 晋升：两道金色 V
        for k in range(2):
            y = c - s * 0.08 + k * s * 0.13
            d.line((c - s * 0.22, y - s * 0.04, c, y + s * 0.1, c + s * 0.22, y - s * 0.04), fill=GOLD + (255,), width=int(s * 0.06), joint='curve')
    elif kind == 'crystals':   # 星晶：一簇晶体
        for dx, hh, ww in ((-0.1, 0.26, 0.08), (0.0, 0.36, 0.1), (0.11, 0.22, 0.07)):
            x = c + dx * s; yb = c + s * 0.18
            d.polygon([(x, yb - hh * s), (x + ww * s, yb - hh * s * 0.7), (x + ww * s * 0.8, yb), (x - ww * s * 0.8, yb), (x - ww * s, yb - hh * s * 0.7)], fill=(150, 214, 220, 255), outline=IVORY + (255,))
            d.line((x, yb - hh * s, x, yb), fill=(90, 160, 176, 255), width=3)
    elif kind == 'heal':       # 治疗：一卷绷带
        d.rounded_rectangle((c - s * 0.2, c - s * 0.1, c + s * 0.2, c + s * 0.1), radius=int(s * 0.1), fill=col)
        for k in range(-2, 3): d.line((c + k * s * 0.05, c - s * 0.1, c + k * s * 0.05, c + s * 0.1), fill=NAVY, width=3)
    elif kind == 'cap':        # 扩编：一面队旗 + 加号
        d.line((c - s * 0.15, c - s * 0.22, c - s * 0.15, c + s * 0.22), fill=col, width=int(s * 0.025))
        d.polygon([(c - s * 0.15, c - s * 0.22), (c + s * 0.15, c - s * 0.15), (c - s * 0.15, c - s * 0.06)], fill=col)
        d.rectangle((c + s * 0.06, c + s * 0.02, c + s * 0.1, c + s * 0.2), fill=GOLD); d.rectangle((c - s * 0.01, c + s * 0.09, c + s * 0.17, c + s * 0.13), fill=GOLD)
    elif kind == 'recruit':    # 招募：一面小纹章盾 + 加号
        pts = shield_pts(c - s * 0.04, c - s * 0.02, s * 0.3, s * 0.36); d.polygon(pts, fill=col)
        d.rectangle((c + s * 0.14, c + s * 0.06, c + s * 0.18, c + s * 0.24), fill=GOLD); d.rectangle((c + s * 0.07, c + s * 0.13, c + s * 0.25, c + s * 0.17), fill=GOLD)
    elif kind == 'depart':     # 离开：前进箭头
        d.polygon([(c - s * 0.2, c - s * 0.05), (c + s * 0.04, c - s * 0.05), (c + s * 0.04, c - s * 0.15), (c + s * 0.22, c), (c + s * 0.04, c + s * 0.15), (c + s * 0.04, c + s * 0.05), (c - s * 0.2, c + s * 0.05)], fill=col)
    return im.resize((S, S), Image.LANCZOS)


for k in ('blank', 'bulwark', 'ironhorse', 'quiver', 'resolve', 'veterans', 'shieldwall', 'medics', 'promote', 'crystals', 'heal', 'cap', 'recruit', 'depart'):
    add('badge_' + k, badge(k))


# ═════════ 卡片底（11-20 v2：一张撕边羊皮纸“调令”——上沿压一条藏青撕边纸当抬头，金线内框，下面一道齿孔线隔出存根放价格） ═════════
def card(seed, W=520, H=720):
    pad = 14
    rgb, a, (x0, y0, x1, y1) = ck.real_sheet(W, H, IVORY, pad=pad, flip=bool(seed % 2))
    h, w = a.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    # 抬头：藏青纸带，下沿是撕的（露一点纸芯）
    a = np.maximum(a, ((yy < 150) & (xx > 3) & (xx < w - 3)).astype(np.float32))   # 抬头那一截上沿是剪齐的（撕边留在两侧和下沿；上沿撕边染了藏青像毛边地毯）
    a[:2] = 0
    edge = 128 + ck._wobble(w, 4, seed)[None, :]
    band = np.clip(edge - yy, 0, 1)
    navy_c = ck._paper(w, h, (18, 26, 46), seed + 9, grad=0.2)   # 平整的染色纸（拉伸的纸条原件会出竖向拉丝）
    core = np.clip(1.6 - np.abs(edge - yy) * 0.8, 0, 1) * 0.6           # 撕开处的浅色纸芯
    rgb = rgb * (1 - band[..., None]) + navy_c * band[..., None]
    rgb = rgb * (1 - core[..., None]) + np.array([200, 196, 186], np.float32) * core[..., None]
    lay = Image.new('RGBA', (w * 2, h * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
    g = ck.GOLD + (255,); gd = ck.GOLD_D + (220,); sp = (78, 52, 34, 200)
    d.line((60, 36, w * 2 - 60, 36), fill=gd, width=2)                                  # 抬头里上沿一道细金线
    # 内框：金色细线，四角小星
    fx0, fy0, fx1, fy1 = 34, 300, w * 2 - 34, h * 2 - 40
    d.rectangle((fx0, fy0, fx1, fy1), outline=gd, width=2)
    for X, Y in ((fx0, fy0), (fx1, fy0), (fx0, fy1), (fx1, fy1)): star4(d, X, Y, 12, g)
    # 齿孔线：隔出下面的存根（价格）
    py = 1180   # 齿孔线（2 倍坐标）：存根高 ≈ 2 cm，放得下价格（CampaignCards 的版心按这几条线算）
    for x in range(fx0 + 20, fx1 - 10, 22): d.ellipse((x - 4, py - 4, x + 4, py + 4), fill=sp)
    # 角落的小字（极小、浅）：编号
    # （原来右下角有一行小编号：贴在角上，删掉）
    rgb, _ = ck.put(rgb, a, lay)
    rgb = ck.corner_dots(rgb, 9, w, h, 260, 200, (150, 130, 100), 0.35)
    return ck.finish(rgb, a, pad, op=0.55)


add('card', card(3))


# 选中时盖的章：棕红色圆形邮戳（星 + 一圈拉丁字 + 波浪销戳线），残缺的印泥
def approve_stamp():
    S = 2; W_, H_ = 360, 300; im = Image.new('L', (W_ * S, H_ * S), 0); d = ImageDraw.Draw(im); cx, cy, R0 = 150 * S, 150 * S, 128 * S
    d.ellipse((cx - R0, cy - R0, cx + R0, cy + R0), outline=255, width=7 * S)
    d.ellipse((cx - R0 + 14 * S, cy - R0 + 14 * S, cx + R0 - 14 * S, cy + R0 - 14 * S), outline=210, width=2 * S)
    star4(d, cx, cy, 46 * S, 255)
    f = font('Cinzel.ttf', 26 * S); txt = 'APPROBATUM · ORDO BELLI · '
    for i, ch in enumerate(txt):
        an = -90 + i * 360 / len(txt); gg = Image.new('L', (50 * S, 50 * S), 0); ImageDraw.Draw(gg).text((25 * S, 25 * S), ch, font=f, fill=255, anchor='mm')
        gg = gg.rotate(-an - 90, resample=Image.BICUBIC); rr = 96 * S
        im.paste(255, (int(cx + math.cos(math.radians(an)) * rr - 25 * S), int(cy + math.sin(math.radians(an)) * rr - 25 * S)), gg)
    for k in range(3):
        y0 = cy + (k - 1) * 40 * S; d.line([(x, y0 + math.sin((x - cx) / (24.0 * S)) * 8 * S) for x in range(cx + R0 + 8 * S, (W_ - 8) * S, 4)], fill=235, width=4 * S)
    L = np.asarray(im.resize((W_, H_), Image.LANCZOS), np.float32) / 255
    r_ = np.random.default_rng(21); big = np.asarray(Image.fromarray((r_.random((10, 12)) * 255).astype(np.uint8)).resize((W_, H_), Image.BICUBIC), np.float32) / 255
    L = L * np.clip((big - 0.15) / 0.35, 0.2, 1) * (r_.random((H_, W_)) > 0.1)
    out = np.zeros((H_, W_, 4), np.uint8); out[..., :3] = (150, 46, 34); out[..., 3] = (ndi.gaussian_filter(L, 0.6) * 0.9 * 255).astype(np.uint8)
    return Image.fromarray(out).rotate(-14, expand=True, resample=Image.BICUBIC)


add('card_ring', approve_stamp())

# ═════════ 文字标签（白色，运行时着色）。键 = 'L_<名字>_<zh|en>' ═════════
LABELS = {
    'reinforce': ('补员', 'REINFORCE'), 'recruit_new': ('新编兵种', 'NEW UNIT'), 'promote': ('晋升', 'PROMOTION'), 'relic': ('纹章', 'INSIGNIA'), 'crystals': ('星晶', 'CRYSTALS'),
    'heal': ('治疗伤员', 'TREAT WOUNDED'), 'cap': ('扩编', 'EXPAND COMMAND'), 'recruit': ('招募', 'RECRUIT'), 'depart': ('出发', 'MOVE OUT'),
    'supply': ('补给站', 'SUPPLY DEPOT'), 'armory': ('军械库', 'ARMORY'), 'choose': ('选一项', 'CHOOSE ONE'),
    'victory': ('战役胜利', 'CAMPAIGN WON'), 'defeat': ('全军覆没', 'ARMY LOST'), 'newrun': ('新战役', 'NEW CAMPAIGN'),
    'all_wounded': ('全部伤员', 'ALL WOUNDED'), 'cap_plus': ('统率上限', 'COMMAND CAP'), 'per': ('每人', 'EACH'),
    'tier': ('阶', 'TIER'), 'cost': ('花费', 'COST'), 'free': ('免费', 'FREE'),
    'ruins': ('遗迹', 'RUINS'), 'trade': ('交换', 'TRADE'), 'leave': ('离开', 'LEAVE'), 'pass_by': ('原样离开', 'WALK AWAY'),
    'ore': ('富矿', 'RICH ORE'), 'stragglers': ('收编散兵', 'STRAGGLERS'), 'shrine': ('先人祭坛', 'OLD SHRINE'),
}
RELIC_DESC = {
    'bulwark': (('坚甲', 'BULWARK'), ('步兵血量 +25%', 'Infantry HP +25%')),
    'ironhorse': (('铁骑', 'IRON STEEDS'), ('骑兵血量 +30%', 'Cavalry HP +30%')),
    'quiver': (('满囊', 'FULL QUIVER'), ('弹药 +50%', 'Ammo +50%')),
    'resolve': (('军心', 'RESOLVE'), ('全军士气 +20', 'Army morale +20')),
    'veterans': (('老兵', 'VETERANS'), ('伤员多、阵亡少', 'More wounded, fewer dead')),
    'shieldwall': (('盾墙', 'SHIELD WALL'), ('盾牌耐久 +50%', 'Shields +50%')),
    'medics': (('医官', 'FIELD MEDICS'), ('一半伤员当场归队', 'Half the wounded return')),
}
DILEMMA_DESC = {
    'shrine': ('献出士兵，换一面纹章', 'Give soldiers for an insignia'),
    'ore': ('挖矿得星晶，有人受伤', 'Crystals, but some are hurt'),
    'stragglers': ('收编散兵，花些星晶', 'New troops for crystals'),
}
PROMO_DESC = ('血量 +15%，士气 +5', 'HP +15%, morale +5')


def label(text, zh, size=64, style='title'):
    if zh:
        f = F_ZH(size, heavy=style == 'title') if style != 'hand' else F_HZH(size)
    else:
        f = F_CAPS(size) if style == 'title' else font('Marcellus-Regular.ttf', int(size * 0.95)) if style == 'type' else F_HAND(int(size * 1.2))
    bb = ImageDraw.Draw(Image.new('L', (1, 1))).textbbox((0, 0), text, font=f)
    w, h = bb[2] - bb[0] + 12, bb[3] - bb[1] + 12
    im = Image.new('L', (w, h), 0); ImageDraw.Draw(im).text((6 - bb[0], 6 - bb[1]), text, font=f, fill=255)
    out = np.zeros((h, w, 4), np.uint8); out[..., :3] = 255; out[..., 3] = np.asarray(im)
    return Image.fromarray(out)


for k, (z, e) in LABELS.items():
    add(f'L_{k}_zh', label(z, True)); add(f'L_{k}_en', label(e, False))
for k, ((nz, ne), (dz, de)) in RELIC_DESC.items():
    add(f'L_relic_{k}_zh', label(nz, True)); add(f'L_relic_{k}_en', label(ne, False))
    add(f'D_relic_{k}_zh', label(dz, True, 44, 'type')); add(f'D_relic_{k}_en', label(de, False, 44, 'type'))
for k, (z, e) in DILEMMA_DESC.items(): add(f'D_dilemma_{k}_zh', label(z, True, 44, 'type')); add(f'D_dilemma_{k}_en', label(e, False, 44, 'type'))
add('D_promote_zh', label(PROMO_DESC[0], True, 44, 'type')); add('D_promote_en', label(PROMO_DESC[1], False, 44, 'type'))
for tid in TROOPS:
    z, e = TROOP_NAMES[tid]
    add(f'L_troop_{tid}_zh', label(z, True, 56)); add(f'L_troop_{tid}_en', label(e.upper(), False, 50))

# ═════════ 打包（按高度排的货架式装箱） ═════════
names = sorted(ITEMS, key=lambda n: -ITEMS[n].height)
AW = 4096; pad = 6
x = y = shelf = 0; rects = {}
for n in names:
    im = ITEMS[n]
    if x + im.width + pad > AW: x = 0; y += shelf + pad; shelf = 0
    rects[n] = (x, y, im.width, im.height); x += im.width + pad; shelf = max(shelf, im.height)
AH = 1
while AH < y + shelf: AH *= 2
atlas = Image.new('RGBA', (AW, AH), (255, 255, 255, 0))
for n, (x, y, w, h) in rects.items(): atlas.alpha_composite(ITEMS[n], (x, y))
os.makedirs(OUT, exist_ok=True)
atlas.save(OUT + 'atlas.png')
ks = sorted(rects)
json.dump({'w': AW, 'h': AH, 'names': ks, 'rects': [v for n in ks for v in rects[n]]}, open(OUT + 'atlas.json', 'w'))   # JsonUtility 能直接读的扁平格式
print('图集', AW, AH, len(rects), '项，用到高度', y + shelf)

# ═════════ 航线条带（11-20 v2）：虚线 = 一串墨点（旧地图的行军路线），实线 = 干净的墨线；横向平铺，透明底白色 ═════════
for nm, dash in (('route_dash', True), ('route_solid', False)):
    im = Image.new('L', (512, 64), 0); d = ImageDraw.Draw(im)
    if dash:
        for cx in (64, 192, 320, 448): d.ellipse((cx - 22, 10, cx + 22, 54), fill=255)
    else: d.rectangle((0, 14, 512, 50), fill=255)
    white(im.resize((256, 32), Image.LANCZOS)).save(OUT + nm + '.png')
print('航线贴图完成')
