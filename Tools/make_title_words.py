"""标题页的游戏名立体字（11-18 用户：游戏改名 Tripo craft）：TRIPO / CRAFT 两个词，Cinzel 最粗一档，各挤成一个 FBX。
python3 Tools/make_title_words.py → 字稿 ArtSource/Cover/title_1118/*.png → Assets/_WarTable/Art/Models/Title/v6/en_tripo.fbx、en_craft.fbx
（挤出走 Tools/cover_v5_meshes.sh；CoverBuilder 版式 5 的中文版和英文版标题都用这两个词——新名字没有中文名）"""
import pathlib, subprocess, os
from PIL import Image, ImageDraw, ImageFont
R = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable'); OUT = R / 'ArtSource/Cover/title_1118'; OUT.mkdir(parents=True, exist_ok=True)
f = ImageFont.truetype(str(R / 'ArtSource/Fonts/Cinzel.ttf'), 300)
try: f.set_variation_by_axes([900])
except Exception: pass
specs = []
for key, text in (('en_tripo', 'TRIPO'), ('en_craft', 'CRAFT')):
    im = Image.new('L', (2600, 700), 255); d = ImageDraw.Draw(im); x = 200
    for ch in text:
        d.text((x, 180), ch, font=f, fill=0, stroke_width=3, stroke_fill=0); x += d.textlength(ch, font=f) + 26
    bb = Image.eval(im, lambda v: 255 - v).getbbox()
    assert bb[0] >= 60 and bb[1] >= 60 and bb[2] + 60 <= im.width and bb[3] + 60 <= im.height, ('裁图越界', text, bb)      # 越界会被补成黑边，挤出来是一条杠
    im.crop((bb[0] - 60, bb[1] - 60, bb[2] + 60, bb[3] + 60)).save(OUT / f'{key}.png')
    specs.append(f'{key}:ArtSource/Cover/title_1118/{key}.png:1:0.13:0.009')   # 厚约 4 cm：标题在视线下方，玩家看得到字的顶面（黄铜侧面）
subprocess.run(['bash', str(R / 'Tools/cover_v5_meshes.sh')] + specs, env=dict(os.environ, COVER_OUT='Assets/_WarTable/Art/Models/Title/v6'), check=False)
print('title words', len(specs))
