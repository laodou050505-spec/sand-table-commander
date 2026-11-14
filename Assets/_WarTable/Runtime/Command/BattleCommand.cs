using System;
using System.Collections.Generic;
using System.Linq;
using WarTable.Sim;

namespace WarTable.Command
{
    /// <summary>
    /// MR 版的指挥（照原型 src/mr/battleCommand.ts 移植，去掉画面部分）：玩家执掌王国军（team 0），敌军交给模拟层的 AI 指挥官。
    /// 选部队、划线 / 点地移动、完整军令都在这里；触发方式（手势 / 菜单 / 键鼠）只调 <see cref="Exec"/> 和 <see cref="Update"/>。
    /// 战旗、光环、划线预览由界面层按 <see cref="Flags"/> 等状态去画（头显界面之后单独设计）。坐标：模拟层的码（原型坐标）。
    /// </summary>
    public class BattleCommand
    {
        public const int Me = 0, Foe = 1;
        public static readonly string[] AllGroups = { Groups.Inf, Groups.Arch, Groups.Cav, Groups.Ha };

        readonly Battle battle;
        readonly AI ai;
        readonly List<BlockPlan> plan;
        readonly Action startFight;

        public readonly PinchCommand Pinch = new();
        /// <summary>选中的部队（按选中顺序，和原型 Set 的插入顺序一致）。</summary>
        public readonly List<string> Selected = new();
        readonly List<string> following = new();
        double followClock;
        /// <summary>正在开向某支敌军的己方部队 → 目标敌军部队。</summary>
        readonly Dictionary<string, string> attacking = new();
        /// <summary>11-08 用户：布阵时把弓兵拖到某支敌军身上，开战后应该直接去打它，不要站着（不然来不及反应）。
        /// 布阵时拖到敌军上 = 预定目标（部队留在原地），开战那一刻自动下“进攻”。我军 → 敌军。</summary>
        readonly Dictionary<string, string> queued = new();
        /// <summary>这支敌军是否已被我军预定为开战后的进攻目标（布阵时旗牌一直显示“被瞄准”）。</summary>
        public bool QueuedTarget(string foeGroup) => Deploying && queued.ContainsValue(foeGroup);
        double attackClock;
        /// <summary>离目标敌军多近（码）开始冲锋。</summary>
        const double AttackRange = 12, RangedAttackRange = 40;
        double engageClock;
        /// <summary>玩家用手势下令坚守的部队（自动接敌距离收紧到 6 码）。</summary>
        readonly HashSet<string> holdGround = new();
        readonly Dictionary<string, List<Agent>>[] groupsNow = { NewGroups(), NewGroups() };

        public string Hover { get; private set; }
        /// <summary>最近的提示（军令回执、主将阵亡等），给状态牌看。</summary>
        public string Message { get; private set; } = "";
        /// <summary>下了一道军令（命令名、哪些部队）：战场上那几支部队的光环弹一下（FieldUI），代替文字回报。</summary>
        public event Action<string, IReadOnlyList<string>> Ordered;
        /// <summary>交互反馈（不写字，界面层出声 / 闪光环）：select 选中、deselect 取消、deny 这一下不行（没选部队就点敌军、布阵时冲敌）、fight 开战。</summary>
        public event Action<string> Feedback;
        double messageT;

        public bool Deploying => battle.Deploying;
        public int? Winner => battle.Winner;

        public BattleCommand(Battle battle, AI ai, List<BlockPlan> plan, Action startFight)
        {
            this.battle = battle; this.ai = ai; this.plan = plan; this.startFight = startFight;
            RefreshGroups();
        }

        static Dictionary<string, List<Agent>> NewGroups() => AllGroups.ToDictionary(g => g, _ => new List<Agent>());

        // ------------------------------------------------------------ 状态（给界面层画）

        public struct Flag { public int team; public string group; public int count; public double x, z; public string order; public bool selected, hover; }

        /// <summary>视线悬停的目标（己方部队、敌方部队或我方帅旗）。</summary>
        public PinchTarget? HoverTarget { get; private set; }

