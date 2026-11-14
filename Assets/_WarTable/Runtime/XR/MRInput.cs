using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

namespace WarTable.XR
{
    /// <summary>
    /// 头显输入：PICO 手部追踪 + 眼动（照《天底》PicoInput，真机验证过的读法）。眼动拿不到时退回头部朝向。
    /// 只在头显上工作（Active）；编辑器里 WarGame 用鼠标试玩。新机型的接口差异都收在这一个文件里。
    /// </summary>
    public class MRInput : MonoBehaviour
    {
        public Camera cam;
        /// <summary>XR 原点（追踪空间 → 世界）。</summary>
        public Transform origin;
        [Tooltip("眼动平滑（秒）：眼跳很快，稍微平滑一点不抖，又不至于拖")] public float gazeSmoothing = 0.035f;
        // 11-06 用户：老是识别不到捏 → 指尖距离阈值放宽（2.5 → 3.2 cm 开、4.5 → 5.5 cm 松），
        // 系统判定只要报“食指捏合”就算（不再要求射线有效），丢帧 0.15 秒内保持上一帧的手（追踪偶尔跳一帧不会断掉捏合）
        // 11-06 真机诊断（logcat [WT-IN]）：系统捏合标记从来不报（sys=False），全靠指尖距离；真捏时右手读数 2.1–3.2 cm，正卡在旧阈值 3.2 cm 上
        // → 开 4.0 cm、松 5.5 cm（松开的手读数 5.1–6.6 cm）
        const float PinchOn = 0.04f, PinchOff = 0.055f, LostGrace = 0.15f, StaleAfter = 3f;
        readonly float[] pinchT0 = new float[2], anchorT = new float[2];
        readonly Vector3[] anchorP = new Vector3[2];
        readonly bool[] stale = new bool[2];
        readonly float[] lostT = { -9f, -9f };
        HandState lastL, lastR;

        public bool Active => XRSettings.isDeviceActive && cam;
        public bool EyeTracked { get; private set; }
        public Ray Gaze { get; private set; }
        public Vector3 Head => cam.transform.position;
        /// <summary>拇指尖到食指尖的距离（米），0 = 左手 1 = 右手；手指上的捏合提示圈用（11-09）。</summary>
        public float TipGap(int side) => tipDist[side];
        public HandState Left, Right;
        /// <summary>系统给的手部射线（从手指向前），没有就是 null。视线没对上目标时拿它兜底瞄准。</summary>
        public Ray? AimLeft, AimRight;
        /// <summary>这一帧刚捏下去（从松到捏）。11-06 真机：闲着的那只手指头自然蜷着会一直判成捏，
        /// 界面不能用 Left.pinch || Right.pinch 的上升沿（被那只手一直顶着永远不会再有上升沿），要用每只手各自的按下沿。</summary>
        public bool PinchDownL { get; private set; }
        public bool PinchDownR { get; private set; }
        public bool PinchDown => PinchDownL || PinchDownR;
        bool prevL, prevR;
        /// <summary>诊断：当前激活的输入设备（手 / 手柄）、本帧系统是否报捏合、指尖距离。</summary>
        public string Diag { get; private set; } = "";
        readonly float[] tipDist = { 9f, 9f }; readonly bool[] sysPinch = new bool[2];

        HandAimState aim;
        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
        EyeTrackingData eye = new() { eyeDatas = new PerEyeData[(int)PerEyeUsage.EyeCount] };
        bool eyeStarted, eyeTried;
        readonly bool[] pinch = new bool[2];
        Vector3 gazeDir;
        bool diagWasL, diagWasR; float diagT;

        void OnDisable()
        {
            if (eyeStarted) { var stop = new EyeTrackingStopInfo(); PXR_MotionTracking.StopEyeTracking(ref stop); eyeStarted = false; eyeTried = false; }
        }

        Vector3 World(Vector3 p) => origin ? origin.TransformPoint(p) : p;
        Quaternion WorldRot(Quaternion q) => origin ? origin.rotation * q : q;

