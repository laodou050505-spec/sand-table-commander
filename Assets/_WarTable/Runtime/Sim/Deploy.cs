// Deploy.cs — 战前布阵：BlockPlan / DeployPlan 及布阵辅助函数
// 移植自 deploy.ts，删除了攻城（siege）分支；siege 参数保留接口但内部按 false 处理。
// 外部依赖（待对应文件移植后方可编译通过）：
//   Agent（agent.ts）、Battle（battle.ts）、TROOPS / Role（defs.ts）、
//   Group / Groups.GroupOf（ai.ts）、Arrangement / Slot / arrange（formation.ts）

using System;
using System.Collections.Generic;
using System.Linq;

namespace WarTable.Sim
{
    // ═══════════════════════════════════════════════════════════════════════════
    // 数据结构
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 一个布阵块：同一兵种的若干人，占据战场上一处位置，面向特定方向。
    /// 对应 TS BlockPlan 接口。
    /// </summary>
    public sealed class BlockPlan
    {
        /// <summary>块唯一 ID（由 Deploy.NewBlockId() 生成）。</summary>
        public int Id { get; set; }
        /// <summary>所属队伍（0 = 金狮王国，1 = 赤牙氏族）。</summary>
        public int Team { get; set; }
        /// <summary>兵种 ID（对应 TROOPS 键）。</summary>
        public string Troop { get; set; } = "";
        /// <summary>人数。</summary>
        public int Count { get; set; }
        /// <summary>块中心 X（码）。</summary>
        public double X { get; set; }
        /// <summary>块中心 Z（码）。</summary>
        public double Z { get; set; }
        /// <summary>朝向（弧度；0 = 面向 −Z，π = 面向 +Z）。</summary>
        public double Yaw { get; set; }
        /// <summary>横排人数（0 = 由阵型自行决定）。</summary>
        public int Files { get; set; }
        /// <summary>本块阵型（null = 使用该组的默认阵型）。</summary>
        public string? Arrangement { get; set; }
    }

    /// <summary>
    /// 完整布阵方案：块列表 + 两支队伍的后备兵种（按插入顺序排列）。
    /// 对应 TS DeployPlan 接口。
    /// </summary>
    public sealed class DeployPlan
    {
        /// <summary>所有布阵块（按插入顺序）。</summary>
        public List<BlockPlan> Blocks { get; set; } = new();
        /// <summary>两队后备增援兵种列表（Reserves[0] = 王国，Reserves[1] = 氏族）。</summary>
        public List<string>[] Reserves { get; set; } = { new(), new() };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Deploy — 布阵辅助函数（均为静态方法）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 战前布阵工具集：槽位计算、范围查询、布阵区、默认阵型生成、方案应用。
    /// </summary>
    public static class Deploy
    {
        /// <summary>单块最大人数。</summary>
        public const int BlockMax = 120;
        /// <summary>单侧战场最大总人数。</summary>
        public const int SideMax = 400;

        // ── ID 生成器 ─────────────────────────────────────────────────────────

        private static int s_nextBlockId = 1;
        /// <summary>生成下一个块 ID（每次递增）。</summary>
        public static int NewBlockId() => s_nextBlockId++;

        // ── 兵种属性辅助 ─────────────────────────────────────────────────────

        /// <summary>该角色是否为骑乘兵种（骑兵 / 马弓手）。</summary>
        private static bool IsMountedRole(Role role)
            => role == Role.Cavalry || role == Role.HorseArcher;

        /// <summary>将阵型字符串转换为 Arrangement 枚举（未知值默认 Line）。</summary>
        private static Arrangement ParseArrStr(string s) => s switch
        {
            "loose"      => Arrangement.Loose,
            "shieldwall" => Arrangement.Shieldwall,
            "circle"     => Arrangement.Circle,
            "square"     => Arrangement.Square,
            "skein"      => Arrangement.Skein,
            "column"     => Arrangement.Column,
            _            => Arrangement.Line,
        };

