using UnityEngine;

namespace WarTable.XR
{
    /// <summary>
    /// 沙盘在真实桌面上的摆放：手掌平按桌面 1 秒 → 托盘落在桌上（近边自动让开身体，玩家一侧朝向玩家）；
    /// 双手捏住托盘两边 → 拖动 / 旋转 / 缩放（0.5×–2×），高度锁在桌面。参数照原型 MR 版。
    /// 编辑器里（没有头显）什么都不做，沙盘留在原地。
    /// </summary>
    public class TrayRig : MonoBehaviour
    {
        public MRInput input;
        public Transform board;
        /// <summary>主光：跟着沙盘的朝向转（光一直从玩家身后左上方来）。</summary>
        public Transform sun;
        Quaternion sunLocal;
        /// <summary>1× 时托盘外沿半宽（米，含木框）：沙盘本地 (内沿 + 框宽) × BoardScale。</summary>
        public float halfX = 0.7475f, halfZ = 0.632f;
        /// <summary>沙盘根节点在桌面上方多高（米，1× 时）：地台基面到桌面 4.5 cm × BoardScale。</summary>
        public float lift = 0.052f;

        public bool Placed { get; private set; }
        public bool Grabbing { get; private set; }
        /// <summary>允许按手掌摆放（WarGame 只在“摆沙盘”阶段打开：封面时、战斗中手掌搁在桌上都不会误触）。</summary>
        public bool Armed;
        public TrayPose Pose { get; private set; }
        /// <summary>按手掌的进度 0–1（界面层画进度环）和手掌位置。</summary>
        public float PressProgress { get; private set; }
        public Vector3? PressCenter { get; private set; }

        /// <summary>把手离手的捏点多近算“捏住了”（米）。</summary>
        public const float HandleReach = 0.07f;
        /// <summary>
        /// 11-10 用户：开局先强制摆好沙盘，把手要好操作。调整阶段（EasyMove）：把手捏取范围放大到 EasyReach，
        /// 单手捏住任意一个把手就能拖（沙盘平移、自动转向正对玩家）；双手各捏一个照旧旋转 / 缩放。
        /// 开战后关掉，回到“双手同时捏才动”，免得打仗时碰到木框把沙盘拖走。
        /// </summary>
        public bool EasyMove;
        public const float EasyReach = 0.1f;
        float Reach => EasyMove ? EasyReach : HandleReach;
        int oneHand = -1; Vector3 oneStart; TrayPose oneFrom; readonly bool[] pinchWas = new bool[2];
        bool hotL, hotR;

        /// <summary>把手的世界位置：0 = 左，1 = 右。11-06 用户：捏合位置要在眼前，不是在沙盘两侧 →
        /// 两个把手并排放在靠玩家一侧木框的正中（玩家正前方、伸手就到），左右各离中线 20 cm（本地，随沙盘缩放；两手间距约肩宽，太近时转动 / 缩放过于灵敏）。
        /// 沙盘本地：玩家一侧是 +z（TrayRules.Place 让本地 +z 朝向玩家），玩家面朝 −z，左手边是 +x。</summary>
        public Vector3 Handle(int side)
        {
            float hz = BoardMap.HalfZ * BoardMap.Yard + 0.05f;
            return board.TransformPoint(new Vector3(side == 0 ? 0.2f : -0.2f, 0.025f, hz));
        }

        /// <summary>这一侧的手正靠在把手上（界面层把把手提亮）。</summary>
        public bool HandleHot(int side) => side == 0 ? hotL : hotR;

        /// <summary>一个捏点是否在某个把手上（WarGame 据此不把这一捏交给战场）。</summary>
        public bool OnHandle(Vector3 p) => Placed && (Vector3.Distance(p, Handle(0)) < Reach || Vector3.Distance(p, Handle(1)) < Reach);

        readonly PalmPress pressL = new(), pressR = new();
        readonly TrayGrab grab = new();
        bool xr;

        bool hadPose;
        /// <summary>
        /// 重新定位（11-14 用户：设置里要有“重新识别沙盘位置”）：忘掉现在的位置，沙盘收起来，等玩家再用手掌按一次桌面。
        /// 玩家之前调好的大小留着（只是换地方）。编辑器里（没有头显）沙盘固定在原地，不做任何事。
        /// </summary>
        public void Unplace()
        {
            if (!xr || !Placed) return;
            hadPose = true; Placed = false; Grabbing = false; EasyMove = false;
            pressL.Reset(); pressR.Reset(); PressProgress = 0; PressCenter = null;
            if (board) board.gameObject.SetActive(false);
        }

