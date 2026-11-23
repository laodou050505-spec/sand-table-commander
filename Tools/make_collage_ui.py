"""拼贴风格的界面底件（11-17 用户：面板不要规规矩矩、不要太死板；可以有深蓝互动那一路的高级感，配色用我们自己的；
字不要太大，版面要更讲究；需要时用生图）。
做法：三张生图原件（ArtSource/UI/gen/collage：铜版画日轮 src_sun、撕边羊皮纸 src_paper、长纸条 src_strip，都是黑底）
→ 抠出来 → 调色 / 染成藏青 → 叠印金线、小号拉丁大写、花体字、半调网点 → 每件自带一圈烘好的软阴影。
游戏里每件是一张单独的面片，前后错开几毫米到几厘米摆（头显里有真实的前后层次），各自转一点角度。
  fx_paper        最底下一张撕边羊皮纸（象牙白，印着月相、细线、小字）
  fx_card         压在上面的藏青纸（结算内容的底）；fx_card_s 小一号（暂停菜单用）
  fx_sun          圆形深色纸片 + 金色铜版画日轮（大字身后）
  fx_strip_win / fx_strip_lose / fx_strip_pause   斜贴的纸条：花体拉丁文 + 一段队伍色
  fx_plaque / fx_plaque_hi                        按钮：票根（平时藏青、指着时象牙白）
  fx_rule         人数行下面的细线
python3 Tools/make_collage_ui.py → ArtSource/Cover/v6/alpha/，并出一张版面小样 /tmp/sp2/collage_preview.jpg"""
import math, json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from scipy import ndimage as ndi

R = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = R + 'ArtSource/UI/gen/collage/'; A = R + 'ArtSource/Cover/v6/alpha/'; F = R + 'ArtSource/Fonts/'
NAVY_T = np.array([11, 15, 26], np.float32); NAVY_B = np.array([24, 34, 57], np.float32)
GOLD = (226, 188, 116); GOLD_D = (150, 120, 72); IVORY = (238, 229, 208); INK = (22, 28, 44)
BLUE = (44, 86, 156); RED = (160, 46, 38)
PAD = 56          # 每件四周留给阴影的边
SIZES = {}        # 名字 → 内容宽 / 整张宽（游戏里按“内容宽”摆时要除以它）

def font(name, size, wght=None):
    f = ImageFont.truetype(F + name, size)
    if wght:
        try: f.set_variation_by_axes([wght])
        except Exception: pass
    return f

def cut(name, flip=False):
    """黑底原件 → (rgb 浮点, alpha 浮点)，裁到内容。黑边用纸的颜色往外补，缩小后边缘不发黑。"""
    im = Image.open(SRC + name).convert('RGB')
    if flip: im = im.transpose(Image.ROTATE_180)
    rgb = np.asarray(im, np.float32); mx = rgb.max(-1) / 255
    a = np.clip((mx - 0.10) / 0.14, 0, 1); a = ndi.gaussian_filter(a, 0.8)
    solid = (a > 0.5).astype(np.float32)
    solid = ndi.binary_opening(solid > 0.5, iterations=2).astype(np.float32)          # 去掉零星噪点
    lab, n = ndi.label(solid > 0.5)
    if n > 1:
        big = 1 + int(np.argmax(ndi.sum(solid, lab, range(1, n + 1)))); a = a * (lab == big)
    ys, xs = np.where(a > 0.05); y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    rgb, a = rgb[y0:y1, x0:x1], a[y0:y1, x0:x1]
    w = ndi.gaussian_filter((a > 0.9).astype(np.float32), 6)[..., None] + 1e-4
    fill = np.stack([ndi.gaussian_filter(rgb[..., c] * (a > 0.9), 6) for c in range(3)], -1) / w
    rgb = np.where((a > 0.9)[..., None], rgb, fill)
    return rgb, a

def resize(rgb, a, w):
    h = int(round(rgb.shape[0] * w / rgb.shape[1]))
    im = Image.fromarray(np.concatenate([rgb.clip(0, 255), a[..., None] * 255], -1).astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)
    arr = np.asarray(im, np.float32); return arr[..., :3], arr[..., 3] / 255

def ivory(rgb):
    """羊皮纸调色：黄斑收掉一大半，整体略压暗（头显里纯白会刺眼），保留折痕。"""
    lum = (rgb @ np.array([0.299, 0.587, 0.114], np.float32))[..., None] / 255
    flat = lum * np.array(IVORY, np.float32) / 0.93
    return (rgb * 0.4 + flat * 0.6) * 0.94

