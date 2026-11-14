using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 游戏途中的返回键（11-04 用户：游戏途中要有返回键）。布阵、交战时视线左下方浮着一枚墨底金圈的暂停键（看沙盘时它在视野左下角），
    /// 看着它捏一下 = 暂停，眼前出现“继续 / 回营”（和封面、结算同一套墨笔刷按钮，看着亮、捏一下生效），再捏一次暂停键也是继续。
    /// 暂停键 / 菜单正在被看着或被捏住时，这一捏不传给战场（不会误选部队、误划线）。编辑器里 Esc = 暂停 / 继续。
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public enum Action { Resume, Camp }

        [System.Serializable]
        public class Item
        {
            public Action action;
            public Transform root;
            public Rect hit;
            public Renderer highlight;
            public GameObject zh, en;
            public Renderer[] texts;
            [HideInInspector] public float hover;
            [HideInInspector] public Vector3 rest;
        }

        public Item[] items;
        public Transform button, panel;
        public Renderer buttonRenderer;
        public MRInput mr;
        public Transform editorEye;
        public GameAudio sound;
        [Tooltip("暂停键大小（贴在沙盘木框上）")] public float btnSize = 0.045f;
        public Transform board;

        /// <summary>选了一项（继续 / 回营）。</summary>
        public event System.Action<Action> OnPick;
        /// <summary>打开 / 关上暂停菜单（WarGame 据此暂停战场）。</summary>
        public event System.Action<bool> OnToggle;

        /// <summary>只在布阵、交战时出现。</summary>
        public bool Available;
        public bool Open { get; private set; }
        /// <summary>这一帧的捏合归暂停键 / 菜单（WarGame 不把它交给战场）。</summary>
        public bool Captures => Available && (Open || btnHover || owned);

        // 11-07 用户：暂停可以“点一下”也可以“看着捏一下” → 菜单拉近到伸手就能点到（0.45 m）、略低于视线，两种方式都舒服
        readonly LazyFollow follow = new() { distance = 0.45f, below = 0.12f, angle = 25f, yTol = 0.04f };   // 视线正前方（教学提示条在它下方 0.2 m，暂停时提示条隐藏）
        bool btnHover, owned, wasPinch, btnPlaced, btnTouchWas;
        int lastTouch = -1;
        float btnAwayT = 9f, toggleT = -9f;
        int lastHover = -1, pressed = -1; float press = -1f, btnHot;
        MaterialPropertyBlock mpb;
        static readonly int RevealId = Shader.PropertyToID("_Reveal"), ColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color TextIdle = new(0.9f, 0.86f, 0.78f), TextHot = new(0.07f, 0.06f, 0.055f);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            foreach (var it in items) if (it.root) it.rest = it.root.localPosition;
            if (panel) panel.gameObject.SetActive(false);
            if (button) button.gameObject.SetActive(false);
        }

        Transform Eye => mr && mr.Active ? mr.cam.transform : editorEye;

        public void Close() { if (Open) { Open = false; OnToggle?.Invoke(false); } if (panel) panel.gameObject.SetActive(false); }

        void Toggle()
        {
            Open = !Open;
            if (Open) { follow.Reset(); lastHover = -1; foreach (var it in items) it.hover = 0f; }
            if (panel) panel.gameObject.SetActive(Open);
            if (sound) sound.Cue(Open ? "page" : "press");
            OnToggle?.Invoke(Open);
        }

        void Update()
        {
            if (mpb == null) Awake();
            float dt = Time.unscaledDeltaTime;
            if (!Available)
            {
                Close();
                if (button && button.gameObject.activeSelf) button.gameObject.SetActive(false);
                btnHover = owned = false; btnPlaced = false; wasPinch = false;
                return;
            }
            var eye = Eye; if (!eye) return;
            if (button && !button.gameObject.activeSelf) button.gameObject.SetActive(true);

            // 暂停键：贴在沙盘木框左前角上（11-05 用户：不要在显眼的地方）
            if (button && board)
            {
                float s = board.lossyScale.x;
                float hx = (BoardMap.HalfX * BoardMap.Yard + 0.05f), hz = (BoardMap.HalfZ * BoardMap.Yard + 0.05f);
                var lp = board.TransformPoint(new Vector3(hx + 0.03f, 0.05f, hz + 0.03f));   // 左前角（玩家左手边、近边）
                button.position = lp;
                var face = lp - eye.position; face.y = 0;
                if (face.sqrMagnitude > 1e-4f) button.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }

            Ray? gaze = null; bool pinch = false;
            if (mr && mr.Active) { gaze = mr.Gaze; pinch = mr.Left.pinch || mr.Right.pinch; }
            else if (Camera.main && Mouse.current != null) { gaze = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()); pinch = Mouse.current.leftButton.isPressed; }

            btnHover = button && gaze.HasValue && HitSphere(gaze.Value, button.position, btnSize * 0.75f);
            int hover = -1;
            if (Open && gaze.HasValue && press < 0f)
            {
                // 11-09：菜单不和沙盘叠在一起——下沿抬到沙盘轮廓之上（最高到眼睛上方 8 cm，手还点得到）
                follow.below = ViewClear.Below(eye, follow.distance, 0.14f, 0.1f, 0.12f, 0.08f);
                follow.Tick(panel, eye, false, dt);
                float best = float.MaxValue;
                for (int i = 0; i < items.Length; i++) if (Hit(items[i], gaze.Value, out float d) && d < best) { best = d; hover = i; }
            }
            if (Open && press < 0f)
                for (int i = 0; i < items.Length; i++) if (Poke.Near(items[i].root, items[i].hit, mr)) { hover = i; break; }
            bool btnNear = button && mr && mr.Active && (TipHover(mr.Left) || TipHover(mr.Right));   // 指尖靠近暂停键：只放大提亮，不算看着（免得另一只手捏部队时误开暂停）
            if (hover >= 0 && hover != lastHover && sound) sound.Cue("hover");
            lastHover = hover;

            bool tap = mr && mr.Active ? mr.PinchDown : pinch && !wasPinch;   // 11-06：头显上用每只手各自的按下沿
            if (tap && (btnHover || Open)) owned = true;      // 这一捏归暂停键 / 菜单，松手前都不交给战场
            if (!pinch && !btnTouchWas && lastTouch < 0) owned = false;
            wasPinch = pinch;
            // 食指直接点（11-06 用户：手够得着的键直接用手点）：暂停键在木框左前角，伸手就到
            bool btnTouch = button && mr && mr.Active && (TipNear(mr.Left) || TipNear(mr.Right));
            int touch = -1;
            if (Open && press < 0f)
                for (int i = 0; i < items.Length; i++) if (Poke.Touch(items[i].root, items[i].hit, mr)) { touch = i; break; }
            // 点按防抖：指尖先离开暂停键 0.4 秒以上、再碰上才算一次点（手搁在木框角上不会反复开关），切换后 0.8 秒内不再认
            if (!btnTouch) btnAwayT += dt;
            bool tapOk = btnAwayT > 0.4f && Time.unscaledTime - toggleT > 0.8f;
            if (btnTouch && !btnTouchWas && tapOk) { owned = true; Toggle(); toggleT = Time.unscaledTime; }
            if (btnTouch) btnAwayT = 0f;
            else if (touch >= 0 && touch != lastTouch) { owned = true; pressed = touch; press = 0f; if (sound) sound.Cue("press"); }
            else if (tap && btnHover) Toggle();
            else if (tap && hover >= 0) { pressed = hover; press = 0f; if (sound) sound.Cue("press"); }
            if (btnTouch || touch >= 0) owned = true;
            btnTouchWas = btnTouch; lastTouch = touch;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Toggle();

            // 按下：往里压 0.1 s 生效，再弹回
            float down = 0f;
            if (press >= 0f)
            {
                press += dt;
                down = press < 0.1f ? press / 0.1f : Mathf.Max(0f, 1f - (press - 0.1f) / 0.15f);
                if (press >= 0.1f && press - dt < 0.1f) { var a = items[pressed].action; Close(); OnPick?.Invoke(a); }
                if (press > 0.25f) { press = -1f; pressed = -1; }
            }

            // 暂停键：被看着时放大一点、提亮
            btnHot = Mathf.MoveTowards(btnHot, btnHover || btnNear || Open ? 1f : 0f, dt / 0.15f);
            if (button) button.localScale = Vector3.one * btnSize * (1f + 0.18f * btnHot);
            if (buttonRenderer) { buttonRenderer.GetPropertyBlock(mpb); mpb.SetColor(ColorId, Color.Lerp(new Color(0.85f, 0.85f, 0.85f, 0.85f), Color.white, btnHot)); buttonRenderer.SetPropertyBlock(mpb); }

            if (!Open) return;
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i]; if (!it.root) continue;
                it.hover = Mathf.MoveTowards(it.hover, i == hover ? 1f : 0f, dt / 0.16f);
                it.root.localPosition = it.rest + Vector3.forward * (0.008f * it.hover - 0.006f * (i == pressed ? down : 0f));
                if (it.highlight) { it.highlight.GetPropertyBlock(mpb); mpb.SetFloat(RevealId, 1f - (1f - it.hover) * (1f - it.hover) * (1f - it.hover)); it.highlight.SetPropertyBlock(mpb); }
                var col = Color.Lerp(TextIdle, TextHot, Mathf.Clamp01(it.hover * 1.6f - 0.3f));
                if (it.texts != null) foreach (var r in it.texts) if (r) { r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, col); r.SetPropertyBlock(mpb); }
            }
            bool zh = Loc.Current == Loc.Lang.Zh;
            foreach (var it in items) { if (it.zh && it.zh.activeSelf != zh) it.zh.SetActive(zh); if (it.en && it.en.activeSelf == zh) it.en.SetActive(!zh); }
        }

        static bool HitSphere(Ray r, Vector3 c, float rad)
        {
            var oc = c - r.origin; float t = Vector3.Dot(oc, r.direction);
            return t > 0f && (oc - r.direction * t).sqrMagnitude < rad * rad;
        }

        bool TipHover(in HandState h) => h.tracked && !h.pinch && Vector3.Distance(h.indexTip, button.position) < btnSize * 0.7f + Poke.Hover;
        bool TipNear(in HandState h) => h.tracked && !h.pinch && Vector3.Distance(h.indexTip, button.position) < btnSize * 0.7f + 0.01f;

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
    }
}
