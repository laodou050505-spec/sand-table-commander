using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WarTable.Game;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 哥特时代的场地配色（11-11 用户：哥特和中古太像，不一定还要很红很蓝，相关旗帜也要换 → 用户选“骨白铜绿 对 煤黑熔橙”）。
    /// 在编辑器里把要换的东西准备好，交给 EraLook 运行时按时代切换：
    /// - 帅旗：王国 → 圣钟骑士团（骨白底黑钟、铜绿边，standard_bell）；氏族 → 炉铁氏族（煤黑底熔橙铁砧，standard_forge）。
    /// - 帐篷：P2 帐篷换成 Tools/gothic_recolor.py 算出的哥特贴图；程序圆帐的蓝白条纹是顶点色 → 另存一份换色网格；三角旗换铜绿。
    /// 建筑（教堂 / 排屋 / 钟楼 / 枯树）是 PropBuilder 摆的孪生件（EraOnly），这里不管。
    /// </summary>
    public static class GothicEra
    {
        const string MatDir = "Assets/_WarTable/Art/Materials/Gothic", MeshDir = "Assets/_WarTable/Art/Generated/Gothic";
        public static readonly Color Verdigris = new(0.16f, 0.5f, 0.45f), Bone = new(0.9f, 0.86f, 0.78f), Molten = new(0.95f, 0.45f, 0.1f), Soot = new(0.12f, 0.11f, 0.1f);

        public static readonly Color Cyan = new(0.2f, 0.78f, 0.9f), Violet = new(0.56f, 0.26f, 0.9f);
        public static readonly Color HiVis = new(0.7f, 0.88f, 0.14f);   // 现代时代蓝盾特勤的荧光黄绿

        /// <summary>11-12：同时准备哥特（Gothic/、后缀 _g）和未来（Future/、后缀 _f，样图 A：白 + 天青 对 黑 + 紫）两套；
        /// 未来那套缺贴图的项留空，运行时退回中古材质。</summary>
        public static void Setup(Transform board, EraLook el)
        {
            System.IO.Directory.CreateDirectory(MatDir); System.IO.Directory.CreateDirectory(MeshDir);
            var rs = new List<Renderer>(); var med = new List<Material>(); var goth = new List<Material>(); var fut = new List<Material>(); var mod = new List<Material>(); var spc = new List<Material>();   // spc = 深空（11-13，后缀 _s：钢蓝 + 橙 对 焦红 + 琥珀）
            // 11-13：第三套 = 现代（Modern/、后缀 _m，样图 A：藏青 + 荧光黄绿 对 沙色 + 红）
            void Swap(Renderer r, Material g, Material f, Material mo = null, Material sp = null) { if (!r || (!g && !f && !mo && !sp)) return; rs.Add(r); med.Add(r.sharedMaterial); goth.Add(g); fut.Add(f); mod.Add(mo); spc.Add(sp); }

            Material Clone(Material src, string name, System.Action<Material> edit)
            {
                var path = $"{MatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
                else m.CopyPropertiesFromMaterial(src);
                m.shader = src.shader; edit(m); EditorUtility.SetDirty(m); return m;
            }
            Material TexClone(Material src, string name, Texture2D t) => t ? Clone(src, name, x => x.SetTexture("_BaseMap", t)) : null;
            Texture2D ETex(string dir, string file)
            {
                var path = $"Assets/_WarTable/Art/Textures/{dir}/{file}.png";
                if (!System.IO.File.Exists(path)) return null;
                AssetDatabase.ImportAsset(path);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            Texture2D STex(string name) => System.IO.File.Exists($"Assets/_WarTable/Art/Textures/{name}.png") ? BoardBuilder.Tex(name) : null;   // 还没出图的帅旗：留空，运行时退回中古
            Texture2D Cov(string name) => System.IO.File.Exists($"ArtSource/Cover/v6/alpha/{name}.png") ? CoverBuilder.Tex(name) : null;

            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);   // 沙盘 + 结算板
            foreach (var r in all)
            {
                var m = r.sharedMaterial; if (!m) continue;
                switch (m.name)
                {
                    case "M_Standard_kingdom": Swap(r, TexClone(m, "M_Standard_bell", BoardBuilder.Tex("standard_bell")), TexClone(m, "M_Standard_prism", BoardBuilder.Tex("standard_prism")), TexClone(m, "M_Standard_shield", STex("standard_shield")), TexClone(m, "M_Standard_lancer", STex("standard_lancer"))); break;
                    case "M_Standard_clan": Swap(r, TexClone(m, "M_Standard_forge", BoardBuilder.Tex("standard_forge")), TexClone(m, "M_Standard_swarm", BoardBuilder.Tex("standard_swarm")), TexClone(m, "M_Standard_sand", STex("standard_sand")), TexClone(m, "M_Standard_ember", STex("standard_ember"))); break;
                    case "M_C6_R_Flag_Ours": Swap(r, TexClone(m, "M_R_Flag_bell", Cov("banner_bell")), TexClone(m, "M_R_Flag_prism", Cov("banner_prism")), TexClone(m, "M_R_Flag_shield", Cov("banner_shield")), TexClone(m, "M_R_Flag_lancer", Cov("banner_lancer"))); break;
                    case "M_C6_R_Flag_Theirs": Swap(r, TexClone(m, "M_R_Flag_forge", Cov("banner_forge")), TexClone(m, "M_R_Flag_swarm", Cov("banner_swarm")), TexClone(m, "M_R_Flag_sand", Cov("banner_sand")), TexClone(m, "M_R_Flag_ember", Cov("banner_ember"))); break;
                    case "M_Pennant_Blue": Swap(r, Clone(m, "M_Pennant_Verdigris", x => x.SetColor("_BaseColor", Verdigris * 1.1f)), Clone(m, "M_Pennant_Cyan", x => x.SetColor("_BaseColor", Cyan * 1.1f)), Clone(m, "M_Pennant_HiVis", x => x.SetColor("_BaseColor", HiVis * 1.05f))); break;
                    case "M_Tent_Kingdom_B": case "M_Tent_Clan_A": case "M_Tent_Clan_B":
                        Swap(r, TexClone(m, m.name + "_G", ETex("Gothic", m.name.Substring(2) + "_g")), TexClone(m, m.name + "_F", ETex("Future", m.name.Substring(2) + "_f")), TexClone(m, m.name + "_M", ETex("Modern", m.name.Substring(2) + "_m")));
                        break;
                }
            }
            el.swapR = rs.ToArray(); el.swapMed = med.ToArray(); el.swapGoth = goth.ToArray(); el.swapFut = fut.ToArray(); el.swapMod = mod.ToArray(); el.swapSpc = spc.ToArray();

            // 程序圆帐（蓝白条纹 = 顶点色）：哥特 蓝 → 铜绿、白 → 骨白；未来 蓝 → 天青、白不变
            var mf = new List<MeshFilter>(); var mm = new List<Mesh>(); var mg = new List<Mesh>(); var mfu = new List<Mesh>(); var mmo = new List<Mesh>();
            var made = new Dictionary<Mesh, (Mesh g, Mesh f, Mesh m)>();
            Mesh Recolor(Mesh src, string sfx, System.Func<Color, Color> fn)
            {
                var path = $"{MeshDir}/{src.name}_{sfx}.asset";
                var g = Object.Instantiate(src); g.name = src.name + "_" + sfx;
                var c = g.colors; for (int i = 0; i < c.Length; i++) c[i] = fn(c[i]); g.colors = c;
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);   // CopySerialized 覆盖网格资产不生效：删了重建
                AssetDatabase.CreateAsset(g, path); return g;
            }
            foreach (var f in board.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.transform.parent && f.transform.parent.name.StartsWith("Tent_Kingdom_A") && f.name == "Body"))
            {
                var src = f.sharedMesh;
                if (!made.TryGetValue(src, out var done))   // 几座圆帐共用一个网格：只做一次
                {
                    bool Blue(Color c, out float s, out float v) { Color.RGBToHSV(c, out var h, out s, out v); return h > 0.52f && h < 0.74f && s > 0.25f; }
                    done = (Recolor(src, "G", c => Blue(c, out var s, out var v) ? Color.HSVToRGB(0.47f, s * 0.75f, v * 0.75f) : new Color(c.r * 0.95f, c.g * 0.91f, c.b * 0.82f, c.a)),
                            Recolor(src, "F", c => Blue(c, out var s, out var v) ? Color.HSVToRGB(0.525f, s * 0.8f, Mathf.Min(1f, v * 1.25f)) : c),
                            // 现代：蓝 → 藏青，白 → 浅灰（帆布帐篷）
                            Recolor(src, "M", c => Blue(c, out var s, out var v) ? Color.HSVToRGB(0.61f, Mathf.Min(1f, s * 0.95f), v * 0.5f) : new Color(c.r * 0.8f, c.g * 0.82f, c.b * 0.84f, c.a)));
                    made[src] = done;
                }
                mf.Add(f); mm.Add(src); mg.Add(done.g); mfu.Add(done.f); mmo.Add(done.m);
            }
            el.meshF = mf.ToArray(); el.meshMed = mm.ToArray(); el.meshGoth = mg.ToArray(); el.meshFut = mfu.ToArray(); el.meshMod = mmo.ToArray();
            EditorUtility.SetDirty(el);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GothicEra] 换材质 {rs.Count}（哥特 {goth.Count(x => x)}、未来 {fut.Count(x => x)}、现代 {mod.Count(x => x)}），换网格 {mf.Count}，哥特孪生布景 {board.GetComponentsInChildren<EraOnly>(true).Count(e => e.era == 1)}");
        }
    }
}