        /// <summary>每支部队一面战旗：重心位置、人数、当前军令（己方）、选中 / 悬停。</summary>
        public IEnumerable<Flag> Flags()
        {
            foreach (var team in new[] { Me, Foe })
                foreach (var g in AllGroups)
                {
                    var list = groupsNow[team][g];
                    if (list.Count == 0) continue;
                    var c = Centroid(team, g);
                    yield return new Flag { team = team, group = g, count = list.Count, x = c.x, z = c.z, order = OrderLabel(team, g), selected = team == Me && Selected.Contains(g), hover = IsHover(team, g) };
                }
        }

        bool IsHover(int team, string g) => HoverTarget is { } h && h.kind == (team == Me ? TargetKind.Own : TargetKind.Foe) && FromGrp(h.group) == g;

        /// <summary>部队脚下光环：中心和半径（75% 的人所在范围，掉队的一两个不撑大光环）。也用来判断视线是否落在这支部队上。</summary>
        public (double x, double z, double r) Ring(string g, int team = Me)
        {
            var list = groupsNow[team][g];
            var c = Centroid(team, g);
            var ds = list.Select(a => Math.Sqrt((a.X - c.x) * (a.X - c.x) + (a.Z - c.z) * (a.Z - c.z))).OrderBy(d => d).ToList();
            double r = ds.Count == 0 ? 3 : Math.Min(14, Math.Max(3, ds[(int)Math.Floor((ds.Count - 1) * 0.75)]));
            return (c.x, c.z, r + 1.5);
        }

        /// <summary>划线预览：线 + 朝向（够长才有）。</summary>
        public LineOrder? PreviewLine
        {
            get
            {
                var p = Pinch.Preview;
                if (p == null) return null;
                return CommandLogic.Line(p.Value.start.x, p.Value.start.z, p.Value.end.x, p.Value.end.z, Centroid(Foe, null));
            }
        }

        /// <summary>我方主将（头顶一颗星；他阵亡全军士气大跌）。</summary>
        public Agent Commander()
        {
            var id = battle.Commander[Me];
            var a = id.HasValue ? battle.Get(id.Value) : null;
            return a != null && a.Alive && !a.Gone ? a : null;
        }

        // ------------------------------------------------------------ 每帧

        P2 Centroid(int team, string g)
        {
            IEnumerable<Agent> list = g != null ? groupsNow[team][g] : AllGroups.SelectMany(k => groupsNow[team][k]);
            double x = 0, z = 0; int n = 0;
            foreach (var a in list) { x += a.X; z += a.Z; n++; }
            if (n == 0) n = 1;
            return new P2(x / n, z / n);
        }

        void RefreshGroups()
        {
            foreach (var team in new[] { Me, Foe }) foreach (var g in AllGroups) groupsNow[team][g].Clear();
            foreach (var a in battle.Agents)
            {
                if (!a.Alive || a.Gone || a.Routed) continue;
                groupsNow[a.Team][Groups.GroupOf(a.Troop.Role)].Add(a);
            }
            foreach (var g in AllGroups) if (groupsNow[Me][g].Count == 0) { Selected.Remove(g); following.Remove(g); attacking.Remove(g); }
        }

        /// <summary>
        /// 每帧调。hover：视线落在哪个目标上（界面层算：战旗或部队脚下的光环范围、我方帅旗）；
        /// pinching / point：捏合与它在沙盘上的落点（模拟层坐标）。
        /// 捏己方部队 = 只选这一支（再捏它 = 取消）；选中后捏敌军 = 冲锋；捏我方帅旗 = 布阵时开战、交战时全选；
        /// 在沙盘上点一下 = 移过去，拖一条线 = 沿线列阵（阵型、朝向、间距都自动）。
        /// </summary>
        public void Update(double dt, PinchTarget? hover, bool pinching, P2? point)
        {
            RefreshGroups();
            if (Winner != null && Pinch.Start != null) Pinch.Cancel();
            HoverTarget = Winner == null && hover is { } h && (h.kind == TargetKind.Standard || groupsNow[h.kind == TargetKind.Own ? Me : Foe][FromGrp(h.group)].Count > 0) ? hover : null;
            Hover = HoverTarget is { kind: TargetKind.Own } ho ? FromGrp(ho.group) : null;
            var e = Winner == null ? Pinch.Update(pinching, HoverTarget, point, Selected.Count > 0) : null;
            if (e != null)
            {
                var ev = e.Value;
                if (ev.type == PinchKind.Target) PickTarget(ev.target);
                else if (ev.type == PinchKind.Clear) Selected.Clear();
                else if (ev.type == PinchKind.Move) MoveSelected(new P2(ev.x, ev.z));
                else if (ev.type == PinchKind.DragTo) DragUnit(ev.target, new P2(ev.x, ev.z));
                else LineSelected(ev.x0, ev.z0, ev.x1, ev.z1);
            }
            UpdateFollow(dt);
            UpdateAttacks(dt);
            UpdateEngage(dt);
            if ((messageT -= dt) <= 0) Message = "";
        }

