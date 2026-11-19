// AI.cs — 移植自 sandtable-xr/src/sim/ai.ts（1364 行）
// 每支队伍一名指挥官为各组（步兵、弓手、骑兵、骑射手）下达命令；
// 每个 Agent 根据命令和所见将其转化为与玩家键盘完全相同的 Input。
// Unity 6，C# 9，无 UnityEngine 引用，所有数值用 double，System.Math。

using System;
using System.Collections.Generic;
using System.Linq;

namespace WarTable.Sim
{
    // ══════════════════════════════════════════════════════════════════
    // 公共类型
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 队伍编组键：步兵 / 弓手 / 骑兵 / 骑射手。
    /// 对应 TS Group = 'inf' | 'arch' | 'cav' | 'ha'。
    /// </summary>
    public static class Groups
    {
        public const string Inf  = "inf";
        public const string Arch = "arch";
        public const string Cav  = "cav";
        public const string Ha   = "ha";

        /// <summary>所有组键的有序列表（与 TS GROUPS 顺序一致）。</summary>
        public static readonly string[] All = { Inf, Arch, Cav, Ha };

        /// <summary>中文名称映射。</summary>
        public static readonly Dictionary<string, string> Names = new()
        {
            { Inf,  "步兵" }, { Arch, "弓手" }, { Cav,  "骑兵" }, { Ha,   "骑射手" },
        };

        /// <summary>角色 → 编组键（对应 TS groupOf）。</summary>
        public static string GroupOf(Role role) =>
            role == Role.Archer      ? Arch :
            role == Role.Cavalry     ? Cav  :
            role == Role.HorseArcher ? Ha   : Inf;
    }

    /// <summary>
    /// 移动命令：auto / charge / hold / follow / advance / fallback / retreat。
    /// 与 TS Order 一一对应（小写字符串）。
    /// </summary>
    public static class Orders
    {
        public const string Auto     = "auto";
        public const string Charge   = "charge";
        public const string Hold     = "hold";
        public const string Follow   = "follow";
        public const string Advance  = "advance";
        public const string Fallback = "fallback";
        public const string Retreat  = "retreat";
    }

    /// <summary>
    /// 一组的当前命令（位置 + 朝向 + 命令类型）。对应 TS GroupOrder 接口。
    /// </summary>
    public sealed class GroupOrder
    {
        public string Order = Orders.Auto;
        public double X, Z, Yaw;
    }

    // ══════════════════════════════════════════════════════════════════
    // AI 主类
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 战斗 AI：每支队伍一名指挥官 + 每个 Agent 的逐帧行为决策。
    /// 移植自 ai.ts，逻辑与随机调用顺序完全忠实于原版。
    /// </summary>
    public sealed class AI
    {
        // ── 常量 ──────────────────────────────────────────────────────
        /// <summary>指挥官发令间隔（秒）。</summary>
        const double CommandEvery = 0.25;
        /// <summary>检测来袭箭矢的刷新间隔。</summary>
        const double ArrowLook    = 0.2;
        /// <summary>弓手检查射线是否畅通的刷新间隔。</summary>
        const double ShotLook     = 0.15;
        /// <summary>步兵在此范围内搜索敌人（码）。</summary>
        const double MeleeSearch  = 20;
        /// <summary>超过此距离时目标选择更宽松。</summary>
        const double FarTarget    = 40;

        // ── 依赖 ──────────────────────────────────────────────────────
        readonly Battle _battle;

        // ── 公共状态 ──────────────────────────────────────────────────
        /// <summary>每队每组的驻留命令。auto 表示由指挥官决定。</summary>
        public Dictionary<string, GroupOrder>[] OrdersMap;

        /// <summary>每队每组玩家选择的阵型（null = 由指挥官决定）。</summary>
        public Dictionary<string, Arrangement?>[] ArrangementMap;

        /// <summary>每队每组的密集度系数（0.6 … 2）。</summary>
        public Dictionary<string, double>[] SpacingMap;

        /// <summary>每队每组的停止射击标记。</summary>
        public Dictionary<string, bool>[] HoldFireMap;

        /// <summary>每组本帧实际执行的阵型。</summary>
        public Dictionary<string, Arrangement>[] CurrentMap;

        /// <summary>指挥官当前战术文字（用于 HUD）。</summary>
        public Dictionary<string, string>[] TacticMap;

        // ── 私有状态 ──────────────────────────────────────────────────
        /// <summary>每个 Agent 的 AI 记忆。</summary>
        readonly Dictionary<int, Brain> _brains = new();

        /// <summary>每个敌人被多少己方盯住（每帧统计一次）。</summary>
        readonly Dictionary<int, int> _targeted = new();

        /// <summary>各组的有效命令（考虑指挥官覆盖后）。</summary>
        Dictionary<string, GroupOrder>[] _effective;

        /// <summary>各组在战斗开始时的参考帧（用于保持阵型块相对位置）。</summary>
        readonly Dictionary<string, (double X, double Z, double Yaw)> _refs = new();

        /// <summary>两队指挥官下次发令的倒计时（秒）。</summary>
        double[] _commandIn = { 0, CommandEvery / 2 };

        /// <summary>各队面临的来袭射弹（每帧收集）。</summary>
        readonly List<Projectile>[] _flying = { new(), new() };

        /// <summary>散落的无主战马（每帧收集）。</summary>
        readonly List<(double X, double Z, double Speed)> _looseHorses = new();

        /// <summary>梯子底部附近的我方人数（攻城用，每帧统计）。</summary>
        readonly Dictionary<double, int> _ladderCrowd = new();

        bool _wasDeploying;

        // ── 构造 ──────────────────────────────────────────────────────

        public AI(Battle battle)
        {
            _battle = battle;
            OrdersMap      = new[] { MkPerGroup(() => new GroupOrder()), MkPerGroup(() => new GroupOrder()) };
            ArrangementMap = new[] { MkPerGroup<Arrangement?>(() => null), MkPerGroup<Arrangement?>(() => null) };
            SpacingMap     = new[] { MkPerGroup(() => 1.0), MkPerGroup(() => 1.0) };
            HoldFireMap    = new[] { MkPerGroup(() => false), MkPerGroup(() => false) };
            CurrentMap     = new[] { MkDefaultArrangement(), MkDefaultArrangement() };
            TacticMap      = new[] { MkPerGroup(() => ""), MkPerGroup(() => "") };
            _effective     = new[] { MkPerGroup(() => new GroupOrder()), MkPerGroup(() => new GroupOrder()) };
        }

        // ── 工具：构造 PerGroup 字典 ──────────────────────────────────

        static Dictionary<string, T> MkPerGroup<T>(Func<T> v) =>
            new() { { Groups.Inf, v() }, { Groups.Arch, v() }, { Groups.Cav, v() }, { Groups.Ha, v() } };

        static Dictionary<string, Arrangement> MkDefaultArrangement() => new()
        {
            { Groups.Inf,  Arrangement.Line  },
            { Groups.Arch, Arrangement.Loose },
            { Groups.Cav,  Arrangement.Line  },
            { Groups.Ha,   Arrangement.Loose },
        };

        // ══════════════════════════════════════════════════════════════
        // 公共命令接口
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 给一组或多组下达命令（对应 TS setOrder）。
        /// </summary>
        public void SetOrder(int team, List<string> groups, string order, double x, double z, double yaw)
        {
            _commandIn[team] = 0;
            foreach (var g in groups)
            {
                OrdersMap[team][g] = new GroupOrder { Order = order, X = x, Z = z, Yaw = yaw };
                if (order == Orders.Auto)
                    _effective[team][g] = new GroupOrder { Order = Orders.Auto, X = x, Z = z, Yaw = yaw };
            }
        }

        /// <summary>
        /// 将一组或多组移动到目标点并朝向指定方向站立（对应 TS moveTo）。
        /// </summary>
        public void MoveTo(int team, List<string> groups, double x, double z, double yaw)
        {
            _commandIn[team] = 0;
            double rx = Math.Cos(yaw), rz = -Math.Sin(yaw);
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                double off  = (i - (groups.Count - 1) / 2.0) * 14;
                double back = g == Groups.Arch ? -6 : 0;
                OrdersMap[team][g] = new GroupOrder
                {
                    Order = Orders.Hold,
                    X = x + rx * off + Agent.ForwardX(yaw) * back,
                    Z = z + rz * off + Agent.ForwardZ(yaw) * back,
                    Yaw = yaw,
                };
            }
        }

        /// <summary>
        /// 就地转向指定角度，不移动（对应 TS face(yaw: number)）。
        /// </summary>
        public void Face(int team, List<string> groups, double yaw)
        {
            _commandIn[team] = 0;
            foreach (var g in groups)
            {
                var members = Members(team, g);
                if (members.Count == 0) continue;
                var c   = Centroid(members);
                var cur = _effective[team][g];
                var at  = cur.Order == Orders.Hold ? (X: cur.X, Z: cur.Z) : (X: c.X, Z: c.Z);
                OrdersMap[team][g] = new GroupOrder { Order = Orders.Hold, X = at.X, Z = at.Z, Yaw = yaw };
            }
        }

        /// <summary>
        /// 就地朝向敌人，不移动（对应 TS face(yaw: 'enemy')）。
        /// </summary>
        public void FaceEnemy(int team, List<string> groups)
        {
            _commandIn[team] = 0;
            foreach (var g in groups)
            {
                var members = Members(team, g);
                if (members.Count == 0) continue;
                var c   = Centroid(members);
                var cur = _effective[team][g];
                var at  = cur.Order == Orders.Hold ? (X: cur.X, Z: cur.Z) : (X: c.X, Z: c.Z);
                var (ex, ez) = TowardEnemy(team, at.X, at.Z);
                OrdersMap[team][g] = new GroupOrder { Order = Orders.Hold, X = at.X, Z = at.Z, Yaw = Agent.YawTo(ex, ez) };
            }
        }

        /// <summary>
        /// 设置阵型（null = 交由指挥官决定）。
        /// </summary>
        public void SetArrangement(int team, List<string> groups, Arrangement? kind)
        {
            _commandIn[team] = 0;
            foreach (var g in groups) ArrangementMap[team][g] = kind;
        }

