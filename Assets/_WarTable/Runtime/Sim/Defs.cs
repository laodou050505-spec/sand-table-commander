// 自动生成：由 sandtable-xr/src/sim/defs.ts 忠实移植
// 单位：码（yard）；男性角色约 2 码高，速度和长度以此为基准。
using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    // ══════════════════════════════════════════════════════════════════
    // 基础枚举
    // ══════════════════════════════════════════════════════════════════

    /// <summary>M&amp;B 四向攻击方向（从防守方视角命名）。</summary>
    public enum Dir { Up, Down, Left, Right }

    /// <summary>伤害类型。</summary>
    public enum DamageType { Cut, Pierce, Blunt }

    /// <summary>武器分类。</summary>
    public enum WeaponClass { OneHanded, TwoHanded, Polearm, Bow, Thrown, Crossbow }

    /// <summary>兵种职能。</summary>
    public enum Role { Infantry, Spear, Shock, Archer, Cavalry, HorseArcher }

    /// <summary>种族。</summary>
    public enum Race { Human, Orc, Dwarf }

    // ══════════════════════════════════════════════════════════════════
    // 工具函数
    // ══════════════════════════════════════════════════════════════════

    public static class Defs
    {
        /// <summary>M&amp;B 四向攻击方向数组（与 TS DIRS 顺序一致）。</summary>
        public static readonly Dir[] Dirs = { Dir.Up, Dir.Down, Dir.Left, Dir.Right };

        /// <summary>防守方挡住对应攻击方向所需的格挡方向。</summary>
        public static Dir BlockFor(Dir attack) =>
            attack == Dir.Left ? Dir.Right :
            attack == Dir.Right ? Dir.Left :
            attack;

        /// <summary>弓/弩属于远程武器。</summary>
        public static bool IsRanged(WeaponClass cls) =>
            cls == WeaponClass.Bow || cls == WeaponClass.Crossbow;

        // ── 坐骑性能公式（来自 Warband 评级） ────────────────────────

        /// <summary>最高速度（码/秒）：速度评级 + 骑术加成。</summary>
        public static double MountTopSpeed(MountDef m, double riding) =>
            (m.SpeedRating + Math.Min(riding, 10)) * 0.32;

        /// <summary>原地转向速率（弧度/秒）。</summary>
        public static double MountTurn(MountDef m, double riding) =>
            (m.Maneuver + riding * 1.5) * 0.05;

        /// <summary>加速度（码/秒²）。</summary>
        public static double MountAccel(MountDef m, double riding) =>
            (m.Maneuver + riding * 2) * 0.12;

        // ── 士兵性能公式 ──────────────────────────────────────────────

        /// <summary>Warband HP = baseHp + Str + Ironflesh×2。</summary>
        public static double TroopHp(TroopDef t) =>
            t.BaseHp + t.Skills.Str + t.Skills.Ironflesh * 2;

        /// <summary>奔跑速度（码/秒）：体术、敏捷、负重综合计算。</summary>
        public static double RunSpeed(Agent a)
        {
            var s = a.Troop.Skills;
            return 5.0 * (1 + s.Athletics * 0.03 + (s.Agi - 10) * 0.005)
                   * Math.Max(0.75, 1 - Carried(a) * 0.006);
        }

        /// <summary>总装备重量（千克）。</summary>
        public static double Carried(Agent a)
        {
            double kg = a.Troop.ArmorWeight;
            foreach (var id in a.Troop.Weapons)
                kg += WeaponsData.Weapons[id].Weight;
            if (a.Troop.Shield != null) kg += 3;
            return kg;
        }

        /// <summary>武器熟练度（Warband 比例）。</summary>
        public static double Proficiency(Agent a, Weapon? w = null)
        {
            w ??= AgentOps.WeaponOf(a);
            var s = a.Troop.Skills;
            return w.Cls switch
            {
                WeaponClass.OneHanded => s.OneHanded,
                WeaponClass.TwoHanded => s.TwoHanded,
                WeaponClass.Polearm   => s.Polearm,
                WeaponClass.Crossbow  => s.Crossbow ?? s.Archery,
                _                     => s.Archery
            };
        }

        /// <summary>
        /// 出手速度乘数：武器速度评级 / 100，熟练度修正，骑兵双手/长杆惩罚。
        /// </summary>
        public static double AttackSpeed(Agent a, Weapon? w = null)
        {
            w ??= AgentOps.WeaponOf(a);
            double k = w.SpeedRating / 100.0;
            k *= 1 + Math.Max(-0.2, (Proficiency(a, w) - 100) / 1000.0);
            if (AgentOps.Mounted(a) && (w.Cls == WeaponClass.TwoHanded || w.Cls == WeaponClass.Polearm) && !w.Couch)
                k *= 0.85;
            return k;
        }

        /// <summary>
        /// 根据武器分类和一手长矛标志获取动作 clip 集合。
        /// </summary>
        public static ClipSet GetClipSet(string model, WeaponClass cls, bool oneHandedPolearm = false)
        {
            if (oneHandedPolearm || cls == WeaponClass.Bow || cls == WeaponClass.Thrown || cls == WeaponClass.Crossbow)
                return Clips[WeaponClass.OneHanded];
            if (Clips.TryGetValue(cls, out var cs)) return cs;
            return Clips[WeaponClass.OneHanded];
        }

        // ── 各种族站立高度（码） ────────────────────────────────────

        public static readonly Dictionary<Race, double> RaceHeight = new()
        {
            { Race.Human, 1.95 },
            { Race.Orc,   2.05 },
            { Race.Dwarf, 1.60 },
        };

        public static readonly string[] TeamNames  = { "金狮王国", "赤牙氏族" };
        public static readonly int[]    TeamColors = { 0x3d7bff,   0xe0402a  };

        /// <summary>箭矢额外伤害（加到弓本身的刺穿伤害上）。</summary>
        public const double ArrowDamage = 2.0;

        // ── Clip 集合 ─────────────────────────────────────────────────

        private static readonly Dictionary<WeaponClass, ClipSet> Clips = new()
        {
            {
                WeaponClass.OneHanded, new ClipSet
                {
                    Attack = new Dictionary<Dir, string>
                    {
                        { Dir.Right, "Swing1H_R" }, { Dir.Left, "Swing1H_L" },
                        { Dir.Up,    "Chop1H"    }, { Dir.Down, "Thrust1H"  }
                    },
                    Ready = "Ready1H", Block = "Parry1H"
                }
            },
            {
                WeaponClass.TwoHanded, new ClipSet
                {
                    Attack = new Dictionary<Dir, string>
                    {
                        { Dir.Right, "Swing2H_R" }, { Dir.Left, "Swing2H_L" },
                        { Dir.Up,    "Chop2H"    }, { Dir.Down, "Thrust2H"  }
                    },
                    Ready = "Ready2H", Block = "Parry2H"
                }
            },
            {
                WeaponClass.Polearm, new ClipSet
                {
                    Attack = new Dictionary<Dir, string>
                    {
                        { Dir.Right, "SwingPole_R" }, { Dir.Left, "SwingPole_L" },
                        { Dir.Up,    "ChopPole"    }, { Dir.Down, "ThrustPole"  }
                    },
                    Ready = "Ready2HL", Block = "Parry2HL"
                }
            },
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // 武器/护盾/士兵/坐骑数据结构
    // ══════════════════════════════════════════════════════════════════

    /// <summary>动作 clip 集合（每种武器风格的四向攻击、就绪、格挡）。</summary>
    public class ClipSet
    {
        public Dictionary<Dir, string> Attack = new();
        public string Ready = "";
        public string Block = "";
    }

    /// <summary>Warband 角色属性。</summary>
    public class Skills
    {
        public double Str = 10, Agi = 10;
        /// <summary>铁甲身：每级 +2 HP。</summary>
        public double Ironflesh;
        /// <summary>力量打击：每级近战伤害 +8%。</summary>
        public double PowerStrike;
        /// <summary>强力拉弓：每级弓伤害 +14%（上限弓要求 +4）。</summary>
        public double PowerDraw;
        /// <summary>盾牌：吃伤/举起速度/覆盖角。</summary>
        public double Shield;
        /// <summary>运动能力：跑步速度。</summary>
        public double Athletics;
        /// <summary>骑术：马速/机动/加速/冲刺伤害。</summary>
        public double Riding;
        /// <summary>马背射击：消除移动惩罚。</summary>
        public double HorseArchery;
        /// <summary>武器熟练度（Warband 比例）。</summary>
        public double OneHanded = 80, TwoHanded = 80, Polearm = 80, Archery = 60;
        /// <summary>弩手专用熟练度（null 则用 Archery）。</summary>
        public double? Crossbow;
    }

    /// <summary>护盾数值定义。</summary>
    public class ShieldDef
    {
        public string Model = "";
        /// <summary>护盾耐久。</summary>
        public double Hp;
        /// <summary>每次打击吸收的固定伤害。</summary>
        public double Resistance;
        /// <summary>覆盖角（弧度半径，即半张角）。</summary>
        public double Coverage;
        /// <summary>举盾速度评级。</summary>
        public double Speed;
    }

    /// <summary>武器定义。</summary>
    public class Weapon
    {
        public string Id = "";
        /// <summary>武器模型名（GLB 文件名）。</summary>
        public string Model = "";
        public WeaponClass Cls;
        /// <summary>武器长度（码）。</summary>
        public double Length;
        /// <summary>挥砍和刺击伤害（0 = 该攻击方式不可用）。</summary>
        public double Swing, Thrust;
        public DamageType SwingType, ThrustType;
        /// <summary>Warband 速度评级（≈75~105）。</summary>
        public double SpeedRating;
        /// <summary>重量（千克）。</summary>
        public double Weight;
        /// <summary>可击穿格挡。</summary>
        public bool Crush;
        /// <summary>钝击可击倒。</summary>
        public bool Knockdown;
        /// <summary>可骑马冲刺攻击（长矛）。</summary>
        public bool Couch;
        /// <summary>可蓄力抵御骑兵（Bannerlord 长杆）。</summary>
        public bool Brace;
        /// <summary>不可在马背使用（大枪、战锤）。</summary>
        public bool NoHorseback;
        /// <summary>投射武器：飞行速度（码/秒）。</summary>
        public double? MissileSpeed;
        /// <summary>准确度（0~100）。</summary>
        public double? Accuracy;
        /// <summary>弓的力量要求。</summary>
        public double? DrawReq;
        /// <summary>对盾牌的伤害倍率（斧类）。</summary>
        public double? ShieldBonus;
        /// <summary>单手长杆：持盾仍可使用（Bannerlord 矛盾组合）。</summary>
        public bool OneHandedPolearm;
        /// <summary>Bannerlord X 键切换用法（字符串为另一武器 Id）。</summary>
        public string? AltUsage;
        /// <summary>弩：上弦时间（秒）。</summary>
        public double? Reload;
        /// <summary>投掷武器每组数量。</summary>
        public int? Stack;
        /// <summary>投射物飞行模型（落地后显示用）。</summary>
        public string? MissileModel;
    }

    /// <summary>士兵定义。</summary>
    public class TroopDef
    {
        public string Id = "";
        public string Name = "";
        /// <summary>队伍（0 = 金狮王国，1 = 赤牙氏族）。</summary>
        public int Team;
        public Role Role;
        /// <summary>对应的 Tripo 角色 ID（GLB 文件名）。</summary>
        public string Model = "";
        public Race Race;
        /// <summary>武器槽 ID 列表（第 0 个为出生时持用的武器）。</summary>
        public string[] Weapons = Array.Empty<string>();
        public ShieldDef? Shield;
        /// <summary>坐骑类型 ("horse" / "wolf" / null)。</summary>
        public string? Mount;
        /// <summary>Warband 基础 HP（未加力量和铁甲身）。</summary>
        public double BaseHp;
        /// <summary>各部位护甲值。</summary>
        public ArmorSet Armor;
        /// <summary>马铠（可选）。</summary>
        public BardingDef? Barding;
        /// <summary>所穿护甲总重（千克，用于负重惩罚）。</summary>
        public double ArmorWeight;
        public Skills Skills = new();
        /// <summary>0~1：AI 格挡反应与攻击质量。</summary>
        public double Skill;
        /// <summary>弓手箭矢数（标枪数量由武器 Stack 决定）。</summary>
        public int? Arrows;
        /// <summary>该兵种是否为队长级。</summary>
        public bool Officer;
        /// <summary>IsOfficer 等同于 Officer（供 Scenario.cs 兼容）。</summary>
        public bool IsOfficer => Officer;
        /// <summary>作为队长时给编队提供的专长。</summary>
        public CaptainPerks? CaptainPerks;
        /// <summary>担任哪个编队组的队长（"inf"/"arch"/"cav"/"ha" 或 null）。</summary>
        public string? CaptainOf;
    }

    /// <summary>各部位护甲值（码制）。</summary>
    public struct ArmorSet
    {
        public double Head, Body, Leg;
        public double? Shoulder, Arm;
    }

    /// <summary>马铠定义。</summary>
    public struct BardingDef
    {
        public double Armor, Weight;
    }

    /// <summary>坐骑定义（Warband 五项评级）。</summary>
    public class MountDef
    {
        public string Model = "";
        public int Skin;
        public double Hp, Armor;
        public double SpeedRating, Maneuver;
        /// <summary>撞击伤害及击倒能力。</summary>
        public double Charge;
        /// <summary>沿身体方向的碰撞长度（码）。</summary>
        public double Length;
    }

    // ══════════════════════════════════════════════════════════════════
    // 静态数据表
    // ══════════════════════════════════════════════════════════════════

    public static class WeaponsData
    {
        // 护盾常量（在 TROOPS 初始化前定义）
        private static readonly ShieldDef ShieldRound = new() { Model="shield_round", Hp=360, Resistance=12, Coverage=1.15, Speed=82 };
        private static readonly ShieldDef ShieldCrest = new() { Model="shield_crest", Hp=480, Resistance=18, Coverage=1.35, Speed=76 };
        private static readonly ShieldDef ShieldClan  = new() { Model="shield_clan",  Hp=450, Resistance=16, Coverage=1.30, Speed=79 };

        // 辅助：构造 Skills 时填入默认值并覆盖
        private static Skills Sk(
            double str=10, double agi=10, double ironflesh=0, double powerStrike=0,
            double powerDraw=0, double shield=0, double athletics=0, double riding=0,
            double horseArchery=0, double oneHanded=80, double twoHanded=80,
            double polearm=80, double archery=60, double? crossbow=null)
            => new()
            {
                Str=str, Agi=agi, Ironflesh=ironflesh, PowerStrike=powerStrike,
                PowerDraw=powerDraw, Shield=shield, Athletics=athletics, Riding=riding,
                HorseArchery=horseArchery, OneHanded=oneHanded, TwoHanded=twoHanded,
                Polearm=polearm, Archery=archery, Crossbow=crossbow
            };

        public static readonly IReadOnlyDictionary<string, Weapon> Weapons =
            new Dictionary<string, Weapon>
        {
            ["arming_sword"]   = new(){ Id="arming_sword",   Model="arming_sword",  Cls=WeaponClass.OneHanded, Length=1.05, Swing=30, Thrust=24, SwingType=DamageType.Cut,   ThrustType=DamageType.Pierce, SpeedRating=99,  Weight=1.5  },
            ["short_sword"]    = new(){ Id="short_sword",    Model="short_sword",   Cls=WeaponClass.OneHanded, Length=0.85, Swing=26, Thrust=22, SwingType=DamageType.Cut,   ThrustType=DamageType.Pierce, SpeedRating=103, Weight=1.25 },
            ["mace"]           = new(){ Id="mace",           Model="warhammer",     Cls=WeaponClass.OneHanded, Length=0.8,  Swing=26, Thrust=0,  SwingType=DamageType.Blunt, ThrustType=DamageType.Blunt,  SpeedRating=98,  Weight=2,    Knockdown=true },
            ["hatchet"]        = new(){ Id="hatchet",        Model="hatchet",       Cls=WeaponClass.OneHanded, Length=0.8,  Swing=32, Thrust=0,  SwingType=DamageType.Cut,   ThrustType=DamageType.Cut,    SpeedRating=97,  Weight=2,    ShieldBonus=1.5 },
            ["clan_axe"]       = new(){ Id="clan_axe",       Model="clan_axe",      Cls=WeaponClass.OneHanded, Length=0.9,  Swing=35, Thrust=0,  SwingType=DamageType.Cut,   ThrustType=DamageType.Cut,    SpeedRating=93,  Weight=2.5,  ShieldBonus=1.5 },
            ["claymore"]       = new(){ Id="claymore",       Model="claymore",      Cls=WeaponClass.TwoHanded, Length=1.45, Swing=40, Thrust=30, SwingType=DamageType.Cut,   ThrustType=DamageType.Pierce, SpeedRating=93,  Weight=2.75 },
            ["war_axe"]        = new(){ Id="war_axe",        Model="war_axe",       Cls=WeaponClass.TwoHanded, Length=1.3,  Swing=46, Thrust=0,  SwingType=DamageType.Cut,   ThrustType=DamageType.Cut,    SpeedRating=87,  Weight=4.5,  ShieldBonus=2 },
            ["war_axe2"]       = new(){ Id="war_axe2",       Model="war_axe2",      Cls=WeaponClass.TwoHanded, Length=1.3,  Swing=45, Thrust=0,  SwingType=DamageType.Cut,   ThrustType=DamageType.Cut,    SpeedRating=88,  Weight=4.25, ShieldBonus=2 },
            ["warhammer"]      = new(){ Id="warhammer",      Model="warhammer",     Cls=WeaponClass.TwoHanded, Length=1.25, Swing=32, Thrust=0,  SwingType=DamageType.Blunt, ThrustType=DamageType.Blunt,  SpeedRating=80,  Weight=6,    Crush=true, Knockdown=true, NoHorseback=true },
            ["maul"]           = new(){ Id="maul",           Model="maul",          Cls=WeaponClass.TwoHanded, Length=1.25, Swing=34, Thrust=0,  SwingType=DamageType.Blunt, ThrustType=DamageType.Blunt,  SpeedRating=78,  Weight=7,    Crush=true, Knockdown=true, NoHorseback=true },
            ["pike"]           = new(){ Id="pike",           Model="halberd",       Cls=WeaponClass.Polearm,   Length=2.1,  Swing=0,  Thrust=26, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=81,  Weight=3,    Brace=true, NoHorseback=true },
            ["halberd"]        = new(){ Id="halberd",        Model="halberd",       Cls=WeaponClass.Polearm,   Length=2.0,  Swing=37, Thrust=24, SwingType=DamageType.Cut,   ThrustType=DamageType.Pierce, SpeedRating=83,  Weight=4,    Brace=true },
            ["glaive"]         = new(){ Id="glaive",         Model="glaive",        Cls=WeaponClass.Polearm,   Length=2.0,  Swing=38, Thrust=21, SwingType=DamageType.Cut,   ThrustType=DamageType.Pierce, SpeedRating=83,  Weight=4.5,  Brace=true },
            ["lance"]          = new(){ Id="lance",          Model="lance",         Cls=WeaponClass.Polearm,   Length=2.4,  Swing=0,  Thrust=26, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=80,  Weight=3,    Couch=true },
            ["spear"]          = new(){ Id="spear",          Model="spear",         Cls=WeaponClass.Polearm,   Length=1.9,  Swing=0,  Thrust=28, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=93,  Weight=2.25, OneHandedPolearm=true, Brace=true, AltUsage="spear_overhand" },
            ["spear_overhand"] = new(){ Id="spear_overhand", Model="spear",         Cls=WeaponClass.Polearm,   Length=1.9,  Swing=22, Thrust=25, SwingType=DamageType.Cut,   ThrustType=DamageType.Pierce, SpeedRating=88,  Weight=2.25, OneHandedPolearm=true, AltUsage="spear" },
            ["javelin"]        = new(){ Id="javelin",        Model="javelin",       Cls=WeaponClass.Thrown,    Length=0,    Swing=0,  Thrust=36, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=89,  Weight=1.5,  MissileSpeed=30, Accuracy=89, Stack=4, AltUsage="javelin_melee", MissileModel="javelin" },
            ["javelin_melee"]  = new(){ Id="javelin_melee",  Model="javelin",       Cls=WeaponClass.Polearm,   Length=1.5,  Swing=0,  Thrust=22, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=95,  Weight=1.5,  OneHandedPolearm=true, AltUsage="javelin" },
            ["crossbow"]       = new(){ Id="crossbow",       Model="crossbow",      Cls=WeaponClass.Crossbow,  Length=0,    Swing=0,  Thrust=38, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=70,  Weight=4,    MissileSpeed=70, Accuracy=96, Reload=3.8 },
            ["bow"]            = new(){ Id="bow",            Model="bow",           Cls=WeaponClass.Bow,       Length=0,    Swing=0,  Thrust=18, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=97,  Weight=1,    MissileSpeed=52, Accuracy=95, DrawReq=2 },
            ["long_bow"]       = new(){ Id="long_bow",       Model="long_bow",      Cls=WeaponClass.Bow,       Length=0,    Swing=0,  Thrust=22, SwingType=DamageType.Pierce,ThrustType=DamageType.Pierce, SpeedRating=79,  Weight=1.8,  MissileSpeed=56, Accuracy=99, DrawReq=3 },
        };

        public static readonly IReadOnlyDictionary<string, TroopDef> Troops =
            new Dictionary<string, TroopDef>
        {
            // ── 金狮王国 (Team 0) ──────────────────────────────────────
            ["kd_footman"]   = new(){ Id="kd_footman",   Name="王国步兵",   Team=0, Role=Role.Infantry, Model="kd_footman",   Race=Race.Human, Weapons=new[]{"short_sword"},          Shield=ShieldRound, BaseHp=42, Armor=new(){ Head=30,  Body=30,  Leg=12                      }, ArmorWeight=16, Skill=0.5,
                Skills=Sk(str:12, ironflesh:2, powerStrike:2, shield:2, athletics:2, oneHanded:100) },
            ["kd_guard"]     = new(){ Id="kd_guard",     Name="御林卫兵",   Team=0, Role=Role.Infantry, Model="kd_guard",     Race=Race.Human, Weapons=new[]{"arming_sword"},         Shield=ShieldCrest, BaseHp=48, Armor=new(){ Head=52,  Body=48,  Leg=24, Shoulder=52, Arm=30 }, ArmorWeight=26, Skill=0.72, Officer=true, CaptainOf="inf", CaptainPerks=new(){ Basher=true, ShieldWall=true, SharpenTheTip=true, StoutDefender=true },
                Skills=Sk(str:18, agi:15, ironflesh:5, powerStrike:5, shield:4, athletics:4, oneHanded:160) },
            ["kd_halberdier"]= new(){ Id="kd_halberdier",Name="王国长戟兵", Team=0, Role=Role.Spear,    Model="kd_halberdier",Race=Race.Human, Weapons=new[]{"halberd"},              BaseHp=45, Armor=new(){ Head=45, Body=40, Leg=20 }, ArmorWeight=20, Skill=0.6,
                Skills=Sk(str:15, agi:12, ironflesh:4, powerStrike:4, athletics:3, polearm:140) },
            ["kd_swordsman"] = new(){ Id="kd_swordsman", Name="双手剑士",   Team=0, Role=Role.Shock,    Model="kd_swordsman", Race=Race.Human, Weapons=new[]{"claymore"},             BaseHp=46, Armor=new(){ Head=30, Body=34, Leg=14 }, ArmorWeight=15, Skill=0.72,
                Skills=Sk(str:18, agi:16, ironflesh:4, powerStrike:6, athletics:5, twoHanded:170) },
            ["kd_hammer"]    = new(){ Id="kd_hammer",    Name="王国锤卫",   Team=0, Role=Role.Shock,    Model="kd_hammer",    Race=Race.Human, Weapons=new[]{"warhammer"},            BaseHp=48, Armor=new(){ Head=45, Body=44, Leg=20 }, ArmorWeight=24, Skill=0.6,
                Skills=Sk(str:21, agi:12, ironflesh:6, powerStrike:6, athletics:2, twoHanded:150) },
            ["kd_spearman"]  = new(){ Id="kd_spearman",  Name="边境矛盾兵", Team=0, Role=Role.Infantry, Model="kd_spearman",  Race=Race.Human, Weapons=new[]{"spear","short_sword"},  Shield=ShieldRound, BaseHp=44, Armor=new(){ Head=35, Body=34, Leg=14 }, ArmorWeight=17, Skill=0.55,
                Skills=Sk(str:15, agi:12, ironflesh:3, powerStrike:3, shield:3, athletics:3, polearm:130, oneHanded:100) },
            ["kd_crossbow"]  = new(){ Id="kd_crossbow",  Name="矮人弩手",   Team=0, Role=Role.Archer,   Model="kd_crossbow",  Race=Race.Dwarf, Weapons=new[]{"crossbow","short_sword"},Shield=ShieldCrest, BaseHp=44, Armor=new(){ Head=40, Body=38, Leg=16 }, ArmorWeight=20, Skill=0.6, Arrows=18,
                Skills=Sk(str:14, agi:12, ironflesh:3, powerStrike:2, shield:2, athletics:2, crossbow:170, oneHanded:100) },
            ["kd_archer"]    = new(){ Id="kd_archer",    Name="长弓手",     Team=0, Role=Role.Archer,   Model="kd_archer",    Race=Race.Human, Weapons=new[]{"long_bow","short_sword"},                    BaseHp=40, Armor=new(){ Head=0,  Body=24, Leg=10 }, ArmorWeight=9,  Skill=0.6, Arrows=30, CaptainOf="arch", CaptainPerks=new(){ BowControl=true },
                Skills=Sk(str:13, agi:15, ironflesh:1, powerDraw:4, powerStrike:1, athletics:4, archery:160, oneHanded:90) },
            ["kd_knight"]    = new(){ Id="kd_knight",    Name="王国骑士",   Team=0, Role=Role.Cavalry,  Model="kd_knight",    Race=Race.Human, Weapons=new[]{"lance","arming_sword"},  Mount="horse",      BaseHp=48, Armor=new(){ Head=50, Body=52, Leg=26, Shoulder=54, Arm=34 }, Barding=new(){ Armor=24, Weight=20 }, ArmorWeight=28, Skill=0.7, CaptainOf="cav", CaptainPerks=new(){ Lancer=true },
                Skills=Sk(str:18, agi:15, ironflesh:5, powerStrike:5, riding:6, oneHanded:150, polearm:150) },

            // ── 赤牙氏族 (Team 1) ──────────────────────────────────────
            ["cl_warrior"]   = new(){ Id="cl_warrior",   Name="赤牙战士",   Team=1, Role=Role.Infantry, Model="cl_warrior",   Race=Race.Orc,   Weapons=new[]{"hatchet"},              Shield=ShieldRound, BaseHp=52, Armor=new(){ Head=26, Body=34, Leg=12, Arm=18 }, ArmorWeight=16, Skill=0.55,
                Skills=Sk(str:17, ironflesh:5, powerStrike:4, shield:3, athletics:2, oneHanded:125) },
            ["cl_guardian"]  = new(){ Id="cl_guardian",  Name="氏族护卫",   Team=1, Role=Role.Infantry, Model="cl_guardian",  Race=Race.Orc,   Weapons=new[]{"clan_axe"},             Shield=ShieldClan,  BaseHp=54, Armor=new(){ Head=20, Body=42, Leg=18 }, ArmorWeight=20, Skill=0.65, Officer=true, CaptainOf="inf", CaptainPerks=new(){ Braced=true, StrongGrip=true, Basher=true, StoutDefender=true },
                Skills=Sk(str:20, agi:13, ironflesh:6, powerStrike:5, shield:3, athletics:3, oneHanded:150) },
            ["cl_pikeman"]   = new(){ Id="cl_pikeman",   Name="赤牙长矛手", Team=1, Role=Role.Spear,    Model="cl_pikeman",   Race=Race.Orc,   Weapons=new[]{"glaive","hatchet"},                         BaseHp=54, Armor=new(){ Head=28, Body=38, Leg=16, Arm=20 }, ArmorWeight=17, Skill=0.62,
                Skills=Sk(str:19, agi:13, ironflesh:6, powerStrike:5, athletics:4, polearm:180, oneHanded:115) },
            ["cl_berserker"] = new(){ Id="cl_berserker", Name="狂战士",     Team=1, Role=Role.Shock,    Model="cl_berserker", Race=Race.Orc,   Weapons=new[]{"war_axe2"},                                 BaseHp=54, Armor=new(){ Head=0,  Body=22, Leg=10 }, ArmorWeight=8,  Skill=0.65,
                Skills=Sk(str:22, agi:16, ironflesh:7, powerStrike:6, athletics:6, twoHanded:160) },
            ["cl_crusher"]   = new(){ Id="cl_crusher",   Name="碎骨者",     Team=1, Role=Role.Shock,    Model="cl_crusher",   Race=Race.Orc,   Weapons=new[]{"maul"},                                     BaseHp=54, Armor=new(){ Head=20, Body=36, Leg=14 }, ArmorWeight=18, Skill=0.55,
                Skills=Sk(str:22, agi:11, ironflesh:7, powerStrike:6, athletics:2, twoHanded:140) },
            ["cl_skirmisher"]= new(){ Id="cl_skirmisher",Name="投矛手",     Team=1, Role=Role.Infantry, Model="cl_skirmisher",Race=Race.Orc,   Weapons=new[]{"javelin","hatchet"},    Shield=ShieldClan,  BaseHp=50, Armor=new(){ Head=20, Body=28, Leg=10 }, ArmorWeight=14, Skill=0.55,
                Skills=Sk(str:17, agi:13, ironflesh:4, powerStrike:4, shield:3, athletics:4, oneHanded:110, polearm:110, archery:120) },
            ["cl_wolfarcher"]= new(){ Id="cl_wolfarcher",Name="狼骑射手",   Team=1, Role=Role.HorseArcher,Model="cl_wolfarcher",Race=Race.Orc, Weapons=new[]{"bow","hatchet"},        Mount="wolf",       BaseHp=50, Armor=new(){ Head=10, Body=26, Leg=10 }, ArmorWeight=10, Skill=0.6, Arrows=18,
                Skills=Sk(str:15, agi:16, ironflesh:3, powerDraw:3, powerStrike:2, riding:6, horseArchery:6, archery:160, oneHanded:100) },
            ["cl_hunter"]    = new(){ Id="cl_hunter",    Name="赤牙猎手",   Team=1, Role=Role.Archer,   Model="cl_hunter",    Race=Race.Orc,   Weapons=new[]{"long_bow","hatchet"},                        BaseHp=48, Armor=new(){ Head=0,  Body=20, Leg=8  }, ArmorWeight=8,  Skill=0.55, Arrows=30, CaptainOf="arch", CaptainPerks=new(){ BowControl=true, StrongBows=true },
                Skills=Sk(str:15, agi:14, ironflesh:2, powerDraw:5, powerStrike:2, athletics:4, archery:165, oneHanded:100) },
            ["cl_wolfrider"] = new(){ Id="cl_wolfrider", Name="狼骑兵",     Team=1, Role=Role.Cavalry,  Model="cl_wolfrider", Race=Race.Orc,   Weapons=new[]{"glaive","war_axe"},     Mount="wolf",       BaseHp=54, Armor=new(){ Head=30, Body=46, Leg=20, Shoulder=40, Arm=24 }, Barding=new(){ Armor=14, Weight=12 }, ArmorWeight=22, Skill=0.65, CaptainOf="cav", CaptainPerks=new(){ Lancer=true },
                Skills=Sk(str:21, agi:14, ironflesh:7, powerStrike:6, riding:6, polearm:170, twoHanded:140) },
        };

        public static readonly IReadOnlyDictionary<string, MountDef> Mounts =
            new Dictionary<string, MountDef>
        {
            // 约对应 Warband 的 Hunter：中等骑兵马
            ["horse"] = new(){ Model="horse", Skin=0, Hp=160, Armor=24, SpeedRating=43, Maneuver=44, Charge=24, Length=1.6 },
            // 更快更灵活，冲撞力稍弱：类草原马
            ["wolf"]  = new(){ Model="wolf",  Skin=0, Hp=165, Armor=26, SpeedRating=45, Maneuver=50, Charge=20, Length=1.4 },
        };
    }
}
