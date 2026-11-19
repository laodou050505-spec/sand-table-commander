using System.Collections.Generic;
using UnityEngine;

namespace WarTable.XR
{
    public enum Gesture { None, Charge, Hold, Retreat, AllCharge }

    /// <summary>
    /// 指挥手势（10-31 用户定，代替按钮）：选中部队后
    ///   握拳向前一推 = 冲锋；掌心朝下往下一按 = 坚守；掌心朝自己往怀里一招 = 撤退；双手同时握拳前推 = 全军冲锋。
    /// 只认身前、低于视线的手，动作要够快够远，识别后全局冷却 1 秒；捏合中的手不认（捏合是拇指食指相碰，这里都是握拳 / 张掌）。
    /// 不用举臂：手在桌面上方、胸前高度做就行。纯逻辑（输入是每帧的手和头），编辑器里可以喂合成数据测。
    /// </summary>
    public class GestureRecognizer
    {
        // 阈值（米、秒）：先按《梦境玩具战》真机经验给，上机后按实测调
        public const float Window = 0.35f, Cooldown = 1f, PairWindow = 0.25f;
        // 11-07 用户：右手做指挥手势有时认不出 → 距离 / 速度 / 姿势都放宽一档（冷却 1 秒防连发）
        public const float PushDist = 0.08f, PushSpeed = 0.4f;       // 冲锋：握拳沿头的水平前方推出
        public const float PressDist = 0.045f, PressSpeed = 0.28f;   // 坚守：掌心朝下往下按
        public const float BeckonDist = 0.065f, BeckonSpeed = 0.32f; // 撤退：掌心朝自己往回招
        const float Straight = 0.65f;   // 位移里沿规定方向的比例（斜着晃不算）

        struct Sample { public float t; public Vector3 p; public bool fist, open, palmDown, palmToMe; }
        readonly List<Sample>[] hist = { new(), new() };
        float cooldownUntil = -1f;
        float pendingChargeT = -1f; int pendingHand = -1;
        readonly float[] lastPinch = { -9f, -9f };
        public const float AfterPinch = 0.6f;
        /// <summary>11-08 用户：指挥手势只认右手——左手点托盘布阵时常被误认成手势。true = 左手不参与任何手势（也就没有“双手全军冲锋”，
        /// 没选中部队时右手手势本来就对全军生效）。</summary>
        public bool RightOnly = true;

        // ── 摆姿势停一下（11-07 用户：很多手势识别很费劲）：不用做快速动作，把手举在胸前、摆好姿势停 HoldTime 秒就算。
        //    手要离桌面至少 RaiseMin（搁在桌上的手不算），姿势要摆得很明确（握紧拳 / 手掌摊平），界面层在手边画进度圈，
        //    玩家看得到“正在识别什么、还差多少”，不想要就松开。同一个姿势触发后要先换姿势才能再触发。
        public const float HoldTime = 0.6f, RaiseMin = 0.1f, HoldStill = 0.5f;
        readonly Gesture[] holdPose = new Gesture[2];
        readonly float[] holdStart = new float[2];
        readonly bool[] holdUsed = new bool[2];
        readonly Vector3[] holdLast = new Vector3[2];
        readonly float[] holdLastT = { -9f, -9f };
        readonly float[] holdMiss = { -1f, -1f };
        /// <summary>姿势中途走样 / 手短暂丢跟踪的宽限：这段时间内进度冻结不清零（11-08 真机：握拳时追踪抖一下，进度就从头来）</summary>
        public const float HoldGrace = 0.3f;
        /// <summary>这一帧每只手正在“停”的姿势和进度（0–1），界面层画进度圈；没有就是 None。</summary>
        public (Gesture pose, float progress)[] Holding { get; } = new (Gesture, float)[2];

        static Gesture PoseOf(in HandState h, Vector3 head)
        {
            var toMe = head - h.palm; toMe.y = 0;
            if (h.extended == 0 && h.curl > 0f && h.curl < 1.18f) return Gesture.Charge;                    // 握紧拳
            if (h.extended >= 3 && h.palmNormal.y < -0.75f) return Gesture.Hold;                          // 手掌朝下摊平
            if (h.extended >= 3 && toMe.sqrMagnitude > 1e-4f && Vector3.Dot(h.palmNormal, toMe.normalized) > 0.6f) return Gesture.Retreat;   // 手掌朝自己
            return Gesture.None;
        }

