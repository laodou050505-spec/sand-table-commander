using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WarTable.Sim;

namespace WarTable.Campaign
{
    // ═══════════════════════════════════════════════════════════════════════
    // 玩法版（肉鸽战役）的纯逻辑层：存档结构、分支航线图、敌军编制、伤亡、星级、奖励、纹章 / 晋升效果。
    // 设计见 Docs/玩法版设计_v1.md。不碰画面，编辑器里能直接跑（Editor/CampaignTest）。
    // ═══════════════════════════════════════════════════════════════════════

    public enum NodeKind { Battle, Elite, Supply, Armory, Boss, Ruins }   // Ruins（11-20）：遗迹，一个两难取舍；只能加在最后（存档里存的是序号）

    [Serializable] public class TroopCount { public string id; public int n; public TroopCount(string id, int n) { this.id = id; this.n = n; } }

    [Serializable]
    public class MapNode
    {
        public int id, layer;
        public NodeKind kind;
        /// <summary>在航线图上的位置（0–1：x 从玩家左到右，y 从近（0）到远（1））。</summary>
        public float x, y;
        public List<int> next = new();
        public bool done;
        /// <summary>这一仗的随机种子（重打同一仗用同一个，敌军不变）。</summary>
        public int seed;
    }

    /// <summary>一轮战役的全部状态。存成 JSON（CampaignStore）。</summary>
    [Serializable]
    public class RunState
    {
        public int version = 1, seed, chapter;
        public List<MapNode> nodes = new();
        /// <summary>现在停在哪个节点（−1 = 还没出发）。</summary>
        public int at = -1;
        /// <summary>健康的兵 / 伤兵（伤兵下一场不能上）/ 各兵种晋升阶数。</summary>
        public List<TroopCount> roster = new(), wounded = new(), tiers = new();
        /// <summary>招募池里已经解锁的兵种。</summary>
        public List<string> unlocked = new();
        public List<string> relics = new();
        public int crystals, cap = 30;
        public int battles, wins, retries;
        /// <summary>上一次出战带了谁（下次默认照这个填）。</summary>
        public List<TroopCount> lastMuster = new();
        public bool over, won;

        public int Get(List<TroopCount> l, string id) => l.FirstOrDefault(t => t.id == id)?.n ?? 0;
        public void Add(List<TroopCount> l, string id, int d)
        {
            var t = l.FirstOrDefault(x => x.id == id);
            if (t == null) { if (d <= 0) return; l.Add(new TroopCount(id, d)); return; }
            t.n = Math.Max(0, t.n + d);
            if (t.n == 0) l.Remove(t);
        }
        public int Total(List<TroopCount> l) => l.Sum(t => t.n);
        public MapNode Node(int id) => nodes.FirstOrDefault(n => n.id == id);
        /// <summary>现在可以去的节点：还没出发时是第 0 层，否则是当前节点连着的下一层。</summary>
        public IEnumerable<MapNode> Reachable() => at < 0 ? nodes.Where(n => n.layer == 0) : Node(at).next.Select(Node);
        public bool Has(string relic) => relics.Contains(relic);
        public RunState Clone() => JsonUtility.FromJson<RunState>(JsonUtility.ToJson(this));
    }

    /// <summary>纹章（全军被动）。名字、简述走 Loc（键 relic.<id>.name / relic.<id>.desc），图标 relic_<id>。</summary>
    public static class Relics
    {
        public static readonly string[] All = { "bulwark", "ironhorse", "quiver", "resolve", "veterans", "shieldwall", "medics" };
        public static readonly Dictionary<string, (string zh, string en)> Names = new()
        {
            ["bulwark"] = ("坚甲", "Bulwark"), ["ironhorse"] = ("铁骑", "Iron Steeds"), ["quiver"] = ("满囊", "Full Quiver"),
            ["resolve"] = ("军心", "Resolve"), ["veterans"] = ("老兵", "Veterans"), ["shieldwall"] = ("盾墙", "Shield Wall"), ["medics"] = ("医官", "Field Medics"),
        };
    }

