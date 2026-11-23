"""宣传图合成：游戏层（Unity 黑底 / 白底两张）叠到房间照片上。都在线性光空间里算。
用法：promo_compose.py <名字> <背景图> <模式: table|dim|blur> <输出.jpg>  （黑白底原图在环境变量 SHOTS 指的目录，默认 /tmp/sp7/promo）
房间照片：ArtSource/Promo/room_study_1119.png（白模 room_blockout_1119.png → banana_pro 图生图）；截图助手 Assets/_WarTable/Editor/PromoShots.cs
  table：背景是同机位的房间照片，沙盘下面加接触阴影
  dim  ：封面——房间按游戏里的遮罩压暗 90%（带一点蓝）
  blur ：近景——背景只在画面边缘露一点，用房间照片做强模糊（像浅景深）"""
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as ndi

import os
name, bgp, mode, out = sys.argv[1:5]
SHOTS = os.environ.get('SHOTS', '/tmp/sp7/promo')
W, H = 2560, 1440


def lin(a):
    a = a.astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def srgb(a):
    a = np.clip(a, 0, 1)
    return np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)


k = lin(np.asarray(Image.open(f'{SHOTS}/{name}_k.png').convert('RGB')))
w = lin(np.asarray(Image.open(f'{SHOTS}/{name}_w.png').convert('RGB')))
alpha = np.clip(1 - (w - k).mean(-1), 0, 1)[..., None]           # 白底 − 黑底 = 1 − 不透明度（线性空间里成立）
bg_img = Image.open(bgp).convert('RGB')
# 背景按 16:9 铺满（居中裁切）
s = max(W / bg_img.width, H / bg_img.height)
bg_img = bg_img.resize((round(bg_img.width * s), round(bg_img.height * s)), Image.LANCZOS)
l, t = (bg_img.width - W) // 2, (bg_img.height - H) // 2
bg_img = bg_img.crop((l, t, l + W, t + H))
if mode == 'blur':
    bg_img = bg_img.filter(ImageFilter.GaussianBlur(28))
bg = lin(np.asarray(bg_img))
if mode == 'dim':
    bg = bg * 0.04 + 0.9 * lin(np.array([[[0.015 * 255, 0.016 * 255, 0.03 * 255]]]))
if mode == 'table':
    a = alpha[..., 0]
    sh = ndi.gaussian_filter(ndi.shift(a, (14, 0), order=1), 18)                  # 沙盘压在桌上的软阴影（往下偏一点）
    bg = bg * (1 - 0.45 * np.clip(sh, 0, 1))[..., None]
res = k + bg * (1 - alpha)
img = Image.fromarray((srgb(res) * 255 + 0.5).astype(np.uint8))
img.save(out, quality=93)
print(out, img.size, '不透明占比 %.2f' % float((alpha > 0.5).mean()))
