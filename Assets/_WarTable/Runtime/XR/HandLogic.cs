using UnityEngine;

namespace WarTable.XR
{
    /// <summary>一只手这一帧（世界坐标，米）。没追踪到时 tracked = false，其余字段无意义。</summary>
    public struct HandState
    {
        public bool tracked;
        /// <summary>捏合（系统判定优先，指尖距离带回差兜底）。</summary>
        public bool pinch;
        /// <summary>拇指尖和食指尖的中点：捏住的那一点。</summary>
        public Vector3 pinchPoint;
        public Vector3 palm, palmNormal;
        /// <summary>食指尖：手够得着的按钮直接用它按（Poke）。</summary>
        public Vector3 indexTip;
        /// <summary>四指平均伸展度：指尖到腕 / 指根到腕。握紧拳 ≈ 1.0–1.15，自然放松半握 ≈ 1.3–1.5，张开 ≈ 1.8–2.0。</summary>
        public float curl;
        /// <summary>伸直的手指数（四指）。</summary>
        public int extended;
        /// <summary>所有关节里最低点（已减关节半径），用来估桌面高度。</summary>
        public float lowY;
    }

    /// <summary>托盘（沙盘）在真实空间的位姿：pos 是托盘底面中心（y = 桌面高度），yaw 绕 y（度），scale 是玩家缩放倍数。</summary>
    public struct TrayPose
    {
        public Vector3 pos; public float yaw, scale;
        public Quaternion Rot => Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>
    /// 摆托盘 / 抓托盘的手部逻辑（照原型 src/mr/handLogic.ts，参数不变）。纯逻辑，不碰场景。
    /// </summary>
    public static class TrayRules
    {
        /// <summary>玩家缩放范围：0.5×（整盘约 75 cm）到 2×（俯看细节）。</summary>
        public const float MinScale = 0.5f, MaxScale = 2f;
        /// <summary>托盘近边离头的最小水平距离：别让托盘戳进肚子。</summary>
        public const float NearGap = 0.22f;
        /// <summary>
        /// 11-09 别人试玩：一摆下来沙盘太大、贴在脸前，看不到东西。第一次摆放：缩到 0.8×（整盘约 1.2 × 1.0 m），
        /// 近边离头水平 PlaceGap，正对玩家视线方向（不再跟手掌偏到一边）；手掌只用来定桌面高度。之后双手捏圆扣可以挪 / 缩放。
        /// </summary>
        public const float FirstScale = 0.8f, PlaceGap = 0.3f;

        /// <summary>摆放（11-09 版）：托盘中心在头的水平正前方，近边离头 PlaceGap，本地 +z 朝向玩家。</summary>
        public static TrayPose PlaceFacing(float tableY, Vector3 head, Vector3 headFwd, float halfZ, float scale)
        {
            var f = new Vector3(headFwd.x, 0f, headFwd.z);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            float r = halfZ * scale + PlaceGap;
            return new TrayPose { pos = new Vector3(head.x + f.x * r, tableY, head.z + f.z * r), yaw = Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, scale = scale };
        }
        /// <summary>双手捏合算“抓托盘”的范围：托盘外扩多少、托盘面上方多高以内。</summary>
        public const float GrabMargin = 0.15f, GrabHeight = 0.25f, RimInside = 0.1f;   // 木框外 15 cm 到框内 10 cm、框上 25 cm 以内才算捏住托盘

        /// <summary>手掌定桌面后托盘放哪：以手掌为中心，近边至少离头 NearGap；托盘玩家一侧（本地 +z）转向玩家。</summary>
        public static TrayPose Place(Vector3 palm, float tableY, Vector3 head, float halfZ, float scale = 1f)
        {
            float dx = palm.x - head.x, dz = palm.z - head.z, d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 1e-3f) { dx = 0f; dz = -1f; d = 1f; }
            float ux = dx / d, uz = dz / d, r = Mathf.Max(d, halfZ * scale + NearGap);
            return new TrayPose { pos = new Vector3(head.x + ux * r, tableY, head.z + uz * r), yaw = Mathf.Atan2(-ux, -uz) * Mathf.Rad2Deg, scale = scale };
        }