        /// <summary>已经在停的姿势，继续的条件放宽一点（迟滞），免得在阈值边上来回跳、进度老清零。</summary>
        static bool Keeps(in HandState h, Vector3 head, Gesture pose)
        {
            if (!h.tracked || h.pinch) return false;
            var toMe = head - h.palm; toMe.y = 0;
            switch (pose)
            {
                case Gesture.Charge: return h.extended <= 1 && h.curl > 0f && h.curl < 1.3f;
                case Gesture.Hold: return h.extended >= 2 && h.palmNormal.y < -0.6f;
                case Gesture.Retreat: return h.extended >= 2 && toMe.sqrMagnitude > 1e-4f && Vector3.Dot(h.palmNormal, toMe.normalized) > 0.45f;
            }
            return false;
        }

        /// <summary>每帧调一次。hands：左、右；tableY = 桌面高度（NaN = 不认“摆姿势停一下”）；返回这一帧识别出的手势（大多数帧是 None）。</summary>
        public Gesture Update(float now, Vector3 head, Vector3 headFwd, HandState left, HandState right, float tableY = float.NaN)
        {
            var held = HoldUpdate(now, head, headFwd, left, right, tableY);
            if (held != Gesture.None)
            {
                cooldownUntil = now + Cooldown; pendingHand = -1;
                hist[0].Clear(); hist[1].Clear();
                return held;
            }
            return MotionUpdate(now, head, headFwd, left, right);
        }

        Gesture HoldUpdate(float now, Vector3 head, Vector3 headFwd, HandState left, HandState right, float tableY)
        {
            Holding[0] = Holding[1] = (Gesture.None, 0f);
            if (float.IsNaN(tableY)) return Gesture.None;
            var fwd = new Vector3(headFwd.x, 0, headFwd.z); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            Gesture fire = Gesture.None;
            for (int i = RightOnly ? 1 : 0; i < 2; i++)
            {
                var h = i == 0 ? left : right;
                if (h.tracked && h.pinch) lastPinch[i] = now;
                bool zone = h.tracked && !h.pinch && now - lastPinch[i] > AfterPinch
                            && Vector3.Dot(h.palm - head, fwd) > 0.1f && h.palm.y < head.y - 0.05f && h.palm.y > tableY + RaiseMin;
                var pose = zone ? PoseOf(h, head) : Gesture.None;
                float dtH = now - holdLastT[i];
                float speed = h.tracked && dtH > 1e-4f && dtH < 0.2f ? (h.palm - holdLast[i]).magnitude / dtH : 0f;
                if (h.tracked) { holdLast[i] = h.palm; }
                holdLastT[i] = now;
                var cur = holdPose[i];
                if (cur != Gesture.None && pose != cur && !holdUsed[i])
                {
                    bool loose = (!h.tracked || zone || (h.palm.y > tableY + RaiseMin * 0.5f && h.palm.y < head.y)) && Keeps(h, head, cur);
                    if (loose) { pose = cur; holdMiss[i] = -1f; }   // 姿势稍松：照常累计
                    else
                    {
                        if (holdMiss[i] < 0f) holdMiss[i] = now;
                        if (now - holdMiss[i] < HoldGrace && pose == Gesture.None)   // 短暂走样 / 丢跟踪：冻结进度
                        {
                            holdStart[i] += Mathf.Clamp(dtH, 0f, 0.2f);
                            Holding[i] = (cur, Mathf.Clamp01((now - holdStart[i]) / HoldTime));
                            continue;
                        }
                    }
                }
                else holdMiss[i] = -1f;
                if (pose != holdPose[i]) { holdPose[i] = pose; holdStart[i] = now; holdUsed[i] = false; }
                else if (speed > HoldStill && pose != Gesture.None) holdStart[i] = Mathf.Max(holdStart[i], now - HoldTime * 0.5f);   // 手在快速移动：进度退回一半（推 / 按的动作交给动作识别）
                if (pose == Gesture.None || holdUsed[i]) continue;
                float k = Mathf.Clamp01((now - holdStart[i]) / HoldTime);
                Holding[i] = (pose, k);
                if (k >= 1f && now >= cooldownUntil) { holdUsed[i] = true; fire = pose; }
            }
            // 两只手都握拳停住 = 全军冲锋
            if (fire == Gesture.Charge && Holding[0].pose == Gesture.Charge && Holding[1].pose == Gesture.Charge
                && Holding[0].progress > 0.5f && Holding[1].progress > 0.5f) { holdUsed[0] = holdUsed[1] = true; fire = Gesture.AllCharge; }
            return fire;
        }

