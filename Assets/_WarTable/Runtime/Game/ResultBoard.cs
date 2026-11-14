using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.XR;
using WarTable;

namespace WarTable.Game
{
    /// <summary>
    /// 结算（11-03 用户：结算显示人数；所有字都要有 UI 载体）。打完一局立在玩家眼前、略高于沙盘，风格同封面：
    /// - 书法大字“胜 / 败”（骨白 + 朱红错位重影，压在一泼朱砂上），从眼前砸下；
    /// - 星级：三颗朱印圆章，挣到的依次盖下，没挣到的是暗印；
    /// - 人数：王国旗 + “存活 / 出战”，氏族旗 + “击溃 / 敌军总数”，数字压在墨笔刷上、从 0 数上去（不写“存活”“击溃”字样，旗色说明是谁）；
    /// - 菜单“再战 / 回营”，和封面一样每项一道墨笔刷底，看着亮起白笔刷，捏一下生效（编辑器里鼠标点，Enter = 再战，Esc = 回营）。
    /// 热区同封面：只认字的矩形（项本地 xy 平面），视线和平面求交。
    /// </summary>
    public class ResultBoard : MonoBehaviour
    {
        public enum Action { Again, Camp }

        [System.Serializable]
        public class Item
        {
            public Action action;
            public Transform root;
            public Rect hit;
            public Renderer highlight;
            public GameObject zh, en;
            public Renderer[] texts;
            [HideInInspector] public float hover; [HideInInspector] public Vector3 rest, restScale;
        }

        /// <summary>一个数字位：0–9 和“/”各一个子物体（搭建时摆好），运行时只开一个。</summary>
        [System.Serializable]
        public class Slot { public GameObject[] glyphs; }

        [System.Serializable]
        public class Number
        {
            public Slot[] slots;
            /// <summary>每个字形的宽（项本地米，0–9、/）。</summary>
            public float[] widths;
            public Transform root;
        }

        public Item[] items;
        /// <summary>大字四个变体（胜 / VICTORY / 败 / DEFEAT），骨白放在 bigChar 下、朱红重影放在 bigGhost 下，顺序相同。</summary>
        public GameObject[] bigs, ghosts;
        public Transform bigChar, bigGhost;
        public Renderer splash;
        public Renderer[] stars;
        public Number ours, theirs;
        public MRInput mr;
        public Transform editorEye;
        public GameAudio sound;
        int lastHover = -1;
        int lastTouch = -1; bool[] stamped = new bool[3];
        public float distance = 0.5f, below = 0.04f;   // 11-07：拉近到伸手能点到（再战 / 回营可以直接用手指点）
        [Tooltip("沙盘根节点（WarGame 打完一局时填）：结算板的下沿抬到沙盘轮廓之上，不和沙盘叠在一起")]
        public Transform board;
        float liftCheckT = -9f;

        public event System.Action<Action> OnPick;
        readonly LazyFollow follow = new() { yTol = 0.04f };

        float t0, press = -1f; int pressed = -1; bool wasPinch;
        int survivors, total, routed, foeTotal, starsWon;
        Vector3 charRest, ghostRest;
        MaterialPropertyBlock mpb;
        static readonly int RevealId = Shader.PropertyToID("_Reveal"), ColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color TextIdle = new(0.9f, 0.86f, 0.78f), TextHot = new(0.07f, 0.06f, 0.055f),
                              StarOn = new(1f, 0.96f, 0.93f, 0.95f), StarOff = new(0.32f, 0.28f, 0.27f, 0.55f);
        const float CharAt = 0.1f, CharDur = 0.35f, StarAt = 0.55f, StarGap = 0.22f, NumAt = 0.5f, NumDur = 0.9f, MenuAt = 1.3f, MenuGap = 0.12f;

        float T => Time.time - t0;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (bigChar) charRest = bigChar.localPosition;
            if (bigGhost) ghostRest = bigGhost.localPosition;
            foreach (var it in items) if (it.root) { it.rest = it.root.localPosition; it.restScale = it.root.localScale; }
        }

        /// <summary>打完一局：摆到玩家眼前，播入场。</summary>
        public void Show(bool won, int stars, int survivors, int total, int routed, int foeTotal)
        {
            this.survivors = survivors; this.total = total; this.routed = routed; this.foeTotal = foeTotal; starsWon = stars;
            gameObject.SetActive(true);
            bool zh = Loc.Current == Loc.Lang.Zh;
            int pick = (won ? 0 : 2) + (zh ? 0 : 1);
            for (int i = 0; i < bigs.Length; i++) { if (bigs[i]) bigs[i].SetActive(i == pick); if (ghosts[i]) ghosts[i].SetActive(i == pick); }
            foreach (var it in items) { if (it.zh) it.zh.SetActive(zh); if (it.en) it.en.SetActive(!zh); }
            Place();
            t0 = Time.time; press = -1f; pressed = -1; wasPinch = true; lastHover = -1; stamped = new bool[3];   // 刚松手的那下捏不算
            Update();
        }

