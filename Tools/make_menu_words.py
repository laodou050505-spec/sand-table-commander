"""标题页菜单 / 设置页 / 风格页的中文立体字（11-18 用户：整个封面按西式奇幻重做，不要书法）：思源宋体 Heavy，文件名和原来一样（CoverBuilder 的菜单表不用改）。
英文那套（menu_*.fbx，Cinzel）本来就是衬线体，不动。
python3 Tools/make_menu_words.py → 字稿 ArtSource/Cover/menu_1118/*.png → Assets/_WarTable/Art/Models/Title/v6/word_*.fbx"""
import pathlib, subprocess, os
from PIL import Image, ImageDraw, ImageFont
R = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable'); OUT = R / 'ArtSource/Cover/menu_1118'; OUT.mkdir(parents=True, exist_ok=True)
WORDS = {'chuzheng': '出征', 'shidai': '时代', 'jiaoxue': '教学', 'shezhi': '设置', 'tuichu': '退出', 'zhongwen': '中文', 'zhansu': '战速', 'andu': '暗度', 'dingwei': '重新定位', 'fanhui': '返回',
         'jiandun': '剑盾', 'tieshi': '铁誓', 'xiaoyan': '硝烟', 'xingchen': '星尘', 'shenkong': '深空', 'dangqian': '当前', 'jingqing': '敬请期待'}
f = ImageFont.truetype(str(R / 'ArtSource/Fonts/SourceHanSerifCN-Heavy.otf'), 420)
specs = []
for key, text in WORDS.items():
    gap = 80 if len(text) <= 2 else 36
    im = Image.new('L', (520 * len(text) + 300, 900), 255); d = ImageDraw.Draw(im)
    bbs = [d.textbbox((0, 0), ch, font=f) for ch in text]; total = sum(b[2] - b[0] for b in bbs) + gap * (len(text) - 1); x = (im.width - total) / 2
    for ch, b in zip(text, bbs):
        d.text((x - b[0], 200), ch, font=f, fill=0); x += b[2] - b[0] + gap
    bb = Image.eval(im, lambda v: 255 - v).getbbox()
    assert bb[1] >= 50 and bb[3] + 50 <= im.height and bb[0] >= 60 and bb[2] + 60 <= im.width, ('裁图越界', text, bb, im.size)      # 越界会被补成黑边，挤出来是一条杠
    im.crop((bb[0] - 60, bb[1] - 50, bb[2] + 60, bb[3] + 50)).save(OUT / f'word_{key}.png')
    k = 2 / max(2, len(text))
    specs.append(f'word_{key}:ArtSource/Cover/menu_1118/word_{key}.png:{len(text)}:{0.05 * k:g}:{0.006 * k:g}')
subprocess.run(['bash', str(R / 'Tools/cover_v5_meshes.sh')] + specs, env=dict(os.environ, COVER_OUT='Assets/_WarTable/Art/Models/Title/v6'), check=False)
print('menu words', len(specs))
