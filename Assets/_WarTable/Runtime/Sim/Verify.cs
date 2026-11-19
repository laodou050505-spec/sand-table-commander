// Verify.cs — 模拟层核验工具
// 用于对比 C# 移植与 TypeScript 原版的行为是否一致。
// 不引用 UnityEngine，可在 Editor cs.py 外从任何上下文调用。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WarTable.Sim
{
    /// <summary>
    /// 核验工具：以固定参数运行战斗模拟，输出 JSON 快照文件，
    /// 与 TypeScript 原型跑相同种子的输出比对。
    /// </summary>
    public static class Verify
    {
        // ── 默认运行参数（对应 MR 开局约定）────────────────────────────────
        private const int    MaxSteps    = 3600;   // 最多 3600 步 = 60 秒
        private const int    StepChunk   = 60;     // 每次运行 60 步再采样
        private const double Dt          = 1.0 / 60.0;
        private const double Distance    = 34.0;
        private const double FieldCap    = double.PositiveInfinity;
        private const string Map         = "open";
        private const string Size        = "xs";

        // ── 公有 API ────────────────────────────────────────────────────────

        /// <summary>
        /// 跑一场种子为 <paramref name="seed"/> 的战斗，采样后写入 <paramref name="outPath"/>。
        /// 返回写入的 JSON 字符串（便于 cs.py 用 result.Log 打印）。
        /// </summary>
        public static string Dump(int seed, string outPath)
        {
            var json = DumpJson(seed);
            File.WriteAllText(outPath, json);
            return json;
        }

        /// <summary>同 <see cref="Dump"/> 但仅返回 JSON，不写文件。</summary>
        public static string DumpJson(int seed)
        {
            // ── 1. 初始化战局 ──────────────────────────────────────────────
            var (battle, _) = Scenario.SetupBattle(
                Scenario.DefaultArmies, null, seed, Distance, FieldCap, Map, Size);

            // 跳过部署阶段（deploying=false 已在 Scenario 里根据 plan 是否存在决定，
            // open/xs 无预设方案时 SetupBattle 不会设 deploying=true）
            battle.Deploying = false;

            var ai = new AI(battle);

            // ── 2. 初始指令：双方全体 charge（与 TS 验证脚本保持一致）──────
            var allGroups = new List<string> { Groups.Inf, Groups.Arch, Groups.Cav, Groups.Ha };
            ai.SetOrder(0, allGroups, Orders.Charge, 0, 0, 0);
            ai.SetOrder(1, allGroups, Orders.Charge, 0, 0, 0);

            // ── 3. 步进并采样 ──────────────────────────────────────────────
            var snapshots = new List<SnapshotEntry>();
            int doneSteps = 0;
            while (doneSteps < MaxSteps && !battle.Winner.HasValue)
            {
                Run.RunSteps(battle, ai, null, StepChunk, Dt, null);
                doneSteps += StepChunk;
                snapshots.Add(Snapshot(battle, doneSteps));
            }

            // ── 4. 序列化 JSON ─────────────────────────────────────────────
            return Serialize(seed, battle, snapshots);
        }

        // ── 内部结构体 ──────────────────────────────────────────────────────

        private struct AgentEntry
        {
            public int    Id, Team;
            public double X, Z, Hp;
            public bool   Alive, Routed;
            public string Act;
        }

        private struct SnapshotEntry
        {
            public int    Step;
            public double Time;
            public int?   Winner;
            public List<AgentEntry> Agents;
        }

        private static SnapshotEntry Snapshot(Battle battle, int step)
        {
            var agents = battle.Agents.Select(a => new AgentEntry
            {
                Id     = a.Id,
                Team   = a.Team,
                X      = a.X,
                Z      = a.Z,
                Hp     = a.Hp,
                Alive  = a.Alive,
                Routed = a.Routed,
                Act    = a.Action?.Kind ?? "dead"
            }).ToList();

            return new SnapshotEntry
            {
                Step    = step,
                Time    = battle.Time,
                Winner  = battle.Winner,
                Agents  = agents
            };
        }

        // ── 简单手写 JSON 序列化器 ──────────────────────────────────────────
        // 避免 System.Text.Json（.NET 5+）和 Newtonsoft.Json 依赖。

        private static string Serialize(int seed, Battle battle, List<SnapshotEntry> snaps)
        {
            var sb = new StringBuilder(snaps.Count * 2000);
            sb.Append("{\n");
            sb.Append($"  \"seed\": {seed},\n");
            sb.Append($"  \"winner\": {JsonNullable(battle.Winner)},\n");
            sb.Append($"  \"endTime\": {battle.Time:F4},\n");
            sb.Append($"  \"snapCount\": {snaps.Count},\n");
            sb.Append("  \"snapshots\": [\n");

            for (int si = 0; si < snaps.Count; si++)
            {
                var s = snaps[si];
                sb.Append("    {\n");
                sb.Append($"      \"step\": {s.Step},\n");
                sb.Append($"      \"time\": {s.Time:F4},\n");
                sb.Append($"      \"winner\": {JsonNullable(s.Winner)},\n");
                sb.Append("      \"agents\": [\n");
                for (int ai = 0; ai < s.Agents.Count; ai++)
                {
                    var a = s.Agents[ai];
                    sb.Append("        {");
                    sb.Append($"\"id\": {a.Id}, ");
                    sb.Append($"\"team\": {a.Team}, ");
                    sb.Append($"\"x\": {a.X:F4}, ");
                    sb.Append($"\"z\": {a.Z:F4}, ");
                    sb.Append($"\"hp\": {a.Hp:F3}, ");
                    sb.Append($"\"alive\": {JsonBool(a.Alive)}, ");
                    sb.Append($"\"routed\": {JsonBool(a.Routed)}, ");
                    sb.Append($"\"act\": \"{a.Act}\"");
                    sb.Append("}");
                    if (ai < s.Agents.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }
                sb.Append("      ]\n");
                sb.Append("    }");
                if (si < snaps.Count - 1) sb.Append(",");
                sb.Append("\n");
            }

            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string JsonNullable(int? v) => v.HasValue ? v.Value.ToString() : "null";
        private static string JsonBool(bool v)     => v ? "true" : "false";
    }
}
