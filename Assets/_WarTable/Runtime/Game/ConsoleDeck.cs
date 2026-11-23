using System.Collections.Generic;
using UnityEngine;
using WarTable.Command;
using WarTable.Sim;
using WarTable.XR;
using WarTable;

namespace WarTable.Game
{
    /// <summary>
    /// 指挥台（11-16 用户：别人试玩不会玩，交互要“一看就会”）：沙盘靠玩家一侧边框外面的一排实物，都是伸手就按的立体按钮
    /// （<see cref="PokeButton"/>：食指按 / 在上面捏一下都行，只认手、不看眼睛）。
    ///   · 正中：大按钮。调整沙盘时是“就位”，布阵时是“开战”——按一下就打。
    ///   · 左边：五个小兵人站在小台座上 = 五种兵种风格，按哪个沙盘上的兵就换成哪种；最左是放大开关（真人比例 / 回到沙盘）。
    ///   · 右边：三个军令按钮（冲锋 / 坚守 / 撤退），交战时按哪个就是对全军下哪道令。
    ///
    /// 11-17 用户（头显试过两轮后）：
    ///   · 风格小兵人小一点更精致；底座要和沙盘一样平、不要倾斜，整体往中间靠 → 台面水平，三块托架挨着大按钮排开，位置不再随沙盘宽度展开；
    ///   · 令牌 / 令标还是难捏 → 换成按钮（<see cref="OnOrder"/>）；
    ///   · 放大改成真人比例、保留真实房间 → 整个战场有一百多米，人走不过去：指挥台上多一块小战场图（<see cref="OnMapPick"/>），点哪里就站到哪里。
    /// 放大模式下这一排东西浮在玩家身前腰的高度，钉在原地；人走开 / 转身一会儿后才缩下去、换到新位置、再长出来。
    /// </summary>
    public class ConsoleDeck : MonoBehaviour
    {
        public WarGame game;
        public MRInput mr;
        public Transform editorEye;
        public Camera editorCam;

        [Header("模型（GameSetup 填；没有就用简单几何体顶着）")]
        public GameObject podCenter;
        public GameObject podLeft, podRight, capPrefab, pedestalPrefab, leverPrefab, platePrefab;
        public Material metal, glow, labelMat, plateMat, mapMat, pinMat;
        [Tooltip("各部件自己的材质（带烘焙贴图）；空项用 metal / glow")] public Material podCenterMat, podLeftMat, podRightMat, capMat, switchMat, pedestalMat, ringMat;

        /// <summary>一块字的贴图：a 颜色（帽顶的字带透明度）、n 法线（字是刻进去 / 凸起来的）、m 金属度 + 光滑度（名牌才有）。</summary>
        [System.Serializable] public class Lab { public Texture2D a, n, m; public static implicit operator bool(Lab l) => l != null && l.a; }
        [Header("字（贴图，中 / 英）")]
        public Lab readyZh;
        public Lab readyEn, fightZh, fightEn, immerseZh, immerseEn, tableZh, tableEn;
        public Lab[] eraZh = new Lab[Settings.Eras], eraEn = new Lab[Settings.Eras];
        [Tooltip("军令按钮：帽顶的令徽（中英共用）和前面名牌上的字")] public Lab[] orderCap = new Lab[3];
        public Lab[] orderZh = new Lab[3], orderEn = new Lab[3];
        [Tooltip("各风格的战场俯视图（放大模式的小战场图）")] public Texture2D[] maps = new Texture2D[Settings.Eras];
        [Tooltip("各风格台座上站的兵种")] public string[] eraTroops = { "kd_guard", "kd_guard", "kd_guard", "kd_guard", "kd_hammer" };

        public enum Main { None, Ready, Fight }
        /// <summary>大按钮现在是什么（WarGame 每帧设）。</summary>
        [System.NonSerialized] public Main main;
        /// <summary>风格小人 / 放大开关 / 军令按钮现在能不能按。</summary>
        [System.NonSerialized] public bool eraLive, modeLive, orderLive;
        /// <summary>整排显示。</summary>
        [System.NonSerialized] public bool show;

        public event System.Action OnReady, OnFight, OnMode;
        public event System.Action<int> OnEra, OnOrder;
        /// <summary>在小战场图上点了一处（模拟层坐标，码）。</summary>
        public event System.Action<double, double> OnMapPick;

