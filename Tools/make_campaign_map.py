"""战役作战地图底图 v2（11-20 用户：地图风格好，但要和游戏统一——全游戏都是西式奇幻；相关的界面面板也要讲究）。
第一版是现代军用地形图（打字机字、北约军标、红色“机密”章、油性笔）→ 改成和结算板 / 封面同一套“分层拼贴”：
  最底层：藏青纸（整张），印极淡的金线星盘（同心弧、经线、小星）和角上的半调网点——四周露出来的边就是它；
  作战图：一张撕边羊皮纸（真纸原件 src_paper），图上用棕褐墨印铜版画式的地形——等高线 + 斜排线刻的山体阴影（越陡越密）、
          陨坑的晕滃线、熔流（朱褐细波纹）、晶簇（点刻）、旧路、废墟、矿井，地名用 Cinzel / 思源宋体；
  图名：一条藏青撕边纸条压在图的左上角（金字），下面再斜塞一条象牙白小纸条（花体拉丁文 + 一段蓝色）；
  罗盘：右下角一枚藏青圆纸片 + 金线日轮（和结算板“胜”字背后同一张铜版画），标北；
  图例：右上角一张藏青小纸片，金色符号 + 象牙白小字；
  邮戳：棕红色圆形测绘局邮戳 + 波浪销戳线（不是红色“机密”章）；
  下边：兵力编成表 = 一张藏青账册纸（金线分格，兵种是纹章盾牌），右边压一张象牙白小纸（统率 / 星晶 / 纹章）。
运行时叠加（不在这张图里）：节点纹章、航线、我军小旗、数字、加减钮。版面坐标和第一版一致（CampaignMap.cs 里的常量不用改）。
用法：python3 Tools/make_campaign_map.py [seed]"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(__file__))
import collage_kit as ck

R = ck.R
OUT = R + 'Assets/_WarTable/Resources/Campaign/'
FONTS = R + 'ArtSource/Fonts/'
W, H = 3072, 2560
SEED = int(sys.argv[1]) if len(sys.argv) > 1 else 7

# ── 版面（像素，y 向下 = 往玩家这边）
FX0, FY0, FX1, FY1 = 110, 300, 2962, 1990        # 图廓（地表）
OOB = (110, 2030, 2290, 2500)                     # 兵力编成表
SIDE = (2320, 2030, 2962, 2500)                   # 统率 / 星晶 / 纹章

NAVY, GOLD, GOLD_D, IVORY, INK = ck.NAVY, ck.GOLD, ck.GOLD_D, ck.IVORY, ck.INK
SEPIA = (78, 52, 34); SEPIA_L = (128, 92, 60); VERM = (156, 58, 38); VERM_L = (196, 120, 88); TEAL = (66, 104, 108)
BLUE = (44, 86, 156); POST = (128, 52, 40)
LINE = SEPIA   # 兼容旧名

TROOPS = ['kd_guard', 'kd_footman', 'kd_spearman', 'kd_halberdier', 'kd_swordsman', 'kd_hammer', 'kd_archer', 'kd_crossbow', 'kd_knight']
TROOP_NAMES = {
    'kd_guard': ('近卫甲士', 'Vanguard'), 'kd_footman': ('突击兵', 'Troopers'), 'kd_spearman': ('枪盾兵', 'Pike Guard'),
    'kd_halberdier': ('戟甲兵', 'Halberds'), 'kd_swordsman': ('巨刃甲', 'Greatblades'), 'kd_hammer': ('破阵锤', 'Breakers'),
    'kd_archer': ('轨枪手', 'Railgunners'), 'kd_crossbow': ('钉弩手', 'Bolters'), 'kd_knight': ('星骑', 'Striders'),
}


def font(name, size, wght=None):
    f = ImageFont.truetype(FONTS + name, size)
    if wght:
        try: f.set_variation_by_axes([wght])
        except Exception: pass
    return f


def F_ZH(size, heavy=False): return font('SourceHanSerifCN-Heavy.otf' if heavy else 'SourceHanSerifCN-Bold.otf', size)
def F_CAPS(size, wght=600): return font('Cinzel.ttf', size, wght)
def F_TEXT(size): return font('Marcellus-Regular.ttf', size)
def F_SCRIPT(size): return font('PinyonScript-Regular.ttf', size)
F_TYPE = lambda size, bold=False: F_TEXT(size)   # 第一版的打字机字体全部换成 Marcellus


def smooth_noise(shape, sigma, rng):
    n = ndi.gaussian_filter(rng.standard_normal(shape).astype(np.float32), sigma, mode='wrap')
    return n / (n.std() + 1e-6)


def heightfield(w, h, rng):
    """单位：米。底子是几层平滑噪声，再挖陨坑、拉一道山脊、给熔流刻一条谷。返回 (高程, 陨坑列表, 熔流折线)。"""
    s = 2   # 半分辨率算，最后放大（省时间，等高线本来就要平滑）
    hw, hh = w // s, h // s
    z = 300 * smooth_noise((hh, hw), 160 / s, rng) + 140 * smooth_noise((hh, hw), 70 / s, rng) + 40 * smooth_noise((hh, hw), 26 / s, rng)
    yy, xx = np.mgrid[0:hh, 0:hw].astype(np.float32) * s
    # 一道斜贯的山脊（赤脊）
    ax, ay, bx, by = w * 0.62, h * 0.02, w * 0.98, h * 0.7
    t = np.clip(((xx - ax) * (bx - ax) + (yy - ay) * (by - ay)) / ((bx - ax) ** 2 + (by - ay) ** 2), 0, 1)
    d = np.hypot(xx - (ax + t * (bx - ax)), yy - (ay + t * (by - ay)))
    z += 420 * np.exp(-(d / 150) ** 2) * (0.6 + 0.4 * np.sin(t * 9))
    # 陨坑：碗 + 环形坑唇
    craters = []
    for _ in range(11):
        R0 = float(rng.uniform(55, 230)); cx = float(rng.uniform(R0, w - R0)); cy = float(rng.uniform(R0, h - R0))
        if any(math.hypot(cx - c[0], cy - c[1]) < (R0 + c[2]) * 0.9 for c in craters): continue
        craters.append((cx, cy, R0))
        r = np.hypot(xx - cx, yy - cy) / R0
        depth = R0 * 1.4
        z += np.where(r < 1, -depth * (1 - r ** 2), 0) + depth * 0.45 * np.exp(-((r - 1) / 0.22) ** 2)
    # 熔流：从远处一个陨坑附近蜿蜒流向近处左侧
    pts = [(w * 0.30, -20), (w * 0.27, h * 0.18), (w * 0.34, h * 0.35), (w * 0.24, h * 0.52), (w * 0.17, h * 0.70), (w * 0.08, h * 0.86), (-20, h * 0.98)]
    lava = catmull(pts, 26)
    dl = dist_to_poly(xx, yy, lava)
    z -= 120 * np.exp(-(dl / 70) ** 2)
    z = fit(ndi.zoom(z, s, order=3), h, w)
    z = 2400 + z
    return z.astype(np.float32), craters, lava


def fit(a, h, w):
    """放大后的数组裁 / 补到 h×w（边缘复制）。"""
    a = a[:h, :w]
    return np.pad(a, ((0, h - a.shape[0]), (0, w - a.shape[1])), mode='edge')


def catmull(pts, n):
    pts = [pts[0]] + list(pts) + [pts[-1]]; out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = map(np.array, (pts[i - 1], pts[i], pts[i + 1], pts[i + 2]))
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(np.array(pts[-2]))
    return np.array(out, np.float32)


def dist_to_poly(xx, yy, poly):
    d = np.full(xx.shape, 1e9, np.float32)
    for (ax, ay), (bx, by) in zip(poly[:-1], poly[1:]):
        vx, vy = bx - ax, by - ay; L = vx * vx + vy * vy + 1e-6
        t = np.clip(((xx - ax) * vx + (yy - ay) * vy) / L, 0, 1)
        d = np.minimum(d, np.hypot(xx - ax - t * vx, yy - ay - t * vy))
    return d


def iso_lines(z, interval, width):
    """等高线的覆盖度（抗锯齿、线宽恒定）：离最近一条等值线的距离 / 坡度 = 像素距离。"""
    gy, gx = np.gradient(z)
    g = np.hypot(gx, gy) + 1e-4
    q = z / interval
    d = np.abs(q - np.round(q)) * interval / g
    return np.clip(width * 0.5 - d + 0.5, 0, 1), np.round(q).astype(np.int32)


# ═══════════════════════════════ 画笔工具 ═══════════════════════════════
class Layer:
    """一层墨：用 RGBA 的 PIL 图画，最后按透明度压到纸上（压印的墨有纸纹）。"""
    def __init__(self):
        self.im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); self.d = ImageDraw.Draw(self.im)


def text_rot(base, xy, s, f, fill, angle=0, anchor='mm', spacing=0):
    """转角度写字（angle 逆时针，度）。xy = 中心。"""
    if spacing:
        s = (' ' * 0).join(s)
    tmp = Image.new('RGBA', (1, 1)); dd = ImageDraw.Draw(tmp)
    bb = dd.textbbox((0, 0), s, font=f)
    tw, th = bb[2] - bb[0] + 20, bb[3] - bb[1] + 20
    t = Image.new('RGBA', (tw, th), (0, 0, 0, 0))
    ImageDraw.Draw(t).text((10 - bb[0], 10 - bb[1]), s, font=f, fill=fill)
    t = t.rotate(angle, resample=Image.BICUBIC, expand=True)
    x, y = xy
    if anchor == 'mm': x -= t.width // 2; y -= t.height // 2
    elif anchor == 'lm': y -= t.height // 2; x -= 10
    base.alpha_composite(t, (int(x), int(y)))


def spaced(s, n=1):
    return (' ' * n).join(list(s))


def text_w(s, f):
    bb = ImageDraw.Draw(Image.new('L', (1, 1))).textbbox((0, 0), s, font=f); return bb[2] - bb[0]


# ═══════════════════════════════ 军标（北约 APP-6 的样子） ═══════════════════════════════
def troop_mark(d, cx, cy, w, h, troop, col, lw):
    """友军框（长方形）+ 兵种符号。"""
    x0, y0, x1, y1 = cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2
    d.rectangle((x0, y0, x1, y1), outline=col, width=lw)
    X = lambda: (d.line((x0, y0, x1, y1), fill=col, width=lw), d.line((x0, y1, x1, y0), fill=col, width=lw))
    if troop in ('kd_footman', 'kd_guard', 'kd_spearman', 'kd_halberdier', 'kd_swordsman', 'kd_hammer'): X()
    if troop == 'kd_guard':      # 近卫：框上方一个实心小三角（指挥）
        d.polygon([(cx - h * 0.18, y0 - 2), (cx + h * 0.18, y0 - 2), (cx, y0 - h * 0.3)], fill=col)
    if troop == 'kd_spearman':   # 枪盾：中间一竖
        d.line((cx, y0, cx, y1), fill=col, width=lw)
    if troop == 'kd_halberdier': # 戟：竖 + 顶上一小横
        d.line((cx, y0, cx, y1), fill=col, width=lw); d.line((cx - w * 0.12, y0 + h * 0.22, cx + w * 0.12, y0 + h * 0.22), fill=col, width=lw)
    if troop == 'kd_swordsman':  # 巨刃：装甲椭圆
        d.ellipse((cx - w * 0.3, cy - h * 0.24, cx + w * 0.3, cy + h * 0.24), outline=col, width=lw)
    if troop == 'kd_hammer':     # 破阵锤：框底一道粗横
        d.rectangle((x0, y1 - h * 0.16, x1, y1), fill=col)
    if troop == 'kd_archer':     # 轨枪：实心圆点（炮兵）
        r = h * 0.17; d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=col)
    if troop == 'kd_crossbow':   # 钉弩：圆点 + 底下一横
        r = h * 0.14; d.ellipse((cx - r, cy - r - h * 0.08, cx + r, cy + r - h * 0.08), fill=col)
        d.line((cx - w * 0.22, cy + h * 0.26, cx + w * 0.22, cy + h * 0.26), fill=col, width=lw)
    if troop == 'kd_knight':     # 星骑：一道斜线（骑兵）
        d.line((x0, y1, x1, y0), fill=col, width=lw)


def mine_mark(d, cx, cy, s, col):
    """矿井（地图通用符号）：交叉的两把锤子——锤柄是细线，锤头是垂直于柄的粗短块。"""
    for sgn in (1, -1):
        ax, ay = cx - s * sgn, cy + s; bx, by = cx + s * 0.75 * sgn, cy - s * 0.75
        d.line((ax, ay, bx, by), fill=col, width=max(3, int(s * 0.16)))
        ux, uy = (bx - ax), (by - ay); L = math.hypot(ux, uy); ux, uy = ux / L, uy / L
        px, py = -uy, ux; hl, hw = s * 0.5, s * 0.2
        q = [(bx + px * hl + ux * hw, by + py * hl + uy * hw), (bx - px * hl + ux * hw, by - py * hl + uy * hw),
             (bx - px * hl - ux * hw, by - py * hl - uy * hw), (bx + px * hl - ux * hw, by + py * hl - uy * hw)]
        d.polygon(q, fill=col)


# ═══════════════════════════════ 主流程 ═══════════════════════════════


def charge(d, cx, cy, w, h, troop, col, lw):
    """兵种的“纹章图案”（来自军标的分类记号，去掉方框）：步兵类一个叉，枪 / 戟加竖线，巨刃一圈，锤一道粗横，远程一个点，骑兵一道斜线。"""
    x0, y0, x1, y1 = cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2
    if troop in ('kd_footman', 'kd_guard', 'kd_spearman', 'kd_halberdier', 'kd_swordsman', 'kd_hammer'):
        d.line((x0, y0, x1, y1), fill=col, width=lw); d.line((x0, y1, x1, y0), fill=col, width=lw)
    if troop == 'kd_guard': star = [(cx + math.cos(math.radians(-90 + k * 36)) * (h * (0.2 if k % 2 == 0 else 0.08)), y0 - h * 0.3 + math.sin(math.radians(-90 + k * 36)) * (h * (0.2 if k % 2 == 0 else 0.08))) for k in range(10)]; d.polygon(star, fill=col)
    if troop == 'kd_spearman': d.line((cx, y0 - h * 0.15, cx, y1 + h * 0.15), fill=col, width=lw)
    if troop == 'kd_halberdier': d.line((cx, y0 - h * 0.15, cx, y1 + h * 0.15), fill=col, width=lw); d.line((cx - w * 0.16, y0 + h * 0.05, cx + w * 0.16, y0 + h * 0.05), fill=col, width=lw)
    if troop == 'kd_swordsman': d.ellipse((cx - w * 0.3, cy - h * 0.26, cx + w * 0.3, cy + h * 0.26), outline=col, width=lw)
    if troop == 'kd_hammer': d.rectangle((x0, y1 + h * 0.08, x1, y1 + h * 0.26), fill=col)
    if troop == 'kd_archer': r = h * 0.22; d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=col)
    if troop == 'kd_crossbow': r = h * 0.18; d.ellipse((cx - r, cy - r - h * 0.1, cx + r, cy + r - h * 0.1), fill=col); d.line((cx - w * 0.3, cy + h * 0.32, cx + w * 0.3, cy + h * 0.32), fill=col, width=lw)
    if troop == 'kd_knight': d.line((x0, y1, x1, y0), fill=col, width=lw)


def shield_pts(cx, cy, w, h):
    """纹章盾（上平、两侧直、下面收成尖）。"""
    pts = [(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h * 0.02)]
    for t in np.linspace(0, 1, 14)[1:]:
        pts.append((cx + w / 2 * (1 - t) ** 1.1 * math.cos(t * math.pi / 2) ** 0.4, cy + h * 0.02 + (h * 0.48) * math.sin(t * math.pi / 2)))
    pts2 = [(2 * cx - x, y) for x, y in reversed(pts[2:])]
    return pts + pts2[1:]


def heraldic(d, cx, cy, w, troop, field, rim, ink, S=1):
    """一面兵种纹章盾：底色 field、金边 rim、图案 ink。"""
    h = w * 1.18
    d.polygon(shield_pts(cx, cy, w, h), fill=field)
    d.line(shield_pts(cx, cy, w, h) + [shield_pts(cx, cy, w, h)[0]], fill=rim, width=max(2, int(w * 0.05)))
    d.line(shield_pts(cx, cy, w * 0.8, h * 0.8) + [shield_pts(cx, cy, w * 0.8, h * 0.8)[0]], fill=rim[:3] + (110,), width=max(1, int(w * 0.015)))
    charge(d, cx, cy - h * 0.06, w * 0.46, h * 0.34, troop, ink, max(2, int(w * 0.06)))


def tracked(d, x, y, text, f, fill, gap, anchor='l'):
    ws = [d.textlength(ch, font=f) for ch in text]; total = sum(ws) + gap * (len(text) - 1)
    if anchor == 'r': x -= total
    if anchor == 'm': x -= total / 2
    for ch, w in zip(text, ws):
        d.text((x, y), ch, font=f, fill=fill, anchor='ls'); x += w + gap
    return total


def star4(d, cx, cy, r, fill):
    k = r * 0.22
    d.polygon([(cx, cy - r), (cx + k, cy - k), (cx + r, cy), (cx + k, cy + k), (cx, cy + r), (cx - k, cy + k), (cx - r, cy), (cx - k, cy - k)], fill=fill)


def navy_paper(w, h, seed, lift=0.0):
    """一大张藏青纸：真纸原件的折痕明暗 + 上深下浅 + 颗粒。lift 让它比底下那层略亮一点（叠层分得开）。"""
    src, _ = ck._cut('src_paper.png', maxdim=2600, flip=bool(seed % 2))
    im = Image.fromarray(src.clip(0, 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)
    rgb = np.asarray(im, np.float32); lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32) / 255
    loc = np.clip(lum / (ndi.gaussian_filter(lum, 40) + 1e-3), 0.84, 1.16) ** 2.2
    t = (np.arange(h, dtype=np.float32) / h)[:, None, None]
    top, bot = np.array([11, 15, 26], np.float32), np.array([24, 34, 57], np.float32)
    out = (top * (1 - t) + bot * t) * (1 + lift) * loc[..., None]
    return out + np.random.default_rng(seed).normal(0, 2.4, out.shape[:2] + (1,)).astype(np.float32)


def ink_layer():
    return Image.new('RGBA', (W, H), (0, 0, 0, 0))


def compose(base, lay, grain_seed=0, strength=1.0):
    """把一层墨（RGBA，和画布同大）压到 base（H×W×3 浮点）上：墨量随纸纹起伏一点（印刷的样子）。"""
    L = np.asarray(lay, np.float32); la = L[..., 3:] / 255 * strength
    if grain_seed:
        g = ck.grain(W, H, grain_seed)[..., None]; la = la * np.clip(0.86 + (g - 1) * 1.3, 0.62, 1.0)
    return base * (1 - la) + L[..., :3] * la


def build(lang):
    zh = lang == 'zh'
    rng = np.random.default_rng(SEED)
    fw, fh = FX1 - FX0, FY1 - FY0

    # ══════════ 0. 最底层：藏青纸 + 极淡的金线星盘
    base = navy_paper(W, H, SEED)
    lay = ink_layer(); d = ImageDraw.Draw(lay)
    ccx, ccy = W * 0.86, H * 1.02
    for k, r in enumerate(range(420, 3600, 170)):
        d.ellipse((ccx - r, ccy - r, ccx + r, ccy + r), outline=GOLD_D + (34 if k % 3 else 60,), width=2 if k % 3 == 0 else 1)
    for a in range(180, 361, 6):
        ra = math.radians(a); d.line((ccx + math.cos(ra) * 420, ccy + math.sin(ra) * 420, ccx + math.cos(ra) * 3600, ccy + math.sin(ra) * 3600), fill=GOLD_D + (22,), width=1)
    for _ in range(70):
        x, y = rng.uniform(0, W), rng.uniform(0, H); star4(d, x, y, rng.uniform(4, 11), GOLD_D + (90,))
    base = compose(base, lay)
    base = ck.corner_dots(base, 13, 0, H, 900, 700, (30, 44, 74), 0.7)

    # ══════════ 1. 作战图：撕边羊皮纸
    SX0, SY0, SX1, SY1 = 26, 22, W - 26, 2022
    sw, sh = SX1 - SX0, SY1 - SY0
    srgb, sa, _ = ck.real_sheet(sw, sh, IVORY, pad=0, flip=True)
    yy, xx = np.mgrid[0:sh, 0:sw].astype(np.float32)
    inner = ((xx > FX0 - SX0 - 70) & (xx < FX1 - SX0 + 70) & (yy > 130) & (yy < sh - 26)).astype(np.float32)   # 撕边只在最外一圈，不吃进图廓和格网数字
    sa = np.maximum(sa, ndi.gaussian_filter(inner, 3))
    srgb = srgb * 0.97
    # 地形（图廓内）
    z, craters, lava = heightfield(fw, fh, rng)
    lyy, lxx = np.mgrid[0:fh, 0:fw].astype(np.float32)
    fld = np.zeros((fh, fw, 4), np.float32)

    def stamp(cov, rgb, a=1.0):
        k = np.clip(cov * a, 0, 1)[..., None]
        na = fld[..., 3:] + (1 - fld[..., 3:]) * k
        fld[..., :3] = (np.array(rgb, np.float32) * k + fld[..., :3] * fld[..., 3:] * (1 - k)) / np.maximum(na, 1e-6)
        fld[..., 3:] = na

    dl = dist_to_poly(lxx, lyy, lava)
    # 山体阴影：斜排线刻（铜版画）。光从左上来，背光坡按暗度加粗排线，最暗处再加一组交叉线
    gy, gx = np.gradient(ndi.gaussian_filter(z, 3))
    nx, ny, nz = -gx / 6.0, -gy / 6.0, np.ones_like(z)
    nl = np.sqrt(nx * nx + ny * ny + nz * nz); lx, ly, lz = -0.55, -0.55, 0.63
    lam = (nx * lx + ny * ly + nz * lz) / nl
    dark = np.clip((0.74 - lam) / 0.5, 0, 1) ** 1.3 * np.clip((dl - 30) / 10, 0, 1)   # 只有背光的陡坡才排线（第一版整张发闷）
    u = (lxx * math.cos(math.radians(32)) + lyy * math.sin(math.radians(32))) % 11.0
    stamp(np.clip(dark * 2.2 / 2 - np.abs(u - 5.5) + 0.5, 0, 1), SEPIA_L, 0.38)
    u2 = (lxx * math.cos(math.radians(-38)) + lyy * math.sin(math.radians(-38))) % 11.0
    stamp(np.clip((dark - 0.7) * 5 * 1.5 / 2 - np.abs(u2 - 5.5) + 0.5, 0, 1), SEPIA_L, 0.3)
    # 晶簇：点刻 + 点线边界
    cz = smooth_noise((fh // 4, fw // 4), 22, rng); cz = fit(ndi.zoom(cz, 4, order=1), fh, fw)
    cz = cz - 3.0 * np.exp(-(dl / 150) ** 2)
    gy0, gx0 = np.gradient(cz); gz = np.hypot(gx0, gy0) + 1e-5
    crys = np.clip((cz - 1.2) / gz + 0.5, 0, 1)
    stip = (rng.random((fh, fw)) > 0.93).astype(np.float32)
    stamp(np.full((fh, fw), 1.0, np.float32) * crys, (206, 218, 208), 0.38)   # 晶簇区：浅青底 + 整齐的菱形纹 + 点线边界（不用随机点刻：看着像霉斑）
    rim = np.clip(1.4 - np.abs(cz - 1.2) / gz, 0, 1) * ((((lxx + lyy) // 7) % 2) == 0)
    stamp(rim, TEAL, 0.75)
    pat = ((np.abs(((lxx + (lyy // 52) % 2 * 26) % 52) - 26) + np.abs((lyy % 52) - 26)) < 5).astype(np.float32)
    stamp(pat * (crys > 0.5), TEAL, 0.7)
    # 熔流：朱褐的河，里面顺着流向的细波纹，两岸深色细边
    stamp(np.clip(26 - dl, 0, 1), VERM_L, 0.42)
    ripple = (np.sin(dl * 0.9 + np.sin(lxx * 0.02 + lyy * 0.013) * 2) > 0.72) & (dl < 23)
    stamp(ripple.astype(np.float32), VERM, 0.55)
    stamp(np.clip(1.4 - np.abs(dl - 25), 0, 1), VERM, 0.85)
    stamp(np.clip(1.0 - np.abs(dl - 31), 0, 1), VERM, 0.45)
    # 等高线：细线 + 计曲线
    iv = 40.0
    cov, _ = iso_lines(z, iv, 1.3); covI, _ = iso_lines(z, iv * 5, 2.6)
    away = np.clip((dl - 30) / 8, 0, 1)
    stamp(cov * away, SEPIA_L, 0.7); stamp(covI * away, SEPIA, 0.9)
    # 陨坑晕滃线
    fl = Image.fromarray(np.zeros((fh, fw), np.uint8)); fd = ImageDraw.Draw(fl)
    for cx, cy, R0 in craters:
        n = int(R0 * 0.62)
        for i in range(n):
            a = 2 * math.pi * i / n + rng.uniform(-0.02, 0.02); L = R0 * rng.uniform(0.14, 0.28); r0 = R0 * 0.98
            fd.line((cx + r0 * math.cos(a), cy + r0 * math.sin(a), cx + (r0 - L) * math.cos(a), cy + (r0 - L) * math.sin(a)), fill=255, width=2)
    stamp(np.asarray(fl, np.float32) / 255, SEPIA, 0.45)
    fimg = Image.fromarray(np.clip(fld * [1, 1, 1, 255], 0, 255).astype(np.uint8), 'RGBA')

    lay = Image.new('RGBA', (sw, sh), (0, 0, 0, 0)); lay.alpha_composite(fimg, (FX0 - SX0, FY0 - SY0)); d = ImageDraw.Draw(lay)
    ox, oy = FX0 - SX0, FY0 - SY0   # 图廓左上角在纸上的位置

    # 计曲线注记（顺着线写）
    labeled = []
    for _ in range(500):
        x = int(rng.uniform(80, fw - 80)); y = int(rng.uniform(80, fh - 80))
        if covI[y, x] < 0.9 or dl[y, x] < 80: continue
        g = math.hypot(gx[y, x], gy[y, x])
        if g < 0.6 or g > 6: continue
        if any(math.hypot(x - a, y - b) < 280 for a, b in labeled): continue
        labeled.append((x, y))
        ang = (math.degrees(math.atan2(-gx[y, x], gy[y, x])) + 90) % 180 - 90
        val = int(round(z[y, x] / (iv * 5)) * iv * 5); f = F_TEXT(28)
        tw = d.textlength(str(val), font=f)
        blk = Image.fromarray(np.full((34, int(tw) + 16, 4), list(IVORY) + [235], np.uint8), 'RGBA').rotate(-ang, expand=True, resample=Image.BICUBIC)
        lay.alpha_composite(blk, (ox + x - blk.width // 2, oy + y - blk.height // 2))
        text_rot(lay, (ox + x, oy + y), str(val), f, SEPIA + (255,), -ang)
        if len(labeled) >= 12: break
    # 高程点（小三角）
    spots = []
    for _ in range(300):
        x = int(rng.uniform(60, fw - 60)); y = int(rng.uniform(60, fh - 60))
        win = z[max(0, y - 40):y + 40, max(0, x - 40):x + 40]
        if z[y, x] < win.max() - 0.5 or dl[y, x] < 70 or any(math.hypot(x - a, y - b) < 300 for a, b in spots + labeled): continue
        spots.append((x, y)); X, Y = ox + x, oy + y
        d.polygon([(X, Y - 9), (X + 8, Y + 6), (X - 8, Y + 6)], fill=SEPIA + (255,))
        d.text((X + 14, Y + 2), str(int(z[y, x])), font=F_TEXT(26), fill=SEPIA + (255,), anchor='lm')
        if len(spots) >= 8: break
    # 旧路（单线虚线）+ 废墟 + 地名
    ruins = [(fw * 0.12, fh * 0.30), (fw * 0.55, fh * 0.22), (fw * 0.78, fh * 0.55), (fw * 0.45, fh * 0.80), (fw * 0.86, fh * 0.90)]
    for a, b in ((0, 1), (1, 2), (2, 4), (3, 2), (0, 3)):
        p = catmull([ruins[a], ((ruins[a][0] + ruins[b][0]) / 2 + rng.uniform(-90, 90), (ruins[a][1] + ruins[b][1]) / 2 + rng.uniform(-90, 90)), ruins[b]], 30)
        acc = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(p, axis=0).T))])
        for (x0, y0), (x1, y1), s0 in zip(p[:-1], p[1:], acc[:-1]):
            if (s0 // 16) % 3 != 2: d.line((ox + x0, oy + y0, ox + x1, oy + y1), fill=SEPIA + (170,), width=2)
    ruin_names = [('旧哨站', 'Old Outpost'), ('锈钟镇', 'Rustbell'), ('第九泵站', 'Pump Nine'), ('残灯堡', 'Emberhold'), ('静港', 'Still Harbour')]
    for (x, y), nm in zip(ruins, ruin_names):
        for k in range(4):
            qx, qy = rng.uniform(-20, 20), rng.uniform(-16, 16); s = rng.uniform(6, 10)
            d.rectangle((ox + x + qx - s, oy + y + qy - s, ox + x + qx + s, oy + y + qy + s), outline=SEPIA + (255,), width=2)
            d.line((ox + x + qx - s, oy + y + qy + s, ox + x + qx + s, oy + y + qy - s), fill=SEPIA + (255,), width=2)
        if zh: d.text((ox + x + 38, oy + y + 30), nm[0], font=F_ZH(30), fill=SEPIA + (255,), anchor='lm')
        else: tracked(d, ox + x + 38, oy + y + 40, nm[1].upper(), F_CAPS(24, 700), SEPIA + (255,), 3)
    # 矿井（交叉锤）
    mines = []
    for _ in range(200):
        x = rng.uniform(120, fw - 120); y = rng.uniform(120, fh - 120)
        if dl[int(y), int(x)] < 120 or any(math.hypot(x - a, y - b) < 380 for a, b in mines + ruins): continue
        mines.append((x, y))
        if len(mines) >= 4: break
    for i, (x, y) in enumerate(mines):
        mine_mark(d, ox + x, oy + y, 18, SEPIA + (255,))
        if zh: d.text((ox + x + 30, oy + y + 4), f'{i + 3}号矿井', font=F_ZH(26), fill=SEPIA + (255,), anchor='lm')
        else: d.text((ox + x + 30, oy + y + 4), f'Mine {["III", "IV", "V", "VI"][i]}', font=F_TEXT(26), fill=SEPIA + (255,), anchor='lm')
    # 大地名：疏排、半透明
    big = [((0.29, 0.66), ('熔  流', 'MAGMA FLOW'), -52, VERM), ((0.82, 0.28), ('赤  脊', 'THE RED SPINE'), -62, SEPIA), ((0.55, 0.5), ('灰 烬 盆 地', 'ASHEN BASIN'), 0, SEPIA)]
    for (u_, v_), nm, ang, col in big:
        if zh: text_rot(lay, (ox + u_ * fw, oy + v_ * fh), nm[0], F_ZH(50), col + (190,), ang)
        else:
            tmp = Image.new('RGBA', (1400, 120), (0, 0, 0, 0)); tracked(ImageDraw.Draw(tmp), 700, 86, nm[1], F_CAPS(46, 500), col + (190,), 16, 'm')
            tmp = tmp.crop(tmp.getbbox()).rotate(ang, expand=True, resample=Image.BICUBIC); lay.alpha_composite(tmp, (int(ox + u_ * fw - tmp.width / 2), int(oy + v_ * fh - tmp.height / 2)))
    for i, (cx, cy, R0) in enumerate(sorted(craters, key=lambda c: -c[2])[:3]):
        nm = [('乌鸦坑', 'Corvid Crater'), ('双月坑', 'Twinmoon Crater'), ('铁砧坑', 'Anvil Crater')][i]
        d.text((ox + cx, oy + cy), nm[0] if zh else nm[1], font=F_ZH(28) if zh else F_TEXT(28), fill=SEPIA + (230,), anchor='mm')

    # 格网（极淡）+ 上 / 左 / 右的数字
    step = fw / 10
    for i in range(11):
        x = ox + i * step; d.line((x, oy, x, oy + fh), fill=SEPIA + (60,), width=2)
    for j in range(8):
        y = oy + fh - j * step
        if y < oy: break
        d.line((ox, y, ox + fw, y), fill=SEPIA + (60,), width=2)
    for i in range(11):
        d.text((ox + i * step, oy - 22), f'{41 + i}', font=F_CAPS(26, 700), fill=SEPIA + (255,), anchor='mb')
    for j in range(8):
        y = oy + fh - j * step
        if y < oy + 30: break
        d.text((ox - 18, y), f'{72 + j}', font=F_CAPS(26, 700), fill=SEPIA + (255,), anchor='rm')
        d.text((ox + fw + 18, y), f'{72 + j}', font=F_CAPS(26, 700), fill=SEPIA + (255,), anchor='lm')
    # 图廓：粗细两道线 + 四角小星
    d.rectangle((ox, oy, ox + fw, oy + fh), outline=SEPIA + (255,), width=4)
    d.rectangle((ox - 9, oy - 9, ox + fw + 9, oy + fh + 9), outline=SEPIA + (200,), width=1)
    for X, Y in ((ox - 9, oy - 9), (ox + fw + 9, oy - 9), (ox - 9, oy + fh + 9), (ox + fw + 9, oy + fh + 9)):
        star4(d, X, Y, 14, SEPIA + (255,))
    srgb, _ = ck.put(srgb, sa, lay)

    # 测绘局邮戳（棕红、残缺）：压在图的上沿
    st = Image.new('L', (700, 300), 0); sd = ImageDraw.Draw(st); cx, cy, R0 = 150, 150, 128
    sd.ellipse((cx - R0, cy - R0, cx + R0, cy + R0), outline=255, width=6); sd.ellipse((cx - R0 + 12, cy - R0 + 12, cx + R0 - 12, cy + R0 - 12), outline=200, width=2)
    star4(sd, cx, cy, 44, 255)
    txt = 'STAR LANCERS · SURVEY CORPS · '
    for i, ch in enumerate(txt):
        an = -90 + i * 360 / len(txt); gg = Image.new('L', (44, 44), 0); ImageDraw.Draw(gg).text((22, 22), ch, font=F_CAPS(22, 700), fill=255, anchor='mm')
        gg = gg.rotate(-an - 90, resample=Image.BICUBIC); rr = 96
        st.paste(255, (int(cx + math.cos(math.radians(an)) * rr - 22), int(cy + math.sin(math.radians(an)) * rr - 22)), gg)
    for k in range(4):
        y0 = cy + (k - 1.5) * 34; sd.line([(x, y0 + math.sin((x - cx) / 26.0) * 8) for x in range(cx + R0 + 10, 680, 4)], fill=235, width=4)
    L_ = np.asarray(st, np.float32) / 255; big_ = np.asarray(Image.fromarray((rng.random((12, 26)) * 255).astype(np.uint8)).resize((700, 300), Image.BICUBIC), np.float32) / 255
    L_ = L_ * np.clip((big_ - 0.18) / 0.35, 0.12, 1) * (rng.random((300, 700)) > 0.12)
    sti = np.zeros((300, 700, 4), np.uint8); sti[..., :3] = POST; sti[..., 3] = (ndi.gaussian_filter(L_, 0.6) * 0.75 * 255).astype(np.uint8)
    sti = Image.fromarray(sti, 'RGBA').rotate(-7, expand=True, resample=Image.BICUBIC)
    lay2 = Image.new('RGBA', (sw, sh), (0, 0, 0, 0)); lay2.alpha_composite(sti, (1660 - SX0, 46 - SY0))
    srgb, _ = ck.put(srgb, sa, lay2)

    base_img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8)).convert('RGBA')
    sheet = ck.finish(srgb, sa, 30, sh=(8, 14), blur=14, op=0.6)
    base_img.alpha_composite(sheet, (SX0 - 30, SY0 - 30))

    # ══════════ 2. 罗盘：藏青圆纸片 + 金线日轮（图廓内右下角）
    comp = ck.ring_disc(360, rays=0.85, field=0.6)
    cd = ImageDraw.Draw(comp); C = 180
    cd.polygon([(C, C - 112), (C - 13, C), (C, C - 10), (C + 13, C)], fill=IVORY + (255,)); cd.polygon([(C, C + 70), (C - 9, C), (C + 9, C)], fill=GOLD_D + (255,))
    ck.paste_rot(base_img, comp, FX1 - 175, FY1 - 190, 0)
    bd = ImageDraw.Draw(base_img)
    bd.text((FX1 - 175, FY1 - 190 - 196), '北' if zh else 'N', font=F_ZH(40, True) if zh else F_CAPS(44, 800), fill=SEPIA + (255,), anchor='mm')

    # ══════════ 3. 图名：藏青撕边纸条（金字）+ 斜塞的象牙白花体小纸条
    TW_, TH_ = 1420, 196
    trgb, ta, _ = ck.real_strip(TW_, TH_, NAVY, pad=0, cut='l')
    tl = Image.new('RGBA', (TW_, TH_), (0, 0, 0, 0)); td = ImageDraw.Draw(tl)
    # 版心：左边距 72；主标题 + 副标题这一组在纸条里上下居中（纸条下沿是撕的，下边多留 8 px）
    if zh:
        td.text((72, 92), '第一星区 · 灰烬带', font=F_ZH(80, True), fill=GOLD + (255,), anchor='ls')
        sub_w = tracked(td, 76, 148, 'SECTOR I · THE ASHEN BELT', F_CAPS(24, 600), GOLD_D + (255,), 6)
    else:
        tracked(td, 72, 100, 'THE ASHEN BELT', F_CAPS(76, 700), GOLD + (255,), 10)
        sub_w = tracked(td, 76, 148, 'SECTOR I  ·  CHART OF OPERATIONS', F_CAPS(24, 600), GOLD_D + (255,), 6)
    lx = 76 + sub_w + 34; td.line((lx, 140, lx + 220, 140), fill=GOLD_D + (230,), width=2); star4(td, lx + 220, 140, 8, GOLD + (255,))
    trgb, _ = ck.put(trgb, ta, tl)
    ck.paste_rot(base_img, ck.finish(trgb, ta, 24, op=0.55), 60 + TW_ / 2, 40 + TH_ / 2, 0.8)
    SW_, SH_ = 620, 112
    s2, s2a, _ = ck.real_strip(SW_, SH_, IVORY, pad=0, flip=True)
    sl = Image.new('RGBA', (SW_, SH_), (0, 0, 0, 0)); sdd = ImageDraw.Draw(sl)
    sdd.rectangle((0, 0, 34, SH_), fill=BLUE + (255,))
    sdd.text((68, 62), 'Mare Cinerum', font=F_SCRIPT(66), fill=INK + (255,), anchor='lm')
    tracked(sdd, SW_ - 44, 90, 'ANNO CCCXII', F_CAPS(16, 600), INK + (200,), 4, 'r')
    s2, _ = ck.put(s2, s2a, sl)
    ck.paste_rot(base_img, ck.finish(s2, s2a, 16, op=0.5), 1290 if zh else 1400, 228, -2.5)
    # 比例尺（印在图纸上沿右边）
    sx, sy = FX1 - 760, FY1 - 78
    for i in range(5): bd.rectangle((sx + i * 64, sy, sx + (i + 1) * 64, sy + 10), fill=SEPIA if i % 2 == 0 else IVORY, outline=SEPIA, width=2)
    for i in (0, 5): bd.text((sx + i * 64, sy - 6), str(i), font=F_TEXT(22), fill=SEPIA, anchor='mb')
    bd.text((sx + 5 * 64 + 16, sy + 5), '千米' if zh else 'leagues', font=F_ZH(22) if zh else F_TEXT(24), fill=SEPIA, anchor='lm')
    bd.text((sx, sy + 34), '比例尺 1 : 50 000  ·  等高距 40 米' if zh else 'Scale 1 : 50 000  ·  contours every 40 m', font=F_ZH(20) if zh else F_TEXT(22), fill=SEPIA, anchor='lm')

    # ══════════ 4. 图例：藏青小纸片，金色符号 + 象牙白小字（右上角压着图纸）
    LW_, LH_ = 760, 236
    lrgb, la_, _ = ck.real_sheet(LW_, LH_, NAVY, pad=0)
    lrgb = np.asarray(Image.fromarray(np.clip(navy_paper(LW_, LH_, 3, 0.25), 0, 255).astype(np.uint8)), np.float32)
    ll = Image.new('RGBA', (LW_, LH_), (0, 0, 0, 0)); ld = ImageDraw.Draw(ll)
    M = 48; colw = (LW_ - 2 * M) / 4                                                                 # 版心：左右各 48，四列等分
    hw_ = tracked(ld, M, 66, '图例' if zh else 'LEGEND', F_ZH(26) if zh else F_CAPS(22, 700), GOLD + (255,), 8 if zh else 6)
    ld.line((M + hw_ + 18, 57, LW_ - M, 57), fill=GOLD_D + (200,), width=1)
    items = [('contour', '等高线', 'Contour'), ('crater', '陨坑', 'Crater'), ('lava', '熔流', 'Magma'), ('crystal', '晶簇', 'Crystal'),
             ('mine', '矿井', 'Mine'), ('ruin', '废墟', 'Ruins'), ('track', '旧路', 'Track'), ('spot', '高程', 'Height')]
    # 两端对齐：每列宽 = 符号 58 + 这一列最长的字；列与列之间的空隙相等，最后一列的字收在右边线上
    lf = F_ZH(22) if zh else F_TEXT(23)
    cwid = [58 + max(ld.textlength(items[c][1] if zh else items[c][2], font=lf), ld.textlength(items[c + 4][1] if zh else items[c + 4][2], font=lf)) for c in range(4)]
    cgap = (LW_ - 2 * M - sum(cwid)) / 3; cxs = [M + sum(cwid[:c]) + cgap * c for c in range(4)]
    for i, (k_, nz, ne) in enumerate(items):
        cx = cxs[i % 4]; cy = 116 + (i // 4) * 66; g = GOLD + (255,)
        if k_ == 'contour':
            for t in range(3): ld.arc((cx - 8 + t * 9, cy - 18 + t * 5, cx + 52 - t * 9, cy + 26 - t * 5), 200, 340, fill=g, width=2 if t == 1 else 1)
        elif k_ == 'crater':
            ld.ellipse((cx + 6, cy - 16, cx + 42, cy + 16), outline=g, width=2)
            for a in range(0, 360, 30): ra = math.radians(a); ld.line((cx + 24 + 18 * math.cos(ra), cy + 16 * math.sin(ra), cx + 24 + 12 * math.cos(ra), cy + 10 * math.sin(ra)), fill=g, width=1)
        elif k_ == 'lava':
            ld.rectangle((cx, cy - 7, cx + 48, cy + 7), fill=(150, 76, 52, 255), outline=g, width=1)
        elif k_ == 'crystal':
            for a_ in range(3):
                for b_ in range(2): ck.diamond(ld, cx + 8 + a_ * 16, cy - 6 + b_ * 13, 4, (130, 184, 186, 255))
        elif k_ == 'mine': mine_mark(ld, cx + 24, cy, 13, g)
        elif k_ == 'ruin':
            for qx, qy in ((10, -4), (26, 4), (40, -8)): ld.rectangle((cx + qx - 6, cy + qy - 6, cx + qx + 6, cy + qy + 6), outline=g, width=2)
        elif k_ == 'track':
            for t in range(3): ld.line((cx + t * 18, cy, cx + t * 18 + 11, cy), fill=g, width=2)
        elif k_ == 'spot':
            ld.polygon([(cx + 26, cy - 8), (cx + 34, cy + 6), (cx + 18, cy + 6)], fill=g)
        ld.text((cx + 58, cy), nz if zh else ne, font=F_ZH(22) if zh else F_TEXT(23), fill=IVORY + (235,), anchor='lm')
    lrgb, _ = ck.put(lrgb, la_, ll)
    ck.paste_rot(base_img, ck.finish(lrgb, la_, 20, op=0.55), 2632, 152, -1.2)

    # ══════════ 5. 兵力编成表：藏青账册纸（金线分格、纹章盾）
    x0, y0, x1, y1 = OOB
    PX0, PY0, PX1, PY1 = x0 - 36, y0 - 22, x1 + 24, y1 + 34
    pw, ph = PX1 - PX0, PY1 - PY0
    prgb, pa, _ = ck.real_sheet(pw, ph, NAVY, pad=0, flip=True)
    prgb = navy_paper(pw, ph, 5, 0.32)
    yy2, xx2 = np.mgrid[0:ph, 0:pw].astype(np.float32)
    pa = np.maximum(pa, ndi.gaussian_filter(((xx2 > 30) & (xx2 < pw - 18) & (yy2 > 16) & (yy2 < ph - 24)).astype(np.float32), 3))
    prgb = ck.corner_dots(prgb, 11, pw, ph, 700, 260, (34, 50, 82), 0.6)
    pl = Image.new('RGBA', (pw, ph), (0, 0, 0, 0)); pd = ImageDraw.Draw(pl)
    q = lambda X, Y: (X - PX0, Y - PY0)
    gc = GOLD_D + (230,); g = GOLD + (255,)
    head = 64
    (hx, hy) = q(x0 + 30, y0 + head // 2 + 12)
    if zh:
        pd.text((hx, hy - 12), '兵力编成', font=F_ZH(34, True), fill=g, anchor='lm')
        tracked(pd, hx + 170, hy + 2, 'ORDER OF BATTLE', F_CAPS(18, 600), GOLD_D + (255,), 5)
    else:
        tracked(pd, hx, hy + 2, 'ORDER OF BATTLE', F_CAPS(32, 700), g, 7)
    tracked(pd, *q(x1 - 30, y0 + head // 2 + 10), '星枪团 · 第一远征队' if zh else 'STAR LANCERS · FIRST EXPEDITION', F_ZH(24) if zh else F_CAPS(20, 600), GOLD_D + (255,), 2 if zh else 4, 'r')
    labw = 230; cw = (x1 - x0 - labw) / len(TROOPS)
    rows_y = [y0 + head, y0 + head + 120, y0 + head + 200, y0 + head + 280, y1]
    # 外框：粗细两道金线；表头下一道带菱形的线
    pd.rectangle((*q(x0, y0), *q(x1, y1)), outline=g, width=3)
    pd.rectangle((*q(x0 + 9, y0 + 9), *q(x1 - 9, y1 - 9)), outline=gc, width=1)
    pd.line((*q(x0 + 9, rows_y[0]), *q(x1 - 9, rows_y[0])), fill=g, width=2)
    for k in range(1, 4): pd.line((*q(x0 + 9, rows_y[k]), *q(x1 - 9, rows_y[k])), fill=GOLD_D + (130,), width=1)
    pd.line((*q(x0 + labw, rows_y[0]), *q(x0 + labw, y1 - 9)), fill=gc, width=2)
    rl = [('兵种', 'UNIT'), ('在营', 'FIT'), ('伤员', 'WOUNDED'), ('出征', 'MARCHING')]
    for i, (a_, b_) in enumerate(rl):
        X, Y = q(x0 + 30, (rows_y[i] + rows_y[i + 1]) / 2)
        if zh: tracked(pd, X, Y + 11, a_, F_ZH(28), GOLD + (235,), 14)
        else: tracked(pd, X, Y + 9, b_, F_CAPS(21, 700), GOLD + (235,), 4)
    cols = []
    for i, tid in enumerate(TROOPS):
        cx0 = x0 + labw + i * cw; ccx = cx0 + cw / 2
        if i: pd.line((*q(cx0, rows_y[0] + 14), *q(cx0, y1 - 22)), fill=GOLD_D + (110,), width=1)
        X, Y = q(ccx, rows_y[0] + 44)                                                              # 盾高 61：上边距 13，盾和名字隔 8，下边距 13
        heraldic(pd, X, Y, 52, tid, (16, 22, 40, 255), GOLD + (255,), IVORY + (255,))
        nm = TROOP_NAMES[tid][0 if zh else 1]
        X, Y = q(ccx, rows_y[0] + 94)
        if zh: pd.text((X, Y), nm, font=F_ZH(26), fill=IVORY + (235,), anchor='mm')
        else: tracked(pd, X, Y + 8, nm.upper(), F_CAPS(17, 700), IVORY + (235,), 2, 'm')
        cols.append(ccx)
    prgb, _ = ck.put(prgb, pa, pl)
    base_img.alpha_composite(ck.finish(prgb, pa, 26, sh=(6, 12), blur=12, op=0.65), (PX0 - 26, PY0 - 26))

    # ══════════ 6. 右下：统率 / 星晶 / 纹章——象牙白小纸压在账册右边
    sx0, sy0, sx1, sy1 = SIDE
    QX0, QY0, QX1, QY1 = sx0 - 22, sy0 - 30, sx1 + 30, sy1 + 30
    qw, qh = QX1 - QX0, QY1 - QY0
    qrgb, qa, _ = ck.real_sheet(qw, qh, IVORY, pad=0)
    yy3, xx3 = np.mgrid[0:qh, 0:qw].astype(np.float32)
    qa = np.maximum(qa, ndi.gaussian_filter(((xx3 > 26) & (xx3 < qw - 26) & (yy3 > 26) & (yy3 < qh - 26)).astype(np.float32), 3))
    ql = Image.new('RGBA', (qw, qh), (0, 0, 0, 0)); qd = ImageDraw.Draw(ql)
    rows = [sy0, sy0 + 130, sy0 + 260, sy1]
    qq = lambda X, Y: (X - QX0, Y - QY0)
    LX, RX = 2352, 2930                                                                               # 版心左右线（数值在运行时右对齐到 RX）
    for yv in rows[1:-1]:
        X0_, Y_ = qq(LX, yv); X1_, _ = qq(RX, yv); qd.line((X0_, Y_, X1_, Y_), fill=SEPIA + (150,), width=1); star4(qd, (X0_ + X1_) / 2, Y_, 6, SEPIA + (200,))
    def lab(text_zh, text_en, x, ymid):
        X, Y = qq(x, ymid)
        if zh: tracked(qd, X, Y + 10, text_zh, F_ZH(28), SEPIA + (255,), 14)
        else: tracked(qd, X, Y + 8, text_en, F_CAPS(21, 700), SEPIA + (255,), 4)
    lab('统率', 'COMMAND', LX, (rows[0] + rows[1]) / 2)
    gx_, gy_ = LX + 14, (rows[1] + rows[2]) / 2                                                       # 星晶：小菱形在左边线上，标签跟在后面
    X, Y = qq(gx_, gy_); qd.polygon([(X, Y - 22), (X + 12, Y), (X, Y + 22), (X - 12, Y)], outline=SEPIA + (255,), width=3); qd.line((X, Y - 22, X, Y + 22), fill=SEPIA + (255,), width=1)
    lab('星晶', 'CRYSTALS', LX + 46, gy_)
    lab('纹章', 'INSIGNIA', LX, rows[2] + 40)
    relic_boxes = []
    for i in range(7):
        bx, by = (LX + RX) / 2 + (i - 3) * 84, rows[2] + 128
        X, Y = qq(bx, by); qd.ellipse((X - 30, Y - 30, X + 30, Y + 30), outline=SEPIA + (120,), width=2)
        relic_boxes.append((bx, by))
    qrgb, _ = ck.put(qrgb, qa, ql)
    ck.paste_rot(base_img, ck.finish(qrgb, qa, 22, op=0.6), (QX0 + QX1) / 2, (QY0 + QY1) / 2, 0)

    out = base_img.convert('RGB')
    os.makedirs(OUT, exist_ok=True)
    out.save(OUT + f'map_{lang}.png')
    print('写出', OUT + f'map_{lang}.png')
    return dict(size=[W, H], field=[FX0, FY0, FX1, FY1], oob=list(OOB), side=list(SIDE), oob_cols=[float(c) for c in cols], oob_rows=[float(v) for v in rows_y],
                oob_cw=float(cw), side_rows=rows, relic_boxes=relic_boxes, crystal_icon=[gx_, gy_], troops=TROOPS)


if __name__ == '__main__':
    lay = None
    for lang in (sys.argv[2:] or ['zh', 'en']):
        lay = build(lang)
    json.dump(lay, open(OUT + 'map_layout.json', 'w'), ensure_ascii=False, indent=1)
    print('版面', OUT + 'map_layout.json')