        /// <summary>
        /// 块所在的 AI 编组（inf / cav / arch / ha）。
        /// 对应 TS blockGroup；依赖 Groups.GroupOf（待 AI.cs 移植）。
        /// </summary>
        public static string BlockGroup(BlockPlan b)
            => Groups.GroupOf(WeaponsData.Troops[b.Troop].Role);

        /// <summary>
        /// 块实际使用的阵型：优先用块自身设置，否则取编组默认值。
        /// 对应 TS blockKind。
        /// </summary>
        public static string BlockKind(BlockPlan b)
        {
            if (b.Arrangement != null) return b.Arrangement;
            var g = BlockGroup(b);
            return g == "arch" || g == "ha" ? "loose" : "line";
        }

        /// <summary>
        /// 骑乘兵种间距（siege = false 时 3.6 码，步兵 1.7 码）。
        /// </summary>
        private static double SpacingOf(BlockPlan b)
            => IsMountedRole(WeaponsData.Troops[b.Troop].Role) ? 3.6 : 1.7;

        // ── 槽位与范围 ────────────────────────────────────────────────────────

        /// <summary>
        /// 计算块在给定人数下的所有站位槽（前排优先）。
        /// <para>siege 参数保留接口但始终按 false 处理（攻城已删除）。</para>
        /// 对应 TS blockSlots；依赖 Formation.Arrange（待 Formation.cs 移植）。
        /// </summary>
        public static List<Slot> BlockSlots(
            BlockPlan b,
            int n = -1,
            bool siege = false,
            (double X, double Z, double Yaw)? at = null,
            double spacingMul = 1.0,
            string? kind = null)
        {
            if (n < 0) n = b.Count;
            if (kind == null) kind = BlockKind(b);
            double px = at?.X ?? b.X, pz = at?.Z ?? b.Z, pyaw = at?.Yaw ?? b.Yaw;
            // siege 已删除，骑乘状态仅按兵种判断
            bool cav = IsMountedRole(WeaponsData.Troops[b.Troop].Role);
            return Formation.Arrange(ParseArrStr(kind!), n, px, pz, pyaw, SpacingOf(b) * spacingMul, cav, b.Files);
        }

        /// <summary>
        /// 块在自身坐标系中的占地范围（码；含外边距）。
        /// 右方向 ∈ [R0, R1]，前方 ∈ [F0, F1]（yaw = 0 时右 = +x，前 = −z）。
        /// 对应 TS blockExtent。
        /// </summary>
        public static (double R0, double R1, double F0, double F1) BlockExtent(
            BlockPlan b, bool siege = false)
        {
            var slots = BlockSlots(b, Math.Max(1, b.Count), siege, (0.0, 0.0, 0.0));
            double r0 = double.PositiveInfinity, r1 = double.NegativeInfinity;
            double f0 = double.PositiveInfinity, f1 = double.NegativeInfinity;
            foreach (var s in slots)
            {
                if (s.X < r0) r0 = s.X;
                if (s.X > r1) r1 = s.X;
                if (-s.Z < f0) f0 = -s.Z;  // yaw = 0 时前方为 −z
                if (-s.Z > f1) f1 = -s.Z;
            }
            double m = IsMountedRole(WeaponsData.Troops[b.Troop].Role) ? 1.8 : 0.9;
            return (r0 - m, r1 + m, f0 - m, f1 + m);
        }

        // ── 布阵区 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 某侧在战场上可站立的区域（各队占各自半场，互不越界）。
        /// 对应 TS deployZone；删除了攻城（if w）分支。
        /// </summary>
        public static (double X0, double X1, double Z0, double Z1) DeployZone(
            Terrain t, int team)
        {
            double h = t.Half - 4;
            double gap = Math.Min(12.0, t.Half * 0.15);
            return team == 0
                ? (-h, h, gap, h)
                : (-h, h, -h, -gap);
        }

        /// <summary>
        /// 将块中心限制在己方布阵区内。对应 TS clampBlock。
        /// </summary>
        public static void ClampBlock(Terrain t, BlockPlan b)
        {
            var (x0, x1, z0, z1) = DeployZone(t, b.Team);
            b.X = Math.Max(x0, Math.Min(x1, b.X));
            b.Z = Math.Max(z0, Math.Min(z1, b.Z));
        }

