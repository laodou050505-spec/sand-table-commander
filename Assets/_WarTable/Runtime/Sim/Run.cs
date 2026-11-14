// 自动生成：由 sandtable-xr/src/sim/run.ts 忠实移植
// 模拟工作线程和页面回退共用的定步长循环。
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WarTable.Sim
{
    /// <summary>
    /// RunSteps 的返回值：事件列表 + AI/仿真各自耗时（毫秒）。
    /// </summary>
    public sealed class RunResult
    {
        public List<BattleEvent> Events { get; } = new();
        public double AiMs;
        public double SimMs;
    }

    public static class Run
    {
        /// <summary>
        /// 运行 steps 步（每步 dt 秒），返回全部事件及 AI/仿真耗时。
        /// 玩家输入完整应用于第 0 步；此后重复，但清除一次性字段
        /// （Attack、Kick、Swap、Alt、MountToggle、Bash）。
        /// Input 是值类型（struct），RunSteps 会在开头自行拷贝一份，
        /// 不会修改调用者传入的原始对象。
        /// </summary>
        public static RunResult RunSteps(Battle b, AI brain, Agent? p,
            int steps, double dt, Input? input)
        {
            var result = new RunResult();

            // 深拷贝 input（调用者可能直接传入 Agent 的 Input 字段，清除前不改原值）
            // Input 是值类型（struct），Aim 也改为值元组，直接赋值即为完整拷贝。
            Input? inp = input.HasValue ? input.Value : (Input?)null;

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < steps; i++)
            {
                // 每帧开始前清除玩家上一帧残留的攻击（防止自动连击）
                if (p is { Alive: true }) p.Input.Attack = null;

                // ── AI 更新 ───────────────────────────────────────────
                long aiStart = sw.ElapsedTicks;
                brain.Update(dt);
                long aiEnd = sw.ElapsedTicks;

                // ── 写入玩家输入 ──────────────────────────────────────
                if (p is { Alive: true } && inp.HasValue)
                {
                    // Input 是值类型，直接赋值即完整拷贝（Aim 也是值元组）。
                    p.Input = inp.Value;
                    // 从第 1 步起清除一次性字段（它们在第 0 步已生效）
                    if (i > 0)
                    {
                        p.Input.Attack      = null;
                        p.Input.Kick        = false;
                        p.Input.Swap        = null;
                        p.Input.Alt         = false;
                        p.Input.MountToggle = false;
                        p.Input.Bash        = false;
                    }
                }

                // ── 仿真推进 ──────────────────────────────────────────
                long simStart = sw.ElapsedTicks;
                b.Step(dt);
                long simEnd = sw.ElapsedTicks;

                result.AiMs  += (aiEnd  - aiStart)  * 1000.0 / Stopwatch.Frequency;
                result.SimMs += (simEnd - simStart)  * 1000.0 / Stopwatch.Frequency;

                // 收集本帧事件
                foreach (var e in b.Events) result.Events.Add(e);
            }

            return result;
        }
    }
}
