using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.XR;

namespace WarTable.Campaign
{
    /// <summary>
    /// 立在地图近边上方的一排卡片（奖励三选一、补给站、军械库、战役结束）。每张卡是一张象牙白“征调单”：
    /// 藏青抬头写类别，中间一枚徽章 / 兵种标，下面写名字和一行说明或手写数字。
    /// 选：手指直接点卡面，或者手指着 / 眼睛看着它捏一下。点中的卡被红笔圈出来，其余的往下收走；补给站的卡可以反复点（钱够就行）。
    /// 卡片挂在地图根节点下（跟着沙盘走），本地 XY 立面朝向玩家。
    /// </summary>
    public class CampaignCards : MonoBehaviour
    {
        public class Def
        {
            public string header, icon, name, desc, number, cost, costIcon = "badge_crystals";   // 卡底 = 要付出的（默认星晶；遗迹里也可以是某个兵种 / 伤员）
            public Color iconTint = Color.white, numberColor = CampaignMap.Blue;   // 纹章 / 兵种盾都自带颜色，一般不着色
            public bool enabled = true, repeat;
            public object tag;
        }

        public MRInput mr;
        public Camera editorCam;
        public Game.GameAudio sound;
        public event System.Action<Def> OnPick;
        public bool Showing => cards.Count > 0;
        public int Count => cards.Count;

        float size = 1f;
        class Card { public Def d; public Transform t; public MeshRenderer ring; public Rect hit; public float hover, inT, pickT = -1f, outT = -1f; public Vector3 rest; }
        readonly List<Card> cards = new();
        readonly List<Ray> rays = new();
        int lastHover = -1, touchWas = -1; float t0;
        const float CH = 0.17f, CW = CH * 520f / 720f;

        public void Show(IList<Def> defs)
        {
            Clear();
            int n = defs.Count; size = n >= 4 ? 0.78f : 1f; float gap = (CW + (n >= 4 ? 0.012f : 0.03f)) * size;
            for (int i = 0; i < n; i++)
            {
                var c = new Card { d = defs[i], t = new GameObject("Card" + i).transform };
                c.t.SetParent(transform, false);
                float x = (i - (n - 1) / 2f) * gap;
                // 卡片立在近边上方、往后仰一点；两侧的略微转向中间（像摊在面前的一手牌）
                c.rest = new Vector3(x, 0.2f, -0.42f + Mathf.Abs(x) * 0.12f);
                c.t.localPosition = c.rest; c.t.localRotation = Quaternion.Euler(-18f, x * 40f, 0f);
                Compose(c);
                c.hit = new Rect(-CW / 2, -CH / 2, CW, CH);
                c.inT = -i * 0.08f;
                cards.Add(c);
            }
            t0 = Time.time; lastHover = -1;
            if (sound) sound.Cue("page");
        }

        public void Clear()
        {
            foreach (var c in cards) if (c.t) Destroy(c.t.gameObject);
            cards.Clear();
        }

        // ── 卡片版心（米，卡片中心为原点；由 make_campaign_atlas.py card() 的几条线换算：卡高 720 px = CH）
        //    抬头纸带中线 0.0665；金线内框上沿 0.046、下沿 −0.077；齿孔线 −0.0576（下面是价格存根，中线 −0.0673）。
        //    内容区在内框上沿和齿孔线之间，左右各留 1 cm：图标 → 名字 → 一行说明或数字，三行等距；只有图标时放在内容区正中。
        const float HeadY = 0.0665f, FrameTop = 0.046f, Perf = -0.0576f, StubY = -0.0673f, TextW = 0.086f;
        // 内容区高 0.1036：图标 0.042 + 名字 0.0135 + 数字（看上去约 0.016）= 0.0715，剩下的分成四段等距（各 0.008）
        const float IconY = 0.017f, NameY = -0.0188f, LineY = -0.0415f, IconH = 0.042f;