        /// <summary>战斗事件：主将阵亡要让玩家知道，保他是玩法的一部分。</summary>
        public void Events(List<BattleEvent> events)
        {
            foreach (var e in events)
                if (e is CommanderEvent c) Say(Loc.T(c.Team == Me ? "say.ourGeneralDown" : "say.theirGeneralDown"));
        }

        public void Say(string s) { Message = s; messageT = 3; }

        static Grp ToGrp(string g) => g == Groups.Arch ? Grp.Arch : g == Groups.Cav ? Grp.Cav : g == Groups.Ha ? Grp.Ha : Grp.Inf;
        static string FromGrp(Grp g) => g == Grp.Arch ? Groups.Arch : g == Grp.Cav ? Groups.Cav : g == Grp.Ha ? Groups.Ha : Groups.Inf;

        string SelectedLabel() => string.Join(Loc.T("group.join"), Selected.Select(g => Loc.T("group." + g)));

        // ------------------------------------------------------------ 军令

        List<BlockPlan> BlocksOf(string g) => plan.Where(b => b.Team == Me && Deploy.BlockGroup(b) == g).ToList();

        void SendPlan()
        {
            foreach (var b in plan) if (b.Team == Me) Deploy.ClampBlock(battle.Terrain!, b);
            Scenario.Replan(battle, plan, null);
        }

        class BlockAdapter : CommandLogic.IBlock
        {
            readonly BlockPlan b;
            public BlockAdapter(BlockPlan b) { this.b = b; }
            public double X { get => b.X; set => b.X = value; }
            public double Z { get => b.Z; set => b.Z = value; }
            public double Yaw { get => b.Yaw; set => b.Yaw = value; }
            public int Files { get => b.Files; set => b.Files = value; }
        }

        static void MoveBlocks(List<BlockPlan> blocks, P2 to, double yaw, double widthScale)
            => CommandLogic.MoveBlocks(blocks.Select(b => (CommandLogic.IBlock)new BlockAdapter(b)).ToList(), to, yaw, widthScale);

        /// <summary>拖动中落点下面的敌军（界面层把它的旗牌换成“被瞄准”的样子、箭头变红）。</summary>
        public string? DragFoe
        {
            get
            {
                var d = Pinch.DragPreview;
                return d.HasValue ? FoeAt(d.Value.end) : null;   // 布阵时也显示（拖上去 = 预定开战后进攻它）
            }
        }

        /// <summary>点 at 落在哪支敌军的光环里（光环半径 × 1.3，拖到敌军附近就算），没有返回 null。</summary>
        string? FoeAt(P2 at) => FoeAt(at, Deploying ? 1.0 : 1.3);   // 11-08：布阵时要真的落在敌军光环里才算（不然在前沿挪位置会被误当成“预定进攻”）

        string? FoeAt(P2 at, double ringK)
        {
            string? best = null; double bd = double.MaxValue;
            foreach (var g in AllGroups)
            {
                if (groupsNow[Foe][g].Count == 0) continue;
                var (x, z, r) = Ring(g, Foe);
                double d = Math.Sqrt((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z));
                if (d < r * ringK && d < bd) { bd = d; best = g; }
            }
            return best;
        }

