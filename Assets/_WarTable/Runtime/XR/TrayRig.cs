using UnityEngine;

namespace WarTable.XR
{
    /// <summary>
    /// 沙盘在真实空间里的摆放。
    /// 11-16 用户（别人试玩不会玩；有人坐着有人站着；沙盘要能上下调；要有放得很大的模式）：
    /// - 不要求“手掌平按桌面”：开局沙盘直接出现在玩家正前方、眼睛下方 <see cref="TrayRules.EyeDrop"/>（坐着约是桌面高度，站着约在腰上）。
    /// - 调整阶段（EasyMove）：单手捏住任意一个把手就能拖，连高度一起走（像端着一个托盘），自动正对玩家；双手各捏一个可转 / 缩放 / 升降。
    /// - 开战后：双手各捏住一个把手才动（免得打仗时碰到边框把沙盘拖走），同样可以升降。
    ///
    /// 11-17 用户：放大改成**真人比例**——兵和人一样大（整个场景等比放大），不要天空盒，就在真实房间里。
    /// - 模拟层的单位就是码，所以真人比例 = 1 码对应 0.9144 米（<see cref="LifeScale"/>）；整个战场约 110 × 91 米。
    /// - 人走不了那么远 → 位置用指挥台上的小战场图来调：点图上哪里，整个战场就平滑地滑过去，让那个位置到你脚下（<see cref="StandAt"/>）。
    ///   真实房间一直看得见（透视开着），战场相对房间滑动不像全虚拟环境那样容易晕。
    /// - 战场有起伏：脚下的地面始终贴着真实地板（<see cref="FloorY"/>），人在房间里走动时高度跟着缓缓变。
    /// - 放大模式下不能用手抓着拖 / 缩放（比例是定死的 1:1）。
    /// 编辑器里（没有头显、没开假手）什么都不做，沙盘留在原地。
    /// </summary>
    public class TrayRig : MonoBehaviour
    {
        public MRInput input;
        public Transform board;
        /// <summary>主光：跟着沙盘的朝向转（光一直从玩家身后左上方来）。</summary>
        public Transform sun;
        Quaternion sunLocal;
        /// <summary>1× 时托盘外沿半宽（米，含边框）：沙盘本地 (内沿 + 框宽) × BoardScale。</summary>
        public float halfX = 0.7475f, halfZ = 0.632f;
        /// <summary>沙盘根节点在托盘底面上方多高（米，1× 时）：地台基面到桌面 4.5 cm × BoardScale。</summary>
        public float lift = 0.052f;

        public bool Placed { get; private set; }
        public bool Grabbing { get; private set; }
        /// <summary>允许摆出沙盘（WarGame 只在“摆沙盘”阶段打开）。</summary>
        public bool Armed;
        public TrayPose Pose { get; private set; }
        /// <summary>旧的“按手掌”进度（不用手掌了，恒为 0 / null；界面层的接口先留着）。</summary>
        public float PressProgress => 0f;
        public Vector3? PressCenter => null;

        /// <summary>把手离手的捏点多近算“捏住了”（米）。</summary>
        public const float HandleReach = 0.07f;
        /// <summary>调整阶段把手的捏取范围放大，单手就能拖。</summary>
        public bool EasyMove;
        public const float EasyReach = 0.1f;
        float Reach => EasyMove ? EasyReach : HandleReach;
        int oneHand = -1; Vector3 oneStart; TrayPose oneFrom; readonly bool[] pinchWas = new bool[2];
        bool hotL, hotR;

        /// <summary>放大模式（真人比例，战场铺在地板上）。</summary>
        public bool Immersive { get; private set; }
        /// <summary>真人比例时的放大倍数：1 码（沙盘本地 1 cm × BoardScale）对应 0.9144 米。</summary>
        public static float LifeScale => 0.9144f / (BoardMap.Yard * BoardMap.BoardScale);
        /// <summary>这一帧别动沙盘（手里正拖着部队时由 WarGame 设）。</summary>
        public bool Blocked;
        TrayPose tablePose;
        float armT;
        // 放大模式：战场正滑向新的位置（slideFrom → slideTo，0.6 秒，先快后慢）
        Vector3 slideFrom, slideTo; float slideT = -1f; const float SlideDur = 0.6f;
        /// <summary>战场正在滑动（界面层这时不接受下一次点图）。</summary>
        public bool Sliding => slideT >= 0f;

        /// <summary>把手的世界位置：0 = 左，1 = 右。靠玩家一侧边框的正中并排两个，左右各离中线 20 cm（本地，随沙盘缩放）。
        /// 沙盘本地：玩家一侧是 +z，玩家面朝 −z，左手边是 +x。</summary>
        public Vector3 Handle(int side)
        {
            float hz = BoardMap.HalfZ * BoardMap.Yard + 0.05f;
            return board.TransformPoint(new Vector3(side == 0 ? 0.2f : -0.2f, 0.025f, hz));
        }

