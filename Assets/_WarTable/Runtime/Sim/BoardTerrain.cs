// BoardTerrain.cs — 沙盘的地形：战场里的起伏 = BoardMap 里那几座战场丘陵，按原型的丘陵公式（双精度）算；
// 坡度方向、坡度换算速度（上坡慢下坡快）、涉水减速、可通行等公式全部沿用原型。
using System;

namespace WarTable.Sim
{
    /// <summary>
    /// 模拟层用的沙盘地形。只取 BoardMap.Hills 里战场上的丘陵（模拟 x = −Unity x），按原型 Terrain.HillHeight 计算；
    /// 画面上的土路、细碎起伏、岩丘、村庄台地、布景平台都不进模拟。
    /// 原因（10-31 实测）：模拟对地形的微小差别很敏感，把这些装饰也算进来时，同样的丘陵下“无脑全冲”的胜率系统性地
    /// 比原型高（原型地形种子 1 固定 9/20 → 沙盘 15/20），平衡就对不上原型测过的数字。
    /// 棋子显示仍贴着画出来的地形网格（BattleView.Ground 射线），两者只差几毫米。
    /// </summary>
    public sealed class BoardTerrain : Terrain
    {
        public BoardTerrain(double half = 40.0) : base(half)
        {
            foreach (var h in BoardMap.Hills)
                if (Math.Abs(h.x) <= half + 5 && Math.Abs(h.y) <= half + 2)
                    Hills.Add(new Hill { X = -h.x, Z = h.y, R = h.z, H = h.w });
        }

        /// <summary>河在战场外（Unity x≈47）；溃兵跑进河道时照样涉水减速。</summary>
        public override double WaterDepth(double x, double z)
        {
            float ux = -(float)x, uz = (float)z;
            if (BoardMap.RiverDistance(ux, uz) > BoardMap.RiverHalfWidth * 1.9f) return 0;
            return Math.Max(0.0, BoardMap.WaterLevel - BoardMap.Height(ux, uz));
        }
    }
}