        /// <summary>11-07 用户：用手直接把部队拖过去——拖到某处就走过去，拖到敌军身上就去打它。
        /// 拖的这支已在多选里：整组一起动；否则只选它。布阵时拖到敌军上只走到那里（还没开战）。</summary>
        void DragUnit(PinchTarget t, P2 at)
        {
            var g = FromGrp(t.group);
            if (groupsNow[Me][g].Count == 0) return;
            // 11-08 用户：现在怎么整组一起被拖走了——不要这样，不同部队要能打不同的敌军。
            // 手里拖的是哪一支就只动哪一支（以前它在多选里会整组一起动；手势 / 看敌军捏的“没选中就全军”又会留下全选）
            Selected.Clear(); Selected.Add(g);
            var foe = FoeAt(at);
            if (foe != null && Deploying) Queue(foe);
            else if (foe != null) Exec("attack:" + foe);
            else MoveSelected(at);   // 11-08 用户：可以先把部队挪到某处、再选要打的目标——挪位置不取消已选的目标
            Selected.Clear();   // 11-09：拖完不留“选中”（没有选中，就不会捏地面变成划线列阵；手势也总是对全军）
        }

        /// <summary>布阵时给选中的部队预定开战后的进攻目标；选中的都已经瞄着它了就取消（再拖 / 再捏一次同一支 = 取消）。</summary>
        void Queue(string foe)
        {
            var sel = Selected.ToList();
            if (sel.Count == 0) return;
            if (sel.All(x => queued.TryGetValue(x, out var f) && f == foe))
            {
                foreach (var x in sel) queued.Remove(x);
                Feedback?.Invoke("deselect");
                return;
            }
            foreach (var x in sel) queued[x] = foe;
            Say(Loc.F("say.attack", SelectedLabel(), Loc.T("group." + foe)));
            Ordered?.Invoke("aim", sel);   // 界面层：落令声、旗牌脉冲、金箭头、“目标·××”小牌
        }

        void MoveSelected(P2 at)
        {
            var groups = Selected.ToList();
            var enemy = Centroid(Foe, null);
            double yaw = CommandLogic.YawToward(enemy.x - at.x, enemy.z - at.z);
            if (Deploying)
            {
                double span = groups.Count > 1 ? 14 : 0;
                for (int i = 0; i < groups.Count; i++)
                {
                    double off = (i - (groups.Count - 1) / 2.0) * span;
                    MoveBlocks(BlocksOf(groups[i]), new P2(at.x + Math.Cos(yaw) * off, at.z - Math.Sin(yaw) * off), yaw, 1);
                }
                SendPlan();
            }
            else
            {
                foreach (var g in groups) { following.Remove(g); attacking.Remove(g); holdGround.Remove(g); }
                ai.MoveTo(Me, groups, at.x, at.z, yaw);
            }
            Say(Loc.F("say.move", SelectedLabel()));
            Ordered?.Invoke("move", Selected.ToList());
        }

        void LineSelected(double x0, double z0, double x1, double z1)
        {
            var groups = Selected.ToList();
            var whole = CommandLogic.Line(x0, z0, x1, z1, Centroid(Foe, null));
            var parts = CommandLogic.SplitLine(whole, groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i]; var o = parts[i];
                double width = GroupWidth(g, o.yaw);
                if (Deploying) MoveBlocks(BlocksOf(g), new P2(o.x, o.z), o.yaw, Math.Max(0.4, Math.Min(3, o.length / Math.Max(3, width))));
                else
                {
                    following.Remove(g); attacking.Remove(g);
                    double cur = ai.SpacingMap[Me][g];
                    ai.MoveTo(Me, new List<string> { g }, o.x, o.z, o.yaw);
                    ai.SetSpacing(Me, new List<string> { g }, CommandLogic.SpacingFactor(o.length, width, cur));
                }
            }
            if (Deploying) SendPlan();
            Say(Loc.F("say.line", SelectedLabel()));
            Ordered?.Invoke("line", groups);
        }