        /// <summary>
        /// 前进 / 后退若干步（对应 TS move10）。
        /// </summary>
        public void Move10(int team, List<string> groups, double paces)
        {
            _commandIn[team] = 0;
            foreach (var g in groups)
            {
                var members = Members(team, g);
                if (members.Count == 0) continue;
                var cur = _effective[team][g];
                var c   = Centroid(members);
                var (ex, ez) = TowardEnemy(team, c.X, c.Z);
                double yaw  = (cur.Order == Orders.Hold || cur.Order == Orders.Advance) ? cur.Yaw : Agent.YawTo(ex, ez);
                double bx   = cur.Order == Orders.Hold ? cur.X : c.X;
                double bz   = cur.Order == Orders.Hold ? cur.Z : c.Z;
                OrdersMap[team][g] = new GroupOrder
                {
                    Order = Orders.Hold,
                    X = bx + Agent.ForwardX(yaw) * paces,
                    Z = bz + Agent.ForwardZ(yaw) * paces,
                    Yaw = yaw,
                };
            }
        }

        /// <summary>
        /// 调整队形密集度（factor ∈ [0.6, 2]）。
        /// </summary>
        public void SetSpacing(int team, List<string> groups, double factor)
        {
            _commandIn[team] = 0;
            foreach (var g in groups)
                SpacingMap[team][g] = Math.Max(0.6, Math.Min(2, SpacingMap[team][g] * factor));
        }

        /// <summary>
        /// 设置是否禁止射击。
        /// </summary>
        public void SetHoldFire(int team, List<string> groups, bool hold)
        {
            foreach (var g in groups) HoldFireMap[team][g] = hold;
        }

        /// <summary>
        /// 查询某组的有效命令（对应 TS effectiveOrder）。
        /// </summary>
        public GroupOrder EffectiveOrder(int team, string g) => _effective[team][g];

        /// <summary>
        /// 某组当前存活、非溃逃、非玩家的成员列表（对应 TS members）。
        /// </summary>
        public List<Agent> Members(int team, string g)
        {
            var result = new List<Agent>();
            foreach (var a in _battle.Alive(team))
                if (Groups.GroupOf(a.Troop.Role) == g && !a.IsPlayer && !a.Routed)
                    result.Add(a);
            return result;
        }

        // ══════════════════════════════════════════════════════════════
        // 每帧主入口
        // ══════════════════════════════════════════════════════════════

