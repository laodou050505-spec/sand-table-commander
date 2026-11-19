// 自动生成：由 sandtable-xr/src/sim/captain.ts 忠实移植
using System.Collections.Generic;

namespace WarTable.Sim
{
    /// <summary>
    /// 指挥官专长（Bannerlord 队长效果）：每个 true 字段激活对应加成。
    /// </summary>
    public class CaptainPerks
    {
        /// <summary>One-Handed "Basher"：盾墙中步兵承受近战伤害 -4%。</summary>
        public bool Basher;
        /// <summary>One-Handed "Shield Wall"：盾墙中对弓箭的护盾覆盖角加大。</summary>
        public bool ShieldWall;
        /// <summary>Polearm "Sharpen the Tip"：步兵刺击伤害 +5%。</summary>
        public bool SharpenTheTip;
        /// <summary>Polearm "Braced"：步兵对骑兵伤害 +10%。</summary>
        public bool Braced;
        /// <summary>Polearm "Lancer"：本编队骑兵长杆速度加成伤害 +30%。</summary>
        public bool Lancer;
        /// <summary>Two-Handed "Strong Grip"：步兵双手技能 +30。</summary>
        public bool StrongGrip;
        /// <summary>Bow "Bow Control"：编队弓伤害 +5%。</summary>
        public bool BowControl;
        /// <summary>Bow "Strong Bows"：弓伤害 +5%（三阶以上兵种均生效）。</summary>
        public bool StrongBows;
        /// <summary>Leadership "Stout Defender"：防御方开局士气 +8。</summary>
        public bool StoutDefender;
    }

    /// <summary>战旗种类（近战/移速/远程/对骑/士气）。</summary>
    public enum BannerKind
    {
        Damage,    // 近战伤害 +10%
        Speed,     // 移动速度 +10%
        Ranged,    // 远程伤害 +10%
        VsMounted, // 对骑兵伤害 +10%
        Morale     // 士气损失 -10%
    }

    public static class BannerData
    {
        /// <summary>战旗中文名称。</summary>
        public static readonly Dictionary<BannerKind, string> BannerNames = new()
        {
            { BannerKind.Damage,    "战旗：近战伤害 +10%" },
            { BannerKind.Speed,     "战旗：移动速度 +10%" },
            { BannerKind.Ranged,    "战旗：远程伤害 +10%" },
            { BannerKind.VsMounted, "战旗：对骑兵伤害 +10%" },
            { BannerKind.Morale,    "战旗：士气损失 −10%" },
        };

        /// <summary>三阶战旗加成值（Bannerlord 一/二/三阶分别 5%/8%/10%）。</summary>
        public const double BannerValue = 0.1;

        /// <summary>旗手附近多少码内的己方能感受到战旗加成（原型取值）。</summary>
        public const double BannerRadius = 25.0;
    }

    /// <summary>战场上正在流通的战旗实例。</summary>
    public class Banner
    {
        /// <summary>战旗种类。</summary>
        public BannerKind Kind;
        /// <summary>所属队伍（0 或 1）。</summary>
        public int Team;
        /// <summary>所属编队组 ("inf"/"arch"/"cav"/"ha")。</summary>
        public string Group = "";
        /// <summary>当前持旗 Agent 的 Id；null 表示旗帜落地。</summary>
        public int? Bearer;
        /// <summary>旗帜当前世界坐标 X。</summary>
        public double X;
        /// <summary>旗帜当前世界坐标 Z。</summary>
        public double Z;
        /// <summary>持旗人阵亡后旗帜落地为 true。</summary>
        public bool Dropped;
    }
}
