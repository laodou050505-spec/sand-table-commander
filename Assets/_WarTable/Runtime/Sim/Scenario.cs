// Scenario.cs — 战局创建：地形工厂、默认军队、SetupBattle / Replan / PickCommanders
// 移植自 scenario.ts，删除了 SetupSiege() 及所有 siege/攻城 分支。
// 保留：open / flat 地图，DEFAULT_ARMIES，SetupBattle，SetupPlanned（private），Replan，PickCommanders。
// 外部依赖（待对应文件移植后方可编译通过）：
//   Battle（battle.ts）、Agent（agent.ts）、TROOPS（defs.ts）、
//   Deploy.ApplyPlan（deploy.ts，已翻译）

using System;
using System.Collections.Generic;
using System.Linq;

namespace WarTable.Sim
{
    // ═══════════════════════════════════════════════════════════════════════════
    // ArmySpec — 兵种编制映射（兵种 ID → 人数）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 一支军队的编制：兵种 ID → 人数。
    /// 对应 TS interface ArmySpec { [troopId: string]: number }。
    /// </summary>
    public sealed class ArmySpec : Dictionary<string, int>
    {
        public ArmySpec() { }
        public ArmySpec(params (string TroopId, int Count)[] entries)
        {
            foreach (var (id, n) in entries) this[id] = n;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Scenario — 战局构建工具集
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 战局创建与重整工具集：地形工厂、默认军队配置、SetupBattle / Replan / PickCommanders。
    /// 所有方法均为静态。
    /// </summary>
    public static class Scenario
    {
        // ── 默认军队配置 ──────────────────────────────────────────────────────

        /// <summary>
        /// 两支默认军队：金狮王国（队伍 0）vs 赤牙氏族（队伍 1）。
        /// 对应 TS DEFAULT_ARMIES（精确复制兵种与人数）。
        /// </summary>
        public static readonly (ArmySpec Team0, ArmySpec Team1) DefaultArmies = (
            new ArmySpec(
                ("kd_guard",     5), ("kd_footman",  5), ("kd_spearman",  6),
                ("kd_halberdier",5), ("kd_swordsman", 3), ("kd_hammer",    3),
                ("kd_archer",    4), ("kd_crossbow",  3), ("kd_knight",    7)
            ),
            new ArmySpec(
                ("cl_guardian",  5), ("cl_warrior",   5), ("cl_skirmisher",6),
                ("cl_pikeman",   5), ("cl_berserker",  3), ("cl_crusher",   3),
                ("cl_hunter",    6), ("cl_wolfrider",  5), ("cl_wolfarcher",4)
            )
        );

        // ── 兵种部署布局参数 ──────────────────────────────────────────────────

        /// <summary>
        /// 各角色部署参数：与友军前线的前后偏移（forward，正值更靠近敌方）、
        /// 侧向偏移（side）、横向间距（spacing，码）、每排人数上限（perRow）。
        /// 对应 TS LAYOUT 常量。
        /// </summary>
        private static readonly Dictionary<Role, (double Forward, double Side, double Spacing, int PerRow)> Layout
            = new()
            {
                { Role.Infantry,    (  0.0,   0.0, 1.7, 14) },
                { Role.Spear,       ( -2.0,   0.0, 1.7, 14) },
                { Role.Shock,       ( -4.0,   0.0, 1.7, 14) },
                { Role.Archer,      (-12.0,   0.0, 2.2, 12) },
                { Role.Cavalry,     ( -6.0,  26.0, 3.6,  6) },
                { Role.HorseArcher, ( -6.0, -26.0, 3.6,  6) },
            };

        // ── 旗帜类型常量 ──────────────────────────────────────────────────────

        /// <summary>
        /// 两支军队的旗帜类型：王国=伤害加成，氏族=士气加成。
        /// 对应 TS BANNERS 常量。
        /// </summary>
        private static readonly string[] Banners = { "damage", "morale" };

        // ── 地形工厂 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 按地图类型、随机种子、战场尺寸创建地形。
        /// 攻城（siege）及特殊地形（oasis/canyon/wadi/dunes）已删除，
        /// 仅保留 "open"（丘陵）和默认平地（flat）。
        /// 对应 TS makeTerrain。
        /// </summary>
        public static Terrain MakeTerrain(string map, int seed, string size = "m")
        {
            double half = Terrain.FieldSizes.TryGetValue(size, out var s) ? s : 110.0;
            return map == "open" ? Terrain.Open(seed, half) : Terrain.Flat(half);
        }

        // ── 兵源分配 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 将兵种编制均匀交织后按上场人数上限分为上场和后备两部分。
        /// 对应 TS 私有 split 函数。
        /// </summary>
        private static (List<string> Field, List<string> Reserve) Split(
            ArmySpec spec, double cap)
        {
            // 均匀交织，使任意前缀都是整支军队的公平样本
            var all   = new List<string>();
            var keys  = spec.Keys.ToList();  // 保留插入顺序
            var left  = new Dictionary<string, int>(spec);
            bool any  = true;
            while (any)
            {
                any = false;
                foreach (var id in keys)
                    if (left[id] > 0) { all.Add(id); left[id]--; any = true; }
            }
            int capInt = double.IsPositiveInfinity(cap) ? int.MaxValue : (int)cap;
            return (all.Take(capInt).ToList(), all.Skip(capInt).ToList());
        }

        // ── 战局构建（主入口） ────────────────────────────────────────────────

        /// <summary>
        /// 创建并布置一场野战（不含攻城）。
        /// 两支军队在战场中线两侧展开，骑兵在翼，弓手在后；
        /// 若提供布阵方案则按方案布置（调用 SetupPlanned）。
        /// 攻城分支（map == "siege"）已删除。
        /// 对应 TS setupBattle。
        /// 依赖 Battle / Agent / TROOPS（待对应文件移植）。
        /// </summary>
        public static (Battle Battle, Agent? Player) SetupBattle(
            (ArmySpec Team0, ArmySpec Team1) armies,
            string? playerTroop = null,
            int seed = 7,
            double distance = 34.0,
            double fieldCap = double.PositiveInfinity,
            string map = "flat",
            string size = "m",
            DeployPlan? plan = null,
            Terrain? terrain = null)
        {
            var battle = new Battle(seed);
            // 游戏里传 BoardTerrain（沙盘真实地形）；核验时为空，按原型生成
            battle.Terrain = terrain ?? MakeTerrain(map, seed, size);

            // 若有预设布阵方案，交由 SetupPlanned 处理
            if (plan != null)
                return SetupPlanned(battle, plan, playerTroop, distance, map);

            // 小战场时两军起始距离适当缩短
            distance = Math.Min(distance, battle.Terrain.Half * 0.35);

            Agent? player = null;

            for (int team = 0; team <= 1; team++)
            {
                double sign = team == 0 ? 1.0 : -1.0;
                double yaw  = team == 0 ? 0.0 : Math.PI;

                var armySpec = team == 0 ? armies.Team0 : armies.Team1;
                var (field, reserve) = Split(armySpec, fieldCap);
                battle.Reserves[team]  = reserve;
                battle.FieldCap[team]  = fieldCap;
                battle.SpawnLine[team] = (Z: sign * (distance + 22), Yaw: yaw);

                // 按角色分组（保留插入顺序，使用列表 + 字典双重结构）
                var roleOrder = new List<Role>();
                var byRole    = new Dictionary<Role, List<string>>();
                foreach (var id in field)
                {
                    var role = WeaponsData.Troops[id].Role;
                    if (!byRole.ContainsKey(role)) { roleOrder.Add(role); byRole[role] = new List<string>(); }
                    byRole[role].Add(id);
                }

                // 按角色逐个生成 Agent
                foreach (var role in roleOrder)
                {
                    if (!Layout.TryGetValue(role, out var l)) continue;
                    var ids = byRole[role];
                    for (int i = 0; i < ids.Count; i++)
                    {
                        int row   = i / l.PerRow;
                        int inRow = Math.Min(l.PerRow, ids.Count - row * l.PerRow);
                        double col = (i % l.PerRow) - (inRow - 1) / 2.0;
                        double x   = (col * l.Spacing + l.Side) * sign;
                        double rowSpacing = role == Role.Cavalry || role == Role.HorseArcher ? 4.5 : 1.9;
                        double z   = sign * (distance - l.Forward + row * rowSpacing);
                        battle.Spawn(ids[i], x, z, yaw);
                    }
                }
            }

            // 玩家 Agent（站在其兵种阵型后方）
            if (playerTroop != null)
            {
                int team   = WeaponsData.Troops[playerTroop].Team;
                double sign = team == 0 ? 1.0 : -1.0;
                var role   = WeaponsData.Troops[playerTroop].Role;
                if (Layout.TryGetValue(role, out var l))
                {
                    double rowOffset = role == Role.Cavalry || role == Role.HorseArcher ? -5.0 : 3.0;
                    player = battle.Spawn(
                        playerTroop,
                        l.Side * sign,
                        sign * (distance - l.Forward + rowOffset),
                        team == 0 ? 0.0 : Math.PI);
                    player.IsPlayer = true;
                }
            }

            PickCommanders(battle, player);
            return (battle, player);
        }

        // ── 按方案布置（私有） ────────────────────────────────────────────────

        /// <summary>
        /// 按 BlockPlan 方案布置战局（攻城分支已删除）。
        /// 对应 TS 私有 setupPlanned。
        /// </summary>
        private static (Battle Battle, Agent? Player) SetupPlanned(
            Battle battle, DeployPlan plan, string? playerTroop,
            double distance, string map)
        {
            var t = battle.Terrain;
            // 非攻城：两军在中线两侧各自出发线上入场
            double d = Math.Min(distance, t.Half * 0.35);
            battle.SpawnLine = new[]
            {
                (Z:  d + 22, Yaw: 0.0),
                (Z: -(d + 22), Yaw: Math.PI)
            };

            Agent? player = null;
            if (playerTroop != null)
            {
                int team = WeaponsData.Troops[playerTroop].Team;
                player = battle.Spawn(
                    playerTroop, 0.0,
                    battle.SpawnLine[team].Z,
                    battle.SpawnLine[team].Yaw);
                player.IsPlayer = true;
                // （siege 已删除，无需 dismountNow）
            }

            for (int team = 0; team <= 1; team++)
            {
                battle.Reserves[team] = new List<string>(plan.Reserves[team]);
                int onField = plan.Blocks
                    .Where(b => b.Team == team)
                    .Sum(b => b.Count);
                battle.FieldCap[team] = plan.Reserves[team].Count > 0
                    ? Math.Max(1.0, onField)
                    : double.PositiveInfinity;
            }

            Deploy.ApplyPlan(battle, plan.Blocks, player);
            PickCommanders(battle, player);
            return (battle, player);
        }

        // ── 重整（部署阶段方案变更） ──────────────────────────────────────────

        /// <summary>
        /// 部署阶段方案变更后重新应用：重新任命队长、清空旗帜、重置士气。
        /// 返回本次新召入的 Agent ID 列表。
        /// 对应 TS replan。
        /// </summary>
        public static List<int> Replan(Battle battle, List<BlockPlan> blocks, Agent? player)
        {
            var raised = Deploy.ApplyPlan(battle, blocks, player);

            for (int team = 0; team <= 1; team++)
            {
                int onField = blocks.Where(b => b.Team == team).Sum(b => b.Count);
                if (!double.IsPositiveInfinity(battle.FieldCap[team]))
                    battle.FieldCap[team] = Math.Max(1.0, onField);
            }

            // 清空队长、旗帜，重置士气
            battle.Captains = new[] { new Dictionary<string, int>(), new Dictionary<string, int>() };
            battle.Banners.Clear();  // 清空旗帜（Banner 类型待 captain.ts 移植后确定）
            foreach (var a in battle.Agents)
                if (a.Alive && !a.Gone)
                    a.Morale = battle.StartMorale[a.Team];

            PickCommanders(battle, player);
            return raised;
        }

        // ── 任命队长与指挥官 ──────────────────────────────────────────────────

        /// <summary>
        /// 任命旗手队长与作战指挥官（攻城"坚守防御"专长检查已删除）。
        /// 对应 TS pickCommanders。
        /// 依赖 Battle.AppointCaptains / Agent（待对应文件移植）。
        /// </summary>
        public static void PickCommanders(Battle battle, Agent? player)
        {
            // 旗手：王国=伤害旗，氏族=士气旗；两侧均任 inf 编组队长为旗手
            battle.AppointCaptains(Banners, new[] { "inf", "inf" });
            // （攻城"坚守防御"专长加成已删除）

            // 指挥官：玩家在场则为己方指挥官，否则取第一名军官
            for (int team = 0; team <= 1; team++)
            {
                Agent? commander = null;
                if (player != null && player.Team == team)
                    commander = player;
                else
                    commander = battle.Agents.Find(a =>
                        a.Alive && !a.Gone && a.Team == team && a.Troop.Officer);
                battle.Commander[team] = commander?.Id ?? (int?)null;
            }
        }
    }
}