    public enum RewardKind { Reinforce, Recruit, Promote, Relic, Crystals }

    [Serializable]
    public class Reward
    {
        public RewardKind kind;
        public string troop, relic;
        public int n;
    }

    public static class CampaignRules
    {
        // ── 兵种 ───────────────────────────────────────────────────────────
        /// <summary>开局就有的兵种和人数（统率 30）。</summary>
        public static readonly (string id, int n)[] StartRoster = { ("kd_footman", 8), ("kd_spearman", 6), ("kd_archer", 6), ("kd_knight", 5), ("kd_guard", 5) };
        /// <summary>能靠奖励解锁的兵种。</summary>
        public static readonly string[] RecruitPool = { "kd_halberdier", "kd_swordsman", "kd_hammer", "kd_crossbow" };
        public static readonly string[] PlayerTroops = { "kd_guard", "kd_footman", "kd_spearman", "kd_halberdier", "kd_swordsman", "kd_hammer", "kd_archer", "kd_crossbow", "kd_knight" };
        public const int MaxTier = 3;

        public static Role RoleOf(string id) => WeaponsData.Troops.TryGetValue(id, out var t) ? t.Role : Role.Infantry;

        // ── 新战役 ─────────────────────────────────────────────────────────
        public static RunState NewRun(int seed)
        {
            var r = new RunState { seed = seed, chapter = 0, crystals = 0, cap = 30 };
            foreach (var (id, n) in StartRoster) r.Add(r.roster, id, n);
            r.unlocked = StartRoster.Select(s => s.id).ToList();
            r.lastMuster = r.roster.Select(t => new TroopCount(t.id, t.n)).ToList();
            r.nodes = MapGen.Generate(seed, 0);
            return r;
        }

