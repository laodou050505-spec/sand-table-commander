using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 封面 v6（10-31 用户选定概念 A：ArtSource/Cover/v6/cover_I_split_helm.png；参考图只借“高级感 + 神秘感”，贴合本游戏风格）：
    /// 房间压暗，一束顶光打在中央的双面战盔上（左半王国骑士盔、右半兽人角盔，沿裂缝崩开），行军地图残片和沙尘在光里缓慢漂浮，
    /// 背后一泼朱砂；书法标题带朱红错位重影；菜单斜排错落、每项一道墨笔刷底，被看着时亮起一道骨白笔刷，捏一下生效（和游戏里一样“看 + 捏”）。
    /// 菜单：出征（开始）/ 设置 / 退出；设置页（11-02 用户：语言放进设置，再放些游戏设置）：语言（中文 / ENGLISH）、战速、暗度（各三档，捏一下换下一档，
    /// 旁边三颗墨点显示档位）、返回。编辑器里 Enter 开始、L 切语言、Esc 返回。
    /// 开场（11-02 用户：标题有冲击力，做一段开场动画，约 1.9 s，捏一下 / Enter 跳过）：罩子压暗 → 朱砂从左往右泼开 →
    /// “将军沙盘”沿斜线一字一字从眼前砸下（放大 + 前冲 → 回弹，朱红重影慢半拍）、“将”落地时战盔一震 → 英文浮现 → 朱印盖下 → 菜单逐项长出。
    /// 热区：每项只认字和档位点的那块矩形（CoverBuilder.HitRect，不含墨笔刷），视线和项平面求交，同页项互不重叠。
    /// 开始后罩子淡出、整件缩小隐去，WarGame 接着让玩家按掌摆沙盘、开局。
    /// </summary>
    public class MenuCover : MonoBehaviour
    {
        public enum Action { Begin, Settings, Quit, Language, Speed, Dim, Back, Tutorial, Era, EraPick, Replace }   // 新动作只能加在最后（预制体 / 场景里按下标存）

        [System.Serializable]
        public class Item
        {
            public Action action;
            /// <summary>0 = 主菜单，1 = 设置页。</summary>
            public int page;
            public Transform root;
            /// <summary>档位点（战速 / 暗度）：前 n 颗亮。</summary>
            public Renderer[] pips;
            /// <summary>热区：项本地 xy 平面上的矩形（只包字和档位点）。</summary>
            public Rect hit;
            /// <summary>悬停时刷出的亮笔刷（WarTable/CoverUnlit，_Reveal 0→1）。</summary>
            public Renderer highlight;
            public GameObject zh, en;
            /// <summary>时代页（第 2 页，11-11 用户：主菜单只写“时代”，点进去选，包括原来那个，当前选中的标“当前”）：
            /// 这一项代表哪个时代（Settings.Era 的值），−1 = 不是时代项；current = “当前”标记（选中时显示）。</summary>
            public int era = -1;
            public GameObject currentZh, currentEn;
            /// <summary>11-12 用户：现代 / 未来兵种以后再做，先放进时代页，点了只说“敬请期待”：era ≥ Settings.Eras 的项是这种，
            /// currentZh / currentEn 换成“敬请期待 / COMING SOON”朱红小字，点了亮 SoonTime 秒，不改时代、不离开这一页。</summary>
            [HideInInspector] public float soonUntil;
            /// <summary>这一项的字（平时骨白，亮笔刷刷出时变炭黑，压在白笔刷上看得清）。</summary>
            public Renderer[] texts;
            [HideInInspector] public Vector3 rest;
            [HideInInspector] public float hover, show;
            [HideInInspector] public Vector3 restScale;
        }

        public Item[] items;
        public Transform helm;
        /// <summary>漂浮件（地图残片、裂缝崩出的碎块）：绕战盔缓慢漂、自转。</summary>
        public Transform[] drifters;
        /// <summary>压暗房间的罩子（跟着头，内表面朝里）。</summary>
        public Renderer dimmer;
        public GameObject titleEn;
        [Header("开场")]
        public Transform title;
        /// <summary>标题四个字（骨白）和对应的朱红重影，按落字顺序排（每个是字中心处的支点）。</summary>
        public Transform[] introChars, introGhosts;
        /// <summary>英文标题（SAND / TABLE / COMMANDER）和它的落字支点；英文时显示这组、藏中文标题和英文副标题。</summary>
        public Transform titleEnRoot;
        public Transform[] introCharsEn, introGhostsEn;
        public GameObject taglineBrush;
        public Renderer introInk;
        public Transform introSeal;
        public MRInput mr;
        public Transform editorEye;
        public GameAudio sound;
        int lastHover = -1;
        [Tooltip("离头的水平距离 / 低于眼睛多少（米）")] public float distance = 0.8f, below = -0.04f;   // 11-13：整体上移 8 cm（原来 0.04）
        [Tooltip("罩子把房间压暗到多少（0 不压，1 全黑）")] public float dim = 0.82f;

        public event System.Action OnBegin;
        /// <summary>11-14 用户：设置里要能重新识别沙盘位置。点了“重新定位”→ 置这个标记并照常开局；WarGame 在 OnBegin 里取走它，
        /// 清掉沙盘的位置，重新走“手掌按桌面 → 调整 → 放好了”。</summary>
        public bool ReplaceRequested { get; private set; }
        public bool TakeReplace() { bool r = ReplaceRequested; ReplaceRequested = false; return r; }
        public bool Started { get; private set; }

        Vector3 helmRest; Quaternion helmRot;
        Vector3[] driftRest; Quaternion[] driftRot; float[] driftPhase;
        float t0, outT = -1f, press = -1f, dimNow, introStart;
        Vector3[] charRest, ghostRest, charRestEn, ghostRestEn; Vector3 enRest, enScale, sealScale;
        // 开场时间表（秒）
        const float InkAt = 0.35f, InkDur = 0.45f, CharAt = 0.55f, CharGap = 0.13f, CharDur = 0.32f,
                    EnAt = 1.2f, SealAt = 1.35f, MenuAt = 1.55f, MenuGap = 0.1f, IntroEnd = 1.9f;
        float Intro => Time.time - introStart;
        int pressed = -1, page, latch = -1, lastTouch = -1;
        bool wasPinch;
        readonly LazyFollow follow = new();
        /// <summary>头显 XR 还没起来（第一帧前后）：先不摆、不开场，等头的位置有效。最多等 3 秒，超时退回编辑器相机。</summary>
        bool Ready => mr == null || !Application.isMobilePlatform || mr.Active || Time.time - bootT > 3f;
        float bootT;
        MaterialPropertyBlock mpb;
        static readonly Color TextIdle = new(0.9f, 0.86f, 0.78f), TextHot = new(0.07f, 0.06f, 0.055f), PipOff = new(0.3f, 0.28f, 0.25f), PipOffHot = new(0.55f, 0.52f, 0.47f);
        static readonly int RevealId = Shader.PropertyToID("_Reveal"), ColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (helm) { helmRest = helm.localPosition; helmRot = helm.localRotation; }
            foreach (var it in items) if (it.root) { it.rest = it.root.localPosition; it.restScale = it.root.localScale; }
            int n = drifters != null ? drifters.Length : 0;
            driftRest = new Vector3[n]; driftRot = new Quaternion[n]; driftPhase = new float[n];
            for (int i = 0; i < n; i++) { driftRest[i] = drifters[i].localPosition; driftRot[i] = drifters[i].localRotation; driftPhase[i] = i * 1.37f; }
            charRest = System.Array.ConvertAll(introChars ?? new Transform[0], x => x.localPosition);
            ghostRest = System.Array.ConvertAll(introGhosts ?? new Transform[0], x => x.localPosition);
            charRestEn = System.Array.ConvertAll(introCharsEn ?? new Transform[0], x => x.localPosition);
            ghostRestEn = System.Array.ConvertAll(introGhostsEn ?? new Transform[0], x => x.localPosition);
            if (titleEn) { enRest = titleEn.transform.localPosition; enScale = titleEn.transform.localScale; }
            if (introSeal) sealScale = introSeal.localScale;
        }

        void OnEnable()
        {
            Started = false; outT = -1f; press = -1f; t0 = Time.time; dimNow = 0f; introStart = Time.time; bootT = Time.time;
            follow.Reset();
            transform.localScale = Vector3.one;
            dim = Settings.Dim; page = 0;
            foreach (var it in items) { it.show = 0f; if (it.root) it.root.gameObject.SetActive(false); }   // 开场末尾再长出来
            Loc.Changed += ShowLang;
            ShowLang();
        }

        void OnDisable() => Loc.Changed -= ShowLang;

        Renderer[] all; bool hidden;
        /// <summary>XR 起来之前整件不画（否则会在错误的位置闪一下）。</summary>
        void HideUntilReady(bool hide)
        {
            if (hide == hidden) return;
            all ??= GetComponentsInChildren<Renderer>(true);
            foreach (var r in all) if (r) r.forceRenderingOff = hide;
            hidden = hide;
        }

        const float SoonTime = 2f;
        float soonCheck;

        void ShowLang()
        {
            bool zh = Loc.Current == Loc.Lang.Zh;
            foreach (var it in items)
            {
                if (it.zh) it.zh.SetActive(zh); if (it.en) it.en.SetActive(!zh);
                bool cur = it.era >= 0 && !Settings.Ready(it.era) ? Time.time < it.soonUntil : it.era >= 0 && it.era == Settings.Era;
                if (it.currentZh) it.currentZh.SetActive(zh && cur); if (it.currentEn) it.currentEn.SetActive(!zh && cur);
            }
            if (title) title.gameObject.SetActive(zh);
            if (titleEnRoot) titleEnRoot.gameObject.SetActive(!zh);
            if (titleEn) titleEn.SetActive(zh);          // 英文副标题只在中文版出现（英文版标题本身就是英文）
            if (taglineBrush) taglineBrush.SetActive(zh);
        }

        Transform Eye => mr && mr.Active ? mr.cam.transform : editorEye;

        /// <summary>摆到眼前并懒跟随（见 LazyFollow）。开始后（整件缩小隐去时）不再跟。</summary>
        void Place(float dt)
        {
            follow.distance = distance; follow.below = below;
            bool first = !follow.Placed;
            follow.Tick(transform, Eye, press >= 0f || lastHover >= 0, dt);
            if (first && follow.Placed) { introStart = Time.time; t0 = Time.time; }   // 真正摆到眼前那一刻才开场
        }

        void Update()
        {
            if (soonCheck > 0f && Time.time > soonCheck) { soonCheck = 0f; ShowLang(); }   // “敬请期待”到时收起
            float dt = Time.deltaTime;
            if (!Ready) { HideUntilReady(true); return; }
            HideUntilReady(false);
            if (outT < 0f) Place(dt);
            float t = Time.time - t0;

            // 罩子：跟着头，淡入 / 淡出
            dimNow = Mathf.MoveTowards(dimNow, outT >= 0f ? 0f : dim, dt / 1.2f);
            if (dimmer)
            {
                var eye = Eye;
                if (eye) dimmer.transform.position = eye.position;
                // 罩子半径 3 m，不跟着封面缩放（开始时封面缩小，罩子要原样淡出）
                dimmer.transform.localScale = Vector3.one * (6f / Mathf.Max(0.001f, transform.localScale.x));
                dimmer.GetPropertyBlock(mpb); mpb.SetColor(ColorId, new Color(0, 0, 0, dimNow)); dimmer.SetPropertyBlock(mpb);
            }

            // 战盔：缓慢摆头、上下浮
            if (helm)
            {
                float jx = Intro - (CharAt + CharDur * 0.45f);   // “将”砸到底的那一刻
                float jolt = jx > 0f ? Mathf.Exp(-jx * 9f) * Mathf.Sin(jx * 38f) * 0.005f : 0f;
                helm.localPosition = helmRest + Vector3.up * (Mathf.Sin(t * 0.7f) * 0.006f + jolt);
                helm.localRotation = helmRot * Quaternion.Euler(Mathf.Sin(t * 0.37f) * 2f, Mathf.Sin(t * 0.23f) * 7f, 0);
            }
            // 漂浮件：各自绕原位画小椭圆、慢慢自转
            for (int i = 0; i < driftRest.Length; i++)
            {
                float p = driftPhase[i], s = 0.35f + 0.1f * (i % 3);
                drifters[i].localPosition = driftRest[i] + new Vector3(Mathf.Sin(t * s + p) * 0.012f, Mathf.Sin(t * s * 1.3f + p * 2f) * 0.01f, Mathf.Cos(t * s + p) * 0.008f);
                drifters[i].localRotation = driftRot[i] * Quaternion.Euler(Mathf.Sin(t * 0.3f + p) * 12f, t * (6f + 3f * (i % 4)) * (i % 2 == 0 ? 1 : -1), Mathf.Sin(t * 0.21f + p) * 9f);
            }

            if (outT >= 0f) { Leave(dt); return; }
            PlayIntro();

            // 看着哪一项（头显：视线；编辑器：鼠标）
            Ray? gaze = null; bool pinch = false;
            if (mr && mr.Active) { gaze = mr.Gaze; pinch = mr.Left.pinch || mr.Right.pinch; }
            else if (Camera.main && Mouse.current != null) { gaze = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()); pinch = Mouse.current.leftButton.isPressed; }
            int hover = -1;
            if (gaze.HasValue && press < 0f)
            {
                // 视线打中好几项时（理论上同页不重叠）取最近的
                float best = float.MaxValue;
                for (int i = 0; i < items.Length; i++)
                    if (items[i].page == page && items[i].show > 0.9f && Hit(items[i], gaze.Value, out float d) && d < best) { best = d; hover = i; }
            }
            // 指尖悬在某项前方（还没碰上）：先把它点亮，优先于视线
            if (press < 0f)
                for (int i = 0; i < items.Length; i++)
                    if (items[i].page == page && items[i].show > 0.9f && Poke.Near(items[i].root, items[i].hit, mr)) { hover = i; break; }
            // 食指直接点（手够得着的项）：哪一项被指尖碰着
            int touch = -1;
            if (press < 0f)
                for (int i = 0; i < items.Length; i++)
                    if (items[i].page == page && items[i].show > 0.9f && Poke.Touch(items[i].root, items[i].hit, mr)) { touch = i; break; }
            // 11-06 用户：按完一次不要一直亮着 → 按过的那项先熄掉，视线离开它再回来才重新亮
            if (latch >= 0 && hover != latch && touch != latch) latch = -1;
            int litItem = hover == latch ? -1 : hover;   // 只影响亮不亮，捏照样生效
            bool tap = mr && mr.Active ? mr.PinchDown : pinch && !wasPinch;   // 11-06：头显上用每只手各自的按下沿
            if (tap && Intro < IntroEnd) { introStart = Time.time - IntroEnd; tap = false; }   // 开场中捏一下 = 跳过
            if (hover >= 0 && hover != lastHover && sound) sound.Cue("hover");
            lastHover = hover;
            if (touch >= 0 && touch != lastTouch && Intro >= IntroEnd) Press(touch);
            else if (tap && hover >= 0) Press(hover);
            lastTouch = touch;
            wasPinch = pinch;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.enterKey.wasPressedThisFrame)
                {
                    if (Intro < IntroEnd) introStart = Time.time - IntroEnd;
                    else Press(System.Array.FindIndex(items, x => x.action == Action.Begin));
                }
                if (Keyboard.current.lKey.wasPressedThisFrame) Fire(Action.Language);
                if (Keyboard.current.escapeKey.wasPressedThisFrame && page != 0) Fire(Action.Back);
            }

            // 按下：往里压 0.1 s 再弹回 0.15 s，压到底时生效
            float down = 0f;
            if (press >= 0f)
            {
                press += dt;
                down = press < 0.1f ? press / 0.1f : Mathf.Max(0f, 1f - (press - 0.1f) / 0.15f);
                if (press >= 0.1f && press - dt < 0.1f) { pickEra = items[pressed].era; Fire(items[pressed].action); }
                if (press > 0.25f) { press = -1f; latch = pressed; pressed = -1; }
            }
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                // 翻页：旧页的项缩小隐去，新页的项随后长出来（先收后放，0.18 s）
                bool here = it.page == page;
                bool othersOut = true;
                foreach (var o in items) if (o.page != page && o.show > 0f) { othersOut = false; break; }
                bool due = Intro >= MenuAt + MenuGap * (it.page == 0 ? i : 0);
                it.show = Mathf.MoveTowards(it.show, here && othersOut && due ? 1f : here ? it.show : 0f, dt / 0.18f);
                if (it.root)
                {
                    it.root.gameObject.SetActive(it.show > 0f);
                    it.root.localScale = it.restScale * Mathf.SmoothStep(0.001f, 1f, it.show);
                }
                it.hover = Mathf.MoveTowards(it.hover, i == litItem || i == pressed ? 1f : 0f, dt * 5f);
                float h = Mathf.SmoothStep(0, 1, it.hover);
                if (it.root) it.root.localPosition = it.rest + Vector3.forward * (0.008f * h - 0.006f * (i == pressed ? down : 0f));
                if (it.highlight)
                {
                    it.highlight.GetPropertyBlock(mpb); mpb.SetFloat(RevealId, h); it.highlight.SetPropertyBlock(mpb);
                }
                if (it.texts != null)
                    foreach (var r in it.texts)
                    {
                        if (!r) continue;
                        r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, Color.Lerp(TextIdle, TextHot, Mathf.Clamp01(h * 1.6f - 0.3f))); r.SetPropertyBlock(mpb);
                    }
                if (it.pips != null)
                {
                    int lit = (it.action == Action.Speed ? Settings.SpeedLevel : Settings.DimLevel) + 1;
                    float k = Mathf.Clamp01(h * 1.6f - 0.3f);
                    for (int j = 0; j < it.pips.Length; j++)
                    {
                        if (!it.pips[j]) continue;
                        var on = Color.Lerp(TextIdle, TextHot, k); var off = Color.Lerp(PipOff, PipOffHot, k);
                        it.pips[j].GetPropertyBlock(mpb); mpb.SetColor(ColorId, j < lit ? on : off); it.pips[j].SetPropertyBlock(mpb);
                    }
                }
            }
        }

        /// <summary>按下一项（玩家捏、键盘、测试都走这里，有按下动画）。</summary>
        public void Press(int index)
        {
            if (index < 0 || index >= items.Length || press >= 0f || outT >= 0f) return;
            pressed = index; press = 0f;
            if (sound) sound.Cue("press");
        }

        /// <summary>视线和项所在平面（项本地 z = 0）求交，交点落在热区矩形里才算看着；d = 沿视线的距离。</summary>
        static bool Hit(Item it, Ray r, out float d)
        {
            d = 0f;
            var t = it.root;
            if (!t) return false;
            Vector3 o = t.InverseTransformPoint(r.origin), v = t.InverseTransformDirection(r.direction);
            if (Mathf.Abs(v.z) < 1e-5f) return false;
            float k = -o.z / v.z;
            if (k <= 0f) return false;
            var p = o + v * k;
            d = Vector3.Distance(r.origin, t.TransformPoint(p));
            return it.hit.Contains(new Vector2(p.x, p.y));
        }

        static float OutBack(float x) { const float c = 2.2f; x -= 1f; return 1f + (c + 1f) * x * x * x + c * x * x; }
        static float OutCubic(float x) { x = 1f - x; return 1f - x * x * x; }

        /// <summary>编辑器验收用：把开场摆到第 t 秒（菜单按时间表直接显隐，不走翻页插值）。t 取很大 = 复原。</summary>
        public void PoseIntro(float t)
        {
            if (mpb == null || charRest == null) Awake();
            introStart = Time.time - t;
            PlayIntro();
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i]; if (!it.root) continue;
                float k = it.page == 0 ? Mathf.Clamp01((t - (MenuAt + MenuGap * i)) / 0.18f) : 0f;
                it.root.gameObject.SetActive(k > 0f);
                it.root.localScale = it.restScale * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, k));
            }
            if (t > IntroEnd + 1f) foreach (var it in items) if (it.root) { it.root.localScale = it.restScale; it.root.gameObject.SetActive(it.page == 0); }
        }

        /// <summary>开场动画（时间表见上面的常量）。结束后各件停在原位，不再改。</summary>
        void PlayIntro()
        {
            float x = Intro;
            if (x > IntroEnd + 0.5f) return;
            if (introInk)
            {
                introInk.GetPropertyBlock(mpb); mpb.SetFloat(RevealId, OutCubic(Mathf.Clamp01((x - InkAt) / InkDur))); introInk.SetPropertyBlock(mpb);
            }
            // 落字：从 1.8–2.2 倍、往玩家这边前冲 6 cm 的地方砸到原位，回弹略低于 1；重影慢 0.05 s、起点更大
            bool zh = Loc.Current == Loc.Lang.Zh;
            var cs = zh ? introChars : introCharsEn; var gs = zh ? introGhosts : introGhostsEn;
            var cr = zh ? charRest : charRestEn; var gr = zh ? ghostRest : ghostRestEn;
            for (int i = 0; cs != null && i < cr.Length; i++) Slam(cs[i], cr[i], x - (CharAt + CharGap * i), i == 0 ? 2.2f : 1.8f);
            for (int i = 0; gs != null && i < gr.Length; i++) Slam(gs[i], gr[i], x - (CharAt + CharGap * i + 0.05f), i == 0 ? 2.6f : 2.2f);
            if (titleEn)
            {
                float k = Mathf.Clamp01((x - EnAt) / 0.35f);
                titleEn.transform.localScale = enScale * Mathf.Max(0.001f, OutCubic(k));
                titleEn.transform.localPosition = enRest + Vector3.down * (0.012f * (1f - OutCubic(k)));
            }
            if (introSeal)
            {
                float k = Mathf.Clamp01((x - SealAt) / 0.22f);
                introSeal.gameObject.SetActive(k > 0f);
                introSeal.localScale = sealScale * Mathf.LerpUnclamped(2.4f, 1f, OutBack(k));
            }
        }

        static void Slam(Transform t, Vector3 rest, float x, float s0)
        {
            if (!t) return;
            float k = Mathf.Clamp01(x / CharDur);
            t.gameObject.SetActive(x > 0f);
            float e = OutBack(k);
            t.localScale = Vector3.one * Mathf.LerpUnclamped(s0, 1f, e);
            t.localPosition = rest + Vector3.forward * (0.06f * (1f - Mathf.Clamp01(e)));
        }

        /// <summary>执行动作（玩家按下、键盘、测试都走这里；编辑器测试可反射调用）。</summary>
        public void FireAction(Action a) => Fire(a);
        /// <summary>编辑器测试：选第 k 个时代。</summary>
        public void PickEra(int k) { pickEra = k; Fire(Action.EraPick); }
        int pickEra = -1;
        void Fire(Action a)
        {
            switch (a)
            {
                case Action.Language: Loc.Current = Loc.Current == Loc.Lang.Zh ? Loc.Lang.En : Loc.Lang.Zh; break;
                case Action.Settings: page = 1; if (sound) sound.Cue("page"); break;
                case Action.Back: page = 0; if (sound) sound.Cue("page"); break;
                // 设置里的“教学”：直接带着新手引导开一局（11-04 用户：教学没教到位，要能随时再看）
                case Action.Tutorial: Tutorial.Seen = false; Fire(Action.Begin); break;
                case Action.Replace: ReplaceRequested = true; Fire(Action.Begin); break;
                case Action.Speed: Settings.SpeedLevel++; break;
                case Action.Era: page = 2; ShowLang(); if (sound) sound.Cue("page"); break;
                case Action.EraPick:
                    if (pickEra >= 0 && !Settings.Ready(pickEra))
                    {
                        // 还没做的时代：亮“敬请期待”，留在这一页
                        foreach (var it in items) if (it.era == pickEra) it.soonUntil = Time.time + SoonTime;
                        soonCheck = Time.time + SoonTime + 0.05f; ShowLang(); if (sound) sound.Cue("hover"); break;
                    }
                    if (pickEra >= 0) { Settings.Era = pickEra; PlayerPrefs.Save(); } ShowLang(); page = 0; if (sound) sound.Cue("stamp"); break;
                case Action.Dim: Settings.DimLevel++; dim = Settings.Dim; break;
                case Action.Quit: if (!Application.isEditor) Application.Quit(); break;
                case Action.Begin:
                    if (Started) return;
                    Started = true; outT = 0f;
                    OnBegin?.Invoke();
                    break;
            }
        }

        /// <summary>开始：停 0.3 秒，整件缩小淡出（0.7 秒），罩子同时淡掉。</summary>
        void Leave(float dt)
        {
            outT += dt;
            float s = outT < 0.3f ? 1f : 1f - Mathf.SmoothStep(0, 1, (outT - 0.3f) / 0.7f);
            transform.localScale = Vector3.one * Mathf.Max(0.001f, s);
            if (outT > 1.2f && dimNow <= 0f) gameObject.SetActive(false);
        }
    }
}