def navy(rgb, a):
    """纸染成藏青：只留纸的折痕明暗，上深下浅；撕开的边露出浅色纸芯。"""
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32) / 255
    loc = np.clip(lum / (ndi.gaussian_filter(lum, 30) + 1e-3), 0.8, 1.2) ** 2.4
    h = rgb.shape[0]; t = (np.arange(h, dtype=np.float32) / h)[:, None, None] ** 1.2
    out = (NAVY_T * (1 - t) + NAVY_B * t) * loc[..., None]
    out += np.random.default_rng(3).normal(0, 2.6, out.shape[:2] + (1,)).astype(np.float32)      # 颗粒
    rim = np.clip((0.9 - ndi.gaussian_filter(a, 3.5)) / 0.45, 0, 1) * (a > 0.2)
    return out * (1 - rim[..., None] * 0.75) + np.array([168, 172, 180], np.float32) * rim[..., None] * 0.75 * lum[..., None]

def halftone(shape, cell, amount):
    """半调网点：amount（0–1 的图）越大点越大。"""
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]].astype(np.float32)
    dx = (xx % cell) - cell / 2; dy = ((yy + (np.floor(xx / cell) % 2) * cell / 2) % cell) - cell / 2
    return np.clip((amount * cell * 0.46 - np.sqrt(dx * dx + dy * dy)) / 1.2 + 0.5, 0, 1)

def tracked(d, x, y, text, f, fill, gap, anchor='l'):
    """字距拉开的一行字。anchor = l / r / m。"""
    ws = [d.textlength(ch, font=f) for ch in text]; total = sum(ws) + gap * (len(text) - 1)
    if anchor == 'r': x -= total
    if anchor == 'm': x -= total / 2
    for ch, w in zip(text, ws):
        d.text((x, y), ch, font=f, fill=fill); x += w + gap
    return total

def layer(shape, S=2):
    return Image.new('RGBA', (shape[1] * S, shape[0] * S), (0, 0, 0, 0))

def over(rgb, a, lay, clip=True):
    """把一张画好的 RGBA 图层（可以是 2 倍大）叠到纸上；clip = 只印在纸里面。"""
    lay = lay.resize((rgb.shape[1], rgb.shape[0]), Image.LANCZOS); L = np.asarray(lay, np.float32)
    la = L[..., 3:] / 255
    rgb = rgb * (1 - la) + L[..., :3] * la
    if not clip: a = np.maximum(a, la[..., 0])
    return rgb, a

def save(name, rgb, a, sh=(7, 12), blur=11, op=0.5, pad=PAD):
    """四周加边、垫一圈软阴影，存盘。"""
    h, w = a.shape; H, W = h + pad * 2, w + pad * 2
    out = np.zeros((H, W, 4), np.float32)
    s = np.zeros((H, W), np.float32); s[pad + sh[1]:pad + sh[1] + h, pad + sh[0]:pad + sh[0] + w] = a
    s = ndi.gaussian_filter(s, blur) * op
    A_ = np.zeros((H, W), np.float32); A_[pad:pad + h, pad:pad + w] = a
    C = np.zeros((H, W, 3), np.float32); C[pad:pad + h, pad:pad + w] = rgb
    out[..., :3] = C * A_[..., None] + np.array([4, 5, 9], np.float32) * (1 - A_[..., None])
    out[..., 3] = (A_ + s * (1 - A_)) * 255
    Image.fromarray(out.clip(0, 255).astype(np.uint8), 'RGBA').save(A + name + '.png')
    SIZES[name] = round(w / W, 4)

def diamond(d, cx, cy, r, fill): d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=fill)

def star4(d, cx, cy, r, fill):
    k = r * 0.2
    d.polygon([(cx, cy - r), (cx + k, cy - k), (cx + r, cy), (cx + k, cy + k), (cx, cy + r), (cx - k, cy + k), (cx - r, cy), (cx - k, cy - k)], fill=fill)

def moons(d, x, y, r, n, gap, col, S):
    """一排月相：朔 → 望 → 晦。亮的部分 = 圆减去一个挪开的圆。"""
    for i in range(n):
        cx = x + i * gap; ph = i / (n - 1); f = 1 - abs(ph * 2 - 1)
        off = f * r * 2.1 * (-1 if ph < 0.5 else 1)
        m = Image.new('L', (r * 2 + 4, r * 2 + 4), 0); md = ImageDraw.Draw(m)
        md.ellipse((2, 2, r * 2 + 2, r * 2 + 2), fill=255); md.ellipse((2 + off, 2, r * 2 + 2 + off, r * 2 + 2), fill=0)
        d.bitmap((cx - r - 2, y - r - 2), m, fill=col)
        d.ellipse((cx - r, y - r, cx + r, y + r), outline=col, width=max(1, S))