        // ── 敌军 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 这一仗的敌军：按层数涨人数（第一章 18 → 约 40 人），精英 ×1.3 并加重甲兵种，Boss 是满编精英（第 1 阶段先用它占位，巨型单位第 2 阶段做）。
        /// 编制按“统帅性格”从几套模板里挑，同一个节点种子永远出同一支军队（重打不变）。
        /// </summary>
        /// <summary>敌军人数 / 玩家统率：第一层、最后一层、精英倍数、Boss（自动试玩 CampaignTest 调）。</summary>
        public static float Ratio0 = 0.55f, Ratio1 = 0.8f, EliteMul = 1.1f, BossRatio = 0.95f, WoundWin = 0.7f, WoundLose = 0.3f;
        public static ArmySpec EnemyArmy(RunState run, MapNode node)
        {
            var rng = new System.Random(node.seed);
            int layers = run.nodes.Max(n => n.layer);
            float t = layers > 0 ? node.layer / (float)layers : 0f;
            // 敌军规模跟着玩家的统率走（兽人单兵比王国人类强一截：血 50+ 对 42～48，等人数时敌方略占优）。
            // 第一版按固定人数涨（18 → 40）：自动试玩第 1 层就折一半、第 2 层精英重打 3 次都赢不了 → 改成统率的 60% → 90%
            float k0 = Mathf.Lerp(Ratio0, Ratio1, t);
            if (node.kind == NodeKind.Elite) k0 *= EliteMul;
            if (node.kind == NodeKind.Boss) k0 = BossRatio;
            // 规模基准 = 一半看统率、一半看现在能上场的人数：兵打少了敌人也略少，免得一路滑坡（第二版自动试玩出现死亡螺旋）；但不完全跟着降，损失仍然有分量
            float basis = 0.5f * run.cap + 0.5f * Mathf.Min(run.cap, run.Total(run.roster));
            int budget = Mathf.Max(8, Mathf.RoundToInt(basis * k0));
            // 模板：各兵种权重。冒进（狂战、狼骑多）/ 稳守（护卫、长矛、猎手多）/ 包抄（狼骑、狼骑射多）/ 均衡
            var templates = new[]
            {
                new (string id, float w)[] { ("cl_warrior", 3), ("cl_berserker", 2), ("cl_crusher", 1), ("cl_wolfrider", 2), ("cl_hunter", 1) },
                new (string id, float w)[] { ("cl_guardian", 3), ("cl_pikeman", 3), ("cl_hunter", 2), ("cl_skirmisher", 1) },
                new (string id, float w)[] { ("cl_warrior", 2), ("cl_wolfrider", 3), ("cl_wolfarcher", 3), ("cl_skirmisher", 1) },
                new (string id, float w)[] { ("cl_warrior", 2), ("cl_guardian", 2), ("cl_pikeman", 1), ("cl_hunter", 2), ("cl_wolfrider", 1), ("cl_crusher", 1) },
            };
            var tpl = templates[rng.Next(templates.Length)].ToList();
            if (node.kind != NodeKind.Battle) { tpl.Add(("cl_crusher", 2)); tpl.Add(("cl_guardian", 2)); }
            // 11-20 新敌种（规则不同，不只是数值高）：第 2 层起按统帅性格混进来；精英战必带盾墙重甲和旗手
            if (node.layer >= 2)
            {
                if (tpl.Any(x => x.id == "cl_berserker")) tpl.Add(("cl_sapper", 1.5f));          // 冒进：爆破兵
                if (tpl.Any(x => x.id == "cl_pikeman")) tpl.Add(("cl_bulwark", 2f));             // 稳守：盾墙重甲
                if (node.layer >= 3 && rng.NextDouble() < 0.5) tpl.Add(("cl_standard", 0.6f));   // 一半的仗带一名鼓舞旗手
                if (node.kind != NodeKind.Battle) { tpl.Add(("cl_bulwark", 1.5f)); if (node.layer >= 4) { tpl.Add(("cl_standard", 0.7f)); tpl.Add(("cl_sapper", 1f)); } }
            }
            // 第一层只用最基础的几种，别一上来就是狼骑射
            if (node.layer == 0) tpl = tpl.Where(x => x.id != "cl_wolfarcher" && x.id != "cl_crusher").ToList();
            float sum = tpl.Sum(x => x.w);
            var spec = new ArmySpec();
            int given = 0;
            foreach (var (id, w) in tpl)
            {
                int k = Mathf.FloorToInt(budget * w / sum);
                if (k <= 0) continue;
                spec[id] = (spec.TryGetValue(id, out var o) ? o : 0) + k; given += k;
            }
            while (given < budget) { var id = tpl[rng.Next(tpl.Count)].id; spec[id] = (spec.TryGetValue(id, out var o) ? o : 0) + 1; given++; }
            return spec;
        }

        /// <summary>玩家这一仗的军队（从选兵结果来）。</summary>
        public static ArmySpec PlayerArmy(List<TroopCount> muster)
        {
            var spec = new ArmySpec();
            foreach (var t in muster) if (t.n > 0) spec[t.id] = t.n;
            return spec;
        }

        /// <summary>默认出战名单：照上一次带的，不够就按现有兵力补，超出统率就砍。</summary>
        public static List<TroopCount> DefaultMuster(RunState run)
        {
            var m = new List<TroopCount>();
            int left = run.cap;
            foreach (var t in run.lastMuster.Concat(run.roster))
            {
                if (m.Any(x => x.id == t.id)) continue;
                int k = Math.Min(Math.Min(run.Get(run.roster, t.id), run.Get(run.lastMuster, t.id) > 0 ? run.Get(run.lastMuster, t.id) : run.Get(run.roster, t.id)), left);
                if (k <= 0) continue;
                m.Add(new TroopCount(t.id, k)); left -= k;
            }
            // 统率还有剩、兵营里还有人：补满
            foreach (var t in run.roster)
            {
                if (left <= 0) break;
                var e = m.FirstOrDefault(x => x.id == t.id);
                int have = e?.n ?? 0, more = Math.Min(t.n - have, left);
                if (more <= 0) continue;
                if (e == null) m.Add(new TroopCount(t.id, more)); else e.n += more;
                left -= more;
            }
            return m;
        }

