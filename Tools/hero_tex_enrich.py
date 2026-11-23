"""封面主角雕像的贴图增强（11-18 用户："模型的细节不够，看着太素了，把贴图整的丰富一些，同时注意贴图不要串色"）。

P2 的贴图这一步会把参考图的面漆洗淡（饱和度只剩 13、几乎全是灰蓝），而四视图里的深钢蓝、崩漆、锈迹是在的。
这里直接在 albedo 上补材质层。**所有处理都由像素自身的颜色驱动，不做任何方向性涂抹 / 模糊 / 位移**，
所以一个面片的颜色不会漏到旁边的面片上（贴图在 UV 空间里相邻的两块，在模型上往往离得很远 → 串色就是这么来的）：
  1. 分区：暖色（警示纹 / 黄铜 / 余烬）、冷亮（面罩冷光）、中性（甲片 / 岩石）——按 R、B 通道差和亮度分，阈值和实测分布对齐。
  2. 甲片：往深钢蓝压（原来发灰），提饱和、拉对比；亮处（受光的棱面）更蓝、暗处（凹槽）更黑。
  3. 崩漆：亮度高于局部水平的像素（也就是棱边受光面）叠一点裸钢的银灰——棱边才会掉漆，平面不会。
  4. 积污 / 锈迹：亮度低于局部水平的像素（凹槽、缝隙）叠暖褐色——污垢就是积在凹处。
  5. 面漆斑驳：一层很细的噪声（UV 空间里的高频，缩放后看不出是噪声，只会让大片面漆不再是死板的纯色）。
  6. 暖色和冷亮分区只做轻微提纯，不动色相：警示纹还是橙黑、黄铜还是黄铜、面罩还是冷青。
"局部水平"用的是中值滤波（不是高斯模糊）：只取同一片颜色里的中位数，不会把邻块的颜色平均进来。

用法：python3 Tools/hero_tex_enrich.py
      → 原贴图备份成 ArtSource/ModelsP2/Hero_Statue/final/albedo_p2.png（只备份一次）
      → 增强后写回 final/albedo.png，并出一张对比图 /private/tmp/sp2/tex_enrich.jpg
之后在 Unity 里重建封面（WarTable/6 搭封面）即可生效。
"""
import pathlib
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

D = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/ArtSource/ModelsP2/Hero_Statue/final')
SRC, BAK, OUT = D / 'albedo.png', D / 'albedo_p2.png', D / 'albedo.png'
if not BAK.exists():
    BAK.write_bytes(SRC.read_bytes())      # 第一次运行先留一份 P2 原图，重跑时都从它出发
a0 = np.asarray(Image.open(BAK).convert('RGB'), np.float32)
a = a0.copy()
h, w, _ = a.shape
r, g, b = a[..., 0], a[..., 1], a[..., 2]
lum = a.mean(-1)
used = lum > 8                                              # UV 没用到的区域是黑的，不动
warm = used & (r > b + 18)                                  # 警示纹 / 黄铜 / 岩石余烬
cool_hi = used & (b > r + 10) & (lum > 150)                 # 面罩冷光
armour = used & ~warm & ~cool_hi                            # 甲片 / 管线 / 岩石主体

# 局部亮度水平：中值滤波（同色块内的中位数，不把邻块平均进来）
local = ndi.median_filter(lum, size=9)
rel = np.clip((lum - local) / 26.0, -1.5, 1.5)              # >0 棱边受光面，<0 凹槽缝隙

STEEL_D = np.array([20, 27, 41], np.float32)                # 深钢蓝（暗部）
STEEL_L = np.array([74, 90, 118], np.float32)             # 受光的钢蓝（亮部）
BARE = np.array([150, 154, 164], np.float32)                # 崩漆露出的裸钢
GRIME = np.array([58, 44, 34], np.float32)                  # 积污 / 锈

# ── 甲片：按自身亮度在深钢蓝和受光钢蓝之间重映射，提饱和拉对比
t = np.clip((lum - 26) / 128.0, 0, 1)[..., None] ** 0.9
target = STEEL_D + (STEEL_L - STEEL_D) * t
m = armour[..., None]
a = np.where(m, a * 0.34 + target * 0.66, a)                # 保留原有的明暗层次，色相换成钢蓝

# ── 崩漆：只落在棱边受光面
chip = np.clip(rel, 0, 1.5) ** 1.6 * armour
rng = np.random.default_rng(7)
speck = np.asarray(Image.fromarray((rng.random((h // 2, w // 2)) * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR), np.float32) / 255
chip = chip * np.clip((speck - 0.42) / 0.3, 0, 1)           # 掉漆是斑驳的，不是整条棱都掉
a = a * (1 - chip[..., None] * 0.55) + BARE * (chip[..., None] * 0.55)

# ── 积污 / 锈迹：只落在凹槽缝隙
dirt = np.clip(-rel, 0, 1.5) ** 1.3 * armour
dirt = dirt * (0.55 + 0.45 * speck)
a = a * (1 - dirt[..., None] * 0.5) + GRIME * (dirt[..., None] * 0.5)

# ── 面漆斑驳：一层很细的噪声（让大片面漆不再是死板的纯色）
fine = np.asarray(Image.fromarray((rng.random((h // 6, w // 6)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
a = a + (fine - 0.5)[..., None] * 13.0 * armour[..., None]

# ── 暖色（警示纹 / 黄铜 / 余烬）：只提纯，不改色相
wm = warm[..., None]
mean_w = a0[warm].mean() if warm.any() else 1.0
a = np.where(wm, np.clip(a0 + (a0 - a0.mean(-1, keepdims=True)) * 0.85, 0, 255) * 1.06, a)

# ── 面罩冷光：稍微提亮提纯
cm = cool_hi[..., None]
a = np.where(cm, np.clip(a0 * 1.06 + np.array([0, 6, 14], np.float32), 0, 255), a)

a = np.clip(a, 0, 255)
a[~used] = a0[~used]                                        # UV 空白区照旧
Image.fromarray(a.astype(np.uint8)).save(OUT)

lum2 = a.mean(-1); sat0 = a0.max(-1) - a0.min(-1); sat1 = a.max(-1) - a.min(-1)
print('饱和度 中位 %.0f → %.0f ；亮度对比(标准差) %.0f → %.0f' % (
    np.median(sat0[used]), np.median(sat1[used]), a0[used].std(), a[used].std()))

# 对比图：左原图、右增强（各取贴图左上角一块看细节）
k = 760
cmp = Image.new('RGB', (2 * k + 10, k), (40, 40, 40))
cmp.paste(Image.fromarray(a0[:k, :k].astype(np.uint8)), (0, 0))
cmp.paste(Image.fromarray(a[:k, :k].astype(np.uint8)), (k + 10, 0))
cmp.save('/private/tmp/sp2/tex_enrich.jpg', quality=92)
print('写回', OUT)
