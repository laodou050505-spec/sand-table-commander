using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 中英双语（用户要求全游戏都支持）。所有给玩家看的文字都从这里取，不在别处写死。
    /// 用法：<c>Loc.T("order.charge")</c>、<c>Loc.F("say.charge", label)</c>（{0} 占位）。语言记在 PlayerPrefs，切换时发 <see cref="Changed"/>。
    /// 中文沿用原型（Three.js 版）的说法；英文名 Sand Table Commander（用户定）。
    /// </summary>
    public static class Loc
    {
        public enum Lang { Zh, En }

        const string PrefKey = "wartable.lang";
        static Lang? current;

        public static Lang Current
        {
            get
            {
                if (current == null)
                {
                    int saved = PlayerPrefs.GetInt(PrefKey, -1);
                    // 第一次进游戏：跟随系统语言（中文系统用中文，其余英文）
                    current = saved >= 0 ? (Lang)saved
                        : Application.systemLanguage is SystemLanguage.Chinese or SystemLanguage.ChineseSimplified or SystemLanguage.ChineseTraditional ? Lang.Zh : Lang.En;
                }
                return current.Value;
            }
            set
            {
                if (current == value) return;
                current = value;
                PlayerPrefs.SetInt(PrefKey, (int)value);
                Changed?.Invoke();
            }
        }

        public static event Action Changed;

        public static string T(string key)
        {
            // 11-11 用户：换时代后兵种名称也要换 → 哥特时代先找 “key@g”，没有再用原文
            int era = Settings.Era;   // 11-12：按时代后缀找（@g 哥特、@f 未来…）
            if (era > 0 && Table.TryGetValue(key + "@" + Settings.EraSuffix[era], out var eg)) return Current == Lang.Zh ? eg.zh : eg.en;
            if (Table.TryGetValue(key, out var e)) return Current == Lang.Zh ? e.zh : e.en;
            Debug.LogWarning($"[Loc] 缺少文字 {key}");
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(T(key), args);

        /// <summary>两种语言都要（比如封面上中英并列）。</summary>
        public static (string zh, string en) Both(string key) => Table.TryGetValue(key, out var e) ? e : (key, key);

        static readonly Dictionary<string, (string zh, string en)> Table = new()
        {
            // ── 游戏
            ["game.title"] = ("将军沙盘", "Sand Table Commander"),
            ["game.tagline"] = ("你的桌子，就是战场", "Your table is the battlefield"),
            ["ui.start"] = ("开始", "Begin"),
            ["ui.language"] = ("语言", "Language"),
            ["ui.lang.zh"] = ("中文", "中文"),
            ["ui.lang.en"] = ("English", "English"),
            ["ui.pause"] = ("暂停", "Pause"),
            ["ui.resume"] = ("继续", "Resume"),
            ["ui.slow"] = ("慢动作", "Slow Motion"),
            ["ui.restart"] = ("再来一局", "Play Again"),
            ["ui.replace"] = ("重新摆放", "Reposition Table"),
            ["ui.deploy"] = ("布阵", "Deployment"),
            ["ui.fight"] = ("开战", "Begin Battle"),

            // ── 两军
            ["team.0"] = ("金狮王国", "Kingdom of the Golden Lion"),
            ["team.1"] = ("赤牙氏族", "Redfang Clan"),

            // ── 部队（四类编队）
            ["group.inf"] = ("步兵", "Infantry"),
            ["group.arch"] = ("弓兵", "Archers"),   // 和旗牌 / 目标牌一致（Tools/era_names.py）
            ["group.cav"] = ("骑兵", "Cavalry"),
            ["group.ha"] = ("骑射", "Mounted Archers"),
            // 哥特时代（11-11 用户选定）：铁卫 / 射手 / 铁骑 / 猎骑
            ["group.inf@g"] = ("铁卫", "Ironguard"),
            ["group.arch@g"] = ("射手", "Marksmen"),
            ["group.cav@g"] = ("铁骑", "Lancers"),
            ["group.ha@g"] = ("猎骑", "Outriders"),
            ["team.0@g"] = ("圣钟骑士团", "Order of the Sacred Bell"),
            ["team.1@g"] = ("炉铁氏族", "Forgeiron Clan"),
            // 未来时代（11-12 用户选定样图 A、兵种名）：锋卫 / 光弓 / 机骑 / 游骑；光棱军团 对 虚空蜂群
            ["group.inf@f"] = ("锋卫", "Vanguard"),
            ["group.arch@f"] = ("光弓", "Lightbows"),
            ["group.cav@f"] = ("机骑", "Mech Riders"),
            ["group.ha@f"] = ("游骑", "Outrunners"),
            ["team.0@f"] = ("光棱军团", "Prism Legion"),
            ["team.1@f"] = ("虚空蜂群", "Void Swarm"),
            // 现代时代（11-13，样图 A）：名字是我先定的默认值，用户还没确认
            ["group.inf@m"] = ("突击", "Assault"),
            ["group.arch@m"] = ("枪手", "Rifles"),
            ["group.cav@m"] = ("摩骑", "Bikers"),
            ["group.ha@m"] = ("飞车", "Raiders"),
            ["team.0@m"] = ("蓝盾特勤", "Blue Shield"),
            ["team.1@m"] = ("赤砂佣兵", "Red Sand"),
            // 深空（11-13 用户选样图 C、要重型机甲感；名字是我先定的，待用户确认）
            ["group.inf@s"] = ("重甲", "Heavies"),
            ["group.arch@s"] = ("轨枪", "Railguns"),
            ["group.cav@s"] = ("星骑", "Striders"),
            ["group.ha@s"] = ("掠车", "Crawlers"),
            ["team.0@s"] = ("星枪团", "Star Lancers"),
            ["team.1@s"] = ("余烬矿帮", "Ember Clans"),
            ["glyph.inf"] = ("步", "INF"),
            ["glyph.arch"] = ("弓", "ARC"),
            ["glyph.cav"] = ("骑", "CAV"),
            ["glyph.ha"] = ("射", "HA"),
            ["group.join"] = ("、", ", "),

            // ── 兵种
            ["troop.kd_footman"] = ("王国步兵", "Kingdom Footman"),
            ["troop.kd_guard"] = ("御林卫兵", "Royal Guard"),
            ["troop.kd_halberdier"] = ("王国长戟兵", "Kingdom Halberdier"),
            ["troop.kd_swordsman"] = ("双手剑士", "Greatswordsman"),
            ["troop.kd_hammer"] = ("王国锤卫", "Hammer Warden"),
            ["troop.kd_spearman"] = ("边境矛盾兵", "Border Spearman"),
            ["troop.kd_crossbow"] = ("矮人弩手", "Dwarven Crossbowman"),
            ["troop.kd_archer"] = ("长弓手", "Longbowman"),
            ["troop.kd_knight"] = ("王国骑士", "Kingdom Knight"),
            ["troop.cl_warrior"] = ("赤牙战士", "Redfang Warrior"),
            ["troop.cl_guardian"] = ("氏族护卫", "Clan Guardian"),
            ["troop.cl_pikeman"] = ("赤牙长矛手", "Redfang Pikeman"),
            ["troop.cl_berserker"] = ("狂战士", "Berserker"),
            ["troop.cl_crusher"] = ("碎骨者", "Bonecrusher"),
            ["troop.cl_skirmisher"] = ("投矛手", "Javelineer"),
            ["troop.cl_wolfarcher"] = ("狼骑射手", "Wolf Archer"),
            ["troop.cl_hunter"] = ("赤牙猎手", "Redfang Hunter"),
            ["troop.cl_wolfrider"] = ("狼骑兵", "Wolf Rider"),

            // ── 阵型
            ["arr.line"] = ("横队", "Line"),
            ["arr.shieldwall"] = ("盾墙", "Shield Wall"),
            ["arr.loose"] = ("散阵", "Loose"),
            ["arr.circle"] = ("圆阵", "Circle"),
            ["arr.square"] = ("方阵", "Square"),
            ["arr.skein"] = ("楔形", "Wedge"),
            ["arr.column"] = ("纵队", "Column"),
            ["arr.auto"] = ("阵型交给 AI", "AI Formation"),

            // ── 战旗上的当前军令
            ["order.auto"] = ("AI", "AI"),
            ["order.charge"] = ("冲锋", "Charge"),
            ["order.hold"] = ("坚守", "Hold"),
            ["order.follow"] = ("跟随", "Follow"),
            ["order.advance"] = ("前进", "Advance"),
            ["order.fallback"] = ("后撤", "Fall Back"),
            ["order.retreat"] = ("撤退", "Retreat"),

            // ── 军令回执（{0} = 部队名）
            ["say.pickFirst"] = ("先选一支部队：看着它捏一下", "Pick a unit first: look at it and pinch"),
            ["say.attack"] = ("{0}：冲向敌军{1}！", "{0}: Charge the enemy {1}!"),
            ["say.readyHint"] = ("摆好阵后，看着我方帅旗捏一下开战", "When your lines are set, look at your standard and pinch to begin"),
            ["say.notYet"] = ("开战后才能下这道令；现在先把部队摆好", "That order comes after the battle begins — deploy your troops first"),
            ["say.fight"] = ("开战！", "To battle!"),
            ["say.move"] = ("{0}：移动", "{0}: Move"),
            ["say.line"] = ("{0}：沿线列阵", "{0}: Form up along the line"),
            ["say.charge"] = ("{0}：冲锋！", "{0}: Charge!"),
            ["say.hold"] = ("{0}：原地坚守", "{0}: Hold position"),
            ["say.follow"] = ("{0}：跟随主将", "{0}: Follow the general"),
            ["say.forward"] = ("{0}：前进十步", "{0}: Advance ten paces"),
            ["say.back"] = ("{0}：后退十步", "{0}: Fall back ten paces"),
            ["say.retreat"] = ("{0}：撤退！", "{0}: Retreat!"),
            ["say.face"] = ("{0}：面向敌军", "{0}: Face the enemy"),
            ["say.auto"] = ("{0}：交给 AI", "{0}: Under AI command"),
            ["say.tighter"] = ("{0}：靠紧", "{0}: Close ranks"),
            ["say.looser"] = ("{0}：散开", "{0}: Spread out"),
            ["say.holdFire"] = ("{0}：停止射击", "{0}: Hold fire"),
            ["say.fireAtWill"] = ("{0}：自由射击", "{0}: Fire at will"),
            ["say.arrangement"] = ("{0}：{1}", "{0}: {1}"),
            ["say.generalDead"] = ("主将已经阵亡", "The general has fallen"),
            ["say.ourGeneralDown"] = ("我方主将阵亡！全军士气大跌", "Our general has fallen! Morale collapses"),
            ["say.theirGeneralDown"] = ("敌方主将阵亡！敌军士气大跌", "The enemy general has fallen! Their morale breaks"),

            // ── 结算
            ["result.victory"] = ("胜利", "Victory"),
            ["result.defeat"] = ("战败", "Defeat"),
            ["result.survivors"] = ("存活 {0} / {1}", "Survivors {0} / {1}"),
            ["result.routed"] = ("击溃 {0}", "Enemies routed {0}"),
            ["result.time"] = ("用时 {0}", "Time {0}"),
        };
    }
}
