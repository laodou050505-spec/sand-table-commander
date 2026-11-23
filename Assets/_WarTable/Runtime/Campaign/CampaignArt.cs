using System.Collections.Generic;
using UnityEngine;

namespace WarTable.Campaign
{
    /// <summary>
    /// 战役界面的贴图件（11-19）：Resources/Campaign 下的图集（Tools/make_campaign_atlas.py）切成一块块面片。
    /// 面片都在本地 XY 平面上、中心为原点、高 = 1（宽按图块比例）；平铺在地图上的件再绕 X 转 90°。
    /// 白色的图块（军标、圈、手写数字、文字）运行时着色；材质用封面那套 WarTable/CoverUnlit（_Reveal 可以做“手画出来”）。
    /// </summary>
    public static class CampaignArt
    {
        [System.Serializable] class AtlasJson { public int w, h; public string[] names; public int[] rects; }

        static Texture2D atlas;
        static readonly Dictionary<string, Rect> uv = new();
        static readonly Dictionary<string, float> aspect = new();
        static readonly Dictionary<string, Mesh> meshes = new();
        static readonly Dictionary<(Texture, int), Material> mats = new();
        static MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), RevealId = Shader.PropertyToID("_Reveal");

        /// <summary>渲染顺序：纸 → 线 → 符号 → 字 → 立着的卡片（都在半透明队列里，按层级排，不靠距离）。</summary>
        public const int QPaper = 3000, QRoute = 3010, QInk = 3020, QTop = 3030, QCard = 3040;

        public static bool Ready => Load();

        static bool Load()
        {
            if (atlas) return true;
            atlas = Resources.Load<Texture2D>("Campaign/atlas");
            var js = Resources.Load<TextAsset>("Campaign/atlas");
            if (!atlas || !js) { Debug.LogWarning("[Campaign] 缺图集 Resources/Campaign/atlas"); return false; }
            var j = JsonUtility.FromJson<AtlasJson>(js.text);
            for (int i = 0; i < j.names.Length; i++)
            {
                int x = j.rects[i * 4], y = j.rects[i * 4 + 1], w = j.rects[i * 4 + 2], h = j.rects[i * 4 + 3];
                // PIL 的 y 向下、Unity 贴图的 v 向上；四周各收半个像素免得渗到旁边的图块
                uv[j.names[i]] = new Rect((x + 0.5f) / j.w, 1f - (y + h - 0.5f) / j.h, (w - 1f) / j.w, (h - 1f) / j.h);
                aspect[j.names[i]] = w / (float)h;
            }
            return true;
        }

        public static bool Has(string key) => Load() && uv.ContainsKey(key);
        public static float Aspect(string key) => Load() && aspect.TryGetValue(key, out var a) ? a : 1f;

        /// <summary>当前语言的文字图块键：L("heal") → "L_heal_zh"。</summary>
        public static string L(string k) => "L_" + k + (Loc.Current == Loc.Lang.Zh ? "_zh" : "_en");
        public static string D(string k) => "D_" + k + (Loc.Current == Loc.Lang.Zh ? "_zh" : "_en");

        public static Material Mat(Texture tex, int queue, bool repeat = false)
        {
            if (mats.TryGetValue((tex, queue), out var m) && m) return m;
            var sh = Shader.Find("WarTable/CoverUnlit");
            m = new Material(sh) { name = "Campaign_" + (tex ? tex.name : "white") + "_" + queue, renderQueue = queue };
            if (tex) { m.SetTexture("_BaseMap", tex); if (repeat) tex.wrapMode = TextureWrapMode.Repeat; }
            m.SetColor(ColorId, Color.white);
            mats[(tex, queue)] = m;
            return m;
        }

        public static Mesh QuadMesh(string key)
        {
            if (meshes.TryGetValue(key, out var m) && m) return m;
            Load();
            var r = uv.TryGetValue(key, out var rr) ? rr : new Rect(0, 0, 1, 1);
            float a = Aspect(key);
            m = new Mesh { name = "cq_" + key };
            m.vertices = new[] { new Vector3(-a / 2, -0.5f, 0), new Vector3(a / 2, -0.5f, 0), new Vector3(a / 2, 0.5f, 0), new Vector3(-a / 2, 0.5f, 0) };
            m.uv = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            meshes[key] = m;
            return m;
        }

