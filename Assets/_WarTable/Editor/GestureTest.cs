using System.Text;
using UnityEngine;
using WarTable.XR;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 指挥手势的合成数据测试：坐姿、头在 (0, 1.2, 0) 朝 −Z，手在身前桌面上方。
    /// 每种手势都要能认出来；慢动作、姿势不对、捏合中、放在桌上不动这些反例都不能误触。改阈值后跑 GestureTest.Run()。
    /// </summary>
    public static class GestureTest
    {
        static readonly Vector3 Head = new(0, 1.2f, 0), Fwd = Vector3.back;
        const float Hz = 72f;

        static HandState Hand(Vector3 p, int extended, Vector3 normal, bool pinch = false, float curl = -1f)
            => new() { tracked = true, palm = p, extended = extended, palmNormal = normal, pinch = pinch, curl = curl > 0f ? curl : extended == 0 ? 1.05f : extended >= 3 ? 1.9f : 1.4f };

        /// <summary>左右手各一条轨迹（null = 没追踪到），按 72 Hz 播放，外加 0.5 秒静止收尾；返回识别出的手势（逗号分隔）。</summary>
        static string Play(System.Func<float, HandState?> left, System.Func<float, HandState?> right, float dur, float tableY = float.NaN)
        {
            var g = new GestureRecognizer();
            var got = new StringBuilder();
            for (float t = 0; t < dur + 0.5f; t += 1f / Hz)
            {
                float u = Mathf.Min(t, dur);
                var r = g.Update(t + 10f, Head, Fwd, left(u) ?? default, right(u) ?? default, tableY);
                if (r != Gesture.None) got.Append(got.Length > 0 ? "," : "").Append(r);
            }
            return got.Length > 0 ? got.ToString() : "None";
        }

        static float Ease(float t, float dur) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / dur));

        public static string Run()
        {
            var rest = new Vector3(0.15f, 0.85f, -0.35f);
            var raised = new Vector3(0.15f, 0.97f, -0.35f);
            var down = Vector3.down; var toMe = Vector3.forward;   // 头在 +Z 一侧：掌心朝自己 = +Z
            var cases = new (string name, string want, string got)[]
            {
                ("握拳前推 15cm / 0.25s", "Charge", Play(_ => null, t => Hand(rest + Fwd * 0.15f * Ease(t, 0.25f), 0, Vector3.left), 0.25f)),
                ("双手握拳前推（只认右手 → 冲锋）", "Charge", Play(t => Hand(rest - Vector3.right * 0.3f + Fwd * 0.15f * Ease(t, 0.25f), 0, Vector3.right),
                                                    t => Hand(rest + Fwd * 0.15f * Ease(t - 0.08f, 0.25f), 0, Vector3.left), 0.35f)),
                ("掌心朝下下按 8cm / 0.2s", "Hold", Play(_ => null, t => Hand(rest + Vector3.down * 0.08f * Ease(t, 0.2f), 4, down), 0.2f)),
                ("掌心朝自己往回招 12cm / 0.25s", "Retreat", Play(_ => null, t => Hand(rest - Fwd * 0.12f * Ease(t, 0.25f), 4, toMe), 0.25f)),
                // 反例
                ("握拳慢推 15cm / 1.2s", "None", Play(_ => null, t => Hand(rest + Fwd * 0.15f * Ease(t, 1.2f), 0, Vector3.left), 1.2f)),
                ("张掌前推", "None", Play(_ => null, t => Hand(rest + Fwd * 0.15f * Ease(t, 0.25f), 4, Vector3.left), 0.25f)),
                ("握拳往下砸", "None", Play(_ => null, t => Hand(rest + Vector3.down * 0.12f * Ease(t, 0.2f), 0, down), 0.2f)),
                ("捏合中前推（划线）", "None", Play(_ => null, t => Hand(rest + Fwd * 0.15f * Ease(t, 0.25f), 0, Vector3.left, true), 0.25f)),
                ("手搁桌上微动", "None", Play(_ => null, t => Hand(rest + new Vector3(Mathf.Sin(t * 9) * 0.01f, 0, Mathf.Cos(t * 7) * 0.01f), 4, down), 2f)),
                ("掌心朝下横扫", "None", Play(_ => null, t => Hand(rest + Vector3.right * 0.2f * Ease(t, 0.25f), 4, down), 0.25f)),
                // 11-07 摆姿势停一下（桌面 0.75 m；rest 离桌 10 cm，raised 离桌 22 cm）
                ("握紧拳举在胸前停 0.8s", "Charge", Play(_ => null, t => Hand(raised, 0, Vector3.left), 0.8f, 0.75f)),
                ("手掌朝下平举停 0.8s", "Hold", Play(_ => null, t => Hand(raised, 4, down), 0.8f, 0.75f)),
                ("手掌朝自己停 0.8s", "Retreat", Play(_ => null, t => Hand(raised, 4, toMe), 0.8f, 0.75f)),
                ("双手握拳停住（只认右手 → 冲锋）", "Charge", Play(t => Hand(raised - Vector3.right * 0.3f, 0, Vector3.right), t => Hand(raised, 0, Vector3.left), 0.8f, 0.75f)),
                ("自然放松半握举着 2s", "None", Play(_ => null, t => Hand(raised, 0, Vector3.left, false, 1.38f), 2f, 0.75f)),
                ("手掌搁在桌上 2s", "None", Play(_ => null, t => Hand(new Vector3(0.15f, 0.79f, -0.35f), 4, down), 2f, 0.75f)),
                ("握拳停住中途丢跟踪 0.2s（11-08）", "Charge", Play(_ => null, t => t > 0.3f && t < 0.5f ? default(HandState) : Hand(raised, 0, Vector3.left), 1.0f, 0.75f)),
                ("握拳停住中途手指松一点（11-08）", "Charge", Play(_ => null, t => t > 0.3f && t < 0.5f ? Hand(raised, 1, Vector3.left, curl: 1.25f) : Hand(raised, 0, Vector3.left), 0.8f, 0.75f)),
                ("只有左手握拳停住（11-08 只认右手）", "None", Play(t => Hand(raised - Vector3.right * 0.3f, 0, Vector3.right), _ => null, 0.8f, 0.75f)),
                ("只有左手掌心朝下下按（11-08 只认右手）", "None", Play(t => Hand(rest - Vector3.right * 0.3f + Vector3.down * 0.08f * Ease(t, 0.2f), 4, down), _ => null, 0.2f)),
                ("握拳只停 0.3s", "None", Play(_ => null, t => Hand(t < 0.3f ? raised : raised + Vector3.up * 0.2f, t < 0.3f ? 0 : 2, Vector3.left), 0.6f, 0.75f)),
                ("捏合中举着", "None", Play(_ => null, t => Hand(raised, 0, Vector3.left, true), 1.5f, 0.75f)),
                ("举到眼前握拳前推", "None", Play(_ => null, t => Hand(new Vector3(0.1f, 1.25f, -0.3f) + Fwd * 0.15f * Ease(t, 0.25f), 0, Vector3.left), 0.25f)),
            };
            var sb = new StringBuilder();
            int pass = 0;
            foreach (var c in cases)
            {
                bool ok = c.got == c.want;
                if (ok) pass++;
                sb.AppendLine($"{(ok ? "✓" : "✗")} {c.name}: 期望 {c.want}，得到 {c.got}");
            }
            return $"手势测试 {pass}/{cases.Length}\n" + sb;
        }
    }
}
