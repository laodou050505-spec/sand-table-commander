// BattleTypes.cs — 战斗事件与射弹的类型定义（对应 battle.ts 的 BattleEvent / Projectile）
// 仅保留事件类型和 Projectile；HitPart/HitKind 已扩展为完整枚举。
// abstract class Battle 已删除，实现位于 Battle.cs。

using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    // ── 命中相关枚举 ────────────────────────────────────────────────

    /// <summary>被击中的身体部位。</summary>
    public enum HitPart { Head, Neck, Shoulder, Arm, Body, Leg, Mount }

    /// <summary>命中类型。</summary>
    public enum HitKind { Cut, Pierce, Blunt, Bump, Couch, Kick }

    // ── 飞行射弹 ────────────────────────────────────────────────────

    /// <summary>飞行或插地的射弹（对应 battle.ts 的 Projectile 接口）。</summary>
    public sealed class Projectile
    {
        public int Id;
        public int Owner;
        public int Team;
        public double X, Y, Z;
        public double Vx, Vy, Vz;
        public double Damage;
        public double Nominal;
        public bool Stuck;
        public int? StuckIn;
        public double Age;
        public string Kind = "";
        public string Weapon = "";
        public bool Taken;
    }

    // ── BattleEvent 判别联合 ────────────────────────────────────────

    /// <summary>每帧战斗事件基类。</summary>
    public abstract class BattleEvent { }

    public sealed class HitEvent       : BattleEvent { public int Attacker, Target; public double Damage; public HitPart Part; public HitKind Kind; public double X, Y, Z; public bool Killed; public double SpeedBonus; public bool Crush; }
    public sealed class BlockEvent     : BattleEvent { public int Attacker, Target; public bool Shield, Parry; public double X, Y, Z; }
    public sealed class ChamberEvent   : BattleEvent { public int Attacker, Target; public double X, Y, Z; }
    public sealed class KnockdownEvent : BattleEvent { public int Target; }
    public sealed class InterruptEvent : BattleEvent { public int Target; }
    public sealed class BraceEvent     : BattleEvent { public int Spear, Target; }
    public sealed class ShieldBrokenEvent : BattleEvent { public int Target; }
    public sealed class DismountEvent  : BattleEvent { public int AgentId; public bool Voluntary; }
    public sealed class MountEvent     : BattleEvent { public int AgentId, From; }
    public sealed class RearEvent      : BattleEvent { public int AgentId; }
    public sealed class ShootEvent     : BattleEvent { public int AgentId; }
    public sealed class DeathEvent     : BattleEvent { public int AgentId, Killer; }
    public sealed class RoutEvent      : BattleEvent { public int AgentId; }
    public sealed class CommanderEvent : BattleEvent { public int Team; }
    public sealed class GateEvent      : BattleEvent { }
    public sealed class BannerEvent    : BattleEvent { public int Team; public bool Lost; }
    public sealed class SpawnEvent     : BattleEvent { public int AgentId; }
    public sealed class SwingEvent     : BattleEvent { public int AgentId; }
}