        /// <summary>每帧调用一次（对应 TS update）。</summary>
        public void Update(double dt)
        {
            // 首帧建格子
            if (_battle.Time == 0) _battle.Grid.Build(_battle.Agents);

            // 梯子底部人数统计（攻城）
            _ladderCrowd.Clear();
            var w = _battle.Terrain.Wall;
            if (w != null && _battle.Ladders.Count > 0)
            {
                double outer = w.Z - w.Inside * (w.Thick / 2);
                foreach (var o in _battle.Agents)
                {
                    if (!o.Alive || o.Gone || o.Team != 0 || Math.Abs(o.Z - outer) >= 3) continue;
                    foreach (var lx in _battle.Ladders)
                        if (Math.Abs(o.X - lx) < 1.5)
                            _ladderCrowd[lx] = (_ladderCrowd.TryGetValue(lx, out var n) ? n : 0) + 1;
                }
            }

            // 部署结束时重置各 auto 命令
            if (_wasDeploying && !_battle.Deploying)
            {
                foreach (int team in new[] { 0, 1 })
                    foreach (var g in Groups.All)
                        if (OrdersMap[team][g].Order == Orders.Auto)
                            _effective[team][g] = new GroupOrder { Order = Orders.Auto };
            }
            _wasDeploying = _battle.Deploying;

            // 部署阶段参考帧随时更新
            if (_battle.Deploying) _refs.Clear();

            // 统计目标被盯数
            _targeted.Clear();
            var deadIds = new List<int>();
            foreach (var (id, br) in _brains)
            {
                var a = _battle.Get(id);
                if (a == null || !a.Alive || a.Gone) { deadIds.Add(id); continue; }
                if (br.Target != 0 && !a.Routed)
                    _targeted[br.Target] = (_targeted.TryGetValue(br.Target, out var n) ? n : 0) + 1;
            }
            foreach (var id in deadIds) _brains.Remove(id);

            // 收集来袭射弹（按威胁队伍分）
            _flying[0].Clear(); _flying[1].Clear();
            foreach (var p in _battle.Projectiles)
                if (!p.Stuck) _flying[1 - p.Team].Add(p);

            // 收集无主战马
            _looseHorses.Clear();
            foreach (var o in _battle.Agents)
                if (o.LooseMount != null && o.LooseMount.Speed <= 1.5)
                    _looseHorses.Add((o.LooseMount.X, o.LooseMount.Z, o.LooseMount.Speed));

            // 指挥官每 COMMAND_EVERY 秒发令
            for (int team = 0; team <= 1; team++)
            {
                _commandIn[team] -= dt;
                if (_commandIn[team] > 0) continue;
                _commandIn[team] += CommandEvery;
                Command(team);
                AssignSlots(team);
            }

            // 每个 Agent 逐帧决策
            foreach (var a in _battle.Agents)
            {
                if (!a.Alive || a.Gone || a.IsPlayer) continue;
                Think(a, dt);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 指挥官逻辑
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 计算点集重心（对应 TS centroid）。
        /// </summary>
        (double X, double Z) Centroid(IReadOnlyList<Agent> list)
        {
            double x = 0, z = 0;
            foreach (var a in list) { x += a.X; z += a.Z; }
            int n = Math.Max(1, list.Count);
            return (x / n, z / n);
        }

        (double X, double Z) Centroid(IReadOnlyList<(double X, double Z)> list)
        {
            double x = 0, z = 0;
            foreach (var p in list) { x += p.X; z += p.Z; }
            int n = Math.Max(1, list.Count);
            return (x / n, z / n);
        }

        /// <summary>从我方质心朝向敌方的方向向量（对应 TS towardEnemy）。</summary>
        (double Dx, double Dz) TowardEnemy(int team, double cx, double cz)
        {
            var alive = _battle.Alive(1 - team);
            var (ex, ez) = Centroid(alive);
            return (ex - cx, ez - cz);
        }

        /// <summary>
        /// 指挥官每 COMMAND_EVERY 秒为本队所有组分配命令和阵型
        /// （对应 TS command，忠实移植 Bannerlord 指挥 AI）。
        /// </summary>
        void Command(int team)
        {
            var mine    = new List<Agent>();
            foreach (var a in _battle.Alive(team)) if (!a.Routed) mine.Add(a);
            var foes    = new List<Agent>();
            foreach (var a in _battle.Alive(1 - team)) if (!a.Routed) foes.Add(a);
            var (ex, ez) = Centroid(foes);
            var player   = mine.Find(a => a.IsPlayer);
            int foeCav   = foes.Count(f => Agent.Mounted(f));
            int foeRanged = foes.Count(f => f.Troop.Role == Role.Archer || f.Troop.Role == Role.HorseArcher);
            int myRanged  = mine.Count(f => f.Troop.Role == Role.Archer || f.Troop.Role == Role.HorseArcher);
            var infMembers = Members(team, Groups.Inf);
            var (infCx, infCz) = Centroid(infMembers);

            foreach (var g in Groups.All)
            {
                var set     = OrdersMap[team][g];
                var eff     = _effective[team][g];
                var members = mine.Where(a => Groups.GroupOf(a.Troop.Role) == g && !a.IsPlayer).ToList();
                var chosen  = ArrangementMap[team][g];

                // follow 命令：玩家阵亡时转为 auto
                if (set.Order == Orders.Follow && player == null)
                {
                    OrdersMap[team][g].Order = Orders.Auto;
                    eff.Order = Orders.Auto;
                }
                else if (set.Order == Orders.Follow && player != null)
                {
                    eff.Order = Orders.Follow; eff.X = player.X; eff.Z = player.Z; eff.Yaw = player.Yaw;
                    CurrentMap[team][g] = chosen ?? DefaultArrangement(g);
                    TacticMap[team][g] = "";
                    continue;
                }

                // 部署阶段
                if (_battle.Deploying && set.Order == Orders.Auto)
                {
                    var cx0 = members.Count > 0 ? Centroid(members).X : Centroid(mine).X;
                    var cz0 = members.Count > 0 ? Centroid(members).Z : Centroid(mine).Z;
                    if (eff.Order != Orders.Hold)
                    {
                        eff.Order = Orders.Hold; eff.X = cx0; eff.Z = cz0;
                        eff.Yaw = Agent.YawTo(ex - cx0, ez - cz0);
                    }
                    CurrentMap[team][g] = chosen ?? DefaultArrangement(g);
                    TacticMap[team][g] = "部署";
                    continue;
                }

                // 非 auto 命令：直接透传
                if (set.Order != Orders.Auto)
                {
                    eff.Order = set.Order; eff.X = set.X; eff.Z = set.Z; eff.Yaw = set.Yaw;
                    CurrentMap[team][g] = chosen ?? DefaultArrangement(g);
                    TacticMap[team][g] = "";
                    continue;
                }
                if (members.Count == 0) continue;

                // 攻城分支（siege 始终 false，以下代码不执行）
                if (_battle.Siege)
                {
                    var sw = _battle.Terrain.Wall!;
                    if (team == 0)
                    {
                        if ((g == Groups.Arch || g == Groups.Ha) && members.Any(m => m.Arrows > 0))
                        {
                            var (cx0, cz0) = Centroid(members);
                            if (eff.Order != Orders.Hold)
                            { eff.Order = Orders.Hold; eff.X = cx0; eff.Z = sw.Z - sw.Inside * 30; eff.Yaw = Agent.YawTo(0, sw.Inside); }
                            TacticMap[team][g] = "压制城墙";
                            CurrentMap[team][g] = chosen ?? Arrangement.Loose;
                        }
                        else
                        {
                            eff.Order = Orders.Charge;
                            TacticMap[team][g] = sw.GateClosed ? "架梯登城 / 破门" : "从城门突入";
                            CurrentMap[team][g] = chosen ?? Arrangement.Line;
                        }
                    }
                    else
                    {
                        bool breached = _battle.Agents.Any(o => o.Alive && o.Team == 0
                            && ((o.Z - sw.Z) * sw.Inside > -sw.Thick / 2 || o.Y > 1));
                        if (g == Groups.Arch || g == Groups.Ha || breached)
                        {
                            eff.Order = Orders.Charge;
                            TacticMap[team][g] = (g == Groups.Arch || g == Groups.Ha) ? "城头射击" : "反击登城之敌";
                        }
                        else
                        {
                            eff.Order = Orders.Hold; eff.X = 0;
                            eff.Z = sw.Z + sw.Inside * (sw.Thick / 2 + 4);
                            eff.Yaw = Agent.YawTo(0, -sw.Inside);
                            TacticMap[team][g] = "守在城门后";
                        }
                        CurrentMap[team][g] = chosen ?? (g == Groups.Inf ? Arrangement.Line : Arrangement.Loose);
                    }
                    continue;
                }

                // 野战 auto 指挥
                var (cx, cz) = Centroid(members);
                double toEnemyYaw = Agent.YawTo(ex - cx, ez - cz);
                var arrangement = DefaultArrangement(g);
                string tactic = "";

                if (g == Groups.Inf)
                {
                    double arrowRatio = members.Count > 0
                        ? (double)members.Count(m => ArrowsIncoming(m)) / members.Count : 0;
                    var footFoes  = foes.Where(f => !Agent.Mounted(f)).ToList();
                    bool footClose = members.Any(m => FoeWithin(m, 7, f => !Agent.Mounted(f)));
                    var (fsx, fsz) = footFoes.Count > 0 ? Centroid(footFoes) : (ex, ez);
                    bool cavThreat = foeCav >= Math.Max(4, foes.Count * 0.2);
                    bool cavNear   = foes.Any(f => Agent.Mounted(f) && f.Troop.Role == Role.Cavalry
                                        && Math.Sqrt((f.X - cx) * (f.X - cx) + (f.Z - cz) * (f.Z - cz)) < 30);
                    if (eff.Order != Orders.Charge && (footClose || (myRanged < foeRanged * 0.6 && _battle.Time > 40)))
                        eff.Order = Orders.Charge;
                    if (eff.Order == Orders.Charge)
                    {
                        tactic = "冲锋";
                    }
                    else if (cavThreat && cavNear)
                    {
                        arrangement = Arrangement.Square;
                        tactic = "方阵防骑";
                        eff.Order = Orders.Hold; eff.X = cx; eff.Z = cz; eff.Yaw = toEnemyYaw;
                    }
                    else
                    {
                        arrangement = arrowRatio > 0.15 ? Arrangement.Shieldwall : Arrangement.Line;
                        tactic = arrangement == Arrangement.Shieldwall ? "盾墙推进" : "列阵推进";
                        double toFoot = Agent.YawTo(fsx - cx, fsz - cz);
                        double step   = Math.Min(10, Math.Sqrt((fsx - cx) * (fsx - cx) + (fsz - cz) * (fsz - cz)));
                        eff.Order = Orders.Advance;
                        eff.X = cx + Agent.ForwardX(toFoot) * step;
                        eff.Z = cz + Agent.ForwardZ(toFoot) * step;
                        eff.Yaw = toFoot;
                    }
                }
                else if (g == Groups.Arch)
                {
                    bool empty     = members.All(m => m.Arrows == 0);
                    bool threatened = foes.Any(f =>
                        Math.Sqrt((f.X - cx) * (f.X - cx) + (f.Z - cz) * (f.Z - cz)) < 14
                        && !EngagedWith(f, team)
                        && f.Troop.Role != Role.Archer);
                    if (eff.Order != Orders.Charge && (empty || foes.Count == 0)) eff.Order = Orders.Charge;
                    if (eff.Order == Orders.Charge)
                    {
                        tactic = "加入近战";
                    }
                    else if (threatened && infMembers.Count > 0)
                    {
                        double backYaw = Agent.YawTo(ex - infCx, ez - infCz);
                        eff.Order = Orders.Hold;
                        eff.X = infCx - Agent.ForwardX(backYaw) * 10;
                        eff.Z = infCz - Agent.ForwardZ(backYaw) * 10;
                        eff.Yaw = backYaw;
                        tactic = "退到步兵后方";
                    }
                    else
                    {
                        if (eff.Order != Orders.Hold) { eff.Order = Orders.Hold; eff.X = cx; eff.Z = cz; eff.Yaw = toEnemyYaw; }
                        tactic = "散阵射击";
                    }
                }
                else if (g == Groups.Cav)
                {
                    var rangedFoes  = foes.Where(f => f.Troop.Role == Role.Archer).ToList();
                    var engaged     = foes.Where(f => EngagedWith(f, team)).ToList();
                    if (_battle.Time < 4)
                    {
                        eff.Order = Orders.Hold; eff.X = cx; eff.Z = cz; eff.Yaw = toEnemyYaw;
                        tactic = "待命";
                    }
                    else if (rangedFoes.Count > 0 && _battle.Time < 40)
                    {
                        var (rcx, rcz) = Centroid(rangedFoes);
                        double side  = (double)Math.Sign(cx - rcx == 0 ? 1 : cx - rcx);
                        double flankX = rcx + side * 20, flankZ = rcz;
                        if (Math.Sqrt((cx - flankX) * (cx - flankX) + (cz - flankZ) * (cz - flankZ)) > 12
                            && eff.Order != Orders.Charge)
                        {
                            eff.Order = Orders.Hold; eff.X = flankX; eff.Z = flankZ;
                            eff.Yaw = Agent.YawTo(rcx - flankX, rcz - flankZ);
                            arrangement = Arrangement.Skein;
                            tactic = "侧翼迂回";
                        }
                        else
                        {
                            eff.Order = Orders.Charge;
                            tactic = "冲击弓手";
                        }
                    }
                    else
                    {
                        eff.Order = Orders.Charge;
                        tactic = engaged.Count > 0 ? "冲击缠斗中的敌人" : "冲锋";
                    }
                }
                else // ha
                {
                    bool empty = members.All(m => m.Arrows == 0);
                    if (empty)
                    {
                        eff.Order = Orders.Charge;
                        tactic = "箭尽冲锋";
                    }
                    else
                    {
                        eff.Order = Orders.Advance; eff.X = ex; eff.Z = ez; eff.Yaw = toEnemyYaw;
                        tactic = "游射";
                    }
                }

                CurrentMap[team][g] = chosen ?? arrangement;
                TacticMap[team][g] = tactic;
            }
        }

        /// <summary>默认阵型（对应 TS DEFAULT_ARRANGEMENT）。</summary>
        static Arrangement DefaultArrangement(string g) =>
            g == Groups.Arch || g == Groups.Ha ? Arrangement.Loose : Arrangement.Line;

        /// <summary>某个敌人是否已被我方包围（对应 TS engagedWith）。</summary>
        bool EngagedWith(Agent e, int team) => FriendsNear(e, team, 3) > 0;

        /// <summary>半径 r 内是否有敌人（满足 ok 条件）（对应 TS foeWithin）。</summary>
        bool FoeWithin(Agent a, double r, Func<Agent, bool>? ok = null)
        {
            foreach (var f in _battle.Grid.Near(a.X, a.Z, r))
            {
                if (f.Team == a.Team || !f.Alive || f.Gone || f.Routed) continue;
                if (Math.Sqrt((f.X - a.X) * (f.X - a.X) + (f.Z - a.Z) * (f.Z - a.Z)) < r
                    && (ok == null || ok(f))) return true;
            }
            return false;
        }

        // ── 参考帧 ───────────────────────────────────────────────────

        /// <summary>
        /// 战斗开始时的阵型参考帧（按兵数加权的块中心 + 朝向敌方角度）。
        /// 对应 TS groupRef。
        /// </summary>
        (double X, double Z, double Yaw) GroupRef(int team, string g, IReadOnlyList<BlockPlan> blocks)
        {
            string key = $"{team}{g}";
            if (_refs.TryGetValue(key, out var r)) return r;
            double x = 0, z = 0, n = 0;
            foreach (var b in blocks) { x += b.X * b.Count; z += b.Z * b.Count; n += b.Count; }
            x /= Math.Max(1, n); z /= Math.Max(1, n);
            var (ex, ez) = Centroid(_battle.Alive(1 - team));
            r = (x, z, Agent.YawTo(ex - x, ez - z));
            _refs[key] = r;
            return r;
        }

        // ── 槽位分配 ─────────────────────────────────────────────────

        /// <summary>
        /// 把本队各组成员分配到阵型槽位（对应 TS assignSlots）。
        /// </summary>
        void AssignSlots(int team)
        {
            var plan = _battle.Plan;
            foreach (var g in Groups.All)
            {
                var eff     = _effective[team][g];
                var members = Members(team, g);
                var kind    = CurrentMap[team][g];
                double moraleMult = FormationData.ArrangementMorale[kind];

                foreach (var m in members)
                {
                    _battle.MoraleMult[m.Id] = eff.Order == Orders.Charge ? 1.0 : moraleMult;
                    _battle.FormationOf[m.Id] = (g, eff.Order == Orders.Charge ? Arrangement.Line : kind);
                }

                if (eff.Order == Orders.Charge || eff.Order == Orders.Auto || eff.Order == Orders.Retreat
                    || (g == Groups.Ha && !_battle.Deploying && eff.Order != Orders.Hold))
                {
                    foreach (var m in members) GetBrain(m).Slot = null;
                    continue;
                }

                double back = eff.Order == Orders.Follow ? -4.0 : 0;
                double ax = eff.X + Agent.ForwardX(eff.Yaw) * back;
                double az = eff.Z + Agent.ForwardZ(eff.Yaw) * back;

                var rest = members;              // TS: let rest = members
                if (plan != null && eff.Order != Orders.Follow)
                {
                    rest = new List<Agent>();    // TS: rest = []
                    var byBlock = new Dictionary<int, List<Agent>>();
                    foreach (var m in members)
                    {
                        BlockPlan? bp = m.FormationBlock >= 0 ? plan.Find(p => p.Id == m.FormationBlock) : null;
                        if (bp == null) { rest.Add(m); continue; }
                        if (!byBlock.TryGetValue(bp.Id, out var l)) byBlock[bp.Id] = l = new();
                        l.Add(m);
                    }
                    var blocks = plan.Where(b => b.Team == team && byBlock.ContainsKey(b.Id)).ToList();
                    (double X, double Z, double Yaw)? refFrame = blocks.Count > 0 && !_battle.Deploying
                        ? (GroupRef(team, g, blocks)) : null;

                    foreach (var b in blocks)
                    {
                        var list = byBlock[b.Id];
                        double bxp = b.X, bzp = b.Z;
                        double byawp = b.Yaw;
                        if (refFrame.HasValue)
                        {
                            var rf = refFrame.Value;
                            double dx = b.X - rf.X, dz = b.Z - rf.Z;
                            double rr  = dx * Math.Cos(rf.Yaw) - dz * Math.Sin(rf.Yaw);
                            double ff  = dx * Agent.ForwardX(rf.Yaw) + dz * Agent.ForwardZ(rf.Yaw);
                            bxp   = ax + Math.Cos(eff.Yaw) * rr + Agent.ForwardX(eff.Yaw) * ff;
                            bzp   = az + (-Math.Sin(eff.Yaw)) * rr + Agent.ForwardZ(eff.Yaw) * ff;
                            byawp = b.Yaw + Agent.AngleDiff(eff.Yaw, rf.Yaw);
                        }
                        var bk = ArrangementMap[team][g] ?? ParseArrangement(b.Arrangement) ?? kind;
                        foreach (var m in list)
                        {
                            _battle.MoraleMult[m.Id] = FormationData.ArrangementMorale[bk];
                            _battle.FormationOf[m.Id] = (g, bk);
                        }
                        // 直接调用 Formation.Arrange，绕过 Deploy.BlockSlots（后者的 kind 参数与 Arrangement 不兼容）
                        bool isCav = g == Groups.Cav || g == Groups.Ha;
                        Fill(list, Formation.Arrange(bk, list.Count, bxp, bzp, byawp,
                            (isCav ? 3.6 : 1.7) * SpacingMap[team][g], isCav, b.Files));
                    }
                }

                if (rest.Count == 0) continue;
                bool cav = g == Groups.Cav;
                double spacing = (cav ? 3.6 : 1.7) * SpacingMap[team][g];
                Fill(rest, Formation.Arrange(kind, rest.Count, ax, az, eff.Yaw, spacing, cav));
            }
        }

        /// <summary>解析阵型字符串（BlockPlan.Arrangement 可为 null）。</summary>
        static Arrangement? ParseArrangement(string? s) => s switch
        {
            "line"       => Arrangement.Line,
            "shieldwall" => Arrangement.Shieldwall,
            "loose"      => Arrangement.Loose,
            "circle"     => Arrangement.Circle,
            "square"     => Arrangement.Square,
            "skein"      => Arrangement.Skein,
            "column"     => Arrangement.Column,
            _            => null,
        };

        /// <summary>
        /// 把 members 分配到 slots（持盾在前，持矛次之，各取最近空位）。
        /// 使用 LINQ OrderBy 保证稳定排序（对应 TS fill）。
        /// </summary>
        void Fill(List<Agent> members, List<Slot> slots)
        {
            var sorted = members.OrderBy(p => Rank(p)).ToList();
            var free   = new List<Slot>(slots);
            int frontCount = slots.Count(s => s.Front);
            int[] left = { slots.Count - frontCount, frontCount };

            for (int idx = 0; idx < sorted.Count; idx++)
            {
                var m = sorted[idx];
                bool wantFront = idx < frontCount;
                bool strict    = left[wantFront ? 1 : 0] > 0;
                int best = -1;
                double bd = double.PositiveInfinity;
                for (int k = 0; k < free.Count; k++)
                {
                    if (strict && free[k].Front != wantFront) continue;
                    double d = Math.Sqrt((free[k].X - m.X) * (free[k].X - m.X) + (free[k].Z - m.Z) * (free[k].Z - m.Z));
                    if (d < bd) { bd = d; best = k; }
                }
                if (best < 0) best = 0;
                if (best >= free.Count) break;
                var s0 = free[best];
                GetBrain(m).Slot = s0;
                if (s0.Front) left[1]--; else left[0]--;
                free[best] = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
            }
        }

        /// <summary>排序键：持盾 0，持矛 1，其余 2（对应 TS function rank）。</summary>
        static int Rank(Agent a)
        {
            if (a.Troop.Shield != null) return 0;
            if (a.Troop.Role == Role.Spear)  return 1;
            return 2;
        }

        // ══════════════════════════════════════════════════════════════
        // Agent 行为
        // ══════════════════════════════════════════════════════════════

        /// <summary>每帧为单个 Agent 计算输入（对应 TS think）。</summary>
        void Think(Agent a, double dt)
        {
            var b = GetBrain(a);
            double face = a.Input.Face;
            a.Input = InputFactory.Empty(face);

            b.AttackCooldown -= dt;
            b.Retarget       -= dt;
            b.StrafeTimer    -= dt;
            if (b.StrafeTimer <= 0)
            {
                b.Strafe      = _battle.Random() < 0.5 ? -1.0 : 1.0;
                b.StrafeTimer = 0.8 + _battle.Random() * 1.6;
            }

            var order = _effective[a.Team][Groups.GroupOf(a.Troop.Role)];
            if (_battle.Deploying)
            {
                if (b.Slot != null && Agent.Mounted(a)) Ride(a, b, null, order);
                else if (b.Slot != null) KeepSlot(a, b, order);
                return;
            }
            a.Retreating = order.Order == Orders.Retreat;

            // 正在爬梯
            var lw = _battle.Terrain.Wall;
            if (a.Climbing && lw != null)
            {
                bool down = a.Routed || a.Retreating;
                a.Input.MoveX = 0;
                a.Input.MoveZ = (down ? -1.0 : 1.0) * lw.Inside;
                a.Input.Face  = Agent.YawTo(0, lw.Inside);
                return;
            }
            if (a.Routed || a.Retreating) { Flee(a); return; }

            var target = PickTarget(a, b, order);

            if (Agent.Mounted(a) && a.Troop.Role == Role.HorseArcher
                && a.Arrows > 0 && !HoldFireMap[a.Team][Groups.Ha] && target != null)
            {
                Skirmish(a, b, target); return;
            }
            if (Agent.Mounted(a)) { Ride(a, b, target, order); return; }

            // 守城弓手上城墙
            var wall = _battle.Terrain.Wall;
            if (wall != null && _battle.Siege && a.Team == 1 && a.Troop.Role == Role.Archer
                && a.Arrows > 0 && a.Y < 1 && (a.Z - wall.Z) * wall.Inside > wall.Thick / 2)
            {
                if (LevelRoute(a, new AgentPos { X = a.X, Y = wall.Height, Z = wall.Z })) return;
            }
            if (a.Troop.Role == Role.Archer && !HoldFireMap[a.Team][Groups.Arch]
                && Archer(a, b, target, order)) return;
            if (Agent.WeaponOf(a).Brace && Brace(a)) return;
            if (a.Troop.Mount != null && !Agent.Mounted(a) && Remount(a)) return;

            bool holding = order.Order != Orders.Charge && order.Order != Orders.Auto && b.Slot != null;
            var curWpn = Agent.WeaponOf(a);
            if (WeaponsData.Weapons.TryGetValue(a.Troop.Weapons[a.WeaponIdx], out var heldW)
                && heldW.Cls == WeaponClass.Thrown && target != null
                && ThrowJavelin(a, b, target, holding))
            {
                if (holding) KeepSlotFeet(a, b, order);
                return;
            }

            var kind = CurrentMap[a.Team][Groups.GroupOf(a.Troop.Role)];
            bool holdsGround = kind == Arrangement.Shieldwall || kind == Arrangement.Square || kind == Arrangement.Circle;
            double engageRange = order.Order == Orders.Charge ? double.PositiveInfinity
                : holdsGround ? 3.2 : order.Order == Orders.Hold ? 7.0 : 9.0;
            double dist = target != null
                ? Math.Sqrt((target.X - a.X) * (target.X - a.X) + (target.Z - a.Z) * (target.Z - a.Z))
                : double.PositiveInfinity;

            if (_battle.Siege && target != null && !holding
                && (SiegeRoute(a, target) || LevelRoute(a, target))) return;
            if (b.Slot != null && (dist > engageRange || target == null)) { KeepSlot(a, b, order); return; }
            if (target != null) Melee(a, b, target, dt);
        }

        // ── 逃跑 ──────────────────────────────────────────────────────

        void Flee(Agent a)
        {
            double half = _battle.Terrain.Half;
            double edge = a.Team == 0 ? half + 20 : -half - 20;
            if (Agent.Mounted(a))
            {
                double want = Agent.YawTo(0, edge - a.Z);
                a.Input.Steer    = Math.Max(-1, Math.Min(1, Agent.AngleDiff(want, a.Yaw) * 2));
                a.Input.Throttle = 1;
                return;
            }
            double dz = edge - a.Z;
            a.Input.MoveX = 0;
            a.Input.MoveZ = (double)Math.Sign(dz);
            a.Input.Face  = Agent.YawTo(0, dz);
        }

        // ── 目标选择 ──────────────────────────────────────────────────

        Agent? PickTarget(Agent a, Brain b, GroupOrder order)
        {
            var cur = _battle.Get(b.Target);
            if (cur != null && cur.Alive && !cur.Gone && b.Retarget > 0) return cur;
            b.Retarget = 0.6 + _battle.Random() * 0.6;
            double anchorX = order.Order == Orders.Hold ? order.X : a.X;
            double anchorZ = order.Order == Orders.Hold ? order.Z : a.Z;
            var wCls = Agent.WeaponOf(a).Cls;
            bool bow = wCls == WeaponClass.Bow || wCls == WeaponClass.Crossbow;
            IEnumerable<Agent> pool = _battle.Agents;
            if (!bow && a.Troop.Role != Role.Cavalry && a.Troop.Role != Role.Archer && a.Troop.Role != Role.HorseArcher)
            {
                var near = _battle.Grid.Near(a.X, a.Z, MeleeSearch);
                if (near.Any(e => e.Team != a.Team && e.Alive && !e.Gone)) pool = near;
            }

            var shortlist = new List<(Agent e, double score)>();
            Agent? best = null;
            double bestScore = double.PositiveInfinity;

            foreach (var e in pool)
            {
                if (!e.Alive || e.Gone || e.Team == a.Team) continue;
                double d     = Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Z - a.Z) * (e.Z - a.Z));
                int crowd    = (_targeted.TryGetValue(e.Id, out var tc) ? tc : 0) - (b.Target == e.Id ? 1 : 0);
                double score = d + crowd * 2.2 + (e.Routed ? 6 : 0)
                               + (e.Action.Kind == "down" ? -3 : 0)
                               + Math.Sqrt((e.X - anchorX) * (e.X - anchorX) + (e.Z - anchorZ) * (e.Z - anchorZ)) * 0.2;

                var wl = _battle.Terrain.Wall;
                if (wl != null && _battle.Siege && wl.GateClosed && a.Y < 1 && e.Y < 1
                    && wCls != WeaponClass.Bow && wCls != WeaponClass.Crossbow)
                {
                    double sideA = Math.Sign((a.Z - wl.Z) * wl.Inside);
                    double sideE = Math.Sign((e.Z - wl.Z) * wl.Inside);
                    if (sideA != sideE && a.Team == 1) score += 40;
                }
                if (_battle.Siege && wCls != WeaponClass.Bow && wCls != WeaponClass.Crossbow
                    && Math.Abs(e.Y - a.Y) > 1.5) score += 20;
                if (a.Troop.Role == Role.Cavalry)
                {
                    if (e.Troop.Role == Role.Archer)     score -= 12;
                    if (e.Troop.Role == Role.Spear && !Agent.Mounted(e)) score += 25;
                    if (e.Troop.Role == Role.HorseArcher && Agent.Mounted(e) && d > 12) score += 30;
                }
                if (a.Troop.Role == Role.Archer && wCls == WeaponClass.Bow)
                {
                    if (shortlist.Count < 8 || score < shortlist[shortlist.Count - 1].score)
                    {
                        shortlist.Add((e, score));
                        shortlist.Sort((p, q) => p.score.CompareTo(q.score));
                        if (shortlist.Count > 8) shortlist.RemoveAt(shortlist.Count - 1);
                    }
                }
                else if (score < bestScore) { bestScore = score; best = e; }
            }

            foreach (var (e, sc) in shortlist)
            {
                double adj = sc + FriendsNear(e, a.Team, 2.2) * 15;
                if (adj < bestScore) { bestScore = adj; best = e; }
            }
            b.Target = best?.Id ?? 0;
            if (best != null && Math.Sqrt((best.X - a.X) * (best.X - a.X) + (best.Z - a.Z) * (best.Z - a.Z)) > FarTarget)
                b.Retarget += 1;
            return best;
        }

