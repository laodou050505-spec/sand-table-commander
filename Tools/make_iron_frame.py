"""沙盘边框的贴图（11-16 用户：所有交互、场景、视觉都往深空那种西方科幻奇幻风格靠）。
原来是胡桃木框 + 一条雕花带，和新做的炮铜指挥台放在一起不搭 → 换成“铁甲框”：
  iron_plate.png  1024×1024 可平铺：发黑的炮铜装甲板，横向拉丝，每半张一条拼缝，缝的交点上有铆钉，少量划痕和磨亮的边
  rivet_trim.png  2048×512 可横向平铺（4:1，一段 11.2 cm）：上下两道黄铜压条，中间凹下去的黑铁带上一排菱形钉和 V 形刻线
边框的着色器是 MiniLit（不是 PBR、不吃法线图）→ 凹凸的明暗直接画进颜色里（光从上方来）。
python3 Tools/make_iron_frame.py → Assets/_WarTable/Art/Textures/"""
import numpy as np
from PIL import Image, ImageFilter
OUT = '/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Textures/'
rng = np.random.default_rng(5)

def blur(a, r, wrap=True):
    """模糊（平铺方向上环绕，接缝处不留痕）。"""
    h, w = a.shape[:2]
    if wrap: a = np.tile(a, (3, 3) + (1,) * (a.ndim - 2))
    lo, hi = float(a.min()), float(a.max()); n = (a - lo) / max(1e-6, hi - lo)
    if n.ndim == 2: o = np.asarray(Image.fromarray((n * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32) / 255
    else: o = np.asarray(Image.fromarray((n * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32) / 255
    o = o * (hi - lo) + lo
    return o[h:2 * h, w:2 * w] if wrap else o

def shade(height, strength):
    """高度图 → 明暗（光从上方偏左来）：朝上的坡亮，朝下的坡暗。"""
    gy = np.roll(height, 1, 0) - np.roll(height, -1, 0); gx = np.roll(height, 1, 1) - np.roll(height, -1, 1)
    return np.clip(1 + (gy * 0.9 + gx * 0.35) * strength, 0.35, 1.9)

def brushed(h, w, amount):
    rows = rng.normal(0, 1, (h, 1)).astype(np.float32); rows = (rows + np.roll(rows, 1, 0) + np.roll(rows, -1, 0)) / 3
    fine = blur(rng.random((h, w)).astype(np.float32), 0.7) - 0.5
    long = np.asarray(Image.fromarray((rng.random((h, w // 32)) * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR), np.float32) / 255 - 0.5
    long = (long + np.roll(long, w // 2, 1)[:, ::-1] * 0) ; return (rows * 0.55 + fine * 0.5 + long * 0.45) * amount

# ------------------------------------------------------------ 装甲板
S = 1024
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
st = brushed(S, S, 1.0)
cloud = blur(rng.random((S // 32, S // 32)).astype(np.float32).repeat(32, 0).repeat(32, 1), 40)[..., None]
base = np.array([52, 56, 66], np.float32) * (0.82 + 0.36 * cloud) + st[..., None] * 10
# 拼缝：每半张一条横缝、一条竖缝（竖缝隔行错开半块，像砌出来的装甲板）
P = S // 2
seam_h = np.minimum(yy % P, P - yy % P)
row = (yy // P).astype(int)
xs = (xx + (row % 2) * (P // 2)) % P
seam_v = np.minimum(xs, P - xs)
seam = np.minimum(seam_h, seam_v)
groove = np.clip(1 - seam / 3.0, 0, 1)                      # 缝本身（3 px 宽的暗线）
lip = np.clip(1 - np.abs(seam - 6) / 3.0, 0, 1)             # 缝边上一道磨亮的倒角
height = -groove * 1.0 + lip * 0.25
# 铆钉：每块板的四个角里面一点
riv = np.zeros((S, S), np.float32)
for r_ in range(2):
    for c_ in range(2):
        for dx, dy in ((26, 26), (P - 26, 26), (26, P - 26), (P - 26, P - 26)):
            cx = (c_ * P + dx - (r_ % 2) * (P // 2)) % S; cy = r_ * P + dy
            d = np.sqrt((np.minimum(np.abs(xx - cx), S - np.abs(xx - cx))) ** 2 + (yy - cy) ** 2)
            riv = np.maximum(riv, np.clip(1 - (d / 9.0) ** 2, 0, 1) ** 0.5 * (d < 9))
height += riv * 1.6
# 划痕：稀疏的斜长线
scr = np.zeros((S, S), np.float32)
for _ in range(26):
    x0, y0 = rng.integers(0, S, 2); L = rng.integers(60, 260); a = rng.normal(0.15, 0.5)
    t = np.arange(L); px = ((x0 + t * np.cos(a)).astype(int)) % S; py = ((y0 + t * np.sin(a)).astype(int)) % S
    scr[py, px] = rng.uniform(0.4, 1.0)
scr = blur(scr, 0.8)
col = base * shade(blur(height, 1.2), 2.2)[..., None]
col = col * (1 - 0.6 * groove[..., None]) + np.array([150, 150, 146], np.float32) * (lip[..., None] * 0.35)
col = col * (1 - (riv[..., None] > 0)) + (np.array([92, 96, 104], np.float32) * shade(blur(riv * 1.6, 1.0), 3.0)[..., None]) * (riv[..., None] > 0)
col += scr[..., None] * 60
ao = blur(np.maximum(groove, (riv > 0) * 0.0), 5)[..., None]; col *= (1 - 0.3 * ao)
Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(OUT + 'iron_plate.png')

# ------------------------------------------------------------ 铆钉压条带
W, H = 2048, 512
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
st = brushed(H, W, 1.0)
edge = np.minimum(yy, H - 1 - yy)
railW = 70.0
rail = np.clip((railW - edge) / 10.0, 0, 1)                 # 上下两道黄铜压条
recess = 1 - rail
iron = np.array([30, 32, 38], np.float32) * (0.9 + 0.2 * blur(rng.random((H // 16, W // 16)).astype(np.float32).repeat(16, 0).repeat(16, 1), 20)[..., None]) + st[..., None] * 7
brass = np.array([196, 150, 76], np.float32) * (0.9 + 0.16 * blur(rng.random((H // 16, W // 16)).astype(np.float32).repeat(16, 0).repeat(16, 1), 24)[..., None]) + st[..., None] * 12
height = rail * 1.0
# 菱形钉：一段里 4 颗，居中
stud = np.zeros((H, W), np.float32)
n = 4; pitch = W / n
for i in range(n):
    cx = pitch * (i + 0.5); d = np.abs(xx - cx) / 62.0 + np.abs(yy - H / 2) / 62.0
    stud = np.maximum(stud, np.clip(1 - d, 0, 1))
height += stud * 1.5
# V 形刻线：两颗钉之间一组三道
chev = np.zeros((H, W), np.float32)
for i in range(n):
    cx = pitch * i
    for k in (-1, 0, 1):
        x0 = cx + k * 34
        dd = np.abs((np.minimum(np.abs(xx - x0), W - np.abs(xx - x0))) - np.abs(yy - H / 2) * 0.55 + 0)   # “>” 形：x 随离中线的距离往外偏
        dd = np.abs((xx - x0 + W / 2) % W - W / 2 + np.abs(yy - H / 2) * 0.5 - 40)
        chev = np.maximum(chev, np.clip(1 - dd / 5.0, 0, 1) * (np.abs(yy - H / 2) < 118))
height -= chev * 0.7 * recess
col = iron * recess[..., None] + brass * rail[..., None]
studMask = (stud > 0.02)[..., None]
col = col * (1 - studMask) + brass * 1.05 * studMask
col = col * shade(blur(height, 1.5), 2.6)[..., None]
col = col * (1 - 0.55 * (chev * recess)[..., None])
# 压条脚下、钉脚下压暗
ao = blur(np.clip(rail + stud * 1.2, 0, 1), 9)[..., None] * recess[..., None] * (1 - studMask)
col *= (1 - 0.45 * ao)
# 压条上每隔一小段一颗小螺钉
for i in range(8):
    cx = W / 8 * (i + 0.5)
    for cy in (34, H - 34):
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2); m = (d < 13)[..., None]
        dome = np.clip(1 - (d / 13.0) ** 2, 0, 1) ** 0.5
        col = col * (1 - m) + (np.array([150, 112, 56], np.float32) * (0.75 + 0.5 * dome[..., None]) * (1 + ((cy - yy) / 13.0)[..., None] * 0.25)) * m
Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(OUT + 'rivet_trim.png')
sheet = Image.new('RGB', (1024 + 8 + 1024, 520 + 260), (30, 30, 30))
sheet.paste(Image.open(OUT + 'iron_plate.png').resize((512, 512)), (0, 0)); sheet.paste(Image.open(OUT + 'iron_plate.png').resize((512, 512)), (512, 0))
t = Image.open(OUT + 'rivet_trim.png').resize((1024, 256)); sheet.paste(t, (1032, 0)); sheet.paste(t, (1032, 256)); sheet.paste(t, (0, 520))
sheet.save('/tmp/sp2/iron_frame.jpg', quality=88); print('ok')
