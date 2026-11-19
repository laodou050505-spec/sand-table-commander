using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WarTable.Game;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 标题页中间那尊重甲骑士（11-18）。
    /// **11-18 晚用户：按雕像参考图做一尊完整模型就行，不要拆件、不要动作**——此前按结构拆成 9 个部件拼装，接缝和关节怎么调都"太奇怪"。
    /// 现在是一整件：参考图 `ArtSource/Hero/design/statue_hi.png`（双手扶战锤、站在焦岩上，连底座一体）→ `ArtSource/Refs/Hero_Statue/image.png`
    /// → `Tools/p2_pipeline.py -- run Hero_Statue`（四视图 → 白模 → 审计 → 【人工看素模图】→ approve → 贴图 → 清理）→ 这里 `WTArt.ImportP2` 导入。
    /// 静止不动（`CoverHero` 只留一个很慢的呼吸起伏，整尊像一起动，不转关节）。模型没做好时返回 null，封面中间先空着。
    /// </summary>
    public static class CoverHeroBuilder
    {
        /// <summary>整尊像的高（米，含岩石底座）和底座落点（封面本地）。</summary>
        public const float Height = 0.44f;
        public static readonly Vector3 Feet = new(0f, -0.155f, 0f);
        const string Name = "Hero_Statue";
        /// <summary>主角单独在第 2 个光照层（11-19 下午）：场景的 Sun 只照第 1 层 → 不照主角，主角只吃封面自己的三盏灯（CoverBuilder.HeroLit）+ 环境光，明暗才压得住。</summary>
        public const uint HeroLayer = 2u;

        // ── 11-19 傍晚的调色板（照 D2 样图；改这里再“搭封面”就行）
        static readonly Color Tint = new(0.88f, 0.83f, 0.78f);   // 第二版 (0.80,0.70,0.60) 整身成了铜橙色，钢甲底色被盖没；用户：不必和样图一样，要的是氛围
        const float Metal = 0.78f, Smooth = 0.44f, EmissionGain = 1.25f, ProbeGain = 1.0f, BackGlowA = 0.30f;
        /// <summary>面罩镜片中心在整尊像包围盒里的归一化坐标（Tools/hero_emission.py 打印的那个）。</summary>
        static readonly Vector3 LensAt = new(0.541f, 0.898f, 0.639f);
        static readonly Color LensC = new(0.35f, 1f, 0.90f), LavaC = new(1f, 0.42f, 0.10f);
        // ── 11-19 质感贴图（Tools/hero_pbr.py）：有 mask.png 时金属度 / 光滑度 / 遮蔽逐像素取贴图，上面的 Metal / Smooth 只在没有贴图时兜底
        const float SmoothScale = 1f, BumpScale = 1f, OcclusionStrength = 0.8f;
        const float LensI = 0.0012f, LavaI = 0.005f;   // 点光离甲片只有 2–8 cm，强度按距离平方衰减：0.001 量级就够（第一版给 0.9 / 1.6，上下半身被染透）

        static void Key(Material m, string keyword, Object on) { if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword); }

        public static bool Ready() => File.Exists($"ArtSource/ModelsP2/{Name}/final/lod0.fbx");

        public static CoverHero Build(Transform root)
        {
            var pf = Ready() ? WTArt.ImportP2(Name, Height) : null;
            if (!pf) { Debug.LogWarning($"[Cover] 主角模型还没做好（ArtSource/ModelsP2/{Name}/final），封面中间先空着"); return null; }
            var hero = new GameObject("Hero").transform; hero.SetParent(root, false); hero.localPosition = Feet;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, hero);
            PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            g.name = "Statue"; g.transform.localPosition = Vector3.zero;
            var lg = g.GetComponent<LODGroup>(); if (lg) Object.DestroyImmediate(lg);     // 封面只有这一尊、离得近：只留最精细的一档
            foreach (Transform c in g.transform) if (c.name != "LOD0") c.gameObject.SetActive(false);
            const string dir = "Assets/_WarTable/Art/Models/P2/" + Name + "/";
            Texture2D Tex(string file, bool srgb, bool normal = false)
            {
                var path = dir + file; if (!File.Exists(path)) return null;
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.sRGBTexture != srgb || (ti.textureType == TextureImporterType.NormalMap) != normal || ti.anisoLevel != 8 || ti.maxTextureSize != 2048))
                {
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = srgb;
                    ti.anisoLevel = 8; ti.maxTextureSize = 2048; ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter; ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            // 11-19 用户：“现在的贴图很糊”“质量很低” → LOD0 重新展了贴图坐标、拆成两个材质槽（hero_a 上半身 / hero_b 下半身 + 岩座），
            // 每槽一套 2048 的贴图，从 Tripo 8K 原图重烘（盔甲 0.79 → 0.28 mm 一个像素）。流程见 Tools/hero_hd_mesh.py 开头。
            // 每套四张（Tools/hero_pbr.py）：albedo_pbr 底色 / mask（R 金属度、G 遮蔽、A 光滑度）/ normal 法线 / emission 自发光（面罩青光、岩座熔岩光）
            Material HeroMat(string tag)
            {
                var tex = Tex($"albedo_pbr_{tag}.png", true); var mask = Tex($"mask_{tag}.png", false); var bump = Tex($"normal_{tag}.png", false, true); var emi = Tex($"emission_{tag}.png", true);
                if (!tex || !mask || !bump) Debug.LogWarning($"[Cover] 主角第 {tag} 套贴图不全，先跑 Tools/hero_pbr.py");
                return BoardBuilder.Mat("M_Cover_Hero_" + tag.ToUpper(), "Universal Render Pipeline/Lit", m =>
                {
                    // 11-19 傍晚：照 D2 样图压成暖调的深色金属——贴图是偏冷的钢蓝，乘一个暖色把它拉回中性炮铜
                    m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Tint);
                    m.SetFloat("_Metallic", Metal); m.SetFloat("_Smoothness", mask ? SmoothScale : Smooth);
                    // 11-19 用户：“把质感做好，不要看着很塑料”——塑料感 = 全身同一个金属度和光滑度、没有法线、缝里照样反光 → 逐像素的质感贴图
                    m.SetTexture("_MetallicGlossMap", mask); m.SetFloat("_SmoothnessTextureChannel", 0f); Key(m, "_METALLICSPECGLOSSMAP", mask);
                    m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", OcclusionStrength); Key(m, "_OCCLUSIONMAP", mask);
                    m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", BumpScale); Key(m, "_NORMALMAP", bump);
                    m.SetTexture("_EmissionMap", emi); m.SetColor("_EmissionColor", emi ? Color.white * EmissionGain : Color.black); Key(m, "_EMISSION", emi); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                });
            }
            Material matA = HeroMat("a"), matB = HeroMat("b");
            // 哪个材质槽是上半身：按几何判断——子网格包围盒中心更高的那个用第一套（模型导入时不带材质名，槽位顺序也不保证，11-19 两个槽都挂成了第一套、下半身全花）
            var l0 = g.transform.Find("LOD0"); var lm = l0.GetComponent<MeshFilter>().sharedMesh;
            var mats = new Material[lm.subMeshCount];
            if (lm.subMeshCount == 2)
            {
                float h0 = l0.TransformPoint(lm.GetSubMesh(0).bounds.center).y, h1 = l0.TransformPoint(lm.GetSubMesh(1).bounds.center).y;
                mats[0] = h0 > h1 ? matA : matB; mats[1] = h0 > h1 ? matB : matA;
            }
            else { Debug.LogWarning("[Cover] 主角 LOD0 不是两个材质槽，先跑 Tools/hero_hd_mesh.py"); for (int k = 0; k < mats.Length; k++) mats[k] = matA; }
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform == l0) r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;   // 封面悬在空中：不往真实地板 / 沙盘上投影
                r.renderingLayerMask = HeroLayer;
            }
            // ── 主角自己的反射环境（没有顶光）：一个刚好包住雕像的盒子，优先级比封面通用那份高；立体标题在 z 0.13 以外，不会被它管到
            var hp = hero.gameObject.AddComponent<ReflectionProbe>();
            hp.mode = ReflectionProbeMode.Custom; hp.customBakedTexture = CoverBuilder.BakeHeroStudio();
            hp.center = new Vector3(0f, Height * 0.5f + 0.02f, 0f); hp.size = new Vector3(0.36f, Height + 0.12f, 0.25f); hp.importance = 20; hp.intensity = ProbeGain;

            // ── 亮点只有两处（样图）：面罩青光、岩座熔岩光。各一盏很小的点光把光染到旁边的甲片上 + 一个叠加的小光晕；都挂在雕像下面，跟着呼吸一起动
            var lod0 = g.transform.Find("LOD0").GetComponent<Renderer>(); var bb = lod0.bounds;
            CoverHeroDecals.Build(g.transform, lod0, HeroLayer);   // 身上的贴花：右胸公司图标、肩甲团徽 / 编号、战绩刻痕、羽纹、日轮（11-19）
            Vector3 At(Vector3 n) => bb.min + Vector3.Scale(n, bb.size);          // 包围盒里的归一化坐标 → 世界
            var fwd = root.forward;                                                // 封面本地 +z = 朝玩家
            Light Pt(string name, Vector3 world, Color c, float intensity, float range)
            {
                var l = new GameObject(name).AddComponent<Light>(); l.transform.position = world; l.transform.SetParent(g.transform, true);
                l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
                CoverBuilder.HeroLit(l); return l;
            }
            void Glow(string name, Vector3 world, float size, Color c, int queue)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>()); q.name = name;
                q.transform.position = world; q.transform.rotation = root.rotation * Quaternion.Euler(0f, 180f, 0f); q.transform.localScale = new Vector3(size, size, 1f);
                q.transform.SetParent(g.transform, true);
                var qr = q.GetComponent<Renderer>(); qr.shadowCastingMode = ShadowCastingMode.Off;
                qr.sharedMaterial = CoverBuilder.Unlit("M_C6_" + name, CoverBuilder.Dot(), c, queue, additive: true);
            }
            var lens = At(LensAt);
            Pt("LensLight", lens + fwd * 0.022f, LensC, LensI, 0.09f);
            Glow("LensGlow", lens + fwd * 0.004f, 0.052f, new Color(LensC.r, LensC.g, LensC.b, 0.38f), 3008);
            var lava = At(new Vector3(0.5f, 0.10f, 0.78f));
            Pt("LavaLight", lava + fwd * 0.03f, LavaC, LavaI, 0.24f);
            Glow("LavaGlow", At(new Vector3(0.5f, 0.05f, 0.5f)) + fwd * 0.02f, 0.26f, new Color(LavaC.r, LavaC.g, LavaC.b, 0.13f), 3007);
            // 身后一团暖色尘光（在金线日轮前面、主角后面）：把深色的主角从压暗的房间里托出来
            Glow("BackGlow", At(new Vector3(0.5f, 0.56f, 0f)) - fwd * 0.03f, 0.62f, new Color(1f, 0.60f, 0.30f, BackGlowA), 2993);

            var ch = hero.gameObject.AddComponent<CoverHero>(); ch.statue = g.transform; ch.size = Height; ch.Capture();
            return ch;
        }
    }
}
