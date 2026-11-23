// 自动生成：由 sandtable-xr/src/sim/agent.ts 忠实移植
// 一名战斗员：状态，以及控制器（玩家或 AI）每帧写入的输入。
// 地面坐标 XZ，Y 向上；yaw 对应 three.js rotation.y，朝 −Z 时向前 = (−sin yaw, −cos yaw)。
using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    // ══════════════════════════════════════════════════════════════════
    // 输入结构（值类型，RunSteps 会整体拷贝一份）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 每帧由控制器（玩家/AI）写入的完整输入。
    /// 声明为 struct 以便 RunSteps 进行值拷贝；Agent.Input 字段必须为 public（而非属性），
    /// 以保证对结构体字段的原地修改（如 p.Input.Attack = null）能正确工作。
    /// Aim 用 C# 值元组 (double X, double Y, double Z)? 代替 AimPoint 引用类型，
    /// 保持 AI.cs 里的 a.Input.Aim = (tx, ty, tz) 元组赋值语法正常编译。
    /// </summary>
    public struct Input
    {
        /// <summary>步行移动方向（世界空间，模长 ≤1）。</summary>
        public double MoveX, MoveZ;
        /// <summary>走路而非奔跑（保持阵型、谨慎接近时使用）。</summary>
        public bool Walk;
        /// <summary>骑乘时：−1..1 油门（后/前）和转向（左/右）。</summary>
        public double Throttle, Steer;
        /// <summary>步行时身体朝向 / 骑乘时骑手视线方向（弧度）。</summary>
        public double Face;
        /// <summary>弓矢俯仰角（弧度向上）；无瞄准目标时才使用。</summary>
        public double AimPitch;
        /// <summary>弓手射击落点世界坐标（X, Y, Z）；sim 在释放时按实际速度解算弧线。</summary>
        public (double X, double Y, double Z)? Aim;
        /// <summary>高抛弧（越过前方友军头顶）。</summary>
        public bool Lob;
        /// <summary>本帧发起此方向的攻击（一次性）。</summary>
        public Dir? Attack;
        /// <summary>攻击键持续按住：蓄力待机，弓保持拉弦。</summary>
        public bool AttackHeld;
        /// <summary>举此方向格挡（盾兵忽略方向）；null 放下格挡。</summary>
        public Dir? Block;
        /// <summary>踢击（一次性）。</summary>
        public bool Kick;
        /// <summary>冲刺攻击（骑枪）。</summary>
        public bool Couch;
        /// <summary>切换到武器槽 n（一次性）。</summary>
        public int? Swap;
        /// <summary>Bannerlord X 键：切换武器用法 / 站立时抵枪（一次性）。</summary>
        public bool Alt;
        /// <summary>下马，或上附近空马（一次性）。</summary>
        public bool MountToggle;
        /// <summary>盾牌猛击（一次性）。</summary>
        public bool Bash;
    }

    /// <summary>创建默认空输入（所有一次性字段为 false/null）。</summary>
    public static class InputFactory
    {
        public static Input Empty(double face = 0) => new()
        {
            MoveX = 0, MoveZ = 0, Walk = false, Throttle = 0, Steer = 0,
            Face = face, AimPitch = 0, Aim = null, Lob = false,
            Attack = null, AttackHeld = false, Block = null,
            Kick = false, Couch = false, Swap = null, Alt = false,
            MountToggle = false, Bash = false
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // Action 层级（TypeScript 区分联合 → C# 抽象类 + 密封派生类）
    // AI.cs 直接用 act.Kind == "attack" 访问，不走 is/switch 模式匹配；
    // 因此把所有子类字段都提到基类，子类只覆写 Kind。
    // ══════════════════════════════════════════════════════════════════

    public abstract class AgentAction
    {
        /// <summary>动作种类标识字符串（对应 TS 的 kind 字段）。</summary>
        public virtual string Kind { get; } = "idle";
        /// <summary>动作内部时间（秒）。</summary>
        public double T;

        // ── AttackAction 字段（提升到基类以兼容 AI.cs 的扁平访问）──────
        /// <summary>攻击方向。</summary>
        public Dir Dir;
        /// <summary>挥击动画片段名。</summary>
        public string Clip = "";
        /// <summary>挥击帧信息（LiveFrom / LiveTo 等）。</summary>
        public SwingInfo? Info;
        /// <summary>在蓄力点持续的秒数。</summary>
        public double Held;
        /// <summary>从按键到现在的总秒数。</summary>
        public double Age;
        /// <summary>破击（重武器上劈或钝击）。</summary>
        public bool Crush;
        /// <summary>已释放（进入收招阶段）。</summary>
        public bool Released;
        /// <summary>动画完成。</summary>
        public bool Done;
        /// <summary>本次挥击已击中的 Agent Id 集合（防止重复伤害）。</summary>
        public HashSet<int> Struck = new();

        // ── BlockAction 字段 ───────────────────────────────────────────
        /// <summary>被重击格挡后锁定的剩余时间（秒）。</summary>
        public double Lock;

        // ── StaggerAction / DownAction 字段 ────────────────────────────
        public double Dur;
        public bool Heavy;

        // ── SwapAction 字段 ────────────────────────────────────────────
        public int To;

        // ── CouchAction 字段 ───────────────────────────────────────────
        public double Cooldown;
    }

    /// <summary>待机。</summary>
    public sealed class IdleAction : AgentAction
    {
        public override string Kind => "idle";
    }

    /// <summary>挥击中。</summary>
    public sealed class AttackAction : AgentAction
    {
        public override string Kind => "attack";
    }

    /// <summary>格挡中。</summary>
    public sealed class BlockAction : AgentAction
    {
        public override string Kind => "block";
    }

    /// <summary>被硬直。</summary>
    public sealed class StaggerAction : AgentAction
    {
        public override string Kind => "stagger";
    }

    /// <summary>被击倒在地，之后爬起。</summary>
    public sealed class DownAction : AgentAction
    {
        public override string Kind => "down";
    }

    /// <summary>踢击动作。</summary>
    public sealed class KickAction : AgentAction
    {
        public override string Kind => "kick";
    }

    /// <summary>盾牌猛击动作。</summary>
    public sealed class BashAction : AgentAction
    {
        public override string Kind => "bash";
    }

    /// <summary>拔弓（过渡动画）。</summary>
    public sealed class DrawAction : AgentAction
    {
        public override string Kind => "draw";
    }

    /// <summary>上弦弩：动作期间几乎静止且无防御能力。</summary>
    public sealed class ReloadAction : AgentAction
    {
        public override string Kind => "reload";
    }

    /// <summary>弓箭释放。</summary>
    public sealed class ReleaseAction : AgentAction
    {
        public override string Kind => "release";
    }

    /// <summary>冲刺状态（骑枪）。</summary>
    public sealed class CouchAction : AgentAction
    {
        public override string Kind => "couch";
    }

    /// <summary>换武器动作。</summary>
    public sealed class SwapAction : AgentAction
    {
        public override string Kind => "swap";
    }

    /// <summary>死亡动画。</summary>
    public sealed class DeadAction : AgentAction
    {
        public override string Kind => "dead";
    }

    // ══════════════════════════════════════════════════════════════════
    // 坐骑状态
    // ══════════════════════════════════════════════════════════════════

    /// <summary>当前骑乘中的坐骑运行状态。</summary>
    public class MountState
    {
        public MountDef Def = null!;
        /// <summary>"horse" 或 "wolf"。</summary>
        public string Kind = "";
        public double Hp, MaxHp;
        /// <summary>当前有符号前向速度（码/秒）。</summary>
        public double Speed;
        public bool Alive;
        /// <summary>剩余晕眩时间（秒）。</summary>
        public double Stun;
        /// <summary>本帧已撞击过的 Agent Id 集合（防重复）。</summary>
        public HashSet<int> Bumped = new();
    }

    /// <summary>骑手阵亡后留在原地的坐骑尸体（仅供视图使用）。</summary>
    public class DeadMountInfo
    {
        public string Kind = "";
        public double X, Z, Yaw;
    }

    /// <summary>失去骑手后到处乱跑的空马（仅供视图使用）。</summary>
    public class LooseMountInfo
    {
        public string Kind = "";
        public double X, Z, Yaw, Speed, Hp;
        public MountDef Def = null!;
    }

    // ══════════════════════════════════════════════════════════════════
    // Agent 类（一名战斗员的完整状态）
    // ══════════════════════════════════════════════════════════════════

    public class Agent
    {
        public int Id;
        public TroopDef Troop = null!;
        public int Team;
        public double X;
        /// <summary>脚底的地面高度（或梯子踏板高度）。</summary>
        public double Y;
        /// <summary>本帧正在爬梯子。</summary>
        public bool Climbing;
        public double Z;
        public double Vx, Vz;
        public double Yaw;
        /// <summary>
        /// 骑乘时上半身相对马的前进方向的偏转角（弧度，正 = 向左如同 yaw）；
        /// 步行时为 0（全身转向）。
        /// </summary>
        public double Look;
        public double Hp, MaxHp;
        public bool Alive;
        /// <summary>已逃离战场。</summary>
        public bool Gone;
        public bool Routed;
        /// <summary>正在执行撤退指令（到达边缘后离场）。</summary>
        public bool Retreating;
        /// <summary>Bannerlord 战场士气 0–100；低于崩溃阈值则逃跑。</summary>
        public double Morale;
        public double ShieldHp;
        public int WeaponIdx;
        public int Arrows;
        /// <summary>弩已上弦并放了弩矢。</summary>
        public bool Loaded;
        /// <summary>每个武器槽的投掷武器剩余数量。</summary>
        public int[] Ammo = Array.Empty<int>();
        /// <summary>当前武器处于替代用法。</summary>
        public bool AltUsage;
        /// <summary>Bannerlord 站立抵枪（步行+静止，手按 X 且持长矛）。</summary>
        public bool Braced;
        public MountState? Mount;
        /// <summary>死亡坐骑留存位置（供视图）。</summary>
        public DeadMountInfo? DeadMount;
        /// <summary>无主坐骑奔跑信息（供视图）。</summary>
        public LooseMountInfo? LooseMount;
        public AgentAction Action = new IdleAction();
        /// <summary>公开字段（非属性）以支持结构体字段的原地修改。</summary>
        public Input Input;
        public bool IsPlayer;
        /// <summary>所在的布阵块序号（−1 = 无）。</summary>
        public int FormationBlock;
        public int Kills;
        /// <summary>上次被击中以来经过的时间（秒），用于 AI 谨慎判断和 HUD 闪烁。</summary>
        public double SinceHit;
        public double Radius;

        // ── AI.cs 使用的静态转发方法（委托给 AgentOps）────────────────
        // AI.cs 后台代理写成了 Agent.Mounted(f) 形式；在此提供转发以避免修改 AI.cs。
        public static bool Mounted(Agent a)        => AgentOps.Mounted(a);
        public static bool HasShield(Agent a)      => AgentOps.HasShield(a);
        public static bool ShieldOnBack(Agent a)   => AgentOps.ShieldOnBack(a);
        public static bool CanAct(Agent a)         => AgentOps.CanAct(a);
        public static Weapon WeaponOf(Agent a)     => AgentOps.WeaponOf(a);
        public static double ForwardX(double yaw)  => AgentOps.ForwardX(yaw);
        public static double ForwardZ(double yaw)  => AgentOps.ForwardZ(yaw);
        public static double YawTo(double dx, double dz)       => AgentOps.YawTo(dx, dz);
        public static double AngleDiff(double a, double b)     => AgentOps.AngleDiff(a, b);
        public static double AimYaw(Agent a)       => AgentOps.AimYaw(a);
        public static double SpeedOf(Agent a)      => AgentOps.SpeedOf(a);
        public static double RunSpeed(Agent a)     => AgentOps.RunSpeed(a);
        public static double Carried(Agent a)      => AgentOps.Carried(a);
        public static double Proficiency(Agent a, Weapon? w = null) => AgentOps.Proficiency(a, w);
        public static double AttackSpeed(Agent a, Weapon? w = null) => AgentOps.AttackSpeed(a, w);
        public static (double wx, double wz) ToWorld(double x, double z, double yaw, double px, double pz)
            => AgentOps.ToWorld(x, z, yaw, px, pz);
    }

    // ══════════════════════════════════════════════════════════════════
    // 静态工厂与工具方法
    // ══════════════════════════════════════════════════════════════════

    public static partial class AgentOps
    {
        private static int nextId = 1;

        /// <summary>重置 Agent 编号（每场战斗从 1 重新开始）。</summary>
        public static void ResetAgentIds() => nextId = 1;

        /// <summary>创建一名战斗员并放置在 (x, z)，朝向 yaw。</summary>
        public static Agent CreateAgent(string troopId, double x, double z, double yaw)
        {
            if (!WeaponsData.Troops.TryGetValue(troopId, out var troop))
                throw new ArgumentException($"未知兵种：{troopId}");

            MountState? mount = null;
            if (troop.Mount != null && WeaponsData.Mounts.TryGetValue(troop.Mount, out var mountDef))
            {
                mount = new MountState
                {
                    Def = mountDef, Kind = troop.Mount,
                    Hp = mountDef.Hp, MaxHp = mountDef.Hp,
                    Speed = 0, Alive = true, Stun = 0, Bumped = new()
                };
            }

            int[] ammo = new int[troop.Weapons.Length];
            for (int i = 0; i < troop.Weapons.Length; i++)
                ammo[i] = WeaponsData.Weapons.TryGetValue(troop.Weapons[i], out var w) ? (w.Stack ?? 0) : 0;

            double hp = Defs.TroopHp(troop);
            return new Agent
            {
                Id           = nextId++,
                Troop        = troop,
                Team         = troop.Team,
                X = x, Y = 0, Z = z,
                Climbing     = false,
                Vx = 0, Vz = 0,
                Yaw          = yaw,
                Look         = 0,
                Hp           = hp,
                MaxHp        = hp,
                Alive        = true,
                Gone         = false,
                Routed       = false,
                Retreating   = false,
                Morale       = 60,
                ShieldHp     = troop.Shield?.Hp ?? 0,
                WeaponIdx    = 0,
                Arrows       = troop.Arrows ?? 0,
                Loaded       = true,
                Ammo         = ammo,
                AltUsage     = false,
                Braced       = false,
                Mount        = mount,
                DeadMount    = null,
                LooseMount   = null,
                Action       = new IdleAction(),
                Input        = InputFactory.Empty(yaw),
                IsPlayer     = false,
                FormationBlock = -1,
                Kills        = 0,
                SinceHit     = 99,
                Radius       = troop.Race == Race.Orc ? 0.5 : 0.42,
            };
        }

        /// <summary>当前手持武器（包含替代用法切换）。</summary>
        public static Weapon WeaponOf(Agent a)
        {
            var base_ = WeaponsData.Weapons[a.Troop.Weapons[a.WeaponIdx]];
            return a.AltUsage && base_.AltUsage != null && WeaponsData.Weapons.TryGetValue(base_.AltUsage, out var alt)
                ? alt : base_;
        }

        /// <summary>当前持有武器是否可以配盾（单手/投掷/单手长矛）。</summary>
        public static bool HasShield(Agent a)
        {
            var w = WeaponOf(a);
            return a.Troop.Shield != null && a.ShieldHp > 0
                   && (w.Cls == WeaponClass.OneHanded || w.Cls == WeaponClass.Thrown || w.OneHandedPolearm);
        }

        /// <summary>盾挂背后时仍可阻挡来自背后的攻击（Bannerlord）。</summary>
        public static bool ShieldOnBack(Agent a) => a.Troop.Shield != null && a.ShieldHp > 0 && !HasShield(a);

        /// <summary>是否骑乘且坐骑存活。</summary>
        public static bool Mounted(Agent a) => a.Mount != null && a.Mount.Alive;

        /// <summary>上半身朝向：步行时等于 Yaw，骑马时加上 Look 偏转。</summary>
        public static double AimYaw(Agent a) => a.Yaw + a.Look;

        /// <summary>朝向 yaw 时前方向 X 分量：−sin(yaw)。</summary>
        public static double ForwardX(double yaw) => -Math.Sin(yaw);

        /// <summary>朝向 yaw 时前方向 Z 分量：−cos(yaw)。</summary>
        public static double ForwardZ(double yaw) => -Math.Cos(yaw);

        /// <summary>由 (dx, dz) 偏移量反求 yaw 角。</summary>
        public static double YawTo(double dx, double dz) => Math.Atan2(-dx, -dz);

        /// <summary>两角之差，归一到 [−π, π]。</summary>
        public static double AngleDiff(double a, double b)
        {
            double d = (a - b) % (Math.PI * 2);
            if (d > Math.PI)  d -= Math.PI * 2;
            if (d < -Math.PI) d += Math.PI * 2;
            return d;
        }

        /// <summary>
        /// 将模型本地坐标 (px, pz)（模型面朝 −Z）转换为世界坐标。
        /// </summary>
        public static (double wx, double wz) ToWorld(double x, double z, double yaw, double px, double pz)
        {
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            return (x + px * c + pz * s, z - px * s + pz * c);
        }

        /// <summary>地面速度（码/秒）。</summary>
        public static double SpeedOf(Agent a) => Math.Sqrt(a.Vx * a.Vx + a.Vz * a.Vz);

        /// <summary>允许发起新攻击/格挡的状态（待机、格挡中、冲刺中）。</summary>
        public static bool CanAct(Agent a)
        {
            var k = a.Action;
            return k is IdleAction || k is BlockAction || k is CouchAction;
        }

        /// <summary>武器熟练度（Warband 比例）。</summary>
        public static double Proficiency(Agent a, Weapon? w = null)
        {
            w ??= WeaponOf(a);
            return Defs.Proficiency(a, w);
        }

        /// <summary>出手速度乘数。</summary>
        public static double AttackSpeed(Agent a, Weapon? w = null)
        {
            w ??= WeaponOf(a);
            return Defs.AttackSpeed(a, w);
        }

        /// <summary>总装备重量（千克）。</summary>
        public static double Carried(Agent a) => Defs.Carried(a);

        /// <summary>跑步速度（码/秒）。</summary>
        public static double RunSpeed(Agent a) => Defs.RunSpeed(a);
    }
}
