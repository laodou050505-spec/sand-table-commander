using UnityEngine;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 指挥手势的反馈：手势一被认出，视线水平稍下方（沙盘上方的空处）浮出一块小漆牌“全军 · 冲锋”，1.5 秒后淡出。
    /// 11-07 用户：图标不要那么大、要高级、不要挡住画面；红色警告玩家看不懂 →
    /// 牌子高 4 cm、离眼 0.6 m（约 4°），只轻轻浮起不弹跳；不再有“拒绝”的红色——没选中部队时手势对全军生效（WarGame）。
    /// </summary>
    public class GestureFlash : MonoBehaviour
    {
        public Renderer quad;
        public Texture2D fist, press, beckon;
        /// <summary>反馈牌：[手势 0 冲锋 / 1 坚守 / 2 撤退][谁 0 全军 / 1 步 / 2 弓 / 3 骑 / 4 骑射 / 5 所选]。</summary>
        public Texture2D[] badgesZh = new Texture2D[18], badgesEn = new Texture2D[18];
        /// <summary>11-13：按风格的叫法另出的反馈牌（“枪手 · 冲锋”）：下标 = 风格 × 18 + 令 × 6 + 谁；空项（全军 / 所选部队、中古）退回上面那套。</summary>
        public Texture2D[] eraBadgesZh, eraBadgesEn;
        public MRInput mr;
        public Transform editorEye;
        [Tooltip("牌子高度（米）")] public float size = 0.048f;
        [Tooltip("离眼睛的水平距离 / 低于眼睛多少（米）")] public float distance = 0.6f, below = 0.05f;

        const float Hold = 1.5f, Fade = 0.35f;
        float t0 = -9f; int gi, wi;
        Vector3 anchor; Quaternion rot;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), MapId = Shader.PropertyToID("_BaseMap");
        static readonly string[] Who = { "all", "inf", "arch", "cav", "ha", "some" };

        Transform Eye => mr && mr.Active ? mr.cam.transform : editorEye;

        public void Show(Gesture g, bool accepted) => Show(g, accepted, "all");

        public void Show(Gesture g, bool accepted, string who)
        {
            gi = g == Gesture.Hold ? 1 : g == Gesture.Retreat ? 2 : 0;
            wi = Mathf.Max(0, System.Array.IndexOf(Who, who));
            t0 = Time.time;
            // 出现时定在眼前一次，之后不跟头（读字时头一动牌子就跑，反而晕）
            var eye = Eye;
            if (eye)
            {
                anchor = GestureHint.Spot(eye, mr, distance, below);   // 11-08：和进度提示同一处，避开手（手的遮挡轮廓会吞掉贴着手的 UI）
                rot = Quaternion.LookRotation(anchor - eye.position, Vector3.up);
            }
        }

        void Update()
        {
            if (!quad) return;
            float t = Time.time - t0;
            var set = Loc.Current == Loc.Lang.Zh ? badgesZh : badgesEn;
            var tex = set != null && gi * 6 + wi < set.Length ? set[gi * 6 + wi] : null;
            {
                var es = Loc.Current == Loc.Lang.Zh ? eraBadgesZh : eraBadgesEn; int ei = Settings.Era * 18 + gi * 6 + wi;
                if (es != null && ei >= 0 && ei < es.Length && es[ei]) tex = es[ei];
            }
            if (!tex) tex = gi == 1 ? press : gi == 2 ? beckon : fist;
            if (t >= Hold + Fade || !tex) { if (quad.gameObject.activeSelf) quad.gameObject.SetActive(false); return; }
            if (!quad.gameObject.activeSelf) quad.gameObject.SetActive(true);
            mpb ??= new MaterialPropertyBlock();
            float k = Mathf.Clamp01(t / 0.18f), e = 1f - (1f - k) * (1f - k);
            var tr = quad.transform;
            tr.SetPositionAndRotation(anchor + Vector3.up * (0.008f * e), rot);
            tr.localScale = new Vector3(size * UiAspect.Of(tex), size, 1f);
            float a = t < Hold ? e : 1f - (t - Hold) / Fade;
            quad.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, new Color(1f, 1f, 1f, a)); quad.SetPropertyBlock(mpb);
        }
    }
}