        /// <summary>这支部队现在沿 yaw 的右方向摊开多宽。</summary>
        double GroupWidth(string g, double yaw)
        {
            double rx = Math.Cos(yaw), rz = -Math.Sin(yaw), lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var a in groupsNow[Me][g]) { double d = a.X * rx + a.Z * rz; lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
            return hi > lo ? hi - lo : 3;
        }

        /// <summary>
        /// 触发一条命令（手势、菜单、键盘都走这里）：fight / all / charge / hold / follow / forward / back / retreat / face / auto /
        /// tighter / looser / holdFire / arrAuto / arr:&lt;阵型&gt; / sel:&lt;部队&gt;。
        /// </summary>
        public void Exec(string cmd)
        {
            if (Winner != null) return;
            if (cmd == "fight") { Fight(); return; }
            if (cmd.StartsWith("sel:"))
            {
                var g = cmd.Substring(4);
                if (groupsNow[Me][g].Count == 0) return;
                if (Selected.Contains(g)) Selected.Remove(g); else Selected.Add(g);
                return;
            }
            if (cmd == "all")
            {
                var every = AllGroups.Where(g => groupsNow[Me][g].Count > 0).ToList();
                if (every.All(Selected.Contains)) Selected.Clear();
                else foreach (var g in every) if (!Selected.Contains(g)) Selected.Add(g);
                return;
            }
            if (Selected.Count == 0) { Say(Loc.T("say.pickFirst")); Feedback?.Invoke("deny"); return; }
            var groups = Selected.ToList();
            string label = SelectedLabel();
            if (cmd.StartsWith("arr"))
            {
                Arrangement? kind = cmd == "arrAuto" ? null : Enum.Parse<Arrangement>(cmd.Substring(4), true);
                if (Deploying)
                {
                    foreach (var g in groups) foreach (var b in BlocksOf(g)) b.Arrangement = kind.HasValue ? kind.Value.ToString().ToLowerInvariant() : null;
                    SendPlan();
                }
                ai.SetArrangement(Me, groups, kind);
                Say(Loc.F("say.arrangement", label, kind.HasValue ? Loc.T("arr." + kind.Value.ToString().ToLowerInvariant()) : Loc.T("arr.auto")));
                return;
            }
            if (Deploying && !(cmd == "face" || cmd == "tighter" || cmd == "looser" || cmd == "holdFire")) { Say(Loc.T("say.notYet")); Feedback?.Invoke("deny"); return; }
            var c = Centroid(Me, null);
            if (cmd != "holdFire" && cmd != "tighter" && cmd != "looser") foreach (var g in groups) { following.Remove(g); attacking.Remove(g); }
            if (cmd.StartsWith("attack:"))
            {
                // 冲向指定的那支敌军：模拟层的冲锋是每个士兵各找最近的敌人、不认目标，
                // 所以离得远时先列队开到它跟前（UpdateAttacks 每 0.5 秒跟着它走），进到 AttackRange 以内再冲锋
                var foe = cmd.Substring(7);
                foreach (var g in groups) { attacking[g] = foe; holdGround.Remove(g); }
                attackClock = 0;
                UpdateAttacks(0);
                Say(Loc.F("say.attack", label, Loc.T("group." + foe)));
                Ordered?.Invoke("attack", groups);
                return;
            }
            switch (cmd)
            {
                case "charge": foreach (var g in groups) holdGround.Remove(g); ai.SetOrder(Me, groups, Orders.Charge, c.x, c.z, 0); Say(Loc.F("say.charge", label)); break;
                case "hold":
                    // 就地坚守：各部队站在自己现在的位置，面朝敌军
                    foreach (var g in groups)
                    {
                        var at = Centroid(Me, g); var enemy = Centroid(Foe, null);
                        ai.MoveTo(Me, new List<string> { g }, at.x, at.z, CommandLogic.YawToward(enemy.x - at.x, enemy.z - at.z));
                    }
                    foreach (var g in groups) { holdGround.Add(g); attacking.Remove(g); following.Remove(g); }
                    Say(Loc.F("say.hold", label));
                    break;
                case "follow":
                    if (Commander() == null) { Say(Loc.T("say.generalDead")); break; }
                    foreach (var g in groups) if (!following.Contains(g)) following.Add(g);
                    followClock = 0;
                    Say(Loc.F("say.follow", label));
                    break;
                case "forward": ai.Move10(Me, groups, 10); Say(Loc.F("say.forward", label)); break;
                case "back": ai.Move10(Me, groups, -10); Say(Loc.F("say.back", label)); break;
                case "retreat": ai.SetOrder(Me, groups, Orders.Retreat, c.x, c.z, 0); Say(Loc.F("say.retreat", label)); break;
                case "face": ai.FaceEnemy(Me, groups); Say(Loc.F("say.face", label)); break;
                case "auto": ai.SetOrder(Me, groups, Orders.Auto, c.x, c.z, 0); Say(Loc.F("say.auto", label)); break;
                case "tighter": ai.SetSpacing(Me, groups, 0.8); Say(Loc.F("say.tighter", label)); break;
                case "looser": ai.SetSpacing(Me, groups, 1.25); Say(Loc.F("say.looser", label)); break;
                case "holdFire":
                    {
                        bool hold = !ai.HoldFireMap[Me][groups[0]];
                        ai.SetHoldFire(Me, groups, hold);
                        Say(Loc.F(hold ? "say.holdFire" : "say.fireAtWill", label));
                        break;
                    }
            }
            Ordered?.Invoke(cmd, groups);
        }

