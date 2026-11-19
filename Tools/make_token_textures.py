"""令牌的质感贴图（11-12 用户：令牌缺少质感）→ Assets/_WarTable/Art/Textures/Tokens/
  lacquer_albedo.png  漆面明暗（灰度，乘在漆色上）：顺着牌身的刷痕 + 漆层深浅 + 少量细小磨痕
  lacquer_normal.png  漆面法线：刷痕起伏 + 细橘皮纹
  lacquer_mask.png    金属 / 光滑度（URP：R 金属、A 光滑度）：刷痕凹处、磨痕处更哑
  gold_albedo.png / gold_normal.png / gold_mask.png  嵌金：拉丝 + 一点点锤痕
都是可平铺的（周期噪声）。用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_token_textures.py"""
import os
import numpy as np
from PIL import Image

OUT = '/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Textures/Tokens/'
os.makedirs(OUT, exist_ok=True)
N = 1024
rng = np.random.default_rng(1111)


def tile_noise(scale_x, scale_y, octaves=4, seed=0):
    """周期（可平铺）噪声：在频域里只保留低频，逆变换回来天然首尾相接。scale = 主要纹理的周期数。"""
    r = np.random.default_rng(seed)
    out = np.zeros((N, N))
    fy = np.fft.fftfreq(N)[:, None] * N; fx = np.fft.fftfreq(N)[None, :] * N
    for o in range(octaves):
        k = 2 ** o
        spec = (r.normal(size=(N, N)) + 1j * r.normal(size=(N, N)))
        env = np.exp(-((fx / (scale_x * k)) ** 2 + (fy / (scale_y * k)) ** 2))
        out += np.real(np.fft.ifft2(spec * env)) / k
    out -= out.mean(); out /= out.std() + 1e-9
    return out


def normal_from(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-gx, gy, np.ones_like(h)], -1)   # Unity 是 OpenGL 方向（+Y 朝上）
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)


def save_gray(a, name):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).convert('RGB').save(OUT + name)


def save_mask(metal, smooth, name):
    m = np.zeros((N, N, 4), np.uint8)
    m[..., 0] = (np.clip(metal, 0, 1) * 255).astype(np.uint8); m[..., 3] = (np.clip(smooth, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(m).save(OUT + name)


# ── 漆面：竖向刷痕（沿牌身长度方向 = 贴图 v 方向拉长）+ 漆层深浅 + 细磨痕
brush = tile_noise(110, 4, 3, 1)          # 横向密、纵向长：一道道刷痕
layer = tile_noise(3, 3, 3, 2)             # 大块的漆层厚薄
peel = tile_noise(90, 90, 2, 3)            # 细橘皮
scratch = np.zeros((N, N))
for _ in range(70):                        # 细小磨痕：随机短直线
    x0, y0 = rng.integers(0, N, 2); ang = rng.uniform(0, np.pi); ln = rng.integers(20, 90)
    t = np.arange(ln); xs = (x0 + np.cos(ang) * t).astype(int) % N; ys = (y0 + np.sin(ang) * t).astype(int) % N
    scratch[ys, xs] = rng.uniform(0.4, 1.0)
alb = 0.93 + 0.012 * brush + 0.035 * layer + 0.05 * scratch   # 11-12 第二版：第一版刷痕太重，看着像树皮 → 刷痕细、浅，漆面以光为主
save_gray(alb, 'lacquer_albedo.png')
Image.fromarray(normal_from(brush * 0.25 + peel * 0.12 - scratch * 0.5, 0.7)).save(OUT + 'lacquer_normal.png')
save_mask(np.zeros((N, N)), 0.82 + 0.05 * layer - 0.06 * np.clip(brush, 0, None) - 0.35 * scratch, 'lacquer_mask.png')

# ── 嵌金：横向拉丝 + 轻微锤痕
brushed = tile_noise(2, 420, 2, 4)
hammer = tile_noise(14, 14, 2, 5)
save_gray(0.95 + 0.012 * brushed + 0.025 * hammer, 'gold_albedo.png')   # 第一版拉丝太粗像条纹 → 细密、浅
Image.fromarray(normal_from(brushed * 0.2 + hammer * 0.5, 0.6)).save(OUT + 'gold_normal.png')
save_mask(np.ones((N, N)), 0.66 + 0.06 * brushed - 0.05 * np.abs(hammer), 'gold_mask.png')
print('ok', sorted(os.listdir(OUT)))
