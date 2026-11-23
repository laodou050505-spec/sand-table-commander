"""标题页的菜单：印在撕边纸片上的大字（11-18 定稿样图 Docs/Concepts/cover_1118/D2_realistic.png）。
每个菜单项三张图：shard_<键>_zh.png / shard_<键>_en.png（同一张纸片、同样大小，只是字不同）、shard_<键>_glow.png（纸片外面一圈暖金色的光，指着时从左往右亮出来）。
两种纸：象牙白羊皮纸印藏青字（像活字印上去的，略有磨损）；藏青纸烫金字。字是厚重的楔形衬线体：中文思源宋体 Heavy，英文 Cinzel 最粗一档。不描边、不投影。
纸片高度统一 250 px（游戏里再按各项的大小缩放），宽度按中英文里较宽的那个定。尺寸记在 Tools/cover_shards.json（CoverBuilder 读）。
python3 Tools/make_cover_shards.py → ArtSource/Cover/v6/alpha/ ，并出一张预览 /tmp/sp2/shards.jpg"""
import os, sys, json, numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage as ndi
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import collage_kit as ck
R = '/Users/ld/Documents/ChatGPT/WarTable/'; A = R + 'ArtSource/Cover/v6/alpha/'; F = R + 'ArtSource/Fonts/'
ZH = ImageFont.truetype(F + 'SourceHanSerifCN-Heavy.otf', 150)
EN = ImageFont.truetype(F + 'Cinzel.ttf', 128)
try: EN.set_variation_by_axes([900])
except Exception: pass
INDIGO = (22, 30, 56); H, PAD, GP = 250, 12, 46
# 键: (中文, 英文, 纸, 是否带三颗档位点)
ITEMS = {
    'begin': ('出征', 'BEGIN', 'ivory', False), 'era': ('风格', 'STYLE', 'indigo', False), 'tutorial': ('教学', 'TUTORIAL', 'indigo', False),
    'settings': ('设置', 'SETTINGS', 'indigo', False), 'quit': ('退出', 'QUIT', 'ivory', False),
    'language': ('中文', 'ENGLISH', 'ivory', False), 'speed': ('战速', 'SPEED', 'indigo', True), 'dim': ('暗度', 'DIMMING', 'indigo', True),
    'replace': ('重新定位', 'REPOSITION', 'indigo', False), 'back': ('返回', 'BACK', 'ivory', False),
    'era0': ('剑盾', 'BLADES', 'indigo', False), 'era1': ('铁誓', 'IRON OATH', 'ivory', False), 'era2': ('硝烟', 'GUNSMOKE', 'indigo', False),
    'era3': ('星尘', 'STARDUST', 'ivory', False), 'era4': ('深空', 'DEEP SPACE', 'indigo', False),
    # 11-19 用户：现在这版（单局对战）以后是游戏里的“演示”功能（直观看各兵种和打斗效果）→ 主菜单加一项。加在最后，前面各张纸片的随机纹理不变
    'demo': ('演示', 'SHOWCASE', 'indigo', False),
}
def width(text, font, gap):
    d = ImageDraw.Draw(Image.new('L', (8, 8))); return sum(d.textlength(ch, font=font) for ch in text) + gap * (len(text) - 1)
def text_mask(W_, text, font, gap, cx, cy):
    S = 2; f = font.font_variant(size=font.size * S)
    try: f.set_variation_by_axes([900])
    except Exception: pass
    m = Image.new('L', (W_ * S, H * S), 0); d = ImageDraw.Draw(m); x = cx * S - width(text, f, gap * S) / 2
    for ch in text:
        d.text((x, cy * S), ch, font=f, fill=255, anchor='lm'); x += d.textlength(ch, font=f) + gap * S
    return np.asarray(m.resize((W_, H), Image.LANCZOS), np.float32) / 255
