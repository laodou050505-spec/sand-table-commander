// Battle.cs — 由 sandtable-xr/src/sim/battle.ts（1858 行）忠实移植
// 命名空间 WarTable.Sim，sealed class Battle，无基类。
// Unity 6，C# 9，不引用 UnityEngine，全局使用 System.Math。
// 所有数值 double，注释简体中文，标识符保持原文。

using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    // ──────────────────────────────────────────────────────────────────────
    // 辅助结构
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>胶囊碰撞体（替代 TS 中的 Capsule 接口）。</summary>
    internal struct Capsule
    {
        public double Ax, Ay, Az;
        public double Bx, By, Bz;
        public double R;
        /// <summary>true = 坐骑躯干；false = 骑手/步兵躯体。</summary>
        public bool IsMount;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Battle 类
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 战场仿真主类：移动、近战（扫刀）、格挡、反击、踢击、坐骑、弓箭、
    /// 士气、溃逃与增援。
    /// 对应 battle.ts 的 Battle 类（围攻/梯子逻辑已删除）。
    /// </summary>
    public sealed class Battle
    {
        // ── 物理常量 ────────────────────────────────────────────────────────
        public const double GravityConst       = 14.0;
        public const double ArrowSpeedConst    = 52.0;
        public const int    MaxLying           = 300;
        public const double LookMax            = 1.75;
        public const double MeleeSpeedPower    = 2.0;
        public const double MissileSpeedPower  = 1.9;

        // Warband 护甲公式：按 DamageType(0=Cut,1=Pierce,2=Blunt) 索引
        private static readonly double[] Soak   = { 0.8, 0.65, 0.5  };
        private static readonly double[] Reduce = { 1.0, 0.5,  0.75 };

        // 命中部位倍率
        private const double HeadMult        = 1.2;
        private const double NeckMult        = 1.2;
        private const double ArmMult         = 0.8;
        private const double LegMult         = 0.9;
        private const double MissileHeadMult = 1.75;

        // 技能加成常量
        private const double PowerStrike = 0.08;
        private const double PowerDraw   = 0.14;
        private const double ProfStep    = 1.15;

        // 打击时间窗口
        private const double ChamberWindow = 0.2;
        private const double ParryWindow   = 0.12;
        private const double ClipTempo     = 1.2;
        private const double NominalOfPeak = 0.85;
        private const double HeadBelow     = 0.12;
        private const double GlanceMin     = 0.35;

        // 士气常量
        private const double MoraleRadius    = 10;
        private const double MoraleCasualty  = 7;
        private const double MoraleKill      = 2.5;
        private const double MoraleCommander = 18;
        private const double MoraleRoutNearby= 4;
        private const double MoralePanic     = 15;
        private const double MoraleRally     = 35;

        // 冲刺常量
        private const double CouchSpeedRef = 5;
        private const double CouchMinSpeed = 7.5;

        // 弓箭发射偏移
        private const double ReleaseAhead  = 0.6;
        private const double ReleaseHeight = 1.55;

        // 击倒常量
        private const double KnockdownMin      = 20;
        private const double KnockdownMaxChance= 0.3;

        // ── 马鞍位置（从 assets.json 编译期写死） ──────────────────────────
        // horse.saddle = [0.0, 1.55, 0.044]；wolf.saddle = [0.0, 1.45, 0.051]
        private static readonly (double Px, double Py, double Pz) SaddleHorse = (0.0, 1.55, 0.044);
        private static readonly (double Px, double Py, double Pz) SaddleWolf  = (0.0, 1.45, 0.051);
        // horse.length = 2.191；wolf.length = 2.544
        private static readonly double MountLengthHorse = 2.191;
        private static readonly double MountLengthWolf  = 2.544;

        // ── 公开字段（对应 TS 实例字段） ──────────────────────────────────
        public List<Agent> Agents { get; } = new();
        public List<BattleEvent> Events { get; } = new();
        public List<Projectile> Projectiles { get; } = new();
        public SpatialGrid Grid { get; } = new();

        public Terrain? Terrain;

        public double Time;
        public bool Deploying;

        /// <summary>每队士气（0-1）。</summary>
        /// <summary>全局伤害倍数（11-08 用户：攻击看着像打不中，接敌后要尽快死人）——原型的数值按 1:1 真实比例，沙盘上一场打得太久。
        /// 乘在护甲减免之前（打得穿甲）。改了要重跑 BalanceTest、核对兵法页的胜率文案。</summary>
        public double DamageScale = 2.2;   // 11-08 实测：×1 一局 55 s、×2.2 约 30 s；×3 会让“全军冲锋”必胜，打法失去意义
        public double[] Morale = { 1.0, 1.0 };
        /// <summary>已上场人数（含增援），每队。</summary>
        public double[] Initial = { 0.0, 0.0 };
        /// <summary>增援队列，每队。</summary>
        public List<string>[] Reserves = { new(), new() };
        /// <summary>场上最大兵力，每队。</summary>
        public double[] FieldCap = { double.PositiveInfinity, double.PositiveInfinity };
        /// <summary>增援出生线 (Z, Yaw)，每队。</summary>
        public (double Z, double Yaw)[] SpawnLine = { (60, 0), (-60, Math.PI) };
        /// <summary>已完成的增援波次计数，每队。</summary>
        public int[] Waves = { 0, 0 };
        /// <summary>胜利方（0 或 1）；null 表示未分胜负。</summary>
        public int? Winner;

        /// <summary>开局士气，每队（0-100）。</summary>
        public double[] StartMorale = { 60, 60 };

        /// <summary>士气倍率，由 AI 按阵型写入（<1 降低，>1 提升脆弱/坚韧感）。</summary>
        public Dictionary<int, double> MoraleMult { get; } = new();

        /// <summary>阵型信息，每 AgentId 对应 (编组, 排布类型)。</summary>
        public Dictionary<int, (string Group, Arrangement Arrangement)> FormationOf { get; } = new();

        /// <summary>队长映射：每队 [组名 → AgentId]。</summary>
        public Dictionary<string, int>[] Captains = { new(), new() };

        /// <summary>战旗实例列表。</summary>
        public List<Banner> Banners { get; } = new();

        /// <summary>指挥官 AgentId（null = 无），每队。</summary>
        public int?[] Commander = { null, null };

        /// <summary>布阵块列表（AI 用）。</summary>
        public List<BlockPlan>? Plan;

        // 攻城/梯子：已删除实现；保留属性供 AI.cs 调用
        public bool Siege => false;
        public List<double> Ladders { get; } = new();

        // ── 私有字段 ────────────────────────────────────────────────────────
        private int nextProjectile = 1;
        private readonly Dictionary<int, Agent> byId = new();
        private readonly Dictionary<int, double> yawRate = new();
        private readonly Func<double> rng;
        private readonly Dictionary<string, double> nominalCache = new();
        private readonly Dictionary<string, double> collidedAt = new();

        // BladeAt 中间缓冲
        private BladePoint bladeTmp;
        private BladePoint bladePrev;
        private BladePoint bladeTmp2;

        // ── 静态方法（供 AI.cs 调用） ─────────────────────────────────────
        public static double Gravity    => GravityConst;
        public static double ArrowSpeed => ArrowSpeedConst;

        /// <summary>低弧弹道仰角（无解时返回 π/4）。</summary>
        public static double BallisticPitch(double range, double dy, double v)
        {
            return BallisticPitchLow(range, dy, v) ?? Math.PI / 4;
        }

        /// <summary>低弧弹道仰角；无解时返回 null。</summary>
        public static double? BallisticPitchLow(double range, double dy, double v)
        {
            if (range < 1e-3) return dy >= 0 ? Math.PI / 2 : -Math.PI / 2;
            double v2 = v * v;
            double disc = v2 * v2 - GravityConst * (GravityConst * range * range + 2 * dy * v2);
            return disc > 0 ? Math.Atan((v2 - Math.Sqrt(disc)) / (GravityConst * range)) : (double?)null;
        }

        /// <summary>高弧弹道仰角；无解时返回 null。</summary>
        public static double? BallisticPitchHigh(double range, double dy, double v)
        {
            if (range < 1e-3) return null;
            double v2 = v * v;
            double disc = v2 * v2 - GravityConst * (GravityConst * range * range + 2 * dy * v2);
            return disc > 0 ? Math.Atan((v2 + Math.Sqrt(disc)) / (GravityConst * range)) : (double?)null;
        }

        /// <summary>Warband 蓄力加成（松手时间 t 的伤害倍率）。</summary>
        public static double HoldMultiplier(double t)
        {
            double hb;
            if      (t >= 1.1) hb = 1.2;
            else if (t >= 0.6) hb = (1.1 - t) * 0.6 + 1.2;
            else if (t >= 0.5) hb = 1.5;
            else               hb = t + 1.0;
            return Math.Max(1, Math.Min(2, hb)) * 0.5 + 0.5;
        }

        // ── 构造函数 ─────────────────────────────────────────────────────
        public Battle(int seed = 1)
        {
            AgentOps.ResetAgentIds();
            // xorshift PRNG，uint，无预热
            uint s = (uint)seed == 0 ? 1u : (uint)seed;
            rng = () =>
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return s / 4294967296.0;
            };
        }

        // ── 公开接口 ─────────────────────────────────────────────────────

        public double Random() => rng();

        private double Uniform(double lo, double hi) => lo + (hi - lo) * rng();

        /// <summary>生成战斗员并放置到场上。</summary>
        public Agent Spawn(string troopId, double x, double z, double yaw)
        {
            var a = AgentOps.CreateAgent(troopId, x, z, yaw);
            a.Y = Terrain?.Height(x, z) ?? 0;
            a.Morale = StartMorale[a.Team];
            Agents.Add(a);
            byId[a.Id] = a;
            Initial[a.Team]++;
            return a;
        }

        /// <summary>根据 Id 查找战斗员。</summary>
        public Agent? Get(int id) => byId.TryGetValue(id, out var a) ? a : null;

        /// <summary>返回存活且未离场的指定队伍战斗员。</summary>
        public List<Agent> Alive(int team)
        {
            var list = new List<Agent>();
            foreach (var a in Agents)
                if (a.Team == team && a.Alive && !a.Gone) list.Add(a);
            return list;
        }

        /// <summary>战斗员脚底 Y（骑乘时为马鞍高度）。</summary>
        public double OriginY(Agent a)
        {
            if (!AgentOps.Mounted(a)) return a.Y;
            var s = GetSaddle(a.Mount!.Kind);
            return a.Y + s.Py;
        }

        /// <summary>推进一个物理帧（dt 秒）。</summary>
        public void Step(double dt)
        {
            Events.Clear();
            if (Deploying)
            {
                foreach (var a in Agents)
                {
                    if (!a.Alive || a.Gone) continue;
                    a.Input.Attack = null;
                    a.Input.Kick = a.Input.Bash = false;
                    ApplyInput(a, dt);
                    Move(a, dt);
                }
                Grid.Build(Agents);
                Separate(true);
                Settle();
                Grid.Build(Agents);
                return;
            }
            Time += dt;
            foreach (var a in Agents)
            {
                if (a.Gone) continue;
                a.SinceHit += dt;
                if (a.Alive) ApplyInput(a, dt);
                else if (a.Action is DeadAction da) da.T += dt;
                if (a.LooseMount != null) RunLoose(a, dt);
            }
            foreach (var a in Agents)
                if (a.Alive && !a.Gone) Move(a, dt);
            Grid.Build(Agents);
            Separate();
            Settle();
            foreach (var a in Agents)
                if (a.Alive && !a.Gone) ResolveActions(a, dt);
            StepProjectiles(dt);
            UpdateBanners();
            UpdateMorale(dt);
            Reinforce();
            Grid.Build(Agents);
        }

        // ── 输入处理 ──────────────────────────────────────────────────────
        private void ApplyInput(Agent a, double dt)
        {
            var inp = a.Input;
            if (!double.IsFinite(inp.Face)) inp.Face = a.Yaw;
            if (!double.IsFinite(inp.AimPitch)) inp.AimPitch = 0;
            if (inp.Aim.HasValue)
            {
                var aim = inp.Aim.Value;
                if (!double.IsFinite(aim.X) || !double.IsFinite(aim.Y) || !double.IsFinite(aim.Z))
                    inp.Aim = null;
            }
            if (!double.IsFinite(inp.MoveX) || !double.IsFinite(inp.MoveZ))
                inp.MoveX = inp.MoveZ = 0;
            a.Input = inp;

            var w = AgentOps.WeaponOf(a);
            var act = a.Action;

            // 换武器
            if (inp.Swap.HasValue && inp.Swap.Value != a.WeaponIdx
                && inp.Swap.Value < a.Troop.Weapons.Length && AgentOps.CanAct(a))
            {
                if (!(AgentOps.Mounted(a) && WeaponsData.Weapons[a.Troop.Weapons[inp.Swap.Value]].NoHorseback))
                {
                    a.Action = new SwapAction { T = 0, To = inp.Swap.Value };
                    a.AltUsage = false;
                    a.Braced = false;
                    return;
                }
            }

            // Alt 用法 / 抵枪
            if (inp.Alt && AgentOps.CanAct(a))
            {
                var baseW = WeaponsData.Weapons[a.Troop.Weapons[a.WeaponIdx]];
                if (a.Braced) a.Braced = false;
                else if (!AgentOps.Mounted(a) && w.Brace && AgentOps.SpeedOf(a) < 1.5) a.Braced = true;
                else if (baseW.AltUsage != null) a.AltUsage = !a.AltUsage;
            }

            if (a.Braced && (inp.Attack.HasValue || inp.Block.HasValue || AgentOps.Mounted(a)
                || (Math.Sqrt(inp.MoveX * inp.MoveX + inp.MoveZ * inp.MoveZ) > 0.05 && !inp.Walk)))
                a.Braced = false;

            if (inp.MountToggle && AgentOps.CanAct(a)) ToggleMount(a);

            switch (act)
            {
                case IdleAction _:
                case BlockAction _:
                case CouchAction _:
                {
                    if (act is CouchAction cAct)
                    {
                        cAct.Cooldown = Math.Max(0, cAct.Cooldown - dt);
                        cAct.T += dt;
                        bool fast = a.Mount != null && a.Mount.Speed > CouchMinSpeed;
                        if (!inp.Couch || !fast || inp.Attack.HasValue || inp.Block.HasValue)
                        { a.Action = new IdleAction(); break; }
                        else break;
                    }
                    bool locked = act is BlockAction ba2 && ba2.Lock > 0;
                    if (act is BlockAction ba3) ba3.Lock = Math.Max(0, ba3.Lock - dt);

                    if (inp.Kick && !AgentOps.Mounted(a) && !locked)
                    { a.Action = new KickAction { T = 0, Done = false }; }
                    else if (inp.Bash && act is BlockAction && AgentOps.HasShield(a) && !AgentOps.Mounted(a) && !locked)
                    { a.Action = new BashAction { T = 0, Done = false }; }
                    else if (inp.Attack.HasValue && !locked && !(AgentOps.Mounted(a) && w.NoHorseback))
                    {
                        if (w.Cls == WeaponClass.Bow)
                        {
                            if (a.Arrows > 0) a.Action = new DrawAction { T = 0 };
                        }
                        else if (w.Cls == WeaponClass.Crossbow)
                        {
                            if (a.Loaded) a.Action = new DrawAction { T = 0 };
                            else if (a.Arrows > 0 && !AgentOps.Mounted(a)) a.Action = new ReloadAction { T = 0 };
                        }
                        else if (w.Cls == WeaponClass.Thrown)
                        {
                            if (a.Ammo[a.WeaponIdx] > 0) a.Action = new DrawAction { T = 0 };
                        }
                        else
                        {
                            Dir dir = inp.Attack!.Value;
                            if (dir == Dir.Down && w.Thrust == 0) dir = Dir.Up;
                            else if (dir != Dir.Down && w.Swing == 0) dir = Dir.Down;
                            var clip = Defs.GetClipSet(a.Troop.Model, w.Cls, w.OneHandedPolearm).Attack[dir];
                            a.Action = new AttackAction
                            {
                                Dir = dir, Clip = clip,
                                Info = SwingData.GetInfo(a.Troop.Model, clip),
                                T = 0, Held = 0, Age = 0,
                                Released = false, Done = false,
                                Struck = new HashSet<int>(),
                                Crush = w.Crush && dir == Dir.Up,
                            };
                        }
                    }
                    else if (inp.Block.HasValue && !Defs.IsRanged(w.Cls))
                    {
                        if (act is BlockAction ba4 && (ba4.Dir == inp.Block.Value || AgentOps.HasShield(a)))
                            ba4.T += dt;
                        else
                            a.Action = new BlockAction { Dir = inp.Block.Value, T = 0, Lock = locked && act is BlockAction baL ? baL.Lock : 0 };
                    }
                    else if (inp.Couch && w.Couch && a.Mount != null && a.Mount.Speed > CouchMinSpeed)
                    { a.Action = new CouchAction { T = 0, Cooldown = 0 }; }
                    else if (!(act is CouchAction) && !locked)
                    { a.Action = new IdleAction(); }
                    break;
                }
                case AttackAction aa:
                {
                    double rate = AgentOps.AttackSpeed(a, w) * ClipTempo;
                    aa.Age += dt;
                    if (!aa.Released)
                    {
                        if (inp.Block.HasValue && aa.T <= aa.Info!.Hold + 1e-6)
                        { a.Action = new BlockAction { Dir = inp.Block.Value, T = 0, Lock = 0 }; break; }
                        if (aa.T < aa.Info!.Hold) aa.T = Math.Min(aa.Info.Hold, aa.T + dt * rate * 1.25);
                        else if (inp.AttackHeld) aa.Held += dt;
                        if (aa.T >= aa.Info.Hold && !inp.AttackHeld)
                        {
                            aa.Released = true;
                            Events.Add(new SwingEvent { AgentId = a.Id });
                        }
                    }
                    else
                    {
                        aa.T += dt * rate;
                        if (aa.T >= aa.Info!.Duration * 0.92 || (aa.Done && aa.T > aa.Info.LiveTo + 0.12))
                            a.Action = new IdleAction();
                    }
                    break;
                }
                case DrawAction dra:
                {
                    dra.T += dt;
                    double minDraw = w.Cls == WeaponClass.Thrown ? 0.25 : w.Cls == WeaponClass.Crossbow ? 0.2 : 0.35;
                    if (!inp.AttackHeld && dra.T > minDraw)
                    {
                        Shoot(a, dra.T);
                        if (a.Action is DrawAction) a.Action = new ReleaseAction { T = 0 };
                    }
                    else if (inp.Block.HasValue)
                    { a.Action = new IdleAction(); }
                    break;
                }
                case ReloadAction rla:
                    rla.T += dt;
                    if (rla.T >= (w.Reload ?? 3))
                    { a.Loaded = true; a.Action = new IdleAction(); }
                    break;
                case ReleaseAction rea:
                    rea.T += dt;
                    if (rea.T > 0.45 * (100.0 / w.SpeedRating)) a.Action = new IdleAction();
                    break;
                case KickAction ka:
                    ka.T += dt;
                    if (ka.T > 0.75) a.Action = new IdleAction();
                    break;
                case BashAction baa:
                    baa.T += dt;
                    if (baa.T > 0.6) a.Action = new BlockAction { Dir = Dir.Up, T = 0.2, Lock = 0 };
                    break;
                case StaggerAction sta:
                    sta.T += dt;
                    if (sta.T >= sta.Dur) a.Action = new IdleAction();
                    break;
                case DownAction doa:
                    doa.T += dt;
                    if (doa.T >= doa.Dur) a.Action = new IdleAction();
                    break;
                case SwapAction swa:
                    swa.T += dt;
                    if (swa.T > 0.45) { a.WeaponIdx = swa.To; a.Action = new IdleAction(); }
                    break;
                case DeadAction _:
                    break;
            }
        }

        // ── 移动 ─────────────────────────────────────────────────────────
        private void Move(Agent a, double dt)
        {
            var inp = a.Input;
            string actKind = a.Action.Kind;
            double yaw0 = a.Yaw;

            if (AgentOps.Mounted(a))
            {
                var m = a.Mount!;
                var d = m.Def;
                double riding = a.Troop.Skills.Riding;
                double top = Defs.MountTopSpeed(d, riding) * (1 - (a.Troop.Barding?.Weight ?? 0) * 0.0025);
                m.Stun = Math.Max(0, m.Stun - dt);
                if (m.Speed < 3) m.Bumped.Clear();
                double target = m.Stun > 0 ? 0 : inp.Throttle >= 0 ? inp.Throttle * top : inp.Throttle * 3;
                double accel = Defs.MountAccel(d, riding);
                double acc = target > m.Speed ? accel : accel * 1.6;
                m.Speed += Math.Sign(target - m.Speed) * Math.Min(Math.Abs(target - m.Speed), acc * dt);
                double turnRate = Defs.MountTurn(d, riding) * (1 - 0.55 * Math.Min(1, Math.Abs(m.Speed) / top));
                a.Yaw += Math.Max(-1, Math.Min(1, inp.Steer)) * turnRate * dt;
                double want = Math.Max(-LookMax, Math.Min(LookMax, AgentOps.AngleDiff(inp.Face, a.Yaw)));
                double turnUp = (a.Action is AttackAction aa2 && aa2.Released ? 2.5 : 7) * dt;
                a.Look += Math.Max(-turnUp, Math.Min(turnUp, want - a.Look));
                double slope = Terrain?.SlopeDir(a.X, a.Z, AgentOps.ForwardX(a.Yaw), AgentOps.ForwardZ(a.Yaw)) ?? 0;
                double sf = (Terrain?.SpeedFactorFromSlope(slope) ?? 1) * (Terrain?.WadeFactor(a.X, a.Z) ?? 1);
                a.Vx = AgentOps.ForwardX(a.Yaw) * m.Speed * sf;
                a.Vz = AgentOps.ForwardZ(a.Yaw) * m.Speed * sf;
            }
            else
            {
                double slope = Terrain?.SlopeDir(a.X, a.Z, inp.MoveX, inp.MoveZ) ?? 0;
                double max = (inp.Walk ? 2.2 : AgentOps.RunSpeed(a)) * SpeedBonus(a);
                max *= (Terrain?.SpeedFactorFromSlope(slope) ?? 1) * (Terrain?.WadeFactor(a.X, a.Z) ?? 1);
                if (actKind == "attack" || actKind == "draw") max *= 0.55;
                if (actKind == "block") max *= 0.6;
                if (actKind == "stagger" || actKind == "kick") max *= 0.2;
                if (a.Routed) max = AgentOps.RunSpeed(a) * 1.1;
                if (actKind == "stagger" || actKind == "kick") max *= 0.2; // TS 原版对 stagger/kick 应用两次 0.2
                if (actKind == "down") max = 0;
                if (actKind == "reload") max *= 0.15;

                double len = Math.Sqrt(inp.MoveX * inp.MoveX + inp.MoveZ * inp.MoveZ);
                double tx = 0, tz = 0;
                if (len > 0.01)
                {
                    double k = Math.Min(1, len) / len;
                    double fwd = (inp.MoveX * AgentOps.ForwardX(a.Yaw) + inp.MoveZ * AgentOps.ForwardZ(a.Yaw)) * k;
                    double dirMul = fwd < -0.3 ? 0.6 : fwd < 0.3 ? 0.8 : 1.0;
                    tx = inp.MoveX * k * max * dirMul;
                    tz = inp.MoveZ * k * max * dirMul;
                }
                double accelV = actKind == "down" ? 8 : 22;
                double dvx = tx - a.Vx, dvz = tz - a.Vz;
                double dl = Math.Sqrt(dvx * dvx + dvz * dvz);
                double step = Math.Min(dl, accelV * dt);
                if (dl > 1e-6) { a.Vx += dvx / dl * step; a.Vz += dvz / dl * step; }

                bool released = a.Action is AttackAction aa3 && aa3.Released;
                double turn = actKind == "down" ? 0 : actKind == "stagger" ? 1.5 : released ? 3 : 9;
                double diff = AgentOps.AngleDiff(inp.Face, a.Yaw);
                a.Yaw += Math.Sign(diff) * Math.Min(Math.Abs(diff), turn * dt);
                a.Look = 0;
            }

            yawRate[a.Id] = (yaw0 == a.Yaw) ? 0 : AgentOps.AngleDiff(a.Yaw, yaw0) / dt;

            double nx = a.X + a.Vx * dt, nz = a.Z + a.Vz * dt;
            a.Climbing = false; // 围攻梯子逻辑已删除

            if (Terrain?.Passable(nx, nz) ?? true)
            { a.X = nx; a.Z = nz; }
            else if (Terrain?.Passable(nx, a.Z) ?? true)
            { a.X = nx; a.Vz = 0; }
            else if (Terrain?.Passable(a.X, nz) ?? true)
            { a.Z = nz; a.Vx = 0; }
            else
            { a.Vx = a.Vz = 0; if (a.Mount?.Alive == true) a.Mount.Speed = 0; }

            a.Y = Terrain?.Height(a.X, a.Z) ?? 0;

            bool leaving = a.Routed || a.Retreating;
            double half = Terrain?.Half ?? 110;
            double lim = half + (leaving ? 12 : 0);
            if (Math.Abs(a.X) > lim || Math.Abs(a.Z) > lim)
            {
                if (leaving) a.Gone = true;
                a.X = Math.Max(-lim, Math.Min(lim, a.X));
                a.Z = Math.Max(-lim, Math.Min(lim, a.Z));
            }
        }

        /// <summary>无主坐骑缓慢减速漂移。</summary>
        private void RunLoose(Agent a, double dt)
        {
            var h = a.LooseMount!;
            if (h.Speed <= 0) return;
            h.Speed = Math.Max(0, h.Speed - 2.5 * dt);
            h.Yaw += (rng() - 0.5) * dt * 1.5;
            double hx = h.X + AgentOps.ForwardX(h.Yaw) * h.Speed * dt;
            double hz = h.Z + AgentOps.ForwardZ(h.Yaw) * h.Speed * dt;
            if (!(Terrain?.Passable(hx, hz) ?? true)) { h.Speed = 0; return; }
            h.X = hx; h.Z = hz;
            double half = Terrain?.Half ?? 110;
            h.X = Math.Max(-half, Math.Min(half, h.X));
            h.Z = Math.Max(-half, Math.Min(half, h.Z));
        }

        // ── 分离/碰撞 ────────────────────────────────────────────────────
        private void Separate(bool deploying = false)
        {
            foreach (var a in Agents)
            {
                if (!a.Alive || a.Gone) continue;
                foreach (var b in Grid.Near(a.X, a.Z, 2.1))
                {
                    if (b.Id <= a.Id || !b.Alive || b.Gone) continue;
                    double ra = AgentOps.Mounted(a) ? 1.0 : a.Radius;
                    double rb = AgentOps.Mounted(b) ? 1.0 : b.Radius;
                    double dx = b.X - a.X, dz = b.Z - a.Z;
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    double min = ra + rb;
                    if (d >= min || d < 1e-5) continue;
                    if (Math.Abs(a.Y - b.Y) > 1.5) continue;
                    double nx2 = dx / d, nz2 = dz / d;
                    bool am = AgentOps.Mounted(a), bm = AgentOps.Mounted(b);
                    if (!deploying)
                    {
                        if (am != bm)
                        { var rider = am ? a : b; var foot = am ? b : a; if (MountImpact(rider, foot)) continue; }
                        else if (am && bm)
                        { HorseCollision(a, b, nx2, nz2); }
                    }
                    double wa = am && !bm ? 0.1 : !am && bm ? 0.9 : 0.5;
                    if (a.Action is DownAction && !(b.Action is DownAction)) wa = 0.95;
                    if (b.Action is DownAction && !(a.Action is DownAction)) wa = 0.05;
                    double push = min - d;
                    Shove(a, -nx2 * push * wa, -nz2 * push * wa);
                    Shove(b,  nx2 * push * (1 - wa),  nz2 * push * (1 - wa));
                }
            }
        }

        private void Settle()
        {
            foreach (var a in Agents)
            {
                if (!a.Alive || a.Gone || a.Climbing) continue;
                a.Y = Terrain?.Height(a.X, a.Z) ?? 0;
            }
        }

        private void Shove(Agent a, double dx, double dz)
        {
            double nx = a.X + dx, nz = a.Z + dz;
            if (a.Climbing) return;
            if (!(Terrain?.Passable(nx, nz) ?? true)) return;
            a.X = nx; a.Z = nz;
        }

        private void HorseCollision(Agent a, Agent b, double nx, double nz)
        {
            double closing = (a.Vx - b.Vx) * nx + (a.Vz - b.Vz) * nz;
            if (closing < 6) return;
            string key = a.Id < b.Id ? $"{a.Id}:{b.Id}" : $"{b.Id}:{a.Id}";
            if (collidedAt.TryGetValue(key, out double prev) && prev > Time - 1) return;
            collidedAt[key] = Time;
            double sb = closing / 10.0 - 1;
            foreach (var (x, y) in new[] { (a, b), (b, a) })
            {
                double raw = y.Mount!.Def.Charge * Uniform(0.9, 1) * Math.Pow(MeleeSpeedPower, Math.Min(1.5, sb)) * 0.5;
                DamageMount(x, raw, DamageType.Blunt, x.Team == y.Team ? null : y);
                if (x.Mount?.Alive == true) x.Mount.Speed *= 0.4;
            }
        }

        private void ToggleMount(Agent a)
        {
            if (AgentOps.Mounted(a))
            {
                if (Math.Abs(a.Mount!.Speed) > 2) return;
                var m = a.Mount;
                m.Alive = false;
                a.LooseMount = new LooseMountInfo { Kind = m.Kind, X = a.X, Z = a.Z, Yaw = a.Yaw, Speed = 0, Hp = m.Hp, Def = m.Def };
                var (tx2, tz2) = AgentOps.ToWorld(a.X, a.Z, a.Yaw, 1.3, 0);
                a.X = tx2; a.Z = tz2;
                a.Action = new StaggerAction { T = 0, Dur = 0.9, Heavy = false };
                Events.Add(new DismountEvent { AgentId = a.Id, Voluntary = true });
                if (WeaponsData.Weapons[a.Troop.Weapons[a.WeaponIdx]].Couch)
                {
                    int idx2 = Array.FindIndex(a.Troop.Weapons, id => !WeaponsData.Weapons[id].Couch);
                    if (idx2 >= 0) a.WeaponIdx = idx2;
                }
                return;
            }
            Agent? best = null; double bd = 2.5;
            foreach (var o in Agents)
            {
                var h = o.LooseMount;
                if (h == null || h.Speed > 1.5) continue;
                double dd = Math.Sqrt((h.X - a.X) * (h.X - a.X) + (h.Z - a.Z) * (h.Z - a.Z));
                if (dd < bd) { bd = dd; best = o; }
            }
            if (best == null) return;
            var lh = best.LooseMount!;
            a.Mount = new MountState { Def = lh.Def, Kind = lh.Kind, Hp = lh.Hp, MaxHp = lh.Def.Hp, Speed = 0, Alive = true, Stun = 0, Bumped = new() };
            best.LooseMount = best == a ? null : a.LooseMount;
            if (best != a) a.LooseMount = null;
            a.X = lh.X; a.Z = lh.Z; a.Yaw = lh.Yaw;
            a.DeadMount = null;
            a.Action = new StaggerAction { T = 0, Dur = 1.0, Heavy = false };
            Events.Add(new MountEvent { AgentId = a.Id, From = best.Id });
        }

        private bool MountImpact(Agent rider, Agent foot)
        {
            var m = rider.Mount!;
            double charge = m.Def.Charge;
            double dx = foot.X - rider.X, dz = foot.Z - rider.Z;
            double ahead = dx * AgentOps.ForwardX(rider.Yaw) + dz * AgentOps.ForwardZ(rider.Yaw);
            double knockSpeed = 120.0 / charge;
            if (ahead < 0 || m.Speed < knockSpeed * 0.6) { m.Speed *= 0.9; return false; }
            if (foot.Team == rider.Team) { m.Speed *= 0.6; return false; }
            if (m.Bumped.Contains(foot.Id)) return true;
            var fw = AgentOps.WeaponOf(foot);
            bool facing2 = Math.Abs(AgentOps.AngleDiff(AgentOps.YawTo(-dx, -dz), foot.Yaw)) < 0.7;
            bool braced2 = fw.Brace && facing2 && (foot.Braced
                || (foot.Action is AttackAction aa4 && !aa4.Released && aa4.Dir == Dir.Down));
            if (braced2)
            {
                double sb = Math.Min(2, m.Speed / CouchSpeedRef - 1);
                double raw = MeleeRaw(foot, fw, true, 1.5, sb) * BonusDamage(foot, fw, true, false, true);
                DamageMount(rider, raw, DamageType.Pierce, foot);
                Events.Add(new BraceEvent { Spear = foot.Id, Target = rider.Id });
                if (AgentOps.Mounted(rider)) Rear(rider);
                foot.Action = new IdleAction();
                foot.Braced = false;
                return false;
            }
            if (m.Speed < knockSpeed) { m.Speed *= 0.7; return false; }
            m.Bumped.Add(foot.Id);
            double sb2 = m.Speed / knockSpeed - 1;
            double raw2 = charge * Uniform(0.9, 1) * Math.Pow(MeleeSpeedPower, Math.Min(2, sb2)) * 0.6;
            Damage(foot, raw2, DamageType.Blunt, rider, HitPart.Body, HitKind.Bump, sb2);
            if (foot.Alive)
            {
                KnockDown(foot, 1.6);
                foot.Vx = AgentOps.ForwardX(rider.Yaw) * 5;
                foot.Vz = AgentOps.ForwardZ(rider.Yaw) * 5;
            }
            m.Speed *= 1 - Math.Min(0.6, 8.0 / charge);
            return true;
        }

        // ── 行动解决 ─────────────────────────────────────────────────────
        private void ResolveActions(Agent a, double dt)
        {
            if (a.Action is AttackAction aa5 && aa5.Released && !aa5.Done)
            {
                Sweep(a, aa5, dt);
            }
            else if (a.Action is KickAction ka2 && !ka2.Done && ka2.T >= 0.35)
            {
                ka2.Done = true;
                var t = NearestInFront(a, 1.6, 0.8);
                if (t != null)
                {
                    Damage(t, 5 * Uniform(0.9, 1), DamageType.Blunt, a, HitPart.Body, HitKind.Kick, 0);
                    if (t.Alive && !AgentOps.Mounted(t))
                    {
                        Stagger(t, 0.9, true);
                        t.Vx += AgentOps.ForwardX(a.Yaw) * 3;
                        t.Vz += AgentOps.ForwardZ(a.Yaw) * 3;
                    }
                }
            }
            else if (a.Action is BashAction baa2 && !baa2.Done && baa2.T >= 0.2)
            {
                baa2.Done = true;
                var t = NearestInFront(a, 1.5, 0.7);
                if (t != null && !AgentOps.Mounted(t))
                {
                    bool shielded = t.Action is BlockAction && AgentOps.HasShield(t)
                        && Facing(t, a, t.Troop.Shield!.Coverage);
                    if (shielded)
                        Events.Add(new BlockEvent { Attacker = a.Id, Target = t.Id, Shield = true, Parry = false, X = t.X, Y = 1.2, Z = t.Z });
                    else
                    {
                        Damage(t, 2, DamageType.Blunt, a, HitPart.Body, HitKind.Kick, 0);
                        if (t.Alive) Stagger(t, 0.8, true);
                        t.Vx += AgentOps.ForwardX(a.Yaw) * 2;
                        t.Vz += AgentOps.ForwardZ(a.Yaw) * 2;
                    }
                }
            }
            else if (a.Action is CouchAction ca2 && ca2.Cooldown <= 0)
            {
                CouchHit(a, ca2);
            }
        }

        private Agent? NearestInFront(Agent a, double range, double arc)
        {
            Agent? best = null; double bd = range;
            foreach (var b in Grid.Near(a.X, a.Z, range + 1))
            {
                if (b == a || !b.Alive || b.Gone || b.Team == a.Team) continue;
                double dx = b.X - a.X, dz = b.Z - a.Z;
                if (Math.Abs(b.Y - a.Y) > 1.2) continue;
                double d = Math.Sqrt(dx * dx + dz * dz) - b.Radius;
                if (d > bd) continue;
                if (Math.Abs(AgentOps.AngleDiff(AgentOps.YawTo(dx, dz), a.Yaw)) > arc) continue;
                best = b; bd = d;
            }
            return best;
        }

        // ── 躯体位置 ─────────────────────────────────────────────────────
        private (double X, double Y, double Z) BodyOrigin(Agent a)
        {
            if (!AgentOps.Mounted(a)) return (a.X, a.Y, a.Z);
            var s = GetSaddle(a.Mount!.Kind);
            var (wx, wz) = AgentOps.ToWorld(a.X, a.Z, a.Yaw, s.Px, s.Pz);
            return (wx, a.Y + s.Py, wz);
        }

        private static (double Px, double Py, double Pz) GetSaddle(string kind)
            => kind == "wolf" ? SaddleWolf : SaddleHorse;

        private static double GetMountLength(string kind)
            => kind == "wolf" ? MountLengthWolf : MountLengthHorse;

        private List<Capsule> Capsules(Agent a)
        {
            var out_ = new List<Capsule>();
            var (ox, oy, oz) = BodyOrigin(a);
            if (AgentOps.Mounted(a))
            {
                out_.Add(new Capsule { Ax = ox, Ay = oy + 0.2, Az = oz, Bx = ox, By = oy + 1.55, Bz = oz, R = 0.42, IsMount = false });
                double len = GetMountLength(a.Mount!.Kind);
                double front = -len * 0.5, back = len * 0.36;
                double y = a.Y + GetSaddle(a.Mount.Kind).Py * 0.85;
                var (fx, fz) = AgentOps.ToWorld(a.X, a.Z, a.Yaw, 0, front);
                var (bx, bz) = AgentOps.ToWorld(a.X, a.Z, a.Yaw, 0, back);
                out_.Add(new Capsule { Ax = fx, Ay = y, Az = fz, Bx = bx, By = y, Bz = bz, R = 0.5, IsMount = true });
            }
            else if (a.Action is DownAction)
            {
                var (hx, hz) = AgentOps.ToWorld(a.X, a.Z, a.Yaw, 0, 1.4);
                out_.Add(new Capsule { Ax = ox, Ay = oy + 0.3, Az = oz, Bx = hx, By = oy + 0.3, Bz = hz, R = 0.35, IsMount = false });
            }
            else
            {
                out_.Add(new Capsule { Ax = ox, Ay = oy + 0.35, Az = oz, Bx = ox, By = oy + BodyTop(a) - a.Radius, Bz = oz, R = a.Radius, IsMount = false });
            }
            return out_;
        }

        private double HeadBone(Agent a)
        {
            var w = AgentOps.WeaponOf(a);
            var set = Defs.GetClipSet(a.Troop.Model, w.Cls, w.OneHandedPolearm);
            if (AgentOps.Mounted(a)) return 1.6;
            return a.Action switch
            {
                AttackAction aa6 => SwingData.HeadHeight(a.Troop.Model, aa6.Clip, aa6.T),
                BlockAction  _   => SwingData.HeadHeight(a.Troop.Model, AgentOps.HasShield(a) ? "ShieldBlock" : set.Block, 0.22),
                StaggerAction _  => SwingData.HeadHeight(a.Troop.Model, "CombatWound", Math.Min(0.45, ((StaggerAction)a.Action).T)),
                KickAction _     => SwingData.HeadHeight(a.Troop.Model, "Kick", ((KickAction)a.Action).T),
                _ => SwingData.HeadHeight(a.Troop.Model, AgentOps.SpeedOf(a) > 3.4 ? "Run" : w.Cls == WeaponClass.Bow ? "ReadyBow" : set.Ready, 0.25)
            };
        }

        private double BodyTop(Agent a) => Defs.RaceHeight[a.Troop.Race] + 0.05;

        private HitPart HitPartOf(Agent b, double y, double x, double z)
        {
            if (b.Action is DownAction) return HitPart.Body;
            double h = y - OriginY(b);
            double head = HeadBone(b);
            if (h >= head - HeadBelow)           return HitPart.Head;
            if (h >= head - HeadBelow - 0.12)    return HitPart.Neck;
            if (h >= head - HeadBelow - 0.30)    return HitPart.Shoulder;
            if (!AgentOps.Mounted(b) && h < 0.85) return HitPart.Leg;
            double side = Math.Abs((x - b.X) * Math.Cos(b.Yaw) - (z - b.Z) * Math.Sin(b.Yaw));
            if (side > b.Radius * 0.75) return HitPart.Arm;
            return HitPart.Body;
        }

        // ── 扫刀 ─────────────────────────────────────────────────────────
        private void Sweep(Agent a, AttackAction act, double dt)
        {
            var info = act.Info!;
            double rate = AgentOps.AttackSpeed(a, AgentOps.WeaponOf(a)) * ClipTempo;
            double t1 = act.T;
            double t0 = Math.Max(info.LiveFrom, t1 - dt * rate);
            if (t1 < info.LiveFrom || t0 > info.LiveTo) return;
            var w = AgentOps.WeaponOf(a);
            double reach = w.Length + 0.15;
            bool m = AgentOps.Mounted(a);
            var (ox, oy, oz) = BodyOrigin(a);
            double cos = Math.Cos(AgentOps.AimYaw(a)), sin = Math.Sin(AgentOps.AimYaw(a));

            var near = new List<(Agent b, List<Capsule> caps)>();
            foreach (var b in Agents)
            {
                if (b == a || !b.Alive || b.Gone || act.Struck.Contains(b.Id)) continue;
                double dd = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
                if (dd >= reach + 4.5) continue;
                near.Add((b, Capsules(b)));
            }
            if (near.Count == 0) return;

            double tEnd = Math.Min(t1, info.LiveTo);
            int steps = Math.Max(1, (int)Math.Ceiling((tEnd - t0) / (1.0 / 160)));
            for (int s = 0; s <= steps; s++)
            {
                double t = t0 + (tEnd - t0) * s / steps;
                // 取 reach 整段（grip→tip）
                SwingData.BladeAt(a.Troop.Model, act.Clip, t, reach, m, out bladeTmp);
                SwingData.BladeAt(a.Troop.Model, act.Clip, t, 0,     m, out bladeTmp2); // grip

                double gx = bladeTmp2.X, gy = bladeTmp2.Y, gz = bladeTmp2.Z;
                double px = bladeTmp.X,  py = bladeTmp.Y,  pz = bladeTmp.Z;

                // 沿刀刃 4 个采样点，从刀尖向刀柄
                for (int k = 4; k >= 1; k--)
                {
                    double f = k / 4.0;
                    double lx = gx + (px - gx) * f;
                    double ly = gy + (py - gy) * f;
                    double lz = gz + (pz - gz) * f;
                    double wx2 = ox + lx * cos + lz * sin;
                    double wy2 = oy + ly;
                    double wz2 = oz - lx * sin + lz * cos;

                    foreach (var (b, caps) in near)
                    {
                        if (act.Struck.Contains(b.Id)) continue;
                        foreach (var c in caps)
                        {
                            if (DistToSegment(wx2, wy2, wz2, c) > c.R) continue;
                            act.Struck.Add(b.Id);
                            if (b.Team == a.Team)
                            {
                                act.Done = true;
                                Stagger(a, 0.35);
                                return;
                            }
                            double sb = MeleeSpeedBonus(a, act, t, f, reach, wx2, wz2, b);
                            MeleeHit(a, act, b, c.IsMount, sb, wx2, wy2, wz2);
                            act.Done = true;
                            return;
                        }
                    }
                }
            }
        }

        private double MeleeSpeedBonus(Agent a, AttackAction act, double t, double along, double reach, double wx, double wz, Agent b)
        {
            double rate = AgentOps.AttackSpeed(a, AgentOps.WeaponOf(a)) * ClipTempo;
            bool m = AgentOps.Mounted(a);
            double eps = 1.0 / 120;
            SwingData.BladeAt(a.Troop.Model, act.Clip, t,                  reach, m, out bladeTmp);
            SwingData.BladeAt(a.Troop.Model, act.Clip, Math.Max(0, t-eps), reach, m, out bladePrev);
            SwingData.BladeAt(a.Troop.Model, act.Clip, t,                  0,     m, out bladeTmp2); // grip at t
            double gripX = bladeTmp2.X, gripY = bladeTmp2.Y, gripZ = bladeTmp2.Z;
            double tipX  = bladeTmp.X,  tipY  = bladeTmp.Y,  tipZ  = bladeTmp.Z;
            double prevGripX, prevGripY, prevGripZ;
            {
                SwingData.BladeAt(a.Troop.Model, act.Clip, Math.Max(0, t-eps), 0, m, out var pg);
                prevGripX = pg.X; prevGripY = pg.Y; prevGripZ = pg.Z;
            }

            // 模型空间中 along 分量处的位置（t 和 t-eps）
            double ptX  = prevGripX + (bladePrev.X - prevGripX) * along;
            double ptY  = prevGripY + (bladePrev.Y - prevGripY) * along;
            double ptZ  = prevGripZ + (bladePrev.Z - prevGripZ) * along;
            double curX = gripX + (tipX - gripX) * along;
            double curY = gripY + (tipY - gripY) * along;
            double curZ = gripZ + (tipZ - gripZ) * along;

            // 模型空间速度，再乘攻速
            double mvx = ((curX - ptX) / eps) * rate;
            double mvy = ((curY - ptY) / eps) * rate;
            double mvz = ((curZ - ptZ) / eps) * rate;
            double cos = Math.Cos(AgentOps.AimYaw(a)), sin = Math.Sin(AgentOps.AimYaw(a));
            double omega = yawRate.TryGetValue(a.Id, out double yr) ? yr : 0;
            var (ox, _, oz) = BodyOrigin(a);
            double vx = mvx * cos + mvz * sin + a.Vx + omega * (wz - oz);
            double vy = mvy;
            double vz = -mvx * sin + mvz * cos + a.Vz - omega * (wx - ox);
            vx -= b.Vx; vz -= b.Vz;
            double v = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double nominal = NominalTipSpeed(a, act, reach) * rate;
            return Math.Max(-1, Math.Min(2, v / Math.Max(1, nominal) - 1));
        }

        private double GlanceFactor(Agent a, AttackAction act, Agent b)
        {
            if (act.Dir == Dir.Down) return 1;
            double reach = AgentOps.WeaponOf(a).Length + 0.15;
            double t = act.T;
            bool m = AgentOps.Mounted(a);
            SwingData.BladeAt(a.Troop.Model, act.Clip, t,                    reach, m, out bladeTmp);
            SwingData.BladeAt(a.Troop.Model, act.Clip, Math.Max(0, t-1.0/120), reach, m, out bladePrev);
            double vx = bladeTmp.X - bladePrev.X, vy = bladeTmp.Y - bladePrev.Y, vz = bladeTmp.Z - bladePrev.Z;
            SwingData.BladeAt(a.Troop.Model, act.Clip, t, 0, m, out bladeTmp2); // grip
            double bx = bladeTmp.X - bladeTmp2.X, by2 = bladeTmp.Y - bladeTmp2.Y, bz = bladeTmp.Z - bladeTmp2.Z;
            double vl = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double bl = Math.Sqrt(bx * bx + by2 * by2 + bz * bz);
            if (vl < 1e-6 || bl < 1e-6) return 1;
            double along2 = Math.Abs((vx * bx + vy * by2 + vz * bz) / (vl * bl));
            double k = along2 > 0.7 ? GlanceMin + (1 - GlanceMin) * (1 - (along2 - 0.7) / 0.3) : 1.0;
            double dist = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)) - b.Radius;
            double reachK = dist > reach + 0.6 ? 0.6 : 1.0;
            return Math.Max(GlanceMin, Math.Min(k, reachK));
        }

        private double NominalTipSpeed(Agent a, AttackAction act, double reach)
        {
            bool m = AgentOps.Mounted(a);
            string key = $"{a.Troop.Model}|{act.Clip}|{reach:F2}|{m}";
            if (nominalCache.TryGetValue(key, out double v)) return v;
            var info = act.Info!;
            int n = 32;
            double step = (info.LiveTo - info.LiveFrom) / n;
            double peak = 0;
            SwingData.BladeAt(a.Troop.Model, act.Clip, info.LiveFrom, reach, m, out var o1);
            for (int i = 1; i <= n; i++)
            {
                SwingData.BladeAt(a.Troop.Model, act.Clip, info.LiveFrom + step * i, reach, m, out var o2);
                double dd = Math.Sqrt((o2.X - o1.X) * (o2.X - o1.X) + (o2.Y - o1.Y) * (o2.Y - o1.Y) + (o2.Z - o1.Z) * (o2.Z - o1.Z)) / Math.Max(1e-4, step);
                if (dd > peak) peak = dd;
                o1 = o2;
            }
            v = peak * NominalOfPeak;
            nominalCache[key] = v;
            return v;
        }

        // ── 近战伤害 ──────────────────────────────────────────────────────
        private double MeleeRaw(Agent a, Weapon w, bool thrust, double holdMult, double sb)
        {
            var s = a.Troop.Skills;
            double raw = (thrust ? w.Thrust : w.Swing) * Uniform(0.9, 1) * holdMult;
            raw *= 1 + PowerStrike * s.PowerStrike;
            raw *= Math.Pow(ProfStep, (AgentOps.Proficiency(a, w) + ProfBonus(a, w) - 100) / 100.0);
            if (AgentOps.Mounted(a) && (w.Cls == WeaponClass.TwoHanded || w.Cls == WeaponClass.Polearm) && !w.Couch)
                raw *= 0.85;
            return raw * Math.Pow(MeleeSpeedPower, sb);
        }

        private void MeleeHit(Agent a, AttackAction act, Agent b, bool isMount, double sbIn, double x, double y, double z)
        {
            double sb = sbIn;
            var w = AgentOps.WeaponOf(a);
            bool thrust = act.Dir == Dir.Down;
            DamageType type = thrust ? w.ThrustType : w.SwingType;
            sb = BonusSpeed(a, w, sb);
            double raw = MeleeRaw(a, w, thrust, HoldMultiplier(act.Age), sb) * BonusDamage(a, w, thrust, false, AgentOps.Mounted(b));
            double glance = GlanceFactor(a, act, b);
            if (glance < 1) raw *= glance;

            if (!isMount)
            {
                // 反击窗口
                if (b.Action is AttackAction ba5 && !ba5.Released && ba5.Age < ChamberWindow
                    && ba5.Dir == Defs.BlockFor(act.Dir) && !act.Crush && Facing(b, a, 1.1))
                {
                    Events.Add(new ChamberEvent { Attacker = a.Id, Target = b.Id, X = x, Y = y, Z = z });
                    Stagger(a, 0.55);
                    ba5.T = ba5.Info!.Hold;
                    ba5.Released = true;
                    Events.Add(new SwingEvent { AgentId = b.Id });
                    return;
                }
                bool blocked2 = Blocks(b, a, act.Dir);
                if (blocked2 && !act.Crush) { Blocked(a, b, w, raw, x, y, z); return; }
                if (blocked2 && act.Crush)
                {
                    if (AgentOps.HasShield(b)) HurtShield(b, w, raw);
                    Damage(b, raw, type, a, HitPartOf(b, y, x, z), (HitKind)(int)type, sb, x, y, z, w, true);
                    return;
                }
            }
            if (isMount)
            {
                DamageMount(b, raw, type, a);
                if (thrust && b.Mount?.Alive == true && Math.Abs(b.Mount.Speed) > 4) Rear(b);
            }
            else
                Damage(b, raw, type, a, HitPartOf(b, y, x, z), (HitKind)(int)type, sb, x, y, z, w);
        }

        // ── 冲刺攻击 ──────────────────────────────────────────────────────
        private void CouchHit(Agent a, CouchAction act)
        {
            var (ox, oy, oz) = BodyOrigin(a);
            var w = AgentOps.WeaponOf(a);
            double reach = w.Length + 0.6;
            var (tx2, tz2) = AgentOps.ToWorld(ox, oz, a.Yaw, 0.35, -reach);
            double[] tips = { oy - 0.45, oy + 0.35 };
            double ty2 = tips[0];
            foreach (var b in Agents)
            {
                if (b == a || !b.Alive || b.Gone || b.Team == a.Team) continue;
                if (Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)) > reach + 3) continue;
                foreach (var c in Capsules(b))
                {
                    double? hitAt = null;
                    foreach (var yy in tips) { if (DistToSegment(tx2, yy, tz2, c) <= c.R + 0.15) { hitAt = yy; break; } }
                    if (!hitAt.HasValue) continue;
                    ty2 = hitAt.Value;
                    double dx2 = b.X - a.X, dz2 = b.Z - a.Z;
                    double dl2 = Math.Sqrt(dx2 * dx2 + dz2 * dz2); if (dl2 < 1e-6) dl2 = 1;
                    double rel = Math.Max(0, ((a.Vx - b.Vx) * dx2 + (a.Vz - b.Vz) * dz2) / dl2);
                    double sb3 = BonusSpeed(a, w, Math.Min(3, rel / CouchSpeedRef - 1));
                    double raw3 = MeleeRaw(a, w, true, 1, 0) * Math.Pow(MeleeSpeedPower, sb3) * BonusDamage(a, w, true, false, AgentOps.Mounted(b));
                    if (c.IsMount) DamageMount(b, raw3, DamageType.Pierce, a);
                    else if (AgentOps.HasShield(b) && b.Action is BlockAction && Facing(b, a, b.Troop.Shield!.Coverage))
                    {
                        Events.Add(new BlockEvent { Attacker = a.Id, Target = b.Id, Shield = true, Parry = false, X = tx2, Y = ty2, Z = tz2 });
                        HurtShield(b, w, raw3);
                        Stagger(b, 0.9, true);
                    }
                    else Damage(b, raw3, DamageType.Pierce, a, HitPartOf(b, ty2, tx2, tz2), HitKind.Couch, sb3, tx2, ty2, tz2);
                    act.Cooldown = 1.2;
                    return;
                }
            }
        }

        // ── 伤害应用 ──────────────────────────────────────────────────────
        public void Damage(
            Agent b, double raw, DamageType type, Agent? by, HitPart part, HitKind kind, double sb,
            double x = double.NaN, double y = double.NaN, double z = double.NaN,
            Weapon? weapon = null, bool crush = false)
        {
            if (!b.Alive) return;
            if (double.IsNaN(x)) x = b.X;
            if (double.IsNaN(y)) y = OriginY(b) + 1.3;
            if (double.IsNaN(z)) z = b.Z;

            double dmgTakenK = BonusTaken(b, kind == HitKind.Pierce && weapon == null);
            var A = b.Troop.Armor;
            double armor = part == HitPart.Head      ? A.Head
                         : part == HitPart.Neck      ? A.Head * 0.5
                         : part == HitPart.Shoulder  ? Math.Max(A.Body, A.Shoulder ?? 0)
                         : part == HitPart.Arm       ? (A.Arm ?? A.Body * 0.5)
                         : part == HitPart.Leg       ? A.Leg
                         : A.Body;

            double locMult = part == HitPart.Head ? HeadMult : part == HitPart.Neck ? NeckMult
                           : part == HitPart.Arm  ? ArmMult  : part == HitPart.Leg  ? LegMult : 1.0;
            int ti = (int)type;
            double dmg = raw * DamageScale * dmgTakenK * locMult;
            dmg = Math.Max(0, dmg - armor * Soak[ti] * Uniform(0.5, 1));
            dmg *= Math.Max(0, 1 - (armor / 100.0) * Reduce[ti] * Uniform(0.5, 1));
            dmg = Math.Round(dmg);
            b.Hp -= dmg;
            b.SinceHit = 0;
            bool killed = b.Hp <= 0;
            Events.Add(new HitEvent { Attacker = by?.Id ?? 0, Target = b.Id, Damage = dmg, Part = part, Kind = kind, X = x, Y = y, Z = z, Killed = killed, SpeedBonus = sb, Crush = crush });
            if (killed) { Kill(b, by); return; }

            if (weapon?.Knockdown == true && type == DamageType.Blunt && raw > KnockdownMin && !AgentOps.Mounted(b))
            {
                double chance = KnockdownMaxChance * (Math.Min(weapon.Weight, 6) / 6.0) * (Math.Min(raw, 40) / 40.0);
                if (crush || rng() < chance) { KnockDown(b, 1.8); return; }
            }
            string k2 = b.Action.Kind;
            if (dmg <= 0) return;
            if (k2 == "attack" || k2 == "draw" || k2 == "block" || k2 == "idle" || k2 == "couch" || k2 == "release" || k2 == "reload")
            {
                if (k2 == "attack" || k2 == "draw") Events.Add(new InterruptEvent { Target = b.Id });
                Stagger(b, type == DamageType.Blunt ? 0.6 : 0.42, crush || (type == DamageType.Blunt && dmg > 25));
            }
        }

        private void DamageMount(Agent rider, double raw, DamageType type, Agent? by)
        {
            var m = rider.Mount;
            if (m == null || !m.Alive) return;
            double armor2 = m.Def.Armor + (rider.Troop.Barding?.Armor ?? 0);
            int ti = (int)type;
            double dmg2 = Math.Max(0, raw - armor2 * Soak[ti] * Uniform(0.5, 1));
            dmg2 = Math.Round(dmg2 * Math.Max(0, 1 - (armor2 / 100.0) * Reduce[ti] * Uniform(0.5, 1)));
            m.Hp -= dmg2;
            var (xm, zm) = AgentOps.ToWorld(rider.X, rider.Z, rider.Yaw, 0, -0.6);
            Events.Add(new HitEvent { Attacker = by?.Id ?? 0, Target = rider.Id, Damage = dmg2, Part = HitPart.Mount, Kind = (HitKind)(int)type, X = xm, Y = rider.Y + 1.3, Z = zm, Killed = m.Hp <= 0, SpeedBonus = 0 });
            if (m.Hp <= 0)
            {
                m.Alive = false;
                rider.DeadMount = new DeadMountInfo { Kind = m.Kind, X = rider.X, Z = rider.Z, Yaw = rider.Yaw };
                Events.Add(new DismountEvent { AgentId = rider.Id });
                rider.Vx = AgentOps.ForwardX(rider.Yaw) * Math.Max(0, m.Speed) * 0.4;
                rider.Vz = AgentOps.ForwardZ(rider.Yaw) * Math.Max(0, m.Speed) * 0.4;
                int idx3 = Array.FindIndex(rider.Troop.Weapons, id => !WeaponsData.Weapons[id].Couch);
                if (WeaponsData.Weapons[rider.Troop.Weapons[rider.WeaponIdx]].Couch && idx3 >= 0) rider.WeaponIdx = idx3;
                Damage(rider, 6 + Math.Max(0, m.Speed), DamageType.Blunt, by, HitPart.Body, HitKind.Bump, 0);
                if (rider.Alive) KnockDown(rider, 1.6);
            }
        }

        private void Kill(Agent b, Agent? by)
        {
            b.Alive = false; b.Hp = 0;
            b.Action = new DeadAction { T = 0 };
            if (b.Mount?.Alive == true)
            {
                b.Mount.Alive = false;
                b.LooseMount = new LooseMountInfo { Kind = b.Mount.Kind, X = b.X, Z = b.Z, Yaw = b.Yaw, Speed = Math.Max(4, b.Mount.Speed), Hp = b.Mount.Hp, Def = b.Mount.Def };
            }
            if (by != null) by.Kills++;
            Events.Add(new DeathEvent { AgentId = b.Id, Killer = by?.Id ?? 0 });
        }

        // ── 格挡与硬直 ────────────────────────────────────────────────────
        private void Blocked(Agent a, Agent b, Weapon w, double raw, double x, double y, double z)
        {
            bool shield2 = AgentOps.HasShield(b);
            double parryT = b.Action is BlockAction bbb ? bbb.T : 0;
            bool parry = !shield2 && parryT < ParryWindow;
            Events.Add(new BlockEvent { Attacker = a.Id, Target = b.Id, Shield = shield2, Parry = parry, X = x, Y = y, Z = z });
            if (shield2) HurtShield(b, w, raw);
            bool heavy = w.Cls == WeaponClass.TwoHanded || w.Weight >= 4;
            Stagger(a, 0.32 + w.Weight * 0.03 + (parry ? 0.25 : 0));
            if (b.Action is BlockAction bblk && !parry)
                bblk.Lock = heavy ? 0.45 : 0.2;
            double push = heavy ? 2.5 : 1.2;
            b.Vx += AgentOps.ForwardX(a.Yaw) * push;
            b.Vz += AgentOps.ForwardZ(a.Yaw) * push;
        }

        private void HurtShield(Agent b, Weapon w, double raw)
        {
            var sh = b.Troop.Shield!;
            double dmg3 = Math.Max(0, raw * (w.ShieldBonus ?? 1) - sh.Resistance) * (1 - 0.08 * b.Troop.Skills.Shield);
            b.ShieldHp -= dmg3;
            if (b.ShieldHp <= 0) { b.ShieldHp = 0; Events.Add(new ShieldBrokenEvent { Target = b.Id }); }
        }

        private bool Blocks(Agent b, Agent a, Dir dir)
        {
            if (!(b.Action is BlockAction bblk2)) return false;
            double bearing2 = Math.Abs(AgentOps.AngleDiff(AgentOps.YawTo(a.X - b.X, a.Z - b.Z), AgentOps.AimYaw(b)));
            if (AgentOps.HasShield(b))
            {
                var sh = b.Troop.Shield!;
                double lvl = b.Troop.Skills.Shield;
                double raise = 0.12 * (80.0 / sh.Speed) * (1 - 0.05 * lvl);
                return bearing2 < sh.Coverage + 0.04 * lvl && bblk2.T > raise;
            }
            if (bearing2 > 1.1) return false;
            double raise2 = 0.06 * (100.0 / AgentOps.WeaponOf(b).SpeedRating);
            return bblk2.Dir == Defs.BlockFor(dir) && bblk2.T > raise2;
        }

        private void Stagger(Agent a, double dur, bool heavy = false)
        {
            if (!a.Alive || a.Action is DownAction) return;
            a.Action = new StaggerAction { T = 0, Dur = dur, Heavy = heavy };
        }

        private void KnockDown(Agent a, double dur)
        {
            if (!a.Alive || AgentOps.Mounted(a)) return;
            a.Action = new DownAction { T = 0, Dur = dur };
            Events.Add(new KnockdownEvent { Target = a.Id });
        }

        private void Rear(Agent rider)
        {
            var m = rider.Mount!;
            m.Speed = 0; m.Stun = 1.4;
            if (!(rider.Action is DeadAction)) rider.Action = new StaggerAction { T = 0, Dur = 1.2, Heavy = false };
            Events.Add(new RearEvent { AgentId = rider.Id });
        }

        private bool Facing(Agent b, Agent a, double arc)
            => Math.Abs(AgentOps.AngleDiff(AgentOps.YawTo(a.X - b.X, a.Z - b.Z), AgentOps.AimYaw(b))) < arc;

        // ── 弓箭 ─────────────────────────────────────────────────────────
        public double Spread(Agent a, double drawT)
        {
            var w = AgentOps.WeaponOf(a);
            if (w.Cls == WeaponClass.Thrown)
                return 0.012 + (100 - (w.Accuracy ?? 90)) * 0.002 + Math.Min(1, AgentOps.SpeedOf(a) / 5) * 0.02;
            if (w.Cls == WeaponClass.Crossbow)
            {
                double prof = AgentOps.Proficiency(a, w);
                return 0.004 + (100 - (w.Accuracy ?? 95)) * 0.002 + 0.02 * Math.Max(0, Math.Min(1, (240 - prof) / 200.0))
                       + (drawT < 0.4 ? (0.4 - drawT) * 0.06 : 0) + Math.Min(1, AgentOps.SpeedOf(a) / 5) * 0.03;
            }
            double prof2 = AgentOps.Proficiency(a, w);
            double s = 0.006 + (100 - (w.Accuracy ?? 95)) * 0.002 + 0.03 * Math.Max(0, Math.Min(1, (260 - prof2) / 220.0));
            double settle = 0.9 * (100.0 / w.SpeedRating);
            if (drawT < settle) s += ((settle - drawT) / settle) * 0.07;
            if (drawT > settle + 2.5) s += Math.Min(0.06, (drawT - settle - 2.5) * 0.02);
            if (AgentOps.Mounted(a))
            {
                double top2 = Defs.MountTopSpeed(a.Mount!.Def, a.Troop.Skills.Riding);
                s += Math.Min(1, Math.Abs(a.Mount.Speed) / top2) * 0.045 * (1 - a.Troop.Skills.HorseArchery / 10.0);
            }
            else s += Math.Min(1, AgentOps.SpeedOf(a) / 5) * 0.035;
            return s;
        }

        private void Shoot(Agent a, double drawT)
        {
            var w = AgentOps.WeaponOf(a);
            bool thrown = w.Cls == WeaponClass.Thrown;
            if (thrown) { if (a.Ammo[a.WeaponIdx] <= 0) return; a.Ammo[a.WeaponIdx]--; }
            else if (w.Cls == WeaponClass.Crossbow) { if (!a.Loaded || a.Arrows <= 0) return; a.Loaded = false; a.Arrows--; }
            else { if (a.Arrows <= 0) return; a.Arrows--; }

            var s = a.Troop.Skills;
            double sp = Spread(a, drawT);
            var (ox2, oy2, oz2) = BodyOrigin(a);
            double v = w.MissileSpeed ?? ArrowSpeedConst;
            double sy = oy2 + ReleaseHeight;
            var aim = a.Input.Aim;
            double yaw2 = a.Input.Face, pitch = a.Input.AimPitch;
            if (aim.HasValue) yaw2 = AgentOps.YawTo(aim.Value.X - ox2, aim.Value.Z - oz2);
            double sx = ox2 + AgentOps.ForwardX(yaw2) * ReleaseAhead;
            double sz = oz2 + AgentOps.ForwardZ(yaw2) * ReleaseAhead;
            if (aim.HasValue)
            {
                double dx3 = aim.Value.X - sx, dz3 = aim.Value.Z - sz;
                double range2 = Math.Sqrt(dx3 * dx3 + dz3 * dz3);
                if (range2 > 0.5)
                {
                    yaw2 = AgentOps.YawTo(dx3, dz3);
                    if (a.Input.Lob)
                        pitch = BallisticPitchHigh(range2, aim.Value.Y - sy, v) ?? BallisticPitchLow(range2, aim.Value.Y - sy, v) ?? Math.Atan2(aim.Value.Y - sy, range2);
                    else
                        pitch = BallisticPitchLow(range2, aim.Value.Y - sy, v) ?? Math.Atan2(aim.Value.Y - sy, range2);
                }
            }
            yaw2   += (rng() - 0.5) * 2 * sp;
            pitch  += (rng() - 0.5) * 2 * sp;
            double cp2 = Math.Cos(pitch);
            double raw4 = thrown
                ? w.Thrust * (1 + PowerStrike * s.PowerStrike * 0.5)
                : w.Cls == WeaponClass.Crossbow ? w.Thrust + Defs.ArrowDamage
                : (w.Thrust + Defs.ArrowDamage) * (1 + PowerDraw * Math.Min(s.PowerDraw, (w.DrawReq ?? 0) + 4));
            raw4 *= Math.Pow(ProfStep, (AgentOps.Proficiency(a, w) - 100) / 100.0);
            if (AgentOps.Mounted(a) && Math.Abs(a.Mount!.Speed) > 1)
                raw4 *= 1 - 0.2 * (1 - s.HorseArchery / 10.0);
            raw4 *= BonusDamage(a, w, false, true, false);

            Projectiles.Add(new Projectile
            {
                Id = nextProjectile++, Owner = a.Id, Team = a.Team,
                X = sx, Y = sy, Z = sz,
                Vx = AgentOps.ForwardX(yaw2) * cp2 * v + a.Vx,
                Vy = Math.Sin(pitch) * v,
                Vz = AgentOps.ForwardZ(yaw2) * cp2 * v + a.Vz,
                Damage = raw4, Nominal = v, Stuck = false, StuckIn = null, Age = 0,
                Kind = thrown ? "javelin" : "arrow", Weapon = w.Id,
            });
            if (thrown && a.Ammo[a.WeaponIdx] == 0)
            {
                int idx4 = -1;
                for (int i = 0; i < a.Troop.Weapons.Length; i++)
                    if (i != a.WeaponIdx && WeaponsData.Weapons[a.Troop.Weapons[i]].Cls != WeaponClass.Thrown) { idx4 = i; break; }
                if (idx4 >= 0) a.Action = new SwapAction { T = 0, To = idx4 };
            }
            Events.Add(new ShootEvent { AgentId = a.Id });
        }

        private void PickUp()
        {
            foreach (var p in Projectiles)
            {
                if (!p.Stuck || p.StuckIn.HasValue || p.Taken || p.Age < 1) continue;
                foreach (var a in Grid.Near(p.X, p.Z, 0.8))
                {
                    if (!a.Alive || a.Gone || AgentOps.Mounted(a)) continue;
                    if (Math.Abs(a.X - p.X) > 0.8 || Math.Abs(a.Z - p.Z) > 0.8) continue;
                    int slot = Array.IndexOf(a.Troop.Weapons, p.Weapon);
                    if (p.Kind == "javelin" && slot >= 0 && a.Ammo[slot] < (WeaponsData.Weapons[p.Weapon].Stack ?? 0))
                    { a.Ammo[slot]++; p.Taken = true; break; }
                    bool hasBow = false;
                    foreach (var id in a.Troop.Weapons)
                        if (WeaponsData.Weapons[id].Cls == WeaponClass.Bow || WeaponsData.Weapons[id].Cls == WeaponClass.Crossbow) { hasBow = true; break; }
                    if (p.Kind == "arrow" && a.Troop.Arrows.HasValue && a.Arrows < a.Troop.Arrows.Value && hasBow)
                    { a.Arrows++; p.Taken = true; break; }
                }
            }
        }

        private void StepProjectiles(double dt)
        {
            foreach (var p in Projectiles)
            {
                p.Age += dt;
                if (p.Stuck) continue;
                double x0 = p.X, y0 = p.Y, z0 = p.Z;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt - 0.5 * GravityConst * dt * dt;
                p.Z += p.Vz * dt;
                p.Vy -= GravityConst * dt;
                double g1 = Terrain?.Height(p.X, p.Z) ?? 0;
                if (p.Y <= g1)
                {
                    double g0 = Terrain?.Height(x0, z0) ?? 0;
                    double kk = (y0 - g0) / Math.Max(1e-6, (y0 - g0) - (p.Y - g1));
                    p.X = x0 + (p.X - x0) * kk;
                    p.Z = z0 + (p.Z - z0) * kk;
                    p.Y = (Terrain?.Height(p.X, p.Z) ?? 0) + 0.05;
                    p.Stuck = true;
                    continue;
                }
                var hit = ArrowHit(p, x0, y0, z0);
                if (hit != null) { p.Stuck = true; p.StuckIn = hit.Id; }
            }
            PickUp();
            CapMissiles();
        }

        private void CapMissiles()
        {
            int lying = 0;
            foreach (var q in Projectiles) if (q.Stuck && !q.StuckIn.HasValue) lying++;
            bool hasTaken = false;
            foreach (var q in Projectiles) if (q.Taken || q.StuckIn.HasValue) { hasTaken = true; break; }
            if (lying <= MaxLying && !hasTaken) return;
            int drop = Math.Max(0, lying - MaxLying);
            var keep = new List<Projectile>();
            foreach (var q in Projectiles)
            {
                if (q.Taken || q.StuckIn.HasValue) continue;
                if (q.Stuck && drop > 0) { drop--; continue; }
                keep.Add(q);
            }
            Projectiles.Clear();
            Projectiles.AddRange(keep);
        }

        private Agent? ArrowHit(Projectile p, double x0, double y0, double z0)
        {
            int steps = 4;
            double half2 = Math.Sqrt((p.X - x0) * (p.X - x0) + (p.Z - z0) * (p.Z - z0)) / 2;
            var near2 = new List<(Agent b, List<Capsule> caps)>();
            foreach (var b in Grid.Near((p.X + x0) / 2, (p.Z + z0) / 2, half2 + 3))
            {
                if (!b.Alive || b.Gone || b.Id == p.Owner) continue;
                near2.Add((b, Capsules(b)));
            }
            if (near2.Count == 0) return null;
            for (int s = 1; s <= steps; s++)
            {
                double ff = s / (double)steps;
                double px = x0 + (p.X - x0) * ff, py = y0 + (p.Y - y0) * ff, pz = z0 + (p.Z - z0) * ff;
                foreach (var (b, caps2) in near2)
                {
                    foreach (var c in caps2)
                    {
                        if (DistToSegment(px, py, pz, c) > c.R) continue;
                        double bearing3 = AgentOps.AngleDiff(AgentOps.YawTo(-p.Vx, -p.Vz), b.Yaw);
                        if (!c.IsMount && AgentOps.ShieldOnBack(b) && Math.Abs(bearing3) > Math.PI - 0.9)
                        {
                            Events.Add(new BlockEvent { Attacker = p.Owner, Target = b.Id, Shield = true, Parry = false, X = px, Y = py, Z = pz });
                            b.ShieldHp = Math.Max(0, b.ShieldHp - Math.Max(0, p.Damage - b.Troop.Shield!.Resistance));
                            return b;
                        }
                        if (!c.IsMount && AgentOps.HasShield(b))
                        {
                            var sh2 = b.Troop.Shield!;
                            double lvl2 = b.Troop.Skills.Shield;
                            bool raised2 = b.Action is BlockAction && Math.Abs(bearing3) < sh2.Coverage + 0.04 * lvl2 + BonusCoverage(b);
                            bool onArm = bearing3 > 0.2 && bearing3 < 1.1;
                            if (raised2 || onArm)
                            {
                                Events.Add(new BlockEvent { Attacker = p.Owner, Target = b.Id, Shield = true, Parry = false, X = px, Y = py, Z = pz });
                                b.ShieldHp = Math.Max(0, b.ShieldHp - Math.Max(0, p.Damage * (p.Kind == "javelin" ? 1.5 : 1) - sh2.Resistance) * (1 - 0.08 * lvl2));
                                if (b.ShieldHp == 0) Events.Add(new ShieldBrokenEvent { Target = b.Id });
                                return b;
                            }
                        }
                        var by2v = byId.TryGetValue(p.Owner, out var bySrc) ? bySrc : null;
                        double rv = Math.Sqrt((p.Vx - b.Vx) * (p.Vx - b.Vx) + p.Vy * p.Vy + (p.Vz - b.Vz) * (p.Vz - b.Vz));
                        double sb5 = Math.Max(-1, Math.Min(1, rv / p.Nominal - 1));
                        double raw5 = p.Damage * Uniform(0.9, 1) * Math.Pow(MissileSpeedPower, sb5);
                        if (c.IsMount) { DamageMount(b, raw5, DamageType.Pierce, by2v); return b; }
                        var part2 = HitPartOf(b, py, px, pz);
                        if (part2 == HitPart.Head) raw5 *= MissileHeadMult;
                        Damage(b, raw5, DamageType.Pierce, by2v, part2, HitKind.Pierce, sb5, px, py, pz);
                        return b;
                    }
                }
            }
            return null;
        }

        // ── 士气 ─────────────────────────────────────────────────────────
        private void UpdateMorale(double dt)
        {
            // 注意：TS 用 for...of 遍历 events，push 进去的新事件也会被迭代到
            // (CommanderEvent 不匹配任何处理分支，所以迭不迭代结果一样)。
            // C# foreach 禁止中途修改集合，改用 for(int i=0;...) 与 TS 行为一致。
            for (int _mei = 0; _mei < Events.Count; _mei++)
            {
                var e = Events[_mei];
                switch (e)
                {
                    case DeathEvent de:
                    {
                        byId.TryGetValue(de.AgentId, out var v);
                        byId.TryGetValue(de.Killer, out var k);
                        if (v == null) break;
                        MoraleNear(v, MoraleRadius, a => a.Team == v.Team ? -MoraleCasualty : 0);
                        if (k != null) MoraleNear(k, MoraleRadius, a => a.Team == k.Team ? MoraleKill : 0);
                        if (Commander[v.Team] == v.Id)
                        {
                            foreach (var a in Agents) if (a.Alive && a.Team == v.Team) a.Morale -= MoraleCommander;
                            Events.Add(new CommanderEvent { Team = v.Team });
                        }
                        break;
                    }
                    case RoutEvent re:
                    {
                        byId.TryGetValue(re.AgentId, out var v);
                        if (v != null) MoraleNear(v, MoraleRadius, a => a.Team == v.Team && !a.Routed ? -MoraleRoutNearby : 0);
                        break;
                    }
                    case HitEvent he when he.Part != HitPart.Mount:
                    {
                        if (byId.TryGetValue(he.Target, out var v) && v.Alive)
                            v.Morale -= Math.Min(6, he.Damage * 0.12);
                        break;
                    }
                }
            }

            for (int team = 0; team <= 1; team++)
            {
                var n = Alive(team);
                if (n.Count > 0) { double sum = 0; foreach (var ag in n) sum += ag.Morale; Morale[team] = sum / n.Count / 100.0; } else Morale[team] = 0;
            }
            // 计算每队士气恢复目标
            double[] rest = { 0, 0 };
            for (int team = 0; team <= 1; team++)
            {
                int other = 1 - team;
                double fielded = Initial[team] + Reserves[team].Count;
                double left = Alive(team).FindAll(a => !a.Routed).Count + Reserves[team].Count;
                double foes = Alive(other).FindAll(a => !a.Routed).Count + Reserves[other].Count;
                double lost = 1 - left / Math.Max(1, fielded);
                double odds = Math.Max(-1, Math.Min(1, (left - foes) / Math.Max(1, left + foes) * 2));
                rest[team] = StartMorale[team] * (1 - lost * 0.9) + odds * 10;
            }
            foreach (var a in Agents)
            {
                if (!a.Alive || a.Gone) continue;
                a.Morale += (rest[a.Team] - a.Morale) * Math.Min(1, dt * (a.Routed && a.SinceHit > 4 ? 0.06 : 0.03));
                a.Morale = Math.Max(0, Math.Min(100, a.Morale));
                if (a.IsPlayer) continue;
                if (!a.Routed && a.Morale < MoralePanic)
                {
                    a.Routed = true;
                    a.Action = new IdleAction();
                    Events.Add(new RoutEvent { AgentId = a.Id });
                    MoraleNear(a, MoraleRadius, o => o.Team == a.Team && !o.Routed ? -MoraleRoutNearby : 0);
                }
                else if (a.Routed && a.Morale > MoraleRally && a.SinceHit > 6)
                    a.Routed = false;
            }
            if (Winner == null)
            {
                for (int team = 0; team <= 1; team++)
                {
                    double left = Alive(team).FindAll(a => !a.Routed).Count + Reserves[team].Count;
                    if (left == 0 && Initial[team] > 0) Winner = 1 - team;
                }
            }
        }

        private void MoraleNear(Agent c, double r, Func<Agent, double> delta)
        {
            var near2 = new List<Agent>();
            foreach (var a in Agents)
            {
                if (a == c || !a.Alive || a.Gone) continue;
                if (Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Z - c.Z) * (a.Z - c.Z)) < r) near2.Add(a);
            }
            near2.Sort((p, q) =>
                Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Z - c.Z) * (p.Z - c.Z))
                    .CompareTo(Math.Sqrt((q.X - c.X) * (q.X - c.X) + (q.Z - c.Z) * (q.Z - c.Z))));
            int limit = Math.Min(10, near2.Count);
            for (int i = 0; i < limit; i++)
            {
                var a = near2[i];
                double d = delta(a);
                a.Morale += d < 0 ? d * (MoraleMult.TryGetValue(a.Id, out double mm) ? mm : 1) * (1 - BannerFor(a, BannerKind.Morale)) : d;
                a.Morale = Math.Max(0, Math.Min(100, a.Morale));
            }
        }

        // ── 增援 ─────────────────────────────────────────────────────────
        private void Reinforce()
        {
            for (int team = 0; team <= 1; team++)
            {
                var queue = Reserves[team];
                if (queue.Count == 0 || double.IsPositiveInfinity(FieldCap[team])) continue;
                int onField = Alive(team).FindAll(a => !a.Routed).Count;
                if (onField > FieldCap[team] * 0.5) continue;
                int present = Alive(team).Count;
                int n = (int)Math.Min(queue.Count, FieldCap[team] - present);
                if (n <= 0) continue;
                var line = SpawnLine[team];
                for (int i = 0; i < n; i++)
                {
                    string id2 = queue[0]; queue.RemoveAt(0);
                    double col = i % 12 - 5.5, row = Math.Floor(i / 12.0);
                    var a = Spawn(id2, col * 1.8, line.Z + (team == 0 ? 1 : -1) * row * 2, line.Yaw);
                    a.FormationBlock = Deploy.BlockFor(Plan, a);
                    // 围攻增援：已删除 dismountNow 调用
                    Events.Add(new SpawnEvent { AgentId = a.Id });
                }
                Waves[team]++;
            }
        }

        // ── 旗帜 ─────────────────────────────────────────────────────────
        private void UpdateBanners()
        {
            foreach (var b in Banners)
            {
                var bearer = b.Bearer.HasValue && byId.TryGetValue(b.Bearer.Value, out var ag) ? ag : null;
                if (bearer != null && bearer.Alive && !bearer.Gone && !bearer.Routed)
                { b.X = bearer.X; b.Z = bearer.Z; b.Dropped = false; continue; }
                if (!b.Dropped)
                { b.Dropped = true; b.Bearer = null; Events.Add(new BannerEvent { Team = b.Team, Lost = true }); }
                foreach (var a in Agents)
                {
                    if (!a.Alive || a.Gone || a.Routed || a.Team != b.Team || a.IsPlayer) continue;
                    if (Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)) < 1.2)
                    { b.Bearer = a.Id; b.Dropped = false; Events.Add(new BannerEvent { Team = b.Team, Lost = false }); break; }
                }
            }
        }

        // ── 队长与战旗任命 ───────────────────────────────────────────────
        public void AppointCaptains(string[] bannerKinds, string[] groups)
        {
            foreach (var a in Agents)
            {
                var g = a.Troop.CaptainOf;
                if (g != null && a.Alive && !a.Gone && !Captains[a.Team].ContainsKey(g))
                    Captains[a.Team][g] = a.Id;
            }
            for (int team = 0; team <= 1; team++)
            {
                if (team >= bannerKinds.Length || team >= groups.Length) continue;
                string group2 = groups[team];
                BannerKind kind2 = ParseBannerKind(bannerKinds[team]);
                Agent? bearer = null;
                foreach (var a in Agents)
                {
                    if (!a.Alive || a.Gone || a.IsPlayer || a.Team != team) continue;
                    if (GroupOfRole(a) != group2) continue;
                    if (Captains[team].TryGetValue(group2, out int captId) && captId == a.Id) continue;
                    bearer = a; break;
                }
                if (bearer == null) continue;
                Banners.Add(new Banner { Kind = kind2, Team = team, Group = group2, Bearer = bearer.Id, X = bearer.X, Z = bearer.Z, Dropped = false });
            }
        }

        private static BannerKind ParseBannerKind(string s) => s switch
        {
            "speed"     => BannerKind.Speed,
            "ranged"    => BannerKind.Ranged,
            "vsMounted" => BannerKind.VsMounted,
            "morale"    => BannerKind.Morale,
            _           => BannerKind.Damage
        };

        private static string GroupOfRole(Agent a)
            => a.Troop.Role == Role.Archer ? "arch" : a.Troop.Role == Role.Cavalry ? "cav" : a.Troop.Role == Role.HorseArcher ? "ha" : "inf";

        // ── 专长与战旗加成 ────────────────────────────────────────────────
        private CaptainPerks? PerksFor(Agent a)
        {
            FormationOf.TryGetValue(a.Id, out var f);
            string g = f.Group ?? GroupOfRole(a);
            if (!Captains[a.Team].TryGetValue(g, out int captId)) return null;
            if (!byId.TryGetValue(captId, out var c) || !c.Alive) return null;
            return c.Troop.CaptainPerks;
        }

        private double BannerFor(Agent a, BannerKind kind)
        {
            FormationOf.TryGetValue(a.Id, out var f);
            string group3 = f.Group ?? GroupOfRole(a);
            foreach (var b in Banners)
            {
                if (b.Team != a.Team || b.Kind != kind || b.Dropped) continue;
                if (b.Group != group3) continue;
                if (Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)) < BannerData.BannerRadius)
                    return BannerData.BannerValue;
            }
            return 0;
        }

        private Arrangement ArrangementOf(Agent a)
        {
            if (FormationOf.TryGetValue(a.Id, out var f)) return f.Arrangement;
            return Arrangement.Line;
        }

        private double BonusDamage(Agent a, Weapon w, bool thrust, bool ranged, bool targetMounted)
        {
            var p = PerksFor(a);
            double k = 1;
            bool inf = !AgentOps.Mounted(a) && !ranged;
            if (p?.SharpenTheTip == true && inf && thrust && w.Cls == WeaponClass.Polearm) k *= 1.05;
            if (p?.Braced == true && inf && targetMounted) k *= 1.1;
            if (ranged && w.Cls == WeaponClass.Bow)
            {
                if (p?.BowControl == true) k *= 1.05;
                if (p?.StrongBows == true) k *= 1.05;
            }
            k *= 1 + BannerFor(a, ranged ? BannerKind.Ranged : BannerKind.Damage);
            if (targetMounted) k *= 1 + BannerFor(a, BannerKind.VsMounted);
            return k;
        }

        private double BonusTaken(Agent b, bool ranged)
        {
            var p = PerksFor(b);
            if (!ranged && p?.Basher == true && ArrangementOf(b) == Arrangement.Shieldwall) return 0.96;
            return 1;
        }

        private double BonusCoverage(Agent b)
        {
            var p = PerksFor(b);
            return p?.ShieldWall == true && ArrangementOf(b) == Arrangement.Shieldwall ? 0.25 : 0;
        }

        private double BonusSpeed(Agent a, Weapon w, double sb)
        {
            var p = PerksFor(a);
            return p?.Lancer == true && AgentOps.Mounted(a) && w.Cls == WeaponClass.Polearm && sb > 0
                ? Math.Log(1 + 1.3 * (Math.Pow(2, sb) - 1), 2) : sb;
        }

        public double ProfBonus(Agent a, Weapon w)
        {
            var p = PerksFor(a);
            return p?.StrongGrip == true && w.Cls == WeaponClass.TwoHanded && !AgentOps.Mounted(a) ? 30 : 0;
        }

        public double SpeedBonus(Agent a) => 1 + BannerFor(a, BannerKind.Speed);

        // ── 围攻（已删除） ───────────────────────────────────────────────
        // Siege => false；Ladders 始终为空；下列方法保留签名但无逻辑
        public void DismountNow(Agent a)
        {
            if (a.Mount == null) return;
            a.Mount.Alive = false;
            a.Mount = null;
            int idx5 = Array.FindIndex(a.Troop.Weapons, id => !WeaponsData.Weapons[id].Couch && WeaponsData.Weapons[id].Cls != WeaponClass.Bow);
            if (idx5 >= 0) a.WeaponIdx = idx5;
        }

        // ── 工具方法 ─────────────────────────────────────────────────────
        /// <summary>点到线段的距离（胶囊碰撞检测）。</summary>
        private static double DistToSegment(double x, double y, double z, Capsule c)
        {
            double abx = c.Bx - c.Ax, aby = c.By - c.Ay, abz = c.Bz - c.Az;
            double apx = x  - c.Ax, apy = y  - c.Ay, apz = z  - c.Az;
            double len2 = abx * abx + aby * aby + abz * abz;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, (apx * abx + apy * aby + apz * abz) / len2)) : 0;
            double dx4 = apx - abx * t, dy4 = apy - aby * t, dz4 = apz - abz * t;
            return Math.Sqrt(dx4 * dx4 + dy4 * dy4 + dz4 * dz4);
        }
    }
}