        /// <summary>大按钮帽顶的位置（教学的手从这里按下去）。</summary>
        public Vector3 MainTop => mainCap ? mainCap.position : transform.position;
        /// <summary>第 i 个军令按钮帽顶的位置（教学用）。</summary>
        public Vector3 OrderTop(int i) => orderCapT[i] ? orderCapT[i].position : transform.position;
        // 旧的令牌架（OrderRack，11-17 起不再创建）还引用这两个接口；留个空壳让它能编译
        public bool Placed => false;
        public (Vector3 pos, Quaternion rot) Slot(int i, float spacing) => (transform.position, Quaternion.identity);
        /// <summary>有手悬在指挥台的按钮上：这一捏归指挥台，不交给战场。</summary>
        public bool Captures { get; private set; }

        // 元件尺寸（米）
        const float MainR = 0.036f, EraR = 0.015f, EraStep = 0.04f, ModeR = 0.019f, OrderR = 0.021f, OrderStep = 0.052f, OrderX0 = 0.036f;
        const float PedScale = 0.72f, FigScale = 1.5f, FigTop = 0.031f;   // 台座模型按 3 cm 直径缩；小人约 3 cm 高
        // 左 / 右托架的原点离正中多远。11-17 晚用户：换风格那块和军令那块挨着大按钮太挤，要拉开一点 → 各往外挪 5.5 cm（原来 0.086 / 0.056，紧挨大按钮底座）
        const float LeftX = 0.141f, RightX = 0.111f;
        // 托架前沿放名牌的斜面（和 Tools/make_deck_models.py 的模型对应）：中心在台面下 1.18 cm、往玩家 4.6 cm，比水平多倾 37°
        const float LipY = -0.0118f, LipZ = -0.046f, LipTilt = 53f;
        // 放大模式：浮在玩家身前，台面朝玩家斜一点好够
        const float FollowDist = 0.42f, FollowDrop = 0.5f, FollowMove = 0.45f, FollowTurn = 70f, FloatTilt = 18f;
        // 小战场图：宽 22 cm（120 : 100），立在大按钮后面，朝玩家仰 50°
        const float MapW = 0.22f, MapH = MapW * BoardMap.HalfZ / BoardMap.HalfX, MapTilt = 50f;

        Transform root, mainPod, mainCap, leftPod, rightPod, modeCap, mapT, pin;
        Renderer mainLabel, modeLabel, mapR;
        readonly Transform[] eraPed = new Transform[Settings.Eras], eraFig = new Transform[Settings.Eras], orderCapT = new Transform[3];
        readonly Renderer[] eraLabel = new Renderer[Settings.Eras], orderLabel = new Renderer[3], orderIcon = new Renderer[3];
        readonly FigureRig[] eraRig = new FigureRig[Settings.Eras];
        readonly Renderer[][] eraRends = new Renderer[Settings.Eras][], pedRends = new Renderer[Settings.Eras][], orderRends = new Renderer[3][];
        Renderer[] mainCapR, modeCapR;
        readonly PokeButton mainBtn = new(), modeBtn = new();
        readonly PokeButton[] eraBtn = new PokeButton[Settings.Eras], orderBtn = new PokeButton[3];
        readonly float[] eraPop = new float[Settings.Eras], orderPress = new float[3], orderFlash = { -9f, -9f, -9f };
        readonly List<Renderer> dots = new();
        MaterialPropertyBlock mpb;
        bool built, mapTouchWas;
        float mainShownT = -1f, mainPress, modePress, mapAwayT = 9f, mapPickT = -9f;
        Main mainWas;
        Vector3 followPos; Vector3 followFwd = Vector3.forward; bool followSet;
        int hoverWas = -99;
        float pickT = -9f; int pickEra = -1;
        static readonly Color[] OrderCol = { new(0.82f, 0.15f, 0.09f), new(0.14f, 0.42f, 0.86f), new(0.9f, 0.58f, 0.1f) };   // 冲锋朱红 / 坚守冰蓝 / 撤退琥珀
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), EmisId = Shader.PropertyToID("_EmissionColor"), MapId = Shader.PropertyToID("_BaseMap"),
                            BumpId = Shader.PropertyToID("_BumpMap"), MaskId = Shader.PropertyToID("_MetallicGlossMap");

        Transform Eye => mr && mr.Active ? mr.cam.transform : editorEye;
        bool Immersive => game.tray && game.tray.Immersive;

        // ------------------------------------------------------------ 搭建

        Transform Node(string name, Transform parent)
        {
            var g = new GameObject(name).transform; g.SetParent(parent, false); return g;
        }