        // ── 开局时的加成（纹章、晋升） ──────────────────────────────────────
        /// <summary>在 Scenario.SetupBattle 之后、开战之前调用：给我军士兵加纹章和晋升的数值。</summary>
        public static void ApplyBonuses(Battle b, RunState run)
        {
            foreach (var a in b.Agents)
            {
                if (a.Team != 0) continue;
                var role = a.Troop.Role;
                double hp = 1;
                int tier = run.Get(run.tiers, a.Troop.Id);
                hp += 0.15 * tier;
                if (run.Has("bulwark") && (role == Role.Infantry || role == Role.Spear || role == Role.Shock)) hp += 0.25;
                if (run.Has("ironhorse") && (role == Role.Cavalry || role == Role.HorseArcher)) hp += 0.3;
                a.MaxHp *= hp; a.Hp = a.MaxHp;
                if (run.Has("shieldwall")) a.ShieldHp *= 1.5;
                if (run.Has("quiver") && a.Ammo != null) for (int i = 0; i < a.Ammo.Length; i++) a.Ammo[i] = Mathf.RoundToInt(a.Ammo[i] * 1.5f);
                if (run.Has("quiver")) a.Arrows = Mathf.RoundToInt(a.Arrows * 1.5f);
                a.Morale += 5 * tier;
            }
            if (run.Has("resolve")) { b.StartMorale[0] += 20; foreach (var a in b.Agents) if (a.Team == 0) a.Morale += 20; }
        }

        // ── 战后 ───────────────────────────────────────────────────────────
        public struct Outcome
        {
            public bool won;
            public int stars, sent, alive, dead, wounded, crystals;
            public List<TroopCount> lostDead, lostWounded;
        }

        /// <summary>
        /// 结算：倒下的兵按比例分成伤兵（下一场不能上）和阵亡（永久失去）。赢了伤兵比例 55%、输了 25%（纹章“老兵”+20%）。
        /// 星级按存活比例：≥80% 三星、≥55% 两星、赢了至少一星。星晶 = 基础 10 + 星 × 5，精英 ×1.5，Boss ×2；一次就过（这一仗没重打过）再 +25%。
        /// </summary>
        public static Outcome Resolve(RunState run, MapNode node, List<TroopCount> muster, Dictionary<string, int> aliveByTroop, bool won, bool firstTry)
        {
            var o = new Outcome { won = won, lostDead = new(), lostWounded = new() };
            float woundRate = (won ? WoundWin : WoundLose) + (run.Has("veterans") ? 0.2f : 0f);
            var rng = new System.Random(node.seed * 31 + run.battles);
            foreach (var t in muster)
            {
                int alive = aliveByTroop.TryGetValue(t.id, out var a) ? Math.Min(a, t.n) : 0;
                int down = t.n - alive;
                int w = 0;
                for (int i = 0; i < down; i++) if (rng.NextDouble() < woundRate) w++;
                int d = down - w;
                o.sent += t.n; o.alive += alive; o.dead += d; o.wounded += w;
                if (d > 0) o.lostDead.Add(new TroopCount(t.id, d));
                if (w > 0) o.lostWounded.Add(new TroopCount(t.id, w));
            }
            float ratio = o.sent > 0 ? o.alive / (float)o.sent : 0f;
            o.stars = !won ? 0 : ratio >= 0.8f ? 3 : ratio >= 0.55f ? 2 : 1;
            if (won)
            {
                float c = 10 + 5 * o.stars;
                if (node.kind == NodeKind.Elite) c *= 1.5f;
                if (node.kind == NodeKind.Boss) c *= 2f;
                if (firstTry) c *= 1.25f;
                o.crystals = Mathf.RoundToInt(c);
            }
            return o;
        }

