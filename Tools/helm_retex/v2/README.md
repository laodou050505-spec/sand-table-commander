# 双面战盔 v2（11-02）

v1（单个 P2 生成整顶不对称盔）又窄又薄、贴图糊。v2 拆成两顶**对称**的盔分别走 P2，再拼成一顶：
- 参考图：ArtSource/Refs/Cover_HelmKnight（雕花骑士大盔）、Cover_HelmOrc（同轮廓的骷髅兽人盔，双角），均由 banana_pro 按 Refs/Cover_SplitHelm2 重设计。
- `combine.py`：兽人盔放大 1.3 倍让轮廓对齐骑士盔，两顶都在 y=0.03 处平面切开（骑士留 −y，兽人留 +y），中缝由骑士的金十字压住。
- `stage_join.py`：从旧战盔（Cover_SplitHelm/repaired.glb）切下金狮装到盔顶（Tripo 限流时的替代；以后可换单独生成的狮子）→ 焊接合并 → 导出给 xatlas。
- `bake_src.py`：三个来源材质各用自己的贴图烘到新 UV，另烘位置 / 法线 / 来源 ID。
- `classify2.py`：按来源分别聚类 + 位置规则分区；`compose2.py`：按分区重画 PBR（保留原图雕花、角纹、骷髅明暗；内壁和深凹做成哑光暗铁）。
其余步骤（xatlas、AO、导出）同上级目录。
