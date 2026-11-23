using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 浮在眼前的面板（封面、结算板）的摆放（11-03 用户：标题的位置没在眼前）。
    /// 原来只在第一帧摆一次：头显刚启动那一帧 XR 还没起来，退回到编辑器座位相机去摆，标题被放到和头无关的地方。现在：
    /// - 头显上等 XR 起来、头部位置有效后才第一次摆（直接到位）；
    /// - 之后“懒跟随”：面板偏离视线水平正前方超过 angle 度、或离头的距离 / 高度差太多（转身、系统重新校准中心）时，
    ///   平滑挪回眼前（约 0.4 秒），对正后停下；差得太离谱（追踪原点跳变）直接到位。
    /// - hold = true（正看着按钮 / 正在捏）时不开始挪，免得按钮从视线下溜走。
    /// 只转水平朝向，面板一直是竖直的。
    /// </summary>
    public class LazyFollow
    {
        public float distance = 0.8f, below = 0.04f, angle = 28f;
        /// <summary>高度差超过多少才重新挪（会随沙盘重算 below 的面板设小一点，抬高 / 降低能跟上）。</summary>
        public float yTol = 0.18f;
        public bool Placed { get; private set; }
        bool following;

        public void Reset() { Placed = false; following = false; }

        /// <summary>每帧调用。eye = 头（头显相机）或编辑器座位相机；返回是否已经摆好。</summary>
        public bool Tick(Transform self, Transform eye, bool hold, float dt)
        {
            if (!eye) return Placed;
            var f = eye.forward; f.y = 0; f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
            var target = eye.position + f * distance + Vector3.down * below;
            var rot = Quaternion.LookRotation(-f, Vector3.up);   // 正面（本地 +Z）朝玩家
            if (!Placed)
            {
                self.SetPositionAndRotation(target, rot);
                Placed = true; following = false;
                return true;
            }
            // 现在面板在头的哪个方向（水平）、离多远、高度差多少
            var to = self.position - eye.position; var toH = to; toH.y = 0;
            float off = toH.sqrMagnitude < 1e-6f ? 180f : Vector3.Angle(f, toH);
            float distErr = Mathf.Abs(toH.magnitude - distance);
            float yErr = Mathf.Abs(self.position.y - target.y);
            if ((self.position - target).sqrMagnitude > 1.5f * 1.5f) { self.SetPositionAndRotation(target, rot); following = false; return true; }
            if (!following && !hold && (off > angle || distErr > 0.25f || yErr > yTol)) following = true;
            if (following)
            {
                float k = 1f - Mathf.Exp(-dt * 7f);
                self.SetPositionAndRotation(Vector3.Lerp(self.position, target, k), Quaternion.Slerp(self.rotation, rot, k));
                if (off < 2f && (self.position - target).sqrMagnitude < 0.01f * 0.01f) following = false;
            }
            return true;
        }
    }
}
