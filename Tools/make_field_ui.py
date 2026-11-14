"""战场界面贴图（11-05 用户：哪边是我们哪边是敌人、怎么攻击都看不懂；手势要一直显示在场景里）。
python3 Tools/make_field_ui.py → ArtSource/Cover/v6/alpha/
  tile_<队 0|1>_<inf|arch|cav|ha>_<zh|en>.png  部队头顶的旗牌：我军蓝底金边 / 敌军暗红底骨白边，上写“我军 / 敌军”，大字兵种
  legend_<zh|en>.png                           手势说明牌（沙盘右前角）"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R = '/Users/ld/Documents/ChatGPT/WarTable/'
A = R + 'ArtSource/Cover/v6/alpha/'
F = R + 'ArtSource/Fonts/'
wk = lambda s: ImageFont.truetype(F + 'LXGWWenKai-Regular.ttf', s)
ms = lambda s: ImageFont.truetype(F + 'MaShanZheng.ttf', s)
def cz(s):
    f = ImageFont.truetype(F + 'Cinzel.ttf', s)
    try: f.set_variation_by_axes([700])
    except Exception: pass
    return f

def centered(d, cx, cy, txt, font, fill):
    bb = d.textbbox((0, 0), txt, font=font); w = bb[2] - bb[0]; h = bb[3] - bb[1]
    d.text((cx - w / 2 - bb[0], cy - h / 2 - bb[1]), txt, font=font, fill=fill)

# ── 部队旗牌
TEAM = [  # (底色, 边色, 上方字色, “我军/敌军”, 英文)
    ((26, 44, 96), (214, 172, 86), (240, 214, 150), '我军', 'OURS'),
    ((112, 24, 20), (232, 214, 182), (240, 222, 196), '敌军', 'FOE'),
]
GLYPH = {'inf': ('步', 'INF'), 'arch': ('弓', 'ARC'), 'cav': ('骑', 'CAV'), 'ha': ('射', 'HA')}
W, H = 256, 330
import os
UNIT_SRC = {'inf': 'unit_inf', 'arch': 'unit_arch', 'cav': 'unit_cav', 'ha': 'unit_ha'}   # Tripo 水墨兵种图（ArtSource/UI/gen/<名>/01_generated_image_url.png）
# 个别图标的排版微调（键 = 兵种 + 时代后缀）：size = 相对标准图标框的倍数，rot = 逆时针转多少度。
# 未来时代的机骑是伏在悬浮摩托上端着长枪的骑手（11-12 用户：未来不必骑马），长枪往前伸出去、侧影很扁，按标准框缩放太小 → 放大。
ICON_TWEAK = {('cav', '_f'): dict(size=1.5), ('ha', '_f'): dict(size=1.12), ('cav', '_m'): dict(size=1.45), ('arch', '_m'): dict(size=1.3), ('inf', '_m'): dict(size=1.1),
              ('inf', '_s'): dict(size=1.1), ('arch', '_s'): dict(size=1.35), ('cav', '_s'): dict(size=1.4), ('ha', '_s'): dict(size=1.25)}
def unit_mask(g, size, sfx=''):
    """兵种水墨图 → 墨迹透明度（0–1），裁到墨迹范围、等比缩进 size×size。没有图返回 None。"""
    p = R + f'ArtSource/UI/gen/{UNIT_SRC[g]}{sfx}/01_generated_image_url.png'   # sfx = "_g"：哥特时代图标
    if not os.path.exists(p): return None
    src = np.array(Image.open(p).convert('L')).astype(float)
    paper = np.percentile(src, 80)
    ink = np.clip((paper - src) / max(1, paper - src.min()), 0, 1); ink = np.clip((ink - 0.15) / 0.85, 0, 1)
    from scipy import ndimage as ndi   # 去掉贴着图片边缘的墨迹（纸边 / 背景阴影）
    lab, n = ndi.label(ink > 0.08)
    edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    for e in edge: ink[lab == e] = 0
    H0, W0 = ink.shape   # 再去掉又细又长的线（纸张内框的边线）
    for li, sl in enumerate(ndi.find_objects(lab), 1):
        if sl is None: continue
        hh = sl[0].stop - sl[0].start; ww = sl[1].stop - sl[1].start
        if (hh < H0 * 0.03 and ww > W0 * 0.15) or (ww < W0 * 0.03 and hh > H0 * 0.15): ink[lab == li] = 0
    # 去掉微小的纸纹噪点（不到最大墨块的万分之五）：一个 2 像素的点就能把裁切框撑到整张图，图标被缩得很小、挤到一边。
    # 只对未来时代的图标做——中古 / 哥特的图标已经定稿，去噪会抹掉一点干笔飞白，旗牌就不是原样了（核对过：不做时 64 张逐字节不变）
    lab2, n2 = ndi.label(ink > 0.08) if sfx in ('_f', '_m', '_s') else (None, 0)
    if n2 > 1:
        area = ndi.sum(ink > 0.08, lab2, range(1, n2 + 1)); big = area.max()
        for li in range(1, n2 + 1):
            if area[li - 1] < big * 0.0005: ink[lab2 == li] = 0
    ys, xs = np.where(ink > 0.5); pad = 8
    ink = ink[max(0, ys.min() - pad):ys.max() + pad, max(0, xs.min() - pad):xs.max() + pad]
    tw = ICON_TWEAK.get((g, sfx), {})
    if tw.get('rot'):
        ink = np.asarray(Image.fromarray((ink * 255).astype(np.uint8)).rotate(tw['rot'], resample=Image.BICUBIC, expand=True)).astype(float) / 255
    size = int(size * tw.get('size', 1))
    h, w = ink.shape; k = size / max(h, w)
    m = Image.fromarray((ink * 255).astype(np.uint8)).resize((max(1, int(w * k)), max(1, int(h * k))), Image.LANCZOS)
    m = m.filter(ImageFilter.MaxFilter(3))   # 缩小后笔画加粗一点
    if sfx and g == "arch": m = m.filter(ImageFilter.MaxFilter(3))   # 哥特长枪是细线稿，再加粗一档（剪影类图标加粗会糊成一团）
    out = Image.new('L', (size, size), 0); out.paste(m, ((size - m.width) // 2, (size - m.height) // 2)); return out
UNIT_ZH = {'inf': '步兵', 'arch': '弓兵', 'cav': '骑兵', 'ha': '骑射'}
UNIT_EN = {'inf': 'INF', 'arch': 'BOW', 'cav': 'CAV', 'ha': 'H.ARCH'}
# 11-11 用户：换时代后兵种名称也要换（远程不能一直叫“弓”）→ 哥特时代另出一套 tile_<队>_<兵种>_g_<语言>[_hot]：
#   名称铁卫 / 射手 / 铁骑 / 猎骑；图标 ArtSource/UI/gen/unit_<兵种>_g（Tripo seedream_v5 水墨：铆钉塔盾 + 战锤 / 栓动长枪 / 披整块面甲的战马头 / 狼头 + 小长枪）
# 哥特两军配色（11-11 用户选“骨白铜绿 对 煤黑熔橙”）：我军深铜绿底 + 骨白边 / 字；敌军煤黑底 + 熔橙边 / 字
TEAM_G = [
    ((22, 74, 68), (226, 216, 194), (236, 228, 208), '我军', 'OURS'),
    ((30, 26, 24), (238, 118, 34), (246, 160, 80), '敌军', 'FOE'),
]
# 未来两军配色（11-12 样图 A）：我军天青底 + 珍珠白边 / 字；敌军紫黑底 + 紫边 / 字
TEAM_F = [
    ((16, 104, 136), (238, 242, 244), (240, 246, 248), '我军', 'OURS'),
    ((34, 22, 52), (168, 92, 240), (206, 168, 250), '敌军', 'FOE'),
]
# 现代两军配色（11-13 样图 A）：我军藏青底 + 荧光黄绿边 / 字；敌军深棕底 + 沙色边、红字（赤砂佣兵：沙漠迷彩 + 红围巾）
TEAM_M = [
    ((24, 40, 84), (196, 232, 60), (224, 244, 150), '我军', 'OURS'),
    ((70, 46, 28), (222, 190, 138), (244, 120, 96), '敌军', 'FOE'),
]
# 深空两军配色（11-13 样图 C）：我军钢蓝底 + 白边、橙字（星枪团：钢蓝白搪瓷 + 橙色警示条）；敌军焦红黑底 + 琥珀边 / 字（余烬矿帮）
TEAM_S = [
    ((44, 70, 98), (236, 240, 244), (252, 158, 60), '我军', 'OURS'),
    ((58, 20, 14), (248, 168, 52), (252, 200, 110), '敌军', 'FOE'),
]
ERAS = [('', UNIT_ZH, UNIT_EN, GLYPH, TEAM),
        ('_g', {'inf': '铁卫', 'arch': '射手', 'cav': '铁骑', 'ha': '猎骑'}, {'inf': 'GUARD', 'arch': 'RIFLE', 'cav': 'LANCE', 'ha': 'RIDER'},
         {'inf': ('卫', 'GRD'), 'arch': ('射', 'MKS'), 'cav': ('铁', 'LNC'), 'ha': ('猎', 'RDR')}, TEAM_G),
        # 未来：锋卫 / 光弓 / 机骑 / 游骑；图标 ArtSource/UI/gen/unit_<兵种>_f（没出图时用大字）
        ('_f', {'inf': '锋卫', 'arch': '光弓', 'cav': '机骑', 'ha': '游骑'}, {'inf': 'VANGUARD', 'arch': 'BOWS', 'cav': 'MECH', 'ha': 'RUNNER'},
         {'inf': ('锋', 'VAN'), 'arch': ('弓', 'BOW'), 'cav': ('机', 'MEC'), 'ha': ('游', 'RUN')}, TEAM_F),
        # 现代：突击 / 枪手 / 摩骑 / 飞车（名字是我先定的默认值）；图标 ArtSource/UI/gen/unit_<兵种>_m
        ('_m', {'inf': '突击', 'arch': '枪手', 'cav': '摩骑', 'ha': '飞车'}, {'inf': 'ASSAULT', 'arch': 'RIFLES', 'cav': 'BIKERS', 'ha': 'RAIDERS'},
         {'inf': ('突', 'AST'), 'arch': ('枪', 'RFL'), 'cav': ('摩', 'BKR'), 'ha': ('飞', 'RDR')}, TEAM_M),
        # 深空：重甲 / 轨枪 / 星骑 / 掠车（名字是我先定的默认值）；图标 ArtSource/UI/gen/unit_<兵种>_s
        ('_s', {'inf': '重甲', 'arch': '轨枪', 'cav': '星骑', 'ha': '掠车'}, {'inf': 'HEAVIES', 'arch': 'RAILGUNS', 'cav': 'STRIDERS', 'ha': 'CRAWLERS'},
         {'inf': ('甲', 'HVY'), 'arch': ('轨', 'RLG'), 'cav': ('星', 'STR'), 'ha': ('掠', 'CRW')}, TEAM_S)]
import os as _os, sys as _sys; _sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import era_names   # 名字的唯一出处（上面 ERAS 里写的名字只是历史记录，以 era_names 为准）
ERAS = [(sfx, era_names.ZH[sfx.lstrip('_')], era_names.EN[sfx.lstrip('_')], gl, tm) for sfx, _uz, _ue, gl, tm in ERAS]
def lighten(c, k): return tuple(int(v + (255 - v) * k) for v in c)
for sfx, UZ, UE, GL, TM in ERAS:
    for ti, (fill, edge, top, zh_t, en_t) in enumerate(TM):
        for g, (gz, ge) in GL.items():
            for lang in ('zh', 'en'):
                for hot in (False, True):
                    # 11-07 用户：眼睛看到的部队应该显示部队自己的图标（比如弓兵就是弓）——中间画兵种水墨图，上面一行小字“我军 · 弓兵”；
                    # 被看着时：底色提亮、图案变金、外圈一道金光边（不只是放大）
                    bg = lighten(fill, 0.08) if hot else fill
                    rim = (255, 214, 120) if hot else edge
                    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
                    sh = Image.new('RGBA', (W, H), (0, 0, 0, 0)); ds = ImageDraw.Draw(sh)
                    ds.rounded_rectangle([14, 16, W - 10, 286], radius=28, fill=(0, 0, 0, 150))
                    ds.polygon([(W / 2 - 28, 284), (W / 2 + 28, 284), (W / 2 + 4, 326)], fill=(0, 0, 0, 150))
                    im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(6)))
                    if hot:
                        gl = Image.new('RGBA', (W, H), (0, 0, 0, 0)); ImageDraw.Draw(gl).rounded_rectangle([4, 2, W - 4, 286], radius=32, outline=(255, 214, 120, 255), width=14)
                        im.alpha_composite(gl.filter(ImageFilter.GaussianBlur(5)))
                    d = ImageDraw.Draw(im)
                    d.rounded_rectangle([10, 8, W - 10, 280], radius=28, fill=bg + (255,), outline=rim + (255,), width=11 if hot else 9)
                    d.polygon([(W / 2 - 26, 276), (W / 2 + 26, 276), (W / 2, 322)], fill=rim + (255,))
                    d.polygon([(W / 2 - 15, 274), (W / 2 + 15, 274), (W / 2, 302)], fill=bg + (255,))
                    label = (zh_t + '·' + UZ[g]) if lang == 'zh' else (en_t + ' ' + UE[g])
                    lf = wk(46) if lang == 'zh' else cz(34)
                    while d.textlength(label, font=lf) > W - 44: lf = (wk if lang == 'zh' else cz)(lf.size - 2)   # 重建字体（font_variant 会丢粗体）
                    centered(d, W / 2, 46, label, lf, top + (255,))
                    d.line([(30, 84), (W - 30, 84)], fill=rim + (220,), width=4)
                    m = unit_mask(g, 124, sfx)   # 11-07 用户：图标不要太满 → 168 → 124，四周留白
                    ink = (255, 214, 120) if hot else (246, 238, 220)
                    if m is not None:
                        col = Image.new('RGBA', m.size, ink + (255,)); col.putalpha(m)
                        im.alpha_composite(col, ((W - m.width) // 2, 180 - m.height // 2))
                    else:   # 还没有兵种图：用大字
                        d = ImageDraw.Draw(im)
                        if lang == 'zh': centered(d, W / 2, 182, gz, ms(128), ink + (255,))
                        else: centered(d, W / 2, 182, ge, cz(80 if len(ge) > 2 else 96), ink + (255,))
                    im.save(A + f'tile_{ti}_{g}{sfx}_{lang}{"_hot" if hot else ""}.png')
print('tiles ok')

# ── 说明面板（11-06 用户：悬浮在沙盘左上方——玩家多用右手做手势；11-07 用户：不要拉伸、注意尺寸、可以收起；会玩了但不会玩好 → 加兵法页）
# 两页同尺寸：标题栏三格 [手势][兵法][收起]，看着捏 / 手指点切换（热区见 BoardSigns.Legend*）
LW, LH = 1000, 1440
HEAD_Y0, HEAD_Y1 = 26, 176
GOLD = (210, 170, 92); INK = (18, 15, 13); LACQ = (112, 24, 20)
TABS = {'zh': ('操作', '兵法', '收起'), 'en': ('CONTROLS', 'TACTICS', 'HIDE')}   # 11-10：手势去掉了，标签改“操作”
# 字号（11-07 用户：注意字是否看得清）：面板实高 0.34 m、离眼约 0.75 m、贴图高 1440 px → 1° ≈ 55 px；正文 ≥ 56 px
GEST = {
    'zh': [('hand_pinch', '捏住令牌拖到别处', '移动'),   # 11-09：主操作改成伸手拿令牌（不靠眼睛），放在最前
           ('hand_pinch', '令牌拖到红旗上', '进攻'),
           ('hand_pinch', '布阵时拖到红旗上', '开战即攻'),
           ('hand_pinch', '捏住帅旗停一下', '开战'),
           ('tok:charge', '“冲锋”令扔到沙盘上', '全军冲锋'),   # 11-10：手势换成右侧令架的三支令牌（扔到哪都一样，对全军）
           ('tok:hold', '“坚守”令扔到沙盘上', '全军坚守'),
           ('tok:retreat', '“撤退”令扔到沙盘上', '全军撤退'),
           ('hand_tray', '双手捏两个圆扣', '挪沙盘'),
           ('btn_pause', '点角上的印章', '暂停')],
    'en': [('hand_pinch', 'Drag a token somewhere', 'Move'),
           ('hand_pinch', 'Drop it on a red flag', 'Attack'),
           ('hand_pinch', 'Deploy: drop on a foe', 'Strike first'),
           ('hand_pinch', 'Pinch & hold banner', 'Start'),
           ('tok:charge', 'Toss CHARGE token', 'All charge'),
           ('tok:hold', 'Toss HOLD token', 'All hold'),
           ('tok:retreat', 'Toss RETREAT token', 'All retreat'),
           ('hand_tray', 'Pinch both knobs', 'Board'),
           ('btn_pause', 'Tap the corner seal', 'Pause')],
}
# 兵法：全部来自实测（BalanceTest 每种打法 6–8 局）和士气规则（Battle.cs：主将阵亡全军 −18，身边阵亡 −7，旁边溃逃 −4，起始 60）
TACT = {
    # 11-10 用户：不要写 12/20 → “胜率约几成”（12/20 ≈ 六成、2/20 ≈ 一成）；每条都能在游戏里看到对应结果
    'zh': [('anvil', '等他撞上来再反冲', '先扔“坚守”令原地待敌，两军一接上就扔“冲锋”令', '胜率约六成'),   # 11-08 骑射手软边界后重测（BalanceTest 各 20 局：待敌反冲 12、全冲 10、坚守 2）
           ('focus', '集中打一支', '全军打同一支；它一崩，旁边的也动摇', '士气连锁'),
           ('general', '主将倒下全军动摇', '头顶金色“帅”字牌的就是主将。他一倒，全军容易溃逃', '看帅字牌'),
           ('wall', '不要只守', '一直坚守不进攻，会被一点点耗光', '胜率约一成')],
    'en': [('anvil', 'Let them come', 'Toss HOLD, then toss CHARGE once the lines meet', 'Wins ~6 in 10'),
           ('focus', 'Focus one unit', 'Break one unit and its neighbours waver', 'Morale'),
           ('general', 'Guard the general', 'Gold GEN badge marks him. If he falls, units flee', 'GEN badge'),
           ('wall', 'Don’t just hold', 'Waiting forever only grinds you down', 'Wins ~1 in 10')],
}
def token_icon(key, lang, S=94):
    """令牌图标（11-12：和新令牌一致）：金圈 + 该令牌的漆色圆底，中间是令牌上刻的同一个书法令徽（ArtSource/Tokens/emblem_<令>.png）。"""
    lacq = {'charge': (120, 20, 12), 'hold': (22, 20, 19), 'retreat': (20, 30, 70)}[key]
    big = S * 4; im = Image.new('RGBA', (big, big), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.ellipse([6, 6, big - 6, big - 6], fill=lacq + (255,), outline=GOLD + (230,), width=12)
    em = Image.open(R + f'ArtSource/Tokens/emblem_{key}.png').convert('L'); em = Image.eval(em, lambda v: 255 - v)
    k = big * 0.62 / max(em.size); em = em.resize((max(1, int(em.width * k)), max(1, int(em.height * k))), Image.LANCZOS)
    g = Image.new('RGBA', em.size, (236, 190, 104, 255)); g.putalpha(em)
    im.alpha_composite(g, ((big - em.width) // 2, (big - em.height) // 2))
    return im.resize((S, S), Image.LANCZOS)
def fit(d, txt, spec, maxw):
    mk, size = spec; f = mk(size)
    while d.textlength(txt, font=f) > maxw and size > 24: size -= 2; f = mk(size)
    return f
def panel_base(lang, active):
    im = Image.new('RGBA', (LW, LH), (0, 0, 0, 0))
    sh = Image.new('RGBA', (LW, LH), (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle([18, 22, LW - 10, LH - 6], radius=44, fill=(0, 0, 0, 140))
    im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(8)))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([10, 10, LW - 18, LH - 18], radius=40, fill=INK + (242,), outline=GOLD + (255,), width=6)
    d.rounded_rectangle([26, 26, LW - 34, LH - 34], radius=30, outline=GOLD + (90,), width=2)
    cw = (LW - 60) / 3
    for c, label in enumerate(TABS[lang]):
        x0 = 26 + cw * c; x1 = x0 + cw
        on = c == active
        ov = Image.new('RGBA', (LW, LH), (0, 0, 0, 0)); od = ImageDraw.Draw(ov)
        od.rounded_rectangle([x0 + 6, HEAD_Y0 + 6, x1 - 6, HEAD_Y1 - 6], radius=24, fill=(LACQ + (240,)) if on else (255, 240, 210, 16), outline=GOLD + ((255,) if on else (110,)), width=3 if on else 2)
        im.alpha_composite(ov); d = ImageDraw.Draw(im)
        f = (wk(64) if lang == 'zh' else cz(44)) if c < 2 else (wk(54) if lang == 'zh' else cz(38))
        col = (246, 226, 180, 255) if on else ((214, 196, 160, 255) if c < 2 else (190, 172, 140, 255))
        cxm, cym = (x0 + x1) / 2, (HEAD_Y0 + HEAD_Y1) / 2
        if c == 2:   # 收起：字 + 一个朝上的小三角（字体里没有箭头符号，手画）
            tw = d.textlength(label, font=f); cxm -= 16
            d.polygon([(cxm + tw / 2 + 16, cym - 8), (cxm + tw / 2 + 40, cym - 8), (cxm + tw / 2 + 28, cym + 8)], fill=col)   # 朝下 = 收起
        centered(d, cxm, cym, label, f, col)
    return im, d
def zebra(im, top, rh):
    ov = Image.new('RGBA', (LW, LH), (0, 0, 0, 0)); ImageDraw.Draw(ov).rounded_rectangle([40, top + 6, LW - 48, top + rh - 6], radius=22, fill=(255, 240, 210, 18))
    im.alpha_composite(ov); return ImageDraw.Draw(im)
def diagram(kind, S=150):
    """兵法小示意图：蓝 = 我军、红 = 敌军，金箭头 = 行动。"""
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.ellipse([4, 4, S - 4, S - 4], fill=(28, 22, 18, 255), outline=GOLD + (200,), width=3)
    B = (70, 110, 200, 255); Rr = (190, 50, 40, 255); G = (240, 200, 120, 255)
    def blk(x, y, c, w=34, h=16): d.rounded_rectangle([x - w / 2, y - h / 2, x + w / 2, y + h / 2], radius=4, fill=c)
    def arrow(pts, c=G, w=6):
        d.line(pts, fill=c, width=w, joint='curve')
        (x1, y1), (x2, y2) = pts[-2], pts[-1]
        import math; a = math.atan2(y2 - y1, x2 - x1)
        tip = [(x2 + 9 * math.cos(a), y2 + 9 * math.sin(a)), (x2 + 9 * math.cos(a + 2.4), y2 + 9 * math.sin(a + 2.4)), (x2 + 9 * math.cos(a - 2.4), y2 + 9 * math.sin(a - 2.4))]
        d.polygon(tip, fill=c)
    if kind == 'anvil':
        blk(75, 46, Rr, 44); blk(75, 78, B, 44); arrow([(75, 100), (75, 92)])
        blk(28, 112, B, 20, 14); arrow([(30, 100), (28, 70), (44, 50)])
    elif kind == 'focus':
        blk(75, 40, Rr, 30); blk(40, 54, Rr, 22, 12); blk(110, 54, Rr, 22, 12)
        for x in (40, 75, 110): blk(x, 116, B, 26, 12); arrow([(x, 104), (75, 56)], w=4)
    elif kind == 'general':
        d.polygon([(75, 30), (86, 52), (64, 52)], fill=G); d.line([(75, 52), (75, 100)], fill=G, width=5)
        for x in (38, 112): blk(x, 104, B, 26, 12)
        d.line([(58, 70), (92, 104)], fill=Rr, width=7); d.line([(92, 70), (58, 104)], fill=Rr, width=7)
    else:  # wall
        blk(75, 92, B, 60, 18); blk(75, 40, Rr, 44)
        for x in (52, 75, 98): arrow([(x, 52), (x, 76)], c=Rr, w=4)
    return im
for lang in ('zh', 'en'):
    im, d = panel_base(lang, 0)
    y0, rh = HEAD_Y1 + 18, 125   # 11-08：多一行“布阵时拖到敌军上”，行高 122 → 108
    for k, (icon, how, what) in enumerate(GEST[lang]):
        top = y0 + rh * k; cy = top + rh / 2
        if k % 2 == 0: d = zebra(im, top, rh)
        ic = token_icon(icon[4:], lang) if icon.startswith('tok:') else Image.open(A + icon + '.png').convert('RGBA').resize((94, 94), Image.LANCZOS)
        im.alpha_composite(ic, (61, int(cy - 47)))
        hf = fit(d, how, (wk, 58) if lang == 'zh' else (cz, 44), LW - 180 - 250)
        d.text((180, cy), how, font=hf, fill=(236, 228, 208, 255), anchor='lm')
        d.text((LW - 66, cy), what, font=fit(d, what, (wk, 62) if lang == 'zh' else (cz, 46), 220), fill=(244, 198, 120, 255), anchor='rm')
        if k < len(GEST[lang]) - 1: d.line([(180, top + rh), (LW - 66, top + rh)], fill=(150, 120, 80, 90), width=2)
    im.save(A + f'legend_{lang}.png')
    im, d = panel_base(lang, 1)
    y0, rh = HEAD_Y1 + 26, 300
    for k, (kind, title, desc, tag) in enumerate(TACT[lang]):
        top = y0 + rh * k; cy = top + rh / 2
        if k % 2 == 0: d = zebra(im, top, rh)
        import os
        tp = A + f'tac_{kind}.png'   # 水墨示意图（Tools/make_tactic_icons.py），缺时退回程序画的小图
        ti_ = Image.open(tp).convert('RGBA').resize((160, 160), Image.LANCZOS) if os.path.exists(tp) else diagram(kind)
        im.alpha_composite(ti_, (48, int(cy - ti_.height / 2))); d = ImageDraw.Draw(im)
        tg = wk(40) if lang == 'zh' else cz(30); tw = d.textlength(tag, font=tg)
        tf = fit(d, title, (wk, 64) if lang == 'zh' else (cz, 48), LW - 230 - 70 - tw - 50); df = wk(52) if lang == 'zh' else cz(38)
        d.text((230, cy - 64), title, font=tf, fill=(244, 198, 120, 255), anchor='lm')
        # 说明分两行（按宽度折行）
        words = list(desc) if lang == 'zh' else desc.split(' ')
        lines, cur = [], ''
        for w_ in words:
            t_ = cur + w_ if lang == 'zh' else (cur + ' ' + w_).strip()
            if d.textlength(t_, font=df) > LW - 230 - 70 and cur and not (lang == 'zh' and w_ in '，。；、：”）！？'): lines.append(cur); cur = w_   # 标点不放行首
            else: cur = t_
        lines.append(cur)
        for li, ln in enumerate(lines[:3]): d.text((230, cy + 8 + li * 64), ln, font=df, fill=(228, 218, 198, 255), anchor='lm')
        # 标签：右上角小金框
        d.rounded_rectangle([LW - 70 - tw - 28, cy - 92, LW - 70, cy - 36], radius=14, outline=GOLD + (220,), width=2)
        d.text((LW - 70 - 14, cy - 64), tag, font=tg, fill=(236, 206, 140, 255), anchor='rm')
        if k < len(TACT[lang]) - 1: d.line([(230, top + rh), (LW - 70, top + rh)], fill=(150, 120, 80, 90), width=2)
    im.save(A + f'legend_tactics_{lang}.png')
    # 收起后的小牌
    label = '操作 · 兵法' if lang == 'zh' else 'CONTROLS · TACTICS'
    lf = wk(56) if lang == 'zh' else cz(40)
    TW, TH = int(140 + ImageDraw.Draw(Image.new('RGBA', (8, 8))).textlength(label, font=lf) + 90), 160
    tb = Image.new('RGBA', (TW, TH), (0, 0, 0, 0))
    sh = Image.new('RGBA', (TW, TH), (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle([12, 16, TW - 6, TH - 4], radius=30, fill=(0, 0, 0, 130))
    tb.alpha_composite(sh.filter(ImageFilter.GaussianBlur(6)))
    td = ImageDraw.Draw(tb)
    td.rounded_rectangle([6, 6, TW - 12, TH - 12], radius=28, fill=INK + (240,), outline=GOLD + (255,), width=4)
    ic = Image.open(A + 'hand_pinch.png').convert('RGBA').resize((104, 104), Image.LANCZOS); tb.alpha_composite(ic, (24, (TH - 6) // 2 - 52)); td = ImageDraw.Draw(tb)
    cyy = (TH - 6) // 2
    td.text((140, cyy), label, font=lf, fill=(244, 214, 150, 255), anchor='lm')
    ax = 140 + td.textlength(label, font=lf) + 24
    td.polygon([(ax, cyy + 12), (ax + 24, cyy + 12), (ax + 12, cyy - 10)], fill=(244, 214, 150, 255))   # 朝上 = 展开
    tb.save(A + f'legend_tab_{lang}.png')
print('legend ok')
prev = Image.new('RGB', (W * 8 + LW // 2, max(H, LH // 2)), (150, 145, 138))
k = 0
for ti in (0, 1):
    for g in GLYPH:
        t = Image.open(A + f'tile_{ti}_{g}_zh.png'); prev.paste(t, (k * W, 0), t); k += 1
lg = Image.open(A + 'legend_zh.png').resize((LW // 2, LH // 2)); prev.paste(lg, (W * 8, 0), lg)
prev.save('/tmp/field_ui_preview.png')   # 预览图不进工程