sizes = {}; prev = []
# 11-19 晚 用户：主菜单六项要“指向中心”排成一圈、左右不能太开 → 这六张中文、英文各按自己的字宽出纸片和发光边
#   （原来共用一个宽度、按英文定：“演示”两个字拖着装 SHOWCASE 的长纸）。文件 shard_<键>_zh/en.png、shard_<键>_glow_zh/en.png，尺寸写 w_zh / w_en。
#   别的页的纸片照旧共用宽度（随机数按纸片尺寸设种子，改这六张不影响别的纸片）。
MAIN = ('begin', 'demo', 'era', 'tutorial', 'settings', 'quit')
MAIN_ASPECT = {'begin': 3.6, 'demo': 4.6, 'era': 3.9, 'tutorial': 4.3, 'settings': 4.2, 'quit': 3.7,   # 纸面宽 ÷ 高；英文最长的 SHOWCASE 要 4.47
               # 11-19 晚 用户：“其他页也要像标题页一样，按钮都朝向中心” → 设置页、风格页也按固定长度 + 象牙白版（装不下字时自动加长）
               'language': 3.9, 'speed': 4.4, 'dim': 4.6, 'replace': 4.4, 'back': 3.4,
               'era0': 3.8, 'era1': 4.2, 'era2': 4.0, 'era3': 4.3, 'era4': 4.5}
