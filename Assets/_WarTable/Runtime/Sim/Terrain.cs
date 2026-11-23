// Terrain.cs — 战场地面高度模型
// 移植自 terrain.ts，删除了攻城相关内容（Wall、Siege 工厂、wallHeight、oasis/canyon/wadi/dunes 工厂）。
// 保留：ITerrain 接口、xorshift PRNG、Open/Flat 工厂、Hill/Pond 结构、全部高度/坡度/水深查询。
// 依赖：无（仅 System.Math / System.Collections.Generic）。

using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    // ═══════════════════════════════════════════════════════════════════════════
    // ITerrain 接口（Battle / AI 所用的地形查询接口）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Battle 和 AI 所用的地形查询接口。
    /// 所有坐标单位为码（yards），Y 轴向上。
    /// </summary>
    public interface ITerrain
    {
        /// <summary>战场半宽（码）；战斗在 ±Half 范围内进行。</summary>
        double Half { get; }
        /// <summary>地面高度（码）。</summary>
        double Height(double x, double z);
        /// <summary>沿 z 轴正方向的坡度（每码高度变化；正值上坡，负值下坡）。</summary>
        double Slope(double x, double z);
        /// <summary>该点是否有积水。</summary>
        bool Water(double x, double z);
        /// <summary>该坐标的坡度速度系数（≤1 上坡减速，>1 下坡加速）。</summary>
        double SpeedFactor(double x, double z);
        /// <summary>蹚水减速系数（干地返回 1.0）。</summary>
        double WadeFactor(double x, double z);
        /// <summary>该坐标是否可通行。</summary>
        bool Passable(double x, double z);
        /// <summary>攻城墙（siege 功能已删除，始终为 null）。</summary>
        Wall? Wall { get; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Wall — 空壳定义（攻城已删除，保留以兼容检查 terrain.wall 的代码）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 攻城墙（对应 terrain.ts 的 Wall 接口）。
    /// 字段名由 camelCase → PascalCase；AI.cs 需直接访问 Z / Inside / Thick / Height / GateClosed。
    /// </summary>
    public sealed class Wall
    {
        /// <summary>墙体中心线 Z 坐标（世界空间）。</summary>
        public double Z;
        /// <summary>墙厚（米）。</summary>
        public double Thick;
        /// <summary>墙顶走道高度（米）。</summary>
        public double Height;
        /// <summary>城门宽度（米）。</summary>
        public double Gate;
        /// <summary>城门当前是否关闭。</summary>
        public bool GateClosed = true;
        /// <summary>防守方站在 +Z 侧（Inside = −1）还是 −Z 侧（Inside = 1）。</summary>
        public double Inside;
        /// <summary>爬梯坡道 X 位置列表。</summary>
        public double[] Ramps = Array.Empty<double>();
        /// <summary>爬梯坡道长度（米）。</summary>
        public double RampLen;
        /// <summary>墙体半长（沿 X 轴，双向 ±HalfLength）。</summary>
        public double HalfLength;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 地形数据结构
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 山丘或洼地：正 H 是隆起，负 H 是凹陷（河床、水池盆地）。
    /// </summary>
    public struct Hill
    {
        public double X, Z;
        /// <summary>峰高（码）；负值为洼地。</summary>
        public double H;
        /// <summary>底部半径（码）。</summary>
        public double R;
        /// <summary>沿 Ang 方向拉伸倍数（0 = 不拉伸）。</summary>
        public double E;
        /// <summary>拉伸轴方向（弧度）。</summary>
        public double Ang;
        /// <summary>台地：平坦顶部 + 陡峭侧面（边缘不规则）。</summary>
        public bool Mesa;
        /// <summary>沙丘：锋利峰脊，背风面（+z）陡缩。</summary>
        public bool Dune;
    }

    /// <summary>
    /// 浅水池：建于洼地之上，可蹚水穿越（无需绕行）。
    /// </summary>
    public struct Pond
    {
        public double X, Z;
        /// <summary>水面高度（码）。</summary>
        public double Level;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Terrain 类
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 战场地面高度模型。
    /// 山丘成对布置（绕中心 180° 对称），使双方地利相当。
    /// </summary>
    public class Terrain : ITerrain
    {
        /// <summary>预设战场尺寸映射：键 → 半宽（码）。</summary>
        public static readonly Dictionary<string, double> FieldSizes = new()
        {
            { "xs", 40.0 }, { "s", 60.0 }, { "m", 110.0 }, { "l", 160.0 }
        };

        /// <summary>山丘列表（按插入顺序迭代）。</summary>
        public List<Hill> Hills { get; } = new();

        /// <summary>水池列表。</summary>
        public List<Pond> Ponds { get; } = new();

        /// <summary>攻城墙（siege 已删除，始终为 null）。</summary>
        public Wall? Wall => null;

        /// <summary>战场半宽（码）。</summary>
        public double Half { get; private set; }

        /// <summary>地貌标记（仅供视图层使用，不影响模拟）。</summary>
        public string Landform { get; private set; } = "desert";

        public Terrain(double half = 110.0)
        {
            Half = half;
        }

        // ── PRNG ──────────────────────────────────────────────────────────────

        /// <summary>
        /// xorshift32 伪随机数生成器，8 步预热。
        /// 与 terrain.ts 中的 rng() 行为完全一致（使用 uint 保证无符号运算）。
        /// </summary>
        public static Func<double> Rng(int seed)
        {
            uint s = (seed == 0) ? 1u : (uint)seed;
            double Rnd()
            {
                // xorshift32，对应 JS：s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0;
                s ^= s << 13;
                s ^= s >> 17;   // uint >> 等同于 JS >>> （无符号右移）
                s ^= s << 5;
                return s / 4294967296.0;
            }
            for (int i = 0; i < 8; i++) Rnd();  // 8 步预热，跳过小种子间的相关性
            return Rnd;
        }

        // ── 静态工厂 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 开阔地形：带随机丘陵，双侧对称，种子决定形态。
        /// 对应 TS Terrain.open()。
        /// </summary>
        public static Terrain Open(int seed, double half = 110.0)
        {
            var t = new Terrain(half);
            var rnd = Rng(seed);
            // 小战场时山丘中心向内缩（等比缩放，而非缩小山丘本身）
            double k = Math.Min(1.0, half / FieldSizes["m"]);

            // 按 TS 对象字面量顺序调用 rnd()，确保 PRNG 状态一致
            double flankXBase = 35 + rnd() * 12;
            double flankZBase = 4  + rnd() * 14;
            double flankH     = 3.5 + rnd() * 3;
            double flankR     = 22  + rnd() * 8;
            double rearXBase  = (rnd() - 0.5) * 24;
            double rearZBase  = 42  + rnd() * 8;
            double rearH      = 3   + rnd() * 2;
            double rearR      = 16  + rnd() * 6;
            double side       = rnd() < 0.5 ? 1.0 : -1.0;  // 侧翼丘在哪侧

            // 侧翼隆起（一侧各一处）
            t.Pair(new Hill { X = flankXBase * side * k, Z = flankZBase * k, H = flankH, R = flankR });
            // 后方小丘（每方阵后各一处）
            t.Pair(new Hill { X = rearXBase  * k,        Z = rearZBase  * k, H = rearH,  R = rearR  });
            return t;
        }

        /// <summary>
        /// 平地（无起伏）。对应 TS Terrain.flat()。
        /// </summary>
        public static Terrain Flat(double half = 110.0)
        {
            return new Terrain(half);
        }

        // ── 内部辅助 ─────────────────────────────────────────────────────────

        /// <summary>平滑插值函数 [0,1]（三次 S 形曲线）。</summary>
        private static double Smooth01(double t) => t * t * (3 - 2 * t);

        /// <summary>
        /// 特征成对布置：原位置 + 绕场地中心 180° 对称的位置，
        /// 使双方始终从相同地利出发。
        /// </summary>
        private void Pair(Hill h)
        {
            Hills.Add(h);
            Hills.Add(new Hill
            {
                X = -h.X, Z = -h.Z,
                H = h.H, R = h.R,
                E = h.E, Ang = h.Ang,
                Mesa = h.Mesa, Dune = h.Dune
            });
        }

        // ── 高度计算 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 所有山丘叠加后的地面高度（码）。
        /// 正高度累加；多个洼地只取最深者（避免重叠河段形成深坑）。
        /// </summary>
        public virtual double HillHeight(double x, double z)
        {
            double up = 0.0, down = 0.0;
            foreach (var hill in Hills)
            {
                double dx = x - hill.X, dz = z - hill.Z;

                // 拉伸变换：将坐标旋转并沿主轴压缩
                if (hill.E != 0.0)
                {
                    double c = Math.Cos(hill.Ang), sn = Math.Sin(hill.Ang);
                    double ndx = (dx * c + dz * sn) / hill.E;
                    double ndz = -dx * sn + dz * c;
                    dx = ndx; dz = ndz;
                }

                // 沙丘背风面（+dz 方向）缩短，坡度更陡
                if (hill.Dune && dz > 0) dz *= 1.9;

                double d = Math.Sqrt(dx * dx + dz * dz) / hill.R;

                // 台地边缘锯齿化（凸凹不均，由位置哈希决定）
                if (hill.Mesa && d < 1.3)
                {
                    double a = Math.Atan2(dz, dx), ph = hill.X * 0.37 + hill.Z * 0.21;
                    d *= 1 + 0.09 * Math.Sin(3 * a + ph) + 0.04 * Math.Sin(7 * a + ph * 2.3);
                }

                if (d >= 1.0) continue;

                double f;
                if (hill.Mesa)
                    // 台地：0.62 以内完全平坦，0.62–0.8 陡坡面，0.8–1.0 碎石裙边
                    f = d < 0.62 ? 1.0
                        : d < 0.80 ? 1 - 0.8 * Smooth01((d - 0.62) / 0.18)
                        : 0.2 * (1 - Smooth01((d - 0.80) / 0.20));
                else if (hill.Dune)
                    // 沙丘：曲率略带尖顶
                    f = Math.Pow(1 - d, 1.35) * (0.6 + 0.4 * Smooth01(1 - d));
                else
                    // 圆丘：余弦衰减
                    f = 0.5 * (1 + Math.Cos(Math.PI * d));

                if (hill.H > 0) up += hill.H * f;
                else down = Math.Min(down, hill.H * f);
            }
            return up + down;
        }

        /// <summary>
        /// 该点积水深度（码）；干地返回 0。
        /// </summary>
        public virtual double WaterDepth(double x, double z)
        {
            if (Ponds.Count == 0) return 0.0;
            double level = double.NegativeInfinity;
            foreach (var p in Ponds)
                if (Math.Abs(x - p.X) < 60 && Math.Abs(z - p.Z) < 60)
                    level = Math.Max(level, p.Level);
            return double.IsNegativeInfinity(level) ? 0.0 : Math.Max(0.0, level - HillHeight(x, z));
        }

        // ── ITerrain 接口实现 ─────────────────────────────────────────────────

        /// <summary>地面高度（码）：山丘叠加，不含攻城墙。</summary>
        public double Height(double x, double z) => HillHeight(x, z);

        /// <summary>该点是否有积水。</summary>
        public bool Water(double x, double z) => WaterDepth(x, z) > 0.0;

        /// <summary>
        /// 沿 z 轴正方向的坡度（ITerrain 接口版本，无方向参数）。
        /// </summary>
        public double Slope(double x, double z) => SlopeDir(x, z, 0.0, 1.0);

        /// <summary>
        /// 带方向的坡度：沿 (dx, dz) 方向每码上升高度（码/码）。
        /// 对应 TS slope(x, z, dx, dz)；正值上坡，负值下坡。
        /// </summary>
        public double SlopeDir(double x, double z, double dx, double dz)
        {
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-6) return 0.0;
            const double e = 0.6;
            return (Height(x + (dx / l) * e, z + (dz / l) * e) - Height(x, z)) / e;
        }

        /// <summary>
        /// 按坡度值计算速度系数（对应 TS speedFactor(slope)）。
        /// ×0.6 约在 1:2 坡；平路 ×1.0；下坡最高 ×1.15。
        /// </summary>
        public double SpeedFactorFromSlope(double slope)
        {
            if (slope > 0)
                return slope < 0.56
                    ? Math.Max(0.55, 1 - slope * 0.8)
                    : Math.Max(0.22, 0.55 - (slope - 0.56) * 0.5);
            return Math.Min(1.15, 1 - slope * 0.3);
        }

        /// <summary>
        /// 该坐标的速度系数（沿 z 轴方向坡度；ITerrain 接口版本）。
        /// </summary>
        public double SpeedFactor(double x, double z)
            => SpeedFactorFromSlope(Slope(x, z));

        /// <summary>
        /// 蹚水减速系数：膝深减半，大腿深近乎停止冲锋（对应 TS wadeFactor）。
        /// </summary>
        public double WadeFactor(double x, double z)
        {
            double d = WaterDepth(x, z);
            return d <= 0.0 ? 1.0 : Math.Max(0.35, 1 - d * 0.75);
        }

        /// <summary>
        /// 是否可通行（siege 功能删除后，始终返回 true）。
        /// 对应 TS passable()，已移除 onLadder / y0 参数。
        /// </summary>
        public bool Passable(double x, double z) => true;
    }
}