        // ── 兵源均匀化 ────────────────────────────────────────────────────────

        /// <summary>
        /// 将兵种映射均匀交织为列表，使任意前缀都能公平代表整支军队。
        /// 对应 TS 私有函数 interleave。
        /// </summary>
        private static List<string> Interleave(Dictionary<string, int> spec)
        {
            var all   = new List<string>();
            // 用 List 保留键的插入顺序（Dictionary 不保证顺序）
            var keys  = spec.Keys.ToList();
            var left  = new Dictionary<string, int>(spec);
            bool any  = true;
            while (any)
            {
                any = false;
                foreach (var id in keys)
                    if (left[id] > 0) { all.Add(id); left[id]--; any = true; }
            }
            return all;
        }

        // ── 默认布阵方案 ──────────────────────────────────────────────────────

        /// <summary>
        /// 按兵种角色生成默认阵型：步兵前列、震撼后队、弓手末后、骑兵右翼、马弓手左翼。
        /// 攻城分支已删除。
        /// 对应 TS defaultPlan（armies 为两队兵种映射，cap = Infinity 时全员上场）。
        /// </summary>
        public static DeployPlan DefaultPlan(
            (Dictionary<string, int> Team0, Dictionary<string, int> Team1) armies,
            Terrain t,
            double distance,
            double cap = double.PositiveInfinity)
        {
            var blocks   = new List<BlockPlan>();
            var reserves = new[] { new List<string>(), new List<string>() };
            int capInt   = double.IsPositiveInfinity(cap) ? int.MaxValue : (int)cap;
            double d     = Math.Min(distance, t.Half * 0.35);

            for (int team = 0; team <= 1; team++)
            {
                var armySpec = team == 0 ? armies.Team0 : armies.Team1;
                var all      = Interleave(armySpec);
                var field    = all.Take(capInt).ToList();
                var reserve  = all.Skip(capInt).ToList();
                reserves[team] = reserve;

                // 统计上场各兵种人数（保留插入顺序以决定后续块排列顺序）
                var seen   = new List<string>();
                var counts = new Dictionary<string, int>();
                foreach (var id in field)
                {
                    if (!counts.ContainsKey(id)) { seen.Add(id); counts[id] = 0; }
                    counts[id]++;
                }

                double yaw   = team == 0 ? 0.0 : Math.PI;
                double right = team == 0 ? 1.0 : -1.0;
                double sign  = team == 0 ? 1.0 : -1.0;

                // 创建一个块（中心尚未确定，由 row 函数排列）
                BlockPlan Mk(string troop, int n) => new BlockPlan
                {
                    Id = NewBlockId(), Team = team, Troop = troop,
                    Count = n, X = 0, Z = 0, Yaw = yaw, Files = 0, Arrangement = null
                };

                // 按给定谓词筛选兵种并创建块列表（保留插入顺序）
                List<BlockPlan> Of(Func<Role, bool> pred)
                {
                    var result = new List<BlockPlan>();
                    foreach (var id in seen)
                        if (pred(WeaponsData.Troops[id].Role)) result.Add(Mk(id, counts[id]));
                    return result;
                }

                // 将一排块沿 X 方向居中排开，各块 Z 坐标统一设为 z
                void Row(List<BlockPlan> list, double z, double gap = 3.0, double centre = 0.0)
                {
                    var widths = list.Select(b => { var e = BlockExtent(b); return e.R1 - e.R0; }).ToList();
                    double totalW = widths.Sum() + gap * Math.Max(0, list.Count - 1);
                    double x = centre - totalW / 2;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = BlockExtent(list[i]);
                        list[i].X = (x - e.R0) * right;
                        list[i].Z = z;
                        x += widths[i] + gap;
                    }
                }

                // 某排块的最大纵深（前→后方向）
                double DepthOf(List<BlockPlan> list)
                    => list.Count == 0 ? 0.0
                        : Math.Max(0.0, list.Max(b => { var e = BlockExtent(b); return e.F1 - e.F0; }));

                // ── 非攻城阵型 ────────────────────────────────────────────────
                // 步兵 / 矛兵（矛兵放两侧，盾牌步兵居中）
                var front = Of(r => r == Role.Infantry || r == Role.Spear);
                // 稳定排序：盾牌步兵在前，矛兵在两侧（使用 LINQ OrderBy 保证稳定性）
                var frontSorted = front.OrderBy(b => WeaponsData.Troops[b.Troop].Role == Role.Spear ? 1 : 0).ToList();
                var mid = new List<BlockPlan>();
                for (int i = 0; i < frontSorted.Count; i++)
                {
                    if (i % 2 == 0) mid.Insert(0, frontSorted[i]);  // 偶数 → 插入头部（TS unshift）
                    else             mid.Add(frontSorted[i]);         // 奇数 → 追加末尾（TS push）
                }
                Row(mid, sign * d, 5.0);

                var shock  = Of(r => r == Role.Shock);
                double zShock = sign * (d + DepthOf(mid) + 2);
                Row(shock, zShock, 6.0);

                var ranged = Of(r => r == Role.Archer);
                Row(ranged, sign * (Math.Abs(zShock) + DepthOf(shock) + 4), 7.0);

                double frontW = mid.Count == 0 ? 30.0
                    : Math.Max(30.0, mid.Max(b => Math.Abs(b.X) + 8));

                var cav = Of(r => r == Role.Cavalry);
                Row(cav, sign * (d + 4), 4.0,
                    centre:  Math.Min(t.Half - 12, frontW + 12));

                var ha = Of(r => r == Role.HorseArcher);
                Row(ha, sign * (d + 4), 4.0,
                    centre: -Math.Min(t.Half - 12, frontW + 12));

                blocks.AddRange(mid);
                blocks.AddRange(shock);
                blocks.AddRange(ranged);
                blocks.AddRange(cav);
                blocks.AddRange(ha);
            }

