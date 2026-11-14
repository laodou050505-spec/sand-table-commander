// 美术资源接入：FBX / 贴图导入设置、P2 模型 → 带 LOD 的预制体（改自 BANG 的 BangArt）。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WarTable.EditorTools
{
    public class WTModelPostprocessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/_WarTable/Art/Models")) return;
            var mi = (ModelImporter)assetImporter;
            bool demo = assetPath.StartsWith("Assets/_WarTable/Art/Models/Demo");   // 原型角色：动画由 DemoModelPostprocessor 管
            mi.materialImportMode = ModelImporterMaterialImportMode.None;   // 材质统一由 WTArt 分配
            if (!demo) mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.globalScale = 1f;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_WarTable/Art")) return;
            var ti = (TextureImporter)assetImporter;
            if (ti.maxTextureSize > 2048) ti.maxTextureSize = 2048;   // 用户要求：贴图不超过 2K
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            // 用户：模型质感要强。模型贴图用 Kaiser 生成 mipmap（远处不糊成一片）+ 轻微负 mip 偏移 + 三线性过滤
            bool model = assetPath.Contains("/Models/");
            if (model) { ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter; ti.mipMapBias = -0.25f; ti.filterMode = FilterMode.Trilinear; }
            var a = ti.GetPlatformTextureSettings("Android");
            bool ui = assetPath.StartsWith("Assets/_WarTable/Art/UI");
            // 原型角色 / 武器贴图只有 512：用 ASTC 4x4 保住细节（几乎不占内存）；P2 布景 2K 用 5x5（6x6 糊掉笔触）
            bool small = assetPath.Contains("/Models/Demo/");
            a.overridden = true; a.maxTextureSize = Mathf.Min(2048, ti.maxTextureSize);
            a.format = ui || small ? TextureImporterFormat.ASTC_4x4 : model ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a);
        }
    }

    public static class WTArt
    {
        public const string P2Src = "ArtSource/ModelsP2";
        public const string P2Dst = "Assets/_WarTable/Art/Models/P2";
        public const string PrefabDir = "Assets/_WarTable/Prefabs/P2";

        /// <summary>LOD 切换屏高：坐着看全盘时布景件用 LOD1（三档都查过 0 破面），俯身凑近才切 LOD0。</summary>
        public static readonly float[] LodHeights = { 0.12f, 0.05f, 0.012f, 0.0005f };   // LOD3（约 4% 面）只给很远很小的；有机形体（树、灌木）在座位距离用 LOD3 会出棱角
        /// <summary>树：面数最多（一丛 4~5 万），座位距离用最低一档（7 千），俯身凑近才切高档。</summary>
        /// <summary>
        /// 每件最低能降到哪一档（逐件看过 LOD0/2/3 近拍）：薄片和细杆多的东西 P2 低档会塌成歪块——
        /// 货车 LOD2 起就碎；农舍 C、货摊、风车、兵器架（盾变方）、废塔、兽皮帐、木桶堆 LOD3 变形。
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> MaxLod = new()
        {
            ["Wagon_Supply"] = 1, ["Cottage_C"] = 2, ["Market_Stall"] = 2, ["Windmill"] = 2, ["Weapon_Rack"] = 2,
            ["Watchtower_Ruin"] = 2, ["Tent_Clan_A"] = 2, ["Barrels_Crates"] = 2,
        };
        public static readonly float[] TreeLodHeights = { 0.24f, 0.1f, 0.02f, 0.0005f };

        public static GameObject Model(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        /// <summary>
        /// 把 P2 管线产出（final/lod0..2.fbx + albedo.png，且 report 全部合格）做成预制体：底部落在 y=0、水平居中、缩放到目标高度（米）。
        /// 没有合格产出时返回 null。
        /// </summary>
        public static GameObject ImportP2(string name, float targetHeight, float gloss = 0.12f)
        {
            var src = Path.Combine(P2Src, name, "final");
            var rep = Path.Combine(src, "report.json");
            if (!File.Exists(Path.Combine(src, "lod0.fbx")) || !File.Exists(rep) || !File.ReadAllText(rep).Contains("\"pass\": true")) return null;
            var dst = $"{P2Dst}/{name}";
            Directory.CreateDirectory(dst);
            bool changed = false;
            foreach (var f in new[] { "lod0.fbx", "lod1.fbx", "lod2.fbx", "lod3.fbx", "albedo.png" })
            {
                var s = Path.Combine(src, f); var d = Path.Combine(dst, f);
                if (File.Exists(s) && (!File.Exists(d) || File.GetLastWriteTimeUtc(s) > File.GetLastWriteTimeUtc(d))) { File.Copy(s, d, true); changed = true; }
            }
            if (changed) AssetDatabase.Refresh();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/albedo.png");
            // 11-12 未来时代的建筑：同目录有 emis.png（Tools/future_tex_fix.py 从贴图里抠出的灯 / 光环 / 缝光）就挂到自发光
            var emis = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/emis.png");
            var mat = BoardBuilder.Mat("M_" + name, "WarTable/MiniLit", m =>
            {
                m.SetTexture("_BaseMap", tex); m.SetFloat("_Gloss", gloss); m.SetFloat("_UseVertexColor", 0);
                m.SetTexture("_EmissionMap", emis ? emis : Texture2D.whiteTexture); m.SetColor("_EmissionColor", emis ? new Color(0.9f, 0.9f, 0.9f) : Color.black);
            });
            if (name.StartsWith("F_") && !name.StartsWith("F_Rock") && !name.StartsWith("F_Standing")) { mat.SetFloat("_Gloss", name == "F_Hive" ? 0.5f : 0.4f); mat.SetFloat("_GlossSize", 48f); EditorUtility.SetDirty(mat); }   // 陶瓷 / 甲壳的高光（Look 会压到 0.05）

            var root = new GameObject(name);
            var lods = new System.Collections.Generic.List<LOD>();
            Bounds b = default; bool hasB = false;
            var lh = (float[])(name.StartsWith("Tree_") ? TreeLodHeights : LodHeights).Clone();
            int maxLod = MaxLod.TryGetValue(name, out var ml) ? ml : lh.Length - 1;
            lh[maxLod] = 0.0005f;   // 最后一档一直显示到很远
            for (int i = 0; i <= maxLod; i++)
            {
                var m = Model($"{dst}/lod{i}.fbx");
                if (!m) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(m);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "LOD" + i;
                inst.transform.SetParent(root.transform, false);
                // P2 模型正面朝 −X：转 90° 让预制体正面朝 +Z（Unity 惯例）
                inst.transform.localRotation = Quaternion.Euler(0, 90, 0) * inst.transform.localRotation;
                inst.transform.localPosition = Quaternion.Euler(0, 90, 0) * inst.transform.localPosition;
                var rs = inst.GetComponentsInChildren<Renderer>();
                foreach (var r in rs) { r.sharedMaterial = mat; if (!hasB) { b = r.bounds; hasB = true; } else if (i == 0) b.Encapsulate(r.bounds); }
                lods.Add(new LOD(lh[i], rs));
            }
            if (!hasB) { Object.DestroyImmediate(root); return null; }
            float s0 = targetHeight / Mathf.Max(b.size.y, 1e-4f);
            foreach (Transform c in root.transform) { c.localScale *= s0; c.localPosition = (c.localPosition - new Vector3(b.center.x, b.min.y, b.center.z)) * s0; }
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(lods.ToArray());
            lg.RecalculateBounds();
            Directory.CreateDirectory(PrefabDir);
            var pf = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return pf;
        }

        // ---------------------------------------------------------------- 棋子

        /// <summary>统一底座：步兵圆座、骑兵椭圆座（米）。比队列间距（1.7 cm / 3.6 cm）小，排成方阵也不会互相压着。</summary>
        /// <summary>
        /// 底座（10-31 用户定：棋子回到原型 1:1 比例，底座半径 = 模拟层身体半径，混战不穿模）：
        /// 人类 / 矮人 0.42 码、兽人 0.5 码；骑兵椭圆收在模拟层骑兵的 1 码碰撞圆里。
        /// </summary>
        public const float BaseH = 0.0018f, InfBaseR = 0.0042f, OrcBaseR = 0.005f, CavBaseRX = 0.0055f, CavBaseRZ = 0.0098f;

        /// <summary>
        /// 棋子预制体：用 Tools/cut_base.py 切掉自带底座的 cut_lod0..2.fbx（全部合格才接入），身体缩放到 bodyHeight，
        /// 站在统一的程序化底座上（顶面植绒、侧面涂队伍色）。正面朝 +Z。
        /// </summary>
        public static GameObject ImportFigure(string name, int team, float bodyHeight, bool mounted)
        {
            var src = Path.Combine(P2Src, name, "final");
            var cut = Path.Combine(src, "cut.json");
            if (!File.Exists(cut)) return null;
            for (int i = 0; i < 3; i++) if (!File.Exists(Path.Combine(src, $"cut_lod{i}.fbx"))) return null;
            var dst = $"{P2Dst}/{name}";
            Directory.CreateDirectory(dst);
            bool changed = false;
            foreach (var f in new[] { "cut_lod0.fbx", "cut_lod1.fbx", "cut_lod2.fbx", "albedo.png" })
            {
                var s = Path.Combine(src, f); var d = Path.Combine(dst, f);
                if (File.Exists(s) && (!File.Exists(d) || File.GetLastWriteTimeUtc(s) > File.GetLastWriteTimeUtc(d))) { File.Copy(s, d, true); changed = true; }
            }
            if (changed) AssetDatabase.Refresh();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/albedo.png");
            var mat = BoardBuilder.Mat("M_" + name, "WarTable/MiniLit", m => { m.SetTexture("_BaseMap", tex); m.SetFloat("_Gloss", 0.12f); m.SetFloat("_UseVertexColor", 0); });

            var root = new GameObject(name);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            var lods = new System.Collections.Generic.List<LOD>();
            Bounds b = default; bool hasB = false;
            var baseR = BaseRenderer(root.transform, team, mounted);
            for (int i = 0; i < 3; i++)
            {
                var m = Model($"{dst}/cut_lod{i}.fbx");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(m);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "LOD" + i;
                inst.transform.SetParent(body, false);
                inst.transform.localRotation = Quaternion.Euler(0, 90, 0) * inst.transform.localRotation;
                inst.transform.localPosition = Quaternion.Euler(0, 90, 0) * inst.transform.localPosition;
                var rs = inst.GetComponentsInChildren<Renderer>();
                foreach (var r in rs) { r.sharedMaterial = mat; if (!hasB) { b = r.bounds; hasB = true; } else if (i == 0) b.Encapsulate(r.bounds); }
                var list = new System.Collections.Generic.List<Renderer>(rs);
                if (i < 2) list.Add(baseR);    // 最远一级连底座一起不画
                lods.Add(new LOD(LodHeights[i], list.ToArray()));
            }
            // 切口在原点：只按高度缩放，脚底落在底座顶面
            float s0 = bodyHeight / Mathf.Max(b.max.y, 1e-4f);
            body.localScale = Vector3.one * s0;
            body.localPosition = new Vector3(0, BaseH - 0.0002f, 0);
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(lods.ToArray());
            lg.RecalculateBounds();
            Directory.CreateDirectory(PrefabDir);
            var pf = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return pf;
        }

        /// <summary>程序化底座：顶面微微隆起、侧面略收，顶面植绒（子网格 0），侧面队伍色（子网格 1）。</summary>
        static Renderer BaseRenderer(Transform parent, int team, bool mounted, float baseR = InfBaseR)
        {
            string meshPath = $"Assets/_WarTable/Art/Generated/Base_{(mounted ? "Cav" : "Inf" + Mathf.RoundToInt(baseR * 10000))}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                float rx = mounted ? CavBaseRX : baseR, rz = mounted ? CavBaseRZ : baseR;
                const int seg = 48;
                var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>();
                var top = new System.Collections.Generic.List<int>(); var side = new System.Collections.Generic.List<int>();
                // 顶面：中心 + 一圈（略低于中心 0.2 mm，像真实底座的缓坡）
                v.Add(new Vector3(0, BaseH, 0)); uv.Add(new Vector2(0.5f, 0.5f));
                for (int k = 0; k < seg; k++)
                {
                    float a = k * Mathf.PI * 2 / seg; float x = Mathf.Cos(a) * rx * 0.96f, z = Mathf.Sin(a) * rz * 0.96f;
                    v.Add(new Vector3(x, BaseH - 0.0002f, z)); uv.Add(new Vector2(0.5f + x * 20f, 0.5f + z * 20f));
                }
                for (int k = 0; k < seg; k++) { top.Add(0); top.Add(1 + (k + 1) % seg); top.Add(1 + k); }
                // 侧面：上沿倒角一圈 + 下沿（略外扩）
                int s0 = v.Count;
                float[] ring = { 0.96f, 1f, 1.03f }; float[] hy = { BaseH - 0.0002f, BaseH - 0.0006f, 0f };
                for (int r = 0; r < 3; r++)
                    for (int k = 0; k <= seg; k++)
                    {
                        float a = k * Mathf.PI * 2 / seg;
                        v.Add(new Vector3(Mathf.Cos(a) * rx * ring[r], hy[r], Mathf.Sin(a) * rz * ring[r])); uv.Add(new Vector2((float)k / seg, r * 0.5f));
                    }
                for (int r = 0; r < 2; r++)
                    for (int k = 0; k < seg; k++)
                    {
                        int a0 = s0 + r * (seg + 1) + k, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                        side.AddRange(new[] { a0, a1, b0, a1, b1, b0 });
                    }
                mesh = new Mesh { name = "Base" };
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.subMeshCount = 2; mesh.SetTriangles(top, 0); mesh.SetTriangles(side, 1);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                Directory.CreateDirectory("Assets/_WarTable/Art/Generated");
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var go = new GameObject("Base");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var topMat = BoardBuilder.Mat("M_BaseTop", "WarTable/MiniLit", m => { m.SetTexture("_BaseMap", BoardBuilder.Tex("grass_turf") ?? BoardBuilder.Tex("grass_flock")); m.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.6f)); m.SetFloat("_Gloss", 0.02f); });
            var rim = BoardBuilder.Mat(team == 0 ? "M_BaseRimBlue" : "M_BaseRimRed", "WarTable/MiniLit", m =>
            {
                m.SetColor("_BaseColor", team == 0 ? new Color(0.13f, 0.3f, 0.72f) : new Color(0.66f, 0.12f, 0.1f));
                m.SetFloat("_Gloss", 0.35f); m.SetFloat("_GlossSize", 40f);
            });
            mr.sharedMaterials = new[] { topMat, rim };
            return mr;
        }
    }
}
