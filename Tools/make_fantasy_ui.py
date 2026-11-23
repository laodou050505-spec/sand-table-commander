"""西式奇幻风格的界面底件（11-17 用户给了四张参考图：深色渐变底、细线花框、发光徽记、粗衬线字——只借气质，图形全部自己画）。
替换原来的墨笔刷 / 朱砂 / 印章：
  fx_card.png        结算板的底：深色竖向渐变卡片，内缩一圈细金线框 + 四角菱形和短线，淡淡的点阵底纹
  fx_plaque.png      按钮牌：两头收尖的深色长牌，细金线描边
  fx_plaque_hi.png   按钮牌（悬停）：同形，象牙白到金的渐变，外面一圈柔光
  fx_star.png        战绩星：四芒金星，带光晕
  fx_glow.png        大字背后的柔光（放射状细线 + 光晕）
  fx_rule.png        人数那一行的底：一道很淡的深色横带，下沿一根金细线，中间一颗小菱形
python3 Tools/make_fantasy_ui.py → ArtSource/Cover/v6/alpha/"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
A = '/Users/ld/Documents/ChatGPT/WarTable/ArtSource/Cover/v6/alpha/'
GOLD = (214, 172, 98); GOLD_D = (128, 100, 58); IVORY = (244, 236, 214)

def diamond(d, cx, cy, r, fill): d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=fill)

# ---- 卡片
# 11-17 用户：“UI 面板不要这么规整”→ 不做正矩形 + 一圈闭合金线：
#   轮廓是一面不对称的旗幡——上沿左高右低斜切、右上角缺一块、下摆是偏向一侧的燕尾；
#   金线只沿轮廓走几段、中间断开；一条斜的亮带从左上压到右下；一杆长枪斜穿过去，枪尖和枪尾都伸到轮廓外面。
#   另出一张 fx_card_back：同一个轮廓的纯深色剪影，游戏里转个角度垫在后面，露出错开的边。
W, H = 1500, 1150; S = 2
def sil(scale=1.0, dx=0, dy=0):
    pts = [(120, 150), (980, 96), (1040, 40), (1340, 70), (1380, 330), (1345, 360), (1372, 860), (1090, 930), (930, 1080), (800, 950), (150, 990), (96, 560)]
    cx, cy = W / 2, H / 2
    return [((cx + (x - cx) * scale + dx) * S, (cy + (y - cy) * scale + dy) * S) for x, y in pts]
mask = Image.new('L', (W * S, H * S), 0); ImageDraw.Draw(mask).polygon(sil(), fill=255)
m = np.asarray(mask, np.float32) / 255
yy, xx = np.mgrid[0:H * S, 0:W * S].astype(np.float32)
t = (yy / (H * S))[..., None] ** 1.3
col = np.array([9, 11, 17], np.float32) * (1 - t) + np.array([26, 36, 56], np.float32) * t
sash = np.clip(1 - np.abs((xx * 0.55 + yy) / (H * S) - 0.95) / 0.16, 0, 1)[..., None]      # 斜亮带
col = col + sash * np.array([16, 20, 30], np.float32)
col += ((xx.astype(int) % 26 == 0) & (yy.astype(int) % 26 == 0))[..., None] * 9.0
# 同日用户：可以有深蓝互动那一路的美术效果（高级感），配色用我们自己的 → 加几层印刷品的质感（都很淡，叠在深色底上）：
#   · 网点渐变：下半截一片由小到大的圆点（老式印刷的半调网），越往下越大；
#   · 菱格：左边缘一条竖着的菱形格，往里渐隐；
#   · 放射细线：以偏上的一点为圆心的一圈极细的线；
#   · 颗粒：整张撒一层胶片颗粒。
hy = np.clip((yy / (H * S) - 0.55) / 0.45, 0, 1)                                    # 0 → 1，从中间到底
cell = 22.0; dxh = (xx % cell) - cell / 2; dyh = ((yy + (np.floor(xx / cell) % 2) * cell / 2) % cell) - cell / 2
half = (np.sqrt(dxh * dxh + dyh * dyh) < hy * cell * 0.42).astype(np.float32)
col += half[..., None] * np.array([14, 20, 34], np.float32) * (0.5 + 0.5 * hy[..., None])
u = xx / 30.0; v = yy / 30.0
chk = ((np.floor(u + v) + np.floor(u - v)) % 2 == 0).astype(np.float32) * np.clip(1 - xx / (W * S * 0.2), 0, 1) ** 1.5
col += chk[..., None] * np.array([12, 16, 26], np.float32)
ra = np.arctan2(yy - H * S * 0.42, xx - W * S * 0.6); rr_ = np.hypot(yy - H * S * 0.42, xx - W * S * 0.6) / (H * S)
rays_ = (np.cos(ra * 64) > 0.985).astype(np.float32) * np.clip((rr_ - 0.2) / 0.1, 0, 1) * np.clip((0.75 - rr_) / 0.3, 0, 1)
col += rays_[..., None] * np.array([40, 34, 22], np.float32)
col += np.random.default_rng(9).normal(0, 3.2, (H * S, W * S, 1)).astype(np.float32)
card = Image.fromarray(np.concatenate([col.clip(0, 255), (m * 0.95 * 255)[..., None]], -1).astype(np.uint8), 'RGBA')
# 金线：轮廓往里缩一圈取边，再按噪声和几处缺口断开
def inset_edge(px, wd):
    er = mask.filter(ImageFilter.MinFilter(px * 2 + 1)); er2 = er.filter(ImageFilter.MinFilter(wd * 2 + 1))
    return np.clip(np.asarray(er, np.float32) - np.asarray(er2, np.float32), 0, 255) / 255
rng = np.random.default_rng(4)
ang = np.arctan2(yy - H * S / 2, xx - W * S / 2)
gaps = ((np.sin(ang * 3 + 0.6) > 0.72) | (np.sin(ang * 7 + 2.1) > 0.93)).astype(np.float32)                  # 这些方位上线是断开的
e1 = inset_edge(34, 3) * (1 - gaps); e2 = inset_edge(58, 1) * (np.sin(ang * 2 + 1.4) > 0.1)
line = Image.fromarray((np.clip(e1 + e2 * 0.6, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
card.paste(Image.new('RGBA', card.size, GOLD + (255,)), mask=line)
d = ImageDraw.Draw(card)
for x, y in ((1040, 40), (930, 1080), (96, 560)):                 # 三个尖角上各一颗菱形（不是四角对称）
    diamond(d, x * S, y * S, 22 * S // 2 + 8, (12, 14, 20, 255)); diamond(d, x * S, y * S, 15 * S // 2 + 6, GOLD + (255,))
# 长枪：从左下斜穿到右上，两头伸出轮廓
canvas = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0)); canvas.alpha_composite(card); d = ImageDraw.Draw(canvas)
x0, y0, x1, y1 = 30 * S, 1010 * S, 1470 * S, 190 * S
d.line([(x0, y0), (x1, y1)], fill=(96, 76, 44, 255), width=7 * S // 2 + 3); d.line([(x0, y0 - 3), (x1, y1 - 3)], fill=GOLD + (255,), width=3)
import math
a_ = math.atan2(y1 - y0, x1 - x0); ux, uy = math.cos(a_), math.sin(a_); nx, ny = -uy, ux
tipL, tipW = 74 * S, 15 * S
d.polygon([(x1 + ux * tipL * 0.5, y1 + uy * tipL * 0.5), (x1 - ux * tipL * 0.35 + nx * tipW, y1 - uy * tipL * 0.35 + ny * tipW), (x1 - ux * tipL * 0.5, y1 - uy * tipL * 0.5), (x1 - ux * tipL * 0.35 - nx * tipW, y1 - uy * tipL * 0.35 - ny * tipW)], fill=IVORY + (255,), outline=GOLD + (255,))
for k in (0.18, 0.21):                                             # 枪尾两道箍
    cxk, cyk = x0 + (x1 - x0) * k, y0 + (y1 - y0) * k
    d.line([(cxk + nx * 9 * S, cyk + ny * 9 * S), (cxk - nx * 9 * S, cyk - ny * 9 * S)], fill=GOLD + (255,), width=5 * S // 2 + 2)
canvas.resize((W, H), Image.LANCZOS).save(A + 'fx_card.png')
back = np.zeros((H * S, W * S, 4), np.uint8); back[..., :3] = (20, 28, 44); back[..., 3] = (m * 0.9 * 255).astype(np.uint8)
bk = Image.fromarray(back, 'RGBA'); bk.paste(Image.new('RGBA', bk.size, GOLD_D + (255,)), mask=Image.fromarray((inset_edge(20, 2) * (np.sin(ang * 2 - 0.5) > 0.2) * 255).astype(np.uint8)))
bk.resize((W, H), Image.LANCZOS).save(A + 'fx_card_back.png')

# ---- 按钮牌：不对称——左头收尖、右头是燕尾缺口，像一面小旗；金线只描上沿和尖头
def plaque(name, hi):
    W, H = 900, 240; S = 2
    im = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    w, h = W * S, H * S; px, py, tip = 56 * S, 46 * S, 76 * S
    shape = [(px + tip, py + 8 * S), (w - px, py - 6 * S), (w - px - 46 * S, h // 2 + 4 * S), (w - px - 8 * S, h - py + 4 * S), (px + tip + 10 * S, h - py), (px, h // 2 + 6 * S)]
    if hi:
        glow = Image.new('L', (w, h), 0); ImageDraw.Draw(glow).polygon(shape, fill=255); glow = glow.filter(ImageFilter.GaussianBlur(26))
        im.paste(Image.new('RGBA', (w, h), (255, 214, 140, 255)), mask=glow.point(lambda v: int(v * 0.55)))
        g = np.zeros((h, w, 4), np.uint8); tt = (np.arange(h)[:, None] / h)
        g[..., 0] = 250 - tt * 26; g[..., 1] = 240 - tt * 52; g[..., 2] = 214 - tt * 96; g[..., 3] = 255
        mk = Image.new('L', (w, h), 0); ImageDraw.Draw(mk).polygon(shape, fill=255)
        im.paste(Image.fromarray(g, 'RGBA'), mask=mk); d = ImageDraw.Draw(im)
        d.line([shape[5], shape[0], shape[1]], fill=(150, 108, 44, 255), width=6, joint='curve')
    else:
        d.polygon(shape, fill=(13, 16, 24, 240))
        d.line([shape[4], shape[5], shape[0], shape[1]], fill=GOLD + (255,), width=6, joint='curve')          # 只描左下、尖头、上沿
        d.line([(shape[4][0] + 60 * S, shape[4][1] - 9 * S), (shape[3][0] - 70 * S, shape[3][1] - 9 * S)], fill=GOLD_D + (255,), width=3)   # 下沿里面一小段细线
        diamond(d, px, h // 2 + 6 * S, 10 * S, GOLD + (255,))
    im.resize((W, H), Image.LANCZOS).save(A + name + '.png')
plaque('fx_plaque', False); plaque('fx_plaque_hi', True)

# ---- 四芒金星
S = 512; yy, xx = np.mgrid[0:S, 0:S].astype(np.float32); x = (xx - S / 2) / (S / 2); y = (yy - S / 2) / (S / 2)
star = np.clip(1 - (np.abs(x) ** 0.5 + np.abs(y) ** 0.5) ** 2 / 1.05, 0, 1)      # 星形（四个尖）
core = np.clip(star * 6, 0, 1)
r = np.sqrt(x * x + y * y); halo = np.exp(-(r / 0.5) ** 2) * 0.5
a = np.clip(core + halo * (1 - core), 0, 1)
shade = 0.75 + 0.25 * np.clip(star * 2.2, 0, 1) + 0.12 * (-(x + y)) * core
rgb = np.stack([np.full_like(a, 255) * shade, 214 * shade, 128 * shade], -1) * core[..., None] + np.array([255, 206, 120]) * (1 - core[..., None])
Image.fromarray(np.concatenate([rgb.clip(0, 255), a[..., None] * 255], -1).astype(np.uint8), 'RGBA').save(A + 'fx_star.png')

# ---- 大字背后的柔光：光晕 + 一圈放射细线
S = 1024; yy, xx = np.mgrid[0:S, 0:S].astype(np.float32); x = (xx - S / 2) / (S / 2); y = (yy - S / 2) / (S / 2)
r = np.sqrt(x * x + y * y); ang = np.arctan2(y, x)
halo = np.exp(-(r / 0.42) ** 2) * 0.75
rays = (np.cos(ang * 36) > 0.965).astype(np.float32) * np.clip((r - 0.34) / 0.08, 0, 1) * np.clip((0.92 - r) / 0.3, 0, 1) * 0.5
ringl = np.clip(1 - np.abs(r - 0.6) / 0.006, 0, 1) * 0.45 + np.clip(1 - np.abs(r - 0.66) / 0.004, 0, 1) * 0.25
a = np.clip(halo + rays + ringl, 0, 1)
Image.fromarray(np.concatenate([np.full((S, S, 3), 255.0) * np.array([1.0, 0.86, 0.6]), a[..., None] * 255], -1).astype(np.uint8), 'RGBA').save(A + 'fx_glow.png')

# ---- 人数行的底
W, H = 900, 160; im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
band = Image.new('L', (W, H), 0); ImageDraw.Draw(band).rectangle((40, 30, W - 40, H - 44), fill=150); band = band.filter(ImageFilter.GaussianBlur(14))
im.paste(Image.new('RGBA', (W, H), (8, 10, 16, 255)), mask=band); d = ImageDraw.Draw(im)
d.line([(70, H - 36), (W - 70, H - 36)], fill=GOLD_D + (255,), width=3); diamond(d, W // 2, H - 36, 9, GOLD + (255,))
im.save(A + 'fx_rule.png')

sheet = Image.new('RGB', (1500, 900), (96, 104, 92))
for n, (px, py, w) in {'fx_card_back': (40, 60, 700), 'fx_card': (10, 10, 700), 'fx_plaque': (730, 10, 450), 'fx_plaque_hi': (730, 140, 450), 'fx_star': (1200, 10, 200), 'fx_glow': (730, 280, 420), 'fx_rule': (10, 540, 450)}.items():
    im = Image.open(A + n + '.png'); im = im.resize((w, int(w * im.height / im.width)), Image.LANCZOS); sheet.paste(im, (px, py), im)
sheet.save('/tmp/sp2/fantasy_ui.jpg', quality=88); print('ok')
