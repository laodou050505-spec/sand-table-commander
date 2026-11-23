"""深空风格的天空（沉浸模式用）：NASA SVS “Deep Star Maps 2020”（公有领域，https://svs.gsfc.nasa.gov/4851）的全天星图 + 程序画的一颗条纹气态巨行星。
先用 Blender 把 EXR 读成数组（/tmp/sp2/sky/exr2npy.py → starmap.npy），再跑本脚本 → Assets/_WarTable/Art/Sky/sky_space.jpg（4096×2048 全景）。
行星画在方位 0（全景图正中 = 天空盒 −Z 方向，沉浸模式把它转到玩家正前方）、仰角 27°，角半径 17°；光从左上方来，右下是暗面。"""
import numpy as np
from PIL import Image, ImageFilter
a = np.load('/tmp/sp2/sky/starmap.npy').astype(np.float32)
H, W = a.shape[:2]
img = 1 - np.exp(-np.clip(a - 0.006, 0, None) * 4.2)   # 提亮银河，亮星不过曝；先减掉一层底，不然满天都是灰蒙蒙的弱星
img = img ** (1 / 2.2)
img[..., 2] *= 1.06; img[..., 0] *= 0.97          # 略偏冷
# 方向向量
v, u = np.meshgrid((np.arange(H) + 0.5) / H, (np.arange(W) + 0.5) / W, indexing='ij')
lon = (u - 0.5) * 2 * np.pi; lat = (0.5 - v) * np.pi
d = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], -1)
el, R = np.radians(27), np.radians(17)
c = np.array([0, np.sin(el), np.cos(el)], np.float32)
right = np.array([1, 0, 0], np.float32); up = np.cross(c, right); up /= np.linalg.norm(up)
x = d @ right; y = d @ up; z = d @ c
ang = np.arccos(np.clip(z, -1, 1))
px = x / np.sin(R); py = y / np.sin(R); r2 = px * px + py * py
disc = (r2 < 1) & (z > 0)
nz = np.sqrt(np.clip(1 - r2, 0, 1))
# 条纹：按“行星纬度”（自转轴倾斜 18°）分带，带边用噪声扰动
t = np.radians(18); ly = py * np.cos(t) - px * np.sin(t); lx = px * np.cos(t) + py * np.sin(t)
rng = np.random.default_rng(7)
noise = np.asarray(Image.fromarray((rng.random((64, 256)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(6)), np.float32) / 255
band = np.sin(ly * 11 + np.sin(lx * 5 + noise * 6) * 0.5 + noise * 2.5) * 0.5 + 0.5
band2 = np.sin(ly * 27 + noise * 5) * 0.5 + 0.5
c1 = np.array([0.80, 0.62, 0.42]); c2 = np.array([0.48, 0.30, 0.22]); c3 = np.array([0.90, 0.80, 0.66])
col = c2 + (c1 - c2) * band[..., None]; col = col + (c3 - col) * (band2[..., None] * 0.25)
L = np.array([-0.78, 0.5, 0.22]); L /= np.linalg.norm(L)
lit = np.clip((px * L[0] + py * L[1] + nz * L[2]) * 1.25 + 0.08, 0, 1) ** 0.9
planet = col * (0.03 + 0.97 * lit[..., None]) * (0.75 + 0.25 * nz[..., None])   # 边缘略暗
edge = np.clip((1 - np.sqrt(r2)) / 0.02, 0, 1)[..., None]                       # 边缘 2% 抗锯齿
m = disc[..., None] * edge
img = img * (1 - m) + planet * m
# 亮面一侧一圈很淡的大气辉光
glow = np.exp(-np.clip(np.sqrt(r2) - 1, 0, None) / 0.035) * (~disc) * np.clip(px * L[0] + py * L[1] + 0.3, 0, 1)
img += glow[..., None] * np.array([0.5, 0.36, 0.24]) * 0.5
# 地平线以下：压暗（被大地挡住；露出来的地方是黑的）
t2 = np.clip((v - 0.5) / 0.04, 0, 1)[..., None]; img = img * (1 - 0.92 * t2)
Image.fromarray((img.clip(0, 1) * 255).astype(np.uint8)).save('Assets/_WarTable/Art/Sky/sky_space.jpg', quality=92)
Image.fromarray((img.clip(0, 1) * 255).astype(np.uint8)).resize((1600, 800)).save('/tmp/sp2/sky/space_prev.jpg')
print('ok')