# ================================================================ 羊皮纸（最底层）
rgb, a = cut('src_paper.png'); rgb, a = resize(rgb, a, 1500); rgb = ivory(rgb)
h, w = a.shape; S = 2
lay = layer(a.shape, S); d = ImageDraw.Draw(lay); sep = (96, 74, 46, 215); sep2 = (96, 74, 46, 120)
f1 = font('Cinzel.ttf', 23 * S, 600); f0 = font('Cinzel.ttf', 15 * S, 500)
tracked(d, (w - 110) * S, 50 * S, 'ORDO · BELLI', f0, sep2, 7 * S, 'r')
moons(d, int(w * 0.50) * S, 58 * S, 11 * S, 7, 36 * S, sep, S)
d.line([(int(w * 0.46) * S, 86 * S), ((w - 110) * S, 86 * S)], fill=sep2, width=S)
tracked(d, (w - 96) * S, (h - 64) * S, 'FIELD DISPATCH', f0, sep2, 7 * S, 'r')
d.line([(int(w * 0.62) * S, (h - 38) * S), ((w - 96) * S, (h - 38) * S)], fill=sep2, width=S)
for k in range(9):                                                            # 左边一列刻度
    y = (170 + k * 62) * S; d.line([(48 * S, y), ((66 if k % 4 else 82) * S, y)], fill=sep2, width=S)
v = Image.new('RGBA', (560 * S, 40 * S), (0, 0, 0, 0)); tracked(ImageDraw.Draw(v), 0, 4 * S, 'ORDER OF BATTLE', f0, sep2, 8 * S)
v = v.rotate(90, expand=True); lay.alpha_composite(v, (92 * S, 190 * S))
for (cx, cy) in ((w - 62, 150),):                           # 套准十字（印刷品的角标）
    d.line([((cx - 14) * S, cy * S), ((cx + 14) * S, cy * S)], fill=sep2, width=S); d.line([(cx * S, (cy - 14) * S), (cx * S, (cy + 14) * S)], fill=sep2, width=S)
    d.ellipse(((cx - 7) * S, (cy - 7) * S, (cx + 7) * S, (cy + 7) * S), outline=sep2, width=S)
rgb, a = over(rgb, a, lay)
yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
amt = np.clip(1 - np.hypot((xx - w * 0.02) / (w * 0.42), (yy - h * 1.0) / (h * 0.5)), 0, 1) ** 1.2          # 左下角一片藏青网点
dots = halftone(a.shape, 13, amt)[..., None]
rgb = rgb * (1 - dots * 0.72) + np.array(INK, np.float32) * dots * 0.72
save('fx_paper', rgb, a, sh=(8, 14), blur=14, op=0.45)

