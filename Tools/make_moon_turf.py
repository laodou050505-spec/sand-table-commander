"""未来时代的地表贴图（月尘）：从哥特的焦土贴图 ash_turf（可平铺的细颗粒）换色得到，颗粒大小和其它时代一致。
去色 → 提亮成冷灰（略带蓝）→ 压低对比，再叠一层可平铺的大块深浅（月壤的明暗斑）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_moon_turf.py → Assets/_WarTable/Art/Textures/moon_turf.png"""
import numpy as np
from PIL import Image
T = '/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Textures/'
a = np.asarray(Image.open(T + 'ash_turf.png').convert('RGB')).astype(np.float64) / 255
g = a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11
g = (g - g.mean()) / (g.std() + 1e-6)
N = g.shape[0]; rng = np.random.default_rng(3)
fy = np.fft.fftfreq(N)[:, None] * N; fx = np.fft.fftfreq(N)[None, :] * N
blot = np.real(np.fft.ifft2((rng.normal(size=(N, N)) + 1j * rng.normal(size=(N, N))) * np.exp(-(fx ** 2 + fy ** 2) / 4.0 ** 2)))   # 低频：天然首尾相接
blot = (blot - blot.mean()) / blot.std()
v = np.clip(0.56 + 0.075 * g + 0.035 * blot, 0, 1)
out = np.stack([v * 0.97, v * 0.985, v * 1.03], -1)
Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8)).save(T + 'moon_turf.png')
print('moon_turf', out.reshape(-1, 3).mean(0).round(3))