        /// <summary>放一个模型；没有模型就用圆柱顶替（直径 d、高 h，原点在顶面中心）。</summary>
        Transform Part(string name, Transform parent, GameObject prefab, float d, float h, Material m, float scale = 1f)
        {
            var n = Node(name, parent);
            if (prefab)
            {
                var o = Instantiate(prefab, n, false); o.name = "Model";
                o.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * o.transform.localRotation;   // Blender 导出的模型进 Unity 后绕竖轴转了 180°，转回来
                o.transform.localScale *= scale;
                foreach (var r in o.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.sharedMaterial = r.name.StartsWith("Glow") && ringMat ? ringMat : m;   // 发光件（台座上的光环）单独一个材质
                }
            }
            else
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(c.GetComponent<Collider>());
                c.name = "Stub"; c.transform.SetParent(n, false);
                c.transform.localScale = new Vector3(d, h * 0.5f, d); c.transform.localPosition = new Vector3(0f, -h * 0.5f, 0f);
                var r = c.GetComponent<Renderer>(); if (m) r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return n;
        }

        /// <summary>帽顶上的字 / 令徽：一块平躺的面片（正面朝上，字头朝离开玩家的方向）。</summary>
        Renderer Label(string name, Transform parent, float size, float lift)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(parent, false);
            q.transform.localPosition = new Vector3(0f, lift, 0f);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(size, size, 1f);
            var r = q.GetComponent<Renderer>(); if (labelMat) r.sharedMaterial = labelMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>名牌：有模型就放铭牌实体（平放、字头朝离开玩家的方向，再朝玩家斜），没有就退回一张面片。</summary>
        Renderer Plate(string name, Transform parent, Vector3 at)
        {
            if (!platePrefab || !plateMat)
            {
                var q = Label(name, parent, 0.034f, 0f); q.transform.localPosition = at; q.transform.localRotation = Quaternion.Euler(LipTilt, 0f, 0f);
                return q;
            }
            var n = Node(name, parent); n.localPosition = at; n.localRotation = Quaternion.Euler(LipTilt - 90f, 0f, 0f);
            var o = Instantiate(platePrefab, n, false); o.name = "Model";
            o.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * o.transform.localRotation;
            var r = o.GetComponentInChildren<Renderer>(); r.sharedMaterial = plateMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        void SetLab(Renderer r, Lab lab, Color col)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetTexture(MapId, lab.a); if (lab.n) mpb.SetTexture(BumpId, lab.n); if (lab.m) mpb.SetTexture(MaskId, lab.m);
            mpb.SetColor(ColorId, col);
            r.SetPropertyBlock(mpb);
        }

