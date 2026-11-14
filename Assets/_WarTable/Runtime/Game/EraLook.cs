using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 时代的场景差异（11-11 用户：哥特和原来区别不大，场景至少地面要不一样）。
    /// 哥特时代：草地换成焦灰荒原（灰黑焦土 + 枯苔），干草换成锈红尘土，土路换成黑煤渣，河床偏冷灰；布景件整体压暗、偏冷。
    /// 用 MaterialPropertyBlock 改，不动共享材质；封面上切换时代时沙盘立刻跟着变。
    /// </summary>
    public class EraLook : MonoBehaviour
    {
        public Renderer terrain;
        /// <summary>哥特时代的地表贴图（焦土），替换青草贴图。</summary>
        public Texture2D ashTex;
        /// <summary>未来时代的地表贴图（月尘），没有就先用焦土。</summary>
        public Texture2D moonTex;
        public Texture2D moonDry, moonRoad, moonSand;
        /// <summary>现代时代的地表贴图（11-13 用户：注意地面的样子）：夏末干草原 / 营地干土 / 碎石路（Tools/make_modern_ground.py）。</summary>
        public Texture2D modTex, modDry, modRoad;
        /// <summary>深空风格的地表贴图（11-13，样图 C）：小行星风化层 / 铁矿砂 / 钢甲板路（Tools/make_space_ground.py）。</summary>
        public Texture2D spcTex, spcDry, spcRoad;
        /// <summary>河水：未来时代调成偏青发亮的“冷却液渠”。</summary>
        public Renderer water;
        /// <summary>布景件（帐篷、房子、树…）：哥特时代压暗、偏冷。</summary>
        public Renderer[] props;
        /// <summary>按时代换材质（11-11 用户：哥特不一定还要红蓝，旗帜也要换）：帅旗、帐篷、三角旗等。下标对齐。</summary>
        public Renderer[] swapR; public Material[] swapMed, swapGoth, swapFut;   // swapFut：未来时代（11-12），空项退回中古
        public Material[] swapMod;   // 现代时代（11-13），空项退回中古
        public Material[] swapSpc;   // 深空风格（11-13），空项退回中古
        /// <summary>按时代换网格（王国圆帐的条纹是顶点色：哥特版另存一份换了色的网格）。</summary>
        public MeshFilter[] meshF; public Mesh[] meshMed, meshGoth, meshFut, meshMod;
        EraOnly[] eraOnly;
        int applied = -1;
        MaterialPropertyBlock mpb;

        void Update()
        {
            if (Settings.Era == applied) return;
            applied = Settings.Era;
            Apply(applied);
        }

        /// <summary>11-12：按时代下标应用（原来只分哥特 / 非哥特）。还没有专属资源的时代（未来）先沿用中古的材质，地面单独调。</summary>
        void Apply(int era)
        {
            bool gothic = era == 1, future = era == 3, modern = era == 2, space = era == 4;
            mpb ??= new MaterialPropertyBlock();
            if (terrain)
            {
                terrain.GetPropertyBlock(mpb); mpb.Clear();
                if (gothic)
                {
                    if (ashTex) mpb.SetTexture("_GrassTex", ashTex);
                    mpb.SetColor("_GrassColor", new Color(1.3f, 1.25f, 1.2f));   // 焦土贴图本身很暗，提亮
                    mpb.SetColor("_GrassColor2", new Color(1.25f, 1.1f, 0.92f));
                    mpb.SetColor("_DryColor", new Color(0.8f, 0.6f, 0.48f));     // 锈红尘土
                    mpb.SetColor("_DirtColor", new Color(0.68f, 0.62f, 0.58f));   // 煤渣路
                    mpb.SetColor("_SandColor", new Color(0.62f, 0.64f, 0.68f));
                    mpb.SetColor("_RockColor", new Color(0.62f, 0.6f, 0.6f));
                }
                if (future)
                {
                    // 未来（样图 A：月面基地）。11-12 用户：第一版地面（焦土换色成灰颗粒、路还是土路）不好看 →
                    // 整套换贴图（Tools/make_future_ground.py）：月壤（带小陨石坑）/ 玄武岩碎屑 / 六边形地砖路 / 冷灰碎石；
                    // 贴图自己带颜色，各层倍率在 1 左右（第一版 1.5 过曝成雪地）
                    if (moonTex) mpb.SetTexture("_GrassTex", moonTex);
                    if (moonDry) mpb.SetTexture("_DryTex", moonDry);
                    if (moonRoad) mpb.SetTexture("_DirtTex", moonRoad);
                    if (moonSand) mpb.SetTexture("_SandTex", moonSand);
                    // 白甲的光棱军团站在太亮的月壤上不显眼 → 地面压到中灰（实测座位视角地面平均亮度约 125 / 255），白和深靛紫都衬得出来
                    mpb.SetColor("_GrassColor", new Color(0.76f, 0.77f, 0.8f));
                    mpb.SetColor("_GrassColor2", new Color(0.62f, 0.63f, 0.68f));
                    mpb.SetColor("_DryColor", new Color(1.15f, 1.15f, 1.2f));
                    mpb.SetColor("_DirtColor", moonRoad ? new Color(0.9f, 0.91f, 0.94f) : new Color(0.3f, 0.31f, 0.36f));
                    mpb.SetColor("_SandColor", new Color(1f, 1f, 1.02f));
                    mpb.SetColor("_RockColor", new Color(0.5f, 0.5f, 0.56f));
                }
                if (modern)
                {
                    // 现代（样图 A：夏末的干草原 + 碎石土路）。11-13 用户：“注意地面的样子”——第一版原封不动沿用中古的青草地，换了兵种却像没换时代。
                    // 青草 → 橄榄绿夹枯黄、露沙土的干草原；干草 → 踩实的营地干土；土路 → 压实的碎石路（带车辙）；河床、岩石不变。
                    // 贴图自己带颜色，倍率都在 1 上下；色块噪声混进去的第二种草色偏枯黄，草原不至于一片匀
                    if (modTex) mpb.SetTexture("_GrassTex", modTex);
                    if (modDry) mpb.SetTexture("_DryTex", modDry);
                    if (modRoad) mpb.SetTexture("_DirtTex", modRoad);
                    // 第一遍整体偏枯黄发亮，沙漠迷彩的赤砂兵站上去不显眼 → 草原往橄榄绿压暗一档，干土也收一点
                    mpb.SetColor("_GrassColor", new Color(0.8f, 0.9f, 0.76f));
                    mpb.SetColor("_GrassColor2", new Color(0.95f, 0.96f, 0.76f));
                    mpb.SetColor("_DryColor", new Color(0.86f, 0.86f, 0.76f));
                    mpb.SetColor("_DirtColor", new Color(1.0f, 1.0f, 1.0f));
                }
                if (space)
                {
                    // 深空（样图 C：小行星）：青草 → 炭灰风化层（带小撞击坑），干草 → 锈橙铁矿砂，土路 → 钢甲板路，河床 / 岩石压成冷深灰。
                    // 地面比未来的月壤暗一档（未来 ≈ 中灰），钢蓝白甲和焦红甲都衬得出来；没出贴图时退回未来的月面贴图
                    if (spcTex || moonTex) mpb.SetTexture("_GrassTex", spcTex ? spcTex : moonTex);
                    if (spcDry || moonDry) mpb.SetTexture("_DryTex", spcDry ? spcDry : moonDry);
                    if (spcRoad || moonRoad) mpb.SetTexture("_DirtTex", spcRoad ? spcRoad : moonRoad);
                    if (moonSand) mpb.SetTexture("_SandTex", moonSand);
                    // 进对局看：钢蓝白甲的我军站在冷灰地面上不显眼（同色相）→ 地面再压暗一档、偏暖褐，冷色的甲和暖色的地分开
                    mpb.SetColor("_GrassColor", new Color(0.66f, 0.62f, 0.59f));
                    mpb.SetColor("_GrassColor2", new Color(0.6f, 0.53f, 0.48f));
                    mpb.SetColor("_DryColor", new Color(0.9f, 0.86f, 0.84f));
                    mpb.SetColor("_DirtColor", new Color(0.6f, 0.63f, 0.7f));   // 甲板路要比风化层暗一档，不然两者一样亮、路看不出来
                    mpb.SetColor("_SandColor", new Color(0.62f, 0.56f, 0.52f));
                    mpb.SetColor("_RockColor", new Color(0.42f, 0.42f, 0.46f));
                }
                terrain.SetPropertyBlock(mpb);
            }
            if (water)
            {
                water.GetPropertyBlock(mpb); mpb.Clear();
                if (future) { mpb.SetColor("_ShallowColor", new Color(0.4f, 0.9f, 0.98f, 0.45f)); mpb.SetColor("_DeepColor", new Color(0.05f, 0.42f, 0.6f, 0.9f)); }
                // 深空：河道 = 一道缓缓流动的熔渣（余烬矿帮的名字由来），暗橙红、不过曝
                if (space) { mpb.SetColor("_ShallowColor", new Color(1f, 0.4f, 0.04f, 0.85f)); mpb.SetColor("_DeepColor", new Color(0.42f, 0.07f, 0.01f, 0.97f)); }
                water.SetPropertyBlock(mpb);
            }
            // 只属于某个时代的布景（哥特版教堂 / 排屋 / 钟楼 / 枯树和它们的中古原件）
            // 每次切换都重新找（只在换时代时跑一次）：缓存的话，编辑器里重摆布景后拿到的是已销毁的旧物体，什么都切不动
            eraOnly = (terrain && terrain.transform.parent ? terrain.transform.parent : transform).GetComponentsInChildren<EraOnly>(true);
            foreach (var e in eraOnly) if (e) e.gameObject.SetActive(e.ActiveIn(era));
            if (swapR != null)
                for (int i = 0; i < swapR.Length; i++)
                    if (swapR[i]) { var m = gothic ? swapGoth[i] : future && swapFut != null && i < swapFut.Length ? swapFut[i] : modern && swapMod != null && i < swapMod.Length ? swapMod[i] : space && swapSpc != null && i < swapSpc.Length ? swapSpc[i] : null; if (!m) m = swapMed[i]; if (m) swapR[i].sharedMaterial = m; }
            if (meshF != null)
                for (int i = 0; i < meshF.Length; i++)
                    if (meshF[i]) { var m = gothic ? meshGoth[i] : future && meshFut != null && i < meshFut.Length ? meshFut[i] : modern && meshMod != null && i < meshMod.Length ? meshMod[i] : null; if (!m) m = meshMed[i]; if (m) meshF[i].sharedMesh = m; }
            if (props != null)
                foreach (var r in props)
                {
                    if (!r) continue;
                    r.GetPropertyBlock(mpb); mpb.Clear();
                    if (gothic) mpb.SetColor("_WorldTint", new Color(0.7f, 0.72f, 0.8f));
                    else if (future || space) mpb.SetColor("_WorldTint", new Color(0.86f, 0.9f, 1f));
                    r.SetPropertyBlock(mpb);
                }
        }
    }
}
