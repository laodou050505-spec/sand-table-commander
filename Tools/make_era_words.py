"""封面“时代页”上各风格的名字（立体字的字稿）。11-13 用户：“这几个风格的名称都改一下”——原来叫 中古 / 哥特 / 现代 / 未来（MEDIEVAL / GOTHIC / MODERN / FUTURE），
按年代叫太泛，而且“哥特”不是年代、以后还要加别的风格。改成每个风格一个两个字的名字（NAMES，下标 = Settings.Era）。
字稿：白底黑字，中文用马善政体加粗（AI 写书法会把简体写成繁体，不用），英文用 Cinzel；→ ArtSource/Cover/era/word_<键>.png、menu_<键>.png
之后挤出成立体字：见本文件末尾打印的 cover_v5_meshes.sh 命令（COVER_OUT=Assets/_WarTable/Art/Models/Title/v6），CoverBuilder 的菜单表里用同样的键。
改名只要改 NAMES 再跑一遍。用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_era_words.py"""
import pathlib
from PIL import Image, ImageDraw, ImageFont, ImageFilter

PROJ = pathlib.Path(__file__).resolve().parent.parent
OUT = PROJ / 'ArtSource/Cover/era'; F = PROJ / 'ArtSource/Fonts'
# (键, 中文, 英文)；下标 = Settings.Era：0 原中古、1 原哥特、2 原现代、3 原未来、4 深空
NAMES = [('jiandun', '剑盾', 'BLADES'), ('tieshi', '铁誓', 'IRON OATH'), ('xiaoyan', '硝烟', 'GUNSMOKE'), ('xingchen', '星尘', 'STARDUST'),
         ('shenkong', '深空', 'DEEP SPACE')]   # 下标 4：第五个风格（样图 C 重型机甲）


def zh(text, path, bold=9):
    """bold = 描边加粗的像素数。笔画多的字（重、新）加粗 9 px 会把笔画之间的空隙填死，给小一点。"""
    f = ImageFont.truetype(str(F / 'MaShanZheng.ttf'), 420)
    W, H, gap = 520 * len(text) + 160, 600, (70 if len(text) <= 2 else 28)   # 四个字的词字距收紧，不然一行比别的项宽太多
    im = Image.new('L', (W, H), 255); d = ImageDraw.Draw(im)
    ws = [d.textbbox((0, 0), ch, font=f, stroke_width=bold) for ch in text]
    total = sum(b[2] - b[0] for b in ws) + gap * (len(text) - 1); x = (W - total) / 2
    for ch, b in zip(text, ws):
        d.text((x - b[0], (H - (b[3] - b[1])) / 2 - b[1]), ch, font=f, fill=0, stroke_width=bold, stroke_fill=0)   # 描边 = 加粗
        x += b[2] - b[0] + gap
    im = im.filter(ImageFilter.GaussianBlur(3 if bold >= 9 else 1.5))          # 边缘揉软一点，像毛笔
    bb = Image.eval(im, lambda v: 255 - v).getbbox()
    OW = 600 * len(text)   # 两个字 1200 宽（和原来一致），四个字 2400
    im.crop((max(0, bb[0] - 90), max(0, bb[1] - 60), min(W, bb[2] + 90), min(H, bb[3] + 60))).resize((OW, int(OW * (bb[3] - bb[1] + 120) / (bb[2] - bb[0] + 180)))).save(path)


def en(text, path):
    f = ImageFont.truetype(str(F / 'Cinzel.ttf'), 150)
    try: f.set_variation_by_axes([700])
    except Exception: pass
    im = Image.new('L', (2400, 320), 255); d = ImageDraw.Draw(im)
    x = 60
    for ch in text:                                       # 字距放宽
        d.text((x, 60), ch, font=f, fill=0, stroke_width=2, stroke_fill=0); x += d.textlength(ch, font=f) + (38 if ch != ' ' else 30)
    bb = Image.eval(im, lambda v: 255 - v).getbbox()
    im.crop((bb[0] - 50, bb[1] - 50, bb[2] + 50, bb[3] + 50)).save(path)


# 封面上别的菜单字也从这里出（11-14：设置页的“重新定位”——清掉沙盘位置，重新用手掌按桌面定位）
EXTRA = [('dingwei', '重新定位', 'REPOSITION')]
BOLD = {'dingwei': 0}   # 笔画多的词：加粗轻一点
specs = []
for key, z, e in NAMES + EXTRA:
    zh(z, OUT / f'word_{key}.png', BOLD.get(key, 9)); en(e, OUT / f'menu_{key}.png')
    k = 2 / len(z)   # 厚度 / 倒角是相对整张字稿宽度的：四个字的词每个字只占一半宽，要减半，不然倒角把笔画糊掉（11-14 “重新定位”）
    specs += [f'word_{key}:ArtSource/Cover/era/word_{key}.png:{len(z)}:{0.06 * k:g}:{0.008 * k:g}', f'menu_{key}:ArtSource/Cover/era/menu_{key}.png:{len(e.split())}:0.012:0.0015']
print('COVER_OUT=Assets/_WarTable/Art/Models/Title/v6 bash Tools/cover_v5_meshes.sh ' + ' '.join(specs))