            foreach (var b in blocks) ClampBlock(t, b);
            return new DeployPlan { Blocks = blocks, Reserves = reserves };
        }

        // ── 槽位站人 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 优先让最适合前排的士兵（持盾 → 矛兵 → 其余）站前排槽位。
        /// 对应 TS standIn。
        /// </summary>
        public static void StandIn(List<Agent> members, List<Slot> slots, Action<Agent, Slot> place)
        {
            // LINQ OrderBy 保证稳定排序
            var sorted = members.OrderBy(a => Rank(a)).ToList();
            for (int i = 0; i < slots.Count && i < sorted.Count; i++)
                place(sorted[i], slots[i]);
        }

        /// <summary>
        /// 排列优先级：持盾 = 0（最优先）、矛兵 = 1、其余 = 2。
        /// 依赖 Agent.HasShield（待 Agent.cs 移植）。
        /// </summary>
        private static int Rank(Agent a)
            => Agent.HasShield(a) ? 0 : a.Troop.Role == Role.Spear ? 1 : 2;

        // ── 应用布阵方案 ──────────────────────────────────────────────────────

        /// <summary>
        /// 将布阵方案应用到正在部署的战局：增减各块人数，移除已删除的块，
        /// 将所有人员站入槽位，并将玩家置于其块后方。
        /// 返回本次新召入的 Agent ID 列表。
        /// 对应 TS applyPlan（删除了 b.siege / dismountNow 调用）。
        /// 依赖 Battle / Agent（待对应文件移植）。
        /// </summary>
        public static List<int> ApplyPlan(Battle b, List<BlockPlan> plan, Agent? player)
        {
            var raised = new List<int>();
            var t = b.Terrain;

            // 克隆方案，避免外部修改影响内部状态
            b.Plan = plan.Select(p => new BlockPlan
            {
                Id = p.Id, Team = p.Team, Troop = p.Troop, Count = p.Count,
                X = p.X, Z = p.Z, Yaw = p.Yaw, Files = p.Files, Arrangement = p.Arrangement
            }).ToList();

            foreach (var p in b.Plan)
            {
                p.Count = Math.Max(0, Math.Min(BlockMax, (int)Math.Round((double)p.Count)));
                ClampBlock(t, p);
            }

            // 按块 ID 分组现有 Agent（跳过已离场或阵亡的）
            var byBlock = new Dictionary<int, List<Agent>>();
            foreach (var a in b.Agents)
            {
                if (a.FormationBlock < 0 || a.Gone || !a.Alive) continue;
                if (!byBlock.ContainsKey(a.FormationBlock))
                    byBlock[a.FormationBlock] = new List<Agent>();
                byBlock[a.FormationBlock].Add(a);
            }

            void Dismiss(Agent a)
            {
                a.Gone  = true;
                a.Alive = false;
                b.Initial[a.Team]--;
            }

            // 移除不在新方案中的块的所有士兵
            var wanted = new HashSet<int>(b.Plan.Select(p => p.Id));
            foreach (var kv in byBlock)
                if (!wanted.Contains(kv.Key))
                    foreach (var a in kv.Value) Dismiss(a);

            // 按新方案增减各块人数
            foreach (var p in b.Plan)
            {
                var list = byBlock.ContainsKey(p.Id) ? byBlock[p.Id] : new List<Agent>();

                // 兵种变更：解散旧士兵重新征召
                if (list.Count > 0 && list[0].Troop.Id != p.Troop)
                {
                    foreach (var a in list) Dismiss(a);
                    list = new List<Agent>();
                }

                // 多了就从后排解散
                while (list.Count > p.Count) { Dismiss(list[^1]); list.RemoveAt(list.Count - 1); }

                // 少了就召入新士兵
                while (list.Count < p.Count)
                {
                    var a = b.Spawn(p.Troop, p.X, p.Z, p.Yaw);
                    a.FormationBlock = p.Id;
                    // （siege 已删除，无需 dismountNow）
                    raised.Add(a.Id);
                    list.Add(a);
                }

                // 将人员站入槽位
                StandIn(list, BlockSlots(p, list.Count),
                    (a, s) => Teleport(b, a, s.X, s.Z, s.Yaw));
            }

            // 将玩家放到其块后方
            if (player != null && player.Alive)
            {
                var pp = b.Plan.Find(q => q.Team == player.Team && q.Troop == player.Troop.Id && q.Count > 0)
                    ?? b.Plan.Find(q => q.Team == player.Team
                                        && BlockGroup(q) == Groups.GroupOf(player.Troop.Role) && q.Count > 0)
                    ?? b.Plan.Find(q => q.Team == player.Team && q.Count > 0);
                if (pp != null)
                {
                    double f = BlockExtent(pp).F0 - 2.0;
                    Teleport(b, player,
                        pp.X + Agent.ForwardX(pp.Yaw) * f,
                        pp.Z + Agent.ForwardZ(pp.Yaw) * f,
                        pp.Yaw);
                }
            }

            return raised;
        }