        public void Hide() => gameObject.SetActive(false);

        Transform Eye => mr && mr.Active ? mr.cam.transform : editorEye;

        void Place()
        {
            follow.distance = distance; follow.below = Below(); follow.Reset(); liftCheckT = Time.time;
            follow.Tick(transform, Eye, false, 0f);
        }

        /// <summary>
        /// 11-08 用户：结算不要再和沙盘叠在一起。从眼睛看过去，沿面板方向采样沙盘（含旗牌顶的高度）最高的仰角，
        /// 面板下沿再高出 3°——视线里面板和沙盘上下分开、互不遮挡。面板最低不低于原来的 below，最高到眼睛上方 12 cm（手还够得着点）。
        /// </summary>
        float Below()
        {
            var eye = Eye;
            if (!eye) return below;
            float halfH = 0.12f, halfW = 0.15f;
            var rs = GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                var inv = transform.worldToLocalMatrix; Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (var r in rs)
                {
                    var b = r.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var p = inv.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                        lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                    }
                }
                var sc = transform.lossyScale;
                halfH = Mathf.Clamp((hi.y - lo.y) * 0.5f * sc.y, 0.05f, 0.4f); halfW = Mathf.Clamp((hi.x - lo.x) * 0.5f * sc.x, 0.05f, 0.5f);
            }
            if (board) ViewClear.Board = board;
            return ViewClear.Below(eye, distance, halfW, halfH, below);
        }

        void Update()
        {
            if (mpb == null) Awake();
            float x = T, dt = Time.deltaTime;
            if (Time.time - liftCheckT > 0.5f) { liftCheckT = Time.time; follow.below = Below(); }   // 玩家站起 / 坐下、走动：跟着重算
            follow.Tick(transform, Eye, press >= 0f || lastHover >= 0, dt);

            // 大字：从 2.2 倍、前冲 6 cm 砸下；重影慢 0.05 s
            Slam(bigChar, charRest, x - CharAt, 2.2f);
            Slam(bigGhost, ghostRest, x - CharAt - 0.05f, 2.6f);
            if (splash)
            {
                splash.GetPropertyBlock(mpb); mpb.SetFloat(RevealId, OutCubic(Mathf.Clamp01((x - 0.02f) / 0.4f))); splash.SetPropertyBlock(mpb);
            }
            // 星：依次盖下（挣到的朱红、没挣到的暗）
            for (int i = 0; stars != null && i < stars.Length; i++)
            {
                var s = stars[i]; if (!s) continue;
                float k = Mathf.Clamp01((x - StarAt - StarGap * i) / 0.2f);
                s.gameObject.SetActive(k > 0f);
                if (k >= 0.5f && !stamped[i]) { stamped[i] = true; if (sound && i < starsWon) sound.Cue("stamp"); }
                s.transform.localScale = Vector3.one * Mathf.LerpUnclamped(2.2f, 1f, OutBack(k)) * StarSize;
                s.GetPropertyBlock(mpb); mpb.SetColor(ColorId, i < starsWon ? StarOn : StarOff); s.SetPropertyBlock(mpb);
            }
            // 数字：从 0 数上去
            float n = OutCubic(Mathf.Clamp01((x - NumAt) / NumDur));
            SetNumber(ours, Mathf.RoundToInt(survivors * n), total);
            SetNumber(theirs, Mathf.RoundToInt(routed * n), foeTotal);

            // 菜单
            Ray? gaze = null; bool pinch = false;
            if (mr && mr.Active) { gaze = mr.Gaze; pinch = mr.Left.pinch || mr.Right.pinch; }
            else if (Camera.main && Mouse.current != null) { gaze = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()); pinch = Mouse.current.leftButton.isPressed; }
            bool ready = x >= MenuAt + MenuGap * items.Length;
            int hover = -1; float best = float.MaxValue;
            if (gaze.HasValue && ready && press < 0f)
                for (int i = 0; i < items.Length; i++)
                    if (Hit(items[i], gaze.Value, out float d) && d < best) { best = d; hover = i; }
            if (ready && press < 0f)
                for (int i = 0; i < items.Length; i++) if (WarTable.XR.Poke.Near(items[i].root, items[i].hit, mr)) { hover = i; break; }
            bool tap = mr && mr.Active ? mr.PinchDown : pinch && !wasPinch;   // 11-06：头显上用每只手各自的按下沿 wasPinch = pinch;
            if (tap && !ready) t0 = Time.time - 10f;   // 入场中捏一下 = 跳过
            else if (tap && hover >= 0) { pressed = hover; press = 0f; if (sound) sound.Cue("press"); }
            // 食指直接点（11-06 用户：手够得着的键直接用手点）
            int touch = -1;
            if (ready && press < 0f)
                for (int i = 0; i < items.Length; i++) if (WarTable.XR.Poke.Touch(items[i].root, items[i].hit, mr)) { touch = i; break; }
            if (touch >= 0 && touch != lastTouch && press < 0f) { pressed = touch; press = 0f; if (sound) sound.Cue("press"); }
            lastTouch = touch;
            if (hover >= 0 && hover != lastHover && sound) sound.Cue("hover");
            lastHover = hover;
            if (ready && press < 0f && Keyboard.current != null)
            {
                if (Keyboard.current.enterKey.wasPressedThisFrame) { pressed = 0; press = 0f; }
                if (Keyboard.current.escapeKey.wasPressedThisFrame) { pressed = 1; press = 0f; }
            }
            float down = 0f;
            if (press >= 0f)
            {
                press += dt;
                down = press < 0.1f ? press / 0.1f : Mathf.Max(0f, 1f - (press - 0.1f) / 0.15f);
                if (press >= 0.1f && press - dt < 0.1f) OnPick?.Invoke(items[pressed].action);
                if (press > 0.25f) { press = -1f; pressed = -1; }
            }
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i]; if (!it.root) continue;
                float k = Mathf.Clamp01((x - MenuAt - MenuGap * i) / 0.18f);
                it.root.gameObject.SetActive(k > 0f);
                it.root.localScale = it.restScale * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, k));
                it.hover = Mathf.MoveTowards(it.hover, i == hover ? 1f : 0f, dt / 0.16f);
                it.root.localPosition = it.rest + Vector3.forward * (0.008f * it.hover - 0.006f * (i == pressed ? down : 0f));
                if (it.highlight) { it.highlight.GetPropertyBlock(mpb); mpb.SetFloat(RevealId, OutCubic(it.hover)); it.highlight.SetPropertyBlock(mpb); }
                var col = Color.Lerp(TextIdle, TextHot, Mathf.Clamp01(it.hover * 1.6f - 0.3f));
                if (it.texts != null) foreach (var r in it.texts) if (r) { r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, col); r.SetPropertyBlock(mpb); }
            }
        }

        public float StarSize = 0.04f;

        /// <summary>按“a/b”摆数字：从左往右（项本地 −x = 玩家右手边），右端对齐不变动，数到多少位就开多少位。</summary>
        static void SetNumber(Number num, int a, int b)
        {
            if (num == null || num.slots == null) return;
            string s = a + "/" + b;
            float x = 0f;
            for (int i = 0; i < num.slots.Length; i++)
            {
                var g = num.slots[i].glyphs;
                int idx = i < s.Length ? (s[i] == '/' ? 10 : s[i] - '0') : -1;
                for (int k = 0; k < g.Length; k++) if (g[k]) g[k].SetActive(k == idx);
                if (idx < 0) continue;
                float w = num.widths[idx];
                g[idx].transform.localPosition = new Vector3(x - w * 0.5f, 0f, 0f);
                x -= w + 0.004f;
            }
        }

        static bool Hit(Item it, Ray r, out float d)
        {
            d = 0f; var t = it.root;
            if (!t || !t.gameObject.activeInHierarchy) return false;
            Vector3 o = t.InverseTransformPoint(r.origin), v = t.InverseTransformDirection(r.direction);
            if (Mathf.Abs(v.z) < 1e-5f) return false;
            float k = -o.z / v.z;
            if (k <= 0f) return false;
            var p = o + v * k;
            d = Vector3.Distance(r.origin, t.TransformPoint(p));
            return it.hit.Contains(new Vector2(p.x, p.y));
        }

        static void Slam(Transform t, Vector3 rest, float x, float s0)
        {
            if (!t) return;
            float k = Mathf.Clamp01(x / CharDur), e = OutBack(k);
            t.gameObject.SetActive(x > 0f);
            t.localScale = Vector3.one * Mathf.LerpUnclamped(s0, 1f, e);
            t.localPosition = rest + Vector3.forward * (0.06f * (1f - Mathf.Clamp01(e)));
        }

        static float OutBack(float x) { const float c = 2.2f; x -= 1f; return 1f + (c + 1f) * x * x * x + c * x * x; }
        static float OutCubic(float x) { x = 1f - x; return 1f - x * x * x; }

        /// <summary>编辑器验收：把入场摆到第 t 秒。</summary>
        public void Pose(float t) { t0 = Time.time - t; Update(); }
    }
}