        void Compose(Card c)
        {
            var d = c.d; var t = c.t;
            CampaignArt.Quad(t, "card", CH, Color.white, CampaignArt.QCard);
            if (!string.IsNullOrEmpty(d.header) && CampaignArt.Has(CampaignArt.L(d.header)))
            {
                var hdr = CampaignArt.Quad(t, CampaignArt.L(d.header), 0.013f, CampaignMap.Gold, CampaignArt.QCard + 1);
                Fit(hdr.transform, TextW); hdr.transform.localPosition = new Vector3(0, HeadY, -0.0004f);
            }
            bool hasName = !string.IsNullOrEmpty(d.name) && CampaignArt.Has(d.name);
            bool hasLine = !string.IsNullOrEmpty(d.desc) && CampaignArt.Has(d.desc) || !string.IsNullOrEmpty(d.number);
            if (!string.IsNullOrEmpty(d.icon) && CampaignArt.Has(d.icon))
            {
                // 只有图标（出发 / 原样离开 / 战役结束）：图标放大一点、放在内容区正中
                float h = hasName ? IconH : IconH * 1.25f, y = hasName ? IconY : (FrameTop + Perf) / 2f;
                var ic = CampaignArt.Quad(t, d.icon, h, d.iconTint, CampaignArt.QCard + 1);
                ic.transform.localPosition = new Vector3(0, y, -0.0004f);
            }
            if (hasName)
            {
                var nm = CampaignArt.Quad(t, d.name, 0.0135f, CampaignMap.Ink, CampaignArt.QCard + 1);
                Fit(nm.transform, TextW); nm.transform.localPosition = new Vector3(0, hasLine ? NameY : NameY - 0.008f, -0.0004f);
            }
            if (!string.IsNullOrEmpty(d.desc) && CampaignArt.Has(d.desc))
            {
                var ds = CampaignArt.Quad(t, d.desc, 0.0095f, new Color(0.31f, 0.2f, 0.13f), CampaignArt.QCard + 1);
                Fit(ds.transform, TextW); ds.transform.localPosition = new Vector3(0, LineY + 0.002f, -0.0004f);
            }
            else if (!string.IsNullOrEmpty(d.number))
            {
                var nu = CampaignArt.Number(t, d.number, 0.019f, d.numberColor, false, 0.5f, CampaignArt.QCard + 1);
                nu.localPosition = new Vector3(0, LineY, -0.0004f);
            }
            if (!string.IsNullOrEmpty(d.cost))
            {
                // 存根：图标 + 数字作为一组，整组居中
                const float ih = 0.013f, nh = 0.0135f, gap = 0.0025f;
                float nw = CampaignArt.NumberWidth(d.cost, nh), total = ih + gap + nw, x0 = -total / 2f;
                var cb = CampaignArt.Quad(t, d.costIcon, ih, Color.white, CampaignArt.QCard + 1);
                cb.transform.localPosition = new Vector3(x0 + ih / 2f, StubY, -0.0004f);
                var cn = CampaignArt.Number(t, d.cost, nh, d.enabled ? CampaignMap.Red : CampaignMap.Gray, false, 0f, CampaignArt.QCard + 1);
                cn.localPosition = new Vector3(x0 + ih + gap, StubY, -0.0004f);
            }
            // 选中 = 盖一个棕红色的邮戳（压在卡的右下角内容区里，不压价格）
            c.ring = CampaignArt.Quad(t, "card_ring", 0.06f, Color.white, CampaignArt.QCard + 2);
            c.ring.transform.localPosition = new Vector3(0.022f, -0.022f, -0.001f);
            c.ring.gameObject.SetActive(false);
            // 钱不够 / 用不了：整张卡压暗（不藏起来，玩家知道有这一项）
            if (!d.enabled) foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) if (r != c.ring && r.name == "card") CampaignArt.Tint(r, new Color(0.62f, 0.6f, 0.58f));
        }

        /// <summary>字太宽就整体缩小到 maxW。</summary>
        static void Fit(Transform t, float maxW)
        {
            var mf = t.GetComponent<MeshFilter>(); if (!mf) return;
            float w = mf.sharedMesh.bounds.size.x * t.localScale.x;
            if (w > maxW) t.localScale *= maxW / w;
        }

        /// <summary>某张卡的状态变了（补给站花了钱）：重画这一张。</summary>
        public void Redraw(int i, Def d)
        {
            if (i < 0 || i >= cards.Count) return;
            var c = cards[i];
            foreach (Transform ch in c.t) Destroy(ch.gameObject);
            c.d = d; Compose(c);
        }