        /// <summary>
        /// 部署阶段瞬移：立即将士兵放到指定位置（不产生速度）。
        /// 对应 TS 私有函数 teleport。
        /// </summary>
        private static void Teleport(Battle b, Agent a, double x, double z, double yaw)
        {
            double h = b.Terrain.Half;
            a.X = Math.Max(-h, Math.Min(h, x));
            a.Z = Math.Max(-h, Math.Min(h, z));
            a.Y = b.Terrain.Height(a.X, a.Z);
            a.Yaw = yaw;
            a.Vx = a.Vz = 0.0;
            a.Input.Face = yaw;
            a.Input.MoveX = a.Input.MoveZ = 0.0;
            if (a.Mount != null) a.Mount.Speed = 0.0;
        }

        // ── 补充兵分配 ────────────────────────────────────────────────────────

        /// <summary>
        /// 确定某增援士兵应加入哪个块（优先同兵种，否则同组）。
        /// 返回块 ID，未找到返回 -1。
        /// 对应 TS blockFor(plan, a)。
        /// </summary>
        public static int BlockFor(List<BlockPlan>? plan, Agent a)
        {
            if (plan == null) return -1;
            return (plan.Find(p => p.Team == a.Team && p.Troop == a.Troop.Id)
                 ?? plan.Find(p => p.Team == a.Team && BlockGroup(p) == Groups.GroupOf(a.Troop.Role)))?.Id ?? -1;
        }
    }
}
