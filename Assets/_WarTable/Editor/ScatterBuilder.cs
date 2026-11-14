using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 地台上的细碎布景（程序化）：静电草丛、野花点、麦田、篱笆。
    /// 都是微缩模型地台上常见的东西，P2 做不了这么细的毛；按 4×4 分块合并成网格（远处可整块剔除）。
    /// </summary>
    public static class ScatterBuilder
    {
        public static void Build(Transform root)
        {
            var parent = new GameObject("Scatter").transform;
            parent.SetParent(root, false);
            var grassMat = BoardBuilder.Mat("M_Tufts", "WarTable/MiniLit", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetFloat("_Cull", 0); m.SetFloat("_Gloss", 0.05f);
                m.SetFloat("_Ambient", 0.5f); m.SetFloat("_RimStrength", 0f); m.SetFloat("_Gloss", 0f); m.SetFloat("_ShadeSoft", 0.8f);
            });
            var rnd = new System.Random(20261030);
            var chunks = new Dictionary<int, MeshData>();
            MeshData Chunk(float x, float z)
            {
                int cx = Mathf.Clamp((int)((x + BoardMap.HalfX) / (BoardMap.HalfX * 0.5f)), 0, 3), cz = Mathf.Clamp((int)((z + BoardMap.HalfZ) / (BoardMap.HalfZ * 0.5f)), 0, 3);
                int key = cz * 4 + cx;
                if (!chunks.TryGetValue(key, out var md)) chunks[key] = md = new MeshData();
                return md;
            }
            float R() => (float)rnd.NextDouble();

            // 草簇：用户嫌满地草簇“毛茸茸”——改成稀疏、矮、成小团的点缀，主要长在布景件脚边、篱笆和木框边上，
            // 战场中央几乎不放；布景件占地里面不长（不穿模）
            var feet = PropBuilder.Footprints();
            bool UnderProp(float x, float z, float pad)
            {
                foreach (var f in feet) if ((new Vector2(x, z) - f.c).sqrMagnitude < (f.r + pad) * (f.r + pad)) return true;
                return false;
            }
            int tufts = 0;
            void TryTuft(float x, float z, float scale)
            {
                if (Mathf.Abs(x) > BoardMap.HalfX - 0.8f || Mathf.Abs(z) > BoardMap.HalfZ - 0.8f) return;
                Vector3 n = Normal(x, z);
                Color w = BoardMap.Weights(x, z, n);
                if (w.g > 0.2f || w.a > 0.15f || w.b > 0.45f || InField(x, z) || UnderProp(x, z, 0.25f)) return;
                float dry = Mathf.Clamp01(w.r * 1.3f + (R() < 0.15f ? 0.5f : 0f));
                var baseCol = Color.Lerp(new Color(0.12f, 0.17f, 0.06f), new Color(0.32f, 0.26f, 0.12f), dry);
                var tipCol = Color.Lerp(new Color(0.40f, 0.48f, 0.18f), new Color(0.70f, 0.60f, 0.32f), dry);
                float h = Mathf.Lerp(0.0022f, 0.0040f, R()) * scale;
                Color? flower = R() < 0.08f && dry < 0.5f ? FlowerColors[rnd.Next(FlowerColors.Length)] : null;
                Tuft(Chunk(x, z), Pos(x, z), h, 12 + rnd.Next(9), baseCol, tipCol, flower, rnd);
                tufts++;
            }
            // 1) 布景件脚边：每件 2~5 小团，贴着占地外沿
            foreach (var f in feet)
            {
                int k = 2 + rnd.Next(4);
                for (int i = 0; i < k; i++)
                {
                    float a2 = R() * Mathf.PI * 2, rr = f.r + 0.35f + R() * 1.2f;
                    float cx = f.c.x + Mathf.Cos(a2) * rr, cz = f.c.y + Mathf.Sin(a2) * rr;
                    int m = 1 + rnd.Next(3);     // 一小团 1~3 簇
                    for (int q = 0; q < m; q++) TryTuft(cx + (R() - 0.5f) * 0.9f, cz + (R() - 0.5f) * 0.9f, 1f);
                }
            }
            // 2) 麦田篱笆外、木框内沿
            foreach (var f in Fields)
                for (int i = 0; i < 10; i++)
                {
                    float u = (R() - 0.5f) * (f.z + 1.6f), side = R() < 0.5f ? -1 : 1;
                    float lx = u, lz = side * (f.z * 0.4f + 1.1f), c = Mathf.Cos(f.w), s2 = Mathf.Sin(f.w);
                    TryTuft(f.x + lx * c - lz * s2, f.y + lx * s2 + lz * c, 1.1f);
                }
            for (int i = 0; i < 260; i++)
            {
                float t = R() * 4f; int e = (int)t; float u = (t - e) * 2 - 1;
                float x = e < 2 ? u * BoardMap.HalfX : (e == 2 ? 1 : -1) * (BoardMap.HalfX - 1.2f - R() * 1.5f);
                float z = e < 2 ? (e == 0 ? 1 : -1) * (BoardMap.HalfZ - 1.2f - R() * 1.5f) : u * BoardMap.HalfZ;
                TryTuft(x, z, 0.9f);
            }
            // 3) 零星散布：布景带多一点，战场里很少
            for (int i = 0; i < 9000 && tufts < 1500; i++)
            {
                float x = (R() * 2 - 1) * BoardMap.HalfX, z = (R() * 2 - 1) * BoardMap.HalfZ;
                bool inField = Mathf.Abs(x) < BoardMap.Half && Mathf.Abs(z) < BoardMap.Half;
                if (R() > (inField ? 0.02f : 0.12f) * BoardMap.Fbm(x * 0.12f + 5, z * 0.12f + 9) * 2f) continue;
                TryTuft(x, z, inField ? 0.8f : 1f);
            }

            // 河边芦苇：沿两岸水线成丛，比草簇高、叶子更直，顶上少量深褐色蒲棒
            int reeds = 0;
            for (float z = -BoardMap.HalfZ + 1f; z < BoardMap.HalfZ - 1f; z += 0.9f)
                for (int side = -1; side <= 1; side += 2)
                {
                    if (R() < 0.35f || Mathf.Abs(z - BoardMap.BridgeZ) < 3.5f) continue;   // 桥两头留空
                    float x = BoardMap.RiverX(z) + side * (BoardMap.RiverHalfWidth * (0.95f + R() * 0.45f));
                    var col0 = new Color(0.20f, 0.26f, 0.10f); var col1 = new Color(0.56f, 0.58f, 0.30f);
                    Reed(Chunk(x, z), Pos(x, z), 0.008f + R() * 0.006f, 10 + rnd.Next(8), col0, col1, rnd);
                    reeds++;
                }

            int stones = 0;   // 路边小石子：近看成了黑色小方块，先去掉（路面贴图里已经有石子）

            // 麦田：成行的麦秆，顶上是麦穗
            foreach (var f in Fields)
            {
                var md = Chunk(f.x, f.y);
                float c = Mathf.Cos(f.w), s = Mathf.Sin(f.w);
                for (float u = -f.z * 0.5f + 0.3f; u < f.z * 0.5f; u += 0.36f)          // 行
                    for (float v = -f.z * 0.4f + 0.2f; v < f.z * 0.4f; v += 0.2f)    // 行内
                    {
                        float lx = u + (R() - 0.5f) * 0.18f, lz = v + (R() - 0.5f) * 0.2f;
                        float x = f.x + lx * c - lz * s, z = f.y + lx * s + lz * c;
                        if (UnderProp(x, z, 0.2f)) continue;
                        float h = 0.0085f + R() * 0.002f;
                        Wheat(md, Pos(x, z), h, rnd);
                    }
                Fence(md, f, rnd);
            }

            foreach (var kv in chunks)
            {
                var mesh = kv.Value.ToMesh($"Scatter_{kv.Key}");
                var go = new GameObject($"Scatter_{kv.Key}");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = grassMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            Debug.Log($"[Scatter] tufts={tufts} reeds={reeds} stones={stones} chunks={chunks.Count}");
        }

        /// <summary>
        /// 未来时代的地面散布（11-12 用户：“注意别把场景做得太空”）：草丛、芦苇、麦田在未来时代都藏起来了，换上——
        /// 月面碎石（布景脚边、木框内沿、河岸成小堆，战场里零星几粒）、小水晶芽（光棱半场冰蓝、蜂群半场紫）、
        /// 麦田的位置铺成一排排太阳能板 + 一圈矮灯桩。单独一组 "ScatterFuture"（挂 EraOnly，只在未来时代出现）。
        /// 由 PropBuilder.Build 末尾调用（要先有布景占地）。
        /// </summary>
        public static void BuildFuture(Transform root)
        {
            var old = root.Find("ScatterFuture");
            if (old) Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject("ScatterFuture").transform;
            parent.SetParent(root, false);
            var mat = BoardBuilder.Mat("M_FutureScatter", "WarTable/MiniLit", m => { m.SetFloat("_UseVertexColor", 1); m.SetFloat("_Cull", 0); });
            mat.SetFloat("_Gloss", 0.3f); mat.SetFloat("_GlossSize", 40f); mat.SetFloat("_Ambient", 0.62f); mat.SetFloat("_RimStrength", 0f); EditorUtility.SetDirty(mat);
            // 会发光的小件（水晶芽、路边地灯、灯桩的灯头）单独一套网格和材质：带一点自发光，暗处也看得见
            var glowMat = BoardBuilder.Mat("M_FutureGlow", "WarTable/MiniLit", m => { m.SetFloat("_UseVertexColor", 1); m.SetFloat("_Cull", 0); });
            glowMat.SetFloat("_Gloss", 0.45f); glowMat.SetFloat("_GlossSize", 50f); glowMat.SetFloat("_Ambient", 0.7f); glowMat.SetColor("_EmissionColor", new Color(0.1f, 0.13f, 0.19f)); EditorUtility.SetDirty(glowMat);
            var rnd = new System.Random(20261112);
            float R() => (float)rnd.NextDouble();
            var chunks = new Dictionary<int, MeshData>();
            MeshData Chunk(float x, float z, bool glow = false)
            {
                int cx = Mathf.Clamp((int)((x + BoardMap.HalfX) / (BoardMap.HalfX * 0.5f)), 0, 3), cz = Mathf.Clamp((int)((z + BoardMap.HalfZ) / (BoardMap.HalfZ * 0.5f)), 0, 3);
                int key = cz * 4 + cx + (glow ? 100 : 0);
                if (!chunks.TryGetValue(key, out var md)) chunks[key] = md = new MeshData();
                return md;
            }
            var feet = PropBuilder.Footprints();
            bool Free(float x, float z, float pad)
            {
                if (Mathf.Abs(x) > BoardMap.HalfX - 0.9f || Mathf.Abs(z) > BoardMap.HalfZ - 0.9f) return false;
                if (InField(x, z) || BoardMap.RoadDistance(x, z) < BoardMap.RoadHalfWidth * 1.15f || BoardMap.RiverDistance(x, z) < BoardMap.RiverHalfWidth * 1.08f) return false;
                foreach (var f in feet) if ((new Vector2(x, z) - f.c).sqrMagnitude < (f.r + pad) * (f.r + pad)) return false;
                return true;
            }
            int rocks = 0, shards = 0, tiles = 0;
            void Rocks(float x, float z, float size)
            {
                int k = 1 + rnd.Next(3);
                for (int i = 0; i < k; i++)
                {
                    float px = x + (R() - 0.5f) * 1.1f, pz = z + (R() - 0.5f) * 1.1f;
                    if (!Free(px, pz, 0.3f)) continue;
                    MoonRock(Chunk(px, pz), Pos(px, pz), (0.16f + 0.3f * R() * R()) * size * BoardMap.Yard * (i == 0 ? 1.5f : 1f), rnd);
                    rocks++;
                }
            }
            void Shards(float x, float z, float size)
            {
                if (!Free(x, z, 0.35f)) return;
                Shard(Chunk(x, z, true), Pos(x, z), (0.45f + 0.5f * R()) * size * BoardMap.Yard, z < -15f, rnd);
                shards++;
            }
            // 1) 布景脚边：每件 2~4 小堆碎石，四成带一丛水晶芽
            foreach (var f in feet)
            {
                int k = 2 + rnd.Next(3);
                for (int i = 0; i < k; i++)
                {
                    float a = R() * Mathf.PI * 2, rr = f.r + 0.4f + R() * 1.3f;
                    Rocks(f.c.x + Mathf.Cos(a) * rr, f.c.y + Mathf.Sin(a) * rr, 1.1f);
                }
                if (R() < 0.4f) { float a = R() * Mathf.PI * 2, rr = f.r + 0.5f + R(); Shards(f.c.x + Mathf.Cos(a) * rr, f.c.y + Mathf.Sin(a) * rr, 1f); }
            }
            // 2) 木框内沿一圈
            for (int i = 0; i < 300; i++)
            {
                float t = R() * 4f; int e = (int)t; float u = (t - e) * 2 - 1;
                float x = e < 2 ? u * BoardMap.HalfX : (e == 2 ? 1 : -1) * (BoardMap.HalfX - 1.2f - R() * 2.2f);
                float z = e < 2 ? (e == 0 ? 1 : -1) * (BoardMap.HalfZ - 1.2f - R() * 2.2f) : u * BoardMap.HalfZ;
                if (R() < 0.72f) Rocks(x, z, 1.25f); else Shards(x, z, 1.1f);
            }
            // 3) 河两岸
            for (float z = -BoardMap.HalfZ + 1f; z < BoardMap.HalfZ - 1f; z += 1.1f)
                for (int side = -1; side <= 1; side += 2)
                {
                    if (R() < 0.3f || Mathf.Abs(z - BoardMap.BridgeZ) < 3.5f) continue;
                    float x = BoardMap.RiverX(z) + side * (BoardMap.RiverHalfWidth * (1.12f + R() * 0.5f));
                    if (R() < 0.7f) Rocks(x, z, 1.2f); else Shards(x, z, 1.25f);
                }
            // 4) 零星散布：布景带多一点，战场里少而小（棋子从上面走过去不显眼）
            for (int i = 0; i < 12000 && rocks < 1500; i++)
            {
                float x = (R() * 2 - 1) * BoardMap.HalfX, z = (R() * 2 - 1) * BoardMap.HalfZ;
                bool inField = Mathf.Abs(x) < BoardMap.Half && Mathf.Abs(z) < BoardMap.Half;
                if (R() > (inField ? 0.05f : 0.16f) * BoardMap.Fbm(x * 0.12f + 5, z * 0.12f + 9) * 2f) continue;
                if (R() < 0.85f) Rocks(x, z, inField ? 0.7f : 1.1f); else Shards(x, z, inField ? 0.6f : 1f);
            }
            // 5) 麦田的位置：一排排太阳能板 + 一圈矮灯桩
            foreach (var f in Fields)
            {
                var md = Chunk(f.x, f.y);
                float c = Mathf.Cos(f.w), s = Mathf.Sin(f.w);
                for (float u = -f.z * 0.5f + 0.55f; u < f.z * 0.5f - 0.2f; u += 0.95f)
                    for (float v = -f.z * 0.4f + 0.5f; v < f.z * 0.4f - 0.2f; v += 1.0f)
                    {
                        float x = f.x + u * c - v * s, z = f.y + u * s + v * c;
                        bool under = false; foreach (var ft in feet) if ((new Vector2(x, z) - ft.c).sqrMagnitude < (ft.r + 0.3f) * (ft.r + 0.3f)) under = true;
                        if (under) continue;
                        SolarTile(md, Pos(x, z), f.w, rnd); tiles++;
                    }
                LightPosts(md, Chunk(f.x, f.y, true), f);
            }
            // 6) 路边地灯：沿路两侧每隔两码半一盏小灯（光棱半场天青、蜂群半场紫），大片空地里有了引导线和光点
            int studs = 0;
            var placed = new List<Vector2>();
            for (float z = -BoardMap.HalfZ + 1.2f; z < BoardMap.HalfZ - 1.2f; z += 0.25f)
                for (float x = -BoardMap.HalfX + 1.2f; x < BoardMap.HalfX - 1.2f; x += 0.25f)
                {
                    float d = BoardMap.RoadDistance(x, z);
                    if (Mathf.Abs(d - BoardMap.RoadHalfWidth * 1.12f) > 0.13f) continue;
                    if (BoardMap.RiverDistance(x, z) < BoardMap.RiverHalfWidth * 1.3f) continue;
                    var q = new Vector2(x, z); bool near = false;
                    foreach (var o in placed) if ((o - q).sqrMagnitude < 2.5f * 2.5f) { near = true; break; }
                    if (near) continue;
                    bool under = false; foreach (var ft in feet) if ((q - ft.c).sqrMagnitude < (ft.r + 0.4f) * (ft.r + 0.4f)) under = true;
                    if (under) continue;
                    placed.Add(q);
                    var p = Pos(x, z);
                    Bar(Chunk(x, z), p - Vector3.up * 0.0006f, p + Vector3.up * 0.0011f, 0.0007f, PanelFrame * 0.9f);
                    Bar(Chunk(x, z, true), p + Vector3.up * 0.0011f, p + Vector3.up * 0.0019f, 0.00055f, z < -15f ? new Color(0.72f, 0.45f, 1f) : PanelGlow);
                    studs++;
                }
            foreach (var kv in chunks)
            {
                var mesh = kv.Value.ToMesh($"ScatterFuture_{kv.Key}");
                var go = new GameObject($"ScatterFuture_{kv.Key}");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key >= 100 ? glowMat : mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            { var seo = parent.gameObject.AddComponent<WarTable.Game.EraOnly>(); seo.era = 3; seo.alsoIn = new[] { 4 }; }   // 深空风格共用这组碎石 / 地灯
            parent.gameObject.SetActive(false);
            Debug.Log($"[ScatterFuture] rocks={rocks} shards={shards} solarTiles={tiles} roadLights={studs} chunks={chunks.Count}");
        }

        /// <summary>
        /// 归一化（不用 Vector3.normalized）：散布件是毫米级的，两条边叉乘出来的长度只有 1e-6 量级，
        /// 而 Vector3.normalized 对长度小于 1e-5 的向量直接返回零向量 → 法线为零 → 整块全黑（以前“路边石子成了黑色小方块”就是这个）。
        /// </summary>
        static Vector3 Unit(Vector3 v) { float m = v.magnitude; return m > 1e-20f ? v / m : Vector3.up; }

        /// <summary>月面碎石：不规则的矮石块（底圈 + 腰圈 + 顶点，逐面一个法线 = 有棱有角），半埋在地里。</summary>
        static void MoonRock(MeshData md, Vector3 p, float r, System.Random rnd)
        {
            float R() => (float)rnd.NextDouble();
            // 深色玄武岩：比浅灰月面深一档才看得出来；再深（0.24–0.4）近看就成了一块块黑斑（11-13 近拍自查）
            float g = 0.36f + 0.16f * R(); var col = new Color(g, g * 1.02f, g * 1.1f);
            int n = 5 + rnd.Next(2); float a0 = R() * Mathf.PI * 2;
            var lo = new Vector3[n]; var mid = new Vector3[n];
            var top = p + new Vector3((R() - 0.5f) * r * 0.5f, r * (0.55f + 0.35f * R()), (R() - 0.5f) * r * 0.5f);
            for (int k = 0; k < n; k++)
            {
                float a = a0 + k * Mathf.PI * 2 / n, rr = r * (0.75f + 0.5f * R());
                lo[k] = p + new Vector3(Mathf.Cos(a) * rr, -r * 0.2f, Mathf.Sin(a) * rr);
                mid[k] = Vector3.Lerp(lo[k], top, 0.45f + 0.2f * R()) + Vector3.up * r * 0.12f;
            }
            var ctr = p + Vector3.up * r * 0.2f;
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                var nrm = Unit(Vector3.Cross(b - a, c - a));
                if (Vector3.Dot(nrm, (a + b + c) / 3f - ctr) < 0) nrm = -nrm;
                var cc = col * (0.72f + 0.6f * Mathf.Clamp01(nrm.y * 0.7f + 0.3f)); cc.a = 1;
                md.Tri(a, b, c, cc, cc, nrm);
            }
            for (int k = 0; k < n; k++)
            {
                int j = (k + 1) % n;
                Face(lo[k], lo[j], mid[j]); Face(lo[k], mid[j], mid[k]); Face(mid[k], mid[j], top);
            }
        }

        /// <summary>水晶芽：2~4 根小棱锥挤成一丛，根部深、尖上亮；蜂群半场是紫的。</summary>
        static void Shard(MeshData md, Vector3 p, float h, bool violet, System.Random rnd)
        {
            float R() => (float)rnd.NextDouble();
            Color cb = violet ? new Color(0.25f, 0.14f, 0.5f) : new Color(0.15f, 0.44f, 0.6f), ct = violet ? new Color(0.86f, 0.74f, 1f) : new Color(0.8f, 0.97f, 1f);
            int n = 2 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                float hi = h * (i == 0 ? 1f : 0.45f + 0.4f * R()), br = hi * 0.2f, a0 = R() * Mathf.PI;
                var o = p + (i == 0 ? Vector3.zero : new Vector3((R() - 0.5f), 0, (R() - 0.5f)) * h * 0.9f) - Vector3.up * hi * 0.08f;
                var lean = i == 0 ? new Vector3((R() - 0.5f) * 0.2f, 1, (R() - 0.5f) * 0.2f) : (Unit(new Vector3(o.x - p.x, 0, o.z - p.z)) * 0.45f + Vector3.up);
                var apex = o + lean.normalized * hi;
                var ring = new Vector3[4];
                for (int k = 0; k < 4; k++) ring[k] = o + new Vector3(Mathf.Cos(a0 + k * Mathf.PI / 2), 0, Mathf.Sin(a0 + k * Mathf.PI / 2)) * br;
                for (int k = 0; k < 4; k++)
                {
                    var a = ring[k]; var b = ring[(k + 1) % 4];
                    var nrm = Unit(Vector3.Cross(b - a, apex - a));
                    if (Vector3.Dot(nrm, (a + b) * 0.5f - o) < 0) nrm = -nrm;
                    float sh = 0.82f + 0.24f * (k % 2);      // 相邻晶面一明一暗
                    md.Tri(a, b, apex, cb * sh, ct * sh, nrm);
                }
            }
        }

        static readonly Color PanelBlue = new Color(0.09f, 0.14f, 0.34f), PanelFrame = new Color(0.86f, 0.86f, 0.83f), PanelGlow = new Color(0.35f, 0.9f, 1f);

        /// <summary>一块小太阳能板：白边框 + 深蓝板面，朝一个方向斜着，下面一根短支柱。</summary>
        static void SolarTile(MeshData md, Vector3 p, float yaw, System.Random rnd)
        {
            var right = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)); var fwd = new Vector3(-Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
            const float tilt = 30f * Mathf.Deg2Rad;
            var slope = fwd * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt);
            var nrm = Vector3.Cross(slope, right).normalized; if (nrm.y < 0) nrm = -nrm;
            var c = p + Vector3.up * 0.0034f;
            void Plate(float hw, float hl, float lift, Color col)
            {
                var o = c + nrm * lift; var r = right * hw; var s = slope * hl;
                md.Quad(o - r - s, o + r - s, o + r + s, o - r + s, col, nrm);
            }
            Plate(0.0036f, 0.0029f, 0f, PanelFrame);
            Plate(0.0032f, 0.0025f, 0.00012f, PanelBlue * (0.9f + 0.25f * (float)rnd.NextDouble()));
            Plate(0.0032f, 0.00012f, 0.0002f, PanelBlue * 2.2f);          // 板面中间一道分格线
            Bar(md, p - Vector3.up * 0.001f, c - nrm * 0.0002f, 0.0005f, PanelFrame * 0.85f);
        }

        /// <summary>太阳能田外一圈矮灯桩：白桩 + 天青灯头（三面，留一面开口，和麦田篱笆同样的走向）。</summary>
        static void LightPosts(MeshData md, MeshData glow, Vector4 f)
        {
            float c = Mathf.Cos(f.w), s = Mathf.Sin(f.w);
            float hx = f.z * 0.5f + 0.5f, hz = f.z * 0.4f + 0.5f;
            var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            Vector3 W(Vector2 l) { float x = f.x + l.x * c - l.y * s, z = f.y + l.x * s + l.y * c; return Pos(x, z); }
            for (int e = 0; e < 3; e++)
            {
                Vector2 a = corners[e], b = corners[(e + 1) % 4];
                int posts = Mathf.CeilToInt(Vector2.Distance(a, b) / 2.4f);
                for (int k = 0; k <= posts; k++)
                {
                    var p = W(Vector2.Lerp(a, b, (float)k / posts));
                    Bar(md, p - Vector3.up * 0.001f, p + Vector3.up * 0.0048f, 0.0008f, PanelFrame);
                    Bar(glow, p + Vector3.up * 0.0048f, p + Vector3.up * 0.0064f, 0.00095f, PanelGlow);
                }
            }
        }

        /// <summary>麦田：中心 (x, z)、边长（码）、转角。</summary>
        public static readonly Vector4[] Fields =
        {
            new Vector4(-46.5f, 24f, 8f, 0.12f),
            new Vector4(-54f, 42f, 8f, -0.08f),
            new Vector4(-44.5f, 37.5f, 6f, 0.3f),
        };

        public static bool InField(float x, float z)
        {
            foreach (var f in Fields)
            {
                float c = Mathf.Cos(-f.w), s = Mathf.Sin(-f.w), dx = x - f.x, dz = z - f.y;
                float lx = dx * c - dz * s, lz = dx * s + dz * c;
                if (Mathf.Abs(lx) < f.z * 0.5f + 0.6f && Mathf.Abs(lz) < f.z * 0.4f + 0.6f) return true;
            }
            return false;
        }

        static readonly Color[] FlowerColors =
        {
            new Color(0.95f, 0.92f, 0.82f), new Color(0.96f, 0.82f, 0.25f), new Color(0.78f, 0.55f, 0.85f), new Color(0.9f, 0.35f, 0.3f),
        };

        static Vector3 Pos(float x, float z) => new Vector3(x, BoardMap.Height(x, z), z) * BoardMap.Yard;

        static Vector3 Normal(float x, float z)
        {
            const float e = 0.3f;
            float hx = BoardMap.Height(x + e, z) - BoardMap.Height(x - e, z), hz = BoardMap.Height(x, z + e) - BoardMap.Height(x, z - e);
            return new Vector3(-hx, 2 * e, -hz).normalized;
        }

        // ---------------------------------------------------------------- 几何

        class MeshData
        {
            public List<Vector3> v = new(); public List<Color> c = new(); public List<Vector3> n = new(); public List<int> t = new();
            public void Tri(Vector3 a, Vector3 b, Vector3 tip, Color cb, Color ct, Vector3 nrm)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(tip); c.Add(cb); c.Add(cb); c.Add(ct); n.Add(nrm); n.Add(nrm); n.Add(nrm);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, Vector3 nrm)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                for (int k = 0; k < 4; k++) { c.Add(col); n.Add(nrm); }
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v); m.SetColors(c); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
                return m;
            }
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = $"Assets/_WarTable/Art/Generated/{m.name}.asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(path)) UnityEditor.AssetDatabase.DeleteAsset(path);
            UnityEditor.AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>一簇静电草：根部收在 0.5 mm 内、顶部向外散开的细三角草叶；开花草簇的部分叶尖带花色。</summary>
        static void Tuft(MeshData md, Vector3 p, float h, int blades, Color cb, Color ct, Color? flower, System.Random rnd)
        {
            for (int i = 0; i < blades; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2, rr = (float)rnd.NextDouble();
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float lean = 0.08f + rr * 0.3f;                     // 外圈的叶子略斜
                var side = new Vector3(-dir.z, 0, dir.x) * 0.00018f;
                var root = p + dir * 0.0005f * rr - Vector3.up * 0.0005f;
                var tip = root + (Vector3.up + dir * lean).normalized * h * (0.65f + 0.35f * (1 - rr * 0.6f) * (float)rnd.NextDouble() + 0.2f);
                float j = 0.85f + 0.3f * (float)rnd.NextDouble();
                var tc = flower.HasValue && rnd.NextDouble() < 0.3 ? flower.Value : ct * j;
                md.Tri(root - side, root + side, tip, cb * j, tc, (Vector3.up * 2 + dir).normalized);
            }
        }

        /// <summary>一丛芦苇：细长直叶，部分顶端有蒲棒（两片交叉的小长方块）。</summary>
        static void Reed(MeshData md, Vector3 p, float h, int blades, Color cb, Color ct, System.Random rnd)
        {
            for (int i = 0; i < blades; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = new Vector3(-dir.z, 0, dir.x) * 0.0002f;
                var root = p + dir * 0.0008f * (float)rnd.NextDouble() - Vector3.up * 0.0008f;
                float hh = h * (0.6f + 0.4f * (float)rnd.NextDouble());
                var tip = root + (Vector3.up + dir * 0.12f).normalized * hh;
                md.Tri(root - side, root + side, tip, cb, ct, (Vector3.up * 2 + dir).normalized);
                if (rnd.NextDouble() < 0.18)
                {
                    var c = new Color(0.30f, 0.18f, 0.10f);
                    for (int k = 0; k < 2; k++)
                    {
                        var s = Quaternion.Euler(0, k * 90, 0) * side * 1.8f;
                        var t0 = tip - (tip - root).normalized * 0.0025f;
                        md.Quad(t0 - s, t0 + s, tip + s, tip - s, c, Vector3.up);
                    }
                }
            }
        }

        static readonly Color StoneA = new Color(0.55f, 0.52f, 0.47f), StoneB = new Color(0.42f, 0.38f, 0.33f);

        /// <summary>小圆石：压扁的八面体，半埋在地里。</summary>
        static void Pebble(MeshData md, Vector3 p, float r, System.Random rnd)
        {
            var c = Color.Lerp(StoneA, StoneB, (float)rnd.NextDouble());
            float a = (float)rnd.NextDouble() * Mathf.PI;
            var x = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r * (1.1f + 0.4f * (float)rnd.NextDouble());
            var z = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
            var y = Vector3.up * r * 0.6f;
            var top = p + y * 0.6f; var c0 = p - y * 0.2f;
            var ring = new[] { c0 + x, c0 + z, c0 - x, c0 - z };
            for (int k = 0; k < 4; k++)
            {
                var n = Unit(Vector3.Cross(ring[(k + 1) % 4] - ring[k], top - ring[k]));
                if (n.y < 0) n = -n;
                md.Tri(ring[k], ring[(k + 1) % 4], top, c * (0.8f + 0.2f * n.y), c * (0.95f + 0.15f * n.y), n);
            }
        }

        static void Flower(MeshData md, Vector3 p, Color c, System.Random rnd)
        {
            float r = 0.0006f + 0.0003f * (float)rnd.NextDouble();
            for (int k = 0; k < 2; k++)
            {
                float a = k * Mathf.PI * 0.5f + (float)rnd.NextDouble();
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                var e = new Vector3(-d.z, 0, d.x);
                md.Quad(p - d - e, p + d - e, p + d + e, p - d + e, c, Vector3.up);
            }
        }

        static readonly Color WheatStem = new Color(0.72f, 0.62f, 0.30f), WheatEar = new Color(0.93f, 0.78f, 0.38f);

        static void Wheat(MeshData md, Vector3 p, float h, System.Random rnd)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI;
            var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.0003f;
            var lean = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f) * 0.0015f;
            var top = p + Vector3.up * h + lean;
            md.Tri(p - side, p + side, top, WheatStem * 0.7f, WheatStem, Vector3.up);
            // 麦穗：两片交叉的小菱形
            for (int k = 0; k < 2; k++)
            {
                var s = Quaternion.Euler(0, k * 90, 0) * side * 1.5f;
                var e0 = top - Vector3.up * 0.0028f; var e1 = top + Vector3.up * 0.0012f;
                md.Quad(e0, top - Vector3.up * 0.001f + s, e1, top - Vector3.up * 0.001f - s, WheatEar, Vector3.up);
            }
        }

        static readonly Color FenceWood = new Color(0.45f, 0.33f, 0.22f);

        /// <summary>麦田外一圈木篱笆：桩 + 两道横杆（方截面细条）。</summary>
        static void Fence(MeshData md, Vector4 f, System.Random rnd)
        {
            float c = Mathf.Cos(f.w), s = Mathf.Sin(f.w);
            float hx = f.z * 0.5f + 0.5f, hz = f.z * 0.4f + 0.5f;
            var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            Vector3 W(Vector2 l) { float x = f.x + l.x * c - l.y * s, z = f.y + l.x * s + l.y * c; return Pos(x, z); }
            for (int e = 0; e < 4; e++)
            {
                if (e == 3) continue;    // 留一面开口（靠路的一边）
                Vector2 a = corners[e], b = corners[(e + 1) % 4];
                int posts = Mathf.CeilToInt(Vector2.Distance(a, b) / 1.6f);
                Vector3 prev = default;
                for (int k = 0; k <= posts; k++)
                {
                    var p = W(Vector2.Lerp(a, b, (float)k / posts));
                    Bar(md, p - Vector3.up * 0.001f, p + Vector3.up * 0.0085f, 0.0007f, FenceWood);
                    if (k > 0)
                        for (int r = 0; r < 2; r++)
                        {
                            float y = 0.0035f + r * 0.0035f;
                            Bar(md, prev + Vector3.up * y, p + Vector3.up * y, 0.00045f, FenceWood * 1.08f);
                        }
                    prev = p;
                }
            }
        }

        static void Bar(MeshData md, Vector3 a, Vector3 b, float r, Color col)
        {
            var d = (b - a).normalized;
            var u = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized * r;
            var w = Vector3.Cross(d, u).normalized * r;
            var o = new[] { u + w, u - w, -u - w, -u + w };
            for (int k = 0; k < 4; k++)
            {
                var n = (o[k] + o[(k + 1) % 4]).normalized;
                md.Quad(a + o[k], a + o[(k + 1) % 4], b + o[(k + 1) % 4], b + o[k], col * (0.85f + 0.15f * n.y), n);
            }
        }
    }
}