        Gesture MotionUpdate(float now, Vector3 head, Vector3 headFwd, HandState left, HandState right)
        {
            var fwd = new Vector3(headFwd.x, 0, headFwd.z);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Gesture g = Gesture.None;
            for (int i = RightOnly ? 1 : 0; i < 2; i++)
            {
                var h = i == 0 ? left : right;
                var list = hist[i];
                if (h.tracked && h.pinch) lastPinch[i] = now;
                bool ok = h.tracked && !h.pinch && now - lastPinch[i] > AfterPinch && Vector3.Dot(h.palm - head, fwd) > 0.12f && h.palm.y < head.y - 0.1f;
                if (!ok) { list.Clear(); continue; }
                var toMe = head - h.palm;
                list.Add(new Sample
                {
                    t = now, p = h.palm,
                    fist = h.extended <= 1,          // 握拳：允许一根手指没握死
                    open = h.extended >= 2,
                    palmDown = h.palmNormal.y < -0.55f,
                    palmToMe = Vector3.Dot(h.palmNormal, new Vector3(toMe.x, 0, toMe.z).normalized) > 0.35f,
                });
                while (list.Count > 0 && now - list[0].t > Window) list.RemoveAt(0);
                if (now < cooldownUntil) continue;
                var d = Detect(list, fwd);
                if (d == Gesture.Charge && RightOnly) g = d;   // 只认右手：不用等另一只手配对
                else if (d == Gesture.Charge)
                {
                    // 另一只手刚推过（或马上要推）：合成全军冲锋
                    if (pendingHand >= 0 && pendingHand != i && now - pendingChargeT <= PairWindow) { pendingHand = -1; g = Gesture.AllCharge; }
                    else if (pendingHand < 0) { pendingHand = i; pendingChargeT = now; list.Clear(); }
                }
                else if (d != Gesture.None) g = d;
            }
            // 单手冲锋要等一小会儿，确认另一只手没跟上才发
            if (g == Gesture.None && pendingHand >= 0 && now - pendingChargeT > PairWindow) { pendingHand = -1; g = Gesture.Charge; }
            if (g != Gesture.None)
            {
                cooldownUntil = now + Cooldown;
                pendingHand = -1;
                hist[0].Clear(); hist[1].Clear();
            }
            return g;
        }

        static Gesture Detect(List<Sample> s, Vector3 fwd)
        {
            if (s.Count < 3) return Gesture.None;
            var last = s[^1];
            // 窗口里从最早一个“姿势对”的样本量到现在：位移、峰值速度、方向
            Gesture Try(System.Func<Sample, bool> pose, System.Func<Vector3, float> along, float dist, float speed, Gesture kind)
            {
                int i0 = s.Count - 1;
                while (i0 > 0 && pose(s[i0 - 1])) i0--;
                if (i0 > s.Count - 3 || !pose(last)) return Gesture.None;
                var disp = last.p - s[i0].p;
                float a = along(disp);
                if (a < dist || a < disp.magnitude * Straight) return Gesture.None;
                float peak = 0;
                for (int k = i0 + 1; k < s.Count; k++)
                {
                    float dt = s[k].t - s[k - 1].t;
                    if (dt > 1e-4f) peak = Mathf.Max(peak, along(s[k].p - s[k - 1].p) / dt);
                }
                return peak >= speed ? kind : Gesture.None;
            }
            var g = Try(x => x.fist, v => Vector3.Dot(v, fwd), PushDist, PushSpeed, Gesture.Charge);
            if (g != Gesture.None) return g;
            g = Try(x => x.open && x.palmDown, v => -v.y, PressDist, PressSpeed, Gesture.Hold);
            if (g != Gesture.None) return g;
            return Try(x => x.open && x.palmToMe, v => -Vector3.Dot(v, fwd), BeckonDist, BeckonSpeed, Gesture.Retreat);
        }
    }
}
