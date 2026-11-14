using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.Command;
using WarTable.Sim;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 一局游戏的主控：封面 → 布阵（时间不走）→ 交战（敌军 AI 指挥官全权指挥，我军不下令就站着）→ 结算。
    /// 模拟按原型的节奏推进：每步 1/60 秒，战斗整体放慢到 0.6 倍（原速一局不到一分钟，来不及指挥），另有慢动作和暂停。
    /// 电脑上的试玩操作（鼠标当视线 + 捏合、键盘快捷键）和屏幕上的临时文字只为在编辑器里测玩法，不是头显界面（UI 阶段重做）。
    /// </summary>
    public class WarGame : MonoBehaviour
    {
        /// <summary>封面 → 摆沙盘（头显上按掌放到桌面；编辑器里跳过）→ 布阵 → 交战 → 结算。</summary>
        public enum Phase { Cover, Place, Deploy, Fight, Over }

        public BattleView view;
        public Transform board;
        public GameObject cover;
        /// <summary>封面（双面战盔 + 书法标题 + 斜排菜单）。</summary>
        public MenuCover menu;
        /// <summary>沙盘布景里的两面帅旗（封面时由封面那两面代替）。</summary>
        public GameObject[] fieldStandards;
        /// <summary>我方帅旗（看着它捏 = 布阵时开战、交战时全选）。</summary>
        Transform standard;
        /// <summary>我方帅旗（引导用）。</summary>
        public Transform Standard => standard;
        public Camera previewCamera;
        /// <summary>头显输入和托盘摆放（头显上才生效；编辑器里用鼠标键盘试玩）。</summary>
        public MRInput mr;
        public TrayRig tray;
        /// <summary>战场提示（光环、划线、帅旗呼吸，不写字）和结算板（胜 / 败、星、人数、再战 / 回营）。</summary>
        public FieldUI field;
        public ResultBoard result;
        /// <summary>BGM、界面音、战斗声。</summary>
        public GameAudio sound;
        /// <summary>玩中学的开局引导（第一次玩时）。</summary>
        public Tutorial tutorial;
        /// <summary>游戏途中的返回键（暂停 → 继续 / 回营）。</summary>
        public PauseMenu pause;
        /// <summary>沙盘上常驻标识（旗牌、说明牌、把手）。</summary>
        public BoardSigns signs;
        /// <summary>指挥手势被认出时胸前闪一下对应图标。</summary>
        public GestureFlash gestureFlash;
        public HitFx hitFx;
        /// <summary>编辑器调试用的屏幕字（战旗标签、军令回报）；头显上不画。</summary>
        public bool debugText;

        /// <summary>战速（封面“设置”里三档，默认 0.6）。</summary>
        public static float Speed => Settings.BattleSpeed;
        public const float SlowFactor = 0.3f, Step = 1f / 60f;
        /// <summary>划线时战场放慢到多少。</summary>
        public const float DrawSlowFactor = 0.35f;
        float drawSlow = 1f;
        float TimeScale => Speed * (Slow ? SlowFactor : 1f) * drawSlow * (tutorial && tutorial.SlowBattle ? 0.4f : 1f);

        public Phase Now { get; private set; } = Phase.Cover;
        public Battle Battle { get; private set; }
        public AI Brain { get; private set; }
        public BattleCommand Command { get; private set; }
        public bool Paused, Slow;
        float acc, fightTime;
        int foeStart, meStart;

        void Start()
        {
            // 编辑器里摆着看的那套两军预览（ArmyPreview）不进游戏
            var preview = board ? board.Find("Armies_Preview") : null;
            if (preview) preview.gameObject.SetActive(false);
            standard = fieldStandards.Where(g => g && g.name.ToLowerInvariant().Contains("kingdom")).Select(g => g.transform).FirstOrDefault();
            if (mr && mr.cam) mr.cam.enabled = false;   // 头显相机等设备起来再开（Update），编辑器里用 SeatCamera
            Listen(false);
            if (menu) menu.OnBegin += () =>
            {
                if (Now != Phase.Cover) return;
                // 设置里点了“重新定位”：忘掉沙盘的位置，这一局开头重新走“手掌按桌面 → 调整 → 放好了”
                if (menu.TakeReplace()) { if (tray) tray.Unplace(); setupDone = false; placedCue = false; Debug.Log("[WT-IN] replace-table"); }
                Now = Phase.Place; if (tutorial) tutorial.Begin(mr && mr.Active);
            };
            if (menu) menu.sound = sound;
            if (result) result.sound = sound;
            if (pause)
            {
                pause.sound = sound;
                pause.OnToggle += open => Paused = open;
                pause.OnPick += a => { Paused = false; if (a == PauseMenu.Action.Camp) ShowCover(); };
            }
            if (result) result.OnPick += a =>
            {
                result.Hide();
                if (a == ResultBoard.Action.Again) { view.Clear(); Begin(); }
                else ShowCover();
            };
            ShowCover();
        }

        /// <summary>同一时间只留一个 AudioListener：头显起来后用头显相机，编辑器里用座位预览相机（3D 战斗声的方位跟着它）。</summary>
        void Listen(bool headset)
        {
            var xl = mr && mr.cam ? mr.cam.GetComponent<AudioListener>() : null;
            var pl = previewCamera ? previewCamera.GetComponent<AudioListener>() : null;
            if (xl) xl.enabled = headset;
            if (pl) pl.enabled = !headset;
        }

        public void ShowCover()
        {
            Now = Phase.Cover;
            view.Clear();
            if (sound) sound.StopBattle();
            if (field) field.Clear();
            if (result) result.Hide();
            if (tutorial) tutorial.Stop();
            if (pause) { pause.Close(); pause.Available = false; }
            Paused = false;
            Battle = null; Command = null;
            if (cover) cover.SetActive(true);
            foreach (var s in fieldStandards) if (s) s.SetActive(false);
            // 封面不出现游戏场景（10-31 用户）：沙盘藏起来，开局才出现
            if (board) board.gameObject.SetActive(false);
        }

        /// <summary>开局：沙盘真实地形 + 原型默认布阵方案，进入布阵阶段。</summary>
        public void Begin()
        {
            ViewClear.Board = board;
            int seed = Random.Range(1, 1 << 20);
            Debug.Log($"[WarGame] 开局 种子 {seed}");
            var terrain = new BoardTerrain(40);
            var plan = Deploy.DefaultPlan((Scenario.DefaultArmies.Team0, Scenario.DefaultArmies.Team1), terrain, 34);
            var (b, _) = Scenario.SetupBattle(Scenario.DefaultArmies, null, seed, 34, double.PositiveInfinity, "open", "xs", plan, terrain);
            b.Deploying = true;
            Battle = b;
            Brain = new AI(b);
            Command = new BattleCommand(b, Brain, plan.Blocks, () => { Battle.Deploying = false; Now = Phase.Fight; fightTime = 0; if (sound) sound.Cue("boom"); });
            if (signs) signs.SyncLegend();
            if (field) { field.Clear(); Command.Ordered += field.OnOrder; }
            if (sound) { sound.StopBattle(); Command.Ordered += (c, _) => sound.Order(c); }
            if (tutorial) { Command.Feedback += tutorial.OnAction; Command.Ordered += (c, _) => tutorial.OnAction(c); }
            Command.Feedback += k =>
            {
                if (k == "deny" && field) field.OnDeny();
                if (!sound) return;
                if (k != "fight") sound.Cue(k == "deselect" ? "select" : k);   // 开战的鼓在开战回调里，不管从哪条路径开战都会响
            };
            if (result) result.Hide();
            view.Build(b, board);
            meStart = b.Agents.Count(a => a.Team == 0);
            foeStart = b.Agents.Count(a => a.Team == 1);
            if (cover) cover.SetActive(false);
            board.gameObject.SetActive(true);
            foreach (var s in fieldStandards) if (s) s.SetActive(true);
            Now = Phase.Deploy;
            acc = 0; drawSlow = 1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (mr && mr.cam && !mr.cam.enabled && UnityEngine.XR.XRSettings.isDeviceActive)
            {
                mr.cam.enabled = true;
                if (previewCamera) previewCamera.gameObject.SetActive(false);
                Listen(true);
            }
            // 兵符合拢后：沙盘已经摆好（编辑器里 / 重开一局）就直接开局，否则等玩家按掌摆好
            if (Now == Phase.Place)
            {
                if (tray) tray.Armed = !tray.Placed;
                if (field && tray) field.Palm(tray.PressCenter, tray.PressProgress);   // 手掌按在桌上：一圈金环随进度画满
                // 11-10 用户：开局要强制先把沙盘挪好再继续 → 沙盘摆出来以后进入“调整”：单手捏把手就能拖（自动正对玩家），
                // 双手可转 / 缩放；摆好了手指点一下木框正中的“放好了”，两军才落到沙盘上。头显外（编辑器）直接开局
                bool headsetSetup = tray && mr && mr.Active && !setupDone;
                if (tray && tray.Placed && !placedCue) { placedCue = true; if (sound && mr && mr.Active) sound.Cue("place"); }
                if (!tray || tray.Placed)
                {
                    if (field) field.Palm(null, 0f);
                    if (headsetSetup)
                    {
                        tray.EasyMove = true;
                        if (signs) signs.ShowReady = true;
                        if (signs && signs.ReadyPressed && !tray.Grabbing) { setupDone = true; Debug.Log("[WT-IN] setup-ready"); if (sound) sound.Cue("stamp"); }
                    }
                    if (!headsetSetup || setupDone)
                    {
                        if (tray) { tray.Armed = false; tray.EasyMove = false; }
                        if (signs) signs.ShowReady = false;
                        Begin();
                    }
                }
            }
            if (sound) sound.Music(Now switch { Phase.Deploy => GameAudio.Track.Deploy, Phase.Fight => GameAudio.Track.Battle, Phase.Over => GameAudio.Track.End, _ => GameAudio.Track.Title });
            if (tutorial && field) field.focus = tutorial.Tick(this);   // 交战放战斗曲，其余阶段放安静曲（换曲时交叉淡入淡出）
            if (pause) pause.Available = Now == Phase.Deploy || Now == Phase.Fight;
            // 11-06 自查：暂停菜单打开时旗牌、去向箭头、说明牌还在后面亮着，画面乱 → 暂停时一并收起（把手照常显示）
            if (signs) signs.Tick(Now == Phase.Over || Now == Phase.Cover || Now == Phase.Place || Paused ? null : Command, this, tray, (Now == Phase.Deploy || Now == Phase.Fight) && !Paused);
            // 暂停键 / 暂停菜单拿走这一捏时，不交给战场（不会误选部队、误划线）；暂停时战场也不响应
            bool blocked = pause && (pause.Captures || pause.Open);
            if (blocked) { if (Command != null) Command.Pinch.Cancel(); }
            else if (mr && mr.Active) HeadsetInput(); else PreviewInput();
            // 11-10 用户：手势识别不好、常误触 → 去掉所有指挥手势，改成右侧令架上的三支令牌（OrderRack），扔到沙盘上 = 对全军下令。
            // 放在输入分支外：编辑器预览也显示令架，暂停键拿走这一捏时令牌也照常归位（拿令牌只在交战中、没在抓沙盘时）
            if (orders && !ordersHooked) { orders.OnOrder += OnOrderToken; ordersHooked = true; }
            if (orders) orders.Tick((Now == Phase.Deploy || Now == Phase.Fight) && !Paused, Now == Phase.Fight && !blocked && !(tray && tray.Grabbing));
            if (Battle == null) return;
            var events = new List<BattleEvent>();
            if (!Paused)
            {
                // 划线时战场自动放慢（松手恢复）：不用赶时间，也不用另开慢动作
                // 11-08：只在真的拖开 / 划出线时才放慢（光捏住不放慢——手指蜷着被误判成一直捏时，战场会一直慢放）
                drawSlow = Mathf.MoveTowards(drawSlow, Command.Pinch.DragPreview.HasValue || Command.PreviewLine.HasValue ? DrawSlowFactor : 1f, dt * 3f);
                acc += dt * TimeScale;
                int steps = Mathf.Min(8, Mathf.FloorToInt(acc / Step));   // 卡顿时最多追 8 步，免得雪崩
                acc -= steps * Step;
                if (acc > Step * 8) acc = 0;
                if (steps > 0) events.AddRange(Run.RunSteps(Battle, Brain, null, steps, Step, null).Events);
                if (Now == Phase.Fight) fightTime += steps * Step;
            }
            Command.Events(events);
            view.Sync(Paused ? 0 : dt * TimeScale, events);
            if (sound) sound.Battle(events, Battle, SimToWorld);
            if (hitFx) hitFx.Play(events, Battle, SimToWorld, board.lossyScale.x / BoardMap.BoardScale);
            if (field) field.Tick(Now == Phase.Over ? null : Command, Now == Phase.Deploy, standard);
            if (Battle.Winner.HasValue && Now == Phase.Fight)   // 11-10：没有练习局了，第一局就分胜负
            {
                Now = Phase.Over;
                if (tutorial) tutorial.Finish();   // 11-14：打完一局 = 新手教学结束，以后不再提示（封面“教学”可以重来）
                if (result)
                {
                    var r = Result();
                    if (tutorial) tutorial.Stop();
                    result.board = board;
                    result.Show(r.won, r.stars, r.survivors, r.total, r.routed, foeStart);
                    if (sound) sound.Cue(r.won ? "boom" : "tom");
                }
            }
        }

        // ------------------------------------------------------------ 头显：眼睛选，手指捏

        /// <summary>看战旗的容差（弧度）：有眼动 0.05（约 3°），只有头部朝向时放宽到 0.14（原型设计第八节）。</summary>
        const float GazeTolEye = 0.12f, GazeTolHead = 0.18f;   // 11-08 用户：眼睛大致看着这支部队就行 → 约 7°（打分取最准的，挨着的两支不会串）
        /// <summary>划线拖动的增益上限：坐着也能把线拖到沙盘远端，不用伸长胳膊。</summary>
        const float MaxDragGain = 4f;
        bool xrPinch;
        Vector3? dragStart; Vector3 handStart; float dragGain;

        // ── 11-09 实物令牌（别人试玩：看不懂要拖什么、眼动不准捏不上）：我军就在玩家这边，伸手捏住令牌就拿起来，不用眼睛。
        //    放：落点在手的正下方；手伸出 ReachComfort 以外，落点按 ReachGain 倍往前延伸（坐着也能放到对面）。眼睛看着捏照旧可用。
        const float ReachGrab = 0.05f, ReachComfort = 0.45f, ReachGain = 3f;
        bool setupDone, placedCue;
        /// <summary>沙盘摆出来了、还在“调整位置”（还没点“放好了”）。</summary>
        public bool SetupPending => Now == Phase.Place && tray && tray.Placed && !setupDone && mr && mr.Active;
        bool directGrab;
        PinchTarget? reachPrev;
        float reachSince;
        // 11-09 防误触
        const float StdHold = 0.7f, ReachDwell = 0.25f;   // 11-09：手伸过去途中一蜷不算拿，要在令牌上停 0.25 秒
        float stdStart = -1f; bool stdConsumed, stdRawWas; int dragHand = -1;
        LineRenderer tether;
        FingerCue fingerCue;
        float handsSeenT;
        /// <summary>两只手都看不到多久了（秒），提示“把手抬到眼前”。</summary>
        public float HandsLostFor { get; private set; }
        /// <summary>这一帧哪只手够着了可以拿的令牌（0 左 1 右 −1 没有）：手指提示圈变亮。</summary>
        public int ReachHand { get; private set; } = -1;

        /// <summary>
        /// 虚拟指尖：手离头水平超过 ReachComfort 后，多出来的部分 × ReachGain 往前推（11-09 实测：坐着时弓手令牌离眼 0.55 m、骑兵 0.70 m，
        /// 伸直胳膊也只够得着最近的一支）。手在近处时虚拟指尖就是真指尖。extended = 推出去了多远（米）。
        /// </summary>
        public Vector3 VirtualTip(Vector3 p, out float extended)
        {
            extended = 0f;
            if (!mr) return p;
            var flat = p - mr.Head; flat.y = 0; float rr = flat.magnitude;
            if (rr <= ReachComfort || rr < 1e-3f) return p;
            extended = (rr - ReachComfort) * (ReachGain - 1f);
            return p + flat / rr * extended;
        }

        /// <summary>虚拟指尖到目标点的距离，按真手的尺度算（推出去时沿深度方向的差除以增益，免得远处的令牌要捏得极准）。</summary>
        float ReachDist(Vector3 vp, Vector3 target, float extended)
        {
            var d = target - vp;
            if (extended > 0f && mr)
            {
                var dir = vp - mr.Head; dir.y = 0; dir.Normalize();
                float radial = Vector3.Dot(d, dir); d += dir * (radial / ReachGain - radial);
            }
            return d.magnitude;
        }

        /// <summary>手（捏合点）伸到了哪支我军的令牌上 / 部队正上方 / 帅旗旁（布阵时）；dist 越小越准。</summary>
        PinchTarget? ReachTarget(in HandState h, out float dist)
        {
            dist = 9f;
            if (!h.tracked || Command == null || Battle == null) return null;
            var p = VirtualTip(h.pinchPoint, out float ext); PinchTarget? best = null;
            float k = board.lossyScale.x / BoardMap.BoardScale;
            foreach (var f in Command.Flags())
            {
                if (f.team != BattleCommand.Me) continue;
                float d = 9f;
                if (signs && signs.OwnTile(f.group, out var tp, out var th)) d = Mathf.Max(0f, ReachDist(p, tp, ext) - th * 0.3f);
                // 手在这支部队脚下光环的正上方、离地 15 cm 以内：也算拿它
                var c = SimToWorld(f.x, Battle.Terrain!.Height(f.x, f.z), f.z);
                var (_, _, rr) = Command.Ring(f.group, f.team);
                var flat = p - c; float up = flat.y; flat.y = 0;
                float rW = (float)rr * BoardMap.Yard * board.lossyScale.x;
                if (up > -0.01f && up < 0.15f * k && flat.magnitude < rW) d = Mathf.Min(d, flat.magnitude / Mathf.Max(1e-3f, rW) * ReachGrab * 0.9f);
                if (d < ReachGrab * k && d < dist) { dist = d; best = new PinchTarget(TargetKind.Own, ToGrp(f.group)); }
            }
            if (standard && Now == Phase.Deploy)
                foreach (var rd in standard.GetComponentsInChildren<Renderer>())
                {
                    var b = rd.bounds; b.Expand(0.06f);
                    if (b.Contains(p) && 0.02f < dist) { dist = 0.02f; best = new PinchTarget(TargetKind.Standard); }
                }
            return best;
        }

        /// <summary>拿着令牌时的落点：手的正下方；手伸出 ReachComfort（离头水平距离）以外，多出来的部分 × ReachGain 往前推。</summary>
        Vector3? DirectDrop(Vector3 hp)
        {
            var q = VirtualTip(hp, out _);
            var down = new Ray(new Vector3(q.x, hp.y + 0.8f, q.z), Vector3.down);
            return view.terrainCollider && view.terrainCollider.Raycast(down, out var hit, 3f) ? hit.point : null;
        }

        /// <summary>拿着令牌时，手指到令牌之间一根细金线（落点被延伸到远处时，玩家知道“令牌还在我手上”）。</summary>
        void Tether(bool on, Vector3 a, Vector3 b)
        {
            if (!tether && on && gestureFlash)
            {
                tether = new GameObject("TokenTether").AddComponent<LineRenderer>(); tether.transform.SetParent(transform, false);
                tether.sharedMaterial = gestureFlash.quad.sharedMaterial; tether.useWorldSpace = true; tether.positionCount = 2;
                tether.widthMultiplier = 0.0018f; tether.numCapVertices = 2; tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mp = new MaterialPropertyBlock(); mp.SetTexture("_BaseMap", Texture2D.whiteTexture); mp.SetColor("_BaseColor", new Color(1f, 0.82f, 0.45f, 0.8f)); tether.SetPropertyBlock(mp);
            }
            if (!tether) return;
            on &= (a - b).sqrMagnitude > 0.04f * 0.04f;
            if (tether.enabled != on) tether.enabled = on;
            if (on) { tether.SetPosition(0, a); tether.SetPosition(1, b); }
        }
        int pinchHand = -1;

        /// <summary>
        /// 头显输入：视线（眼动射线，没有就用头部朝向）选战旗，单手捏合下令。
        /// 划线 = 捏合那一刻视线在沙盘上的落点为起点，之后线跟着手走（看哪捏哪、手动拖；增益 = 落点距离 / 手的距离，手在落点上时就是直接拖）。
        /// 双手都捏在托盘上是抓托盘（TrayRig），这一笔作废。
        /// </summary>
        void HeadsetInput()
        {
            var gaze = mr.Gaze;
            var l = mr.Left; var r = mr.Right;
            ViewClear.Board = board;
            if (l.tracked || r.tracked || handsSeenT == 0f) handsSeenT = Time.time;
            HandsLostFor = Time.time - handsSeenT;
            bool grabbing = tray && tray.Grabbing;
            // 捏的那只手：11-06 真机诊断——系统从不报捏合（sys=False），全靠指尖距离；玩家闲着的那只手指头常自然蜷着（拇指食指 0.5–2 cm），
            // 被判成一直在捏。原来“先捏上的那只说了算、松开前不换”，于是另一只手怎么捏都没反应 → 改成“刚捏下去的那只说了算”。
            // 换手时这一帧强制当作松开：原来那只手一直“捏着”，不松一下战场收不到新的一次捏
            bool switched = false;
            if (mr.PinchDownR && pinchHand != 1) { switched = pinchHand >= 0; pinchHand = 1; }
            else if (mr.PinchDownL && pinchHand != 0) { switched = pinchHand >= 0; pinchHand = 0; }
            if (pinchHand == 0 && !l.pinch || pinchHand == 1 && !r.pinch) pinchHand = -1;
            var hand = pinchHand == 1 ? r : l;
            // 捏的起点在木框附近 = 想抓托盘（等另一只手），这一捏不交给战场，免得顺手选中 / 划线（11-04 用户：操作时不要误触）
            bool raw = pinchHand >= 0;
            // 11-06：拖沙盘只认把手。把手挪到正前方后正好是手自然捏的位置 → 只有“捏在把手上、而且眼睛没看着任何部队 / 帅旗”才算想抓沙盘，
            // 看着部队捏照常交给战场（真抓沙盘时第二只手一捏上，Grabbing 会把这一笔作废）
            // 说明面板（手势 / 兵法 / 收起）：看着它捏一下就切页 / 收起 / 展开，这一捏不交给战场（免得透过面板捏到后面的部队）
            bool onLegend = false;   // 11-10：说明面板挪到手边，只用手指点（不再看着捏）
            // 11-07 真机日志：把手挪到眼前木框正中后，正是手自然搁着、捏合的地方——单手一捏就被当成“想抓沙盘”吞掉。
            // 现在只有两只手都靠在各自的把手上才算要拖沙盘（TrayRig 也只认双手），单手捏到把手附近照常交给战场
            bool onHandles = tray && tray.Placed && tray.HandleHot(0) && tray.HandleHot(1);
            bool onOrders = orders && orders.Captures;   // 手靠着 / 拿着令牌：这一捏归令架
            if (raw && (!rawPinchWas || switched)) { rimPinch = onLegend || onHandles || onOrders; rimWhy = onLegend ? "legend" : onHandles ? "handles" : onOrders ? "orders" : "-"; }
            if (!raw) rimPinch = false;
            rawPinchWas = raw;
            bool pinching = raw && !switched && !rimPinch && !grabbing && (!tray || tray.Placed);
            // 11-11 用户：把部队拖到某处松手后，收手时部队又被拖走、换了位置——收手时捏合识别抖一下，被当成又捏了一次（手正好经过令牌）。
            // ①松手后锁一下：拖完 0.7 s、点完 0.35 s 内不认新的捏，而且新的捏必须是锁过之后才开始的（锁着时一直“捏着”的不算）；
            // ②捏要稳住 0.1 s 才交给战场（一两帧的抖动不算）
            if (!pinching)
            {
                if (pinchGate && Command != null) lockUntil = Time.time + (Command.Pinch.Drawing ? 0.7f : 0.35f);
                pinchGate = false; pinchSince = -1f;
            }
            else if (!pinchGate)
            {
                if (pinchSince < 0f) pinchSince = Time.time;
                if (pinchSince >= lockUntil && Time.time - pinchSince >= 0.1f) pinchGate = true;
            }
            pinching = pinchGate;

            // 封面的交互（看着菜单项捏一下）在 MenuCover 里；摆沙盘在 TrayRig 里
            if (Now == Phase.Cover || Now == Phase.Place || Command == null) { ReachHand = -1; Tether(false, default, default); if (fingerCue) fingerCue.Tick(false, -1, this); return; }
            if (grabbing) Command.Pinch.Cancel();

            // 11-09：先看手伸到了哪块令牌上（不靠眼睛）；正捏着时只看捏的那只手
            PinchTarget? reach = null; int rh = -1; float rdist = 9f;
            if (!Paused && !grabbing && (Now == Phase.Deploy || Now == Phase.Fight))
                for (int i = 0; i < 2; i++)
                {
                    if (pinchHand >= 0 && pinchHand != i) continue;
                    var t0 = ReachTarget(i == 0 ? l : r, out float d0);
                    if (t0.HasValue && d0 < rdist) { rdist = d0; reach = t0; rh = i; }
                }
            ReachHand = rh;
            // 手刚碰到一块令牌：轻响一声（= 可以捏了）
            if (reach.HasValue && !raw && (!reachPrev.HasValue || reachPrev.Value.kind != reach.Value.kind || reachPrev.Value.group != reach.Value.group) && sound) sound.Cue("hover");
            if (!raw && !SameTarget(reach, reachPrev)) reachSince = Time.time;
            reachPrev = raw ? reachPrev : reach;
            if (!fingerCue && gestureFlash)
            {
                fingerCue = new GameObject("FingerCue").AddComponent<FingerCue>(); fingerCue.transform.SetParent(transform, false);
                fingerCue.mr = mr; fingerCue.mat = gestureFlash.quad.sharedMaterial;
            }
            if (orders && orders.HoverHand >= 0) rh = orders.HoverHand;   // 靠着令牌时手指圈也变金
            if (fingerCue) fingerCue.Tick((Now == Phase.Deploy || Now == Phase.Fight) && !Paused && !grabbing, rh, this);

            // 视线落在什么上：短暂移开（眨眼、眼跳）0.3 秒内仍算看着刚才那个，捏的时候不会扑空
            // 11-08 用户：别靠眼睛盯——眼睛大致看着那片，手捏住旗牌就能拖。像隔空捏东西：从眼睛连向捏合手指的这条线碰到哪块旗牌，就是哪支部队。
            // 只在手大致处在视线方向上（和视线夹角 < 25°）时才用这条线：手搁在桌上、不在视野里时仍按眼睛看的方向
            var aimHand = pinchHand == 1 ? r : pinchHand == 0 ? l : (r.tracked ? r : l);
            Ray? handRay = null;
            if (aimHand.tracked)
            {
                var hd = (aimHand.pinchPoint - mr.Head).normalized;
                if (Vector3.Angle(hd, gaze.direction) < 25f) handRay = new Ray(mr.Head, hd);
            }
            // 11-09 用户：只留“拖”，去掉以前的“看着捏一下 = 选中 / 进攻、选中后拖线”——眼睛一扫、手指一蜷就误触。
            // 战场上的目标只认手：伸手够着的我军令牌（拖走）或帅旗（按住开战）；眼睛、手的系统射线都不再选目标
            var now = reach;
            if (now.HasValue) { hoverT = now; hoverSeen = Time.time; }
            else if (Time.time - hoverSeen > HoverGrace) hoverT = null;
            if (raw && (!diagPinchWas || switched))
                Debug.Log($"[WT-IN] game-pinch phase={Now} hand={pinchHand} rim={rimPinch}/{rimWhy} grab={grabbing} target={(hoverT.HasValue ? hoverT.Value.kind + "/" + hoverT.Value.group : "none")} eye={mr.EyeTracked} sel={Command.Selected.Count}");
            // 11-07：捏到了但眼睛没看着任何部队 / 帅旗、也没选中部队（这一捏什么都不会发生）→ 轻响一声，玩家知道“手势收到了，要看着目标”
            if (raw && (!diagPinchWas || switched) && !rimPinch && !grabbing && !hoverT.HasValue && Command.Selected.Count == 0 && sound) sound.Cue("hover");
            diagPinchWas = raw;

            // 防误触 ①：帅旗就在手边，手搁着一捏就开战了 → 布阵时要捏住帅旗停 StdHold 秒（帅旗脚下金弧画满）才开战，中途松手作废
            bool stdTarget = Now == Phase.Deploy && hoverT is { kind: TargetKind.Standard } && (!tutorial || tutorial.AllowFight);
            if (raw && (!stdRawWas || switched))
            {
                stdStart = stdTarget && !rimPinch && !grabbing ? Time.time : -1f;
                // 教学还没到开战这一步：捏帅旗整笔不响应（不开战、也不交给战场）
                stdConsumed = Now == Phase.Deploy && hoverT is { kind: TargetKind.Standard } && tutorial && !tutorial.AllowFight;
            }
            if (!raw) { stdStart = -1f; stdConsumed = false; }
            stdRawWas = raw;
            if (stdStart >= 0f && !stdTarget && Time.time - hoverSeen > HoverGrace) stdStart = -1f;   // 手 / 眼离开帅旗：作废
            if (stdStart >= 0f)
            {
                float k = (Time.time - stdStart) / StdHold;
                if (k >= 1f) { stdStart = -1f; stdConsumed = true; Command.Exec("fight"); Debug.Log("[WT-IN] standard-hold fight"); }
                if (field) field.standardHold = Mathf.Clamp01(k);
            }
            else if (field) field.standardHold = 0f;
            if (stdStart >= 0f || stdConsumed) pinching = false;   // 这一捏不交给战场

            // 防误触 ②：拖着令牌时手丢了追踪（转头、手出视野）——作废这一笔，不把部队扔到半路
            if (pinching) dragHand = pinchHand;
            else if (xrPinch && dragHand >= 0 && !(dragHand == 0 ? l : r).tracked) { Command.Pinch.Cancel(); Debug.Log("[WT-IN] drag-cancel lost"); }
            if (!pinching && !xrPinch) dragHand = -1;

            P2? point = null;
            if (pinching && !xrPinch)
            {
                // 防误触 ③：手扫过令牌时顺手一捏不算拿——手要在这块令牌上停够 ReachDwell 秒
                directGrab = reach.HasValue && rh == pinchHand && Time.time - reachSince >= ReachDwell;
                if (reach.HasValue && !directGrab) { hoverT = null; }
                var startRay = new Ray(mr.Head, (hand.pinchPoint - mr.Head).normalized);
                dragStart = view.terrainCollider && view.terrainCollider.Raycast(startRay, out var hit, 10f) ? hit.point
                          : view.terrainCollider && view.terrainCollider.Raycast(gaze, out hit, 10f) ? hit.point : null;
                handStart = hand.pinchPoint;
                if (directGrab) dragStart = DirectDrop(hand.pinchPoint) ?? dragStart;
                dragGain = dragStart.HasValue ? Mathf.Clamp(Vector3.Distance(dragStart.Value, mr.Head) / Mathf.Max(0.05f, Vector3.Distance(handStart, mr.Head)), 1f, MaxDragGain) : 1f;
            }
            if (pinching && dragStart.HasValue)
            {
                // 手的位置：平滑 60 ms 去手抖，并滞后 80 ms——松手那一下手指张开会让捏点跳，线的终点取松手前的位置
                handHist.Enqueue((Time.time, hand.pinchPoint));
                while (handHist.Count > 1 && Time.time - handHist.Peek().t > ReleaseLag) handHist.Dequeue();
                var lagged = handHist.Peek().p;
                handSmooth = xrPinch ? Vector3.Lerp(handSmooth, lagged, 1f - Mathf.Exp(-Time.deltaTime / 0.06f)) : lagged;
                // 11-08：落点 = “眼睛 → 手指”这条线和地面的交点，旗牌始终压在手指下面（隔空拖）；线打不到沙盘时才按手的位移 × 增益推算
                // 拖部队时旗牌浮在落点上方约 10 码：把这条线整体往下平移这么高再求交点，旗牌就正好压在手指下（划线不平移）
                float lift = Command.Pinch.Holding.HasValue ? board.lossyScale.y * BoardMap.Yard * 10f : 0f;
                Vector3? w;
                if (directGrab) w = DirectDrop(handSmooth);   // 伸手拿的：落在手的正下方（远处按延伸）；落到沙盘外就停在上一个落点
                else
                {
                    var hr = new Ray(mr.Head - Vector3.up * lift, (handSmooth - mr.Head).normalized);
                    w = view.terrainCollider && view.terrainCollider.Raycast(hr, out var hh, 10f) ? hh.point : dragStart.Value + (handSmooth - handStart) * dragGain;
                }
                if (w.HasValue) { var lp = board.InverseTransformPoint(w.Value) / BoardMap.Yard; point = new P2(-lp.x, lp.z); }
            }
            if (!pinching) { dragStart = null; handHist.Clear(); directGrab = false; }
            xrPinch = pinching;
            Command.Update(Time.deltaTime, hoverT, pinching, point);
            // 拿着令牌：手指到令牌之间一根细金线
            var held = Command.Pinch.Holding;
            if (pinching && directGrab && held.HasValue)
            {
                var e = held.Value.end;
                Tether(true, hand.pinchPoint, SimToWorld(e.x, Battle.Terrain!.Height(e.x, e.z) + 9, e.z));
            }
            else Tether(false, default, default);

        }

        /// <summary>令牌落地：对全军下这道令（临时全选，用完还原），眼前弹“全军 · 冲锋”。</summary>
        void OnOrderToken(OrderRack.Order o)
        {
            if (Command == null || Now != Phase.Fight) return;
            var before = Command.Selected.ToList();
            Command.SelectAll();
            Command.Exec(o == OrderRack.Order.Hold ? "hold" : o == OrderRack.Order.Retreat ? "retreat" : "charge");
            Command.Selected.Clear(); Command.Selected.AddRange(before);
            var g = o == OrderRack.Order.Hold ? Gesture.Hold : o == OrderRack.Order.Retreat ? Gesture.Retreat : Gesture.Charge;
            LastGesture = (g, Time.time);
            if (gestureFlash) gestureFlash.Show(g, true, "all");
            Debug.Log($"[WT-IN] order {o}");
        }

        /// <summary>某点正下方落在沙盘地形上的位置（没落在沙盘上返回 null）。令牌扔出去时用。</summary>
        public Vector3? DropOnBoard(Vector3 p)
        {
            var down = new Ray(new Vector3(p.x, p.y + 0.8f, p.z), Vector3.down);
            return view.terrainCollider && view.terrainCollider.Raycast(down, out var hit, 3f) ? hit.point : null;
        }

        /// <summary>线段 a→b 是否穿过沙盘地形（令牌飞行时逐帧检测落地），hit = 交点。</summary>
        public bool SweepBoard(Vector3 a, Vector3 b, out Vector3 hit)
        {
            hit = b; var d = b - a; float len = d.magnitude;
            if (len < 1e-6f || !view.terrainCollider) return false;
            if (view.terrainCollider.Raycast(new Ray(a, d / len), out var h, len)) { hit = h.point; return true; }
            return false;
        }

        [Tooltip("右侧令架（11-10：代替指挥手势）")] public OrderRack orders;
        bool ordersHooked;
        bool rawPinchWas, rimPinch, diagPinchWas;
        bool pinchGate; float pinchSince = -1f, lockUntil;
        string rimWhy = "-";
        /// <summary>最近一次生效的指挥手势和时间（界面层在战旗上闪对应图标）。</summary>
        public (Gesture g, float t) LastGesture { get; private set; } = (Gesture.None, -10f);

        const float HoverGrace = 0.3f, ReleaseLag = 0.08f;
        readonly Queue<(float t, Vector3 p)> handHist = new();
        Vector3 handSmooth;
        PinchTarget? hoverT;
        float hoverSeen;

        /// <summary>
        /// 视线（或鼠标射线）指着的目标：我方帅旗 → 战旗（容差 tol 弧度）→ 部队本身（视线落在沙盘上、在这支部队脚下光环范围里）。
        /// 敌我都算：看着己方部队捏 = 选中，选中后看着敌军捏 = 冲向它。
        /// </summary>
        PinchTarget? AimTarget(Ray ray, float tol)
        {
            if (standard && LookingAt(standard, ray)) return new PinchTarget(TargetKind.Standard);
            // 11-07 真机日志：交战时 68 次捏合 67 次落在自己的步兵上——原来按“我军在前”逐个查，视线擦到我军旗牌就直接返回，
            // 而我军步兵正好挡在玩家和敌军之间，敌军永远轮不到。现在所有候选一起比分（越小越准），取最小的：
            //   旗牌命中：0–0.5（按离旗牌中心多远）；视线方向：角度 / tol；脚下光环：0.9 × 离圈心多远。
            // 已选中部队时敌军分数 × 0.55（选中之后多半是要打谁），免得又捏回自己那支把选中取消掉。
            bool sel = Command.Selected.Count > 0;
            PinchTarget? best = null; float score = 1f;
            Vector3? ground = view.terrainCollider && view.terrainCollider.Raycast(ray, out var hit, 10f) ? hit.point : null;
            foreach (var f in Command.Flags())
            {
                var kind = f.team == BattleCommand.Me ? TargetKind.Own : TargetKind.Foe;
                float bias = sel && kind == TargetKind.Foe ? 0.55f : 1f;
                float sc = 1f;
                if (signs && signs.TileScore(f.team, f.group, ray, out float ts)) sc = Mathf.Min(sc, ts * 0.5f);
                var to = SimToWorld(f.x, Battle.Terrain!.Height(f.x, f.z) + 5, f.z) - ray.origin;
                sc = Mathf.Min(sc, Vector3.Angle(ray.direction, to) * Mathf.Deg2Rad / tol);
                if (ground.HasValue)
                {
                    var (rx, rz, rr) = Command.Ring(f.group, f.team);
                    var lp = board.InverseTransformPoint(ground.Value) / BoardMap.Yard;
                    float d = (float)(System.Math.Sqrt((-lp.x - rx) * (-lp.x - rx) + (lp.z - rz) * (lp.z - rz)) / rr);
                    if (d < 1f) sc = Mathf.Min(sc, d * 0.9f);
                }
                sc *= bias;
                if (sc < score) { score = sc; best = new PinchTarget(kind, ToGrp(f.group)); }
            }
            return best;
        }

        static bool SameTarget(PinchTarget? a, PinchTarget? b) => a.HasValue == b.HasValue && (!a.HasValue || a.Value.kind == b.Value.kind && a.Value.group == b.Value.group);

        static Grp ToGrp(string g) => g == Groups.Arch ? Grp.Arch : g == Groups.Cav ? Grp.Cav : g == Groups.Ha ? Grp.Ha : Grp.Inf;

        static bool LookingAt(Transform t, Ray gaze)
        {
            if (!t) return false;
            foreach (var rd in t.GetComponentsInChildren<Renderer>())
                if (rd.bounds.IntersectRay(gaze)) return true;
            return false;
        }

        // ------------------------------------------------------------ 电脑试玩（编辑器里测玩法用，头显输入之后接）

        static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        static Vector3 MousePos => Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;

        /// <summary>模拟层坐标（码，原型坐标）↔ 沙盘世界坐标。</summary>
        public Vector3 SimToWorld(double x, double y, double z) => board.TransformPoint(new Vector3(-(float)x, (float)y, (float)z) * BoardMap.Yard);

        void PreviewInput()
        {
            var cam = previewCamera ? previewCamera : Camera.main;
            if (Now == Phase.Cover || Now == Phase.Place) return;   // 封面：Enter 开始、L 切语言（MenuCover）
            if (Command == null || !cam) return;
            // 视线 = 鼠标射线（鼠标很准，容差收窄到约 1.7°）
            var mray = cam.ScreenPointToRay(MousePos);
            var aim = AimTarget(mray, 0.03f);
            P2? point = null;
            var ray = cam.ScreenPointToRay(MousePos);
            if (view.terrainCollider && view.terrainCollider.Raycast(ray, out var hit, 10f))
            {
                var l = board.InverseTransformPoint(hit.point) / BoardMap.Yard;
                point = new P2(-l.x, l.z);
            }
            Command.Update(Time.deltaTime, aim, (Mouse.current != null && Mouse.current.leftButton.isPressed), point);

            // 快捷键（和原型电脑预览一致）
            if (Pressed(Key.Digit1)) Command.Exec("sel:inf");
            if (Pressed(Key.Digit2)) Command.Exec("sel:arch");
            if (Pressed(Key.Digit3)) Command.Exec("sel:cav");
            if (Pressed(Key.Digit4)) Command.Exec("sel:ha");
            if (Pressed(Key.Digit0)) Command.Exec("all");
            if (Pressed(Key.Space)) Command.Exec("fight");
            if (Pressed(Key.C)) Command.Exec("charge");
            if (Pressed(Key.V)) Command.Exec("hold");
            if (Pressed(Key.F)) Command.Exec("follow");
            if (Pressed(Key.W)) Command.Exec("forward");
            if (Pressed(Key.S)) Command.Exec("back");
            if (Pressed(Key.R)) Command.Exec("retreat");
            if (Pressed(Key.E)) Command.Exec("face");
            if (Pressed(Key.A)) Command.Exec("auto");
            if (Pressed(Key.LeftBracket)) Command.Exec("tighter");
            if (Pressed(Key.RightBracket)) Command.Exec("looser");
            if (Pressed(Key.H)) Command.Exec("holdFire");
            if (Pressed(Key.Q)) Command.Exec("arrAuto");
            string[] arr = { "line", "shieldwall", "loose", "circle", "square", "skein", "column" };
            for (int i = 0; i < arr.Length; i++) if (Pressed(Key.F1 + i)) Command.Exec("arr:" + arr[i]);
            if (Pressed(Key.P) && !pause) Paused = !Paused;
            if (Pressed(Key.T)) Slow = !Slow;
            if (Pressed(Key.L)) Loc.Current = Loc.Current == Loc.Lang.Zh ? Loc.Lang.En : Loc.Lang.Zh;
            if (Now == Phase.Over && !result && Pressed(Key.Enter)) { view.Clear(); Begin(); }
        }

        /// <summary>结算（原型 rating：按存活比例给星）。</summary>
        public (bool won, int stars, int survivors, int total, int routed, float time) Result()
        {
            bool won = Battle.Winner == BattleCommand.Me;
            int survivors = Battle.Agents.Count(a => a.Team == 0 && a.Alive && !a.Routed);
            int routed = Battle.Agents.Count(a => a.Team == 1 && (!a.Alive || a.Routed));
            return (won, CommandLogic.Rating(won, survivors, meStart), survivors, meStart, routed, fightTime);
        }

        // 临时屏幕文字（编辑器试玩用）：战旗标签、提示、结算；头显界面在 UI 阶段重做
        void OnGUI()
        {
            var cam = previewCamera ? previewCamera : Camera.main;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true };
            if (Now == Phase.Cover || Now == Phase.Place) return;   // 封面上的菜单是场景里的实物（MenuCover），不画屏幕字
            if (!debugText || (mr && mr.Active)) return;           // 头显界面全是场景实物（FieldUI / ResultBoard），这些屏幕字只给编辑器调试
            if (Command == null || !cam) return;
            foreach (var f in Command.Flags())
            {
                var sp = cam.WorldToScreenPoint(SimToWorld(f.x, Battle.Terrain!.Height(f.x, f.z) + 5, f.z));
                if (sp.z <= 0) continue;
                string col = f.team == 0 ? (f.selected ? "#ffd65a" : f.hover ? "#f6e7b8" : "#7fb2ff") : (f.hover ? "#ffc2b5" : "#ff7a6a");
                GUI.Label(new Rect(sp.x - 80, Screen.height - sp.y - 14, 160, 28), $"<color={col}><b>{Loc.T("glyph." + f.group)} {f.count}</b> {f.order}</color>", st);
            }
            var preview = Command.PreviewLine;
            if (preview.HasValue)
            {
                var p = preview.Value;
                var sp = cam.WorldToScreenPoint(SimToWorld(p.x, Battle.Terrain!.Height(p.x, p.z), p.z));
                GUI.Label(new Rect(sp.x - 60, Screen.height - sp.y - 10, 120, 20), $"<color=#ffd65a>━━ {p.length:0} ━━</color>", st);
            }
            var top = new GUIStyle(st) { fontSize = 18 };
            string phase = Now == Phase.Deploy ? Loc.T("ui.deploy") + "  [Space] " + Loc.T("ui.fight") : "";
            int me = Battle.Agents.Count(a => a.Team == 0 && a.Alive && !a.Routed), foe = Battle.Agents.Count(a => a.Team == 1 && a.Alive && !a.Routed);
            GUI.Label(new Rect(0, 10, Screen.width, 26), $"<color=#7fb2ff>{Loc.T("team.0")} {me}</color>   vs   <color=#ff7a6a>{foe} {Loc.T("team.1")}</color>   {phase}{(Paused ? "  ‖" : "")}{(Slow ? "  ×0.3" : "")}", top);
            if (!string.IsNullOrEmpty(Command.Message)) GUI.Label(new Rect(0, 40, Screen.width, 26), Command.Message, top);
            if (Now == Phase.Over)
            {
                var r = Result();
                GUI.Label(new Rect(0, Screen.height / 2 - 40, Screen.width, 30), $"<size=30><b>{Loc.T(r.won ? "result.victory" : "result.defeat")}</b></size>  {new string('★', r.stars)}", top);
                GUI.Label(new Rect(0, Screen.height / 2, Screen.width, 26), $"{Loc.F("result.survivors", r.survivors, r.total)}   {Loc.F("result.routed", r.routed)}   {Loc.F("result.time", $"{(int)r.time / 60}:{(int)r.time % 60:00}")}   [Enter] {Loc.T("ui.restart")}", top);
            }
        }
    }
}