        Renderer Flat(string name, Transform parent, Material m)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(parent, false);
            var r = q.GetComponent<Renderer>(); if (m) r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        void Build()
        {
            built = true; mpb = new MaterialPropertyBlock();
            root = Node("Deck", transform);
            // 正中：底座 + 大按钮
            mainPod = Part("MainPod", root, podCenter, MainR * 2.9f, 0.02f, podCenterMat ? podCenterMat : metal);
            mainCap = Part("MainCap", mainPod, capPrefab, MainR * 2f, 0.016f, capMat ? capMat : glow); mainCapR = mainCap.GetComponentsInChildren<Renderer>();
            mainLabel = Label("MainLabel", mainCap, MainR * 1.6f, 0.0005f);
            mainBtn.t = mainCap; mainBtn.radius = MainR;

            // 左边：五个风格台座 + 放大开关
            leftPod = Part("LeftPod", root, podLeft, 0.001f, 0.001f, podLeftMat ? podLeftMat : metal);
            for (int i = 0; i < Settings.Eras; i++)
            {
                var ped = Part("Era_" + i, leftPod, pedestalPrefab, EraR * 2f, 0.009f, pedestalMat ? pedestalMat : metal, PedScale);
                eraPed[i] = ped; pedRends[i] = ped.GetComponentsInChildren<Renderer>();
                // 按的是小人（头顶往下一直到台座都算）：判定面放在小人头顶
                var hit = Node("Hit", ped); hit.localPosition = new Vector3(0f, FigTop, 0f);
                eraBtn[i] = new PokeButton { t = hit, radius = EraR * 1.2f };
                eraLabel[i] = Plate("EraLabel_" + i, leftPod, new Vector3(-((Settings.Eras - 1 - i) * EraStep), LipY, LipZ));
                var pf = game && game.view ? game.view.PrefabFor(i, eraTroops[Mathf.Min(i, eraTroops.Length - 1)]) : null;
                if (!pf) continue;
                var holder = Node("Fig", ped); holder.localScale = Vector3.one * FigScale; holder.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 面朝玩家
                var f = Instantiate(pf, holder, false); f.name = "Figure";
                foreach (var lg in f.GetComponentsInChildren<LODGroup>()) lg.ForceLOD(0);
                eraFig[i] = holder; eraRig[i] = f.GetComponent<FigureRig>();
                eraRends[i] = f.GetComponentsInChildren<Renderer>(true);
                foreach (var r in eraRends[i]) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (eraRig[i])
                {
                    var rig = eraRig[i];
                    if (rig.mount) rig.mount.gameObject.SetActive(false);
                    rig.rider.Init();
                    Idle(i, 0f);
                }
            }
            modeCap = Part("ModeCap", leftPod, leverPrefab, ModeR * 2f, 0.012f, switchMat ? switchMat : glow, ModeR / 0.0235f); modeCapR = modeCap.GetComponentsInChildren<Renderer>();
            modeLabel = Label("ModeLabel", modeCap, ModeR * 1.7f, 0.0005f);
            modeBtn.t = modeCap; modeBtn.radius = ModeR;

            // 右边：三个军令按钮
            rightPod = Part("RightPod", root, podRight, 0.001f, 0.001f, podRightMat ? podRightMat : metal);
            for (int i = 0; i < 3; i++)
            {
                var cap = Part("Order_" + i, rightPod, capPrefab, OrderR * 2f, 0.012f, capMat ? capMat : glow, OrderR / MainR);
                orderCapT[i] = cap; orderRends[i] = cap.GetComponentsInChildren<Renderer>();
                orderIcon[i] = Label("OrderIcon_" + i, cap, OrderR * 1.62f, 0.0004f);
                orderBtn[i] = new PokeButton { t = cap, radius = OrderR };
                orderLabel[i] = Plate("OrderLabel_" + i, rightPod, new Vector3(OrderX0 + i * OrderStep, LipY, LipZ));
            }

            // 小战场图（放大模式才出现）：一块板，上面贴俯视图，再叠各队的小点和“你在这里”的箭头
            mapT = Node("Map", root);
            mapR = Flat("MapFace", mapT, mapMat); mapR.transform.localScale = new Vector3(MapW, MapH, 1f);
            pin = Flat("Pin", mapT, pinMat).transform; pin.localPosition = new Vector3(0f, 0f, -0.001f);
        }

        /// <summary>台座上的小人摆预备姿势（站着、拿着第一件武器）。</summary>
        void Idle(int i, float dt)
        {
            var rig = eraRig[i]; if (!rig || !rig.rider || !rig.rider.Ready) return;
            string id = eraTroops[Mathf.Min(i, eraTroops.Length - 1)];
            if (!WeaponsData.Troops.TryGetValue(id, out var troop)) return;
            var w = WeaponsData.Weapons[troop.Weapons[0]];
            var set = Defs.GetClipSet(troop.Model, w.Cls, w.OneHandedPolearm);
            rig.ShowWeapon(0);
            rig.ShowShield(troop.Shield != null && (w.Cls == WeaponClass.OneHanded || w.Cls == WeaponClass.Thrown || w.OneHandedPolearm));
            bool cur = i == Settings.Era;
            string clip = cur && Time.time - pickT < 2.2f && pickEra == i ? "Victory" : set.Ready;
            rig.rider.Upper.Play(clip, fade: 0.25f); rig.rider.Lower.Play(clip, fade: 0.25f);
            rig.rider.Tick(cur ? dt : 0f);   // 只有当前风格的小人在动，其余定住
        }

        // ------------------------------------------------------------ 位置

        /// <summary>指挥台这一排的参照系：origin = 正中（台面高度），right = 玩家右手，away = 离开玩家，tilt = 台面朝玩家倾斜多少度。</summary>
        struct Frame { public Vector3 origin, right, away; public float tilt; }
        bool following, jumped; float farT, hopT = -9f;
        Frame frame;

        /// <summary>放大模式里指挥台换位置时的大小（1 = 正常；换位时先缩到 0 再长回来）。</summary>
        public float HopScale
        {
            get
            {
                if (!following) return 1f;
                float u = Time.time - hopT;
                return u < 0.12f ? 1f - u / 0.12f : Mathf.SmoothStep(0f, 1f, (u - 0.12f) / 0.2f);
            }
        }

