"""现代时代兵种贴图修正（11-13）。convert_demo_assets.py 重跑会覆盖贴图，跑完它之后再跑一次本脚本。
对象：Assets/_WarTable/Art/Models/Demo/troops/m_*_tex/Color_*.png（取最新的一张）
  1. 去字：AI 贴图会自己在背心的荧光色块上写字（kd_spearman 背后出现过 “POLICE”）——要求是不带文字和真实标识。
     判据：深色像素（亮度 < 0.5）、周围一圈大部分是荧光黄绿 → 用附近荧光色的中位数填掉。只动荧光块里面的东西，别处不碰。
  2. 轻微加对比（S 曲线）：P2 贴图偏灰平，和中古 / 未来的棋子摆一起显得淡。
幂等：原图备份在 ArtSource/Modern/texfix_src/<名>.png + 输出的 md5；贴图仍是上次的输出就从备份重算。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/modern_tex_fix.py [名字…]（不带参数 = 全部 m_*）"""
import glob, hashlib, json, pathlib, sys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

PROJ = pathlib.Path(__file__).resolve().parent.parent
DEMO = PROJ / 'Assets/_WarTable/Art/Models/Demo'
SRC = PROJ / 'ArtSource/Modern/texfix_src'


def rgb2hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1); d = mx - mn
    h = np.zeros_like(mx); m = d > 1e-6
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6; h[gm] = (b - r)[gm] / d[gm] + 2; h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60, np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0), mx


def md5(p): return hashlib.md5(p.read_bytes()).hexdigest()


def fix(a):
    h, s, v = rgb2hsv(a)
    hiv = (h > 55) & (h < 100) & (s > 0.45) & (v > 0.5)                      # 荧光黄绿
    frac = ndi.uniform_filter(hiv.astype(np.float32), 21)                    # 周围 21×21 里荧光色的占比
    text = (~hiv) & (v < 0.5) & (frac > 0.45)                                # 荧光块里的深色笔画
    text = ndi.binary_dilation(text, iterations=2) & (frac > 0.3) & ~(hiv & (v > 0.8))
    n = int(text.sum())
    if n:
        iy, ix = ndi.distance_transform_edt(~(hiv & ~text), return_distances=False, return_indices=True)
        a = a.copy(); a[text] = a[iy[text], ix[text]]
    a = np.clip(a + 0.1 * (a - 0.5) * (1 - np.abs(2 * a - 1)), 0, 1)        # 轻微 S 曲线
    return a, n


def run(name):
    files = glob.glob(str(DEMO / 'troops' / f'{name}_tex' / 'Color_*.png'))
    if not files: print(name, '找不到贴图'); return
    p = max((pathlib.Path(f) for f in files), key=lambda f: f.stat().st_mtime)
    SRC.mkdir(parents=True, exist_ok=True)
    bak, rec = SRC / f'{name}.png', SRC / f'{name}.json'
    last = json.loads(rec.read_text()).get('out') if rec.exists() else None
    if not bak.exists() or md5(p) != last: bak.write_bytes(p.read_bytes())
    im = Image.open(bak); a = np.asarray(im.convert('RGB')).astype(np.float64) / 255
    out, n = fix(a)
    res = Image.fromarray(np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8))
    if im.mode == 'RGBA': res.putalpha(im.getchannel('A'))
    res.save(p); rec.write_text(json.dumps({'out': md5(p)}))
    print(f'{name:20s} 去字像素 {n:6d}（{100 * n / a.shape[0] / a.shape[1]:.2f}%）')


if __name__ == '__main__':
    want = sys.argv[1:]
    for d in sorted((DEMO / 'troops').glob('m_*_tex')):
        n = d.name[:-4]
        if not want or n in want: run(n)
