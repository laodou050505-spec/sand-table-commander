using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 封面主角身上的贴花（11-19 用户：“可以做一些很酷的图案，像纹身一样贴在模型上”“右胸口贴公司标志，不要太大”）。
    /// 做法：不画进主角的大贴图（那张图一个像素约 0.75 mm、又被切成几厘米的小块，标志画进去是糊的），
    /// 而是把落在投影框里的那几块甲片表面“拓”下来做成一张薄片网格，沿法线抬高半毫米，用自己的贴图、半透明的受光材质盖在上面。
    /// 贴图在 Art/Models/P2/Hero_Statue/Decals/（Tools/hero_decals.py 生成）；位置在下面的表里，坐标用 <see cref="Pick"/> 从验收图上点出来。
    /// 模型重做后位置要重新点。
    /// </summary>
    public static class CoverHeroDecals
    {
        const string Dir = "Assets/_WarTable/Art/Models/P2/Hero_Statue/Decals/";
        const float Lift = 0.0005f;                       // 抬离甲片的距离（米）：再小侧看会和甲片打架闪烁

        struct Spec
        {
            public string name;      // 贴图文件名（不带 .png）
            public Vector3 at;       // 贴花中心：雕像本地包围盒里的归一化坐标（x 玩家看过去的左边为大 / y 上 / z 朝玩家为大；Pick 打印的那个）
            public float width;      // 贴图整张的宽（米）；高按贴图比例
            public float roll;       // 绕投影方向转多少度
            public Color tint;       // 颜色 × 透明度
            public float smooth;     // 漆面光滑度
            public int over;         // 叠在第几层：0 贴甲片，1 盖在别的贴花上（渲染顺序和抬高量都跟着加）
            public float fill;       // >0：自动居中到所在那块甲片的正中，宽度 = 甲片可用宽高的这个比例（width 只当上限）
        }

        static readonly Color Ivory = new(0.86f, 0.82f, 0.72f, 0.92f), Gold = new(0.80f, 0.60f, 0.26f, 0.92f), Chalk = new(0.88f, 0.86f, 0.80f, 0.80f);

        /// <summary>公司标志按品牌规范原样用（白字版、不改色不做旧、不拉伸）；其余是做旧的喷漆 / 漆绘。</summary>
        static readonly Spec[] Specs =
        {
            // 11-19 晚 用户：“锤子那个要在锤面正中间，膝盖那个要在膝盖正中间，别把贴图位置弄得太乱”
            //   → 除胸口图标外都改成自动居中（fill > 0）：从 at 点往外找出它所在的那一整块甲片（碰到折角 / 沟槽就停），贴在甲片外框的正中，大小按甲片定；
            //   at 只要点在那块甲片上就行。只留左右成对的位置（双肩、双膝）+ 胸口 + 锤面，去掉了只有一边的前臂刻痕（tally）。
            // 右胸（角色自己的右边 = 玩家看过去的左边）：公司图标，约 1.7 cm 宽。用户：只要图标、不带字、底下不垫底
            new Spec { name = "logo", at = new Vector3(0.640f, 0.800f, 0.618f), width = 0.029f, roll = 8f, tint = Color.white, smooth = 0.25f },
            new Spec { name = "crest", at = new Vector3(0.307f, 0.863f, 0.599f), width = 0.036f, fill = 0.95f, tint = Gold, smooth = 0.34f },     // 左肩甲正对玩家那一面（上沿那道斜面不算）：翼星长枪团徽
            new Spec { name = "num07", at = new Vector3(0.757f, 0.864f, 0.596f), width = 0.032f, fill = 0.66f, tint = Ivory, smooth = 0.30f },    // 右肩甲正对玩家那一面：编号
            new Spec { name = "chevron", at = new Vector3(0.742f, 0.360f, 0.628f), width = 0.030f, fill = 0.58f, tint = Ivory, smooth = 0.30f },  // 右膝甲：军衔折线
            new Spec { name = "filigree", at = new Vector3(0.296f, 0.354f, 0.622f), width = 0.040f, fill = 0.70f, tint = Ivory, smooth = 0.30f }, // 左膝甲：彗星羽纹
            new Spec { name = "sun", at = new Vector3(0.541f, 0.225f, 0.878f), width = 0.032f, fill = 0.72f, tint = Gold, smooth = 0.34f },       // 锤头正面中间那块：日轮
        };

        public static void Build(Transform statue, Renderer lod0, uint layer)
        {
            AssetDatabase.Refresh();
            if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir, "D_*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            World(lod0, out var v, out var n, out var t);
            var lb = Box(statue, v);
            foreach (var s in Specs)
            {
                var tex = Tex(s.name); if (!tex) { Debug.LogWarning("[Cover] 贴花贴图缺失：" + s.name); continue; }
                var A = statue.TransformPoint(lb.min + Vector3.Scale(s.at, lb.size)); var spec = s;
                if (s.fill > 0f)
                {
                    var pl = Plate(A, statue, s.roll, v, n, t);
                    float aspect = (float)tex.height / tex.width, wFit = Mathf.Min(pl.w, pl.h / aspect) * s.fill;
                    spec.width = Mathf.Min(s.width, wFit); A = pl.center;
                    Debug.Log($"[Cover] 贴花 {s.name}：甲片 {pl.w * 100f:F1}×{pl.h * 100f:F1} cm（{pl.tris} 面），中心挪了 {pl.shift * 1000f:F1} mm，宽 {spec.width * 100f:F1} cm");
                }
                var mesh = Project(spec, tex, statue, A, v, n, t);
                if (!mesh) { Debug.LogWarning("[Cover] 贴花没有落到模型上：" + s.name); continue; }
                AssetDatabase.CreateAsset(mesh, Dir + "D_" + s.name + ".asset");
                var go = new GameObject("Decal_" + s.name); go.transform.SetParent(statue, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.renderingLayerMask = layer;
                r.sharedMaterial = BoardBuilder.Mat("M_Cover_HeroDecal_" + s.name, "Universal Render Pipeline/Lit", m =>
                {
                    m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", s.tint);
                    m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", s.smooth);
                    // 半透明、普通混合：透明的地方什么都不画（“保留高光”那种混合会在看不见的地方留一层反光）
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_BlendModePreserveSpecular", 0f); m.SetFloat("_AlphaClip", 0f);
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f); m.SetFloat("_QueueOffset", s.over); m.renderQueue = (int)RenderQueue.Transparent + s.over;
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHATEST_ON");
                    m.SetShaderPassEnabled("ShadowCaster", false); m.SetShaderPassEnabled("DepthOnly", false);
                });
            }
        }

        static Texture2D Tex(string name)
        {
            var path = Dir + name + ".png"; if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.wrapMode != TextureWrapMode.Clamp || !ti.alphaIsTransparency || ti.anisoLevel != 8 || ti.maxTextureSize != 1024))
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 8; ti.maxTextureSize = 1024; ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void World(Renderer r, out Vector3[] v, out Vector3[] n, out int[] t)
        {
            var m = r.GetComponent<MeshFilter>().sharedMesh; var tf = r.transform;
            v = m.vertices; n = m.normals; t = m.triangles;
            for (int i = 0; i < v.Length; i++) { v[i] = tf.TransformPoint(v[i]); n[i] = tf.TransformDirection(n[i]).normalized; }
        }

        /// <summary>雕像本地坐标下的包围盒。贴花坐标按它归一化——不用网格自己的本地坐标：模型重新导出后本地坐标轴会换方向（11-19 踩过）。</summary>
        static Bounds Box(Transform statue, Vector3[] worldVerts)
        {
            var b = new Bounds(statue.InverseTransformPoint(worldVerts[0]), Vector3.zero);
            foreach (var p in worldVerts) b.Encapsulate(statue.InverseTransformPoint(p));
            return b;
        }

        /// <summary>
        /// 找出点 A 所在的那一整块甲片：从最近的三角形往外长，相邻两面夹角超过 StepDeg 就算碰到了折角 / 沟槽，和起点朝向差超过 SeedDeg 也停，最远 6 cm。
        /// 返回甲片在贴花平面里的外框（宽、高）和外框正中落在模型表面上的点。
        /// </summary>
        public static float StepDeg = 22f, SeedDeg = 62f;   // 14° / 45° 时膝甲（弧面）只找到上半截、肩甲只找到下半块
        public struct PlateInfo { public Vector3 center; public float w, h, shift; public int tris; public List<int> faces; }
        public static PlateInfo Plate(Vector3 A, Transform statue, float roll, Vector3[] v, Vector3[] n, int[] t)
        {
            int T = t.Length / 3;
            var fn = new Vector3[T]; var fc = new Vector3[T];
            var weld = new Dictionary<Vector3Int, int>(); var vid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 20000f);   // 0.05 mm 内算同一个顶点（贴图接缝处的顶点是拆开的）
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
                vid[i] = id;
            }
            var byVert = new List<int>[weld.Count];
            for (int k = 0; k < T; k++)
            {
                Vector3 a = v[t[3 * k]], b = v[t[3 * k + 1]], c = v[t[3 * k + 2]];
                fn[k] = Vector3.Cross(b - a, c - a).normalized; fc[k] = (a + b + c) / 3f;
                for (int q = 0; q < 3; q++) (byVert[vid[t[3 * k + q]]] ??= new List<int>()).Add(k);
            }
            int seed = 0; float bd = float.MaxValue;
            for (int k = 0; k < T; k++) { float d = (fc[k] - A).sqrMagnitude; if (d < bd) { bd = d; seed = k; } }
            float stepCos = Mathf.Cos(StepDeg * Mathf.Deg2Rad), seedCos = Mathf.Cos(SeedDeg * Mathf.Deg2Rad);
            var seen = new HashSet<int> { seed }; var q2 = new Queue<int>(); q2.Enqueue(seed); var faces = new List<int>();
            while (q2.Count > 0)
            {
                int k = q2.Dequeue(); faces.Add(k);
                for (int e = 0; e < 3; e++)
                    foreach (int j in byVert[vid[t[3 * k + e]]])
                        if (!seen.Contains(j) && Vector3.Dot(fn[j], fn[k]) > stepCos && Vector3.Dot(fn[j], fn[seed]) > seedCos && (fc[j] - A).magnitude < 0.06f)
                        { seen.Add(j); q2.Enqueue(j); }
            }
            // 贴花平面：和 Project 里一样的朝上 / 朝右
            var N = Vector3.zero; foreach (int k in faces) N += fn[k] * Vector3.Cross(v[t[3 * k + 1]] - v[t[3 * k]], v[t[3 * k + 2]] - v[t[3 * k]]).magnitude;
            N.Normalize(); var d0 = -N;
            var up = Mathf.Abs(N.y) > 0.9f ? statue.forward : Vector3.up;
            var right = Vector3.Cross(up, d0).normalized; var upv = Vector3.Cross(d0, right);
            var rq = Quaternion.AngleAxis(roll, d0); right = rq * right; upv = rq * upv;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (int k in faces)
                for (int q = 0; q < 3; q++)
                {
                    var pp = v[t[3 * k + q]] - A; float x = Vector3.Dot(pp, right), y = Vector3.Dot(pp, upv);
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
            Vector2 mid = new((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            int best = seed; float bm = float.MaxValue;
            foreach (int k in faces)
            {
                var pp = fc[k] - A; float dd = (new Vector2(Vector3.Dot(pp, right), Vector3.Dot(pp, upv)) - mid).sqrMagnitude;
                if (dd < bm) { bm = dd; best = k; }
            }
            var bc = fc[best] - A; var center = fc[best] + right * (mid.x - Vector3.Dot(bc, right)) + upv * (mid.y - Vector3.Dot(bc, upv));
            return new PlateInfo { center = center, w = x1 - x0, h = y1 - y0, shift = (center - A).magnitude, tris = faces.Count, faces = faces };
        }

        /// <summary>验收用：把每个自动居中的贴花找到的甲片染成红色，拍正面（看甲片找得对不对），拍完删掉。</summary>
        public static string ShowPlates(string path)
        {
            var hr = HeroLod0(out var cover); World(hr, out var v, out var n, out var t);
            var statue = hr.transform.parent; var lb = Box(statue, v); var made = new List<GameObject>(); var sb = new System.Text.StringBuilder();
            var red = new Material(Shader.Find("Universal Render Pipeline/Unlit")); red.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f));
            try
            {
                foreach (var s in Specs)
                {
                    if (s.fill <= 0f) continue;
                    var pl = Plate(statue.TransformPoint(lb.min + Vector3.Scale(s.at, lb.size)), statue, s.roll, v, n, t);
                    var mv = new List<Vector3>(); var mt = new List<int>();
                    foreach (int k in pl.faces) for (int q = 0; q < 3; q++) { mt.Add(mv.Count); var w = v[t[3 * k + q]]; mv.Add(w + n[t[3 * k + q]] * 0.0006f); }
                    var mesh = new Mesh(); mesh.SetVertices(mv); mesh.SetTriangles(mt, 0);
                    var go = new GameObject("__plate"); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = red; made.Add(go);
                    var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere); dot.transform.position = pl.center; dot.transform.localScale = Vector3.one * 0.003f; made.Add(dot);
                    sb.Append($"{s.name}: {pl.w * 100f:F1}×{pl.h * 100f:F1} cm {pl.tris} 面 挪 {pl.shift * 1000f:F1} mm | ");
                }
                sb.Append(Shoot(path, "front,low,up"));
            }
            finally { foreach (var g in made) Object.DestroyImmediate(g); }
            return sb.ToString();
        }

        /// <summary>贴花朝哪边贴：中心周围半个贴花宽以内、和最近那块面朝向相近的面，按面积平均法线。</summary>
        static Vector3 Facing(Vector3 A, float radius, Vector3[] v, Vector3[] n, int[] t)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < t.Length; i += 3)
            {
                float d = ((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f - A).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            var n0 = (n[t[best]] + n[t[best + 1]] + n[t[best + 2]]).normalized; var sum = Vector3.zero;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (((a + b + c) / 3f - A).sqrMagnitude > radius * radius) continue;
                var tn = (n[t[i]] + n[t[i + 1]] + n[t[i + 2]]).normalized;
                if (Vector3.Dot(tn, n0) > 0.5f) sum += tn * Vector3.Cross(b - a, c - a).magnitude;
            }
            return sum.sqrMagnitude > 0f ? sum.normalized : n0;
        }

        static Mesh Project(Spec s, Texture2D tex, Transform statue, Vector3 A, Vector3[] v, Vector3[] n, int[] t)
        {
            float w = s.width, h = s.width * tex.height / tex.width, depth = Mathf.Max(0.008f, w * 0.35f);
            var N = Facing(A, w * 0.4f, v, n, t); var d = -N;                        // 沿 d 往甲片上看
            var up = Mathf.Abs(N.y) > 0.9f ? statue.forward : Vector3.up;
            var right = Vector3.Cross(up, d).normalized; var upv = Vector3.Cross(d, right);
            var q = Quaternion.AngleAxis(s.roll, d); right = q * right; upv = q * upv;
            var map = new Dictionary<int, int>(); var ov = new List<Vector3>(); var on = new List<Vector3>(); var ouv = new List<Vector2>(); var ot = new List<int>();
            var x = new float[3]; var y = new float[3];
            for (int i = 0; i < t.Length; i += 3)
            {
                var tn = (n[t[i]] + n[t[i + 1]] + n[t[i + 2]]).normalized;
                if (Vector3.Dot(tn, N) < 0.35f) continue;                           // 侧着的面不贴（图案会被拉长）
                bool near = true;
                for (int k = 0; k < 3; k++)
                {
                    var p = v[t[i + k]] - A; x[k] = Vector3.Dot(p, right); y[k] = Vector3.Dot(p, upv);
                    if (Mathf.Abs(Vector3.Dot(p, d)) > depth) near = false;         // 只贴这一层甲片，不穿到后面的部件上
                }
                if (!near || Mathf.Max(x[0], x[1], x[2]) < -w / 2 || Mathf.Min(x[0], x[1], x[2]) > w / 2 || Mathf.Max(y[0], y[1], y[2]) < -h / 2 || Mathf.Min(y[0], y[1], y[2]) > h / 2) continue;
                for (int k = 0; k < 3; k++)
                {
                    int src = t[i + k];
                    if (!map.TryGetValue(src, out int dst))
                    {
                        dst = ov.Count; map[src] = dst;
                        ov.Add(statue.InverseTransformPoint(v[src] + n[src] * Lift * (1f + 0.7f * s.over))); on.Add(statue.InverseTransformDirection(n[src]).normalized);
                        ouv.Add(new Vector2(x[k] / w + 0.5f, y[k] / h + 0.5f));
                    }
                    ot.Add(dst);
                }
            }
            if (ot.Count == 0) return null;
            var mesh = new Mesh { name = "D_" + s.name };
            mesh.SetVertices(ov); mesh.SetNormals(on); mesh.SetUVs(0, ouv); mesh.SetTriangles(ot, 0); mesh.RecalculateBounds();
            return mesh;
        }

        // ───────────────────────── 验收机位 / 取点工具（给 cs.py 用）

        static Renderer HeroLod0(out Transform cover)
        {
            cover = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == "Cover").transform;
            return cover.GetComponentInChildren<WarTable.Game.CoverHero>(true).statue.Find("LOD0").GetComponent<Renderer>();
        }

        /// <summary>主角的六个验收机位：seat 座位 / front 正面 / up 上半身 / low 下半身 / L 左前 / R 右前。</summary>
        static void View(string k, Transform cover, Bounds b, out Vector3 pos, out Vector3 look, out float fov)
        {
            Vector3 c = b.center, f = cover.forward, rt = cover.right, up = Vector3.up; float h = b.size.y; fov = 42f; look = c;
            switch (k)
            {
                case "seat": pos = WTCapture.SeatEye; look = new Vector3(0f, -0.02f, -0.06f); fov = 55f; break;
                // 玩家真实机位：头显里封面原点在眼前 0.8 m、高出眼睛 4 cm、竖直正对（LazyFollow + MenuCover.below）。"seat" 是坐在沙盘前往下 28° 看，不是玩家看封面的样子
                case "player": { float ey = EyeY(cover); pos = cover.TransformPoint(new Vector3(0f, ey, 0.8f)); look = cover.TransformPoint(new Vector3(0f, ey, 0f)); fov = 62f; break; }
                case "up": look = c + up * h * 0.22f; pos = look + f * 0.30f + rt * 0.05f; break;
                case "low": look = c - up * h * 0.28f; pos = c - up * h * 0.26f + f * 0.30f - rt * 0.04f; break;
                case "L": pos = c + (f * 0.64f - rt * 0.77f) * 0.6f + up * 0.03f; break;
                case "R": pos = c + (f * 0.64f + rt * 0.77f) * 0.6f + up * 0.03f; break;
                default: pos = c + f * 0.62f + up * 0.03f; break;
            }
        }

        /// <summary>拍主角的验收图 → <paramref name="prefix"/>_{seat,front,up,low,L,R}.png（1600×1000）。拍的时候临时关掉沙盘和预览节点，拍完还原。</summary>
        /// <summary>玩家眼睛在封面本地的高度：LazyFollow 把封面原点放在眼睛下方 below 处 → 眼睛 = 原点 + below。</summary>
        static float EyeY(Transform cover) { var mc = cover.GetComponent<WarTable.Game.MenuCover>(); return mc ? mc.below : -0.04f; }

        /// <summary>从玩家机位量封面里每个东西的视角（水平 / 竖直，度；正 = 右 / 上），看左右是不是太开、上下是不是太偏。</summary>
        public static string Angles()
        {
            var cover = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == "Cover").transform;
            var eye = cover.TransformPoint(new Vector3(0f, EyeY(cover), 0.8f)); var sb = new System.Text.StringBuilder();
            string A(Vector3 w) { var d = cover.InverseTransformDirection(w - eye); return $"{Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg:+0.0;-0.0}°/{Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg:+0.0;-0.0}°"; }
            foreach (Transform c in cover)
            {
                if (!(c.name.StartsWith("Item_") || c.name.StartsWith("Title") || c.name.StartsWith("Credit") || c.name == "Hero")) continue;
                var rs = c.GetComponentsInChildren<Renderer>(false); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.Append($"{c.name} 中心 {A(b.center)} 左右边 {A(new Vector3(b.max.x, b.center.y, b.center.z))}…{A(new Vector3(b.min.x, b.center.y, b.center.z))} 上下 {A(new Vector3(b.center.x, b.max.y, b.center.z))}…{A(new Vector3(b.center.x, b.min.y, b.center.z))}\n");
            }
            return sb.ToString();
        }

        public static string Shoot(string prefix, string views = "seat,front,up,low,L,R")
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var hide = roots.Where(g => g.name == "PreviewOnly" || g.name == "WarTable_Board").ToList(); var was = hide.Select(g => g.activeSelf).ToList();
            var hr = HeroLod0(out var cover);
            try
            {
                foreach (var g in hide) g.SetActive(false);
                foreach (var k in views.Split(',')) { View(k, cover, hr.bounds, out var pos, out var look, out float fov); WTCapture.Shot(prefix + "_" + k + ".png", pos, look, fov); }
            }
            finally { for (int i = 0; i < hide.Count; i++) hide[i].SetActive(was[i]); }
            var m = hr.sharedMaterial;
            return "根 " + string.Join(" ", roots.Select(g => g.name + "=" + g.activeSelf)) + " | 关键字 " + string.Join(",", m.shaderKeywords) + " | 贴花 " + hr.transform.parent.GetComponentsInChildren<Renderer>(true).Count(r => r.name.StartsWith("Decal_"));
        }

        /// <summary>在某个验收机位的图上点像素（1600×1000 的坐标，成对给）→ 打到模型上的点（归一化坐标，填进 Specs 的 at）和那里的朝向。</summary>
        public static string Pick(string view, params float[] xy)
        {
            var hr = HeroLod0(out var cover); View(view, cover, hr.bounds, out var pos, out var look, out float fov);
            World(hr, out var v, out var n, out var t);
            var statue = hr.transform.parent; var lb = Box(statue, v); var rot = Quaternion.LookRotation(look - pos);
            float tan = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad); var sb = new System.Text.StringBuilder();
            for (int j = 0; j + 1 < xy.Length; j += 2)
            {
                var dir = (rot * new Vector3((xy[j] / 1600f * 2f - 1f) * tan * 1.6f, (1f - xy[j + 1] / 1000f * 2f) * tan, 1f)).normalized;
                float bt = float.MaxValue; int hit = -1;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a, pv = Vector3.Cross(dir, e2); float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-12f) continue;
                    var tv = pos - a; float u = Vector3.Dot(tv, pv) / det; if (u < 0f || u > 1f) continue;
                    var qv = Vector3.Cross(tv, e1); float w = Vector3.Dot(dir, qv) / det; if (w < 0f || u + w > 1f) continue;
                    float dist = Vector3.Dot(e2, qv) / det; if (dist > 0.001f && dist < bt) { bt = dist; hit = i; }
                }
                if (hit < 0) { sb.AppendLine($"({xy[j]}, {xy[j + 1]}) 没打到模型"); continue; }
                var lp = statue.InverseTransformPoint(pos + dir * bt); var nn = Vector3.Scale(lp - lb.min, new Vector3(1f / lb.size.x, 1f / lb.size.y, 1f / lb.size.z));
                var fn = Facing(pos + dir * bt, 0.012f, v, n, t);
                sb.AppendLine($"({xy[j]}, {xy[j + 1]}) → at = new Vector3({nn.x:F3}f, {nn.y:F3}f, {nn.z:F3}f)   朝向(世界) {fn.x:F2} {fn.y:F2} {fn.z:F2}   离相机 {bt:F3} m");
            }
            return sb.ToString();
        }
    }
}
