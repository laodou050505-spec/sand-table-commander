using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>原型角色（Art/Models/Demo，由 Tools/convert_demo_assets.py 原样转出）的导入设置：带骨骼动画、Generic、轴向烘焙。</summary>
    public class DemoModelPostprocessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/_WarTable/Art/Models/Demo")) return;
            var mi = (ModelImporter)assetImporter;
            bool rigged = assetPath.Contains("/troops/") || assetPath.Contains("/mounts/");
            mi.importAnimation = rigged && !assetPath.EndsWith("_lod1.fbx");   // 低模只要蒙皮网格，动作由原模驱动
            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true;
        }
    }

    /// <summary>
    /// 用户要求：角色沿用原型（Three.js 版里 Tripo 做的士兵、马、狼、武器），不重做。
    /// 这里把它们原样做成棋子预制体：骨骼模型 + 手上的武器 / 盾（挂在原型的 GripR / GripL / ShieldL 挂点）+ 骑兵的坐骑，
    /// 放大到沙盘比例。底座是沙盘这边加的（队伍色描边），角色本身不改。
    /// </summary>
    public static class DemoFigures
    {
        public const string Dir = "Assets/_WarTable/Art/Models/Demo";
        public const string PrefabDir = "Assets/_WarTable/Prefabs/Figures";
        /// <summary>原型模型 1 单位 = 1 码；沙盘 1 码 = 1 cm，再乘英雄比例。</summary>
        public const float FigureScale = 1.0f;   // 10-31 用户定：回到原型比例（底座 = 模拟层身体，混战不穿模）
        public static float Scale => BoardMap.Yard * FigureScale;
        public static bool UseBases = true;
        public static Vector3 ShieldFixEuler = new Vector3(-90f, 0, 0);
        // 对照原型截图试出来的（锤卫锤头朝上竖握、弓手竖握长弓、盾面朝外）
        public static Vector3 GripRFix = new Vector3(-90f, 0, 0), GripLFix = new Vector3(0, -90f, 0);

        public struct Gear
        {
            public string weapon, grip, shield, ready, mount;
            /// <summary>第二件武器（原型 defs.ts troop.weapons[1]），平时收着，切武器时显示。</summary>
            public string weapon2;
            public Gear(string weapon, string grip, string shield, string ready, string mount = null, string weapon2 = null) { this.weapon = weapon; this.grip = grip; this.shield = shield; this.ready = ready; this.mount = mount; this.weapon2 = weapon2; }
        }

        /// <summary>每个兵种手上拿什么、站什么姿势（原型 defs.ts 的第一件武器 + 盾 + 待机动作）。</summary>
        public static readonly Dictionary<string, Gear> Gears = new()
        {
            ["kd_footman"] = new("short_sword", "GripR", "shield_round", "Ready1H"),
            ["kd_guard"] = new("arming_sword", "GripR", "shield_crest", "Ready1H"),
            ["kd_spearman"] = new("spear", "GripR", "shield_round", "Ready1H", null, "short_sword"),
            ["kd_halberdier"] = new("halberd", "GripR", null, "Ready2HL"),
            ["kd_swordsman"] = new("claymore", "GripR", null, "Ready2H"),
            ["kd_hammer"] = new("warhammer", "GripR", null, "Ready2H"),
            ["kd_archer"] = new("long_bow", "GripL", null, "ReadyBow", null, "short_sword"),
            ["kd_crossbow"] = new("crossbow", "GripR", "shield_crest", "ReadyRifle", null, "short_sword"),
            ["kd_knight"] = new("lance", "GripR", null, "Mount", "horse", "arming_sword"),
            ["cl_warrior"] = new("hatchet", "GripR", "shield_round", "Ready1H"),
            ["cl_guardian"] = new("clan_axe", "GripR", "shield_clan", "Ready1H"),
            ["cl_skirmisher"] = new("javelin", "GripR", "shield_clan", "HoldThrown", null, "hatchet"),
            ["cl_pikeman"] = new("glaive", "GripR", null, "Ready2HL", null, "hatchet"),
            ["cl_berserker"] = new("war_axe2", "GripR", null, "Ready2H"),
            ["cl_crusher"] = new("maul", "GripR", null, "Ready2H"),
            ["cl_hunter"] = new("long_bow", "GripL", null, "ReadyBow", null, "hatchet"),
            ["cl_wolfrider"] = new("glaive", "GripR", null, "Mount", "wolf", "war_axe"),
            ["cl_wolfarcher"] = new("bow", "GripL", null, "Mount", "wolf", "hatchet"),
        };

        /// <summary>坐骑的鞍点（原型 assets.json，单位码，坐骑本地坐标）。</summary>
        static readonly Dictionary<string, Vector3> Saddle = new() { ["horse"] = new Vector3(0, 1.55f, 0.044f), ["wolf"] = new Vector3(0, 1.45f, 0.051f) };

        /// <summary>虚空蜂群一方的未来武器 / 盾（和 Tools/future_tex_fix.py 的 CLAN_PROPS 一致）。</summary>
        static readonly HashSet<string> SwarmProps = new() { "f_shield_round_clan", "f_hatchet", "f_clan_axe", "f_javelin", "f_glaive", "f_war_axe2", "f_maul", "f_war_axe", "f_bow", "f_long_bow_clan", "f_shield_clan" };

        static Material Mat(string kind, string name)
        {
            var tex = Directory.Exists($"{Dir}/{kind}/{name}_tex")
                // 11-11：重做过的兵种文件夹里会留着旧贴图（P2 每次换 UUID 文件名），按文件名取第一张会拿到旧的 → 取最新写入的那张
                ? Directory.GetFiles($"{Dir}/{kind}/{name}_tex", "Color_*.png").OrderByDescending(File.GetLastWriteTimeUtc).Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p.Replace('\\', '/'))).FirstOrDefault(t => t)
                : null;
            tex ??= Directory.Exists($"{Dir}/{kind}/{name}_tex") ? Directory.GetFiles($"{Dir}/{kind}/{name}_tex", "*.png").Where(p => !p.Contains("Normal") && !p.Contains("Emis_")).Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p.Replace('\\', '/'))).FirstOrDefault(t => t) : null;
            // 11-12 未来时代（用户：别做成一片漆黑什么都看不见）：Tools/future_tex_fix.py 出的自发光遮罩 Emis_*.png（紫 / 青色光缝、硬光刃）挂到自发光，
            // 不受光照影响；虚空蜂群再加一圈淡紫边缘光，深色甲片的轮廓看得清
            var emis = tex ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex).Replace("/Color_", "/Emis_")) : null;
            bool swarm = name.StartsWith("f_cl_") || name == "f_wolf" || name == "f_skimmer" || SwarmProps.Contains(name);
            var mat = BoardBuilder.Mat($"M_Demo_{name}", "WarTable/MiniLit", m =>
            {
                m.SetTexture("_BaseMap", tex); m.SetFloat("_Gloss", 0.15f); m.SetFloat("_UseVertexColor", 0);
                m.SetTexture("_EmissionMap", emis ? emis : Texture2D.whiteTexture);
                m.SetColor("_EmissionColor", !emis ? Color.black : swarm ? new Color(1.25f, 1.1f, 1.4f) : new Color(0.55f, 0.6f, 0.62f));
            });
            // BoardBuilder.Look 会把边缘光清零、暗部统一成冷灰（全游戏定稿外观）；深色的虚空蜂群是特例，之后单独补
            if (swarm) { mat.SetColor("_RimColor", new Color(0.72f, 0.58f, 1f)); mat.SetFloat("_RimPower", 2.4f); mat.SetFloat("_RimStrength", 0.4f); mat.SetFloat("_Ambient", 0.6f); mat.SetColor("_ShadowColor", new Color(0.46f, 0.43f, 0.6f)); }
            // 11-12 用户：“角色不能完全没有质感” → 未来时代的材质要有高光（Look 把非金属高光压到 0.05）：光棱 = 陶瓷釉面，蜂群 = 甲壳清漆
            if (name.StartsWith("f_")) { mat.SetFloat("_Gloss", swarm ? 0.55f : 0.42f); mat.SetFloat("_GlossSize", swarm ? 34f : 60f); EditorUtility.SetDirty(mat); }
            return mat;
        }

        static GameObject Inst(string kind, string name, Transform parent)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{kind}/{name}.fbx");
            if (!src) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetParent(parent, false);
            var mat = Mat(kind, name);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            return go;
        }

        public static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f) return f; }
            return null;
        }

        public static AnimationClip Clip(string kind, string model, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Dir}/{kind}/{model}.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview") && (c.name == name || c.name.EndsWith("|" + name))) return c;
            return null;
        }

        [MenuItem("WarTable/3 导入原型角色（棋子）")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh();
            int n = 0;
            foreach (var kv in Gears) if (Build(kv.Key)) n++;
            Debug.Log($"[DemoFigures] 棋子预制体 {n}/{Gears.Count}");
        }

        /// <summary>
        /// 预制体结构：根（缩放到沙盘比例，正面朝 +Z）→ Rider（原型角色）[+ Mount（坐骑）] + Base（队伍色底座，不随缩放）。
        /// </summary>
        /// <summary>时代（11-10 用户：标题选不同时期的战斗）：null = 原版；"gothic" = 哥特科幻（模型 g_&lt;兵种&gt;，Tools/gothic_chars.py 生成）。
        /// 兵种 ID、模拟、武器挂点都不变，只换角色外观。</summary>
        public static string ModelOf(string id, string era) => EraPrefix(era) + id;
        /// <summary>时代版坐骑（f_horse）对应的原版坐骑名（horse）：鞍点、运行时的骑乘姿势都按原版算。</summary>
        /// <summary>时代版坐骑沿身长方向的缩放（让它和原版一样长，阵型间距不用改）。</summary>
        /// 11-12：机械狼 f_wolf 比原版狼长 11%，但用真实网格（BakeMesh）量过，16 个坐骑互不穿插、最近间隙 24.8 mm，不用压；
        /// 当时用渲染包围盒量出“重叠 2.3 mm”是误报（蒙皮网格的包围盒比模型大得多）。
        static readonly Dictionary<string, float> MountLength = new();
        /// <summary>
        /// 11-12 用户：“都到未来了，不一定还骑马，可以骑高科技坐骑” → 未来时代的坐骑换成悬浮载具（没有腿、不绑骨，靠 HoverMount 程序悬浮）：
        /// 原版坐骑 → 载具模型（Tools/future_hover_mounts.py 摆正、缩放、把座位顶抬到原版鞍高，骑手的坐姿和鞍点都不用改）。
        /// </summary>
        // 11-13：现代时代同理——巡逻摩托 / 越野摩托（带轮子、停在地上，HoverMount 只给一点发动机抖动，阵亡时侧翻）。键 = 时代前缀 + 原版坐骑名
        static readonly Dictionary<string, string> HoverOf = new() { ["f_horse"] = "f_bike", ["f_wolf"] = "f_skimmer", ["m_horse"] = "m_moto", ["m_wolf"] = "m_dirtbike",
            ["s_horse"] = "s_strider", ["s_wolf"] = "s_crawler" };   // 深空：星枪团的悬浮冲击艇、余烬矿帮的履带矿车
        public static bool IsHover(string m) => m != null && HoverOf.ContainsValue(m);
        /// <summary>悬浮载具的座位比原版鞍点低多少（棋子本地单位；和 Tools/future_hover_mounts.py 里 SPEC 的 saddle 对应）：悬浮摩托按马的鞍高会离地太高。</summary>
        static readonly Dictionary<string, float> HoverDrop = new() { ["f_bike"] = 0.35f, ["m_moto"] = 0.61f, ["m_dirtbike"] = 0.45f, ["s_strider"] = 0.3f, ["s_crawler"] = 0.19f };   // 深空：冲击艇鞍高 1.25（马 1.55 − 0.3）；矿车座高 1.265（狼 1.45 − 0.19）   // 摩托：原版鞍高 − future_hover_mounts.py 打印的 seat_h（0.94 / 1.0）
        static Vector3 SaddleOf(string mount) => Saddle[BaseMount(mount)] - new Vector3(0f, HoverDrop.TryGetValue(mount, out float dr) ? dr : 0f, 0f);
        /// <summary>时代版坐骑对应的原版坐骑名（鞍点、模拟里的坐骑种类都按原版）。</summary>
        public static string BaseMount(string m)
        {
            foreach (var kv in HoverOf) if (kv.Value == m) return kv.Key.Substring(2);
            return m != null && m.Length > 2 && m[1] == '_' ? m.Substring(2) : m;
        }
        /// <summary>时代的资源前缀：哥特 g_、未来 f_（11-12）、原版没有。</summary>
        public static string EraPrefix(string era) => era == "gothic" ? "g_" : era == "future" ? "f_" : era == "modern" ? "m_" : era == "space" ? "s_" : "";
        public static string PrefabPath(string id, string era) => era == null ? $"{PrefabDir}/{id}.prefab" : $"{PrefabDir}/{char.ToUpper(era[0]) + era.Substring(1)}/{id}.prefab";

        /// <summary>
        /// 哥特时代的装备（11-11 用户：哥特和原来太像，武器要配上）：原版道具名 → 哥特版（原型 props/g_*，握点 / 长度同原版，模拟不变）。
        /// 原版两边共用的道具（长弓、圆盾）按阵营分开：炉铁氏族用铁簧长弓、锅炉盖盾。
        /// </summary>
        public static Gear GearOf(string id, string era)
        {
            var g = Gears[id];
            string pre = EraPrefix(era);
            if (pre == "") return g;
            bool clan = id.StartsWith("cl_");
            string G(string m)
            {
                if (m == null) return null;
                if (pre == "g_" && clan && m == "long_bow") return "g_iron_longbow";
                if (pre == "g_" && clan && m == "shield_round") return "g_shield_clan";
                // 两边共用的道具按阵营分开：有 <前缀><名>_clan 就给氏族一方用（未来：f_long_bow_clan 等）
                if (clan && File.Exists($"{Dir}/props/{pre}{m}_clan.fbx")) return pre + m + "_clan";
                return File.Exists($"{Dir}/props/{pre}{m}.fbx") ? pre + m : m;
            }
            // 11-12：坐骑也可以有时代版（mounts/f_horse、f_wolf：机械马 / 机械狼），没有就用原版
            string mount = g.mount != null && File.Exists($"{Dir}/mounts/{pre}{g.mount}.fbx") ? pre + g.mount : g.mount;
            // 未来时代：有悬浮载具就骑载具（机械马 / 机械狼 f_horse、f_wolf 留作备用）
            if (g.mount != null && HoverOf.TryGetValue(pre + g.mount, out var hv) && File.Exists($"{Dir}/mounts/{hv}.fbx")) mount = hv;
            // 现代时代：弓兵拿的是步枪——握在右手（和弩兵一样）、预备姿势用持枪的
            string grip = g.grip, ready = g.ready;
            if ((pre == "m_" || pre == "s_") && g.grip == "GripL") { grip = "GripR"; if (ready == "ReadyBow") ready = "ReadyRifle"; }
            return new Gear(G(g.weapon), grip, G(g.shield), ready, mount, G(g.weapon2));
        }

        public static GameObject Build(string id) => Build(id, null);

        public static GameObject Build(string id, string era)
        {
            var d = Troops.Get(id);
            string body = ModelOf(id, era);
            var g = GearOf(id, era);
            var root = new GameObject(id);
            var fig = new GameObject("Figure").transform;
            fig.SetParent(root.transform, false);
            fig.localScale = Vector3.one * Scale;
            Transform mount = null;
            if (g.mount != null)
            {
                var m = Inst("mounts", g.mount, fig);
                if (!m) { Object.DestroyImmediate(root); return null; }
                if (IsHover(g.mount))
                {
                    // 悬浮载具：Mount（空节点，运行时被 FigureView 拆出去摆位置）→ Hover（HoverMount 做起伏 / 侧翻）→ Model（静态网格）
                    var wrap = new GameObject("Mount").transform; wrap.SetParent(fig, false);
                    var hov = new GameObject("Hover").transform; hov.SetParent(wrap, false);
                    m.name = "Model"; m.transform.SetParent(hov, false);
                    var bb = new Bounds(); bool any = false; var pts = new List<Vector3>();
                    foreach (var mf in m.GetComponentsInChildren<MeshFilter>())
                        foreach (var v in mf.sharedMesh.vertices)
                        {
                            var p = hov.InverseTransformPoint(mf.transform.TransformPoint(v)); pts.Add(p);
                            if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                        }
                    var hm = hov.gameObject.AddComponent<WarTable.Game.HoverMount>();
                    hm.center = bb.center; hm.halfWidth = bb.extents.x; hm.halfHeight = bb.extents.y;
                    // 坠毁侧翻后的落地高度：按真实顶点算（包围盒估的偏高，车会浮在地面上 2–3 mm），再压 0.01 让它实实在在贴地
                    float Land(float roll) { var q = Quaternion.Euler(0f, 0f, roll); float lo = 0f; foreach (var p in pts) lo = Mathf.Min(lo, (q * (p - bb.center)).y); return -lo - 0.01f; }
                    hm.landLeft = Land(hm.crashRoll); hm.landRight = Land(-hm.crashRoll);
                    hm.seatDrop = HoverDrop.TryGetValue(g.mount, out float drop) ? drop : 0f;
                    if (g.mount.StartsWith("m_") || g.mount == "s_crawler") { hm.bob = 0.004f; hm.sway = 0.25f; }   // 摩托：不悬浮，只有一点发动机抖动
                    m = wrap.gameObject;
                }
                m.name = "Mount";
                mount = m.transform;
                // 时代版坐骑如果比原版长太多、阵型里会穿插，可以在 MountLength 里登记沿身长的缩放（鞍高不变）
                if (MountLength.TryGetValue(g.mount, out float ml)) mount.localScale = Vector3.Scale(mount.localScale, new Vector3(1f, 1f, ml));
            }
            var rider = Inst("troops", body, fig);
            if (!rider) { Object.DestroyImmediate(root); return null; }
            rider.name = "Rider";
            if (mount) rider.transform.localPosition = SaddleOf(g.mount);
            void Attach(string model, string point)
            {
                var at = Find(rider.transform, point);
                var p = at ? Inst("props", model, at) : null;
                // 道具 FBX 根节点自带握点偏移 / 朝向 / 尺寸（原型 glb 的根变换），保持不动，直接挂在挂点下面
                if (!p) { Debug.LogWarning($"[DemoFigures] {id}: 挂点 {point} 或道具 {model} 缺失"); return; }
            }
            Attach(g.weapon, g.grip);
            // 挂在骨骼下的空物体经 Blender→FBX 丢了原型里的挂点朝向（build.ts attachNode），在武器实例上补回
            {
                var gp = Find(rider.transform, g.grip);
                var w = gp ? gp.Find(g.weapon) : null;
                // 11-11 用户：武器要在角色手上——原来只补了朝向、没补位置：道具根节点带“握点 → 模型中心”的偏移（剑约 0.42 码），
                // 挂点丢掉的那一转要连偏移一起转（绕握点转），否则武器整个偏出手掌半个身位（弓的握点在正中、偏移为 0，所以以前只有弓是对的）
                if (w) { var q = Quaternion.Euler(g.grip == "GripL" ? GripLFix : GripRFix); w.localPosition = q * w.localPosition; w.localRotation = q * w.localRotation; SnapToPalm(w, gp, rider.transform, id); }
            }
            if (g.weapon2 != null)
            {
                Attach(g.weapon2, "GripR");
                var gp2 = Find(rider.transform, "GripR");
                var w2 = gp2 ? gp2.Find(g.weapon2) : null;
                if (w2) { var q2 = Quaternion.Euler(GripRFix); w2.localPosition = q2 * w2.localPosition; w2.localRotation = q2 * w2.localRotation; SnapToPalm(w2, gp2, rider.transform, id); w2.gameObject.SetActive(false); }
            }
            if (g.shield != null)
            {
                Attach(g.shield, "ShieldL");
                // 原型 ShieldL 挂点自带绕 X −90°（build.ts attachNode）；挂在骨骼下的空物体经 Blender→FBX 丢了这一转，在这里补回
                // 挂点本身会被动作曲线覆盖（导出时空物体也烘了动画），所以修正加在盾牌实例上
                var sp = Find(rider.transform, "ShieldL");
                var sh = sp ? sp.Find(g.shield) : null;
                if (sh) { var qs = Quaternion.Euler(ShieldFixEuler); sh.localPosition = qs * sh.localPosition; sh.localRotation = qs * sh.localRotation; }
            }
            // 武器 / 盾的低模：和原件同一个挂点、同一个变换（含朝向修正），远处显示
            extra0.Clear(); extra1.Clear();
            foreach (var (model, point) in new[] { (g.weapon, g.grip), (g.weapon2, "GripR"), (g.shield, "ShieldL") })
            {
                if (model == null) continue;
                var at = Find(rider.transform, point); var w = at ? at.Find(model) : null;
                var lsrc = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/props/{model}_lod1.fbx");
                if (!w || !lsrc) continue;
                var l = (GameObject)PrefabUtility.InstantiatePrefab(lsrc);
                PrefabUtility.UnpackPrefabInstance(l, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                l.name = model + "_LOD1";
                l.transform.SetParent(at, false);
                l.transform.localPosition = w.localPosition; l.transform.localRotation = w.localRotation; l.transform.localScale = w.localScale;
                var mat = Mat("props", model);
                foreach (var r in l.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; extra1.Add(r); }
                if (!w.gameObject.activeSelf) l.SetActive(false);
                extra0.AddRange(w.GetComponentsInChildren<Renderer>());
            }
            AddLods(root, rider.transform, mount, body, g.mount);
            AddRig(root, rider, mount, body, g);
            if (UseBases) AddBase(root.transform, d.team, mount != null, id);
            if (UseBases && era != null && era != "modern") GothicBase(root.transform, d.team, era);   // 现代：沿用原版底座（草皮顶 + 蓝 / 红边圈，和样图 A 一致）
            var path = PrefabPath(id, era);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var pf = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return pf;
        }

        /// <summary>
        /// 握柄对准掌心（11-11 用户：武器要在角色手上）。原型把握点放在“手腕 + 固定手长”、把武器按包围盒中线对到握点，
        /// 所以头部不对称的武器（锤、单刃斧、偃月刀、反曲弓）握柄不在握点上，手大的角色（哥特炉铁氏族的大手套）握点又偏在手腕。
        /// 做法：在握点处垂直于武器长轴截一刀，取截面里最粗的那一圈（握柄；弓的弦很细，不会被选中）的中心，
        /// 垂直于长轴平移武器，让握柄穿过掌心（蒙在手骨上的顶点的加权中心）。沿长轴不动，武器伸出去的长度（模拟的攻击距离）不变。
        /// </summary>
        static void SnapToPalm(Transform w, Transform grip, Transform rider, string id)
        {
            var mf = w.GetComponentInChildren<MeshFilter>();
            if (!mf || !mf.sharedMesh || !grip) return;
            // 掌心：握点挂在手骨下面，找这根手骨蒙的顶点
            var hand = grip.parent;
            var smr = rider.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.sharedMesh && System.Array.IndexOf(x.bones, hand) >= 0).OrderByDescending(x => x.sharedMesh.vertexCount).FirstOrDefault();
            Vector3 palm = grip.position;
            if (smr)
            {
                int bi = System.Array.IndexOf(smr.bones, hand);
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                var bv = baked.vertices; var bw = smr.sharedMesh.boneWeights; Vector3 sum = Vector3.zero; float ws = 0f;
                for (int k = 0; k < bv.Length && k < bw.Length; k++)
                {
                    var b = bw[k];
                    float ww = (b.boneIndex0 == bi ? b.weight0 : 0) + (b.boneIndex1 == bi ? b.weight1 : 0) + (b.boneIndex2 == bi ? b.weight2 : 0) + (b.boneIndex3 == bi ? b.weight3 : 0);
                    if (ww > 0.6f) { sum += smr.transform.TransformPoint(bv[k]) * ww; ws += ww; }
                }
                Object.DestroyImmediate(baked);
                if (ws > 0f) palm = sum / ws;
            }
            var mesh = mf.sharedMesh; var mb = mesh.bounds;
            int ax = mb.size.x > mb.size.y ? (mb.size.x > mb.size.z ? 0 : 2) : (mb.size.y > mb.size.z ? 1 : 2);
            float len = mb.size[ax];
            var gl = mf.transform.InverseTransformPoint(grip.position);
            float z = Mathf.Clamp(gl[ax], mb.min[ax] + len * 0.02f, mb.max[ax] - len * 0.02f);
            var vs = mesh.vertices; var tr = mesh.triangles; var pts = new List<Vector3>();
            for (int t = 0; t < tr.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    Vector3 a = vs[tr[t + e]], c = vs[tr[t + (e + 1) % 3]];
                    float da = a[ax] - z, dc = c[ax] - z;
                    if (da * dc < 0f) pts.Add(Vector3.Lerp(a, c, da / (da - dc)));
                }
            if (pts.Count < 3) return;
            // 截面点按距离连成几簇，取最粗的一簇
            float link = len * 0.03f; var cid = new int[pts.Count]; for (int i = 0; i < cid.Length; i++) cid[i] = -1;
            int nc = 0; var stack = new Stack<int>(); Vector3 best = gl; float bestSize = -1f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (cid[i] >= 0) continue;
                cid[i] = nc; stack.Push(i); var cb = new Bounds(pts[i], Vector3.zero);
                while (stack.Count > 0)
                {
                    int u = stack.Pop(); cb.Encapsulate(pts[u]);
                    for (int v = 0; v < pts.Count; v++) if (cid[v] < 0 && (pts[v] - pts[u]).sqrMagnitude < link * link) { cid[v] = nc; stack.Push(v); }
                }
                if (cb.size.magnitude > bestSize) { bestSize = cb.size.magnitude; best = cb.center; }
                nc++;
            }
            // 握柄中心 → 掌心（只在垂直于长轴的平面里挪）
            var pl = mf.transform.InverseTransformPoint(palm);
            var off = pl - best; off[ax] = 0f;
            if (off.magnitude > len * 0.3f) { Debug.LogWarning($"[DemoFigures] {id} {w.name}: 握柄离掌心太远（{off.magnitude / len:0.00} 个武器长），没挪"); return; }
            w.position += mf.transform.TransformVector(off);
        }

        /// <summary>远处用的低模切换距离：屏高占比低于它就换低模（座位上大约只有最近两三排用原模）。</summary>
        public const float LodSwitch = 0.045f;   // LODGroup 包围盒含长矛长枪（约 5 cm），0.045 ≈ 眼前 55 cm 以内才用原模

        /// <summary>
        /// 低模（Tools/make_demo_lods.py 减面到 30%，骨骼 / 权重 / UV 不变）：把它的蒙皮网格挂到原模同名骨骼上，
        /// 动作仍由原模驱动；根节点 LODGroup 近处原模、远处低模。武器、盾、底座不进 LOD（两档都显示）。
        /// </summary>
        static readonly List<Renderer> extra0 = new(), extra1 = new();

        static readonly string[] Locomotion = { "Walk", "Run", "Walkbackwards" };

        /// <summary>
        /// 原型 glb 里的走 / 跑片段有毛病（原型没有底座看不出来）：Run 的髋部整体偏出去约半个身位，
        /// Walk / Run / Walkbackwards 的髋部高度在一个循环里线性爬升（循环接头处跳一下）。
        /// 生成清洗版：髋部水平位置钉在站立位置，高度去掉线性爬升、以站立高度为中心保留起伏；其余骨骼不动。
        /// </summary>
        static AnimationClip CleanLocomotion(AnimationClip src, string model)
        {
            const string ClipDir = "Assets/_WarTable/Art/Generated/Clips";
            System.IO.Directory.CreateDirectory(ClipDir);
            string path = $"{ClipDir}/{model}_{src.name.Substring(src.name.LastIndexOf('|') + 1)}.anim";
            var clip = new AnimationClip { name = src.name, frameRate = src.frameRate };
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var c = AnimationUtility.GetEditorCurve(src, b);
                if (b.path.EndsWith("mixamorig:Hips") && b.propertyName.StartsWith("m_LocalPosition"))
                {
                    char ax = b.propertyName[b.propertyName.Length - 1];
                    var keys = c.keys;
                    float v0 = c.Evaluate(0f), v1 = c.Evaluate(src.length), mean = 0f;
                    for (int i = 0; i < keys.Length; i++) { keys[i].value -= (v1 - v0) * keys[i].time / src.length; mean += keys[i].value; }
                    mean /= Mathf.Max(1, keys.Length);
                    // 水平两轴：钉在站立位置（0）；高度轴：以站立高度为中心
                    float rest = ax == 'z' ? StandHipsZ : 0f;
                    for (int i = 0; i < keys.Length; i++) { keys[i].value = ax == 'z' ? keys[i].value - mean + rest : rest; keys[i].inTangent = keys[i].outTangent = ax == 'z' ? keys[i].inTangent - (v1 - v0) / src.length : 0f; }
                    c = new AnimationCurve(keys);
                }
                clip.SetCurve(b.path, b.type, b.propertyName, c);
            }
            var st = AnimationUtility.GetAnimationClipSettings(src);
            st.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        /// <summary>站立时髋部高度（片段空间，扫描 50 段动作得到：站立 / 待机都是 −0.512）。</summary>
        const float StandHipsZ = -0.512f;

        /// <summary>这个模型（带风格前缀的名字，如 s_kd_footman）的手臂修正角度 [左张, 左送, 右张, 右送]；没有 = null。</summary>
        public static float[] BodyFitOf(string model)
        {
            string dir = model.StartsWith("s_") ? "Space" : null;
            if (dir == null) return null;
            string path = $"ArtSource/{dir}/bodyfit.json";
            if (!File.Exists(path)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), "\"" + model + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return null;
            var v = m.Groups[1].Value.Split(',').Select(x => float.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return v.Length == 4 ? v : null;
        }

        /// <summary>动画组件 + FigureRig（显示层按模拟状态驱动它们）。</summary>
        static void AddRig(GameObject root, GameObject rider, Transform mount, string id, Gear g)
        {
            AnimationClip[] Clips(string kind, string model) => AssetDatabase.LoadAllAssetsAtPath($"{Dir}/{kind}/{model}.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__"))
                .Select(c => kind == "troops" && Locomotion.Contains(c.name.Substring(c.name.LastIndexOf('|') + 1)) ? CleanLocomotion(c, model) : c).ToArray();
            FigureAnimator Anim(GameObject go, string kind, string model, string upper)
            {
                if (!go.TryGetComponent<Animator>(out var an)) an = go.AddComponent<Animator>();
                an.runtimeAnimatorController = null; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var fa = go.AddComponent<FigureAnimator>();
                fa.clips = Clips(kind, model); fa.upperRoot = upper;
                return fa;
            }
            var rig = root.AddComponent<FigureRig>();
            rig.figure = root.transform.Find("Figure");
            rig.rifleBows = id.StartsWith("m_") || id.StartsWith("s_");   // 现代的步枪、深空的轨道枪：都用步枪动作
            rig.rider = Anim(rider, "troops", id, "Spine");
            if (mount) rig.mount = Anim(mount.gameObject, "mounts", g.mount, "");
            rig.spine = Find(rider.transform, "mixamorig:Spine");
            // 壮身躯的手臂修正（深空的重型机甲）：角度是 FigureClipAudit.Fit 按每个兵种拟合出来的，存在 ArtSource/<风格>/bodyfit.json
            var fitRow = BodyFitOf(id);
            if (fitRow != null)
            {
                var bf = rider.AddComponent<WarTable.Game.BodyFit>();
                bf.Bind(rider.transform, Find(rider.transform, "mixamorig:LeftArm"), Find(rider.transform, "mixamorig:RightArm"));
                bf.abductL = fitRow[0]; bf.flexL = fitRow[1]; bf.abductR = fitRow[2]; bf.flexR = fitRow[3];
            }
            GameObject[] Parts(string model, string point)
            {
                var at = Find(rider.transform, point);
                if (!at || model == null) return new GameObject[0];
                return new[] { at.Find(model), at.Find(model + "_LOD1") }.Where(t => t).Select(t => t.gameObject).ToArray();
            }
            var list = new List<FigureRig.WeaponSet> { new() { id = g.weapon, parts = Parts(g.weapon, g.grip) } };
            if (g.weapon2 != null) list.Add(new() { id = g.weapon2, parts = Parts(g.weapon2, "GripR") });
            rig.weapons = list.ToArray();
            rig.shield = Parts(g.shield, "ShieldL");
        }

        static void AddLods(GameObject root, Transform rider, Transform mount, string id, string mountName)
        {
            var lod0 = new List<Renderer>(extra0); var lod1 = new List<Renderer>(extra1);
            void One(string kind, string model, Transform target)
            {
                var originals = target.GetComponentsInChildren<SkinnedMeshRenderer>();
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{kind}/{model}_lod1.fbx");
                if (!src) { Debug.LogWarning($"[DemoFigures] 没有低模 {model}_lod1"); return; }
                var inst = (GameObject)Object.Instantiate(src);
                var mat = Mat(kind, model);
                if (originals.Length == 0)
                {
                    // 静态网格（悬浮载具，没有骨骼）：低模放在原模旁边，同一个变换
                    var statics = target.GetComponentsInChildren<MeshRenderer>();
                    foreach (var lmf in inst.GetComponentsInChildren<MeshFilter>())
                    {
                        var orig = System.Array.Find(statics, o => o.name == lmf.name) ?? (statics.Length > 0 ? statics[0] : null);
                        if (!orig) continue;
                        var go = new GameObject(lmf.name + "_LOD1");
                        go.transform.SetParent(orig.transform.parent, false);
                        go.transform.localPosition = orig.transform.localPosition; go.transform.localRotation = orig.transform.localRotation; go.transform.localScale = orig.transform.localScale;
                        go.AddComponent<MeshFilter>().sharedMesh = lmf.sharedMesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = System.Linq.Enumerable.Repeat(mat, lmf.sharedMesh.subMeshCount).ToArray();
                        mr.shadowCastingMode = ShadowCastingMode.On;
                        lod1.Add(mr);
                    }
                    lod0.AddRange(statics);
                    Object.DestroyImmediate(inst);
                    return;
                }
                foreach (var lsm in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var orig = System.Array.Find(originals, o => o.name == lsm.name) ?? (originals.Length > 0 ? originals[0] : null);
                    if (!orig) continue;
                    var go = new GameObject(lsm.name + "_LOD1");
                    go.transform.SetParent(orig.transform.parent, false);
                    go.transform.localPosition = orig.transform.localPosition; go.transform.localRotation = orig.transform.localRotation; go.transform.localScale = orig.transform.localScale;
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = lsm.sharedMesh;
                    smr.sharedMaterials = System.Linq.Enumerable.Repeat(mat, lsm.sharedMesh.subMeshCount).ToArray();
                    smr.bones = lsm.bones.Select(b => b ? Find(target, b.name) : null).ToArray();
                    smr.rootBone = lsm.rootBone ? Find(target, lsm.rootBone.name) : orig.rootBone;
                    smr.localBounds = orig.localBounds;
                    smr.updateWhenOffscreen = false;
                    smr.shadowCastingMode = ShadowCastingMode.On;
                    if (smr.bones.Any(b => !b)) Debug.LogWarning($"[DemoFigures] {model} 低模有骨骼对不上");
                    lod1.Add(smr);
                }
                lod0.AddRange(originals.Where(o => !o.name.EndsWith("_LOD1")));
                Object.DestroyImmediate(inst);
            }
            One("troops", id, rider);
            if (mount) One("mounts", mountName, mount);
            if (lod1.Count == 0) return;
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(LodSwitch, lod0.ToArray()), new LOD(0.0001f, lod1.ToArray()) });
            lg.RecalculateBounds();
        }

        /// <summary>
        /// 哥特时代的底座（11-11 用户：哥特和原来太像，不一定还要红蓝 → 用户选“骨白铜绿 对 煤黑熔橙”）：
        /// 侧圈我军铜绿、敌军熔橙；顶面植绒换成焦土（和哥特地面一致）。中古底座不动。
        /// </summary>
        static void GothicBase(Transform root, int team, string era = "gothic")
        {
            var b = root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterials.Length == 2 && r.sharedMaterials[1] && r.sharedMaterials[1].name.StartsWith("M_BaseRim"));
            if (!b) { Debug.LogWarning("[DemoFigures] 哥特底座：找不到底座 " + root.name); return; }
            if (era == "space")
            {
                // 11-13 深空（样图 C）：顶面小行星灰砾，边圈沿用蓝 / 红（样图里就是蓝圈对红圈）
                var stop = BoardBuilder.Mat("M_BaseTopAsteroid", "WarTable/MiniLit", m => { m.SetTexture("_BaseMap", (System.IO.File.Exists("Assets/_WarTable/Art/Textures/space_regolith.png") ? BoardBuilder.Tex("space_regolith") : BoardBuilder.Tex("moon_turf"))); m.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.98f)); m.SetFloat("_Gloss", 0.02f); });
                b.sharedMaterials = new[] { stop, b.sharedMaterials[1] };
                return;
            }
            bool fut = era == "future";   // 11-12 未来（样图 A）：我军天青、敌军紫，顶面月尘
            var top = fut
                ? BoardBuilder.Mat("M_BaseTopMoon", "WarTable/MiniLit", m => { m.SetTexture("_BaseMap", BoardBuilder.Tex("moon_turf") ?? BoardBuilder.Tex("ash_turf")); m.SetColor("_BaseColor", new Color(1.25f, 1.27f, 1.32f)); m.SetFloat("_Gloss", 0.02f); })
                : BoardBuilder.Mat("M_BaseTopAsh", "WarTable/MiniLit", m => { m.SetTexture("_BaseMap", BoardBuilder.Tex("ash_turf") ?? BoardBuilder.Tex("grass_turf")); m.SetColor("_BaseColor", new Color(0.9f, 0.86f, 0.82f)); m.SetFloat("_Gloss", 0.02f); });
            var rim = BoardBuilder.Mat(fut ? (team == 0 ? "M_BaseRimCyan" : "M_BaseRimViolet") : team == 0 ? "M_BaseRimVerdigris" : "M_BaseRimMolten", "WarTable/MiniLit", m =>
            {
                m.SetColor("_BaseColor", fut ? (team == 0 ? new Color(0.2f, 0.78f, 0.9f) : new Color(0.56f, 0.26f, 0.9f)) : team == 0 ? new Color(0.16f, 0.56f, 0.5f) : new Color(0.95f, 0.42f, 0.08f));
                m.SetFloat("_Gloss", 0.35f); m.SetFloat("_GlossSize", 40f);
            });
            b.sharedMaterials = new[] { top, rim };
        }

        static void AddBase(Transform parent, int team, bool mounted, string troopId)
        {
            // 借用 WTArt 的统一底座（顶面植绒、侧面队伍色）
            var tmp = new GameObject("tmp");
            var mi = typeof(WTArt).GetMethod("BaseRenderer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            float radius = WarTable.Sim.WeaponsData.Troops.TryGetValue(troopId, out var td) && td.Race == WarTable.Sim.Race.Orc ? WTArt.OrcBaseR : WTArt.InfBaseR;
            var r = (Renderer)mi.Invoke(null, new object[] { tmp.transform, team, mounted, radius });
            r.transform.SetParent(parent, false);
            Object.DestroyImmediate(tmp);
            // 角色站在底座顶面上
            parent.Find("Figure").localPosition = new Vector3(0, WTArt.BaseH - 0.0002f, 0);
        }

        /// <summary>把场景里的棋子摆成待机姿势（编辑器里直接采样动作；玩法阶段换成 Animator）。</summary>
        public static void Pose(GameObject inst, string id, float t = 0.2f, string era = null)
        {
            var g = GearOf(id, era);
            var rider = Find(inst.transform, "Rider");
            Clip("troops", ModelOf(id, era), g.ready)?.SampleAnimation(rider.gameObject, t);
            var bfit = rider.GetComponent<WarTable.Game.BodyFit>(); if (bfit) bfit.Apply();
            if (g.mount != null)
            {
                var m = Find(inst.transform, "Mount");
                Clip("mounts", g.mount, "Stand")?.SampleAnimation(m.gameObject, t);
            }
        }
    }
}