def make(i, key, zh, en, paper, pips, langs, gsuf, W_fixed=None, nsuf=''):
    zgap = 34 if len(zh) <= 2 else 10; egap = 9
    tw = max(width(zh, ZH, zgap) if 'zh' in langs else 0, width(en, EN, egap) if 'en' in langs else 0); room = 150 if pips else 0
    W_ = int(tw + 150 + room + PAD * 2) if W_fixed is None else max(W_fixed, int(tw + 150 + room + PAD * 2)); W_ += W_ % 2   # 固定长度也至少装得下字
    color = ck.IVORY if paper == 'ivory' else INDIGO
    rgb, a, (x0, y0, x1, y1) = ck.real_strip(W_, H, color, pad=PAD, flip=bool(i % 2))
    h, w = a.shape; yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    # 纸片不是规矩的长条：上沿斜切一刀（一头低 14 px 左右），每张方向不同
    k = (16 if i % 3 == 0 else -12 if i % 3 == 1 else 8); cut = np.clip(yy - (k * (xx / w - 0.5) + abs(k) * 0.5) + 0.5, 0, 1); a = a * cut
    if paper == 'ivory': rgb = rgb * 0.5 + np.array(ck.IVORY, np.float32) * 0.5      # 字后面的纸干净一点
    # 一角的铜版画排线（细斜线，往里渐隐）
    cx_, cy_ = (w, h) if i % 2 == 0 else (0, h); dist = np.hypot((xx - cx_) / (w * 0.3), (yy - cy_) / (h * 0.9))
    hatch = ((xx + yy * (1 if i % 2 == 0 else -1)) % 7 < 1.6).astype(np.float32) * np.clip(1 - dist, 0, 1) ** 1.3
    hc = np.array(ck.INK if paper == 'ivory' else (70, 92, 150), np.float32); rgb = rgb * (1 - hatch[..., None] * 0.55) + hc * hatch[..., None] * 0.55
    base = (rgb.copy(), a.copy())
    tcx = (w - room) / 2 + (6 if i % 2 else -6); tcy = h / 2 + 4
    rng = np.random.default_rng(100 + i)
    wear = np.asarray(Image.fromarray((rng.random((h // 3 + 2, w // 3 + 2)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
    for lang, text, font, gap in (('zh', zh, ZH, zgap), ('en', en, EN, egap)):
        if lang not in langs: continue
        rgb, a = base[0].copy(), base[1].copy()
        m = text_mask(w, text, font, gap, tcx, tcy)[:h]
        if paper == 'ivory':     # 活字油墨：墨色略有深浅，个别地方吃墨不足
            ink = np.array(ck.INK, np.float32) * (0.95 + 0.1 * wear[..., None]); al = m * np.clip(0.9 + (wear - 0.2) * 0.3, 0.88, 1.0)
        else:                     # 烫金：上亮下暗，带细碎的金箔颗粒
            t = np.clip((yy - (tcy - 70)) / 140, 0, 1)[..., None]
            ink = np.array([240, 206, 134], np.float32) * (1 - t) + np.array([176, 132, 66], np.float32) * t
            ink = ink * (0.95 + 0.1 * wear[..., None]) + (rng.random((h, w, 1)) > 0.993) * 26; al = m * np.clip(0.93 + (wear - 0.3) * 0.25, 0.9, 1.0)
        rgb = rgb * (1 - al[..., None]) + ink * al[..., None]
        im = ck.finish(rgb, a, PAD, op=0.5); d = ImageDraw.Draw(im)
        rc = ck.INK + (170,) if paper == 'ivory' else ck.GOLD_D + (230,)          # 两段细线：上沿一段、下沿一段，错开
        d.line([(PAD + 40, PAD + 22), (PAD + w * 0.5, PAD + 22 + k * 0.0)], fill=rc, width=2)
        d.line([(PAD + w * 0.42, PAD + h - 16), (PAD + w - 44, PAD + h - 16)], fill=rc, width=2); ck.diamond(d, PAD + w - 44, PAD + h - 16, 5, rc)
        im.save(A + f'shard_{key}_{lang}{nsuf}.png')
        if lang == 'zh' or key in ('begin', 'settings'): prev.append(im)
    # 发光：纸片的剪影往外晕开
    g = np.zeros((H + GP * 2, W_ + GP * 2), np.float32); g[GP + PAD:GP + PAD + h, GP + PAD:GP + PAD + w] = base[1]
    halo = np.clip(ndi.gaussian_filter(g, 15) * 1.35, 0, 1)
    out = np.zeros(g.shape + (4,), np.float32); out[..., :3] = (255, 208, 124); out[..., 3] = halo * 255 * 0.85
    Image.fromarray(out.astype(np.uint8)).save(A + f'shard_{key}_glow{gsuf}.png')
    return {'w': W_, 'h': H, 'pad': PAD, 'glow_pad': GP, 'pips': pips, 'text_cx': round(float(PAD + tcx), 1), 'text_w': round(float(tw), 1)}
for i, (key, (zh, en, paper, pips)) in enumerate(ITEMS.items()):
    if key in MAIN_ASPECT:
        # 11-19 晚续 用户：“看向哪个，哪个就变大、变成白色；面板大小不要被字的大小限制，长一点，和之前一样”
        #   → 每张一个固定的长度（纸面宽高比 MAIN_ASPECT，各不相同、不看字数，中英文同长），平时一律藏青纸，
        #     另出一张同形状的象牙白版 shard_<键>_<语言>_hot.png（被看着时淡入）。发光边两种语言同一张（_glow_zh / _glow_en 内容相同）。
        W = int(round(MAIN_ASPECT[key] * (H - 2 * PAD))) + 2 * PAD
        z = make(i, key, zh, en, 'indigo', pips, ('zh', 'en'), '_zh', W)
        make(i, key, zh, en, 'ivory', pips, ('zh', 'en'), '_en', W, '_hot')
        make(i, key, zh, en, 'indigo', pips, ('zh', 'en'), '_en', W)
        sizes[key] = dict(z, w_zh=z['w'], w_en=z['w'])
    else:
        sizes[key] = make(i, key, zh, en, paper, pips, ('zh', 'en'), '')
json.dump(sizes, open(R + 'Tools/cover_shards.json', 'w'), indent=1)
sheet = Image.new('RGB', (2000, 1500), (58, 56, 54)); x = y = 20; rowh = 0
for im in prev:
    if x + im.width > 1980: x = 20; y += rowh + 20; rowh = 0
    sheet.paste(im, (x, y), im); x += im.width + 20; rowh = max(rowh, im.height)
sheet.crop((0, 0, 2000, y + rowh + 20)).save('/tmp/sp2/shards.jpg', quality=90); print('shards', len(sizes))
