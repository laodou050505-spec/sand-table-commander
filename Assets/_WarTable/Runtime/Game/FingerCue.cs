using UnityEngine;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 手指上的捏合提示圈（11-09 别人试玩：捏了没反应，不知道是没捏对还是没对准）。
    /// 每只手在拇指和食指之间浮一个小圈：手张开时是淡淡的大圈，两指靠近时缩小变亮，捏住时变成实心金点；
    /// 手够着了可以拿的令牌时圈变金色（= 现在捏就能拿起来）。
    /// 圈画在捏合点往眼睛方向 3 cm 处——在手指前面，不会被手部遮挡（HandOccluder）吞掉。
    /// 手伸远了（虚拟指尖被往前推，见 WarGame.VirtualTip）：推出去的位置再画一个同样的小圈，和手指之间连一根淡线——
    /// 玩家看得到“我现在够到的是那里”。
    /// </summary>
    public class FingerCue : MonoBehaviour
    {
        public MRInput mr;
        public Material mat;
        const int Seg = 28;
        readonly LineRenderer[] rings = new LineRenderer[2], far = new LineRenderer[2], links = new LineRenderer[2];
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), MapId = Shader.PropertyToID("_BaseMap");
        static readonly Color Bone = new(0.95f, 0.92f, 0.84f), Gold = new(1f, 0.8f, 0.36f);

        void Build()
        {
            mpb = new MaterialPropertyBlock();
            LineRenderer Make(string n, bool loop, int count)
            {
                var l = new GameObject(n).AddComponent<LineRenderer>(); l.transform.SetParent(transform, false);
                l.sharedMaterial = mat; l.useWorldSpace = true; l.loop = loop; l.positionCount = count; l.numCapVertices = 0;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.receiveShadows = false; l.enabled = false;
                return l;
            }
            for (int i = 0; i < 2; i++) { rings[i] = Make("FingerRing" + i, true, Seg); far[i] = Make("ReachRing" + i, true, Seg); links[i] = Make("ReachLink" + i, false, 2); }
        }

        /// <param name="active">布阵 / 交战、没暂停、没在抓沙盘</param>
        /// <param name="reachHand">哪只手够着了令牌（0 左 1 右 −1 没有）</param>
        public void Tick(bool active, int reachHand, WarGame game)
        {
            if (mpb == null) Build();
            var eye = mr && mr.Active ? mr.cam.transform : null;
            for (int i = 0; i < 2; i++)
            {
                var l = rings[i];
                var h = i == 0 ? mr.Left : mr.Right;
                bool on = active && eye && h.tracked;
                Vector3 to = default;
                if (on)
                {
                    to = h.pinchPoint - eye.position;
                    on = to.sqrMagnitude > 0.01f && Vector3.Angle(to, eye.forward) < 55f;   // 手不在眼前（搁在腿上）不画
                }
                if (!on) { if (l.enabled) l.enabled = false; far[i].enabled = links[i].enabled = false; continue; }
                float gap = mr.TipGap(i);
                float k = Mathf.InverseLerp(0.08f, 0.015f, gap);   // 0 = 张开，1 = 快捏上了
                bool reach = reachHand == i;
                float rad = h.pinch ? 0.0028f : Mathf.Lerp(0.0095f, 0.0045f, k);
                float width = h.pinch ? 0.0056f : Mathf.Lerp(0.0011f, 0.0018f, k);   // 捏住：线宽 = 直径 → 实心点
                var c = h.pinch || reach ? Gold : Bone;
                float a = h.pinch ? 1f : reach ? 0.95f : Mathf.Lerp(0.28f, 0.75f, k);
                var center = h.pinchPoint - to.normalized * 0.03f;
                Circle(l, center, eye, rad, width, new Color(c.r, c.g, c.b, a));
                // 推出去的虚拟指尖：远处同样画一个圈（离得远，半径按距离放大，看上去一样大），中间一根淡线
                float ext = 0f;
                var vp = game ? game.VirtualTip(h.pinchPoint, out ext) : h.pinchPoint;
                bool show = ext > 0.03f;
                if (show)
                {
                    float s = Vector3.Distance(eye.position, vp) / Mathf.Max(0.1f, to.magnitude);
                    Circle(far[i], vp, eye, rad * s, width * s, new Color(c.r, c.g, c.b, Mathf.Max(a, 0.6f)));
                    var lk = links[i]; lk.SetPosition(0, center); lk.SetPosition(1, vp); lk.widthMultiplier = 0.0008f;
                    lk.GetPropertyBlock(mpb); mpb.SetTexture(MapId, Texture2D.whiteTexture); mpb.SetColor(ColorId, new Color(c.r, c.g, c.b, 0.35f)); lk.SetPropertyBlock(mpb);
                    if (!lk.enabled) lk.enabled = true;
                }
                else { far[i].enabled = false; links[i].enabled = false; }
            }
        }

        void Circle(LineRenderer l, Vector3 center, Transform eye, float rad, float width, Color col)
        {
            var rot = Quaternion.LookRotation((center - eye.position).normalized, Vector3.up);
            for (int j = 0; j < Seg; j++)
            {
                float ang = Mathf.PI * 2f * j / Seg;
                l.SetPosition(j, center + rot * new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0f));
            }
            l.widthMultiplier = width;
            l.GetPropertyBlock(mpb); mpb.SetTexture(MapId, Texture2D.whiteTexture); mpb.SetColor(ColorId, col); l.SetPropertyBlock(mpb);
            if (!l.enabled) l.enabled = true;
        }
    }
}