        // ── 保持槽位 ─────────────────────────────────────────────────

        /// <summary>在投矛时不修改朝向和攻击输入（对应 TS keepSlotFeet）。</summary>
        void KeepSlotFeet(Agent a, Brain b, GroupOrder order)
        {
            double face   = a.Input.Face;
            Dir?   attack = a.Input.Attack;
            bool   held   = a.Input.AttackHeld;
            var    aim    = a.Input.Aim;
            bool   alt    = a.Input.Alt;
            KeepSlot(a, b, order);
            a.Input.Face        = face;
            a.Input.Attack      = attack;
            a.Input.AttackHeld  = held;
            a.Input.Aim         = aim;
            a.Input.Alt         = alt;
        }

        void KeepSlot(Agent a, Brain b, GroupOrder order)
        {
            var    s  = b.Slot!.Value;
            double dx = s.X - a.X, dz = s.Z - a.Z;
            double d  = Math.Sqrt(dx * dx + dz * dz);
            var kind  = CurrentMap[a.Team][Groups.GroupOf(a.Troop.Role)];
            if (d > 0.4)
            {
                double pace = d < 6 ? FormationData.ArrangementPace[kind] : 1.0;
                double k    = (Math.Min(1, d / 1.5) / d) * pace;
                a.Input.MoveX = dx * k;
                a.Input.MoveZ = dz * k;
                a.Input.Walk  = d < 6 && order.Order != Orders.Follow;
                a.Input.Face  = d > 2 ? Agent.YawTo(dx, dz) : s.Yaw;
            }
            else a.Input.Face = s.Yaw;
            if (Agent.HasShield(a) && (kind == Arrangement.Shieldwall || ArrowsIncoming(a)))
                a.Input.Block = Dir.Up;
        }

