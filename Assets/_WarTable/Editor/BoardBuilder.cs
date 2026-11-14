using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 程序化搭沙盘本体：地台地形、河水、木框、铜包角；预览用的书桌和灯光放在 PreviewOnly 下（真机透视时不显示）。
    /// 菜单 WarTable/1 搭建沙盘。所有尺寸是真实米：沙盘外框 0.9 m，可玩区 0.8 m（1 码 = 1 cm）。
    /// </summary>
    public static class BoardBuilder
    {
        public const string Root = "WarTable_Board";
        public const string Preview = "PreviewOnly";
        const string ScenePath = "Assets/_WarTable/Scenes/Board.unity";
        const string MeshDir = "Assets/_WarTable/Art/Generated";
        const string MatDir = "Assets/_WarTable/Art/Materials";

        /// <summary>木框：内沿在可玩区边上，宽 5 cm；地台基面 y = 0，桌面在 y = −4.5 cm。</summary>
        public const float InnerX = BoardMap.HalfX * BoardMap.Yard, InnerZ = BoardMap.HalfZ * BoardMap.Yard, FrameW = 0.05f, TableY = -0.045f;

        [MenuItem("WarTable/1 搭建沙盘")]
        public static void BuildMenu() => Build();

        public static GameObject Build()
        {
            AssetDatabase.Refresh();   // Tools/make_tileable.py 刚写进来的贴图
            EnsureScene();
            foreach (var n in new[] { Root, Preview, "Sun" })
            {
                var old = GameObject.Find(n);
                if (old) Object.DestroyImmediate(old);
            }
            System.IO.Directory.CreateDirectory(MeshDir);
            BoardMap.Pads = PropBuilder.ComputePads();
            var root = new GameObject(Root);
            root.AddComponent<BoardLook>().Apply();
            // 整张沙盘按 BoardScale 放大：下面所有东西都按本地坐标（1 码 = 1 cm）搭
            root.transform.localScale = Vector3.one * BoardMap.BoardScale;
            BuildTerrain(root.transform);
            BuildWater(root.transform);
            BuildFrame(root.transform);
            ScatterBuilder.Build(root.transform);
            BuildPreview();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return root;
        }

        static void EnsureScene()
        {
            var s = EditorSceneManager.GetActiveScene();
            if (s.path == ScenePath) return;
            if (System.IO.File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            System.IO.Directory.CreateDirectory("Assets/_WarTable/Scenes");
            var ns = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(ns, ScenePath);
        }

        // ---------------------------------------------------------------- 材质

        public static Material Mat(string name, string shader, System.Action<Material> setup = null)
        {
            System.IO.Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader.name != shader) m.shader = Shader.Find(shader);
            setup?.Invoke(m);
            if (shader == "WarTable/MiniLit") Look(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 统一的材质规矩（用户：模型不要像蒙了一层雾、反光不要太强）：关边缘光；普通材质高光上限 0.05，
        /// 金属（铜、金、铁）保留一点但收窄；暗部压深一点（不泛灰）。在每个材质创建 / 更新的最后执行，重建时不会被覆盖回去。
        /// </summary>
        public static void Look(Material m)
        {
            bool metal = m.name.Contains("Brass") || m.name.Contains("Gold") || m.name.Contains("Iron");
            m.SetFloat("_RimStrength", 0f);
            m.SetFloat("_Gloss", Mathf.Min(m.GetFloat("_Gloss"), metal ? 0.4f : 0.05f));
            m.SetFloat("_GlossSize", metal ? 56f : 40f);
            m.SetColor("_ShadowColor", new Color(0.34f, 0.35f, 0.42f));
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            string path = $"{MeshDir}/{name}.asset";
            // CopySerialized 覆盖到旧网格资产上不生效（形状一直是旧的），直接删掉重建
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Obj(string name, Transform parent, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        // ---------------------------------------------------------------- 地形

        const int N = 180;   // 每格约 5.6 mm（起伏都很缓，草地细节靠贴图；240 格时地形一张就 14 万面）

        static void BuildTerrain(Transform root)
        {
            float HX = BoardMap.HalfX, HZ = BoardMap.HalfZ, Y = BoardMap.Yard;
            int NX = Mathf.RoundToInt(N * HX / HZ), NZ = N;
            var v = new Vector3[(NX + 1) * (NZ + 1)];
            var nrm = new Vector3[v.Length];
            var col = new Color[v.Length];
            float step = 2 * HZ / NZ, e = step * 0.5f;
            for (int j = 0; j <= NZ; j++)
                for (int i = 0; i <= NX; i++)
                {
                    float x = -HX + i * step, z = -HZ + j * step;
                    int k = j * (NX + 1) + i;
                    v[k] = new Vector3(x, BoardMap.Height(x, z), z) * Y;
                    float hx = BoardMap.Height(x + e, z) - BoardMap.Height(x - e, z), hz = BoardMap.Height(x, z + e) - BoardMap.Height(x, z - e);
                    nrm[k] = new Vector3(-hx, 2 * e, -hz).normalized;
                    col[k] = BoardMap.Weights(x, z, nrm[k]);
                }
            var tri = new int[NX * NZ * 6];
            int t = 0;
            for (int j = 0; j < NZ; j++)
                for (int i = 0; i < NX; i++)
                {
                    int a = j * (NX + 1) + i, b = a + 1, c = a + NX + 1, d = c + 1;
                    tri[t++] = a; tri[t++] = c; tri[t++] = b;
                    tri[t++] = b; tri[t++] = c; tri[t++] = d;
                }
            var m = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32, vertices = v, normals = nrm, colors = col, triangles = tri };
            m.RecalculateBounds();
            var mat = Mat("M_Terrain", "WarTable/TerrainSplat", mm =>
            {
                // 草地用细颗粒草皮（grass_turf / dry_turf）：用户嫌长草丝的静电草贴图“毛茸茸”；没有新图时退回旧图
                string grass = Tex("grass_turf") ? "grass_turf" : "grass_flock", dryT = Tex("dry_turf") ? "dry_turf" : "dry_grass_flock";
                foreach (var (prop, tex, tint) in new[] { ("_GrassTex", grass, "_GrassColor"), ("_DryTex", dryT, "_DryColor"), ("_DirtTex", "dirt_path", "_DirtColor"), ("_RockTex", "rock_face", "_RockColor"), ("_SandTex", "river_bed", "_SandColor") })
                {
                    var t = Tex(tex);
                    mm.SetTexture(prop, t);
                    // 有照片贴图时颜色只做轻微校色（照片本身已经是对的颜色），没有贴图时用纯色
                    if (t) mm.SetColor(tint, new Color(1f, 1f, 1f));
                }
                if (grass == "grass_turf") { mm.SetColor("_GrassColor", new Color(0.66f, 0.74f, 0.56f)); mm.SetColor("_GrassColor2", new Color(0.84f, 0.8f, 0.56f)); }
                else if (Tex("grass_flock")) { mm.SetColor("_GrassColor", new Color(0.92f, 0.95f, 0.85f)); mm.SetColor("_GrassColor2", new Color(1.08f, 1.0f, 0.72f)); }
                // 干草皮照片偏沙黄：压一点饱和、偏橄榄
                mm.SetColor("_DryColor", dryT == "dry_turf" ? new Color(0.9f, 0.9f, 0.72f) : new Color(1f, 0.93f, 0.8f));
                mm.SetFloat("_Tile", 7f);
                mm.SetFloat("_ShadeSoft", 0.9f);
            });
            Obj("Terrain", root, SaveMesh(m, "Terrain"), mat, true);
        }

        // ---------------------------------------------------------------- 河水

        static void BuildWater(Transform root)
        {
            float H = BoardMap.HalfZ, Y = BoardMap.Yard, w = BoardMap.RiverHalfWidth * 1.75f;
            const int L = 120, W = 14;
            var v = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>();
            for (int j = 0; j <= L; j++)
            {
                float z = -H + 2 * H * j / L;
                float cx = BoardMap.RiverX(z);
                for (int i = 0; i <= W; i++)
                {
                    float x = cx - w + 2 * w * i / W;
                    float depth = Mathf.Clamp01((BoardMap.WaterLevel - BoardMap.Height(x, z)) / (BoardMap.WaterLevel - BoardMap.RiverBed));
                    v.Add(new Vector3(x, BoardMap.WaterLevel, z) * Y);
                    c.Add(new Color(1, 1, 1, depth));
                }
            }
            for (int j = 0; j < L; j++)
                for (int i = 0; i < W; i++)
                {
                    int a = j * (W + 1) + i, b = a + 1, cc = a + W + 1, d = cc + 1;
                    tri.AddRange(new[] { a, cc, b, b, cc, d });
                }
            var m = new Mesh { name = "River" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            var mat = Mat("M_RiverWater", "WarTable/ResinWater");
            Obj("RiverWater", root, SaveMesh(m, "River"), mat, false);
        }

        // ---------------------------------------------------------------- 木框

        /// <summary>框的断面（u 从内沿往外，v 高度），true = 硬边（拆顶点）。</summary>
        static readonly (float u, float v, bool hard)[] Profile =
        {
            (0f, -0.026f, true), (0f, 0.006f, true),
            (0.0012f, 0.0082f, false), (0.0035f, 0.0094f, false), (0.006f, 0.0086f, true),
            (0.0075f, 0.0088f, true), (0.0075f, 0.0118f, true), (0.031f, 0.0118f, true),
            (0.0345f, 0.0106f, false), (0.039f, 0.0078f, false), (0.0435f, 0.0062f, true),
            (0.05f, 0.0058f, true), (0.05f, -0.0035f, true), (0.05f, -0.0315f, true), (0.05f, TableY + 0.004f, true),
            (0.0525f, TableY + 0.0028f, false), (0.0525f, TableY, true),
        };

        static void BuildFrame(Transform root)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var band = new List<int>();
            // 四条边：outward 方向与沿边方向
            // (外法线, 沿边方向, 这条边离中心的距离, 半边长)
            var sides = new[] { (Vector3.forward, Vector3.right, InnerZ, InnerX), (Vector3.right, Vector3.back, InnerX, InnerZ), (Vector3.back, Vector3.left, InnerZ, InnerX), (Vector3.left, Vector3.forward, InnerX, InnerZ) };
            foreach (var (outw, along, dist, halfLen) in sides)
            {
                // 每个断面点一行（硬边处两行），每行两个端点（45° 斜接）
                float acc = 0f;
                int prevRow = -1;
                for (int p = 0; p < Profile.Length; p++)
                {
                    var (u, h, hard) = Profile[p];
                    if (p > 0) acc += new Vector2(u - Profile[p - 1].u, h - Profile[p - 1].v).magnitude;
                    int copies = hard && p > 0 && p < Profile.Length - 1 ? 2 : 1;
                    for (int c = 0; c < copies; c++)
                    {
                        float r = dist + u, l = halfLen + u;
                        int row = v.Count;
                        v.Add(outw * r - along * l + Vector3.up * h);
                        v.Add(outw * r + along * l + Vector3.up * h);
                        // 雕花带：单独一组 UV，雕花贴图 4:1，带高 2.8 cm → 每 11.2 cm 重复一次
                        bool inBand = (p == BandA && c == 1) || (p == BandB && c == 0);
                        float bv = p == BandA ? 1f : 0f;
                        uv.Add(inBand ? new Vector2(-l / 0.112f, bv) : new Vector2(-l * 8f, acc * 8f));
                        uv.Add(inBand ? new Vector2(l / 0.112f, bv) : new Vector2(l * 8f, acc * 8f));
                        if (c == 0 && prevRow >= 0)
                        {
                            int a = prevRow, b = prevRow + 1, cc = row, d = row + 1;
                            (p == BandB ? band : tri).AddRange(new[] { a, b, cc, b, d, cc });
                        }
                        prevRow = row;
                    }
                }
            }
            // 底板
            int s = v.Count;
            float RX = InnerX + 0.0525f, RZ = InnerZ + 0.0525f;
            v.AddRange(new[] { new Vector3(-RX, TableY, -RZ), new Vector3(RX, TableY, -RZ), new Vector3(RX, TableY, RZ), new Vector3(-RX, TableY, RZ) });
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            tri.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            var m = new Mesh { name = "Frame" };
            m.subMeshCount = 2;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.SetTriangles(band, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            FixWinding(m);
            var wood = Mat("M_FrameWalnut", "WarTable/MiniLit", mm =>
            {
                var t = Tex("walnut_wood");
                mm.SetTexture("_BaseMap", t);
                mm.SetColor("_BaseColor", t ? new Color(1.12f, 1.14f, 1.2f) : new Color(0.36f, 0.22f, 0.14f));
                mm.SetFloat("_Gloss", 0.35f); mm.SetFloat("_GlossSize", 48f);
            });
            var carve = Mat("M_FrameCarving", "WarTable/MiniLit", mm =>
            {
                var t = Tex("carved_trim", false);
                mm.SetTexture("_BaseMap", t);
                mm.SetColor("_BaseColor", t ? new Color(1.15f, 1.12f, 1.12f) : new Color(0.32f, 0.2f, 0.13f));
                mm.SetFloat("_Gloss", 0.3f); mm.SetFloat("_GlossSize", 40f);
            });
            var fr = Obj("Frame", root, SaveMesh(m, "Frame"), wood, true);
            fr.GetComponent<MeshRenderer>().sharedMaterials = new[] { wood, carve };

            // 铜包角：外角两块竖板 + 顶面一块角片
            var brass = Mat("M_Brass", "WarTable/MiniLit", mm =>
            {
                mm.SetColor("_BaseColor", new Color(0.78f, 0.6f, 0.3f));
                mm.SetFloat("_Gloss", 0.8f); mm.SetFloat("_GlossSize", 64f); mm.SetFloat("_RimStrength", 0.35f);
            });
            var corners = new GameObject("BrassCorners").transform;
            corners.SetParent(root, false);
            float OX = InnerX + 0.05f, OZ = InnerZ + 0.05f, plateH = 0.05f - 0.004f, len = 0.045f, th = 0.0012f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float y0 = TableY + 0.0045f + plateH * 0.5f;
                    Box(corners, brass, new Vector3(sx * (OX + th - len * 0.5f), y0, sz * (OZ + th * 0.5f)), new Vector3(len, plateH, th));
                    Box(corners, brass, new Vector3(sx * (OX + th * 0.5f), y0, sz * (OZ + th - len * 0.5f)), new Vector3(th, plateH, len));
                    Box(corners, brass, new Vector3(sx * (OX - 0.012f), 0.0058f + 0.0006f, sz * (OZ - 0.012f)), new Vector3(0.026f, 0.0012f, 0.026f));
                }
        }

        /// <summary>雕花带在外侧立面上（断面第 12→13 点，高 2.8 cm）：坐着的玩家正好看得见近侧这条边。</summary>
        const int BandA = 12, BandB = 13;

        /// <summary>材质贴图：Art/Textures/&lt;名&gt;.png（由 Tools/make_tileable.py 生成）；没有就返回 null（用纯色兜底）。</summary>
        public static Texture2D Tex(string name, bool repeat = true)
        {
            string path = $"Assets/_WarTable/Art/Textures/{name}.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!imp) return null;
            bool dirty = false;
            if (imp.maxTextureSize != 1024) { imp.maxTextureSize = 1024; dirty = true; }
            if (imp.mipmapEnabled != true) { imp.mipmapEnabled = true; dirty = true; }
            if (imp.anisoLevel != 4) { imp.anisoLevel = 4; dirty = true; }
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Repeat;
            if (imp.wrapMode != wrap) { imp.wrapMode = wrap; dirty = true; }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Box(Transform parent, Material mat, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>按“法线应当背离框中心竖轴 / 顶面朝上”统一绕序（断面方向一换就容易整片反面）。</summary>
        static void FixWinding(Mesh m)
        {
            var v = m.vertices; var t = m.triangles;     // 全部子网格，只用来投票
            int flipped = 0, total = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 ctr = (a + b + c) / 3f;
                Vector3 radial = new Vector3(ctr.x, 0, ctr.z);
                float dot = Vector3.Dot(n, radial.normalized) + n.y * 0.5f;
                total++;
                if (dot < 0) flipped++;
            }
            // 内壁的正确法线朝里，用多数票决定整体是否反了
            if (flipped * 2 > total)
            {
                // 按子网格分别翻（直接写 m.triangles 会把所有子网格合成一个）
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    var st = m.GetTriangles(sm);
                    for (int i = 0; i < st.Length; i += 3) (st[i + 1], st[i + 2]) = (st[i + 2], st[i + 1]);
                    m.SetTriangles(st, sm);
                }
                m.RecalculateNormals();
            }
        }

        // ---------------------------------------------------------------- 预览（不进真机）

        static void BuildPreview()
        {
            var p = new GameObject(Preview);
            p.tag = "EditorOnly";
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(desk.GetComponent<Collider>());
            desk.name = "Desk";
            desk.transform.SetParent(p.transform, false);
            desk.transform.localPosition = new Vector3(0, TableY * BoardMap.BoardScale - 0.02f, 0.12f);
            desk.transform.localScale = new Vector3(2.3f, 0.04f, 1.75f);
            desk.GetComponent<MeshRenderer>().sharedMaterial = Mat("M_PreviewDesk", "WarTable/MiniLit", mm =>
            {
                mm.SetColor("_BaseColor", new Color(0.5f, 0.42f, 0.34f)); mm.SetFloat("_Gloss", 0.05f);
            });
            // 主光是游戏的一部分（不能放在 EditorOnly 的预览节点下，否则打包后头显里没有主光）；
            // 放在场景顶层，不挂沙盘（封面阶段沙盘藏起来时兵符也要有光），TrayRig 摆沙盘时跟着沙盘转
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.shadowBias = 0.02f; sun.shadowNormalBias = 0.2f;
            // 光从玩家身后左上方照过来：朝向玩家的棋子正面是亮的
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-0.45f, -0.78f, -0.55f));
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.27f, 0.22f);
        }
    }
}
