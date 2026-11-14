using UnityEngine;
using WarTable.Command;

namespace WarTable.Game
{
    /// <summary>
    /// 新手引导（11-10 用户：改成软提示，不强制——玩家可能不想跟教程，只想马上看两军开打；开打后再给简单提示。
    /// 只有开局摆沙盘是强制的：先把沙盘挪好再继续。要像教傻瓜一样教，但要教得让人想玩下去）。
    ///
    /// 强制（摆沙盘，WarGame 不放行就不会开局）：①手掌平放桌上 → ②捏把手挪沙盘、点“放好了”。
    /// 软提示（不挡任何操作，不锁步骤；玩家想马上开打就捏帅旗）：
    ///   布阵：移动（幻影手从最近的蓝色令牌拖出去）→ 开战（帅旗发光）；
    ///   交战：进攻（幻影手从令牌拖到红色令牌）→ 冲锋 → 坚守 → 撤退。
    /// 一次只出一条；每条显示 ShowFor 秒后收起，隔 Cooldown 秒（或玩家闲着 IdleAfter 秒）再出现，最多出现 MaxShows 次；
    /// 玩家第一次做到就盖一个“学会了”的朱印（盖印声），这条以后不再出现（记在 PlayerPrefs，封面“教学”清空重来）。
    /// 11-14：打完一局就算教学结束（Done），之后不再出软提示——见 Done 的说明。
    /// 两只手都看不到时优先提示“把手抬到眼前”。
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        public enum Hint { Palm, Adjust, Move, Banner, Attack, Charge, Hold, Retreat, Hands }
        public static readonly string[] Keys = { "palm", "adjust", "move", "banner", "attack", "charge", "hold", "retreat", "hands" };

        public Transform icon;
        public Renderer iconRenderer;
        public Texture2D pinchTex, palmTex, trayTex, fistTex, pressTex, beckonTex;
        public float iconSize = 0.075f;
        /// <summary>提示条（Quad）；每条提示的贴图（下标 = Hint），学会时的朱印贴图（下标 = Hint，没有的为空）。</summary>
        public Renderer tip;
        public Texture2D[] tipZh, tipEn, learnZh, learnEn;
        public float tipWidth = 0.42f;
        readonly LazyFollow tipFollow = new() { distance = 0.6f, below = 0.2f, angle = 30f, yTol = 0.03f };

        const float ShowFor = 10f, Cooldown = 12f, IdleAfter = 10f, LearnShow = 1.8f, Gap = 1.5f;
        const int MaxShows = 3;