        bool ReadHand(HandType type, int side, out HandState h)
        {
            h = default;
            if (!PXR_HandTracking.GetJointLocations(type, ref locs) || locs.isActive == 0 || locs.jointLocations == null) return false;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            // 11-06：只要求腕、拇指尖、食指尖有效（捏合时其余手指常被挡住、个别关节掉成无效，以前一掉整只手就丢、捏不上）
            bool Ok(HandJoint j) => ((ulong)locs.jointLocations[(int)j].locationStatus & need) != 0;
            bool all = Ok(HandJoint.JointWrist) && Ok(HandJoint.JointThumbTip) && Ok(HandJoint.JointIndexTip);
            Vector3 P(HandJoint j) => World(locs.jointLocations[(int)j].pose.Position.ToVector3());
            var wrist = P(HandJoint.JointWrist); var thumb = P(HandJoint.JointThumbTip);
            var tips = new[] { P(HandJoint.JointIndexTip), P(HandJoint.JointMiddleTip), P(HandJoint.JointRingTip), P(HandJoint.JointLittleTip) };
            var prox = new[] { P(HandJoint.JointIndexProximal), P(HandJoint.JointMiddleProximal), P(HandJoint.JointRingProximal), P(HandJoint.JointLittleProximal) };
            if (!all) return false;
            float low = float.MaxValue;
            for (int i = 0; i < (int)HandJoint.JointMax; i++)
            {
                var l = locs.jointLocations[i];
                low = Mathf.Min(low, World(l.pose.Position.ToVector3()).y - l.radius);
            }
            int ext = 0; float curlSum = 0f;
            for (int i = 0; i < 4; i++) curlSum += Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist));
            for (int i = 0; i < 4; i++) if (Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist)) > 1.45f) ext++;
            // 掌心法线：OpenXR 规定掌心关节的 −Y 指向掌心外侧（手掌朝下时 y≈−1）
            var pj = locs.jointLocations[(int)HandJoint.JointPalm].pose;
            var q = new Quaternion(pj.Orientation.x, pj.Orientation.y, -pj.Orientation.z, -pj.Orientation.w);
            float d = Vector3.Distance(thumb, tips[0]);
            bool aimOk = PXR_HandTracking.GetAimState(type, ref aim);
            bool sys = aimOk && ((ulong)aim.aimStatus & (ulong)HandAimStatus.AimIndexPinching) != 0;
            Ray? aimRay = null;
            if (aimOk && ((ulong)aim.aimStatus & (ulong)HandAimStatus.AimComputed) != 0)
            {
                var ap = aim.aimRayPose;
                var ao = World(new Vector3(ap.Position.x, ap.Position.y, -ap.Position.z));
                var aq = WorldRot(new Quaternion(ap.Orientation.x, ap.Orientation.y, -ap.Orientation.z, -ap.Orientation.w));
                aimRay = new Ray(ao, aq * Vector3.forward);
            }
            if (side == 0) AimLeft = aimRay; else AimRight = aimRay;
            tipDist[side] = d; sysPinch[side] = sys;
            // 11-06 自查：系统判定单独成立时，手半握 / 被挡住也可能误报 → 系统判定还要指尖距离在松开阈值以内才算
            // 11-06 真机：握拳 / 手指自然蜷着时拇指食指也贴着（0.5–2 cm），被当成一直在捏 → 捏合时其余三指至少伸直一根
            // （真正的捏是“OK 手势”，中指无名指小指是张开的）；已经捏住后不再要求，免得捏着拖动时手指一收就断
            // 中指 / 无名指 / 小指里至少一根没握死（指尖到腕 / 指根到腕 > 1.3；握死 ≈ 1.0，伸直 ≈ 1.9）：握拳（冲锋手势）和自然蜷着的手不算捏
            int others = 0; for (int i = 1; i < 4; i++) if (Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist)) > 1.3f) others++;
            bool start = d < PinchOn && others >= 1;
            bool was = pinch[side];
            pinch[side] = (sys && d < PinchOff) || (was ? d < PinchOff : start);
            // 11-08 真机日志：松手后手指自然蜷着，拇指食指仍贴着（0.5–2 cm），“捏合”一挂就是 20–50 秒——
            // 战场一直慢放、命令发不出去。捏着超过 StaleAfter 秒、而且最近 1.5 秒里手几乎没动（< 2.5 cm），就当已经松手；
            // 要等手指张开（> PinchOff）一次才能再捏
            var pp = (thumb + tips[0]) * 0.5f;
            if (pinch[side] && !was) { pinchT0[side] = Time.time; anchorP[side] = pp; anchorT[side] = Time.time; }
            if (pinch[side])
            {
                if ((pp - anchorP[side]).sqrMagnitude > 0.025f * 0.025f) { anchorP[side] = pp; anchorT[side] = Time.time; }
                if (Time.time - pinchT0[side] > StaleAfter && Time.time - anchorT[side] > 1.5f) { pinch[side] = false; stale[side] = true; }
            }
            // 11-08 用户：右手手势有时认不出——日志里捏合常常延续成握拳（拇指压在食指上），“捏着”就不认冲锋 / 坚守。
            // 捏着时中指 / 无名指 / 小指全部握死（比值都 < 1.15）且手 0.5 秒内几乎没动 → 当作握拳，松开捏合（拖动时手在动，不受影响）
            float maxOther = 0f; for (int i = 1; i < 4; i++) maxOther = Mathf.Max(maxOther, Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist)));
            if (pinch[side] && was && maxOther < 1.15f && Time.time - anchorT[side] > 0.5f) { pinch[side] = false; stale[side] = true; }
            if (stale[side]) { if (d > PinchOff) stale[side] = false; else pinch[side] = false; }
            h = new HandState
            {
                tracked = true, pinch = pinch[side], pinchPoint = (thumb + tips[0]) * 0.5f, indexTip = tips[0],
                palm = (wrist + prox[0] + prox[1] + prox[3]) * 0.25f, palmNormal = WorldRot(q) * Vector3.down,
                extended = ext, lowY = low, curl = curlSum / 4f,
            };
            return true;
        }

        bool ReadEye(out Ray ray)
        {
            ray = default;
            if (!eyeTried)
            {
                eyeTried = true;
                var start = new EyeTrackingStartInfo { needCalibration = 0, mode = EyeTrackingMode.PXR_ETM_BOTH };
                eyeStarted = PXR_MotionTracking.StartEyeTracking(ref start) == 0;
            }
            if (!eyeStarted) return false;
            var info = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
            if (PXR_MotionTracking.GetEyeTrackingData(ref info, ref eye) != 0 || eye.eyeDatas == null) return false;
            var cmb = eye.eyeDatas[(int)PerEyeUsage.Combined];
            if (cmb.isPoseValid == 0) return false;
            if (cmb.isOpennessValid != 0 && cmb.openness < 0.2f) return false;   // 闭眼 / 眨眼：这一帧不用眼动
            var o = new Vector3(cmb.pose.position.x, cmb.pose.position.y, -cmb.pose.position.z);
            var q = new Quaternion(cmb.pose.orientation.x, cmb.pose.orientation.y, -cmb.pose.orientation.z, -cmb.pose.orientation.w);
            ray = new Ray(World(o), (WorldRot(q) * Vector3.forward).normalized);
            return true;
        }

        void Update()
        {
            if (!Active) { Left = Right = default; PinchDownL = PinchDownR = false; return; }
            var t = cam.transform;
            bool hands = PXR_HandTracking.GetActiveInputDevice() == ActiveInputDevice.HandTrackingActive;
            float now = Time.time;
            if (hands && ReadHand(HandType.HandLeft, 0, out var nl)) { Left = lastL = nl; lostT[0] = now; }
            else if (now - lostT[0] < LostGrace) Left = lastL;              // 丢一两帧：保持上一帧（不松开捏合）
            else { Left = default; pinch[0] = false; }
            if (hands && ReadHand(HandType.HandRight, 1, out var nr)) { Right = lastR = nr; lostT[1] = now; }
            else if (now - lostT[1] < LostGrace) Right = lastR;
            else { Right = default; pinch[1] = false; }
            PinchDownL = Left.pinch && !prevL; PinchDownR = Right.pinch && !prevR;
            prevL = Left.pinch; prevR = Right.pinch;
            if (!Left.tracked) AimLeft = null;
            if (!Right.tracked) AimRight = null;
            // 11-06 用户：一直捏、捏不到 → 真机诊断：捏合起落、每 3 秒一次状态都写进 logcat（adb logcat -s Unity | grep "\[WT-IN\]"）
            var dev = PXR_HandTracking.GetActiveInputDevice();
            Diag = $"dev={dev} extL={Left.extended} extR={Right.extended} L={(Left.tracked ? (Left.pinch ? "PINCH" : "open") : "lost")} d={tipDist[0] * 100:0.0}cm sys={sysPinch[0]} R={(Right.tracked ? (Right.pinch ? "PINCH" : "open") : "lost")} d={tipDist[1] * 100:0.0}cm sys={sysPinch[1]} eye={EyeTracked}";
            if (Left.pinch != diagWasL || Right.pinch != diagWasR) { Debug.Log("[WT-IN] pinch-change " + Diag); diagWasL = Left.pinch; diagWasR = Right.pinch; }
            if (now - diagT > 3f) { diagT = now; Debug.Log("[WT-IN] status " + Diag); }
            EyeTracked = ReadEye(out var e);
            var target = EyeTracked ? e : new Ray(t.position, t.forward);
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, gazeSmoothing));
            gazeDir = gazeDir == Vector3.zero ? target.direction : Vector3.Slerp(gazeDir, target.direction, k);
            Gaze = new Ray(target.origin, gazeDir.normalized);
        }
    }
}