        // ── 箭矢检测 ─────────────────────────────────────────────────

        bool ArrowsIncoming(Agent a)
        {
            var b = GetBrain(a);
            if (_battle.Time - b.ArrowsAt < ArrowLook && b.ArrowsAt >= 0) return b.Arrows;
            b.ArrowsAt = _battle.Time;
            b.Arrows   = false;
            foreach (var p in _flying[a.Team])
            {
                double dx = a.X - p.X, dz = a.Z - p.Z;
                double d  = Math.Sqrt(dx * dx + dz * dz);
                if (d < 25)
                {
                    double dot  = (p.Vx * dx + p.Vz * dz);
                    double vMag = Math.Sqrt(p.Vx * p.Vx + p.Vz * p.Vz);
                    if (vMag > 0 && dot / (d * vMag) > 0.9) { b.Arrows = true; break; }
                }
            }
            return b.Arrows;
        }

        // ── 攻城路线 ─────────────────────────────────────────────────

        bool SiegeRoute(Agent a, Agent t)
        {
            var w = _battle.Terrain.Wall;
            if (w == null || Agent.Mounted(a)) return false;
            double outer   = w.Z - w.Inside * (w.Thick / 2);
            bool outside   = (a.Z - outer) * w.Inside < 0.5 && a.Y < 0.5;
            if (!outside) return false;
            bool tBeyond   = (t.Z - outer) * w.Inside > -0.2 || t.Y > 1;
            if (!tBeyond) return false;
            double gx, gz;
            if (!w.GateClosed)
            {
                gx = Math.Max(-w.Gate + 0.8, Math.Min(w.Gate - 0.8, a.X));
                bool lined = Math.Abs(a.X) < w.Gate - 0.4;
                gz = lined ? outer + w.Inside * 3 : outer - w.Inside * 3;
            }
            else
            {
                var wpn = Agent.WeaponOf(a);
                bool gateMan = a.Team == 0
                    && (wpn.SwingType == DamageType.Blunt || (wpn.ShieldBonus ?? 1.0) > 1)
                    && (a.Id % 3 == 0);
                if (gateMan)
                {
                    gx = Math.Max(-w.Gate + 0.6, Math.Min(w.Gate - 0.6, a.X));
                    gz = outer - w.Inside * 0.8;
                    if (Math.Sqrt((gx - a.X) * (gx - a.X) + (gz - a.Z) * (gz - a.Z)) < 1.4)
                    {
                        a.Input.Face = Agent.YawTo(0, w.Inside);
                        if (Agent.CanAct(a) && a.Action.Kind != "attack")
                        { a.Input.Attack = Dir.Up; a.Input.AttackHeld = false; }
                        return true;
                    }
                }
                else
                {
                    var br = GetBrain(a);
                    double scoreL(double lx) =>
                        Math.Abs(lx - a.X) + ((_ladderCrowd.TryGetValue(lx, out var lc) ? lc : 0)
                        - (AtFoot(a, lx, outer) ? 1 : 0)) * 4;
                    double initBest = br.Ladder ?? _battle.Ladders[0];
                    double bs  = scoreL(initBest) - (br.Ladder != null ? 6 : 0);
                    foreach (var lx in _battle.Ladders) { double sc = scoreL(lx); if (sc < bs) { bs = sc; initBest = lx; } }
                    br.Ladder = initBest;
                    gx = initBest;
                    gz = Math.Abs(a.X - initBest) < 0.6 ? outer + w.Inside * 2 : outer - w.Inside * 1.2;
                }
            }
            double ddx = gx - a.X, ddz = gz - a.Z;
            double dd  = Math.Sqrt(ddx * ddx + ddz * ddz) == 0 ? 1 : Math.Sqrt(ddx * ddx + ddz * ddz);
            a.Input.MoveX = ddx / dd; a.Input.MoveZ = ddz / dd;
            a.Input.Face  = Agent.YawTo(ddx, ddz);
            if (Agent.HasShield(a) && ArrowsIncoming(a)) a.Input.Block = Dir.Up;
            return true;
        }

        bool AtFoot(Agent o, double lx, double outer) =>
            Math.Abs(o.X - lx) < 1.5 && Math.Abs(o.Z - outer) < 3;

        // 重载：接受 Agent 目标（Agent 未直接实现 IAgentPos）
        bool LevelRoute(Agent a, Agent t) => LevelRoute(a, new AgentPos { X = t.X, Y = t.Y, Z = t.Z });

        bool LevelRoute(Agent a, IAgentPos t)
        {
            var w = _battle.Terrain.Wall;
            if (w == null || Agent.Mounted(a) || a.Climbing) return false;
            if (Math.Abs(a.Y - t.Y) < 1.5) return false;
            double inner(double z) => (z - w.Z) * w.Inside;
            if (inner(a.Z) < -w.Thick / 2 && a.Y < 1) return false;
            double ramp = w.Ramps.Aggregate((best, rx) => Math.Abs(rx - a.X) < Math.Abs(best - a.X) ? rx : best);
            var top  = (X: ramp, Z: w.Z + w.Inside * (w.Thick / 2 - 0.6));
            var foot = (X: ramp, Z: w.Z + w.Inside * (w.Thick / 2 + w.RampLen + 1.5));
            bool onWalkway = inner(a.Z) <= w.Thick / 2 && a.Y > w.Height - 0.5;
            bool onRamp    = Math.Abs(a.X - ramp) < 1.6 && inner(a.Z) > w.Thick / 2 - 0.5
                          && inner(a.Z) < w.Thick / 2 + w.RampLen + 1;
            (double X, double Z) goal;
            if (t.Y < a.Y)
            {
                goal = onWalkway && Math.Abs(a.X - ramp) > 1.2 ? (ramp, w.Z) : foot;
                if (!onWalkway && !onRamp) return false;
            }
            else
            {
                goal = onRamp ? (inner(a.Z) <= w.Thick / 2 + 0.3 ? (t.X, w.Z) : top) : foot;
            }
            double gxl = goal.X - a.X, gzl = goal.Z - a.Z;
            double dl   = Math.Sqrt(gxl * gxl + gzl * gzl) == 0 ? 1 : Math.Sqrt(gxl * gxl + gzl * gzl);
            a.Input.MoveX = gxl / dl; a.Input.MoveZ = gzl / dl;
            a.Input.Face  = Agent.YawTo(gxl, gzl);
            if (Agent.HasShield(a) && ArrowsIncoming(a)) a.Input.Block = Dir.Up;
            return true;
        }

