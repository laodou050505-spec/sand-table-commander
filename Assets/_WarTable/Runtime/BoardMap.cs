using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 沙盘这张图：地形高度、地表混合权重、河道、各处布景的位置。
    /// 单位是“码”（模拟层的单位），1 码 = 沙盘上 1 cm（<see cref="Yard"/>）。
    /// 我军（蓝）在玩家近侧 +Z，敌军（红）在远侧 −Z；从玩家看过去，+X 在左手边。
    /// 战场中央保持开阔（玩法沿用 Three.js 版的丘陵野战），布景放在四周。
    /// 玩法阶段模拟层直接读这里的高度。
    /// </summary>
    public static class BoardMap
    {
        /// <summary>1 码在沙盘上的长度（米）。</summary>
        public const float Yard = 0.01f;
        /// <summary>战场半宽（码）：模拟层的战场 80 码见方 = 沙盘上 0.8 m。</summary>
        public const float Half = 40f;
        /// <summary>
        /// 沙盘内沿半宽（码）：左右各多出 20 码的布景带（河、对岸树林、村庄、牧场、塔），两军身后各多出 10 码放营地，
        /// 战场里不放大件，两军布阵不会压在房子上。
        /// </summary>
        public const float HalfX = 60f, HalfZ = 50f;
        /// <summary>
        /// 整张沙盘（含棋子、布景）的放大倍数 = 沙盘根节点的缩放。模拟层和所有搭建代码都在根节点本地坐标里算（1 码 = <see cref="Yard"/>），
        /// 放大只改这一个数：内沿 120 × 100 码 → 1.38 × 1.15 m，步兵棋子约 4 cm。
        /// </summary>
        public const float BoardScale = 1.15f;

        // ---------------------------------------------------------------- 布局（码）

        /// <summary>两座缓丘（对称摆放，谁也不占地利）。x, z, 半径, 高。</summary>
        public static Vector4[] Hills =
        {
            // 战场丘陵 = 原型 Terrain.open(地形种子 1) 的丘陵（x 取反到 Unity）：两军阵线侧前方各一座宽缓丘、身后各一道缓坡。
            // 模拟层只用这几座（Sim/BoardTerrain，原型公式），装饰起伏不进模拟。
            // 10-31 平衡筛选（BalanceTest，16 套原型地形，每种打法 20 局）：挂机 0 / 无脑全冲 9 / 待敌反冲 14 / 步弓待敌 + 骑兵包抄 12 / 骑兵延后 13，
            // 三种有章法的打法都胜过全冲；多数地形上全冲能赢 14–18 局
            new Vector4(-14.765f, 4.911f, 24.785f, 5.356f), new Vector4(14.765f, -4.911f, 24.785f, 5.356f),
            new Vector4(-1.797f, 15.328f, 20.729f, 3.041f), new Vector4(1.797f, -15.328f, 20.729f, 3.041f),
            // 布景带里的起伏：对岸树林的山坡、立石小丘、牧场缓坡、营地后面的土坎
            new Vector4(56f, -26f, 13f, 4.5f), new Vector4(55f, 36f, 10f, 3.2f),
            new Vector4(-55f, -19f, 10f, 2.0f), new Vector4(-54f, 40f, 9f, 1.4f),
            new Vector4(-22f, -47f, 9f, 2.2f), new Vector4(24f, 47f, 8f, 2.0f),
        };

        /// <summary>瞭望塔所在的岩丘（玩家右手边远角）。</summary>
        public static readonly Vector4 Knoll = new Vector4(-50f, -35f, 10f, 5.6f);
        /// <summary>村庄所在的缓台地（玩家右手边布景带）。</summary>
        public static readonly Vector4 Village = new Vector4(-50f, 5f, 11f, 0.7f);

        /// <summary>河中心线的 x（随 z 弯曲），沿玩家左侧布景带从远流到近。</summary>
        public static float RiverX(float z) => 47f + 2.2f * Mathf.Sin(z * 0.085f + 0.7f) + 0.9f * Mathf.Sin(z * 0.21f + 2.1f);
        public const float RiverHalfWidth = 2.6f;
        public const float RiverBed = -1.5f;
        public const float WaterLevel = -0.55f;
        /// <summary>石桥所在的 z。</summary>
        public const float BridgeZ = 6f;

        /// <summary>土路：折线（码），画成土色带；纯视觉。</summary>
        public static readonly Vector2[][] Roads =
        {
            // 横穿战场的大路：石桥 → 战场中部 → 村庄
            new[] { new Vector2(60, 7), new Vector2(53, 6.5f), new Vector2(45, 6), new Vector2(36, 5), new Vector2(22, 2), new Vector2(8, 3), new Vector2(-8, 1), new Vector2(-24, 3), new Vector2(-38, 5), new Vector2(-46, 5) },
            // 村庄里的小路 → 村外
            new[] { new Vector2(-46, 5), new Vector2(-53, 4), new Vector2(-60, 2) },
            // 村庄 → 塔
            new[] { new Vector2(-48, 4), new Vector2(-50, -8), new Vector2(-48, -20), new Vector2(-48, -28) },
            // 村庄 → 风车
            new[] { new Vector2(-50, 7), new Vector2(-52, 18), new Vector2(-53.5f, 25.5f) },
            // 我军营地 → 大路
            new[] { new Vector2(-2, 46), new Vector2(-3, 38), new Vector2(-4, 30), new Vector2(-6, 18), new Vector2(-8, 1) },
            // 敌军营地 → 大路
            new[] { new Vector2(6, -46), new Vector2(7, -38), new Vector2(8, -28), new Vector2(9, -14), new Vector2(8, 3) },
        };
        public const float RoadHalfWidth = 1.5f;

        // ---------------------------------------------------------------- 高度

        static float Bump(float x, float z, Vector4 h)
        {
            float d = Mathf.Sqrt((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y)) / h.z;
            if (d >= 1f) return 0f;
            return h.w * 0.5f * (1f + Mathf.Cos(Mathf.PI * d));
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }

        public static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        public static float Fbm(float x, float y) => Noise(x, y) * 0.55f + Noise(x * 2.1f + 13f, y * 2.1f + 7f) * 0.3f + Noise(x * 4.3f + 3f, y * 4.3f + 29f) * 0.15f;

        /// <summary>到折线的最近距离。</summary>
        public static float DistToPolyline(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        public static float RoadDistance(float x, float z)
        {
            var p = new Vector2(x, z);
            float d = float.MaxValue;
            foreach (var r in Roads) d = Mathf.Min(d, DistToPolyline(p, r));
            return d;
        }

        /// <summary>离河中心线的水平距离（码）。</summary>
        public static float RiverDistance(float x, float z) => Mathf.Abs(x - RiverX(z));

        /// <summary>地面高度（码，地台基面 = 0）。</summary>
        public static float Height(float x, float z)
        {
            float h = 0f;
            foreach (var b in Hills) h += Bump(x, z, b);
            // 细碎起伏：只留大尺度的缓坡（几毫米）。原来还有一层 3 码波长的小波纹，迎光面在草皮上成了一道道发白的波纹线，去掉
            // 起伏噪声做成绕中心 180° 对称（两军面对的地形互为镜像）：地形每局都一样，不对称就会让一方一直占便宜
            // （测过：不对称时“无脑全冲”10/10 胜，打法不再决定胜负）
            h += (Undulation(x, z) + Undulation(-x, -z)) * 0.5f;
            // 岩丘：主体圆丘 + 岩石台阶感
            float k = Bump(x, z, Knoll);
            if (k > 0f) h += k * (0.85f + 0.3f * Fbm(x * 0.25f, z * 0.25f));
            // 村庄台地：顶部压平
            float vd = Mathf.Sqrt((x - Village.x) * (x - Village.x) + (z - Village.y) * (z - Village.y));
            float vk = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(Village.z * 0.6f, Village.z, vd));
            h = Mathf.Lerp(h, Village.w, vk);
            // 土路略低于草皮（像压出来的车辙）
            float rd = RoadDistance(x, z);
            h -= 0.12f * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(RoadHalfWidth * 0.6f, RoadHalfWidth * 1.4f, rd));
            // 河道：河床 + 岸坡
            float d = RiverDistance(x, z);
            float bank = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(RiverHalfWidth * 0.55f, RiverHalfWidth * 1.9f, d));
            h = Mathf.Lerp(h, RiverBed, bank);
            // 贴近木框的一圈压到 0 左右，和框内壁接平
            float edge = Mathf.Min(HalfX - Mathf.Abs(x), HalfZ - Mathf.Abs(z));
            float ek = Mathf.InverseLerp(0.5f, 6f, edge);
            float rim = d < RiverHalfWidth * 1.9f ? RiverBed * bank : 0.35f;  // 河从框下穿出，不填平；其余贴着框沿高度
            h = Mathf.Lerp(rim, h, Mathf.SmoothStep(0f, 1f, ek));
            // 布景件脚下的平台：压平到平台高度，模型的土坪贴地（不悬空、草不从底座里冒出来）
            if (Pads != null)
                foreach (var p in Pads)
                {
                    float pd = Mathf.Sqrt((x - p.x) * (x - p.x) + (z - p.y) * (z - p.y));
                    float pk = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.z * 0.95f, p.z * 1.6f + 0.6f, pd));
                    if (pk > 0f) h = Mathf.Lerp(h, p.w, pk);
                }
            return h;
        }

        /// <summary>
        /// 布景件平台（x, z, 半径, 高度；码）。编辑器里搭沙盘前由 PropBuilder.ComputePads 按摆放表算出来；
        /// 都在布景带里，战场不受影响（玩法阶段要读高度时把它序列化进场景数据）。
        /// </summary>
        public static Vector4[] Pads;

        /// <summary>大范围细碎起伏的幅度（只影响画面，不进模拟；0.3 让棋子显示和模拟高度只差几毫米）。</summary>
        public static float UndulationScale = 0.3f;
        static float Undulation(float x, float z) => ((Fbm(x * 0.06f, z * 0.06f) - 0.5f) * 2.0f + (Fbm(x * 0.13f + 7f, z * 0.13f) - 0.5f) * 0.6f) * UndulationScale;

        /// <summary>地表混合权重：r 干草  g 土路  b 岩石  a 河床（都为 0 时是青草）。</summary>
        public static Color Weights(float x, float z, Vector3 normal)
        {
            float h = Height(x, z);
            float slope = 1f - normal.y;
            // 干草：坡顶和向阳坡、成片噪声
            float dry = Mathf.Clamp01((Fbm(x * 0.07f + 3f, z * 0.07f) - 0.52f) * 2.4f) * 0.6f + Mathf.Clamp01((h - 1.4f) * 0.5f) * 0.35f;
            // 土路
            float rd = RoadDistance(x, z);
            float dirt = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(RoadHalfWidth * 0.55f, RoadHalfWidth * 1.25f + 0.6f * Noise(x * 0.8f, z * 0.8f), rd));
            // 村庄空地踩出来的土
            float vd = Mathf.Sqrt((x - Village.x) * (x - Village.x) + (z - Village.y) * (z - Village.y));
            dirt = Mathf.Max(dirt, Mathf.SmoothStep(0.55f, 0f, vd / Village.z) * (0.4f + 0.5f * Noise(x * 0.5f, z * 0.5f)));
            // 岩石：陡坡 + 岩丘顶
            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.42f, slope));
            float kd = Mathf.Sqrt((x - Knoll.x) * (x - Knoll.x) + (z - Knoll.y) * (z - Knoll.y)) / Knoll.z;
            // 注意 Mathf.SmoothStep(a, b, t) 是在 a、b 之间插值，不是着色器里的 smoothstep(edge0, edge1, x)；
            // 之前写成 SmoothStep(0.8, 0.25, kd)，岩丘外面全盘都得到 0.25 的岩石权重，岩石贴图的白色裂纹透到草地上
            rock = Mathf.Max(rock, Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.25f, 0.8f, kd)) * (0.35f + 0.65f * Fbm(x * 0.3f, z * 0.3f)));
            // 河床与岸边沙石
            float d = RiverDistance(x, z);
            float sand = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(RiverHalfWidth * 1.1f, RiverHalfWidth * 1.9f + Noise(x * 0.6f, z * 0.6f), d));
            dry *= 1f - Mathf.Max(dirt, rock, sand);
            dirt *= 1f - sand;
            return new Color(Mathf.Clamp01(dry), Mathf.Clamp01(dirt), Mathf.Clamp01(rock * (1f - sand * 0.7f)), Mathf.Clamp01(sand));
        }
    }
}