# ================================================================ 藏青纸（内容的底）
def navy_card(name, wpx, crop=None, caption=None, divider=True):
    rgb, a = cut('src_paper.png', flip=True)
    if crop:
        H0, W0 = a.shape; x0, x1 = int(W0 * crop[0]), int(W0 * crop[1]); y1 = int(H0 * crop[2]); rgb, a = rgb[:y1, x0:x1], a[:y1, x0:x1]
        ramp = np.clip(np.minimum(np.arange(x1 - x0), (x1 - x0 - 1) - np.arange(x1 - x0)) / 3.0, 0, 1); a = a * ramp[None, :]   # 裁出来的边是直的（剪刀剪的）
        a = a * np.clip(((y1 - 1) - np.arange(y1)) / 3.0, 0, 1)[:, None]
    rgb, a = resize(rgb, a, wpx); rgb = navy(rgb, a); h, w = a.shape; S = 2
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    amt = np.clip(1 - np.hypot((xx - w * 1.0) / (w * 0.5), (yy - h * 1.0) / (h * 0.62)), 0, 1) ** 1.3 * 0.9               # 右下角网点
    dots = halftone(a.shape, 12, amt)[..., None]; rgb = rgb + dots * np.array([15, 23, 40], np.float32)
    ppm = w / 0.47; sx, sy = w / 2 - 0.155 * ppm, h / 2 - 0.034 * ppm; sr = 0.1075 * ppm                                # 日轮纸片在这张纸上的圆心和半径（版面：纸面宽 0.47 m）
    ra = np.arctan2(yy - sy, xx - sx); rr = np.hypot(yy - sy, xx - sx) / h                                               # 从日轮圆心往外极淡的放射线
    rays = (np.cos(ra * 72) > 0.99) * np.clip((rr - 0.34) / 0.05, 0, 1) * np.clip((0.7 - rr) / 0.3, 0, 1)
    if divider: rgb = rgb + rays[..., None] * np.array([34, 29, 18], np.float32)
    lay = layer(a.shape, S); d = ImageDraw.Draw(lay); g = GOLD + (255,); gd = GOLD_D + (255,); gf = GOLD_D + (150,)
    f0 = font('Cinzel.ttf', 15 * S, 500)
    if caption:
        tracked(d, (w - 86) * S, 58 * S, caption, f0, gd, 8 * S, 'r')
        d.line([(int(w * 0.5) * S, 92 * S), ((w - 86) * S, 92 * S)], fill=gd, width=S); diamond(d, (w - 86) * S, 92 * S, 5 * S, g)
    if divider:
        # 11-17 用户指着日轮和竖线之间说“这里也有点空”→ 绕着日轮印一段星盘轨道：两道弧（外面那道是虚线）、中间一圈刻度、
        #   内轨上一颗带环的小行星、外轨上一颗小星；只画右半边（左半边在纸外 / 被日轮盖住）
        def arc(r, a0, a1, fill, wd, dash=None):
            n = int((a1 - a0) * 2)
            for i in range(n):
                if dash and (i // dash) % 2: continue
                t0, t1 = math.radians(a0 + i / 2), math.radians(a0 + (i + 1.2) / 2)
                d.line([((sx + math.cos(t0) * r) * S, (sy + math.sin(t0) * r) * S), ((sx + math.cos(t1) * r) * S, (sy + math.sin(t1) * r) * S)], fill=fill, width=wd)
        r1, r2 = sr + 0.012 * ppm, sr + 0.027 * ppm
        arc(r1, -64, 66, gd, 2 * S); arc(r2, -58, 58, gf, S, dash=5)
        for k in range(-56, 57, 4):
            t = math.radians(k); a_, b_ = r1 + 5, r1 + (15 if k % 20 == 0 else 9)
            d.line([((sx + math.cos(t) * a_) * S, (sy + math.sin(t) * a_) * S), ((sx + math.cos(t) * b_) * S, (sy + math.sin(t) * b_) * S)], fill=gf, width=S)
        t = math.radians(-27); px, py = (sx + math.cos(t) * r2) * S, (sy + math.sin(t) * r2) * S                                  # 小行星：实心金点 + 一道斜环
        d.ellipse((px - 11 * S, py - 11 * S, px + 11 * S, py + 11 * S), fill=(11, 15, 26, 255)); d.ellipse((px - 8 * S, py - 8 * S, px + 8 * S, py + 8 * S), fill=g)
        ring = Image.new('RGBA', (60 * S, 60 * S), (0, 0, 0, 0)); ImageDraw.Draw(ring).ellipse((6 * S, 23 * S, 54 * S, 37 * S), outline=gd, width=S)
        ring = ring.rotate(24, resample=Image.BICUBIC); lay.alpha_composite(ring, (int(px - 30 * S), int(py - 30 * S)))
        t = math.radians(31); star4(d, (sx + math.cos(t) * r2) * S, (sy + math.sin(t) * r2) * S, 10 * S, g)
        t = math.radians(52); px, py = (sx + math.cos(t) * r1) * S, (sy + math.sin(t) * r1) * S
        d.ellipse((px - 5 * S, py - 5 * S, px + 5 * S, py + 5 * S), fill=(11, 15, 26, 255), outline=gd, width=S)
        x = int(w * 0.485) * S
        d.line([(x, int(h * 0.2) * S), (x, int(h * 0.44) * S)], fill=gf, width=S); d.line([(x, int(h * 0.5) * S), (x, int(h * 0.7) * S)], fill=gf, width=S)
        diamond(d, x, int(h * 0.47) * S, 5 * S, gd)
    d.line([(64 * S, (h - 62) * S), (int(w * 0.36) * S, (h - 62) * S)], fill=gf, width=S)                                 # 左下一段细线 + 小星
    star4(d, 64 * S, (h - 62) * S, 9 * S, gd)
    cx, cy = 70, 74                                                                                                       # 左上角套准十字
    d.line([((cx - 13) * S, cy * S), ((cx + 13) * S, cy * S)], fill=gf, width=S); d.line([(cx * S, (cy - 13) * S), (cx * S, (cy + 13) * S)], fill=gf, width=S)
    rgb, a2 = over(rgb, a, lay)
    save(name, rgb, a, sh=(9, 15), blur=13, op=0.55)

navy_card('fx_card', 1400, caption='AFTER · ACTION · REPORT')
navy_card('fx_card_s', 760, crop=(0.08, 0.62, 0.74), caption=None, divider=False)

# ================================================================ 日轮 / 月轮（圆形深色纸片 + 金线）
def medallion(name, src):
    im = Image.open(SRC + src).convert('RGB').resize((1024, 1024), Image.LANCZOS)
    lum = np.asarray(im, np.float32).max(-1) / 255; la = np.clip((lum - 0.2) / 0.5, 0, 1)
    S_ = 1024; yy, xx = np.mgrid[0:S_, 0:S_].astype(np.float32); r = np.hypot(xx - S_ / 2, yy - S_ / 2) / (S_ / 2)
    la = la * (r < 0.95)                                                                          # 圆外的零星笔画不要
    disc = np.clip((0.955 - r) / 0.004, 0, 1)                                                     # 纸片比外圈略大一点
    t = (yy / S_)[..., None]; base = (NAVY_T * 0.9 * (1 - t) + NAVY_B * 0.85 * t) + np.random.default_rng(5).normal(0, 2.4, (S_, S_, 1)).astype(np.float32)
    glow = np.exp(-(r / 0.5) ** 2)[..., None] * np.array([30, 22, 8], np.float32)                 # 中心一点暖光，托住大字
    gold = np.array(GOLD, np.float32) * (0.82 + 0.3 * (1 - yy / S_))[..., None]
    rgb = (base + glow) * (1 - la[..., None]) + gold * la[..., None]
    save(name, rgb, disc, sh=(6, 10), blur=10, op=0.55, pad=40)

medallion('fx_sun', 'src_sun.png')
import os
if os.path.exists(SRC + 'src_moon.png'): medallion('fx_moon', 'src_moon.png')

# ================================================================ 一小张羊皮纸碎片（暂停菜单垫在后面）
rgb, a = cut('src_paper.png')
H0, W0 = a.shape; rgb, a = rgb[int(H0 * 0.22):, int(W0 * 0.5):], a[int(H0 * 0.22):, int(W0 * 0.5):]
a = a * np.clip(np.arange(a.shape[1]) / 3.0, 0, 1)[None, :] * np.clip(np.arange(a.shape[0]) / 3.0, 0, 1)[:, None]          # 左、上两条边是剪的，右、下是撕的
rgb, a = resize(rgb, a, 760); rgb = ivory(rgb); h, w = a.shape; S = 2
lay = layer(a.shape, S); d = ImageDraw.Draw(lay); sep = (96, 74, 46, 215); sep2 = (96, 74, 46, 120); f0 = font('Cinzel.ttf', 15 * S, 500)
tracked(d, (w - 80) * S, (h - 70) * S, 'ORDO · BELLI', f0, sep2, 7 * S, 'r')
d.line([(int(w * 0.4) * S, (h - 44) * S), ((w - 80) * S, (h - 44) * S)], fill=sep2, width=S)
moons(d, 60 * S, (h - 110) * S, 10 * S, 5, 34 * S, sep, S)
for k in range(7):
    x = (w - 44) * S; y = (90 + k * 58) * S; d.line([(x, y), (x - (30 if k % 3 == 0 else 16) * S, y)], fill=sep2, width=S)
rgb, a = over(rgb, a, lay)
yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
amt = np.clip(1 - np.hypot((xx - w * 0.0) / (w * 0.55), (yy - h * 1.0) / (h * 0.5)), 0, 1) ** 1.2
dots = halftone(a.shape, 13, amt)[..., None]; rgb = rgb * (1 - dots * 0.72) + np.array(INK, np.float32) * dots * 0.72
save('fx_paper_s', rgb, a, sh=(7, 12), blur=12, op=0.45)

# ================================================================ 纸条
def strip(name, script, caps, band):
    rgb, a = cut('src_strip.png'); rgb, a = resize(rgb, a, 1100); rgb = ivory(rgb); h, w = a.shape; S = 2
    bw = int(w * 0.085)                                                                       # 左端一段队伍色（印上去的，透出纸纹）
    k = np.clip((bw - np.arange(w)) / 2.0, 0, 1)[None, :, None] * 0.9
    lumi = (rgb @ np.array([0.299, 0.587, 0.114], np.float32))[..., None] / 235
    rgb = rgb * (1 - k) + np.array(band, np.float32) * lumi * k
    lay = layer(a.shape, S); d = ImageDraw.Draw(lay); ink = INK + (240,); ink2 = INK + (150,)
    fs = font('PinyonScript-Regular.ttf', int(h * 0.56) * S); f0 = font('Cinzel.ttf', 14 * S, 600)
    x0 = (bw + 34) * S; bb = d.textbbox((0, 0), script, font=fs)
    d.text((x0 - bb[0], (h * 0.47) * S - (bb[1] + bb[3]) / 2), script, font=fs, fill=ink)
    d.line([(bw + 5) * S, 12 * S, (bw + 5) * S, (h - 12) * S], fill=ink2, width=S)
    tw = tracked(d, (w - 62) * S, (h - 50) * S, caps, f0, ink2, 6 * S, 'r')
    d.line([((w - 62) * S - tw, (h - 58) * S + 0), ((w - 62) * S - tw - 70 * S, (h - 58) * S)], fill=ink2, width=S)
    star4(d, int(bw * 0.5) * S, int(h * 0.5) * S, 11 * S, IVORY + (235,))
    rgb, a2 = over(rgb, a, lay)
    save(name, rgb, a, sh=(6, 10), blur=9, op=0.5, pad=44)

strip('fx_strip_win', 'Ad Astra', 'THE FIELD IS WON', BLUE)
strip('fx_strip_lose', 'Per Aspera', 'THE FIELD IS LOST', RED)
strip('fx_strip_pause', 'Intermission', 'AT EASE', GOLD_D)

# ================================================================ 按钮：票根
def ticket(name, hi):
    rgb, a = cut('src_strip.png', flip=True)
    H0, W0 = a.shape; x1 = int(W0 * 0.74); rgb, a = rgb[:, :x1], a[:, :x1]                    # 右端剪齐、左端是撕的
    ramp = np.clip(((x1 - 1) - np.arange(x1)) / 3.0, 0, 1); a = a * ramp[None, :]
    rgb, a = resize(rgb, a, 820); h, w = a.shape; S = 2
    rgb = ivory(rgb) * 1.03 if hi else navy(rgb, a) * np.array([1.5, 1.42, 1.3], np.float32) + np.array([12, 13, 15], np.float32)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32); sx = w * 0.2                              # 票根的齿孔线：上下各咬一个半圆，中间一列小孔
    bite = np.minimum(np.hypot(xx - sx, yy - 0), np.hypot(xx - sx, yy - (h - 1))); a = a * np.clip((bite - h * 0.085) / 1.5, 0, 1)
    lay = layer(a.shape, S); d = ImageDraw.Draw(lay)
    c1 = (INK + (230,)) if hi else (GOLD + (255,)); c2 = (INK + (120,)) if hi else (GOLD_D + (200,))
    for k in range(7):
        y = h * (0.2 + 0.1 * k); d.ellipse(((sx - 2.2) * S, (y - 2.2) * S, (sx + 2.2) * S, (y + 2.2) * S), fill=c2)
    star4(d, int(sx * 0.52) * S, int(h * 0.5) * S, int(h * 0.11) * S, c1)
    d.line([((sx + 20) * S, 15 * S), ((w - 150) * S, 15 * S)], fill=c2, width=S)               # 上沿一段、下沿一段，错开
    d.line([((sx + 110) * S, (h - 15) * S), ((w - 24) * S, (h - 15) * S)], fill=c2, width=S)
    diamond(d, (w - 24) * S, (h - 15) * S, 4 * S, c1)
    rgb, a2 = over(rgb, a, lay)
    save(name, rgb, a, sh=(5, 9), blur=8, op=0.55 if not hi else 0.4, pad=40)