        Frame Where(Transform eye)
        {
            var board = game.board;
            if (!Immersive)
            {
                followSet = false; following = false;
                var away = -board.forward; away.y = 0; away = away.sqrMagnitude < 1e-4f ? Vector3.forward : away.normalized;
                var right = -board.right; right.y = 0; right = right.sqrMagnitude < 1e-4f ? Vector3.right : right.normalized;
                // 边框外沿上沿（本地 z = 0.552，y = 0.0122），台面在它外面、略低一点；和沙盘一样水平（11-17 用户：不要倾斜）
                var o = board.TransformPoint(new Vector3(0f, 0.0122f, 0.552f)) - away * 0.05f - Vector3.up * 0.012f;
                return new Frame { origin = o, right = right, away = away, tilt = 0f };
            }
            // 放大模式：浮在玩家身前，钉在世界里不动；人走开 / 转身超过 0.7 秒才“缩下去 → 换到新位置 → 长出来”（视野里不要有东西跟着头漂）
            var h = eye.position; var f = eye.forward; f.y = 0; f = f.sqrMagnitude < 1e-4f ? followFwd : f.normalized;
            var want = new Vector3(h.x, h.y - FollowDrop, h.z) + f * FollowDist;
            if (!followSet) { followPos = want; followFwd = f; followSet = true; }
            else
            {
                var cur = followPos - new Vector3(h.x, followPos.y, h.z); cur.y = 0;
                bool far = Mathf.Abs(cur.magnitude - FollowDist) > FollowMove || Vector3.Angle(cur, f) > FollowTurn || Mathf.Abs(followPos.y - want.y) > 0.25f;
                farT = far && !following ? farT + Time.deltaTime : 0f;
                if (farT > 0.7f) { following = true; hopT = Time.time; farT = 0f; jumped = false; }
                if (following)
                {
                    float u = Time.time - hopT;
                    if (u >= 0.12f && !jumped) { followPos = want; followFwd = f; jumped = true; }
                    if (u >= 0.32f) following = false;
                }
            }
            return new Frame { origin = followPos, right = Vector3.Cross(Vector3.up, followFwd).normalized, away = followFwd, tilt = FloatTilt };
        }

        /// <summary>台面的朝向：本地 +Z = 离开玩家，法线从竖直往玩家那边倒 tilt 度。</summary>
        static Quaternion DeckRot(in Frame f)
        {
            var n = Quaternion.AngleAxis(-f.tilt, f.right) * Vector3.up;
            return Quaternion.LookRotation(Vector3.ProjectOnPlane(f.away, n).normalized, n);
        }

        // ------------------------------------------------------------ 每帧

