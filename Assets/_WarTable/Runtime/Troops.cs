using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 兵种的外观数据（规则数值在玩法阶段从原型移植）：模型名、高度、所属队伍、开局布阵块（原型 defaultPlan 在 xs 战场上的结果）。
    /// 坐标换算：Unity x = −原型 x（原型右手系、Unity 左手系，这样左右和原型一致：我军骑士在玩家右手边）。
    /// </summary>
    public static class Troops
    {
        public struct Def
        {
            public string id, name;
            public int team;
            /// <summary>棋子身高（米，不含 2.2 mm 底座）。</summary>
            public float height;
            public bool mounted;
            public Def(string id, string name, int team, float height, bool mounted = false) { this.id = id; this.name = name; this.team = team; this.height = height; this.mounted = mounted; }
        }

        // 人类约 1.8 码 × 英雄比例 1.6 ≈ 2.9 cm；兽人高一截；矮人矮一截；骑兵连坐骑约 4.2 cm
        public static readonly Def[] All =
        {
            new("kd_guard", "御林卫兵", 0, 0.028f), new("kd_footman", "王国步兵", 0, 0.025f), new("kd_spearman", "边境矛盾兵", 0, 0.031f),
            new("kd_halberdier", "王国长戟兵", 0, 0.030f), new("kd_swordsman", "双手剑士", 0, 0.028f), new("kd_hammer", "王国锤卫", 0, 0.028f),
            new("kd_archer", "长弓手", 0, 0.028f), new("kd_crossbow", "矮人弩手", 0, 0.022f), new("kd_knight", "王国骑士", 0, 0.042f, true),
            new("cl_guardian", "氏族护卫", 1, 0.030f), new("cl_warrior", "赤牙战士", 1, 0.029f), new("cl_skirmisher", "投矛手", 1, 0.031f),
            new("cl_pikeman", "赤牙长矛手", 1, 0.033f), new("cl_berserker", "狂战士", 1, 0.030f), new("cl_crusher", "碎骨者", 1, 0.031f),
            new("cl_hunter", "赤牙猎手", 1, 0.030f), new("cl_wolfrider", "狼骑兵", 1, 0.040f, true), new("cl_wolfarcher", "狼骑射手", 1, 0.039f, true),
        };

        public static Def Get(string id) { foreach (var d in All) if (d.id == id) return d; return default; }

        public struct Block
        {
            public string troop; public int count; public float x, z;
            public Block(string troop, int count, float x, float z) { this.troop = troop; this.count = count; this.x = x; this.z = z; }
        }

        /// <summary>开局布阵（码，原型坐标）。敌军是我军的中心对称。</summary>
        public static readonly Block[] Opening =
        {
            new("kd_spearman", 6, -20, 14), new("kd_guard", 5, -7, 14), new("kd_footman", 5, 7, 14), new("kd_halberdier", 5, 20, 14),
            new("kd_swordsman", 3, -6, 20), new("kd_hammer", 3, 6, 20), new("kd_archer", 4, -8, 26), new("kd_crossbow", 3, 10, 26),
            new("kd_knight", 7, 28, 18),
            new("cl_skirmisher", 6, 20, -14), new("cl_guardian", 5, 7, -14), new("cl_warrior", 5, -7, -14), new("cl_pikeman", 5, -20, -14),
            new("cl_berserker", 3, 6, -20), new("cl_crusher", 3, -6, -20), new("cl_hunter", 6, 0, -26),
            new("cl_wolfrider", 5, -28, -18), new("cl_wolfarcher", 4, 28, -18),
        };

        /// <summary>
        /// 步兵左右间距（码）。原型是 1.7；棋子按英雄比例放大后盾牌、武器会伸到隔壁，改成 2.1（双方一样，平衡不变）。
        /// 玩法阶段移植模拟层时同步改这个值。
        /// </summary>
        public const float Spacing = 1.7f;   // 棋子已回到原型比例，间距也回到原型的 1.7

        /// <summary>横队排布（同原型 arrange('line')）：每排约 √(4n) 人，相邻 1.7 码（骑兵 3.6），排距 1.9（骑兵 4.5）。返回 Unity 坐标（码）。</summary>
        public static Vector2[] LineSlots(Block b)
        {
            var d = Get(b.troop);
            float gap = d.mounted ? 3.6f : Spacing, rowGap = d.mounted ? 4.5f : 2.1f;
            int perRow = Mathf.Max(4, Mathf.Min(b.count, Mathf.CeilToInt(Mathf.Sqrt(b.count * 4))));
            perRow = Mathf.Min(perRow, b.count);
            float back = d.team == 0 ? 1f : -1f;    // 我军面朝 −z，后排在 +z
            var o = new Vector2[b.count];
            for (int i = 0; i < b.count; i++)
            {
                int row = i / perRow, inRow = Mathf.Min(perRow, b.count - row * perRow);
                float side = (i % perRow - (inRow - 1) * 0.5f) * gap;
                o[i] = new Vector2(-(b.x + side * (d.team == 0 ? 1 : -1)), b.z + back * row * rowGap);
            }
            return o;
        }
    }
}