        /// <summary>武器有效距离（中心到剑尖）。</summary>
        double Reach(Agent a) => Agent.WeaponOf(a).Length + 0.95;

        // ── 近战 ──────────────────────────────────────────────────────

        void Melee(Agent a, Brain b, Agent t, double dt)
        {
            var w = Agent.WeaponOf(a);
            if (Defs.IsRanged(w.Cls))
            {
                a.Input.Swap = Array.FindIndex(a.Troop.Weapons, id => !Defs.IsRanged(WeaponsData.Weapons[id].Cls));
                return;
            }
            double dx = t.X - a.X, dz = t.Z - a.Z;
            double d  = Math.Sqrt(dx * dx + dz * dz);
            if (w.Length > 1.8 && d < 1.3 && Agent.CanAct(a) && a.Troop.Weapons.Length > 1 && !Agent.Mounted(a))
            {
                int side = Array.FindIndex(a.Troop.Weapons, id => WeaponsData.Weapons[id].Length < 1.8 && !Defs.IsRanged(WeaponsData.Weapons[id].Cls));
                if (side >= 0) { a.Input.Swap = side; return; }
            }
            double reach = Reach(a);
            double want  = reach * 0.78 + (Agent.Mounted(t) ? 0.6 : 0);
            a.Input.Face = Agent.YawTo(dx, dz);
            double ux = dx / (d == 0 ? 1 : d), uz = dz / (d == 0 ? 1 : d);
            double mx = 0, mz = 0;
            if (d > want + 0.35)       { mx = ux; mz = uz; }
            else if (d < want - 0.5)   { mx = -ux * 0.8; mz = -uz * 0.8; }
            double circle = d < reach + 2 ? 0.35 * b.Strafe : 0;
            mx += -uz * circle; mz += ux * circle;
            a.Input.MoveX = mx; a.Input.MoveZ = mz;
            a.Input.Walk  = d < 5;

            var threat = Threat(a);
            if (threat != null)
            {
                string key = $"{threat.Value.Id}:{threat.Value.Dir}";
                if (b.ThreatKey != key)
                {
                    b.ThreatKey = key;
                    double sk  = a.Troop.Skill;
                    double delay = 0.12 + (1 - sk) * 0.4 + _battle.Random() * 0.15;
                    b.BlockAt = _battle.Time + delay;
                    if (_battle.Random() > 0.45 + sk * 0.5) b.BlockAt = double.PositiveInfinity;
                    bool right = _battle.Random() < 0.3 + 0.65 * sk;
                    b.BlockDir = Agent.HasShield(a) ? Dir.Up : right ? Defs.BlockFor(threat.Value.Dir) : Defs.Dirs[(int)(_battle.Random() * 4)];
                    b.Plan = threat.Value.Crush && _battle.Random() < 0.3 + 0.6 * sk ? "evade"
                           : !Agent.HasShield(a) && right && !threat.Value.Crush && _battle.Random() < sk * sk * 0.35 ? "chamber"
                           : "block";
                }
                if (_battle.Time >= b.BlockAt)
                {
                    if (b.Plan == "evade")
                    {
                        a.Input.MoveX = -(t.X - a.X) / (d == 0 ? 1 : d);
                        a.Input.MoveZ = -(t.Z - a.Z) / (d == 0 ? 1 : d);
                        a.Input.Walk  = false;
                        return;
                    }
                    if (b.Plan == "chamber")
                    {
                        if (threat.Value.Released && Agent.CanAct(a))
                        {
                            a.Input.Attack = Defs.BlockFor(threat.Value.Dir);
                            a.Input.AttackHeld = true;
                            b.ReleaseAt = 0.25; b.Plan = "block"; b.BlockAt = double.PositiveInfinity;
                            return;
                        }
                    }
                    else b.BlockUntil = _battle.Time + 0.35;
                }
            }
            else b.ThreatKey = "";

            if (_battle.Time < b.BlockUntil && b.BlockDir.HasValue)
            {
                if (a.Action.Kind != "attack" || !a.Action.Released)
                { a.Input.Block = b.BlockDir; return; }
            }
            if (Agent.HasShield(a) && ArrowsIncoming(a) && d > 6)
            { a.Input.Block = Dir.Up; return; }

            var act = a.Action;
            if (act.Kind == "attack" && !act.Released)
            {
                var tb   = t.Action;
                bool read = tb.Kind == "block" && (Agent.HasShield(t) || tb.Dir == Defs.BlockFor(act.Dir));
                if (read && act.Held > 0.15 && _battle.Random() < a.Troop.Skill * dt * 4)
                { a.Input.Block = act.Dir; b.AttackCooldown = 0.1; return; }
                a.Input.AttackHeld = act.Age < b.ReleaseAt || d > reach + 0.4;
                if (act.Held > 2.2) a.Input.AttackHeld = false;
                return;
            }
            if (!Agent.CanAct(a) || b.AttackCooldown > 0 || d > reach + 0.5) return;
            if (Agent.HasShield(a) && a.Action.Kind == "block" && t.Action.Kind == "attack"
                && !t.Action.Released && d < 1.4 && _battle.Random() < 0.25 + a.Troop.Skill * 0.25)
            { a.Input.Block = a.Action.Dir; a.Input.Bash = true; b.AttackCooldown = 0.5; return; }
            if (Agent.HasShield(t) && t.Action.Kind == "block" && d < 1.6
                && _battle.Random() < 0.08 + a.Troop.Skill * 0.12)
            { a.Input.Kick = true; b.AttackCooldown = 0.6; return; }

            a.Input.Attack     = ChooseDir(a, t);
            a.Input.AttackHeld = true;
            b.ReleaseAt        = 0.55 + (_battle.Random() - 0.5) * (0.15 + 0.6 * (1 - a.Troop.Skill));
            b.AttackCooldown   = 0.35 + _battle.Random() * (1.1 - a.Troop.Skill * 0.6);
        }

        Dir? ChooseDir(Agent a, Agent t)
        {
            var w       = Agent.WeaponOf(a);
            var options = Defs.Dirs.Where(d => d == Dir.Down ? w.Thrust > 0 : w.Swing > 0).ToArray();
            var tb = t.Action;
            if (w.Crush && (tb.Kind == "block" || tb.Kind == "down")) return Dir.Up;
            Dir? avoid = tb.Kind == "block" ? Defs.BlockFor(tb.Dir) : (Dir?)null;
            var pool = avoid.HasValue && _battle.Random() < a.Troop.Skill + 0.2
                ? options.Where(d => d != avoid.Value).ToArray() : options;
            // polearm 偏好刺击——pool 为空时 pool.Contains 返回 false，&&短路，不消耗 PRNG
            if (w.Cls == WeaponClass.Polearm && w.Thrust > w.Swing && pool.Contains(Dir.Down) && _battle.Random() < 0.5)
                return Dir.Down;
            // 与 TS 对称：pool[Math.floor(random() * pool.length)]
            // 当 pool 为空时 TS 返回 undefined（PRNG 仍消耗一次），C# 同步消耗后返回 null
            double pick = _battle.Random();
            return pool.Length > 0 ? (Dir?)pool[(int)(pick * pool.Length)] : null;
        }

        (int Id, Dir Dir, bool Crush, bool Released)? Threat(Agent a)
        {
            (int Id, Dir Dir, bool Crush, bool Released)? best = null;
            double bd = double.PositiveInfinity;
            foreach (var e in _battle.Grid.Near(a.X, a.Z, 5.2))
            {
                if (!e.Alive || e.Gone || e.Team == a.Team) continue;
                var act = e.Action;
                if (act.Kind != "attack" || act.Done) continue;
                if (act.Released && act.T > act.Info.LiveFrom + 0.05) continue;
                double dx = a.X - e.X, dz = a.Z - e.Z;
                double d  = Math.Sqrt(dx * dx + dz * dz);
                if (d > Agent.WeaponOf(e).Length + 2.6) continue;
                if (Math.Abs(Agent.AngleDiff(Agent.YawTo(dx, dz), Agent.AimYaw(e))) > 0.9) continue;
                if (d < bd) { bd = d; best = (e.Id, act.Dir, act.Crush, act.Released); }
            }
            return best;
        }

        // ── 投矛 ──────────────────────────────────────────────────────

        bool ThrowJavelin(Agent a, Brain b, Agent t, bool holding = false)
        {
            double d    = Math.Sqrt((t.X - a.X) * (t.X - a.X) + (t.Z - a.Z) * (t.Z - a.Z));
            var w       = Agent.WeaponOf(a);
            int left    = a.Ammo[a.WeaponIdx];
            if (d < 4 || left == 0)
            {
                if (left == 0 && Agent.CanAct(a))
                    a.Input.Swap = Array.FindIndex(a.Troop.Weapons, id => WeaponsData.Weapons[id].Cls != WeaponClass.Thrown);
                else if (!a.AltUsage && Agent.CanAct(a)) a.Input.Alt = true;
                return false;
            }
            if (a.AltUsage && Agent.CanAct(a) && d > 6) a.Input.Alt = true;
            if (d > 28 || w.Cls != WeaponClass.Thrown) return false;
            double v    = w.MissileSpeed ?? 30;
            double lead = d / v;
            double tx   = t.X + t.Vx * lead, tz = t.Z + t.Vz * lead;
            a.Input.Face = Agent.YawTo(tx - a.X, tz - a.Z);
            a.Input.Aim  = (tx, _battle.OriginY(t) + 1.1, tz);
            bool clear   = FriendsNear(t, a.Team, 1.8) == 0 && ClearShot(a, tx, 1.1, tz, v, false);
            var act = a.Action;
            if (act.Kind == "draw") a.Input.AttackHeld = act.T < b.DrawFor || !clear;
            else if (Agent.CanAct(a) && clear && Math.Abs(Agent.AngleDiff(a.Input.Face, a.Yaw)) < 0.25)
            { a.Input.Attack = Dir.Up; a.Input.AttackHeld = true; b.DrawFor = 0.45 + _battle.Random() * 0.3; }
            if (!holding)
            { a.Input.MoveX = (t.X - a.X) / d * 0.4; a.Input.MoveZ = (t.Z - a.Z) / d * 0.4; a.Input.Walk = true; }
            return true;
        }