        /// <summary>把结算落到存档上（玩家点了“继续”才调；点“重打”就不调，直接用开打前的快照）。</summary>
        public static void Commit(RunState run, MapNode node, List<TroopCount> muster, Outcome o)
        {
            // 上一场的伤兵这一场歇过了：归队（纹章“医官”：本场的伤兵也立刻归队一半）
            foreach (var t in run.wounded.ToList()) { run.Add(run.roster, t.id, t.n); }
            run.wounded.Clear();
            foreach (var t in o.lostDead) run.Add(run.roster, t.id, -t.n);
            foreach (var t in o.lostWounded)
            {
                int back = run.Has("medics") ? t.n / 2 : 0;
                run.Add(run.roster, t.id, -(t.n - back));
                run.Add(run.wounded, t.id, t.n - back);
            }
            run.crystals += o.crystals;
            run.battles++; if (o.won) run.wins++;
            run.lastMuster = muster.Select(t => new TroopCount(t.id, t.n)).ToList();
            node.done = true; run.at = node.id;
            if (node.kind == NodeKind.Boss && o.won) { run.over = true; run.won = true; }
            if (run.Total(run.roster) + run.Total(run.wounded) == 0) run.over = true;
        }

        // ── 三选一 ─────────────────────────────────────────────────────────
        public static List<Reward> Rewards(RunState run, MapNode node)
        {
            var rng = new System.Random(node.seed * 7 + 3);
            var pool = new List<Reward>();
            // 补员：现有兵种 +6（骑兵 +4）
            foreach (var id in run.unlocked) pool.Add(new Reward { kind = RewardKind.Reinforce, troop = id, n = RoleOf(id) == Role.Cavalry ? 5 : 8 });
            // 新兵种
            foreach (var id in RecruitPool.Where(x => !run.unlocked.Contains(x))) pool.Add(new Reward { kind = RewardKind.Recruit, troop = id, n = 6 });
            // 晋升
            foreach (var id in run.unlocked.Where(x => run.Get(run.tiers, x) < MaxTier && run.Get(run.roster, x) > 0)) pool.Add(new Reward { kind = RewardKind.Promote, troop = id, n = 1 });
            // 纹章
            foreach (var r in Relics.All.Where(x => !run.relics.Contains(x))) pool.Add(new Reward { kind = RewardKind.Relic, relic = r });
            var pick = new List<Reward>();
            // 精英 / Boss 保底一个纹章
            if (node.kind != NodeKind.Battle)
            {
                var rel = pool.Where(p => p.kind == RewardKind.Relic).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (rel != null) { pick.Add(rel); pool.Remove(rel); }
            }
            // 其余从不同类型里各挑一个，尽量不重类
            foreach (var kind in pool.Select(p => p.kind).Distinct().OrderBy(_ => rng.Next()))
            {
                if (pick.Count >= 3) break;
                if (pick.Any(p => p.kind == kind)) continue;
                var c = pool.Where(p => p.kind == kind).OrderBy(_ => rng.Next()).First();
                pick.Add(c); pool.Remove(c);
            }
            while (pick.Count < 3 && pool.Count > 0) { var c = pool[rng.Next(pool.Count)]; pick.Add(c); pool.Remove(c); }
            while (pick.Count < 3) pick.Add(new Reward { kind = RewardKind.Crystals, n = 20 });
            return pick;
        }

        public static void Apply(RunState run, Reward r)
        {
            switch (r.kind)
            {
                case RewardKind.Reinforce: run.Add(run.roster, r.troop, r.n); break;
                case RewardKind.Recruit: run.Add(run.roster, r.troop, r.n); if (!run.unlocked.Contains(r.troop)) run.unlocked.Add(r.troop); break;
                case RewardKind.Promote: run.Add(run.tiers, r.troop, 1); break;
                case RewardKind.Relic: if (!run.relics.Contains(r.relic)) run.relics.Add(r.relic); break;
                case RewardKind.Crystals: run.crystals += r.n; break;
            }
        }

        // ── 遗迹：两难取舍（设计稿第 2 节）─────────────────────────────────
        /// <summary>一个取舍：拿什么换什么。kind = shrine（献兵换纹章）/ ore（挖矿得星晶、有人受伤）/ stragglers（花星晶收编散兵）。</summary>
        public struct Dilemma { public string kind, troop, relic; public int cost, gain; public bool ok; }

