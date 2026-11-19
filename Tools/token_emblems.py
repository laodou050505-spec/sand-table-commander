"""令牌令徽（11-12 用户：令牌上的图案不好看 → 选“保留箭头和盾，只做精细”）：
Tripo seedream_v5 出的书法笔意黑白剪影 ArtSource/Tokens/gen_<令>/<版>.png → 清理（去掉杂笔、裁到墨迹）→ ArtSource/Tokens/emblem_<令>.png
→ Tools/title_contours.py 提轮廓 → ArtSource/Tokens/emblem_<令>.json（make_token_models.py 刻槽嵌金、make_order_badges.py 画反馈牌都用它）。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/token_emblems.py"""
import json, subprocess
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

R = '/Users/ld/Documents/ChatGPT/WarTable/'
SRC = {'charge': 'gen_charge/a.png', 'hold': 'gen_hold/b.png', 'retreat': 'gen_retreat/a.png'}
for key, f in SRC.items():
    a = np.asarray(Image.open(R + 'ArtSource/Tokens/' + f).convert('L')).astype(float)
    ink = a < 110
    lab, n = ndi.label(ink)
    sizes = ndi.sum(ink, lab, range(1, n + 1))
    keep = np.zeros(n + 1, bool)
    big = sizes.max()
    for i, s in enumerate(sizes, 1):
        if s < big * 0.004: continue                      # 飞白碎点
        ys, xs = np.where(lab == i)
        # 坚守：盾面下半部原图多出一个小字（不是铆钉也不是盾环）→ 去掉：重心在盾面下 35% 且不是最大的那块（下排铆钉在 55% 处，要留下）
        if key == 'hold' and s < big * 0.2 and ys.mean() > np.where(lab == np.argmax(sizes) + 1)[0].min() + 0.65 * np.ptp(np.where(lab == np.argmax(sizes) + 1)[0]): continue
        keep[i] = True
    m = keep[lab]
    if key in ('charge', 'retreat'): m = ndi.binary_dilation(m, iterations={'charge': 30, 'retreat': 10}[key])   # 箭杆原图很细，刻在 11 cm 的令牌上发飘 → 笔画加粗
    ys, xs = np.where(m); pad = 40
    m = m[ys.min() - pad:ys.max() + pad, xs.min() - pad:xs.max() + pad]
    out = R + f'ArtSource/Tokens/emblem_{key}.png'
    Image.fromarray(np.where(m, 0, 255).astype(np.uint8)).save(out)
    subprocess.run(['python3', R + 'Tools/title_contours.py', out, R + f'ArtSource/Tokens/emblem_{key}.json', '1', '3'], check=True)
    d = json.load(open(R + f'ArtSource/Tokens/emblem_{key}.json'))
    print(key, m.shape, 'pieces', len(d['chars'][0]), 'holes', sum(len(p['holes']) for p in d['chars'][0]))