        // ── 重新上马 ──────────────────────────────────────────────────

        bool Remount(Agent a)
        {
            (double X, double Z, double Speed) best = default;
            double bd = 12;
            bool found = false;
            foreach (var h in _looseHorses)
            {
                double d = Math.Sqrt((h.X - a.X) * (h.X - a.X) + (h.Z - a.Z) * (h.Z - a.Z));
                if (d < bd) { bd = d; best = h; found = true; }
            }
            if (!found) return false;
            if (FoeWithin(a, 3)) return false;
            if (bd < 2) { if (Agent.CanAct(a)) a.Input.MountToggle = true; }
            else
            {
                a.Input.MoveX = (best.X - a.X) / bd;
                a.Input.MoveZ = (best.Z - a.Z) / bd;
                a.Input.Face  = Agent.YawTo(best.X - a.X, best.Z - a.Z);
            }
            return true;
        }

        // ── 长矛格挡骑兵 ─────────────────────────────────────────────

        bool Brace(Agent a)
        {
            if (!Agent.WeaponOf(a).Brace) return false;
            Agent? horse = null;
            double hd = 16;
            foreach (var e in _battle.Grid.Near(a.X, a.Z, 16))
            {
                if (!e.Alive || e.Gone || e.Team == a.Team || !Agent.Mounted(e)) continue;
                if (e.Mount!.Speed < 6) continue;
                double dx = a.X - e.X, dz = a.Z - e.Z;
                double d  = Math.Sqrt(dx * dx + dz * dz);
                if (d > hd) continue;
                if (Math.Abs(Agent.AngleDiff(Agent.YawTo(dx, dz), e.Yaw)) > 0.5) continue;
                hd = d; horse = e;
            }
            if (horse == null) { if (a.Braced) a.Input.Alt = true; return false; }
            a.Input.Face = Agent.YawTo(horse.X - a.X, horse.Z - a.Z);
            if (!a.Braced && Agent.CanAct(a) && Agent.SpeedOf(a) < 1.5) a.Input.Alt = true;
            return true;
        }

        // ── 弓手 ──────────────────────────────────────────────────────

        bool Archer(Agent a, Brain b, Agent? t, GroupOrder order)
        {
            int bowIdx   = Array.FindIndex(a.Troop.Weapons, id => Defs.IsRanged(WeaponsData.Weapons[id].Cls));
            int meleeIdx = Array.FindIndex(a.Troop.Weapons, id => !Defs.IsRanged(WeaponsData.Weapons[id].Cls));
            double dist  = t != null ? Math.Sqrt((t.X - a.X) * (t.X - a.X) + (t.Z - a.Z) * (t.Z - a.Z)) : double.PositiveInfinity;

            if (_battle.Siege && a.Team == 1 && a.Y > 1 && t != null)
            {
                if (a.Arrows == 0 || (dist < 4 && Math.Abs(t.Y - a.Y) < 1))
                { if (Defs.IsRanged(Agent.WeaponOf(a).Cls) && Agent.CanAct(a)) a.Input.Swap = meleeIdx; return false; }
            }
            else if (t == null || a.Arrows == 0 || dist < 7)
            {
                if (Defs.IsRanged(Agent.WeaponOf(a).Cls) && Agent.CanAct(a) && a.Action.Kind != "swap")
                    a.Input.Swap = meleeIdx;
                return t == null;
            }
            if (dist > 95) return false;
            if (!Defs.IsRanged(Agent.WeaponOf(a).Cls))
            { if (Agent.CanAct(a)) a.Input.Swap = bowIdx; return true; }

            bool cross = Agent.WeaponOf(a).Cls == WeaponClass.Crossbow;
            if (cross && !a.Loaded) { if (Agent.CanAct(a)) a.Input.Attack = Dir.Up; return true; }

            if (b.Slot != null && order.Order != Orders.Charge)
            {
                double sdx = b.Slot.Value.X - a.X, sdz = b.Slot.Value.Z - a.Z;
                double sd  = Math.Sqrt(sdx * sdx + sdz * sdz);
                if (sd > 0.6) { a.Input.MoveX = sdx / sd * Math.Min(1, sd); a.Input.MoveZ = sdz / sd * Math.Min(1, sd); a.Input.Walk = true; }
            }
            else if (dist < 14)
            {
                a.Input.MoveX = -(t!.X - a.X) / dist * 0.6;
                a.Input.MoveZ = -(t!.Z - a.Z) / dist * 0.6;
            }

            double v  = Agent.WeaponOf(a).MissileSpeed ?? Battle.ArrowSpeed;
            double tx = t!.X, tz = t.Z;
            for (int i = 0; i < 2; i++)
            {
                double range = Math.Sqrt((tx - a.X) * (tx - a.X) + (tz - a.Z) * (tz - a.Z));
                double pitch = Battle.BallisticPitch(range, _battle.OriginY(t) - _battle.OriginY(a), v);
                double tof   = range / (v * Math.Cos(pitch));
                tx = t.X + t.Vx * tof; tz = t.Z + t.Vz * tof;
            }
            double ty = _battle.OriginY(t) + 1.1;
            a.Input.Face = Agent.YawTo(tx - a.X, tz - a.Z);
            a.Input.Aim  = (tx, ty, tz);

            if (b.ShotTarget != t.Id || _battle.Time - b.ShotAt >= ShotLook || b.ShotAt < 0)
            {
                b.ShotTarget = t.Id;
                b.ShotAt     = _battle.Time;
                bool engaged = FriendsNear(t, a.Team, 1.8) > 0;
                if (engaged) b.Retarget = Math.Min(b.Retarget, 0.2);
                b.ShotLow  = !engaged && ClearShot(a, tx, ty, tz, v, false);
                b.ShotHigh = !b.ShotLow && !engaged && ClearShot(a, tx, ty, tz, v, true);
            }
            a.Input.Lob = b.ShotHigh;
            bool clear = b.ShotLow || a.Input.Lob;
            var act = a.Action;
            if (act.Kind == "draw")
            {
                a.Input.AttackHeld = act.T < b.DrawFor || !clear;
                if (!clear && act.T > 3)
                { a.Input.MoveX += b.Strafe * 0.6 * Math.Cos(a.Yaw); a.Input.MoveZ += b.Strafe * -0.6 * Math.Sin(a.Yaw); }
            }
            else if (Agent.CanAct(a) && Math.Abs(Agent.AngleDiff(a.Input.Face, a.Yaw)) < 0.2)
            { a.Input.Attack = Dir.Up; a.Input.AttackHeld = true; b.DrawFor = 1.0 + _battle.Random() * 0.8; }
            return true;
        }

        // ── 友方检测 ──────────────────────────────────────────────────

        int FriendsNear(Agent e, int team, double r)
        {
            int n = 0;
            foreach (var f in _battle.Grid.Near(e.X, e.Z, r))
                if (f.Alive && !f.Gone && f.Team == team
                    && Math.Sqrt((f.X - e.X) * (f.X - e.X) + (f.Z - e.Z) * (f.Z - e.Z)) < r) n++;
            return n;
        }

        // ── 弹道视线检测 ─────────────────────────────────────────────

        bool ClearShot(Agent a, double tx, double ty, double tz, double v, bool high)
        {
            double sx = a.X, sz = a.Z, sy = _battle.OriginY(a) + 1.55;
            double range = Math.Sqrt((tx - sx) * (tx - sx) + (tz - sz) * (tz - sz));
            if (range < 1) return true;
            double pitch = high ? (Battle.BallisticPitchHigh(range, ty - sy, v) ?? double.NaN)
                                : Battle.BallisticPitch(range, ty - sy, v);
            if (double.IsNaN(pitch)) return false;
            double ux = (tx - sx) / range, uz = (tz - sz) / range;
            double vh = v * Math.Cos(pitch), vy = v * Math.Sin(pitch);
            double arcY(double d) { double tt = d / vh; return sy + vy * tt - 0.5 * Battle.Gravity * tt * tt; }
            var seen = new HashSet<Agent>();
            for (double d = 1; d < range - 1; d += 3)
            {
                double px = sx + ux * d, pz = sz + uz * d;
                if (arcY(d) - _battle.Terrain.Height(px, pz) > 3.6) continue;
                foreach (var f in _battle.Grid.Near(px, pz, 2.6))
                    if (f != a && f.Team == a.Team && f.Alive && !f.Gone) seen.Add(f);
            }
            foreach (var f in seen)
            {
                double rx = f.X - sx, rz = f.Z - sz;
                double along = rx * ux + rz * uz;
                if (along < 0.5 || along > range - 1.5) continue;
                double side = Math.Abs(rx * uz - rz * ux);
                if (side > (Agent.Mounted(f) ? 1.0 : 0.7)) continue;
                if (arcY(along) < _battle.OriginY(f) + 2.3) return false;
            }
            return true;
        }

        // ── 骑射手游击 ───────────────────────────────────────────────