        void Update()
        {
            if (cards.Count == 0) return;
            float dt = Time.deltaTime;
            bool busy = false;
            foreach (var c in cards) if (c.pickT >= 0f && !c.d.repeat) busy = true;
            int hover = -1; bool tap = false;
            if (!busy && Time.time - t0 > 0.45f)
            {
                rays.Clear();
                if (mr && mr.Active) { mr.Pointers(rays); tap = mr.PinchDown; }
                else if (editorCam && Mouse.current != null) { rays.Add(WarTable.Game.FlyCam.View(editorCam).ScreenPointToRay(Mouse.current.position.ReadValue())); tap = Mouse.current.leftButton.wasPressedThisFrame; }
                foreach (var ray in rays)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < cards.Count; i++)
                        if (Hit(cards[i], ray, out float d) && d < best) { best = d; hover = i; }
                    if (hover >= 0) break;
                }
                int touch = -1;
                for (int i = 0; i < cards.Count && touch < 0; i++) if (Poke.Touch(cards[i].t, cards[i].hit, mr)) touch = i;
                if (touch >= 0) hover = touch;
                if (hover >= 0 && (tap || touch >= 0 && touchWas < 0)) Pick(hover);
                touchWas = touch;
            }
            if (hover != lastHover && hover >= 0 && sound) sound.Cue("hover");
            lastHover = hover;
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                var c = cards[i];
                c.inT += dt;
                float kIn = Mathf.Clamp01(c.inT / 0.3f), e = 1f - (1f - kIn) * (1f - kIn);
                c.hover = Mathf.MoveTowards(c.hover, i == hover && c.d.enabled ? 1f : 0f, dt / 0.15f);
                var p = c.rest + new Vector3(0, -0.06f * (1f - e), 0) + c.t.localRotation * new Vector3(0, 0, -0.012f * c.hover);
                float s = (0.0001f + e) * (1f + 0.05f * c.hover) * size;
                if (c.pickT >= 0f)
                {
                    c.pickT += dt;
                    float k = Mathf.Clamp01(c.pickT / 0.16f);
                    c.ring.gameObject.SetActive(true);
                    c.ring.transform.localScale = Vector3.one * 0.06f * Mathf.Lerp(1.8f, 1f, k * k);
                    float fade = c.d.repeat ? Mathf.Clamp01((1.3f - c.pickT) / 0.4f) : 1f;   // 可以反复点的卡：章停一秒再淡掉
                    CampaignArt.Tint(c.ring, new Color(1f, 1f, 1f, k * fade));
                    if (c.d.repeat && c.pickT > 1.3f) { c.pickT = -1f; c.ring.gameObject.SetActive(false); }
                }
                if (c.outT >= 0f) { c.outT += dt; float k = Mathf.Clamp01(c.outT / 0.35f); p += new Vector3(0, -0.08f * k, 0); s *= 1f - k; }
                c.t.localPosition = p; c.t.localScale = Vector3.one * Mathf.Max(0.0001f, s);
            }
        }

        void Pick(int i)
        {
            var c = cards[i];
            if (!c.d.enabled) { if (sound) sound.Cue("deny"); return; }
            if (sound) sound.Cue(c.d.repeat ? "select" : "stamp");
            c.pickT = 0f;
            if (!c.d.repeat)
                for (int k = 0; k < cards.Count; k++) if (k != i) cards[k].outT = 0f;
            Debug.Log("[Campaign] 选卡 " + i + " " + c.d.header + " " + c.d.name);
            if (c.d.repeat) OnPick?.Invoke(c.d);
            else StartCoroutine(After(0.9f, c.d));
        }

        System.Collections.IEnumerator After(float s, Def d)
        {
            yield return new WaitForSeconds(s);
            Clear();
            OnPick?.Invoke(d);
        }

        /// <summary>编辑器验收：直接点第 i 张。</summary>
        public void PickIndex(int i) { if (i >= 0 && i < cards.Count) Pick(i); }

        static bool Hit(Card c, Ray r, out float d)
        {
            d = 0f; var t = c.t;
            if (!t || !t.gameObject.activeInHierarchy) return false;
            Vector3 o = t.InverseTransformPoint(r.origin), v = t.InverseTransformDirection(r.direction);
            if (Mathf.Abs(v.z) < 1e-5f) return false;
            float k = -o.z / v.z; if (k <= 0f) return false;
            var p = o + v * k; d = Vector3.Distance(r.origin, t.TransformPoint(p));
            return c.hit.Contains(new Vector2(p.x, p.y));
        }
    }
}
