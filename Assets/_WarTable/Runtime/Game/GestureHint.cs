using UnityEngine;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 摆姿势停一下时的进度提示（11-07 用户：很多手势识别很费劲、玩家不知道怎么回事）：
    /// 右手举在胸前摆出握拳 / 掌心朝下 / 掌心朝己，眼前浮出对应的水墨图标，外圈一道金线随进度画满，满了就下令（GestureFlash 在同一位置弹出“谁 · 做什么”）。
    /// 11-08 用户：提示不要贴着手——手的遮挡轮廓会把它吞掉。现在放在眼前 0.6 m、略低于视线（和下令后的反馈牌同一处），
    /// 开始停姿势时定一次位置、之后不跟头；离手的方向至少 18°，太近就往上挪。图标 4 cm；松手 / 换姿势立刻消失。
    /// </summary>
    public class GestureHint : MonoBehaviour
    {
        public MRInput mr;
        public Material mat;
        public Texture2D fist, press, beckon;
        const float Size = 0.04f, Dist = 0.6f, Below = 0.05f, MinSep = 18f;
        readonly Vector3[] anchor = new Vector3[2]; readonly Quaternion[] facing = new Quaternion[2];
        const int Seg = 48;
        readonly Renderer[] icons = new Renderer[2];
        readonly LineRenderer[] rings = new LineRenderer[2];
        readonly float[] shown = new float[2];
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), MapId = Shader.PropertyToID("_BaseMap");
        static readonly Color Gold = new(1f, 0.82f, 0.45f);

        void Build()
        {
            mpb = new MaterialPropertyBlock();
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.name = "HoldIcon" + i; q.transform.SetParent(transform, false);
                icons[i] = q.GetComponent<Renderer>(); icons[i].sharedMaterial = mat; icons[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var l = new GameObject("HoldRing" + i).AddComponent<LineRenderer>(); l.transform.SetParent(transform, false);
                l.sharedMaterial = mat; l.useWorldSpace = true; l.widthMultiplier = 0.0028f; l.numCapVertices = 2;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.positionCount = 0;
                l.GetPropertyBlock(mpb); mpb.SetTexture(MapId, Texture2D.whiteTexture); mpb.SetColor(ColorId, Gold); l.SetPropertyBlock(mpb);
                rings[i] = l;
                q.SetActive(false); l.gameObject.SetActive(false);
            }
        }

        /// <summary>手势提示 / 反馈牌放在视线右侧 SideYaw 度（11-09：正前方是教学提示条，两个叠在一起看不清；右手做手势，放右边顺眼）。</summary>
        public const float SideYaw = 28f;

        /// <summary>眼前的提示位置：视线右侧 SideYaw 度、水平距离 dist、在沙盘轮廓之上；和两只手的方向至少隔 MinSep 度，不够就往上抬。</summary>
        public static Vector3 Spot(Transform eye, MRInput mr, float dist, float below)
        {
            var f = eye.forward; f.y = 0; f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
            f = Quaternion.Euler(0f, SideYaw, 0f) * f;
            below = ViewClear.Below(eye, dist, 0.06f, 0.03f, below, 0.1f, 3f, SideYaw);   // 11-09：也不压在沙盘上
            var p = eye.position + f * dist + Vector3.down * below;
            if (!mr) return p;
            for (int step = 0; step < 8; step++)
            {
                bool clear = true;
                foreach (var h in new[] { mr.Left, mr.Right })
                {
                    if (!h.tracked) continue;
                    var toHand = h.palm - eye.position; if (toHand.sqrMagnitude < 1e-4f) continue;
                    if (Vector3.Angle(p - eye.position, toHand) < MinSep) clear = false;
                }
                if (clear) break;
                p += Vector3.up * 0.04f;
            }
            return p;
        }

        public void Tick((Gesture pose, float progress)[] holding, bool active)
        {
            if (mpb == null) Build();
            var eye = mr && mr.Active ? mr.cam.transform : null;
            for (int i = 0; i < 2; i++)
            {
                var (pose, k) = holding != null && i < holding.Length ? holding[i] : (Gesture.None, 0f);
                bool on = active && eye && pose != Gesture.None && k > 0.12f;
                shown[i] = Mathf.MoveTowards(shown[i], on ? 1f : 0f, Time.deltaTime / 0.12f);
                if (shown[i] <= 0f || !eye) { icons[i].gameObject.SetActive(false); rings[i].gameObject.SetActive(false); anchor[i] = Vector3.zero; continue; }
                if (shown[i] <= Time.deltaTime / 0.12f + 1e-4f || anchor[i] == Vector3.zero)   // 刚出现：定一次位置
                {
                    anchor[i] = Spot(eye, mr, Dist, Below); facing[i] = Quaternion.LookRotation(anchor[i] - eye.position, Vector3.up);
                }
                var c = anchor[i]; var rot = facing[i];
                var tex = pose == Gesture.Hold ? press : pose == Gesture.Retreat ? beckon : fist;
                var it = icons[i].transform; it.gameObject.SetActive(true);
                it.SetPositionAndRotation(c, rot); it.localScale = Vector3.one * Size * (0.85f + 0.15f * k);
                icons[i].GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, new Color(1f, 1f, 1f, shown[i])); icons[i].SetPropertyBlock(mpb);
                // 进度圈：从正上方顺时针画到 k
                var l = rings[i]; l.gameObject.SetActive(true);
                int n = Mathf.Max(2, Mathf.CeilToInt(Seg * k) + 1); l.positionCount = n;
                float r = Size * 0.62f;
                for (int j = 0; j < n; j++)
                {
                    float a = Mathf.PI * 0.5f - Mathf.PI * 2f * k * j / (n - 1);
                    l.SetPosition(j, c + rot * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, -0.001f));
                }
                l.GetPropertyBlock(mpb); mpb.SetTexture(MapId, Texture2D.whiteTexture); mpb.SetColor(ColorId, new Color(Gold.r, Gold.g, Gold.b, shown[i])); l.SetPropertyBlock(mpb);
            }
        }
    }
}
