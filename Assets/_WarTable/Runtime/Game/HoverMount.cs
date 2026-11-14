using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 悬浮载具（11-12 用户：都到未来时代了，不一定还骑马，可以骑高科技坐骑）：未来时代的坐骑是没有腿、没有骨骼的悬浮摩托 / 甲虫飞行器，
    /// 没有行走动作——挂在坐骑模型外面的一层空节点上，自己做悬浮：上下轻轻起伏、左右微微摇摆、跑起来车头往下压一点；
    /// 坐骑阵亡时（FigureView 置 crashed）绕车身中心侧翻、落到地上。
    /// 这层节点在预制体里是单位变换（位置 0、不旋转、不缩放），尺寸单位 = 棋子本地（约等于码）。
    /// </summary>
    public class HoverMount : MonoBehaviour
    {
        [Tooltip("车身包围盒中心（本节点的本地坐标）")] public Vector3 center = new Vector3(0f, 1f, 0f);
        [Tooltip("车身半宽 / 半高（没填 landLeft / landRight 时用它估落地高度）")] public float halfWidth = 0.5f, halfHeight = 0.4f;
        [Tooltip("侧翻 +crashRoll / −crashRoll 之后，车身中心离地多高才刚好贴地（装配时用真实顶点算；0 = 没算，用包围盒估）")] public float landLeft, landRight;
        [Tooltip("起伏幅度")] public float bob = 0.035f;
        [Tooltip("摇摆幅度的倍数：悬浮载具 1；带轮子的摩托（现代时代）给很小的值，只留一点发动机抖动")] public float sway = 1f;
        [Tooltip("座位比原版鞍点低多少（骑手跟着降）")] public float seatDrop;
        [Tooltip("坠毁后侧翻的角度")] public float crashRoll = 72f;

        /// <summary>坐骑阵亡：落地、侧翻（FigureView.UpdateMount 设）。</summary>
        [System.NonSerialized] public bool crashed;
        /// <summary>行进速度占极速的比例 0–1：车头下压、起伏变快（FigureView.UpdateMount 设）。</summary>
        [System.NonSerialized] public float drive;
        /// <summary>这一帧的起伏高度（本节点单位）——骑手跟着一起浮，屁股不离座。</summary>
        public float Lift { get; private set; }

        static int serial;
        Vector3 rest; Quaternion restRot; bool init; float phase, fall, lean, clock;

        void Awake() { Capture(); }

        void Capture()
        {
            if (init) return;
            init = true; rest = transform.localPosition; restRot = transform.localRotation;
            int n = serial++;
            phase = (n * 37 % 101) * 0.062f;                                   // 每辆错开相位，不整齐划一
            crashRoll = Mathf.Abs(crashRoll) * (n % 2 == 0 ? 1 : -1);          // 往左往右翻各一半
        }

        /// <summary>一出场就是坠毁状态（半路加入的显示）：直接摆到终态，不播下落。</summary>
        public void Snap() { Capture(); crashed = true; fall = 1f; Apply(); }

        void LateUpdate()
        {
            Capture();
            float dt = Time.deltaTime;
            fall = Mathf.MoveTowards(fall, crashed ? 1f : 0f, dt * (crashed ? 1.9f : 1.2f));
            lean = Mathf.MoveTowards(lean, crashed ? 0f : Mathf.Clamp01(drive), dt * 1.5f);
            clock += dt * (2.1f + 1.4f * lean);
            Apply();
        }

        void Apply()
        {
            float t = clock + phase, live = 1f - fall;
            float f = fall * fall * (3f - 2f * fall);                 // 下落：先慢后快再收
            float exact = crashRoll > 0f ? landLeft : landRight;
            float land = exact > 0f ? exact : Mathf.Abs(Mathf.Sin(crashRoll * Mathf.Deg2Rad)) * halfWidth + Mathf.Abs(Mathf.Cos(crashRoll * Mathf.Deg2Rad)) * halfHeight;
            Lift = Mathf.Sin(t) * bob * live;
            float cy = Mathf.Lerp(center.y + Lift, land, f);          // 车身中心的高度：悬浮 → 侧躺在地上
            var r = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 1.4f * sway * live + 5f * Mathf.Min(1f, sway * 2f) * lean * live, 0f, Mathf.Sin(t * 0.53f + 1.3f) * 2f * sway * live + crashRoll * f);
            // 绕车身中心转（不是绕脚下的原点转，否则一侧翻就钻进地里）
            transform.localRotation = restRot * r;
            transform.localPosition = rest + restRot * (center - r * center) + new Vector3(0f, cy - center.y, 0f);
        }
    }
}