        public static Dilemma DilemmaOf(RunState run, MapNode node)
        {
            var rng = new System.Random(node.seed * 13 + 5);
            var kinds = new List<string> { "ore", "stragglers" };
            if (Relics.All.Any(r => !run.relics.Contains(r))) kinds.Add("shrine");
            var d = new Dilemma { kind = kinds[rng.Next(kinds.Count)] };
            // 涉及的兵种：现在人最多的那种（献兵 / 受伤都落在它身上，玩家一眼看得出要付出什么）
            var big = run.roster.OrderByDescending(t => t.n).FirstOrDefault();
            switch (d.kind)
            {
                case "shrine":
                    d.troop = big?.id; d.cost = 4; d.relic = Relics.All.Where(r => !run.relics.Contains(r)).OrderBy(_ => rng.Next()).First();
                    d.ok = big != null && big.n >= d.cost; break;
                case "ore":
                    d.troop = big?.id; d.cost = 3; d.gain = 40; d.ok = big != null && big.n >= d.cost; break;
                default:
                    var pool = run.unlocked.Concat(RecruitPool).Distinct().ToList();
                    d.troop = pool[rng.Next(pool.Count)]; d.cost = 15; d.gain = RoleOf(d.troop) == Role.Cavalry ? 4 : 6; d.ok = run.crystals >= d.cost; break;
            }
            return d;
        }

        /// <summary>接受这个取舍。</summary>
        public static void Accept(RunState run, Dilemma d)
        {
            if (!d.ok) return;
            switch (d.kind)
            {
                case "shrine": run.Add(run.roster, d.troop, -d.cost); if (!run.relics.Contains(d.relic)) run.relics.Add(d.relic); break;
                case "ore": run.Add(run.roster, d.troop, -d.cost); run.Add(run.wounded, d.troop, d.cost); run.crystals += d.gain; break;
                default:
                    run.crystals -= d.cost; run.Add(run.roster, d.troop, d.gain);
                    if (!run.unlocked.Contains(d.troop)) run.unlocked.Add(d.troop);
                    break;
            }
        }

        // ── 补给站 ─────────────────────────────────────────────────────────
        public const int HealCost = 2, RecruitCost = 4, CapCost = 30;
        /// <summary>治疗伤兵：每人 2 星晶，立刻归队。</summary>
        public static bool Heal(RunState run, string id)
        {
            if (run.Get(run.wounded, id) <= 0 || run.crystals < HealCost) return false;
            run.Add(run.wounded, id, -1); run.Add(run.roster, id, 1); run.crystals -= HealCost; return true;
        }
        /// <summary>招募：已解锁兵种每人 4 星晶（骑兵 6）。</summary>
        public static bool Recruit(RunState run, string id)
        {
            int cost = RoleOf(id) == Role.Cavalry ? RecruitCost + 2 : RecruitCost;
            if (!run.unlocked.Contains(id) || run.crystals < cost) return false;
            run.Add(run.roster, id, 1); run.crystals -= cost; return true;
        }
        /// <summary>统率 +5：30 星晶。</summary>
        public static bool RaiseCap(RunState run)
        {
            if (run.crystals < CapCost || run.cap >= 60) return false;
            run.cap += 5; run.crystals -= CapCost; return true;
        }
    }

    /// <summary>
    /// 分支航线图：第 0 层 3 个起点，往远处推进，最后一层是 Boss。每个节点连下一层 1～2 个相邻节点，连线不交叉。
    /// 节点类型：第 0 层都是普通战；第 2 层以后出现精英；中段和 Boss 前各保证一个补给站；军械库偶尔出现。
    /// </summary>
    public static class MapGen
    {
        public const int Layers = 9;   // 0..7 普通层 + 第 8 层 Boss

