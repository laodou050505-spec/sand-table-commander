using System.Collections.Generic;
using System.Linq;
using WarTable.Campaign;
using WarTable.Sim;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 战役自动试玩（不出画面）：生成一轮，用“原地待敌、接战再反冲”这套中等打法一路打到 Boss，
    /// 每场打输了就重打（最多 3 次），看兵力消耗、胜率和星晶是不是落在设计曲线上（Docs/玩法版设计_v1.md 第 8 节）。
    /// 用法：cs.py 'result.Log(WarTable.EditorTools.CampaignTest.Run(1));'
    /// </summary>
    public static class CampaignTest
    {
        static (double x, double z)? Cent(Battle b, string g)
        {
            var a = b.Alive(0).Where(x => Groups.GroupOf(x.Troop.Role) == g).ToList();
            if (a.Count == 0) return null;
            return (a.Average(x => x.X), a.Average(x => x.Z));
        }
        static bool Contact(Battle b, double d)
        {
            var me = b.Alive(0); var foe = b.Alive(1);
            foreach (var a in me) foreach (var f in foe) if ((a.X - f.X) * (a.X - f.X) + (a.Z - f.Z) * (a.Z - f.Z) < d * d) return true;
            return false;
        }

        /// <summary>打一仗（自动），返回 (赢了吗, 各兵种活着几个, 用时)。</summary>
        public static (bool won, Dictionary<string, int> alive, double t) Fight(RunState run, MapNode node, List<TroopCount> muster, int attempt)
        {
            var me = CampaignRules.PlayerArmy(muster); var foe = CampaignRules.EnemyArmy(run, node);
            var terrain = new BoardTerrain(40);
            var plan = Deploy.DefaultPlan((me, foe), terrain, 34);
            var (b, _) = Scenario.SetupBattle((me, foe), null, node.seed + attempt * 977, 34, double.PositiveInfinity, "open", "xs", plan, terrain);
            CampaignRules.ApplyBonuses(b, run);
            b.Deploying = false;
            var ai = new AI(b); double t = 0; bool held = false, charged = false;
            var all = Groups.All.ToList();
            while (!b.Winner.HasValue && t < 240)
            {
                if (!held) { held = true; foreach (var g in all) { var c = Cent(b, g); if (c != null) ai.SetOrder(0, new List<string> { g }, Orders.Hold, c.Value.x, c.Value.z, 0); } }
                if (!charged && Contact(b, 6)) { charged = true; ai.SetOrder(0, all, Orders.Charge, 0, 0, 0); }
                WarTable.Sim.Run.RunSteps(b, ai, null, 30, 1.0 / 60, null); t += 0.5;
            }
            var alive = new Dictionary<string, int>();
            foreach (var a in b.Agents) if (a.Team == 0 && a.Alive) alive[a.Troop.Id] = (alive.TryGetValue(a.Troop.Id, out var n) ? n : 0) + 1;
            return (b.Winner == 0, alive, t);
        }

        /// <summary>跑 n 轮，只看每轮结果一行：走到第几层、打了几场、重打几次、首战胜率。</summary>
        public static string Many(int n, int from = 1)
        {
            var sb = new System.Text.StringBuilder(); int clears = 0;
            for (int sd = from; sd < from + n; sd++)
            {
                var lines = Run(sd).Split('\n').Where(l => l.Length > 0).ToList();
                var fights = lines.Where(l => l.Contains("试 ")).ToList();
                int first = fights.Count(l => l.Contains("试 1 次")); int retries = fights.Sum(l => int.Parse(l.Split(new[] { "试 " }, System.StringSplitOptions.None)[1].Split(' ')[0]) - 1);
                bool clear = lines.Last().Contains("通关"); if (clear) clears++;
                var lost = lines.FirstOrDefault(l => l.Contains("仍败"));
                sb.AppendLine($"种子 {sd}：{(clear ? "通关" : "失败于 " + (lost != null ? lost.Trim().Split('：')[0] : "?"))}，胜 {fights.Count} 场（一次过 {first}，共重打 {retries} 次） {lines.Last().Split('，').Skip(2).FirstOrDefault()}");
            }
            sb.AppendLine($"通关 {clears}/{n}");
            return sb.ToString();
        }

        public static string Run(int seed, int maxRetry = 3)
        {
            var sb = new System.Text.StringBuilder();
            var run = CampaignRules.NewRun(seed);
            var rng = new System.Random(seed);
            sb.AppendLine($"战役种子 {seed}：{run.nodes.Count} 个节点，" + string.Join(" ", Enumerable.Range(0, MapGen.Layers).Select(L => string.Join("", run.nodes.Where(n => n.layer == L).Select(n => n.kind.ToString()[0])))));
            while (!run.over)
            {
                var opts = run.Reachable().ToList();
                if (opts.Count == 0) break;
                var node = opts[rng.Next(opts.Count)];
                if (node.kind == NodeKind.Supply)
                {
                    while (run.wounded.Count > 0 && CampaignRules.Heal(run, run.wounded[0].id)) { }
                    if (run.crystals >= CampaignRules.CapCost) CampaignRules.RaiseCap(run);
                    var cheap = run.unlocked.OrderBy(id => run.Get(run.roster, id)).FirstOrDefault();
                    while (cheap != null && run.Total(run.roster) < run.cap && CampaignRules.Recruit(run, cheap)) { }
                    node.done = true; run.at = node.id;
                    sb.AppendLine($"  第{node.layer}层 补给站：兵 {run.Total(run.roster)}，伤 {run.Total(run.wounded)}，统率 {run.cap}，星晶 {run.crystals}");
                    continue;
                }
                if (node.kind == NodeKind.Ruins)
                {
                    var dl = CampaignRules.DilemmaOf(run, node);
                    bool take = dl.ok && rng.NextDouble() < 0.7;   // 七成会接受
                    if (take) CampaignRules.Accept(run, dl);
                    node.done = true; run.at = node.id;
                    sb.AppendLine($"  第{node.layer}层 遗迹：{dl.kind} {(take ? "接受" : "离开")}（{dl.troop}{dl.relic} 付 {dl.cost} 得 {dl.gain}）");
                    continue;
                }
                if (node.kind == NodeKind.Armory)
                {
                    var r = CampaignRules.Rewards(run, node).FirstOrDefault(x => x.kind == RewardKind.Relic) ?? CampaignRules.Rewards(run, node)[0];
                    CampaignRules.Apply(run, r); node.done = true; run.at = node.id;
                    sb.AppendLine($"  第{node.layer}层 军械库：拿 {r.kind} {r.relic}{r.troop}");
                    continue;
                }
                var muster = CampaignRules.DefaultMuster(run);
                int foeN = CampaignRules.EnemyArmy(run, node).Values.Sum();
                CampaignRules.Outcome o = default; int tries = 0; double tt = 0;
                for (; tries <= maxRetry; tries++)
                {
                    var (won, alive, t) = Fight(run, node, muster, tries);
                    tt = t;
                    o = CampaignRules.Resolve(run, node, muster, alive, won, tries == 0);
                    if (won) break;
                }
                if (!o.won) { sb.AppendLine($"  第{node.layer}层 {node.kind}：敌 {foeN}，我 {o.sent}，重打 {maxRetry} 次仍败 → 战役失败"); run.over = true; break; }
                CampaignRules.Commit(run, node, muster, o);
                var rw = CampaignRules.Rewards(run, node);
                var pickR = rw[rng.Next(rw.Count)];
                CampaignRules.Apply(run, pickR);
                sb.AppendLine($"  第{node.layer}层 {node.kind}：敌 {foeN} 我 {o.sent} → 活 {o.alive}（{o.stars} 星，{tt:0} 秒，试 {tries + 1} 次）阵亡 {o.dead} 伤 {o.wounded}；剩兵 {run.Total(run.roster)} 伤兵 {run.Total(run.wounded)} 星晶 {run.crystals}；奖励 {pickR.kind} {pickR.troop}{pickR.relic}");
            }
            sb.AppendLine($"结果：{(run.won ? "通关" : "失败")}，打了 {run.battles} 场，纹章 {string.Join(",", run.relics)}，晋升 {string.Join(",", run.tiers.Select(t => t.id + t.n))}");
            return sb.ToString();
        }
    }
}