        /// <summary>这一侧的手正靠在把手上（界面层把把手提亮）。</summary>
        public bool HandleHot(int side) => side == 0 ? hotL : hotR;

        /// <summary>一个捏点是否在某个把手上（WarGame 据此不把这一捏交给战场）。</summary>
        public bool OnHandle(Vector3 p) => Placed && !Immersive && (Vector3.Distance(p, Handle(0)) < Reach || Vector3.Distance(p, Handle(1)) < Reach);

        readonly TrayGrab grab = new();
        bool xr => input && input.Active;
        bool hadPose, started;

        /// <summary>
        /// 重新定位（设置里）：忘掉现在的位置，沙盘收起来，下一局开头重新出现在玩家正前方（玩家可能换了座位 / 站起来了）。
        /// 之前调好的大小留着。编辑器里沙盘固定在原地，不做任何事。
        /// </summary>
        public void Unplace()
        {
            if (!xr || !Placed) return;
            if (Immersive) { Immersive = false; Pose = tablePose; slideT = -1f; }
            hadPose = true; Placed = false; Grabbing = false; EasyMove = false; oneHand = -1; armT = 0f;
            if (board) board.gameObject.SetActive(false);
        }

        void Start()
        {
            started = true;
            if (sun) sunLocal = Quaternion.Inverse(board.rotation) * sun.rotation;
            if (!xr) { Placed = true; return; }   // 编辑器：沙盘在原地（封面阶段由 WarGame 藏起来）
        }

        /// <summary>真实地板的高度（世界 y）：追踪原点设成了地面就是 0；没设成（少数情况）按头的高度估。</summary>
        public float FloorY() => MRRig.FloorSet || MRInput.Sim ? (input.origin ? input.origin.position.y : 0f) : input.Head.y - 1.4f;

        /// <summary>模拟层的一点（码）在沙盘本地的位置（米，沙盘缩放之前）：x 取反，高度取地形。</summary>
        static Vector3 LocalOf(double x, double z) => new Vector3(-(float)x, BoardMap.Height(-(float)x, (float)z), (float)z) * BoardMap.Yard;

        /// <summary>放大模式下，让模拟层的 (x, z) 这一点正好在玩家脚下（真实地板上）时，沙盘该在哪。</summary>
        Vector3 PosFor(double x, double z, float yaw, float scale)
        {
            var h = input.Head;
            var local = LocalOf(x, z) * (BoardMap.BoardScale * scale);
            return new Vector3(h.x, FloorY(), h.z) - Quaternion.Euler(0f, yaw, 0f) * local;
        }

        /// <summary>玩家现在站在模拟层的哪一点（码）。</summary>
        public (double x, double z) StandingOn()
        {
            var lp = board.InverseTransformPoint(input.Head) / BoardMap.Yard;
            return (-lp.x, lp.z);
        }

        /// <summary>
        /// 放大模式：站到战场的另一处去（小战场图上点的地方）。整个战场 0.6 秒平滑滑过去；点在战场外面会夹回边界里。
        /// </summary>
        public void StandAt(double x, double z)
        {
            if (!Immersive) return;
            x = System.Math.Clamp(x, -BoardMap.HalfX + 2, BoardMap.HalfX - 2); z = System.Math.Clamp(z, -BoardMap.HalfZ + 2, BoardMap.HalfZ - 2);
            slideFrom = Pose.pos; slideTo = PosFor(x, z, Pose.yaw, Pose.scale); slideT = 0f;
            Debug.Log($"[WT-IN] stand-at {x:0},{z:0}");
        }

        /// <summary>
        /// 进 / 出放大模式。进：记下桌面上的位置，沙盘放大到真人比例铺在地板上，玩家站在己方阵后（底线往里 12 码），面朝敌军；
        /// 出：回到原来桌面上的位置和大小。返回是否切换成功。
        /// </summary>
        public bool SetImmersive(bool on)
        {
            if (!xr || !Placed || on == Immersive) return false;
            Grabbing = false; oneHand = -1; slideT = -1f;
            if (on)
            {
                tablePose = Pose;
                var f = input.cam.transform.forward; f.y = 0; f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
                float yaw = Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, s = LifeScale;
                Immersive = true;
                Pose = new TrayPose { pos = PosFor(0, BoardMap.HalfZ - 12, yaw, s), yaw = yaw, scale = s };
            }
            else { Immersive = false; Pose = tablePose; }
            Apply();
            Debug.Log($"[WT-IN] immersive {(on ? "on" : "off")} scale={Pose.scale:0.0} y={Pose.pos.y:0.00} head={input.Head.y:0.00} floorSet={MRRig.FloorSet}");
            return true;
        }