        /// <summary>我军这支部队现在的去向（界面画箭头，11-05 用户：攻击是怎么攻击的看不懂）：
        /// attack = 冲向某支敌军（目标那支敌军的中心）；move = 走向目的地；null = 原地 / 自由交战。</summary>
        public (string kind, double x, double z)? Intent(string g)
        {
            if (groupsNow[Me][g].Count == 0) return null;
            if (Deploying)   // 11-08：布阵时预定了目标 → 画一道金色箭头指向它（还没打，所以不用红）
                return queued.TryGetValue(g, out var qf) && groupsNow[Foe][qf].Count > 0 ? ("aim", Centroid(Foe, qf).x, Centroid(Foe, qf).z) : null;
            var me = Centroid(Me, g);
            if (attacking.TryGetValue(g, out var foe) && groupsNow[Foe][foe].Count > 0) { var c = Centroid(Foe, foe); return ("attack", c.x, c.z); }
            var o = ai.OrdersMap[Me][g];
            if (o.Order == Orders.Charge)
            {
                // 冲锋：指向最近的一支敌军
                double best = double.MaxValue; P2? tgt = null;
                foreach (var eg in AllGroups)
                {
                    if (groupsNow[Foe][eg].Count == 0) continue;
                    var c = Centroid(Foe, eg); double d = (c.x - me.x) * (c.x - me.x) + (c.z - me.z) * (c.z - me.z);
                    if (d < best) { best = d; tgt = c; }
                }
                return tgt.HasValue ? ("attack", tgt.Value.x, tgt.Value.z) : null;
            }
            if (o.Order == Orders.Hold || o.Order == Orders.Advance)
            {
                double dx = o.X - me.x, dz = o.Z - me.z;
                if (dx * dx + dz * dz > 25) return ("move", o.X, o.Z);
            }
            return null;
        }

        /// <summary>全军选中（不切换：已经全选就保持）。</summary>
        public void SelectAll()
        {
            foreach (var g in AllGroups) if (groupsNow[Me][g].Count > 0 && !Selected.Contains(g)) Selected.Add(g);
        }

