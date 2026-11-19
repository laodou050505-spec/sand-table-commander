"""未来时代的岩石（11-13）：中古的岩石 / 立石阵贴图上有绿苔藓和土黄色，放在月面上不协调 → 同一个模型，只把贴图改成玄武岩灰：
  ArtSource/ModelsP2/<Rock_A|Rock_B|Standing_Stones>/final/ → ArtSource/ModelsP2/F_<名>/final/（模型原样拷贝，贴图去苔藓、偏冷灰、缩到 1K 省内存）。
之后 WTArt.ImportP2 照常接入，PropBuilder.Future 里把原件换成 F_<名>。
用法：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/make_future_rocks.py"""
import pathlib, shutil
import numpy as np
from PIL import Image

P2 = pathlib.Path(__file__).resolve().parent.parent / 'ArtSource/ModelsP2'
for name in ('Rock_A', 'Rock_B', 'Standing_Stones'):
    src, dst = P2 / name / 'final', P2 / ('F_' + name) / 'final'
    dst.mkdir(parents=True, exist_ok=True)
    for f in ('lod0.fbx', 'lod1.fbx', 'lod2.fbx', 'lod3.fbx', 'report.json'):
        if (src / f).exists(): shutil.copyfile(src / f, dst / f)
    a = np.asarray(Image.open(src / 'albedo.png').convert('RGB')).astype(np.float64) / 255
    lum = a @ np.array([0.299, 0.587, 0.114])
    mx, mn = a.max(-1), a.min(-1); sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
    green = (a[..., 1] >= a[..., 0]) & (a[..., 1] > a[..., 2]) & (sat > 0.12)        # 苔藓（绿 / 黄绿）
    # 整体去色成冷灰；苔藓处原来偏暗偏花，亮度往石头的中位数拉一拉，免得去色后留下一块块深斑
    med = np.median(lum[~green]) if (~green).any() else 0.4
    l2 = np.where(green, lum * 0.45 + med * 0.55, lum)
    l2 = 0.1 + 0.78 * l2                                                              # 稍微压暗、抬一点暗部：和月面碎石一个调子
    out = np.stack([l2 * 0.97, l2 * 0.99, l2 * 1.06], -1)
    Image.fromarray(np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).save(dst / 'albedo.png')
    print(f'F_{name}: 苔藓像素 {100 * green.mean():.1f}%，亮度中位 {np.median(l2):.2f}')