        void LateUpdate()
        {
            if (!game || !game.board) return;
            if (!built) Build();
            var eye = Eye;
            bool on = show && eye && game.board.gameObject.activeInHierarchy;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            Captures = false;
            if (!on)
            {
                mainBtn.Reset(); modeBtn.Reset(); foreach (var b in eraBtn) b?.Reset(); foreach (var b in orderBtn) b?.Reset();
                mainShownT = -1f; mapTouchWas = false; return;
            }
            bool zh = Loc.Current == Loc.Lang.Zh;
            float dt = Time.deltaTime, t = Time.time;
            frame = Where(eye);
            root.localScale = Vector3.one * Mathf.Max(0.001f, HopScale);
            var rot = DeckRot(frame);
            bool imm = Immersive, grabbing = game.tray && game.tray.Grabbing;

            // ── 大按钮
            bool mainOn = main != Main.None;
            if (mainPod.gameObject.activeSelf != mainOn) mainPod.gameObject.SetActive(mainOn);
            if (mainOn)
            {
                if (main != mainWas || mainShownT < 0f) mainShownT = t;
                mainPod.SetPositionAndRotation(frame.origin, rot);
                // 刚出现的 0.8 秒不认（摆沙盘的手顺手碰到）；抓着沙盘时也不认
                bool live = t - mainShownT > 0.8f && !grabbing;
                mainBtn.Tick(mr, live, editorCam);
                mainPress = Mathf.MoveTowards(mainPress, mainBtn.Down || mainBtn.Pressed ? 1f : 0f, dt * 12f);
                mainCap.localPosition = new Vector3(0f, 0.016f - 0.008f * mainPress, 0f);
                var tex = main == Main.Ready ? (zh ? readyZh : readyEn) : (zh ? fightZh : fightEn);
                // 呼吸的光：这是现在该按的东西
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
                var baseCol = main == Main.Ready ? new Color(0.2f, 0.62f, 0.5f) : new Color(0.78f, 0.16f, 0.1f);
                Tint(mainCapR, baseCol * (mainBtn.Hover ? 1.25f : 1f), baseCol * (mainBtn.Hover ? 0.9f : 0.25f + 0.35f * pulse));
                if (mainLabel) { mainLabel.gameObject.SetActive(tex); if (tex) SetLab(mainLabel, tex, Color.white); }
                if (mainBtn.Pressed)
                {
                    if (game.sound) game.sound.Cue("stamp");
                    Debug.Log("[WT-IN] deck-main " + main);
                    if (main == Main.Ready) OnReady?.Invoke(); else OnFight?.Invoke();
                }
                Captures |= mainBtn.Hover;
            }
            mainWas = main;

            // ── 风格台座
            leftPod.SetPositionAndRotation(frame.origin - rot * new Vector3(LeftX, 0f, 0f), rot);
            int hoverNow = -1;
            for (int i = 0; i < Settings.Eras; i++)
            {
                var ped = eraPed[i]; if (!ped) continue;
                bool ready = Settings.Ready(i);
                if (ped.gameObject.activeSelf != ready) ped.gameObject.SetActive(ready);
                if (!ready) continue;
                bool cur = i == Settings.Era;
                var b = eraBtn[i];
                b.Tick(mr, eraLive && !cur && !grabbing, editorCam);
                if (b.Hover && !cur && eraLive) hoverNow = i;
                // 当前的抬高一点；手悬上去的那个踮一下
                eraPop[i] = Mathf.MoveTowards(eraPop[i], cur ? 1f : b.Hover && eraLive ? 0.6f : 0f, dt * 7f);
                ped.localPosition = new Vector3(-((Settings.Eras - 1 - i) * EraStep), 0.009f + 0.004f * eraPop[i], 0f);
                ped.localRotation = Quaternion.identity;
                float dim = cur ? 1f : eraLive ? 0.72f : 0.4f;
                Tint(pedRends[i], new Color(dim, dim, dim), cur ? new Color(0.5f, 0.36f, 0.12f) : b.Hover && eraLive ? new Color(0.25f, 0.2f, 0.1f) : Color.black);
                if (eraRends[i] != null)
                    foreach (var r in eraRends[i]) { if (!r) continue; r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, new Color(dim, dim, dim, 1f)); r.SetPropertyBlock(mpb); }
                Idle(i, dt);
                var tex = (zh ? eraZh : eraEn)[i];
                if (eraLabel[i])
                {
                    eraLabel[i].gameObject.SetActive(tex);
                    if (tex) { float lum = cur ? 1f : eraLive ? 0.72f : 0.42f; SetLab(eraLabel[i], tex, new Color(lum, lum, lum, 1f)); }
                }
                if (b.Pressed)
                {
                    pickT = t; pickEra = i;
                    if (game.sound) game.sound.Cue("stamp");
                    Debug.Log("[WT-IN] deck-era " + i);
                    OnEra?.Invoke(i);
                }
                Captures |= b.Hover && eraLive;
            }
            if (hoverNow != hoverWas) { if (hoverNow >= 0 && game.sound) game.sound.Cue("hover"); hoverWas = hoverNow; }

            // ── 放大开关（风格台座再往左）
            bool modeOn = mr && mr.Active;   // 只有头显上有放大模式
            if (modeCap.gameObject.activeSelf != modeOn) modeCap.gameObject.SetActive(modeOn);
            if (modeOn)
            {
                modeBtn.Tick(mr, modeLive && !grabbing, editorCam);
                modePress = Mathf.MoveTowards(modePress, modeBtn.Down || modeBtn.Pressed ? 1f : 0f, dt * 12f);
                modeCap.localPosition = new Vector3(-(Settings.Eras * EraStep + 0.006f), 0.012f - 0.005f * modePress, 0f);
                var c = imm ? new Color(0.85f, 0.6f, 0.2f) : new Color(0.25f, 0.5f, 0.85f);
                Tint(modeCapR, c * (modeLive ? 1f : 0.45f), c * (modeBtn.Hover && modeLive ? 0.8f : 0.2f));
                var tex = imm ? (zh ? tableZh : tableEn) : (zh ? immerseZh : immerseEn);
                if (modeLabel) { modeLabel.gameObject.SetActive(tex); if (tex) SetLab(modeLabel, tex, Color.white); }
                if (modeBtn.Pressed) { if (game.sound) game.sound.Cue("stamp"); Debug.Log("[WT-IN] deck-mode"); OnMode?.Invoke(); }
                Captures |= modeBtn.Hover && modeLive;
            }

