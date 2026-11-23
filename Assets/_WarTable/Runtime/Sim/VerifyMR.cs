// VerifyMR.cs — 按 MR 实际玩法核验：敌军完全交给 AI 指挥官，我军按脚本下各种军令（坚守 / 阵型 / 移动 / 间距 / 十步 / 冲锋）。
// 与 sandtable-xr/tmp/verify_mr.ts 逐步对称；输出格式同 Verify（可用 /tmp/compare_verify.py 比较）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WarTable.Sim
{
    public static class VerifyMR
    {
        static readonly List<string> All = new() { Groups.Inf, Groups.Arch, Groups.Cav, Groups.Ha };

        static (double x, double z, int n) Cent(Battle b, int team, string g)
        {
            double x = 0, z = 0; int n = 0;
            foreach (var a in b.Alive(team)) { if (g != null && Groups.GroupOf(a.Troop.Role) != g) continue; x += a.X; z += a.Z; n++; }
            return n == 0 ? (0, 0, 0) : (x / n, z / n, n);
        }

        /// <summary>MR 实际流程：默认布阵方案 → 布阵阶段搬方块 replan → 开战（就地坚守）→ 下令。与 sandtable-xr/tmp/verify_plan.ts 对称。</summary>
        public static string DumpPlan(int seed, string outPath)
        {
            var plan = Deploy.DefaultPlan((Scenario.DefaultArmies.Team0, Scenario.DefaultArmies.Team1), Scenario.MakeTerrain("open", seed, "xs"), 34, double.PositiveInfinity);
            var (battle, _) = Scenario.SetupBattle(Scenario.DefaultArmies, null, seed, 34, double.PositiveInfinity, "open", "xs", plan);
            battle.Deploying = true;
            var ai = new AI(battle);
            Run.RunSteps(battle, ai, null, 60, 1.0 / 60.0, null);
            foreach (var b in plan.Blocks)
            {
                if (b.Team != 0) continue;
                var g = Deploy.BlockGroup(b);
                if (g == Groups.Arch) b.Z -= 6;
                if (g == Groups.Cav) b.X += 8;
                Deploy.ClampBlock(battle.Terrain, b);
            }
            Scenario.Replan(battle, plan.Blocks, null);
            Run.RunSteps(battle, ai, null, 60, 1.0 / 60.0, null);
            battle.Deploying = false;
            {
                var e = Cent(battle, 1, null);
                foreach (var g in All) { var c = Cent(battle, 0, g); if (c.n > 0) ai.MoveTo(0, new List<string> { g }, c.x, c.z, Math.Atan2(-(e.x - c.x), -(e.z - c.z))); }
            }
            int step = 0; bool contact = false;
            var sb = new StringBuilder("{\n  \"seed\": " + seed + ",\n  \"snapshots\": [\n");
            bool first = true;
            while (step < 5400 && !battle.Winner.HasValue)
            {
                if (step == 240) ai.SetArrangement(0, new List<string> { Groups.Inf }, Arrangement.Shieldwall);
                if (!contact && battle.Alive(1).Any(a => a.Z > -2)) { contact = true; ai.SetOrder(0, new List<string> { Groups.Cav, Groups.Ha }, Orders.Charge, 0, 0, 0); }
                if (step == 1800) ai.SetOrder(0, All, Orders.Charge, 0, 0, 0);
                Run.RunSteps(battle, ai, null, 60, 1.0 / 60.0, null);
                step += 60;
                if (!first) sb.Append(",\n");
                first = false;
                AppendSnap(sb, battle, step);
            }
            sb.Append("\n  ],\n  \"winner\": ").Append(battle.Winner.HasValue ? battle.Winner.Value.ToString() : "null")
              .Append(",\n  \"endTime\": ").Append(battle.Time.ToString("F4")).Append(",\n  \"snapCount\": ").Append(step / 60).Append("\n}\n");
            File.WriteAllText(outPath, sb.ToString());
            return $"seed {seed} winner {(battle.Winner.HasValue ? battle.Winner.Value.ToString() : "无")} time {battle.Time:F2} agents {battle.Agents.Count}";
        }

        static void AppendSnap(StringBuilder sb, Battle battle, int step)
        {
            sb.Append("    {\"step\": ").Append(step).Append(", \"time\": ").Append(battle.Time.ToString("F4")).Append(", \"agents\": [");
            bool f2 = true;
            foreach (var a in battle.Agents)
            {
                if (!f2) sb.Append(", ");
                f2 = false;
                sb.Append("{\"id\": ").Append(a.Id).Append(", \"team\": ").Append(a.Team)
                  .Append(", \"x\": ").Append(a.X.ToString("F4")).Append(", \"z\": ").Append(a.Z.ToString("F4"))
                  .Append(", \"hp\": ").Append(a.Hp.ToString("F3")).Append(", \"alive\": ").Append(a.Alive ? "true" : "false")
                  .Append(", \"routed\": ").Append(a.Routed ? "true" : "false").Append(", \"act\": \"").Append(a.Action?.Kind ?? "dead").Append("\"}");
            }
            sb.Append("]}");
        }

        public static string Dump(int seed, string outPath)
        {
            var (battle, _) = Scenario.SetupBattle(Scenario.DefaultArmies, null, seed, 34, double.PositiveInfinity, "open", "xs");
            battle.Deploying = false;
            var ai = new AI(battle);
            var sb = new StringBuilder();
            sb.Append("{\n  \"seed\": ").Append(seed).Append(",\n  \"snapshots\": [\n");
            int step = 0; bool contact = false; bool first = true;
            while (step < 5400 && !battle.Winner.HasValue)
            {
                if (step == 180)
                {
                    var e = Cent(battle, 1, null);
                    foreach (var g in All)
                    {
                        var c = Cent(battle, 0, g);
                        if (c.n > 0) ai.MoveTo(0, new List<string> { g }, c.x, c.z, Math.Atan2(-(e.x - c.x), -(e.z - c.z)));
                    }
                    ai.SetArrangement(0, new List<string> { Groups.Inf }, Arrangement.Shieldwall);
                }
                if (step == 360) { ai.MoveTo(0, new List<string> { Groups.Cav }, 30, 6, 0); ai.SetSpacing(0, new List<string> { Groups.Arch }, 1.25); }
                if (!contact && battle.Alive(1).Any(a => a.Z > -2)) { contact = true; ai.SetOrder(0, new List<string> { Groups.Cav, Groups.Ha }, Orders.Charge, 0, 0, 0); }
                if (step == 1200) ai.Move10(0, new List<string> { Groups.Arch }, -10);
                if (step == 2400) ai.SetOrder(0, All, Orders.Charge, 0, 0, 0);
                Run.RunSteps(battle, ai, null, 60, 1.0 / 60.0, null);
                step += 60;
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {\"step\": ").Append(step).Append(", \"time\": ").Append(battle.Time.ToString("F4")).Append(", \"agents\": [");
                bool f2 = true;
                foreach (var a in battle.Agents)
                {
                    if (!f2) sb.Append(", ");
                    f2 = false;
                    sb.Append("{\"id\": ").Append(a.Id).Append(", \"team\": ").Append(a.Team)
                      .Append(", \"x\": ").Append(a.X.ToString("F4")).Append(", \"z\": ").Append(a.Z.ToString("F4"))
                      .Append(", \"hp\": ").Append(a.Hp.ToString("F3")).Append(", \"alive\": ").Append(a.Alive ? "true" : "false")
                      .Append(", \"routed\": ").Append(a.Routed ? "true" : "false").Append(", \"act\": \"").Append(a.Action?.Kind ?? "dead").Append("\"}");
                }
                sb.Append("]}");
            }
            sb.Append("\n  ],\n  \"winner\": ").Append(battle.Winner.HasValue ? battle.Winner.Value.ToString() : "null")
              .Append(",\n  \"endTime\": ").Append(battle.Time.ToString("F4")).Append(",\n  \"snapCount\": ").Append(step / 60).Append("\n}\n");
            File.WriteAllText(outPath, sb.ToString());
            return $"seed {seed} winner {(battle.Winner.HasValue ? battle.Winner.Value.ToString() : "无")} time {battle.Time:F2}";
        }
    }
}
