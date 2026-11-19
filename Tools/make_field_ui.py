"""战场界面贴图（11-05 用户：哪边是我们哪边是敌人、怎么攻击都看不懂；手势要一直显示在场景里）。
python3 Tools/make_field_ui.py → ArtSource/Cover/v6/alpha/
  tile_<队 0|1>_<inf|arch|cav|ha>_<zh|en>.png  部队头顶的旗牌：我军蓝底金边 / 敌军暗红底骨白边，上写“我军 / 敌军”，大字兵种
  legend_<zh|en>.png                           手势说明牌（沙盘右前角）"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R = '/Users/ld/Documents/ChatGPT/WarTable/'
A = R + 'ArtSource/Cover/v6/alpha/'
F = R + 'ArtSource/Fonts/'
wk = lambda s: ImageFont.truetype(F + 'SourceHanSerifCN-Bold.otf', s)
ms = lambda s: ImageFont.truetype(F + 'SourceHanSerifCN-Heavy.otf', s)
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
    p = R + f'ArtSource/UI/gen/{UNIT_SRC[g]}{sfx}_e/01_generated_image_url.png'   # 11-17：铜版画风格的重绘稿（Tools/engrave_icons.py）
    engr = os.path.exists(p)
    if not engr: p = R + f'ArtSource/UI/gen/{UNIT_SRC[g]}{sfx}/01_generated_image_url.png'   # sfx = "_g"：哥特时代图标
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
    lab2, n2 = ndi.label(ink > 0.08) if (engr or sfx in ('_f', '_m', '_s')) else (None, 0)
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
import collage_kit as ck   # 11-17：全套界面换成拼贴纸片（和结算板一套）
BODY = {}
for sfx, UZ, UE, GL, TM in ERAS:
    for ti, (fill, edge, top, zh_t, en_t) in enumerate(TM):
        for g, (gz, ge) in GL.items():
            for lang in ('zh', 'en'):
                for hot in (False, True):
                    # 11-07 用户：眼睛看到的部队应该显示部队自己的图标（比如弓兵就是弓）——中间画兵种水墨图，上面一行小字“我军 · 弓兵”；
                    # 被看着时：底色提亮、图案变金、外圈一道金光边（不只是放大）
                    bg = lighten(fill, 0.08) if hot else fill
                    rim = (255, 214, 120) if hot else edge
                    # 11-17：圆角方牌 + 一圈粗边 → 一面尖底小旗（染了队伍色的纸，细线往里缩一圈、在斜边上断开，最上面一条深色番号带）
                    # 同日晚用户：“没有之前那种感觉了，有点素”→ 真纸版：旗面是染色的真纸（下面两条斜边是撕的），透出一轮淡淡的铜版画日轮和网点；
                    #   番号写在一片象牙白纸签上，斜贴在旗面上沿（和结算板“象牙白压深色”的叠法一样）
                    bkey = (bg, rim, hot, ti)
                    if bkey not in BODY: BODY[bkey] = ck.pennant2(W, H, bg, rim, hot=hot, pad=12, top=30, tip=0.19, seed=ti * 7 + 1, icon_y=178)
                    im = BODY[bkey].copy(); d = ImageDraw.Draw(im)
                    label = (zh_t + '·' + UZ[g]) if lang == 'zh' else (en_t + ' ' + UE[g])
                    lf = wk(38) if lang == 'zh' else cz(28)
                    while d.textlength(label, font=lf) > W - 76: lf = (wk if lang == 'zh' else cz)(lf.size - 2)   # 重建字体（font_variant 会丢粗体）
                    tag = ck.chip(label, lf, padx=16, h=62, pad=7, cut='l', flip=bool(ti))
                    ck.paste_rot(im, tag if tag.width <= W else tag.resize((W, tag.height)), W / 2 + (2 if ti else -2), 40, -2.5 if ti else 2.5)
                    m = unit_mask(g, 124, sfx)   # 11-07 用户：图标不要太满 → 168 → 124，四周留白
                    ink = (255, 214, 120) if hot else (246, 238, 220)
                    if m is not None:
                        col = Image.new('RGBA', m.size, ink + (255,)); col.putalpha(m)
                        im.alpha_composite(col, ((W - m.width) // 2, 178 - m.height // 2))
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
    'zh': [('hand_pinch', '捏住旗牌拖到别处', '调遣'),   # 11-09：主操作改成伸手拿令牌（不靠眼睛），放在最前
           ('hand_pinch', '旗牌拖到红旗上', '进攻'),
           ('hand_pinch', '布阵时拖到红旗上', '先发制人'),
           ('hand_pinch', '按红色大按钮', '开战'),   # 11-16：开战改成指挥台正中的大按钮
           ('tok:charge', '按红色“冲锋”', '全军冲锋'),   # 11-10：手势换成右侧令架的三支令牌（扔到哪都一样，对全军）
           ('tok:hold', '按蓝色“坚守”', '全军坚守'),
           ('tok:retreat', '按琥珀色“撤退”', '全军撤退'),
           ('hand_tray', '双手各捏一枚圆钮', '调整沙盘'),
           ('btn_pause', '点角上的圆钮', '暂停')],
    'en': [('hand_pinch', 'Drag a token somewhere', 'Move'),
           ('hand_pinch', 'Drop it on a red flag', 'Attack'),
           ('hand_pinch', 'Deploy: drop on a foe', 'Strike first'),
           ('hand_pinch', 'Press the red button', 'Start'),
           ('tok:charge', 'Press red CHARGE', 'All charge'),
           ('tok:hold', 'Press blue HOLD', 'All hold'),
           ('tok:retreat', 'Press amber RETREAT', 'All retreat'),
           ('hand_tray', 'Pinch both knobs', 'Board'),
           ('btn_pause', 'Tap the corner dial', 'Pause')],
}
# 兵法：全部来自实测（BalanceTest 每种打法 6–8 局）和士气规则（Battle.cs：主将阵亡全军 −18，身边阵亡 −7，旁边溃逃 −4，起始 60）
TACT = {
    # 11-10 用户：不要写 12/20 → “胜率约几成”（12/20 ≈ 六成、2/20 ≈ 一成）；每条都能在游戏里看到对应结果
    'zh': [('anvil', '后发制人', '先按“坚守”原地待敌，两军一接战，再按“冲锋”', '胜率约六成'),   # 11-08 骑射手软边界后重测（BalanceTest 各 20 局：待敌反冲 12、全冲 10、坚守 2）
           ('focus', '集中兵力', '全军合击一支；它一溃，左右随之动摇', '士气连锁'),
           ('general', '主将系于全军', '头顶金冠的便是主将。主将一倒，全军易溃', '认准金冠'),
           ('wall', '久守必失', '只守不攻，终会被一点点耗尽', '胜率约一成')],
    'en': [('anvil', 'Let them come', 'Press HOLD, then CHARGE once the lines meet', 'Wins ~6 in 10'),
           ('focus', 'Focus one unit', 'Break one unit and its neighbours waver', 'Morale'),
           ('general', 'Guard the general', 'The gold crown marks him. If he falls, units flee', 'Gold crown'),
           ('wall', 'Don’t just hold', 'Waiting forever only grinds you down', 'Wins ~1 in 10')],
}
def token_icon(key, lang, S=94):
    """军令小图标（11-17：和指挥台三个军令按钮一致）：一枚按钮同色的圆纸片，中间是按钮上的同一个令徽（ArtSource/Tokens/emblem_<令>.png）。"""
    lacq = {'charge': (128, 30, 22), 'hold': (30, 62, 112), 'retreat': (140, 92, 24)}[key]
    big = S * 4; im = ck.ring_disc(big, lacq, seed=3, field=0.6)
    em = Image.open(R + f'ArtSource/Tokens/emblem_{key}.png').convert('L'); em = Image.eval(em, lambda v: 255 - v)
    k = big * 0.4 / max(em.size); em = em.resize((max(1, int(em.width * k)), max(1, int(em.height * k))), Image.LANCZOS)
    g = Image.new('RGBA', em.size, (236, 190, 104, 255)); g.putalpha(em)
    im.alpha_composite(g, ((big - em.width) // 2, (big - em.height) // 2))
    return im.resize((S, S), Image.LANCZOS)
def hand_icon(name, S=94):
    """手的小图标：只取象牙白的手（原图带一团墨底，按亮度分开），不要底。"""
    if os.path.exists(A + name + '_line.png'): return Image.open(A + name + '_line.png').convert('RGBA').resize((S, S), Image.LANCZOS)   # 铜版画重绘稿（Tools/make_hand_icons.py）
    h = np.asarray(Image.open(A + name + '.png').convert('RGBA'), np.float32); lum = h[..., :3].max(-1) / 255
    out = np.zeros(h.shape, np.float32); out[..., :3] = ck.IVORY; out[..., 3] = np.clip((lum - 0.45) / 0.3, 0, 1) * h[..., 3]
    return Image.fromarray(out.astype(np.uint8), 'RGBA').resize((S, S), Image.LANCZOS)
def fit(d, txt, spec, maxw):
    mk, size = spec; f = mk(size)
    while d.textlength(txt, font=f) > maxw and size > 24: size -= 2; f = mk(size)
    return f
def panel_base(lang, active):
    # 11-17：圆角黑牌 + 一圈金边 → 一张藏青纸（下沿是撕的），页签是夹在纸上沿的索引签：当前页一片象牙白纸签，其余只有字和一道细线
    # 同日晚（用户：有点素）→ 真纸：一整张撕边的藏青纸（竖着用的羊皮纸原件），右下透出一轮淡淡的日轮线稿和一片网点，左下角一枚磨旧的邮戳
    rgb_, a_, _box = ck.real_sheet(LW, LH, ck.NAVY, pad=14, quarter=1, flip=(active == 1))
    hh, ww = a_.shape; D = 900; Ls = ck.sun_lines(D, 0.0, 0.96); wm = np.zeros((hh, ww), np.float32)
    cxw, cyw = int(ww * 0.86), int(hh * 0.84); y0, x0 = cyw - D // 2, cxw - D // 2
    ys0, ys1, xs0, xs1 = max(0, y0), min(hh, y0 + D), max(0, x0), min(ww, x0 + D)
    wm[ys0:ys1, xs0:xs1] = Ls[ys0 - y0:ys1 - y0, xs0 - x0:xs1 - x0]
    rgb_ = rgb_ + wm[..., None] * (np.array(ck.GOLD, np.float32) - rgb_) * 0.17
    rgb_ = ck.corner_dots(rgb_, 13, ww * 1.0, hh * 1.0, ww * 0.6, hh * 0.3, (40, 58, 100), 0.5)
    im = ck.finish(rgb_, a_, 14, op=0.55)
    d = ImageDraw.Draw(im); cw = (LW - 60) / 3
    for c, label in enumerate(TABS[lang]):
        x0 = 26 + cw * c; x1 = x0 + cw; on = c == active
        f = (wk(58) if lang == 'zh' else cz(40)) if c < 2 else (wk(50) if lang == 'zh' else cz(36))
        cxm, cym = (x0 + x1) / 2, (HEAD_Y0 + HEAD_Y1) / 2
        if on:
            trgb, ta, _b = ck.real_strip(int(cw) + 10, HEAD_Y1 - HEAD_Y0 + 6, ck.IVORY, pad=9, flip=(c == 1)); tab = ck.finish(trgb, ta, 9)      # 当前页：一片真的象牙白纸签
            ck.paste_rot(im, tab, (x0 + x1) / 2, (HEAD_Y0 + HEAD_Y1) / 2, -1.6 if c == 0 else 1.4); d = ImageDraw.Draw(im)
            col = ck.INK + (255,)
        else:
            col = (214, 196, 160, 255) if c < 2 else (176, 160, 132, 255)
            d.line([(x0 + 40, HEAD_Y1 - 22), (x1 - 40, HEAD_Y1 - 22)], fill=ck.GOLD_D + (170,), width=2)
        if c == 2:   # 收起：字 + 一个朝下的小三角（字体里没有箭头符号，手画）
            tw = d.textlength(label, font=f); cxm -= 16
            d.polygon([(cxm + tw / 2 + 16, cym - 8), (cxm + tw / 2 + 40, cym - 8), (cxm + tw / 2 + 28, cym + 8)], fill=col)
        centered(d, cxm, cym, label, f, col)
    d.line([(44, HEAD_Y1 + 6), (LW - 52, HEAD_Y1 + 6)], fill=ck.GOLD_D + (200,), width=2); ck.diamond(d, LW - 52, HEAD_Y1 + 6, 6, ck.GOLD + (255,))
    return im, d
def zebra(im, top, rh):
    return ImageDraw.Draw(im)   # 11-17：隔行的浅底去掉（只留行间细线），纸面干净些
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
# ── 11-17 晚 用户：右边展开的兵法 / 操作面板“也有点死板”→ 和结算板一样拼贴（都画在一张贴图里，本来就是一个平面）：
#   后面斜垫一张象牙白纸（右边和下边露出来，印着刻度和网点）；藏青纸反方向微转；三个页签是夹在纸上沿的纸签，高低、角度、颜色都不一样；
#   操作页：三条军令垫一张浅一档的纸条；兵法页：四条各占一张错开的小纸条，圆图标压在纸条左沿外，标签纸签伸出右沿；
#   右下角压一枚日轮纸片（伸出纸外），左下角一行花体落款 Ars Belli（兵法）。
#   画布 1180×1600；页签热区（BoardSigns.HeadV0 / HeadV1 / CellU0 / CellW）= y 50–190、x 从 60 起每格 303。
LW2, LH2 = 1180, 1600; SW, SH = 1000, 1380
TAB_U0, TAB_W, TAB_Y0, TAB_Y1 = 60, 303, 50, 190
PIN = ImageFont.truetype(R + 'ArtSource/Fonts/PinyonScript-Regular.ttf', 88)
def sheet_navy(page):
    rgb_, a_, _b = ck.real_sheet(SW, SH, ck.NAVY, pad=14, quarter=1, flip=(page == 1))
    hh, ww = a_.shape; D = 860; Ls = ck.sun_lines(D, 0.0, 0.96); wm = np.zeros((hh, ww), np.float32)
    cxw, cyw = int(ww * 0.9), int(hh * 0.9); y0, x0 = cyw - D // 2, cxw - D // 2
    ys0, ys1, xs0, xs1 = max(0, y0), min(hh, y0 + D), max(0, x0), min(ww, x0 + D)
    wm[ys0:ys1, xs0:xs1] = Ls[ys0 - y0:ys1 - y0, xs0 - x0:xs1 - x0]
    rgb_ = rgb_ + wm[..., None] * (np.array(ck.GOLD, np.float32) - rgb_) * 0.15
    rgb_ = ck.corner_dots(rgb_, 13, ww * 0.0, hh * 1.0, ww * 0.5, hh * 0.22, (40, 58, 100), 0.5)
    ns = ck.finish(rgb_, a_, 14, op=0.55); d = ImageDraw.Draw(ns)
    d.text((70, SH - 96), 'Ars Belli', font=PIN, fill=ck.GOLD + (235,), anchor='lm')                       # 花体落款
    d.line([(74, SH - 50), (330, SH - 50)], fill=ck.GOLD_D + (220,), width=2); ck.diamond(d, 338, SH - 50, 5, ck.GOLD + (255,))
    return ns, d
def sheet_ivory():
    rgb_, a_, _b = ck.real_sheet(1000, 1400, ck.IVORY, pad=14, quarter=1, flip=True)
    hh, ww = a_.shape; rgb_ = ck.corner_dots(rgb_, 13, ww * 1.0, hh * 1.0, ww * 0.42, hh * 0.24, ck.INK, 0.72)
    iv = ck.finish(rgb_, a_, 14, op=0.45); d = ImageDraw.Draw(iv); sep = (96, 74, 46, 200)
    for k in range(13):
        y = 150 + k * 78; d.line([(1000 - 40, y), (1000 - 40 - (30 if k % 4 == 0 else 15), y)], fill=sep, width=2)
    d.text((1000 - 46, 120), 'ORDO · BELLI', font=cz(20), fill=sep, anchor='rm')
    return iv
IVORY_SHEET = sheet_ivory(); SUN = Image.open(A + 'fx_sun.png').convert('RGBA').resize((236, 236), Image.LANCZOS)
def compose(ns, lang, active):
    im = Image.new('RGBA', (LW2, LH2), (0, 0, 0, 0))
    ck.paste_rot(im, IVORY_SHEET, LW2 / 2 + 54, LH2 / 2 + 44, -2.6)
    ck.paste_rot(im, ns, LW2 / 2 - 34, LH2 / 2 + 38, 1.0)      # 藏青纸的上沿在页签下半截后面：页签像是夹在纸上
    for c, label in enumerate(TABS[lang]):
        cx = TAB_U0 + TAB_W * (c + 0.5); cy = (TAB_Y0 + TAB_Y1) / 2; on = c == active
        if c == 2:   # 收起：一小片炮铜色纸签，斜得多一点
            w_, h_, col_, rot, tc, f = 236, 100, (50, 56, 68), -3.5, (206, 192, 164, 255), (wk(46) if lang == 'zh' else cz(32)); cy += 14
        elif on:
            w_, h_, col_, rot, tc, f = 304, 132, ck.IVORY, (2.2 if c == 0 else -2.0), ck.INK + (255,), (wk(58) if lang == 'zh' else cz(40)); cy -= 8
        else:
            w_, h_, col_, rot, tc, f = 284, 112, (36, 50, 84), (-1.6 if c == 0 else 1.8), (214, 196, 160, 255), (wk(54) if lang == 'zh' else cz(38)); cy += 10
        rgb_, a_, _b = ck.real_strip(w_, h_, col_, pad=9, flip=(c == 1), cut=('r' if c == 2 else ''))
        ck.paste_rot(im, ck.finish(rgb_, a_, 9), cx, cy, rot); d = ImageDraw.Draw(im)
        if c == 2:
            tw = d.textlength(label, font=f); d.polygon([(cx + tw / 2 + 2, cy - 7), (cx + tw / 2 + 24, cy - 7), (cx + tw / 2 + 13, cy + 8)], fill=tc); cx -= 12
        centered(d, cx, cy, label, f, tc)
    im.alpha_composite(SUN, (LW2 - 262, LH2 - 286))
    return im
def wrap(d, desc, df, maxw, lang):
    words = list(desc) if lang == 'zh' else desc.split(' '); lines, cur = [], ''
    for w_ in words:
        t_ = cur + w_ if lang == 'zh' else (cur + ' ' + w_).strip()
        if d.textlength(t_, font=df) > maxw and cur and not (lang == 'zh' and w_ in '，。；、：”）！？'): lines.append(cur); cur = w_   # 标点不放行首
        else: cur = t_
    return lines + [cur]
for lang in ('zh', 'en'):
    # ---- 操作页
    ns, d = sheet_navy(0); y0, rh = 104, 122
    rgb_, a_, _b = ck.real_strip(948, rh * 3 + 14, (32, 44, 74), pad=10, flip=True)                       # 三条军令垫一张浅一档的纸条
    ck.paste_rot(ns, ck.finish(rgb_, a_, 10, op=0.4), SW / 2 + 4, y0 + rh * 5.5, 0.7); d = ImageDraw.Draw(ns)
    for k, (icon, how, what) in enumerate(GEST[lang]):
        top = y0 + rh * k; cy = top + rh / 2
        ic = token_icon(icon[4:], lang) if icon.startswith('tok:') else (Image.open(A + icon + '.png').convert('RGBA').resize((94, 94), Image.LANCZOS) if icon == 'btn_pause' else hand_icon(icon))
        ns.alpha_composite(ic, (58, int(cy - 47))); d = ImageDraw.Draw(ns)
        hf = fit(d, how, (wk, 56) if lang == 'zh' else (cz, 42), SW - 180 - 260)
        d.text((178, cy), how, font=hf, fill=(236, 228, 208, 255), anchor='lm')
        d.text((SW - 86, cy), what, font=fit(d, what, (wk, 58) if lang == 'zh' else (cz, 44), 220), fill=(244, 198, 120, 255), anchor='rm')
        if k not in (3, 4, 5, 6, len(GEST[lang]) - 1): d.line([(178, top + rh), (SW - 86 - (140 if k % 2 else 0), top + rh)], fill=(150, 120, 80, 110), width=2)
    compose(ns, lang, 0).save(A + f'legend_{lang}.png')
    # ---- 兵法页：四条各占一张错开的小纸条
    ns, d = sheet_navy(1); y0, rh = 100, 286
    for k, (kind, title, desc, tag) in enumerate(TACT[lang]):
        cy = y0 + rh * k + rh / 2; off = 26 if k % 2 == 0 else 58
        rgb_, a_, _b = ck.real_strip(872, rh - 26, (30, 42, 72) if k % 2 == 0 else (24, 34, 60), pad=10, flip=bool(k % 2))
        ck.paste_rot(ns, ck.finish(rgb_, a_, 10, op=0.45), off + 436 + 16, cy, 0.9 if k % 2 == 0 else -0.8)
        tp = A + f'tac_{kind}.png'
        ti_ = Image.open(tp).convert('RGBA').resize((168, 168), Image.LANCZOS) if os.path.exists(tp) else diagram(kind)
        ns.alpha_composite(ti_, (off - 6, int(cy - ti_.height / 2))); d = ImageDraw.Draw(ns)             # 圆图标压在纸条左沿上
        tx = off + 186; tg = wk(38) if lang == 'zh' else cz(28)
        tgp = ck.chip(tag, tg, padx=18, h=72, pad=8, cut='l', flip=bool(k % 2))
        tf = fit(d, title, (wk, 62) if lang == 'zh' else (cz, 46), off + 872 - tx - tgp.width - 10); df = wk(48) if lang == 'zh' else cz(35)
        d.text((tx, cy - 62), title, font=tf, fill=(244, 198, 120, 255), anchor='lm')
        for li, ln in enumerate(wrap(d, desc, df, off + 872 - tx - 36, lang)[:3]): d.text((tx, cy + 4 + li * 60), ln, font=df, fill=(228, 218, 198, 255), anchor='lm')
        ck.paste_rot(ns, tgp, off + 872 + 30 - tgp.width / 2, cy - 74, 2.4 if k % 2 else -2.2); d = ImageDraw.Draw(ns)   # 标签纸签伸出纸条右沿
    compose(ns, lang, 1).save(A + f'legend_tactics_{lang}.png')
    # 收起后的小牌
    label = '操作 · 兵法' if lang == 'zh' else 'CONTROLS · TACTICS'
    lf = wk(56) if lang == 'zh' else cz(40)
    TW, TH = int(140 + ImageDraw.Draw(Image.new('RGBA', (8, 8))).textlength(label, font=lf) + 90), 160
    trgb, ta, (tx0, ty0, tx1, ty1) = ck.real_strip(TW, TH, ck.NAVY, pad=12, cut='l')      # 11-17：收起后的小牌 = 一张真的藏青纸条，右端是撕的，右下一片网点
    trgb = ck.corner_dots(trgb, 9, trgb.shape[1] * 1.0, trgb.shape[0] * 1.0, trgb.shape[1] * 0.35, trgb.shape[0] * 1.3, (44, 62, 104), 0.55)
    tb = ck.finish(trgb, ta, 12); td = ImageDraw.Draw(tb)
    td.line([(tx0 + 130, ty0 + 10), (tx0 + (tx1 - tx0) * 0.7, ty0 + 10)], fill=ck.GOLD_D + (210,), width=2)
    ic = hand_icon('hand_pinch', 104); tb.alpha_composite(ic, (24, TH // 2 - 52)); td = ImageDraw.Draw(tb)
    cyy = TH // 2
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