        /// <summary>一个点是否落在托盘抓取范围里（halfX / halfZ：1× 时托盘外沿半宽，米）。</summary>
        public static bool InGrabZone(Vector3 p, TrayPose pose, float halfX, float halfZ)
        {
            var l = Quaternion.Inverse(pose.Rot) * (p - pose.pos);
            float hx = halfX * pose.scale, hz = halfZ * pose.scale;
            bool box = Mathf.Abs(l.x) <= hx + GrabMargin && Mathf.Abs(l.z) <= hz + GrabMargin && l.y > -GrabMargin && l.y < GrabHeight;
            float inside = Mathf.Min(hx - Mathf.Abs(l.x), hz - Mathf.Abs(l.z));   // 离最近一条边往里多远（负 = 在框外）
            return box && inside < RimInside;
        }
    }

    /// <summary>“手掌平按桌面 1 秒”的检测器（原型 PalmPress）。Progress 到 1 时 Done，Height 是估出的桌面高度。</summary>
    public class PalmPress
    {
        const float Hold = 1f, Down = 0.75f, Still = 0.08f, Grace = 0.15f, BelowHead = 0.2f;
        public float Progress, Height;
        public bool Done;
        public Vector3? Center;
        Vector3? last;
        float bad;
        readonly System.Collections.Generic.List<float> lows = new();

        public void Reset() { Progress = 0; Done = false; Center = null; last = null; bad = 0; lows.Clear(); }

        public void Update(float dt, HandState h, float headY)
        {
            if (Done) return;
            bool ok = false;
            if (h.tracked)
            {
                float speed = last.HasValue && dt > 0 ? Vector3.Distance(h.palm, last.Value) / dt : 0f;
                last = h.palm;
                ok = h.palmNormal.y < -Down && h.extended >= 3 && speed < Still && h.palm.y < headY - BelowHead;
                if (ok) { Center = h.palm; lows.Add(h.lowY); }
            }
            else last = null;
            if (ok)
            {
                bad = 0;
                Progress = Mathf.Min(1f, Progress + dt / Hold);
                if (Progress >= 1f)
                {
                    lows.Sort();   // 取中位数：追踪跳变的单帧不影响桌面高度
                    Height = lows[lows.Count / 2];
                    Done = true;
                }
            }
            else if ((bad += dt) > Grace) { Progress = 0; Center = null; lows.Clear(); }
        }
    }

    /// <summary>
    /// 双手抓托盘（原型 TrayGrab）：两手中点带着托盘平移，两手连线转动带着托盘转，两手间距带着托盘缩放。
    /// 抓住的那一点始终跟手；高度锁在桌面上（只有重新按手掌才改高度）。
    /// </summary>
    public class TrayGrab
    {
        Vector2 mid; float angle, span; TrayPose from;

        static float Ang(Vector3 l, Vector3 r) => Mathf.Atan2(r.x - l.x, r.z - l.z);
        static float Span(Vector3 l, Vector3 r) => Mathf.Max(0.03f, new Vector2(r.x - l.x, r.z - l.z).magnitude);

        public void Begin(Vector3 l, Vector3 r, TrayPose pose)
        {
            mid = new Vector2((l.x + r.x) / 2, (l.z + r.z) / 2);
            angle = Ang(l, r); span = Span(l, r); from = pose;
        }

        public TrayPose Update(Vector3 l, Vector3 r)
        {
            float scale = Mathf.Clamp(from.scale * Span(l, r) / span, TrayRules.MinScale, TrayRules.MaxScale);
            float turn = Mathf.DeltaAngle(0f, (Ang(l, r) - angle) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float k = scale / from.scale, ox = (from.pos.x - mid.x) * k, oz = (from.pos.z - mid.y) * k;
            float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
            return new TrayPose
            {
                pos = new Vector3((l.x + r.x) / 2 + ox * c + oz * s, from.pos.y, (l.z + r.z) / 2 - ox * s + oz * c),
                yaw = from.yaw + turn * Mathf.Rad2Deg,
                scale = scale,
            };
        }
    }
}