        void Update()
        {
            if (!started || !xr) return;
            var l = input.Left; var r = input.Right;

            // 摆出沙盘：等头的位置稳定一下（刚戴上 / 刚从封面过来）就直接出现在正前方，不用玩家做任何手势
            if (Armed && !Placed)
            {
                armT += Time.deltaTime;
                if (armT > 0.4f)
                {
                    float y = input.Head.y - TrayRules.EyeDrop;
                    Pose = TrayRules.PlaceFacing(y, input.Head, input.cam.transform.forward, halfZ, hadPose ? Pose.scale : TrayRules.FirstScale);
                    Armed = false; Placed = true; hadPose = true; armT = 0f;
                    Apply();
                    board.gameObject.SetActive(true);
                    Debug.Log($"[WT-IN] tray-auto y={y:0.00} head={input.Head.y:0.00} scale={Pose.scale:0.00}");
                }
            }
            else armT = 0f;
            if (!Placed) return;

            if (Immersive)
            {
                hotL = hotR = false; Grabbing = false;
                var p = Pose;
                if (slideT >= 0f)
                {
                    slideT += Time.deltaTime;
                    float u = Mathf.Clamp01(slideT / SlideDur); u = 1f - (1f - u) * (1f - u) * (1f - u);   // 先快后慢
                    p.pos = Vector3.Lerp(slideFrom, slideTo, u);
                    if (slideT >= SlideDur) slideT = -1f;
                }
                else
                {
                    // 脚下的地面贴着真实地板：人在房间里走动，战场的高度跟着地形缓缓变（每秒追 3 倍差距，不会一跳一跳）
                    var (sx, sz) = StandingOn();
                    float want = PosFor(sx, sz, p.yaw, p.scale).y;
                    p.pos.y = Mathf.Lerp(p.pos.y, want, 1f - Mathf.Exp(-Time.deltaTime * 3f));
                }
                Pose = p; Apply();
                pinchWas[0] = l.tracked && l.pinch; pinchWas[1] = r.tracked && r.pinch;
                return;
            }

            bool both = l.tracked && r.tracked && l.pinch && r.pinch;
            // 桌面：左手捏左把手、右手捏右把手才开始拖 / 转 / 缩放 / 升降；只捏住一个、或在别处捏都不动沙盘
            hotL = l.tracked && Vector3.Distance(l.pinchPoint, Handle(0)) < Reach;
            hotR = r.tracked && Vector3.Distance(r.pinchPoint, Handle(1)) < Reach;
            if (EasyMove)
            {
                // 调整阶段：哪只手靠在任一把手上（不分左右）
                hotL = l.tracked && (Vector3.Distance(l.pinchPoint, Handle(0)) < Reach || Vector3.Distance(l.pinchPoint, Handle(1)) < Reach);
                hotR = r.tracked && (Vector3.Distance(r.pinchPoint, Handle(0)) < Reach || Vector3.Distance(r.pinchPoint, Handle(1)) < Reach);
            }
            bool twoNow = Grabbing && oneHand < 0;
            if (both && !twoNow && hotL && hotR && !Blocked)
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
                    // 高度也跟着手走（端着托盘挪）；最高到眼睛下方 15 cm，最低到眼睛下方 1.5 m（差不多是地板）
                    var pos = oneFrom.pos + (h.pinchPoint - oneStart);
                    pos.y = Mathf.Clamp(pos.y, input.Head.y - 1.5f, input.Head.y - 0.15f);
                    var toB = pos - input.Head; toB.y = 0;
                    float yaw = toB.sqrMagnitude > 1e-4f ? Mathf.Atan2(-toB.x, -toB.z) * Mathf.Rad2Deg : oneFrom.yaw;   // 始终正对玩家
                    Pose = new TrayPose { pos = pos, yaw = yaw, scale = oneFrom.scale };
                    Apply();
                }
            }
            else if (Grabbing)
            {
                var p = grab.Update(l.pinchPoint, r.pinchPoint);
                p.pos.y = Mathf.Clamp(p.pos.y, input.Head.y - 1.5f, input.Head.y - 0.15f);
                Pose = p; Apply();
            }
            if (!Grabbing) oneHand = -1;
            pinchWas[0] = l.tracked && l.pinch; pinchWas[1] = r.tracked && r.pinch;
        }

        void Apply()
        {
            var p = Pose;
            // 放大模式没有托盘底座：沙盘地面直接贴着地板
            board.SetPositionAndRotation(p.pos + Vector3.up * (Immersive ? 0f : lift * p.scale), p.Rot);
            board.localScale = Vector3.one * (BoardMap.BoardScale * p.scale);
            if (sun) sun.rotation = p.Rot * sunLocal;
        }
    }
}