        public static List<MapNode> Generate(int seed, int chapter)
        {
            var rng = new System.Random(seed * 101 + chapter);
            var nodes = new List<MapNode>();
            var byLayer = new List<List<MapNode>>();
            int id = 0;
            for (int L = 0; L < Layers; L++)
            {
                int count = L == 0 ? 3 : L == Layers - 1 ? 1 : L == Layers - 2 ? 2 : 2 + rng.Next(3);
                var row = new List<MapNode>();
                for (int i = 0; i < count; i++)
                {
                    float x = count == 1 ? 0.5f : 0.14f + 0.72f * i / (count - 1);
                    x += (float)(rng.NextDouble() - 0.5) * (count == 1 ? 0f : 0.08f);
                    float y = 0.07f + 0.86f * L / (Layers - 1) + (float)(rng.NextDouble() - 0.5) * 0.03f;
                    var n = new MapNode { id = id++, layer = L, x = x, y = y, seed = rng.Next(1, 1 << 20), kind = NodeKind.Battle };
                    row.Add(n); nodes.Add(n);
                }
                byLayer.Add(row);
            }
            // 连线：按横向位置把相邻两层“拉链式”连起来——每个节点至少一条出线、下一层每个节点至少一条入线，不交叉
            for (int L = 0; L < Layers - 1; L++)
            {
                var a = byLayer[L]; var b = byLayer[L + 1];
                int i = 0, j = 0;
                a[0].next.Add(b[0].id);
                while (i < a.Count - 1 || j < b.Count - 1)
                {
                    // 往前推进 a 或 b：谁的下一个更靠左先推谁
                    bool stepA = j >= b.Count - 1 || (i < a.Count - 1 && a[i + 1].x <= b[j + 1].x);
                    if (stepA) i++; else j++;
                    if (!a[i].next.Contains(b[j].id)) a[i].next.Add(b[j].id);
                }
                // 偶尔加一条斜向的岔路（相邻、不交叉：只在 a[i] 连着 b[j] 且 a[i+1] 也连着 b[j] 时，给 a[i] 加 b[j+1] 会交叉，所以只加“同向扩展”）
            }
            // 类型
            foreach (var n in nodes)
            {
                if (n.layer == Layers - 1) { n.kind = NodeKind.Boss; continue; }
                if (n.layer == 0) continue;
                double r = rng.NextDouble();
                if (n.layer >= 2 && r < 0.18) n.kind = NodeKind.Elite;
                else if (r < 0.26) n.kind = NodeKind.Armory;
                else if (n.layer >= 1 && r < 0.33) n.kind = NodeKind.Ruins;
            }
            // 中段（第 4 层）和 Boss 前一层（第 7 层）各保证一个补给站
            foreach (int L in new[] { 4, Layers - 2 })
            {
                var row = byLayer[L];
                if (row.All(n => n.kind != NodeKind.Supply)) row[rng.Next(row.Count)].kind = NodeKind.Supply;
            }
            // Boss 前一层不要精英（留口气）
            foreach (var n in byLayer[Layers - 2]) if (n.kind == NodeKind.Elite) n.kind = NodeKind.Battle;
            return nodes;
        }
    }

    /// <summary>存档：Application.persistentDataPath/campaign.json。每个节点前存一次。</summary>
    public static class CampaignStore
    {
        static string PathOf => System.IO.Path.Combine(Application.persistentDataPath, "campaign.json");
        public static bool Exists => System.IO.File.Exists(PathOf);
        public static void Save(RunState r) { try { System.IO.File.WriteAllText(PathOf, JsonUtility.ToJson(r)); } catch (Exception e) { Debug.LogWarning("[Campaign] 存档失败 " + e.Message); } }
        public static RunState Load()
        {
            try { return Exists ? JsonUtility.FromJson<RunState>(System.IO.File.ReadAllText(PathOf)) : null; }
            catch (Exception e) { Debug.LogWarning("[Campaign] 读档失败 " + e.Message); return null; }
        }
        public static void Clear() { try { if (Exists) System.IO.File.Delete(PathOf); } catch { } }
    }
}