ticket('fx_plaque', False); ticket('fx_plaque_hi', True)

# ================================================================ 人数行的细线
W, H = 700, 60; S = 2; im = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
d.line([(20 * S, 40 * S), (500 * S, 40 * S)], fill=GOLD_D + (190,), width=S); d.line([(520 * S, 40 * S), (680 * S, 40 * S)], fill=GOLD_D + (90,), width=S)
diamond(d, 510 * S, 40 * S, 4 * S, GOLD + (255,))
im.resize((W, H), Image.LANCZOS).save(A + 'fx_rule.png'); SIZES['fx_rule'] = 1.0

# ================================================================ 邮戳（盖在藏青纸右下的空处：圆章 + 波浪销戳线，磨旧的暗金色）
# 11-17 用户指着人数下面、按钮上面那一块说“这里有点空”
def stamp():
    S = 2; W, H = 760, 440; im = Image.new('L', (W * S, H * S), 0); d = ImageDraw.Draw(im); cx, cy, R0 = 220 * S, 220 * S, 190 * S
    d.ellipse((cx - R0, cy - R0, cx + R0, cy + R0), outline=255, width=5 * S)
    d.ellipse((cx - R0 + 13 * S, cy - R0 + 13 * S, cx + R0 - 13 * S, cy + R0 - 13 * S), outline=200, width=S)
    r1 = 118 * S; d.ellipse((cx - r1, cy - r1, cx + r1, cy + r1), outline=255, width=2 * S)
    star4(d, cx, cy, 70 * S, 255); d.ellipse((cx - 9 * S, cy - 9 * S, cx + 9 * S, cy + 9 * S), fill=0)
    for k in range(8):
        an = math.radians(k * 45 + 22.5); d.ellipse((cx + math.cos(an) * 92 * S - 3 * S, cy + math.sin(an) * 92 * S - 3 * S, cx + math.cos(an) * 92 * S + 3 * S, cy + math.sin(an) * 92 * S + 3 * S), fill=230)
    f = font('Cinzel.ttf', 34 * S, 700); txt = 'FIELD DISPATCH · ORDO BELLI · '                              # 字沿圆排一圈
    for i, ch in enumerate(txt):
        an = -90 + i * 360 / len(txt); g = Image.new('L', (60 * S, 60 * S), 0); ImageDraw.Draw(g).text((30 * S, 30 * S), ch, font=f, fill=255, anchor='mm')
        g = g.rotate(-an - 90, resample=Image.BICUBIC); rr = 153 * S
        px = cx + math.cos(math.radians(an)) * rr; py = cy + math.sin(math.radians(an)) * rr
        im.paste(255, (int(px - 30 * S), int(py - 30 * S)), g)
    for k in range(5):                                                                                       # 波浪销戳线，从圆章右边拖出去
        y0 = cy + (k - 2) * 46 * S; pts = [(x, y0 + math.sin((x - cx) / (34.0 * S)) * 11 * S) for x in range(cx + R0 + 14 * S, (W - 24) * S, 4)]
        d.line(pts, fill=235, width=4 * S)
    L = np.asarray(im.resize((W, H), Image.LANCZOS), np.float32) / 255
    rng = np.random.default_rng(11)                                                                          # 磨损：大块的缺墨 + 细碎的掉点
    big = np.asarray(Image.fromarray((rng.random((H // 28, W // 28)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC), np.float32) / 255
    L = L * np.clip((big - 0.2) / 0.35, 0.15, 1) * (rng.random((H, W)) > 0.14)
    out = np.zeros((H, W, 4), np.float32); out[..., :3] = np.array(GOLD_D, np.float32) * 1.15; out[..., 3] = ndi.gaussian_filter(L, 0.6) * 0.8 * 255
    Image.fromarray(out.clip(0, 255).astype(np.uint8), 'RGBA').save(A + 'fx_stamp.png'); SIZES['fx_stamp'] = 1.0
stamp()

# ================================================================ 结算里人数前面的两面小旗（蓝 = 我军、红 = 敌军；原来是两块旗面贴图，缩小后只是暗色方块）
import sys as _sys; _sys.path.insert(0, R + 'Tools')
import collage_kit as ck
for nm, colr in (('fx_flag_ours', (34, 62, 132)), ('fx_flag_foe', (128, 30, 24))):
    fl = ck.pennant(150, 200, colr, IVORY, pad=10, seed=3, tip=0.24); dd = ImageDraw.Draw(fl); star4(dd, 75, 86, 30, IVORY + (255,))
    fl.save(A + nm + '.png'); SIZES[nm] = 1.0

# ================================================================ 封面（标题页）
# 11-18 用户：整个封面按西式奇幻重做。第一版在盔下面垫了一大张拼贴纸，用户：“标题不要大面积的 UI 底，太丑；这是 MR 游戏，MR 的标题不是一张大封面；视觉冲击力要更强”
# → 不要任何底板。盔后面悬一轮只有金线的大日轮（没有底色，透出真实房间），立体大字斜压在盔前，署名条和票根这些小件悬在下面。
def halo():
    S_ = 1600; L = ck.sun_lines(S_, 0.0, 0.96)
    yy, xx = np.mgrid[0:S_, 0:S_].astype(np.float32); r = np.hypot(xx - S_ / 2, yy - S_ / 2) / (S_ / 2)
    glow = np.exp(-(r / 0.42) ** 2) * 0.16 + np.exp(-((r - 0.6) / 0.05) ** 2) * 0.05                       # 中心一团很淡的暖光，托住盔
    a = np.clip(L * 0.95 + glow * (1 - L), 0, 1)
    gold = np.array(GOLD, np.float32) * (0.9 + 0.25 * (1 - yy / S_))[..., None]
    rgb = gold * L[..., None] + np.array([255, 196, 110], np.float32) * (1 - L[..., None])
    Image.fromarray(np.concatenate([rgb.clip(0, 255), a[..., None] * 255], -1).astype(np.uint8), 'RGBA').save(A + 'fx_halo.png'); SIZES['fx_halo'] = 1.0
halo()

# 封面菜单用的扁票根（3.4 : 1；结算板那种 2.3 : 1 的票根在菜单里太高，五张叠在一起互相压住）：真纸条两头原样、只拉中段，左端咬两个半圆 + 一列齿孔
def flat_ticket(name, hi):
    W_, H_, PAD_ = 840, 250, 12
    rgb, a, (x0, y0, x1, y1) = ck.real_strip(W_, H_, ck.IVORY if hi else (30, 42, 74), pad=PAD_, cut='r')
    h, w = a.shape; yy, xx = np.mgrid[0:h, 0:w].astype(np.float32); sx = w * 0.17
    bite = np.minimum(np.hypot(xx - sx, yy - 0), np.hypot(xx - sx, yy - (h - 1))); a = a * np.clip((bite - h * 0.085) / 1.5, 0, 1)
    if not hi: rgb = ck.corner_dots(rgb, 9, w * 1.0, h * 1.0, w * 0.3, h * 1.2, (50, 68, 112), 0.5)
    S = 2; lay = layer(a.shape, S); d = ImageDraw.Draw(lay)
    c1 = (INK + (230,)) if hi else (GOLD + (255,)); c2 = (INK + (120,)) if hi else (GOLD_D + (210,))
    for k in range(7):
        y = h * (0.2 + 0.1 * k); d.ellipse(((sx - 2.2) * S, (y - 2.2) * S, (sx + 2.2) * S, (y + 2.2) * S), fill=c2)
    star4(d, int(sx * 0.52) * S, int(h * 0.5) * S, int(h * 0.13) * S, c1)
    d.line([((sx + 20) * S, 13 * S), ((w - 170) * S, 13 * S)], fill=c2, width=S)
    d.line([((sx + 130) * S, (h - 13) * S), ((w - 26) * S, (h - 13) * S)], fill=c2, width=S); diamond(d, (w - 26) * S, (h - 13) * S, 4 * S, c1)
    rgb, a2 = over(rgb, a, lay)
    save(name, rgb, a, sh=(4, 7), blur=6, op=0.5 if not hi else 0.4, pad=PAD_)
flat_ticket('fx_ticket', False); flat_ticket('fx_ticket_hi', True)

# ================================================================ 教学提示条的底（给 Tools/make_tutorial_tips.py 用，存在 ArtSource/UI/gen/collage）
def long_strip(name, W, H, tab):
    rgb, a = cut('src_strip.png'); hh = H - 2 * 44; rgb, a = resize(rgb, a, int(round(rgb.shape[1] * hh / rgb.shape[0])))
    rgb, a = rgb[:hh], a[:hh]; w0 = a.shape[1]; e = int(w0 * 0.22); mid = (W - 2 * 44) - 2 * e                # 两头原样、中段横向拉长
    def stretch(x, ch):
        im = Image.fromarray(x.clip(0, 255).astype(np.uint8) if ch == 3 else (x * 255).clip(0, 255).astype(np.uint8)).resize((mid, hh), Image.LANCZOS)
        return np.asarray(im, np.float32) / (1 if ch == 3 else 255)
    rgb = np.concatenate([rgb[:, :e], stretch(rgb[:, e:w0 - e], 3), rgb[:, w0 - e:]], 1); a = np.concatenate([a[:, :e], stretch(a[:, e:w0 - e], 1), a[:, w0 - e:]], 1)
    ivo = ivory(rgb); nav = navy(rgb, a) * np.array([1.25, 1.2, 1.12], np.float32) + np.array([5, 6, 8], np.float32); h, w = a.shape; S = 2
    k = np.clip((tab - np.arange(w)) / 2.0, 0, 1)[None, :, None]                                             # 左端一截留成象牙白（像贴上去的纸签）
    rgb = nav * (1 - k) + ivo * k
    rgb = ck.corner_dots(rgb, 10, w * 1.0, h * 1.0, w * 0.3, h * 1.2, (44, 62, 104), 0.5)                      # 右端一片网点
    if tab:                                                                                                 # 纸签上印一枚墨色的小日轮
        D = int(h * 0.82); Ls = ck.sun_lines(D, 0.0, 0.96); x0 = int(tab * 0.52 - D / 2); y0 = (h - D) // 2
        xs0, xs1 = max(0, x0), min(w, x0 + D); k2 = Ls[:, xs0 - x0:xs1 - x0, None] * 0.85
        rgb[y0:y0 + D, xs0:xs1] = rgb[y0:y0 + D, xs0:xs1] * (1 - k2) + np.array(INK, np.float32) * k2
    lay = layer(a.shape, S); d = ImageDraw.Draw(lay)
    d.line([((tab + 4) * S, 10 * S), ((tab + 4) * S, (h - 10) * S)], fill=GOLD_D + (255,), width=S)
    d.line([((tab + 40) * S, 13 * S), (int(w * 0.62) * S, 13 * S)], fill=GOLD_D + (200,), width=S)             # 上沿一段、下沿一段，错开
    d.line([(int(w * 0.45) * S, (h - 13) * S), ((w - 60) * S, (h - 13) * S)], fill=GOLD_D + (200,), width=S)
    diamond(d, (w - 60) * S, (h - 13) * S, 4 * S, GOLD + (255,))
    rgb, a2 = over(rgb, a, lay)
    keep = A; globals()['A'] = SRC; save(name, rgb, a, sh=(6, 10), blur=9, op=0.5, pad=44); globals()['A'] = keep; SIZES.pop(name, None)

long_strip('tip_bg', 1700, 340, 150)
long_strip('learn_bg', 1300, 300, 0)

json.dump(SIZES, open(R + 'Tools/collage_sizes.json', 'w'), indent=1)
print(SIZES)
