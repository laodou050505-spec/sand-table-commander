"""手的图标（捏合、双手挪沙盘、手掌、握拳、下按、回招）：铜版画重绘稿 → 两种成品（11-17：全套界面统一成西式铜版画，不要毛笔味）。
  hand_<名>_line.png  只有象牙白的线稿（透明底）——说明牌的行首、把手圆钮上用
  hand_<名>.png       一枚藏青圆纸片（边上一圈铜版画刻度）+ 线稿——教学里单独飘在手边时用，有个底托着才看得清
原稿：ArtSource/UI/gen/hand_<名>_e/01_generated_image_url.png（Tools/engrave_icons.py hand_<名> 出的白底黑线）。
python3 Tools/make_hand_icons.py → ArtSource/Cover/v6/alpha/"""
import os, sys, numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as ndi
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import collage_kit as ck
R = '/Users/ld/Documents/ChatGPT/WarTable/'; A = R + 'ArtSource/Cover/v6/alpha/'; G = R + 'ArtSource/UI/gen/'; S = 512
for n in ('hand_pinch', 'hand_tray', 'hand_palm', 'hand_fist', 'hand_press', 'hand_beckon'):
    p = G + n + '_e/01_generated_image_url.png'
    if not os.path.exists(p): print('缺', n); continue
    src = np.asarray(Image.open(p).convert('L'), np.float32); paper = np.percentile(src, 85)
    ink = np.clip((paper - src) / max(1.0, paper - src.min()), 0, 1); ink = np.clip((ink - 0.18) / 0.6, 0, 1)
    lab, k = ndi.label(ink > 0.1)                                                     # 去掉零星噪点
    if k > 1:
        area = ndi.sum(ink > 0.1, lab, range(1, k + 1)); big = area.max()
        for li in range(1, k + 1):
            if area[li - 1] < big * 0.002: ink[lab == li] = 0
    ys, xs = np.where(ink > 0.3); ink = ink[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = ink.shape; kk = S * 0.92 / max(h, w)
    m = Image.fromarray((ink * 255).astype(np.uint8)).resize((max(1, int(w * kk)), max(1, int(h * kk))), Image.LANCZOS)
    full = Image.new('L', (S, S), 0); full.paste(m, ((S - m.width) // 2, (S - m.height) // 2))
    full = full.point(lambda v: min(255, int(v * 2.4)))      # 铜版画的线很淡：只提亮、不加粗（加粗 5 像素试过，线糊成一团，又像毛笔了）
    line = Image.new('RGBA', (S, S), ck.IVORY + (255,)); line.putalpha(full); line.save(A + n + '_line.png')
    disc = ck.ring_disc(S, ck.NAVY, seed=6, field=0.68)
    small = full.resize((int(S * 0.6), int(S * 0.6)), Image.LANCZOS).point(lambda v: min(255, int(v * 1.3)))
    col = Image.new('RGBA', small.size, ck.IVORY + (255,)); col.putalpha(small)
    disc.alpha_composite(col, ((S - small.width) // 2, (S - small.height) // 2)); disc.save(A + n + '.png'); print(n, 'ok')