        void Start()
        {
            xr = input && input.Active;
            if (sun) sunLocal = Quaternion.Inverse(board.rotation) * sun.rotation;
            if (!xr) { Placed = true; return; }   // 编辑器：沙盘在原地（封面阶段由 WarGame 藏起来）
        }

        void Update()
        {
            if (!xr) return;
            var l = input.Left; var r = input.Right;
            float headY = input.Head.y;

            // 摆放 / 重新摆放：两只手哪只先按满 1 秒算哪只
            if (Armed && !Grabbing)
            {
                pressL.Update(Time.deltaTime, l, headY);
                pressR.Update(Time.deltaTime, r, headY);
                var best = pressL.Progress >= pressR.Progress ? pressL : pressR;
                PressProgress = best.Progress; PressCenter = best.Center;
                if (best.Done && best.Center.HasValue)
                {
                    Pose = TrayRules.PlaceFacing(best.Height, input.Head, input.cam.transform.forward, halfZ, Placed || hadPose ? Pose.scale : TrayRules.FirstScale);
                    Armed = false;
                    Placed = true;
                    pressL.Reset(); pressR.Reset();
                    PressProgress = 0; PressCenter = null;
                    Apply();
                    board.gameObject.SetActive(true);
                }
            }
            if (!Placed) return;

            // 双手各捏住一个把手（11-05 用户：拖动沙盘要左右两个按钮，左右手捏住才能拖，要不经常误触）：
            // 左手捏左把手、右手捏右把手（离把手 7 cm 以内）才开始拖；只捏住一个、或在别处捏都不动沙盘
            hotL = l.tracked && Vector3.Distance(l.pinchPoint, Handle(0)) < Reach;
            hotR = r.tracked && Vector3.Distance(r.pinchPoint, Handle(1)) < Reach;
            if (EasyMove)
            {
                // 调整阶段：哪只手靠在任一把手上（不分左右）
                hotL = l.tracked && (Vector3.Distance(l.pinchPoint, Handle(0)) < Reach || Vector3.Distance(l.pinchPoint, Handle(1)) < Reach);
                hotR = r.tracked && (Vector3.Distance(r.pinchPoint, Handle(0)) < Reach || Vector3.Distance(r.pinchPoint, Handle(1)) < Reach);
            }
            bool both = l.tracked && r.tracked && l.pinch && r.pinch;
            bool twoNow = Grabbing && oneHand < 0;
            if (both && !twoNow && hotL && hotR)
            {
                Grabbing = true; oneHand = -1;
                grab.Begin(l.pinchPoint, r.pinchPoint, Pose);
            }
            else if (twoNow && !both) Grabbing = false;
            else if (EasyMove && !Grabbing)
            {
                // 单手：刚捏下去（这一帧之前没捏）、捏点在把手上
                for (int i = 0; i < 2 && oneHand < 0; i++)
                {
                    var h = i == 0 ? l : r; bool hot = i == 0 ? hotL : hotR;
                    if (h.tracked && h.pinch && !pinchWas[i] && hot) { oneHand = i; oneStart = h.pinchPoint; oneFrom = Pose; Grabbing = true; }
                }
            }
            if (Grabbing && oneHand >= 0)
            {
                var h = oneHand == 0 ? l : r;
                if (!h.tracked || !h.pinch) { Grabbing = false; oneHand = -1; }
                else
                {
                    var d = h.pinchPoint - oneStart; d.y = 0;
                    var pos = oneFrom.pos + d;
                    var toB = pos - input.Head; toB.y = 0;
                    float yaw = toB.sqrMagnitude > 1e-4f ? Mathf.Atan2(-toB.x, -toB.z) * Mathf.Rad2Deg : oneFrom.yaw;   // 始终正对玩家
                    Pose = new TrayPose { pos = pos, yaw = yaw, scale = oneFrom.scale };
                    Apply();
                }
            }
            else if (Grabbing) { Pose = grab.Update(l.pinchPoint, r.pinchPoint); Apply(); }
            if (!Grabbing) oneHand = -1;
            pinchWas[0] = l.tracked && l.pinch; pinchWas[1] = r.tracked && r.pinch;
        }

        void Apply()
        {
            var p = Pose;
            board.SetPositionAndRotation(p.pos + Vector3.up * (lift * p.scale), p.Rot);
            board.localScale = Vector3.one * (BoardMap.BoardScale * p.scale);
            if (sun) sun.rotation = p.Rot * sunLocal;
        }
    }
}
