using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 让给“人”做的动作适合更壮的身躯（11-13 用户：深空的兵种要注意动作是否适合他现在的身躯）。
    /// 深空的重型机甲胸、肩、前臂、大腿都比人厚一大圈，原样套人的动作：手臂垂下来插进大腿、端枪时前臂埋进胸甲、盾贴着身体穿进去。
    /// 办法不是重做 50 段动作，而是每帧动画算完之后，把两条上臂统一再往外张一点（abduct）、往前送一点（flex）——
    /// 角度按每个兵种的身躯自动拟合（编辑器里 FigureClipAudit.Fit：在这个兵种实际用到的动作上试一圈角度，取“手臂 / 盾 / 武器落在身体里的比例”最小的）。
    /// 转动是在胸腔骨头的坐标系里做的（人躺下 / 弯腰时跟着身体走）。由 FigureAnimator.Tick 在求值之后调用。
    /// </summary>
    public class BodyFit : MonoBehaviour
    {
        [Tooltip("左右上臂骨（mixamorig:LeftArm / RightArm）和它们所在的胸腔骨")] public Transform armL, armR, chest;
        [Tooltip("绑定姿势下，身体的 上 / 右 / 前 在胸腔骨本地坐标里的方向（搭预制体时算好）")] public Vector3 upInChest = Vector3.up, rightInChest = Vector3.right, fwdInChest = Vector3.forward;
        [Tooltip("上臂往外张 / 往前送多少度：左、右各一组")] public float abductL, flexL, abductR, flexR;

        /// <summary>搭预制体时调用（此时是绑定姿势）：找骨头、记下身体坐标轴。</summary>
        public void Bind(Transform root, Transform l, Transform r)
        {
            armL = l; armR = r; chest = l.parent ? l.parent.parent : null;
            if (!chest) return;
            var right = (r.position - l.position); right -= Vector3.Dot(right, root.up) * root.up; right.Normalize();
            var up = root.up; var fwd = Vector3.Cross(right, up);
            upInChest = chest.InverseTransformDirection(up); rightInChest = chest.InverseTransformDirection(right); fwdInChest = chest.InverseTransformDirection(fwd);
        }

        public void Apply()
        {
            if (!chest || (abductL == 0f && flexL == 0f && abductR == 0f && flexR == 0f)) return;
            Vector3 up = chest.TransformDirection(upInChest), right = chest.TransformDirection(rightInChest), fwd = chest.TransformDirection(fwdInChest);
            One(armL, up, -right, fwd, abductL, flexL);
            One(armR, up, right, fwd, abductR, flexR);
        }

        static void One(Transform arm, Vector3 up, Vector3 outward, Vector3 fwd, float abduct, float flex)
        {
            if (!arm || (abduct == 0f && flex == 0f)) return;
            // 以“垂下的手臂”为准：往外张 = 把朝下的方向往身体外侧转 abduct 度；往前送 = 往身体前方转 flex 度。别的姿势下是绕同一根轴转
            float a = abduct * Mathf.Deg2Rad, f = flex * Mathf.Deg2Rad;
            var qa = Quaternion.FromToRotation(-up, -up * Mathf.Cos(a) + outward * Mathf.Sin(a));
            var qf = Quaternion.FromToRotation(-up, -up * Mathf.Cos(f) + fwd * Mathf.Sin(f));
            arm.rotation = qa * qf * arm.rotation;
        }
    }
}
