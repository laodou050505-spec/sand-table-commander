using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 标题页中间那尊重甲骑士。11-18 晚用户：**做一尊完整模型就行，不要拆件、不要动作** → 这里只留一点点"活着"的呼吸：
    /// 整尊像一起很慢地上下浮 + 极轻微的侧倾（不转关节，所以不会出现拼装那种扭曲）。点出征时整尊像稳稳往上送一截 + 微微前倾，像迎战。
    /// </summary>
    public class CoverHero : MonoBehaviour
    {
        [Tooltip("整尊像（含岩石底座）")] public Transform statue;
        [Tooltip("像高（米）：位移量按它缩放")] public float size = 0.4f;

        Vector3 rest; Quaternion restRot;
        float raiseT = -1f;

        public bool Raised => raiseT >= 0f;

        void Awake() => Capture();

        /// <summary>记下静止姿态（搭建完调一次）。</summary>
        public void Capture()
        {
            if (!statue) return;
            rest = statue.localPosition; restRot = statue.localRotation;
        }

        /// <summary>点了出征。</summary>
        public void Raise() { if (raiseT < 0f) raiseT = 0f; }

        /// <summary>回到静止（封面重新出现时）。</summary>
        public void ResetPose() { raiseT = -1f; Pose(Time.time, 0f); }

        void LateUpdate()
        {
            if (!statue) return;
            if (raiseT >= 0f) raiseT += Time.deltaTime;
            Pose(Time.time, raiseT < 0f ? 0f : EaseOutBack(Mathf.Clamp01(raiseT / 0.5f)));
        }

        /// <summary>摆到时刻 t、出征程度 k（0 静止 … 1 送起）。编辑器验收时也直接调它。</summary>
        public void Pose(float t, float k)
        {
            float breath = Mathf.Sin(t * 0.62f);          // 周期约 10 秒
            float sway = Mathf.Sin(t * 0.37f + 1.1f);     // 周期约 17 秒
            statue.localPosition = rest + new Vector3(0f, breath * 0.006f + 0.055f * k, -0.01f * k) * size;
            statue.localRotation = restRot * Quaternion.Euler(-3.5f * k, sway * 0.9f, sway * 0.5f);
        }

        static float EaseOutBack(float x) { const float c = 1.2f; x -= 1f; return 1f + (c + 1f) * x * x * x + c * x * x; }
    }
}