        void PickTarget(PinchTarget t)
        {
            var g = FromGrp(t.group);
            switch (t.kind)
            {
                case TargetKind.Own:
                    // 11-09 用户：只留拖动——捏住令牌没拖开就松手，什么都不发生（不再“点一下 = 选中”）
                    break;
                case TargetKind.Foe:
                    if (Deploying)
                    {
                        // 11-09 防误触：布阵时眼睛扫过敌军、手指一蜷就让全军都瞄准了它 → 没选中部队时看着敌军捏不做任何事
                        // （预定进攻用“把令牌拖到敌军令牌上”，一支一支来）；选中了部队才给选中的那几支预定
                        if (Selected.Count == 0) break;
                        Queue(g);
                        break;
                    }
                    // 11-07 用户：教程“看着一支敌军捏一下”过不去 → 没选中部队时不再拒绝，直接全军冲这支敌军
                    bool auto = Selected.Count == 0;
                    if (auto) SelectAll();
                    Exec("attack:" + g);
                    if (auto) Selected.Clear();
                    break;
                case TargetKind.Standard:
                    if (Deploying) { Fight(); Feedback?.Invoke("fight"); }
                    // 交战中捏帅旗不再“全选”（11-09：没有选中这回事了，手势本来就对全军）
                    break;
            }
        }

        void Fight()
        {
            if (!Deploying) return;
            startFight();
            // 玩家不下令，部队就站着不动：不指挥就会输，这才是游戏
            var enemy = Centroid(Foe, null);
            foreach (var g in AllGroups)
            {
                if (groupsNow[Me][g].Count == 0) continue;
                var at = Centroid(Me, g);
                ai.MoveTo(Me, new List<string> { g }, at.x, at.z, CommandLogic.YawToward(enemy.x - at.x, enemy.z - at.z));
            }
            // 布阵时预定了目标的部队：开战即进攻（弓兵 / 骑射开到射程里放箭、近了冲锋，规则同进攻命令）
            foreach (var (g, foe) in queued)
                if (groupsNow[Me][g].Count > 0 && groupsNow[Foe][foe].Count > 0) { attacking[g] = foe; holdGround.Remove(g); }
            queued.Clear();
            if (attacking.Count > 0) { attackClock = 0; UpdateAttacks(0); }
            Say(Loc.T("say.fight"));
        }

        void UpdateAttacks(double dt)
        {
            if (attacking.Count == 0 || (attackClock -= dt) > 0) return;
            attackClock = 0.5;
            foreach (var (g, foe) in attacking.ToList())
            {
                if (groupsNow[Me][g].Count == 0) { attacking.Remove(g); continue; }
                // 目标没了（被歼灭 / 溃散）：就近冲锋
                if (groupsNow[Foe][foe].Count == 0) { attacking.Remove(g); ai.SetOrder(Me, new List<string> { g }, Orders.Charge, 0, 0, 0); continue; }
                var me = Centroid(Me, g); var them = Centroid(Foe, foe);
                double dx = them.x - me.x, dz = them.z - me.z, d = Math.Sqrt(dx * dx + dz * dz);
                // 11-07：队形厚时两队中心可能一直隔着 12 码以上（前排早贴上了）→ 也看双方最近的士兵，10 码内就冲
                double near = double.MaxValue;
                foreach (var a in groupsNow[Me][g]) foreach (var e in groupsNow[Foe][foe]) near = Math.Min(near, (e.X - a.X) * (e.X - a.X) + (e.Z - a.Z) * (e.Z - a.Z));
                // 11-08：弓兵 / 骑射不用贴到 12 码——进到 RangedAttackRange 就放开射（模拟层里远程的“冲锋”是边退边射，不会冲进肉搏）
                bool ranged = g == Groups.Arch || g == Groups.Ha;
                if (d <= (ranged ? RangedAttackRange : AttackRange) || near < 100) { attacking.Remove(g); ai.SetOrder(Me, new List<string> { g }, Orders.Charge, them.x, them.z, 0); continue; }
                double k = Math.Max(0, d - 4) / d;
                ai.MoveTo(Me, new List<string> { g }, me.x + dx * k, me.z + dz * k, CommandLogic.YawToward(dx, dz));
            }
        }

