"""现代时代布景贴图修正（11-13）：去掉 AI 自己写上去的假字样（要求：不带文字和真实标识）。
  做法：把鲜红像素用四周的颜色补平（归一化卷积往里填）。用法里给名字才处理。
  注意先分清是不是真的字：M_Container 上那行“红字母”其实是一整道红漆被坏掉的烘焙撕碎了（根因在 blender_repair，已修：specs 里 bake=nearest、src=raw），不该抹。
改的是 ArtSource/ModelsP2/<名>/final/albedo.png（WTArt.ImportP2 从这里拷进 Assets）；原图备份在 ArtSource/Modern/texfix_src/<名>_albedo.png，幂等。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/modern_prop_tex_fix.py <名字…>"""
import pathlib, shutil
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

PROJ = pathlib.Path(__file__).resolve().parent.parent
BAK = PROJ / 'ArtSource/Modern/texfix_src'


def fill(a, m, rounds=40):
    """把 m 标出来的像素用外面的颜色往里填：反复做“只算未遮住像素”的模糊（归一化卷积）。"""
    out = a.copy(); known = ~m
    for _ in range(rounds):
        if known.all(): break
        w = ndi.uniform_filter(known.astype(np.float64), 7)
        s = np.stack([ndi.uniform_filter(out[..., c] * known, 7) for c in range(3)], -1)
        new = (w > 0.05) & ~known
        out[new] = s[new] / w[new][:, None]; known = known | new
    return out


def red_text(name, grow=5):
    src = PROJ / f'ArtSource/ModelsP2/{name}/final/albedo.png'; bak = BAK / f'{name}_albedo.png'
    BAK.mkdir(parents=True, exist_ok=True)
    if not bak.exists(): shutil.copy(src, bak)
    a = np.asarray(Image.open(bak).convert('RGB')).astype(np.float64) / 255
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = (r > 0.45) & (g < 0.32) & (b < 0.32) & (r - np.maximum(g, b) > 0.25)      # 鲜红（锈红的 g 更高、没这么艳）
    m = ndi.binary_dilation(m, iterations=grow)
    out = fill(a, m)
    # 补上的地方太平：叠一点原图别处的细颗粒
    grain = a - ndi.gaussian_filter(a, (3, 3, 0)); out[m] = np.clip(out[m] + np.roll(grain, 97, 1)[m] * 0.8, 0, 1)
    Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(src)
    print(name, '抹掉鲜红像素', round(float(m.mean()) * 100, 2), '%')


if __name__ == '__main__':
    import sys
    for n in sys.argv[1:]: red_text(n)