        /// <summary>一块面片（立着，本地 XY）。height = 高（米），宽按图块比例。</summary>
        public static MeshRenderer Quad(Transform parent, string key, float height, Color color, int queue = QInk, string name = null)
        {
            var go = new GameObject(name ?? key);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * height;
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh(key);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Load() ? Mat(atlas, queue) : null;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            Tint(r, color);
            return r;
        }

        /// <summary>平铺在地图上的面片（本地 XZ 平面，文字顶端朝远处）。angle：绕竖直轴转（度，逆时针看下去）。</summary>
        public static MeshRenderer Flat(Transform parent, string key, float height, Color color, Vector3 pos, float angle = 0f, int queue = QInk)
        {
            var r = Quad(parent, key, height, color, queue);
            r.transform.localPosition = pos;
            r.transform.localRotation = Quaternion.AngleAxis(-angle, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            return r;
        }

        public static void Tint(Renderer r, Color c, float reveal = 1f)
        {
            if (!r) return;
            mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, c); mpb.SetFloat(RevealId, reveal); r.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// 一串手写数字（0–9 + − / ×），以 align 对齐（0 左、0.5 中、1 右），返回根节点。flat = 平铺在地图上。
        /// 每次内容变了就整串重建（字数很少）。
        /// </summary>
        public static Transform Number(Transform parent, string s, float height, Color color, bool flat, float align = 0.5f, int queue = QTop, string name = "num")
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            float x = 0f; var parts = new List<(Transform t, float w)>();
            foreach (char ch in s)
            {
                string k = ch switch { '+' => "d_plus", '-' => "d_minus", '/' => "d_slash", '×' => "d_times", _ => "d_" + ch };
                if (!Has(k)) { x += height * 0.3f; continue; }
                float w = Aspect(k) * height * 0.8f;   // 手写字留的边比较宽，挤一点
                var r = Quad(root, k, height, color, queue);
                parts.Add((r.transform, w));
                r.transform.localPosition = new Vector3(x + w / 2, 0, 0); x += w;
            }
            foreach (var (t, _) in parts) t.localPosition -= new Vector3(x * align, 0, 0);
            if (flat) root.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return root;
        }

        /// <summary>一串数字摆出来有多宽（和 Number 同一套算法），用来把“图标 + 数字”整组居中。</summary>
        public static float NumberWidth(string s, float height)
        {
            float x = 0f;
            foreach (char ch in s)
            {
                string k = ch switch { '+' => "d_plus", '-' => "d_minus", '/' => "d_slash", '×' => "d_times", _ => "d_" + ch };
                x += Has(k) ? Aspect(k) * height * 0.8f : height * 0.3f;
            }
            return x;
        }

        /// <summary>沿一条折线拉一根带子（本地 XZ 平面，略高于纸面），uv.x 按 period 米平铺。</summary>
        public static Mesh Strip(Mesh m, IList<Vector3> pts, float width, float period)
        {
            m ??= new Mesh { name = "route" };
            m.Clear();
            int n = pts.Count; if (n < 2) return m;
            var v = new Vector3[n * 2]; var u = new Vector2[n * 2]; var tri = new List<int>();
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                var d = (i < n - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]); d.y = 0; d.Normalize();
                var side = new Vector3(-d.z, 0, d.x) * (width / 2);
                if (i > 0) acc += Vector3.Distance(pts[i], pts[i - 1]);
                v[i * 2] = pts[i] - side; v[i * 2 + 1] = pts[i] + side;
                u[i * 2] = new Vector2(acc / period, 0); u[i * 2 + 1] = new Vector2(acc / period, 1);
                if (i < n - 1) tri.AddRange(new[] { i * 2, i * 2 + 1, i * 2 + 3, i * 2, i * 2 + 3, i * 2 + 2 });
            }
            m.vertices = v; m.uv = u; m.triangles = tri.ToArray(); m.RecalculateBounds();
            return m;
        }

        public static Texture2D Tex(string name) => Resources.Load<Texture2D>("Campaign/" + name);
    }
}