        /// <summary>
        /// 自动接敌（11-07 用户：两军挤在一起就是不打）：模拟层里“站位”的士兵只打很近的敌人（盾墙 3.2 码、一般 7 码），
        /// 两边都站位时前排隔着 5–9 码对峙，40 秒只死几个人。现在我军步兵 / 骑兵在站位状态下，
        /// 离任何（没溃逃的）敌人足够近就自动转成冲锋：拖过去 / 进攻途中 10 码，玩家下令坚守的 6 码（守阵地，但贴上来就打）。
        /// 弓兵、骑射站着放箭，不自动冲。
        /// </summary>
        void UpdateEngage(double dt)
        {
            if (Deploying || (engageClock -= dt) > 0) return;
            engageClock = 0.5;
            foreach (var g in new[] { Groups.Inf, Groups.Cav })
            {
                var mine = groupsNow[Me][g];
                if (mine.Count == 0 || following.Contains(g) || attacking.ContainsKey(g)) continue;
                if (ai.EffectiveOrder(Me, g).Order != Orders.Hold) continue;
                double reach = holdGround.Contains(g) ? 6 : 10, r2 = reach * reach;
                bool close = false;
                foreach (var e in battle.Agents)
                {
                    if (e.Team != Foe || !e.Alive || e.Gone || e.Routed) continue;
                    foreach (var a in mine) { double dx = e.X - a.X, dz = e.Z - a.Z; if (dx * dx + dz * dz < r2) { close = true; break; } }
                    if (close) break;
                }
                if (!close) continue;
                holdGround.Remove(g);
                ai.SetOrder(Me, new List<string> { g }, Orders.Charge, 0, 0, 0);
            }
        }

        /// <summary>
        /// 我军一支部队现在在做什么（11-08 用户：都站着，不知道他们在干嘛）——旗牌上方的小牌显示：
        /// idle 待命 / move 行进 / attack 进攻（正开往指定敌军）/ charge 冲锋 / fight 交战（身边 4 码有敌人）/ hold 坚守 / retreat 撤退。
        /// </summary>
        public string StateOf(string g)
        {
            var mine = groupsNow[Me][g];
            if (mine.Count == 0) return "";
            bool contact = false;
            foreach (var e in battle.Agents)
            {
                if (e.Team != Foe || !e.Alive || e.Gone || e.Routed) continue;
                foreach (var a in mine) { double dx = e.X - a.X, dz = e.Z - a.Z; if (dx * dx + dz * dz < 16) { contact = true; break; } }
                if (contact) break;
            }
            if (Deploying) return queued.TryGetValue(g, out var tg) ? "aim_" + tg : "idle";   // 布阵：“目标·步兵”这类深色牌，不用红色（还没打）
            var o = ai.EffectiveOrder(Me, g);
            if (o.Order == Orders.Retreat) return "retreat";
            if (contact) return "fight";
            if (attacking.ContainsKey(g)) return "attack";
            if (o.Order == Orders.Charge || o.Order == Orders.Auto) return g == Groups.Arch || g == Groups.Ha ? "attack" : "charge";   // 远程的“冲锋”是放开射，牌子写“进攻”
            if (following.Contains(g)) return "move";
            if (o.Order == Orders.Hold)
            {
                var c = Centroid(Me, g);
                if ((c.x - o.X) * (c.x - o.X) + (c.z - o.Z) * (c.z - o.Z) > 64) return "move";   // 8 码（弓兵站位点会往后错 6 码）
                return holdGround.Contains(g) ? "hold" : "idle";
            }
            return "idle";
        }

        void UpdateFollow(double dt)
        {
            if (following.Count == 0 || (followClock -= dt) > 0) return;
            followClock = 1;
            var c = Commander();
            if (c == null) { following.Clear(); return; }
            var enemy = Centroid(Foe, null);
            ai.MoveTo(Me, following.ToList(), c.X, c.Z, CommandLogic.YawToward(enemy.x - c.X, enemy.z - c.Z));
        }

        string OrderLabel(int team, string g)
        {
            if (team == Foe) return "";
            if (following.Contains(g)) return Loc.T("order.follow");
            if (attacking.ContainsKey(g)) return Loc.T("order.charge");
            var kind = ai.ArrangementMap[Me][g];
            if (Deploying) return kind.HasValue ? Loc.T("arr." + kind.Value.ToString().ToLowerInvariant()) : Loc.T("ui.deploy");
            var o = ai.EffectiveOrder(Me, g).Order;
            return Loc.T("order." + o);
        }
    }
}
