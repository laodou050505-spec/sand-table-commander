using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 玩家设置（封面“设置”页里改，存在 PlayerPrefs）。语言在 <see cref="Loc"/> 里。
    /// 战速：战场推进快慢（模拟时间倍率）；暗度：封面 / 菜单时把真实房间压暗多少。各三档，捏一下切到下一档。
    /// </summary>
    public static class Settings
    {
        public const int Levels = 3;
        static readonly float[] Speeds = { 0.45f, 0.6f, 0.8f };
        static readonly float[] Dims = { 0.6f, 0.82f, 0.95f };
        const string SpeedKey = "wartable.speed", DimKey = "wartable.dim";

        public static int SpeedLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SpeedKey, 1), 0, Levels - 1);
            set => PlayerPrefs.SetInt(SpeedKey, ((value % Levels) + Levels) % Levels);
        }

        public static int DimLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(DimKey, 1), 0, Levels - 1);
            set => PlayerPrefs.SetInt(DimKey, ((value % Levels) + Levels) % Levels);
        }

        /// <summary>时代（11-10 用户：标题里选不同时期的战斗）：0 = 中古（原版兵种），1 = 哥特（哥特科幻兵种，预制体 Prefabs/Figures/Gothic）。</summary>
        /// 11-12：扩成 4 个时代——0 中古、1 哥特、2 现代、3 未来。没做好的时代（EraReady = false）在时代页点了只显示“敬请期待”，Era 也不会取到它。
        /// 11-13：第五个风格“深空”（下标 4，后缀 s，样图 C：星枪团 对 余烬矿帮，重型机甲）。
        public const int Eras = 5;
        /// <summary>各时代是否做好了、能选。未来时代的兵种装配齐后把 [3] 改成 true。</summary>
        public static readonly bool[] EraReady = { true, true, true, true, true };   // 11-13：深空（[4]）也装配齐了，五个风格全开   // 11-13：现代时代也装配齐了，四个时代全开
        /// <summary>各时代的资源 / 文字后缀：文字键 “group.inf@g”、旗牌 “tile_0_inf_g_zh”。</summary>
        public static readonly string[] EraSuffix = { "", "g", "m", "f", "s" };
        public static bool Ready(int era) => era >= 0 && era < Eras && EraReady[era];
        const string EraKey = "wartable.era";
        public static int Era
        {
            get { int e = PlayerPrefs.GetInt(EraKey, 0); return Ready(e) ? e : 0; }
            set => PlayerPrefs.SetInt(EraKey, Ready(value) ? value : 0);
        }

        public static float BattleSpeed => Speeds[SpeedLevel];
        public static float Dim => Dims[DimLevel];
    }
}
