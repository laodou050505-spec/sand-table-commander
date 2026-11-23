"""结算板 / 暂停菜单的立体字（11-17 用户：全游戏 UI 改成西式奇幻、要高级、不要中国风；文字要更有格调，“再战”显得傻）。
原来是马善政毛笔字（边缘揉软像毛笔）→ 换成思源宋体 Heavy（锐利的衬线），英文 Cinzel 粗体；措辞也换了。文件名不变（搭建代码不用改）：
  word_sheng 胜 / res_victory VICTORY      word_bai 败 / res_defeat DEFEAT
  word_zaizhan 再启战端 / menu_again FIGHT ANEW      word_huiying 收兵归营 / menu_camp WITHDRAW      word_jixu 重返战局 / menu_resume RESUME
用法：python3 Tools/make_ui_words.py   （出字稿 ArtSource/Cover/ui_words/*.png，再调 Tools/cover_v5_meshes.sh 挤成 Art/Models/UI/*.fbx）"""
import pathlib, subprocess, os
from PIL import Image, ImageDraw, ImageFont
R = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable'); F = R / 'ArtSource/Fonts'; OUT = R / 'ArtSource/Cover/ui_words'
OUT.mkdir(parents=True, exist_ok=True)
WORDS = [('sheng', '胜', 'res_victory', 'VICTORY'), ('bai', '败', 'res_defeat', 'DEFEAT'),
         ('zaizhan', '再启战端', 'menu_again', 'FIGHT ANEW'), ('huiying', '收兵归营', 'menu_camp', 'WITHDRAW'), ('jixu', '重返战局', 'menu_resume', 'RESUME'),
         ('jinjun', '继续进军', 'menu_march', 'MARCH ON')]   # 11-20 战役：打赢后结算板的“回营”换成“继续进军”

def zh(text, path):
    f = ImageFont.truetype(str(F / 'SourceHanSerifCN-Heavy.otf'), 420)
    gap = 70 if len(text) <= 2 else 36
    im = Image.new('L', (520 * len(text) + 200, 900), 255); d = ImageDraw.Draw(im)      # 画布要比字高出一截：裁图越界的部分会被补成黑色，挤出来就是字下面一条杠（11-17 踩过）
    bbs = [d.textbbox((0, 0), ch, font=f) for ch in text]
    total = sum(b[2] - b[0] for b in bbs) + gap * (len(text) - 1); x = (im.width - total) / 2
    for ch, b in zip(text, bbs):
        d.text((x - b[0], 200), ch, font=f, fill=0); x += b[2] - b[0] + gap     # 同一条基线
    bb = Image.eval(im, lambda v: 255 - v).getbbox()
    assert bb[1] >= 50 and bb[3] + 50 <= im.height and bb[0] >= 60 and bb[2] + 60 <= im.width, ('裁图越界', text, bb, im.size)
    im.crop((bb[0] - 60, bb[1] - 50, bb[2] + 60, bb[3] + 50)).save(path)

def en(text, path):
    f = ImageFont.truetype(str(F / 'Cinzel.ttf'), 150)
    try: f.set_variation_by_axes([800])
    except Exception: pass
    im = Image.new('L', (2600, 320), 255); d = ImageDraw.Draw(im); x = 60
    for ch in text:
        d.text((x, 60), ch, font=f, fill=0, stroke_width=2, stroke_fill=0); x += d.textlength(ch, font=f) + (38 if ch != ' ' else 30)
    bb = Image.eval(im, lambda v: 255 - v).getbbox()
    assert bb[1] >= 50 and bb[3] + 50 <= im.height and bb[0] >= 50 and bb[2] + 50 <= im.width, ('裁图越界', text, bb, im.size)
    im.crop((bb[0] - 50, bb[1] - 50, bb[2] + 50, bb[3] + 50)).save(path)

import sys
ONLY = set(sys.argv[1:])   # 只出这几个（键名），不给就全出
specs = []
for key, z, ekey, e in WORDS:
    if ONLY and key not in ONLY: continue
    zh(z, OUT / f'word_{key}.png'); en(e, OUT / f'{ekey}.png')
    k = 1.0 if len(z) == 1 else 2 / len(z)     # 厚度 / 倒角相对整张字稿的宽度：字多的词按比例减
    specs += [f'word_{key}:ArtSource/Cover/ui_words/word_{key}.png:{len(z)}:{0.05 * k:g}:{0.006 * k:g}', f'{ekey}:ArtSource/Cover/ui_words/{ekey}.png:{len(e.split())}:0.012:0.0015']
env = dict(os.environ, COVER_OUT='Assets/_WarTable/Art/Models/UI')
subprocess.run(['bash', str(R / 'Tools/cover_v5_meshes.sh')] + specs, env=env, check=False)
print('words', len(specs))
