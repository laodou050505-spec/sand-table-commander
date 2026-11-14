using System;
using System.Collections.Generic;
using System.Linq;
using WarTable.Sim;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 难度测试（照原型 sandtable-xr/tmp/bal.ts）：五种打法各打 N 局，看打法是否真的决定胜负。
    /// board = true 用沙盘真实地形（游戏里实际的），false 用原型生成的丘陵（和原型的结论对照，检查测试本身没写错）。
    /// </summary>
    public static class BalanceTest
    {
        static readonly List<string> All = new() { Groups.Inf, Groups.Arch, Groups.Cav, Groups.Ha };
        static List<string> L(params string[] g) => g.ToList();

        static (double x, double z)? Cent(Battle b, string g)
        {
            var m = b.Alive(0).Where(a => Groups.GroupOf(a.Troop.Role) == g).ToList();
            if (m.Count == 0) return null;
            return (m.Average(a => a.X), m.Average(a => a.Z));
        }

        static bool Contact(Battle b, double d)
        {
            foreach (var a in b.Alive(0).Where(a => Groups.GroupOf(a.Troop.Role) == Groups.Inf))
                foreach (var e in b.Alive(1)) if (Math.Sqrt((a.X - e.X) * (a.X - e.X) + (a.Z - e.Z) * (a.Z - e.Z)) < d) return true;
            return false;
        }

        delegate void Bot(Battle b, AI ai, double t, Dictionary<string, bool> st);

        static readonly (string name, Bot bot)[] Bots =
        {
            ("挂机（一直坚守）", (b, ai, t, st) => { if (st.ContainsKey("d")) return; st["d"] = true; foreach (var g in All) { var c = Cent(b, g); if (c != null) ai.MoveTo(0, L(g), c.Value.x, c.Value.z, 0); } }),
            ("无脑全军冲锋", (b, ai, t, st) => { if (st.ContainsKey("d")) return; st["d"] = true; ai.SetOrder(0, All, Orders.Charge, 0, 0, 0); }),
            ("原地待敌，接战再反冲", (b, ai, t, st) =>
            {
                if (!st.ContainsKey("a")) { st["a"] = true; foreach (var g in All) { var c = Cent(b, g); if (c != null) ai.SetOrder(0, L(g), Orders.Hold, c.Value.x, c.Value.z, 0); } }
                if (Contact(b, 6) && !st.ContainsKey("b")) { st["b"] = true; ai.SetOrder(0, All, Orders.Charge, 0, 0, 0); }
            }),
            ("步弓待敌 + 骑兵绕侧翼", (b, ai, t, st) =>
            {
                if (!st.ContainsKey("a")) { st["a"] = true; foreach (var g in new[] { Groups.Inf, Groups.Arch }) { var c = Cent(b, g); if (c != null) ai.SetOrder(0, L(g), Orders.Hold, c.Value.x, c.Value.z, 0); } ai.MoveTo(0, L(Groups.Cav, Groups.Ha), 34, -4, 0); }
                if (Contact(b, 8) && !st.ContainsKey("b")) { st["b"] = true; ai.SetOrder(0, L(Groups.Inf, Groups.Arch), Orders.Charge, 0, 0, 0); ai.SetOrder(0, L(Groups.Cav, Groups.Ha), Orders.Charge, 0, 0, 0); }
            }),
            ("步弓先冲，骑兵延后 8 秒", (b, ai, t, st) =>
            {
                if (!st.ContainsKey("a")) { st["a"] = true; ai.SetOrder(0, L(Groups.Inf, Groups.Arch, Groups.Ha), Orders.Charge, 0, 0, 0); var c = Cent(b, Groups.Cav); if (c != null) ai.SetOrder(0, L(Groups.Cav), Orders.Hold, c.Value.x, c.Value.z, 0); }
                if (t > 8 && !st.ContainsKey("b")) { st["b"] = true; ai.SetOrder(0, L(Groups.Cav), Orders.Charge, 0, 0, 0); }
            }),
        };

        /// <summary>
        /// 选地形：用原型 Terrain.open(地形种子) 的丘陵替换沙盘战场里的丘陵（布景带的坡保留），跑五种打法。
        /// 返回一行：每种打法胜几局。
        /// </summary>
        public static string EvalTerrainSeed(int terrainSeed, int n = 10, int[] only = null)
        {
            var saved = WarTable.BoardMap.Hills;
            var proto = Terrain.Open(terrainSeed, 40);
            var bands = saved.Where(h => System.Math.Abs(h.x) > 45 || System.Math.Abs(h.y) > 42).ToList();
            WarTable.BoardMap.Hills = proto.Hills.Select(h => new UnityEngine.Vector4(-(float)h.X, (float)h.Z, (float)h.R, (float)h.H)).Concat(bands).ToArray();
            try
            {
                var lines = Run(true, n, only).Split('\n').Where(l => l.Length > 0).Select(l => l.Split(new[] { "胜 " }, StringSplitOptions.None)[1].Split(' ')[0]);
                var hs = string.Join(" ", proto.Hills.Select(h => $"({h.X:0},{h.Z:0},r{h.R:0},h{h.H:0.0})"));
                return $"地形种子 {terrainSeed}: " + string.Join(" | ", lines) + "   丘陵 " + hs;
            }
            finally { WarTable.BoardMap.Hills = saved; }
        }

        public static string Run(bool board, int n = 10, int[] only = null)
        {
            var sb = new System.Text.StringBuilder();
            for (int bi = 0; bi < Bots.Length; bi++)
            {
                if (only != null && !only.Contains(bi)) continue;
                var (name, bot) = Bots[bi];
                int w = 0; double T = 0, al = 0;
                for (int seed = 1; seed <= n; seed++)
                {
                    Terrain terrain = board ? new BoardTerrain(40) : Scenario.MakeTerrain("open", seed, "xs");
                    var plan = Deploy.DefaultPlan((Scenario.DefaultArmies.Team0, Scenario.DefaultArmies.Team1), board ? terrain : Scenario.MakeTerrain("open", seed, "xs"), 34);
                    var (battle, _) = Scenario.SetupBattle(Scenario.DefaultArmies, null, seed, 34, double.PositiveInfinity, "open", "xs", plan, board ? terrain : null);
                    battle.Deploying = false;
                    var ai = new AI(battle); var st = new Dictionary<string, bool>(); double t = 0;
                    while (!battle.Winner.HasValue && t < 240) { bot(battle, ai, t, st); WarTable.Sim.Run.RunSteps(battle, ai, null, 30, 1.0 / 60, null); t += 0.5; }
                    if (battle.Winner == 0) w++;
                    T += t; al += battle.Alive(0).Count;
                }
                sb.AppendLine($"{(board ? "沙盘地形" : "原型地形")} {name}: 胜 {w}/{n}  平均 {T / n:0}s  我方存活 {al / n:0}");
            }
            return sb.ToString();
        }
    }
}