        bool headset, running;
        Hint? cur; float curT = -99f, hiddenT = -99f, lastActT;
        readonly int[] shows = new int[Keys.Length];
        Hint? learning; float learnT = -99f;
        float deployT = -1f, fightT = -1f;
        Vector3? demoFrom, demoTo;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), MapId = Shader.PropertyToID("_BaseMap");

        static string Pref(Hint h) => "wartable.learn." + Keys[(int)h];
        public static bool Learned(Hint h) => PlayerPrefs.GetInt(Pref(h), 0) == 1;
        static readonly Hint[] Teachable = { Hint.Move, Hint.Banner, Hint.Attack, Hint.Charge };   // 11-11：坚守 / 撤退不单独教（和冲锋是同一类操作）

        /// <summary>
        /// 新手教学结束了没有（11-14 用户：玩过新手教学之后，不该每次玩都再提醒）。
        /// 原来只有“这一条亲手做过”才算学会、不再提示——只扔令牌、从不拖部队的玩家，“移动 / 进攻”两条永远学不会，
        /// 于是每一局开头都被提示三遍、战场还被放慢（玩家的感觉就是“每次扔令牌它都在教我”）。
        /// 现在：打完一局（分出胜负）就算教学结束，之后任何一局都不再出软提示；四条都亲手做过也算。封面“教学”清掉这个记号重来。
        /// </summary>
        const string DoneKey = "wartable.tutorial.done";
        public static bool Done
        {
            get => PlayerPrefs.GetInt(DoneKey, 0) == 1;
            set { if (value) PlayerPrefs.SetInt(DoneKey, 1); else PlayerPrefs.DeleteKey(DoneKey); PlayerPrefs.Save(); }
        }

        /// <summary>全部学会了没有（教学结束也算）。设成 false = 清空记录（封面“教学”），所有提示重新出现。</summary>
        public static bool Seen
        {
            get { if (Done) return true; foreach (var h in Teachable) if (!Learned(h)) return false; return true; }
            set { if (value) return; foreach (var h in Teachable) PlayerPrefs.DeleteKey(Pref(h)); PlayerPrefs.DeleteKey(DoneKey); PlayerPrefs.Save(); }
        }

        /// <summary>一局打完（分出胜负）时调用：教学到此结束。</summary>
        public void Finish() { if (!Done) Done = true; }

        // 兼容 WarGame 的接口
        public bool Active => running;
        public bool AllowFight => true;
        /// <summary>交战中正显示一条还没学会的提示：战场放慢到 0.6 倍，给玩家照着做的时间。</summary>
        public bool SlowBattle => running && cur.HasValue && fightT >= 0f && cur.Value >= Hint.Attack && cur.Value <= Hint.Retreat;

        /// <summary>开局（从封面进入摆沙盘）时调用。</summary>
        public void Begin(bool headset)
        {
            this.headset = headset; running = true;
            cur = null; learning = null; hiddenT = -99f; lastActT = Time.time; deployT = fightT = -1f;
            System.Array.Clear(shows, 0, shows.Length);
            tipFollow.Reset();
        }

        public void Stop() { running = false; cur = null; Hide(); }

        /// <summary>玩家的操作（WarGame 转发 BattleCommand 的 Feedback / Ordered）。</summary>
        public void OnAction(string kind)
        {
            lastActT = Time.time;
            switch (kind)
            {
                case "move" or "line": Learn(Hint.Move); break;
                case "aim" or "attack": Learn(Hint.Move); Learn(Hint.Attack); break;   // 拖到敌军上也说明会拖了
                case "fight": Learn(Hint.Banner); break;
                case "charge": Learn(Hint.Charge); break;
                case "hold" or "retreat": Learn(Hint.Charge); break;   // 11-11 用户：同一类只教一次——令牌只教冲锋，用过哪支令牌都算会了
            }
        }

        void Learn(Hint h)
        {
            if (Learned(h)) return;
            PlayerPrefs.SetInt(Pref(h), 1); PlayerPrefs.Save();
            if (!running || Done) return;   // 教学结束以后不再盖“学会了”的印
            learning = h; learnT = Time.time;   // 盖“学会了”的印
            if (cur == h) { cur = null; hiddenT = Time.time; }
        }

        /// <summary>每帧：选这一刻该出哪条提示，摆幻影手 / 图标和提示条；返回要 FieldUI 强调的目标。</summary>
        public FieldUI.Focus Tick(WarGame g)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            var focus = new FieldUI.Focus();
            demoFrom = demoTo = null;
            Vector3? at = null; Texture2D tex = pinchTex; bool flat = false;
            var eye = Eye(g);
            if (!running) { Hide(); return focus; }
            if (g.Now == WarGame.Phase.Deploy && deployT < 0f) deployT = Time.time;
            if (g.Now == WarGame.Phase.Fight && fightT < 0f) { fightT = Time.time; Learn(Hint.Banner); cur = null; hiddenT = Time.time - Cooldown + 3f; }   // 开打 3 秒后再提示
            if (g.Now != WarGame.Phase.Deploy && g.Now != WarGame.Phase.Fight && g.Now != WarGame.Phase.Place) { Hide(); return focus; }
            if (learning.HasValue && Time.time - learnT > LearnShow) learning = null;

            Hint? show;
            if (g.Now == WarGame.Phase.Place)
                show = g.tray && g.tray.Placed ? Hint.Adjust : headset ? Hint.Palm : null;   // 强制的两步：一直显示，直到做完
            else show = Pick(g);
            if (show != cur) { cur = show; curT = Time.time; if (show.HasValue && show.Value >= Hint.Move) shows[(int)show.Value]++; }

            var cmd = g.Command;
            switch (cur)
            {
                case Hint.Palm:
                    tex = palmTex; flat = true;
                    if (eye) at = Ahead(eye, 0.45f, eye.position.y > 1f ? Mathf.Clamp(eye.position.y - 0.75f, 0.3f, 1.1f) : 0.38f);
                    break;
                case Hint.Adjust:
                    // 幻影手：捏住右把手往旁边拖一下，再到“放好了”上点一下
                    if (g.tray) { var h1 = g.tray.Handle(1); demoFrom = h1 + Vector3.up * 0.03f; demoTo = demoFrom.Value + g.board.right * -0.1f; }
                    break;
                case Hint.Move:
                {
                    var me = NearestOwn(g);
                    if (me == null) break;
                    focus.team = BattleCommand.Me; focus.group = me.Value.group;
                    double tx = me.Value.x, tz = me.Value.z - 12;   // 往敌人方向 12 码
                    demoFrom = TokenAt(g, me.Value); demoTo = g.SimToWorld(tx, g.Battle.Terrain!.Height(tx, tz) + 7, tz);
                    focus.hintLine = true; focus.x0 = me.Value.x; focus.z0 = me.Value.z; focus.x1 = tx; focus.z1 = tz;
                    break;
                }
                case Hint.Banner:
                    if (g.Standard) { at = g.Standard.position + Vector3.up * 0.06f; focus.standard = true; }
                    break;
                case Hint.Attack:
                {
                    var me = NearestOwn(g); var e = me == null ? null : NearestFoe(cmd, me.Value);
                    if (me == null || e == null) break;
                    focus.team = BattleCommand.Foe; focus.group = e.Value.group; focus.alsoOwn = me.Value.group;
                    demoFrom = TokenAt(g, me.Value); demoTo = TokenAt(g, e.Value);
                    break;
                }
                case Hint.Charge: case Hint.Hold: case Hint.Retreat:
                {
                    // 11-10：手势换成令架上的令牌 → 幻影手从令架捏起这一支，扔到沙盘中间（扔到哪都一样，都是对全军）
                    if (!g.orders || !g.board) break;
                    var o = cur == Hint.Charge ? OrderRack.Order.Charge : cur == Hint.Hold ? OrderRack.Order.Hold : OrderRack.Order.Retreat;
                    demoFrom = g.orders.SlotTop(o);
                    demoTo = g.board.TransformPoint(new Vector3(0f, 0.05f, BoardMap.HalfZ * BoardMap.Yard * 0.25f));   // 沙盘中间略靠玩家
                    break;
                }
            }
            Place(at, tex, flat, eye);
            Tip(g, eye);
            return focus;
        }

        /// <summary>布阵 / 交战时这一刻该出哪条（null = 现在不提示）。一次一条，没学会的按顺序来，每条出现过 MaxShows 次就跳过。</summary>
        Hint? Pick(WarGame g)
        {
            if (Done) return null;   // 教学已经结束：不再出任何软提示（摆沙盘的两步强制引导、“把手抬到眼前”不受影响）
            bool fight = g.Now == WarGame.Phase.Fight;
            // 正在显示的那条：没学会、阶段还对，就显示满 ShowFor 秒再收起（不中途换成别的）
            if (cur.HasValue && cur.Value >= Hint.Move && !Learned(cur.Value) && (cur.Value >= Hint.Attack) == fight)
            {
                if (Time.time - curT < ShowFor) return cur;
                hiddenT = Time.time; return null;
            }
            Hint[] order = fight ? new[] { Hint.Attack, Hint.Charge } : new[] { Hint.Move, Hint.Banner };
            Hint? next = null;
            foreach (var h in order) if (!Learned(h) && shows[(int)h] < MaxShows) { next = h; break; }
            // 布阵：先教移动；玩家一直没拖（20 秒后），和“捏帅旗开战”轮流提示——想直接开打的玩家也知道怎么开
            if (!fight && next == Hint.Move && cur != Hint.Move && deployT >= 0f && Time.time - deployT > 20f
                && !Learned(Hint.Banner) && shows[(int)Hint.Banner] < shows[(int)Hint.Move] && shows[(int)Hint.Banner] < MaxShows) next = Hint.Banner;
            if (next == null) return null;
            if (learning.HasValue || Time.time - learnT < LearnShow + Gap) return null;   // 刚盖完印：先让玩家看清
            bool idle = Time.time - lastActT > IdleAfter;
            bool first = shows[(int)next.Value] == 0;
            if (first ? Time.time - hiddenT > Gap : (Time.time - hiddenT > Cooldown || idle && Time.time - hiddenT > Gap * 2f)) return next;
            return null;
        }

        static Transform Eye(WarGame g) => g.mr && g.mr.Active ? g.mr.cam.transform : g.previewCamera ? g.previewCamera.transform : null;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized; }
        static Vector3 Ahead(Transform eye, float dist, float down)
        {
            var f = Flat(eye.forward); if (f == Vector3.zero) f = Vector3.forward;
            return eye.position + f * dist + Vector3.down * down;
        }

        /// <summary>离玩家最近的一支我军（教“拖”时用它：伸手最容易够着）。</summary>
        static BattleCommand.Flag? NearestOwn(WarGame g)
        {
            if (g.Command == null) return null;
            BattleCommand.Flag? best = null; double bz = double.MinValue;
            foreach (var f in g.Command.Flags()) if (f.team == BattleCommand.Me && f.z > bz) { bz = f.z; best = f; }   // 玩家一侧 = +z
            return best;
        }

        static BattleCommand.Flag? NearestFoe(BattleCommand cmd, BattleCommand.Flag me)
        {
            BattleCommand.Flag? best = null; double bd = double.MaxValue;
            foreach (var f in cmd.Flags())
            {
                if (f.team != BattleCommand.Foe) continue;
                double d = (f.x - me.x) * (f.x - me.x) + (f.z - me.z) * (f.z - me.z);
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        /// <summary>这支部队的令牌在哪（BoardSigns 摆的旗牌位置；没有就按部队中心上方 9 码）。</summary>
        static Vector3 TokenAt(WarGame g, BattleCommand.Flag f)
        {
            if (f.team == BattleCommand.Me && g.signs && g.signs.OwnTile(f.group, out var p, out _)) return p;
            return g.SimToWorld(f.x, g.Battle.Terrain!.Height(f.x, f.z) + 9, f.z);
        }

        void Place(Vector3? at, Texture2D tex, bool flat, Transform eye)
        {
            if (!icon) return;
            if (demoFrom.HasValue && demoTo.HasValue && pinchTex) { Demo(demoFrom.Value, demoTo.Value, eye); return; }
            if (!at.HasValue || !tex) { if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false); return; }
            icon.gameObject.SetActive(true);
            float t = Time.time - curT, appear = Mathf.Clamp01(t / 0.35f);
            var toEye = eye ? Flat(eye.position - at.Value) : Vector3.zero;
            icon.position = at.Value + (flat ? Vector3.zero : toEye * 0.03f) + Vector3.up * (0.006f * Mathf.Sin(t * 2.4f));
            if (flat) icon.rotation = Quaternion.Euler(90f, eye ? eye.eulerAngles.y : 0f, 0f);
            else if (eye) icon.rotation = Quaternion.LookRotation(icon.position - eye.position, Vector3.up);
            float beat = flat ? 0f : Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f / 1.6f)) * 0.12f;
            icon.localScale = Vector3.one * iconSize * (flat ? 1.4f - 0.4f * appear : 0.75f) * (1f - beat);
            if (iconRenderer) { iconRenderer.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, new Color(1f, 1f, 1f, 0.95f * appear)); iconRenderer.SetPropertyBlock(mpb); }
        }

        /// <summary>幻影手：半透明的捏合手示范一遍——出现在令牌上 → 捏住 → 拎着飘到目标 → 松开 → 淡出，2.6 秒一轮。</summary>
        void Demo(Vector3 from, Vector3 to, Transform eye)
        {
            icon.gameObject.SetActive(true);
            float u = (Time.time - curT) % 2.6f, sc = 1f, a = 0.85f;
            Vector3 pos;
            if (u < 0.35f) { pos = from; a *= u / 0.35f; sc = 1.15f - 0.15f * (u / 0.35f); }
            else if (u < 0.6f) { pos = from; sc = 1f - 0.18f * Mathf.Sin((u - 0.35f) / 0.25f * Mathf.PI * 0.5f); }
            else if (u < 1.8f) { float k = Mathf.SmoothStep(0f, 1f, (u - 0.6f) / 1.2f); pos = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.06f; sc = 0.82f; }
            else if (u < 2.1f) { pos = to; sc = Mathf.Lerp(0.82f, 1f, (u - 1.8f) / 0.3f); }
            else { pos = to; a *= 1f - (u - 2.1f) / 0.5f; }
            var toEye = eye ? (eye.position - pos).normalized : Vector3.zero;
            icon.position = pos + toEye * 0.025f + Vector3.up * 0.025f;
            if (eye) icon.rotation = Quaternion.LookRotation(icon.position - eye.position, Vector3.up);
            icon.localScale = Vector3.one * iconSize * 0.75f * sc;
            if (iconRenderer) { iconRenderer.GetPropertyBlock(mpb); mpb.SetTexture(MapId, pinchTex); mpb.SetColor(ColorId, new Color(1f, 1f, 1f, a)); iconRenderer.SetPropertyBlock(mpb); }
        }

        void Hide()
        {
            if (icon && icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
            if (tip && tip.gameObject.activeSelf) tip.gameObject.SetActive(false);
        }

        /// <summary>提示条：视线前方 0.6 m、抬到沙盘轮廓之上（不压沙盘）；学会时换成“学会了”朱印条，泛金一下；暂停菜单打开时让开。</summary>
        void Tip(WarGame g, Transform eye)
        {
            if (!tip) return;
            var holder = tip.transform.parent;
            bool zh = Loc.Current == Loc.Lang.Zh;
            var set = zh ? tipZh : tipEn; var learnSet = zh ? learnZh : learnEn;
            Texture2D tex = null; bool learned = false;
            bool lost = headset && g.HandsLostFor > 2.5f && (g.Now == WarGame.Phase.Deploy || g.Now == WarGame.Phase.Fight);
            if (lost) tex = Get(set, Hint.Hands);
            else if (learning.HasValue && Get(learnSet, learning.Value)) { tex = Get(learnSet, learning.Value); learned = true; }
            else if (cur.HasValue) tex = Get(set, cur.Value);
            bool paused = g.pause && g.pause.Open;
            if (!tex || paused || !holder || !eye) { if (tip.gameObject.activeSelf) tip.gameObject.SetActive(false); return; }
            if (!tip.gameObject.activeSelf) tip.gameObject.SetActive(true);
            float tipH = tipWidth / UiAspect.Of(tex, 5f);
            tipFollow.below = ViewClear.Below(eye, tipFollow.distance, tipWidth * 0.5f, tipH * 0.5f, 0.2f, 0.1f);
            tipFollow.Tick(holder, eye, false, Time.deltaTime);
            float k = learned ? Mathf.Clamp01((Time.time - learnT) / 0.25f) : Mathf.Clamp01((Time.time - curT) / 0.3f);
            float pop = learned ? Mathf.Sin(Mathf.Clamp01((Time.time - learnT) / 0.35f) * Mathf.PI) * 0.06f : 0f;
            tip.transform.localScale = new Vector3(tipWidth, tipH, 1f) * (1f + pop);
            var c = learned ? new Color(1f, 0.92f, 0.7f, k) : new Color(1f, 1f, 1f, k);
            tip.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, c); tip.SetPropertyBlock(mpb);
            if (learned && Time.time - learnT < Time.deltaTime * 1.5f && g.sound) g.sound.Cue("stamp");
        }

        static Texture2D Get(Texture2D[] set, Hint h) => set != null && (int)h < set.Length ? set[(int)h] : null;
    }
}
