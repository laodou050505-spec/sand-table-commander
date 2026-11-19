"""标题页的出品署名（11-18，规则见记忆 tripo-brand-credit）：官方 TRIPO 标志在前、署名文字在后，印在一条藏青撕边纸条上（和游戏里其余界面同一套纸片；封面没有底板，纸条直接悬在压暗的房间里）。
  credit_zh.png  [TRIPO 标志] | Tripo XR 出品         credit_en.png  [TRIPO 标志] | Tripo XR Developers
标志用官方原图（ArtSource/Brand/raw/tripo_logo_h_800_white.png，深色底用的白字版）：只做等比缩小，不改色、不拉伸、不加效果，四周留白。
文字用本游戏界面的正文字体：中文思源宋体 Bold，英文 Cinzel。
python3 Tools/make_brand_credit.py → Assets/_WarTable/Art/Brand/credit_<zh|en>.png（不压缩，导入设置由 CoverBuilder.BrandTex 定）"""
import os, sys, numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import collage_kit as ck
R = '/Users/ld/Documents/ChatGPT/WarTable/'; F = R + 'ArtSource/Fonts/'; OUT = R + 'Assets/_WarTable/Art/Brand/'
logo = Image.open(R + 'ArtSource/Brand/raw/tripo_logo_h_800_white.png').convert('RGBA')
K = 0.62; logo = logo.resize((int(logo.width * K), int(logo.height * K)), Image.LANCZOS)      # 等比缩小
zh = ImageFont.truetype(F + 'SourceHanSerifCN-Bold.otf', 60)
en = ImageFont.truetype(F + 'Cinzel.ttf', 50)
try: en.set_variation_by_axes([700])
except Exception: pass
PAD, H = 14, 200
for lang, text, font in (('zh', 'Tripo XR 出品', zh), ('en', 'Tripo XR Developers', en)):
    tw = ImageDraw.Draw(Image.new('L', (8, 8))).textlength(text, font=font)
    W = int(PAD * 2 + 34 + logo.width + 26 + 34 + tw + 78)
    rgb, a, (x0, y0, x1, y1) = ck.real_strip(W, H, ck.NAVY, pad=PAD, flip=(lang == 'en'))
    rgb = ck.corner_dots(rgb, 9, rgb.shape[1] * 1.0, rgb.shape[0] * 1.0, rgb.shape[1] * 0.22, rgb.shape[0] * 1.2, (44, 62, 104), 0.5)
    im = ck.finish(rgb, a, PAD); d = ImageDraw.Draw(im); cy = H // 2
    lx = x0 + 34; im.alpha_composite(logo, (lx, cy - logo.height // 2)); d = ImageDraw.Draw(im)
    dx = lx + logo.width + 26
    d.line([(dx, cy - 34), (dx, cy + 34)], fill=ck.GOLD_D + (255,), width=2)                 # 标志和文字之间一道细金线
    d.text((dx + 34, cy + (2 if lang == 'zh' else 4)), text, font=font, fill=ck.IVORY + (255,), anchor='lm')
    im.save(OUT + f'credit_{lang}.png'); print(lang, im.size)
