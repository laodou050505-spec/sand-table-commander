# 封面战盔精修（11-02）

P2 给 `Cover_SplitHelm` 出的贴图把反光直接画进了颜色、UV 碎成几千块，看着像塑料。这里按材质分区重画整套 PBR 贴图。
脚本里的工作目录写死为 `/tmp/helm`，先把本目录的脚本拷过去（`cluster_centers.npy` 也要拷）。B = Blender 可执行文件。

1. `B -b -P stage0.py -- ArtSource/ModelsP2/Cover_SplitHelm/repaired.glb /tmp/helm`：焊接接缝、导出网格。**要用 P2 修复后的 repaired.glb**；clean.glb 在骷髅前面留着一层隐藏面，骷髅会烂。
2. `python3 atlas.py`：xatlas 重展 UV（约 850 块，利用率 80%）。
3. `B -b -P stage1b.py`：把原贴图、位置、法线烘到新 UV，再把 src_albedo 转成 sRGB 的 `src_albedo_srgb.png`（转换代码见对话记录：线性 → sRGB）。
4. `python3 classify.py`：按颜色聚类 + 空间规则分区：0 银钢 / 1 黑铁 / 2 鎏金 / 3 角 / 4 骷髅 / 5 古铜铆钉。
5. `B -b -P bake_mask.py` 后，用 scipy 的 `ndimage.label` 把掩码标成 `charts.npy`（UV 块，骷髅按块投票用）。
6. `python3 compose.py` → `smooth.py`（平滑钢板内部顶点）→ `bake_ao.py` → 再跑一次 `compose.py` → `B -b -P export.py`。
7. 把 `out/` 里的 helm.fbx 和 helm_*.png 拷到 `Assets/_WarTable/Art/Models/Cover/Helm/`，在 Unity 里执行菜单 WarTable/6。

核对：`B -b -P render_check.py -- <UV0 贴图> <输出.png> [emit]` 渲染 5 个角度。
坑：Blender 烘焙的 EXR 要设 Non-Color，否则数值被当成 sRGB 编码；P2 原图里的明暗要留下来（骷髅、狮子靠它才有形体，见 compose.py 里的 shade）。

---

# 11-13 第三版战盔（Helm3）修正：重展 UV + 中缝切直

上面的流程是第二版（Cover_SplitHelm）的。现在封面用的是 `Art/Models/Cover/Helm3/helm3.fbx`（part0 银钢 … part7 内衬）+ 一张灰度明暗图 `helm3_albedo.png`（线性，Unity 里乘各部件底色）。

用户反馈“有些结构部位的贴图还是有问题，看着很不舒服”。定位到两个病根：

1. **UV 拉飞的三角形**（主要问题）。每个部件的 UV 总面积是贴图的 5–8 倍，有的三角形一个就横跨四分之一张贴图，在贴图上扫过一长条不相干的内容，显示出来是一片片尖角碎块。按表面积：银钢 19%、黑铁 15%、骷髅 20%、铆钉 27%。结构越密（眉额、面甲、中缝）越多。
2. **银钢 / 黑铁的分界在模型上是锯齿**。`helm3_retex.py` 当初按“三角面中心在中线哪一侧”分的两半，跨线的面左右交错（锯齿最深 44 mm）。

**怎么定位的**（下次贴图不对劲先这么查，别直接改贴图）：同一个机位拍四种——正常 / 只看明暗图（Unlit）/ 只看模型（纯灰、无贴图）/ 各部件纯色。碎块只在“只看明暗图”里有、模型本身是平的 → 贴图或 UV 的问题；再把“UV 边长 ÷ 实际边长 > 该部件中位数 4 倍”的面标红渲染，和碎块位置一对就清楚了。11-11 那次我以为是 P2 把反光画进了颜色，只压了暗斑，没对症。

**四步流程**（都从 `ArtSource/Cover/helm3_backup_1113/` 读，可重复跑；B = Blender，PY = /Library/Developer/CommandLineTools/usr/bin/python3）：

1. `B -b -P helm3_fix1113.py`：两半合起来沿中线平面切一刀（只加边）→ 中线附近的面按在哪一侧重新归属（分界偏离中线 0 mm）→ 三角化、标出拉飞的面 → `/tmp/h13/mesh.npz`、`stepA.blend`。
2. `PY helm3_atlas1113.py`：xatlas 整顶盔重新展 UV。细节多的部件先放大再展（`DENSITY`：骷髅 2.0、鎏金 1.8、铆钉 1.6），否则按表面积平均分，骷髅只有 3% 的贴图面积。
3. `B -b -P helm3_bake1113.py`：套上新 UV，把旧明暗图（按旧 UV 采样）、AO、曲率烘到新 UV，导出 `helm3.fbx`。
4. `PY helm3_shade1113.py`：合成。两半盔壳不用旧图，= 中性灰 × AO 渍洗 × 锻打起伏 + 棱边磨亮 + 细划痕；其余部件沿用旧明暗，拉飞的面用同部件最近的好像素补。

然后在 Unity 里跑 `CoverBuilder.Build(); GameSetup.Setup()`。

**坑**
- 中脊（中缝银钢一侧那条波浪边饰带，宽约 3 cm、高约 2 cm）**模型别动**。试过带内顶点平滑（最多挪了 185 mm）和只沿中缝走向做平均，同光照近景对比都比原样差（成了一道道皱褶）。
- 棱边别用曲率图找：盔壳网格稀，曲率逐顶点插值，整片穹顶都偏“凸”（中位数 0.76），还带三角形渐变。用 AO 的高通（AO − 周围平均）。
- 划痕别太亮太长（0.10、70 像素时穹顶上像大理石纹）；现在 0.055、8–34 像素。
- 打开 `.blend` 存档后新建的世界 / 材质不一定自带 Background / Principled 节点，要自己建。
