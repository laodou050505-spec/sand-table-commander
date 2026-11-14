using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 程序化布景件：P2 做不干净的东西在这里搭（几何自己生成，保证封闭、没有破面）。
    /// 圆帐：P2 版帐壁下半截贴图一直碎（重做贴图也没好），改成程序版——蓝白条纹帐壁 + 帐顶 + 金檐 + 扇贝垂饰 + 门帘 + 顶上三角小旗。
    /// 尺寸单位：沙盘本地米（1 码 = 1 cm），和 P2 版圆帐（高 4.2 cm）一致。
    /// </summary>
    public static class ProcProps
    {
        static readonly Color Blue = new Color(0.16f, 0.27f, 0.68f), White = new Color(0.93f, 0.91f, 0.86f), Gold = new Color(0.9f, 0.7f, 0.28f),
                              DoorBlue = new Color(0.11f, 0.18f, 0.48f), Earth = new Color(0.42f, 0.33f, 0.24f);

        class MD
        {
            public List<Vector3> v = new(); public List<Color> c = new(); public List<int> t = new(); public List<Vector2> uv = new();
            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                for (int k = 0; k < 4; k++) c.Add(col);
                UV(a, b, cc, d);
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            public void Tri(Vector3 a, Vector3 b, Vector3 cc, Color col)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(cc);
                for (int k = 0; k < 3; k++) c.Add(col);
                UV(a, b, cc);
                t.AddRange(new[] { i, i + 1, i + 2 });
            }
            /// <summary>柱面 UV（帆布贴图）：u = 弧长，v = 高度；同一个面内的角度相对第一个点展开，接缝处不会整张挤进一格。</summary>
            void UV(params Vector3[] ps)
            {
                float a0 = float.NaN;
                foreach (var p in ps)
                {
                    float r = new Vector2(p.x, p.z).magnitude;
                    float a = r < 1e-5f ? float.NaN : Mathf.Atan2(p.x, p.z);
                    if (float.IsNaN(a0) && !float.IsNaN(a)) a0 = a;
                    if (float.IsNaN(a)) a = float.IsNaN(a0) ? 0 : a0;
                    while (a - a0 > Mathf.PI) a -= Mathf.PI * 2; while (a0 - a > Mathf.PI) a += Mathf.PI * 2;
                    uv.Add(new Vector2(a * 0.0175f * 14f, p.y * 14f));
                }
            }
        }

        static Vector3 P(float r, float a, float y) => new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);

        static Mesh pavMesh, penMesh;
        /// <summary>每次 PropBuilder.Build 开头调用：网格只生成一次，几顶圆帐共用（重复删建资产会把前一顶的网格删掉）。</summary>
        public static void ResetCache() { pavMesh = null; penMesh = null; }

        public static GameObject Pavilion(Transform parent)
        {
            pavMesh ??= PavilionMesh();
            penMesh ??= PennantMesh();
            return Assemble(parent);
        }

        static Mesh PavilionMesh()
        {
            const float Rt = 0.0158f, Rb = 0.0172f, Hw = 0.021f, Re = 0.0188f, He = Hw + 0.0012f, Apex = Hw + 0.0185f;
            const int Stripes = 20, Sub = 3, N = Stripes * Sub;
            var md = new MD();
            float A(int i) => i * Mathf.PI * 2f / N;
            // 帐壁：每道条纹 3 段，条纹之间颜色硬切
            for (int i = 0; i < N; i++)
            {
                var col = (i / Sub) % 2 == 0 ? Blue : White;
                md.Quad(P(Rb, A(i), 0), P(Rt, A(i), Hw), P(Rt, A(i + 1), Hw), P(Rb, A(i + 1), 0), col);
            }
            // 帐顶：檐口 → 中圈（略内凹）→ 顶点
            const float Rm = 0.0105f, Hm = Hw + 0.0105f;
            for (int i = 0; i < N; i++)
            {
                var col = (i / Sub) % 2 == 0 ? Blue : White;
                md.Quad(P(Re, A(i), He), P(Rm, A(i), Hm), P(Rm, A(i + 1), Hm), P(Re, A(i + 1), He), col);
                md.Tri(P(Rm, A(i), Hm), new Vector3(0, Apex, 0), P(Rm, A(i + 1), Hm), col);
            }
            // 檐口底面（从帐壁顶到檐边，挡住缝）
            for (int i = 0; i < N; i++) md.Quad(P(Rt, A(i), Hw), P(Re, A(i), He), P(Re, A(i + 1), He), P(Rt, A(i + 1), Hw), White * 0.8f);
            // 金色檐边：一圈竖带
            for (int i = 0; i < N; i++) md.Quad(P(Re, A(i), He), P(Re + 0.0002f, A(i), He - 0.0013f), P(Re + 0.0002f, A(i + 1), He - 0.0013f), P(Re, A(i + 1), He), Gold);
            // 扇贝垂饰：每道条纹一片半圆形布舌（蓝），挂在金边下面，朝外
            for (int s = 0; s < Stripes; s++)
            {
                float a0 = A(s * Sub), a1 = A((s + 1) * Sub), r = Re + 0.0003f, top = He - 0.0013f;
                var ctr = P(r, (a0 + a1) * 0.5f, top);
                const int K = 6;
                for (int k = 0; k < K; k++)
                {
                    float u0 = (float)k / K, u1 = (float)(k + 1) / K;
                    float d0 = 0.0028f * Mathf.Sin(u0 * Mathf.PI) + 0.0006f, d1 = 0.0028f * Mathf.Sin(u1 * Mathf.PI) + 0.0006f;
                    var q0 = P(r, Mathf.Lerp(a0, a1, u0), top - d0); var q1 = P(r, Mathf.Lerp(a0, a1, u1), top - d1);
                    md.Tri(P(r, Mathf.Lerp(a0, a1, u0), top), q0, q1, s % 2 == 0 ? Blue : White);
                    md.Tri(P(r, Mathf.Lerp(a0, a1, u0), top), q1, P(r, Mathf.Lerp(a0, a1, u1), top), s % 2 == 0 ? Blue : White);
                }
            }
            // 门帘：正面（+Z）一块深蓝尖拱，贴在帐壁外 0.2 mm，下沿两片略分开
            {
                float half = 0.0042f, h = 0.0165f, off = 0.00025f;
                const int K = 8;
                for (int k = 0; k < K; k++)
                {
                    float y0 = h * k / K, y1 = h * (k + 1) / K;
                    float w0 = half * Mathf.Sqrt(1f - Mathf.Pow(Mathf.Max(0, y0 - h * 0.55f) / (h * 0.45f), 2)), w1 = half * Mathf.Sqrt(Mathf.Max(0, 1f - Mathf.Pow(Mathf.Max(0, y1 - h * 0.55f) / (h * 0.45f), 2)));
                    float rr0 = Mathf.Lerp(Rb, Rt, y0 / Hw) + off, rr1 = Mathf.Lerp(Rb, Rt, y1 / Hw) + off;
                    md.Quad(new Vector3(-w0, y0, rr0), new Vector3(-w1, y1, rr1), new Vector3(w1, y1, rr1), new Vector3(w0, y0, rr0), DoorBlue);
                }
                // 门帘金边中缝
                md.Quad(new Vector3(-0.0002f, 0, Rb + off * 2), new Vector3(-0.0002f, h * 0.98f, Rt + off * 2 + (Rb - Rt) * 0.02f), new Vector3(0.0002f, h * 0.98f, Rt + off * 2), new Vector3(0.0002f, 0, Rb + off * 2), Gold);
            }
            // 土坪：不规则的低圆台
            {
                const int K = 28; float y = 0.0007f;
                for (int k = 0; k < K; k++)
                {
                    float a0 = k * Mathf.PI * 2 / K, a1 = (k + 1) * Mathf.PI * 2 / K;
                    float r0 = Rb * (1.28f + 0.08f * Mathf.Sin(a0 * 3 + 1) + 0.05f * Mathf.Sin(a0 * 7)), r1 = Rb * (1.28f + 0.08f * Mathf.Sin(a1 * 3 + 1) + 0.05f * Mathf.Sin(a1 * 7));
                    md.Tri(new Vector3(0, y, 0), P(r1, a1, y), P(r0, a0, y), Earth);
                    md.Quad(P(r0, a0, y), P(r1, a1, y), P(r1 * 1.04f, a1, -0.001f), P(r0 * 1.04f, a0, -0.001f), Earth * 0.85f);
                }
            }

            var m = new Mesh { name = "Pavilion" };
            // 布褶和明暗（乘进顶点色）：条纹中间微凹、檐下和帐壁底部压暗、帐顶越往上越亮
            for (int i = 0; i < md.v.Count; i++)
            {
                var p = md.v[i];
                float a = Mathf.Atan2(p.x, p.z), phase = a / (Mathf.PI * 2f) * Stripes;
                float fold = 0.9f + 0.1f * Mathf.Cos((phase - Mathf.Floor(phase)) * Mathf.PI * 2f);
                float shade = 1f;
                if (p.y < Hw + 0.0001f) shade *= Mathf.Lerp(0.8f, 1f, Mathf.InverseLerp(0f, 0.005f, p.y)) * Mathf.Lerp(1f, 0.82f, Mathf.InverseLerp(Hw - 0.006f, Hw, p.y));
                else shade *= Mathf.Lerp(0.88f, 1.04f, Mathf.InverseLerp(Hw, Apex, p.y));
                if (p.y < 0.001f) { fold = 1f; shade = 1f; }   // 土坪
                var c = md.c[i]; md.c[i] = new Color(c.r * fold * shade, c.g * fold * shade, c.b * fold * shade, c.a);
            }
            m.SetVertices(md.v); m.SetColors(md.c); m.SetTriangles(md.t, 0); m.SetUVs(0, md.uv);
            FixOutward(m);
            m.RecalculateNormals(); m.RecalculateBounds();
            return Save(m, "Pavilion");
        }

        static GameObject Assemble(Transform parent)
        {
            const float Hw = 0.021f, Apex = Hw + 0.0185f;
            var m = pavMesh;
            var root = new GameObject("Tent_Kingdom_A");
            root.transform.SetParent(parent, false);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BoardBuilder.Mat("M_Pavilion", "WarTable/MiniLit", mm => { mm.SetTexture("_BaseMap", BoardBuilder.Tex("painted_canvas")); mm.SetFloat("_UseVertexColor", 1); mm.SetColor("_BaseColor", new Color(1.08f, 1.06f, 1.04f)); mm.SetFloat("_Gloss", 0.03f); mm.SetFloat("_Cull", 2); });
            mr.shadowCastingMode = ShadowCastingMode.On;
            // 顶上金尖 + 小旗杆 + 三角小旗（随风飘）
            var gold = BoardBuilder.Mat("M_StdGold", "WarTable/MiniLit");
            var wood = BoardBuilder.Mat("M_StdPole", "WarTable/MiniLit");
            Prim(PrimitiveType.Sphere, root.transform, gold, new Vector3(0, Apex + 0.0004f, 0), new Vector3(0.0028f, 0.0022f, 0.0028f));
            Prim(PrimitiveType.Cylinder, root.transform, wood, new Vector3(0, Apex + 0.0045f, 0), new Vector3(0.0007f, 0.004f, 0.0007f));
            Prim(PrimitiveType.Sphere, root.transform, gold, new Vector3(0, Apex + 0.0088f, 0), Vector3.one * 0.0012f);
            var pen = new GameObject("Pennant");
            pen.transform.SetParent(root.transform, false);
            pen.transform.localPosition = new Vector3(0.0003f, Apex + 0.0045f, 0);
            pen.AddComponent<MeshFilter>().sharedMesh = penMesh;
            var pr = pen.AddComponent<MeshRenderer>();
            pr.sharedMaterial = BoardBuilder.Mat("M_Pennant_Blue", "WarTable/FlagWave", mm => { mm.SetTexture("_BaseMap", Texture2D.whiteTexture); mm.SetColor("_BaseColor", Blue * 1.15f); mm.SetFloat("_Width", 0.009f); mm.SetFloat("_Amp", 0.14f); mm.SetFloat("_Speed", 3f); });
            pr.shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }

        static Mesh PennantMesh()
        {
            const int C = 10, R = 6; const float W = 0.009f, H = 0.0042f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= R; j++)
            {
                float vv = (float)j / R, xmax = W * (1f - Mathf.Abs(2f * vv - 1f)) + 0.0004f;
                for (int i = 0; i <= C; i++) { v.Add(new Vector3(xmax * i / C, vv * H, 0)); uv.Add(new Vector2((float)i / C, vv)); }
            }
            for (int j = 0; j < R; j++)
                for (int i = 0; i < C; i++) { int a = j * (C + 1) + i, b = a + 1, c = a + C + 1, d = c + 1; t.AddRange(new[] { a, c, b, b, c, d }); }
            var m = new Mesh { name = "Pennant" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals();
            m.bounds = new Bounds(new Vector3(W * 0.5f, H * 0.5f, 0), new Vector3(W * 1.4f, H * 1.6f, W));
            return Save(m, "Pennant");
        }

        /// <summary>按“法线背离竖轴 / 顶面朝上”逐个三角形统一绕序（手写网格容易有几片反了）。</summary>
        static void FixOutward(Mesh m)
        {
            var v = m.vertices; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                var ctr = (a + b + c) / 3f;
                var outw = new Vector3(ctr.x, 0, ctr.z);
                // 檐口底面朝下，其余朝外 / 朝上
                bool under = Mathf.Abs(n.normalized.y) > 0.3f && ctr.y > 0.019f && ctr.y < 0.0225f && new Vector2(ctr.x, ctr.z).magnitude < 0.0185f;
                float want = under ? -n.y : Vector3.Dot(n, outw) + (ctr.y < 0.001f ? n.y : 0f) + (ctr.y > 0.0225f ? n.y * 0.5f : 0f);
                if (want < 0) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
            m.triangles = t;
        }

        static Mesh Save(Mesh m, string name)
        {
            string path = $"Assets/_WarTable/Art/Generated/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