        void Skirmish(Agent a, Brain b, Agent t)
        {
            var w = Agent.WeaponOf(a);
            if (w.Cls != WeaponClass.Bow)
            {
                if (Agent.CanAct(a)) a.Input.Swap = Array.FindIndex(a.Troop.Weapons, id => WeaponsData.Weapons[id].Cls == WeaponClass.Bow);
                return;
            }
            double dx = t.X - a.X, dz = t.Z - a.Z;
            double d  = Math.Sqrt(dx * dx + dz * dz) == 0 ? 1 : Math.Sqrt(dx * dx + dz * dz);
            double want = 26;
            double ttx = -dz / d * b.Circle, ttz = dx / d * b.Circle;
            double radial = (d - want) / 10;
            // 11-08 用户：敌方骑兵卡在地图边界上走——是骑射手“游射”绕目标转 26 码的圈，目标靠边时圈画到边界外，只能贴边滑。
            // 软边界：离边界 10 码内往里推；绕圈方向朝外就掉头（原型战场半宽 110 码很少碰边，沙盘只有 40 码）
            double half = _battle.Terrain.Half, margin = 10, px = 0, pz = 0;
            if (a.X > half - margin) px -= (a.X - (half - margin)) / margin;
            if (a.X < -half + margin) px += (-half + margin - a.X) / margin;
            if (a.Z > half - margin) pz -= (a.Z - (half - margin)) / margin;
            if (a.Z < -half + margin) pz += (-half + margin - a.Z) / margin;
            if ((px != 0 || pz != 0) && ttx * px + ttz * pz < -0.2) { b.Circle = -b.Circle; ttx = -ttx; ttz = -ttz; }
            double gx = ttx + (dx / d) * radial + px * 2.5, gz = ttz + (dz / d) * radial + pz * 2.5;
            double heading = Agent.YawTo(gx, gz);
            double diff = Agent.AngleDiff(heading, a.Yaw);
            a.Input.Steer    = Math.Max(-1, Math.Min(1, diff * 2));
            a.Input.Throttle = 0.8;
            bool danger = false;
            foreach (var e in _battle.Grid.Near(a.X, a.Z, 12))
            {
                if (e.Alive && !e.Gone && e.Team != a.Team
                    && Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Z - a.Z) * (e.Z - a.Z)) < (Agent.Mounted(e) ? 12 : 7))
                { danger = true; break; }
            }
            if (danger)
            {
                double away = Agent.YawTo(-dx / d + px * 2.5, -dz / d + pz * 2.5);   // 躲开敌人时也别往边界外躲
                a.Input.Steer    = Math.Max(-1, Math.Min(1, Agent.AngleDiff(away, a.Yaw) * 2));
                a.Input.Throttle = 1;
            }
            double v    = w.MissileSpeed ?? Battle.ArrowSpeed;
            double lead = d / v;
            double ax   = t.X + (t.Vx - a.Vx) * lead, az = t.Z + (t.Vz - a.Vz) * lead;
            a.Input.Face = Agent.YawTo(ax - a.X, az - a.Z);
            a.Input.Aim  = (ax, _battle.OriginY(t) + 1.1, az);
            var act = a.Action;
            if (act.Kind == "draw") a.Input.AttackHeld = act.T < b.DrawFor || d > 45;
            else if (Agent.CanAct(a) && d < 45)
            { a.Input.Attack = Dir.Up; a.Input.AttackHeld = true; b.DrawFor = 1.1 + _battle.Random() * 0.6; }
        }

        // ── 骑兵 ──────────────────────────────────────────────────────

        void Ride(Agent a, Brain b, Agent? t, GroupOrder order)
        {
            if (Defs.IsRanged(Agent.WeaponOf(a).Cls) && Agent.CanAct(a))
                a.Input.Swap = Array.FindIndex(a.Troop.Weapons, id => !Defs.IsRanged(WeaponsData.Weapons[id].Cls));

            void SteerTo(double x, double z, double throttle)
            {
                double want = Agent.YawTo(x - a.X, z - a.Z);
                double diff = Agent.AngleDiff(want, a.Yaw);
                a.Input.Steer    = Math.Max(-1, Math.Min(1, diff * 2.2));
                a.Input.Throttle = throttle * (Math.Abs(diff) > 1.4 ? 0.45 : 1);
                a.Input.Face     = want;
            }

            bool closeFoe = t != null && Math.Sqrt((t.X - a.X) * (t.X - a.X) + (t.Z - a.Z) * (t.Z - a.Z)) < 7;
            if (b.Slot != null && order.Order != Orders.Charge && !closeFoe)
            {
                double sd = Math.Sqrt((b.Slot.Value.X - a.X) * (b.Slot.Value.X - a.X) + (b.Slot.Value.Z - a.Z) * (b.Slot.Value.Z - a.Z));
                if (sd > 2) SteerTo(b.Slot.Value.X, b.Slot.Value.Z, Math.Min(1, sd / 15));
                else
                {
                    a.Input.Throttle = 0;
                    a.Input.Steer    = Math.Max(-1, Math.Min(1, Agent.AngleDiff(b.Slot.Value.Yaw, a.Yaw) * 2));
                }
                return;
            }
            if (t == null) { a.Input.Throttle = 0; return; }

            var w   = Agent.WeaponOf(a);
            double dx = t.X - a.X, dz = t.Z - a.Z;
            double d   = Math.Sqrt(dx * dx + dz * dz);
            double ahead = dx * Agent.ForwardX(a.Yaw) + dz * Agent.ForwardZ(a.Yaw);
            var m = a.Mount!;

            if (b.Cav == "out")
            {
                if (_battle.Time > b.OutUntil
                    || Math.Abs(a.X) > _battle.Terrain.Half - 8
                    || Math.Abs(a.Z) > _battle.Terrain.Half - 8)
                    b.Cav = "in";
                else SteerTo(a.X + Agent.ForwardX(a.Yaw) * 10, a.Z + Agent.ForwardZ(a.Yaw) * 10, 1);
                if (b.Cav == "out") return;
            }

            if (w.Couch)
            {
                double lead = Math.Min(1.2, d / Math.Max(4, m.Speed));
                SteerTo(t.X + t.Vx * lead, t.Z + t.Vz * lead, 1);
                a.Input.Couch = m.Speed > 8 && d < 30;
                if ((ahead < -1.5 && d < 8) || (d < 4 && m.Speed < 5))
                { b.Cav = "out"; b.OutUntil = _battle.Time + 2.2 + _battle.Random() * 1.5; }
                if (m.Speed < 5 && d < 6 && a.Troop.Weapons.Length > 1 && Agent.CanAct(a)) a.Input.Swap = 1;
                return;
            }

            double side = 1.6;
            double rx = Math.Cos(a.Yaw), rz = -Math.Sin(a.Yaw);
            double px = t.X - rx * side, pz = t.Z - rz * side;
            SteerTo(px, pz, d < 12 ? 0.75 : 1);
            var act  = a.Action;
            double reach = Reach(a) + 0.8;
            if (d < reach + m.Speed * 0.5) a.Input.Face = Agent.YawTo(dx, dz);
            if (act.Kind == "attack" && !act.Released)
                a.Input.AttackHeld = (d > reach || ahead > reach) && act.Held < 2.5;
            else if (Agent.CanAct(a) && d < reach + m.Speed * 0.35 && ahead > 0)
            {
                bool onRight = dx * rx + dz * rz > 0;
                a.Input.Attack = onRight ? Dir.Right : Dir.Left;
                a.Input.AttackHeld = true;
            }
            if (ahead < -2 && d < 8)
            { b.Cav = "out"; b.OutUntil = _battle.Time + 1.6 + _battle.Random() * 1.2; }
            if (Agent.SpeedOf(a) < 2 && d < reach && Agent.CanAct(a))
            { a.Input.Attack = Dir.Up; a.Input.AttackHeld = false; }
        }

        // ══════════════════════════════════════════════════════════════
        // Brain 访问器
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取或创建 Agent 的 Brain（随机初始化顺序必须与 TS 完全一致）。
        /// </summary>
        Brain GetBrain(Agent a)
        {
            if (_brains.TryGetValue(a.Id, out var b)) return b;
            b = new Brain
            {
                Target         = 0,
                Retarget       = 0,
                ThreatKey      = "",
                BlockAt        = 0,
                BlockDir       = null,
                BlockUntil     = 0,
                Plan           = "block",
                ReleaseAt      = 0.5,
                // !! 随机调用顺序与 TS brain 初始化完全一致，不可更改 !!
                AttackCooldown = _battle.Random() * 0.8,
                Strafe         = _battle.Random() < 0.5 ? -1.0 : 1.0,
                StrafeTimer    = 0,
                Circle         = _battle.Random() < 0.5 ? -1.0 : 1.0,
                Ladder         = null,
                Cav            = "in",
                OutUntil       = 0,
                DrawFor        = 1,
                Slot           = null,
                Arrows         = false,
                ArrowsAt       = -1,
                ShotAt         = -1,
                ShotTarget     = 0,
                ShotLow        = false,
                ShotHigh       = false,
            };
            _brains[a.Id] = b;
            return b;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Brain 私有类型
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 单个 Agent 的 AI 记忆（对应 TS Brain 接口）。
    /// </summary>
    sealed class Brain
    {
        public int    Target;
        public double Retarget;
        public string ThreatKey = "";
        public double BlockAt;
        public Dir?   BlockDir;
        public double BlockUntil;
        public string Plan = "block";
        public double ReleaseAt;
        public double AttackCooldown;
        public double Strafe;
        public double StrafeTimer;
        public double Circle;
        public double? Ladder;
        public string  Cav = "in";
        public double  OutUntil;
        public double  DrawFor;
        public Slot?   Slot;
        public bool    Arrows;
        public double  ArrowsAt;
        public double  ShotAt;
        public int     ShotTarget;
        public bool    ShotLow;
        public bool    ShotHigh;
    }

    // ══════════════════════════════════════════════════════════════════
    // 辅助接口：用于 LevelRoute 接受目标坐标
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 最小位置接口，供 LevelRoute 同时接受 Agent 和临时坐标点。
    /// </summary>
    public interface IAgentPos
    {
        double X { get; }
        double Y { get; }
        double Z { get; }
    }

    /// <summary>临时坐标结构体（用于城墙目标点）。</summary>
    public struct AgentPos : IAgentPos
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    /// <summary>
    /// blockSlots 的目标位置参数（避免与 BlockPlan 混用）。
    /// </summary>
    public struct BlockPlanRef
    {
        public double X, Z, Yaw;
    }
}

// FORCE_RECOMPILE_MARKER