            // ── 军令按钮：交战中才亮、才能按；按哪个就是对全军下哪道令
            rightPod.SetPositionAndRotation(frame.origin + rot * new Vector3(RightX, 0f, 0f), rot);
            for (int i = 0; i < 3; i++)
            {
                var b = orderBtn[i];
                b.Tick(mr, orderLive && !grabbing, editorCam);
                orderPress[i] = Mathf.MoveTowards(orderPress[i], b.Down || b.Pressed ? 1f : 0f, dt * 12f);
                orderCapT[i].localPosition = new Vector3(OrderX0 + i * OrderStep, 0.0115f - 0.005f * orderPress[i], 0f);
                float flash = Mathf.Clamp01(1f - (t - orderFlash[i]) / 0.6f);   // 按下后亮一下再落回去
                var c = OrderCol[i];
                if (orderLive) Tint(orderRends[i], c * (b.Hover ? 1.2f : 1f), c * (0.22f + (b.Hover ? 0.5f : 0f) + 1.4f * flash));
                else Tint(orderRends[i], Color.Lerp(c, new Color(0.3f, 0.3f, 0.3f), 0.7f) * 0.55f, Color.black);
                if (orderIcon[i] && orderCap[i]) SetLab(orderIcon[i], orderCap[i], orderLive ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.8f));
                var tex = (zh ? orderZh : orderEn)[i];
                if (orderLabel[i]) { orderLabel[i].gameObject.SetActive(tex); if (tex) { float lum = orderLive ? 1f : 0.5f; SetLab(orderLabel[i], tex, new Color(lum, lum, lum, 1f)); } }
                if (b.Pressed)
                {
                    orderFlash[i] = t;
                    if (game.sound) game.sound.Cue("stamp");
                    Debug.Log("[WT-IN] deck-order " + i);
                    OnOrder?.Invoke(i);
                }
                Captures |= b.Hover && orderLive;
            }

            MapTick(imm, rot);
        }

        /// <summary>
        /// 小战场图（只在放大模式）：立在大按钮后面，朝玩家仰着。上面是战场的俯视图，蓝点 / 红点是两军各队，金色箭头是“你在这里”（箭头指你面朝的方向）。
        /// 食指点图上任意一处（或在上面捏一下）= 站到战场的那个位置去。防误触：指尖先离开 0.3 秒再碰上才算，点完 0.8 秒内不再认。
        /// </summary>
        void MapTick(bool imm, Quaternion rot)
        {
            if (mapT.gameObject.activeSelf != imm) mapT.gameObject.SetActive(imm);
            if (!imm) { mapTouchWas = false; return; }
            // 图面：先平躺（正面朝上、图的上方朝离开玩家的方向），再绕横轴朝玩家仰 MapTilt 度
            var mrot = rot * Quaternion.Euler(90f - MapTilt, 0f, 0f);
            mapT.SetPositionAndRotation(frame.origin + rot * new Vector3(0f, MapH * 0.5f * Mathf.Sin(MapTilt * Mathf.Deg2Rad) + 0.02f, 0.11f), mrot);
            var tex = maps != null && Settings.Era < maps.Length ? maps[Settings.Era] : null;
            mapR.GetPropertyBlock(mpb); if (tex) mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, Color.white); mapR.SetPropertyBlock(mpb);

            // 图上的位置：模拟层 (x, z)（码）→ 图的本地坐标（米）。玩家右手 = 模拟层 +x，远离玩家 = 模拟层 −z = 图的上方
            Vector3 OnMap(double x, double z, float lift) => new Vector3((float)(x / BoardMap.HalfX) * MapW * 0.5f, (float)(-z / BoardMap.HalfZ) * MapH * 0.5f, -lift);

            // 各队的小点
            int n = 0;
            if (game.Command != null && game.Battle != null)
                foreach (var f in game.Command.Flags())
                {
                    if (n >= dots.Count) { var d = Flat("Dot", mapT, pinMat); dots.Add(d); }
                    var dr = dots[n++]; dr.gameObject.SetActive(true);
                    dr.transform.localPosition = OnMap(f.x, f.z, 0.0006f);
                    dr.transform.localRotation = Quaternion.Euler(0f, 0f, 45f); dr.transform.localScale = Vector3.one * 0.0085f;   // 小菱形
                    dr.GetPropertyBlock(mpb); mpb.SetTexture(MapId, Texture2D.whiteTexture);
                    mpb.SetColor(ColorId, f.team == BattleCommand.Me ? new Color(0.35f, 0.65f, 1f) : new Color(1f, 0.3f, 0.22f)); dr.SetPropertyBlock(mpb);
                }
            for (int i = n; i < dots.Count; i++) dots[i].gameObject.SetActive(false);

            // 你在这里
            var eye = Eye; var board = game.board;
            var lp = board.InverseTransformPoint(eye.position) / BoardMap.Yard;
            var lf = board.InverseTransformDirection(eye.forward); lf.y = 0;
            pin.localPosition = OnMap(Mathf.Clamp(-lp.x, -BoardMap.HalfX, BoardMap.HalfX), Mathf.Clamp(lp.z, -BoardMap.HalfZ, BoardMap.HalfZ), 0.0012f);
            pin.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(lf.x, -lf.z) * Mathf.Rad2Deg);   // 图的右 = 本地 −x，图的上 = 本地 −z
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 4f);
            pin.localScale = new Vector3(0.012f, 0.02f, 1f) * pulse;
            var pr = pin.GetComponent<Renderer>(); pr.GetPropertyBlock(mpb); mpb.SetTexture(MapId, Texture2D.whiteTexture); mpb.SetColor(ColorId, new Color(1f, 0.82f, 0.35f)); pr.SetPropertyBlock(mpb);

            // 点图
            bool touch = false; Vector2 at = default;
            if (mr && mr.Active)
                for (int i = 0; i < 2; i++)
                {
                    var h = i == 0 ? mr.Left : mr.Right;
                    if (!h.tracked) continue;
                    var p = mapT.InverseTransformPoint(h.pinch ? h.pinchPoint : h.indexTip);
                    bool inside = Mathf.Abs(p.x) < MapW * 0.5f && Mathf.Abs(p.y) < MapH * 0.5f;
                    if (inside && Mathf.Abs(p.z) < 0.05f) Captures = true;
                    // 面片的正面朝本地 −Z：指尖从正面按上来（离图面 1.2 cm 以内，或刚穿过去一点）
                    bool hit = inside && (h.pinch ? (i == 0 ? mr.PinchDownL : mr.PinchDownR) && Mathf.Abs(p.z) < 0.03f : p.z > -0.012f && p.z < 0.03f);
                    if (hit) { touch = true; at = new Vector2(p.x, p.y); }
                }
            else if (editorCam && UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                var ray = FlyCam.View(editorCam).ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
                var o = mapT.InverseTransformPoint(ray.origin); var v = mapT.InverseTransformDirection(ray.direction);
                if (Mathf.Abs(v.z) > 1e-5f) { float k = -o.z / v.z; var p = o + v * k; if (k > 0 && Mathf.Abs(p.x) < MapW * 0.5f && Mathf.Abs(p.y) < MapH * 0.5f) { touch = true; at = new Vector2(p.x, p.y); mapAwayT = 9f; } }
            }
            if (touch && !mapTouchWas && mapAwayT > 0.3f && Time.time - mapPickT > 0.8f && modeLive)
            {
                mapPickT = Time.time;
                double sx = at.x / (MapW * 0.5f) * BoardMap.HalfX, sz = -at.y / (MapH * 0.5f) * BoardMap.HalfZ;
                if (game.sound) game.sound.Cue("select");
                Debug.Log($"[WT-IN] deck-map {sx:0},{sz:0}");
                OnMapPick?.Invoke(sx, sz);
            }
            if (touch) mapAwayT = 0f; else mapAwayT += Time.deltaTime;
            mapTouchWas = touch;
        }

        /// <summary>给一个元件调底色和自发光。发光件（光环）亮不亮全看自发光；金属件只调明暗，自发光给一点点（不然整块铁都在发光）。</summary>
        void Tint(Renderer[] rs, Color col, Color emis)
        {
            if (rs == null) return;
            col.a = 1f;
            foreach (var r in rs)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                if (r.name.StartsWith("Glow")) { mpb.SetColor(ColorId, new Color(0.08f, 0.07f, 0.06f, 1f) + emis * 0.6f); mpb.SetColor(EmisId, emis * 2.2f); }
                else if (ringMat && r.sharedMaterial != capMat && r.sharedMaterial != switchMat && r.sharedMaterial != glow) { mpb.SetColor(ColorId, col); mpb.SetColor(EmisId, emis * 0.12f); }
                else { mpb.SetColor(ColorId, col); mpb.SetColor(EmisId, emis); }
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
