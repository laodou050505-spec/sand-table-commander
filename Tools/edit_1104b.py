"""11-04 第二轮反馈的代码改动（一次性脚本）：主菜单加“教学”、只认木框抓沙盘、手势防误触 + 反馈、暂停键提到沙盘上方、教学期间战场放慢、场景接线。"""
R = '/Users/ld/Documents/ChatGPT/WarTable/'


def edit(p, pairs):
    p = R + p
    s = open(p).read()
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:80], s.count(a))
        s = s.replace(a, b)
    open(p, 'w').write(s)


# ── 封面菜单：主菜单 出征 / 教学 / 设置 / 退出；设置页 语言 / 战速 / 暗度 / 返回（11-04 用户：教学不要在设置里面）
edit('Assets/_WarTable/Editor/CoverBuilder.cs', [
    ('''                (MenuCover.Action.Begin, "word_chuzheng", "menu_begin", -0.165f, -0.005f + L.menuDy, 4f),
                (MenuCover.Action.Settings, "word_shezhi", "menu_settings", -0.228f, -0.083f + L.menuDy, -3.5f),
                (MenuCover.Action.Quit, "word_tuichu", "menu_quit", -0.19f, -0.162f + L.menuDy, 2.5f),
                (MenuCover.Action.Language, "word_zhongwen", "menu_english", 0f, 0f, 0f),
                (MenuCover.Action.Speed, "word_zhansu", "menu_speed", 0f, 0f, 0f),
                (MenuCover.Action.Dim, "word_andu", "menu_dim", 0f, 0f, 0f),
                (MenuCover.Action.Tutorial, "word_jiaoxue", "menu_tutorial", 0f, 0f, 0f),
                (MenuCover.Action.Back, "word_fanhui", "menu_back", 0f, 0f, 0f),''',
     '''                (MenuCover.Action.Begin, "word_chuzheng", "menu_begin", -0.165f, -0.005f + L.menuDy, 4f),
                (MenuCover.Action.Tutorial, "word_jiaoxue", "menu_tutorial", -0.2f, -0.044f + L.menuDy, -2f),
                (MenuCover.Action.Settings, "word_shezhi", "menu_settings", -0.228f, -0.083f + L.menuDy, -3.5f),
                (MenuCover.Action.Quit, "word_tuichu", "menu_quit", -0.19f, -0.162f + L.menuDy, 2.5f),
                (MenuCover.Action.Language, "word_zhongwen", "menu_english", 0f, 0f, 0f),
                (MenuCover.Action.Speed, "word_zhansu", "menu_speed", 0f, 0f, 0f),
                (MenuCover.Action.Dim, "word_andu", "menu_dim", 0f, 0f, 0f),
                (MenuCover.Action.Back, "word_fanhui", "menu_back", 0f, 0f, 0f),'''),
    ('''                int page = i < 3 ? 0 : 1, slot = i < 3 ? i : i - 3;''',
     '''                const int MainCount = 4;   // 主菜单项数
                int page = i < MainCount ? 0 : 1, slot = i < MainCount ? i : i - MainCount;'''),
    ('''                    if (L.centered) { var a = L.menuAt[i];''', '''                    if (L.centered && i < L.menuAt.Length) { var a = L.menuAt[i];'''),
    ('''                    if (L.free) { var fm = L.fmenu[i];''', '''                    if (L.free && i < L.fmenu.Length) { var fm = L.fmenu[i];'''),
    ('''fmenu = new[] { (new Vector3(0.0f, -0.165f, 0.12f), 1.05f, 0f), (new Vector3(0.0f, -0.23f, 0.12f), 1.0f, 0f), (new Vector3(0.0f, -0.295f, 0.12f), 1.0f, 0f) },''',
     '''fmenu = new[] { (new Vector3(0.0f, -0.155f, 0.12f), 1.05f, 0f), (new Vector3(0.0f, -0.212f, 0.12f), 1.0f, 0f), (new Vector3(0.0f, -0.269f, 0.12f), 1.0f, 0f), (new Vector3(0.0f, -0.326f, 0.12f), 1.0f, 0f) },'''),
])

# ── 抓沙盘只认木框附近（11-04 用户：操作时不要误触——双手在沙盘中间各捏一下不该把沙盘拖走）
edit('Assets/_WarTable/Runtime/XR/HandLogic.cs', [
    ('public const float GrabMargin = 0.15f, GrabHeight = 0.4f;',
     'public const float GrabMargin = 0.15f, GrabHeight = 0.25f, RimInside = 0.1f;   // 木框外 15 cm 到框内 10 cm、框上 25 cm 以内才算捏住托盘'),
    ('''            return Mathf.Abs(l.x) <= halfX * pose.scale + GrabMargin && Mathf.Abs(l.z) <= halfZ * pose.scale + GrabMargin
                && l.y > -GrabMargin && l.y < GrabHeight;''',
     '''            float hx = halfX * pose.scale, hz = halfZ * pose.scale;
            bool box = Mathf.Abs(l.x) <= hx + GrabMargin && Mathf.Abs(l.z) <= hz + GrabMargin && l.y > -GrabMargin && l.y < GrabHeight;
            float inside = Mathf.Min(hx - Mathf.Abs(l.x), hz - Mathf.Abs(l.z));   // 离最近一条边往里多远（负 = 在框外）
            return box && inside < RimInside;'''),
])

# ── 手势：捏过的手 0.6 秒内不认手势（伸手去捏、松手时手指一张，都会被误认成握拳 / 张掌）
edit('Assets/_WarTable/Runtime/XR/Gestures.cs', [
    ('''        float pendingChargeT = -1f; int pendingHand = -1;''',
     '''        float pendingChargeT = -1f; int pendingHand = -1;
        readonly float[] lastPinch = { -9f, -9f };
        public const float AfterPinch = 0.6f;'''),
    ('''                bool ok = h.tracked && !h.pinch && Vector3.Dot(h.palm - head, fwd) > 0.12f && h.palm.y < head.y - 0.1f;''',
     '''                if (h.tracked && h.pinch) lastPinch[i] = now;
                bool ok = h.tracked && !h.pinch && now - lastPinch[i] > AfterPinch && Vector3.Dot(h.palm - head, fwd) > 0.12f && h.palm.y < head.y - 0.1f;'''),
])

# ── WarGame：捏在木框上 = 抓托盘，不交给战场；手势反馈；教学期间战场放慢
edit('Assets/_WarTable/Runtime/Game/WarGame.cs', [
    ('''        float TimeScale => Speed * (Slow ? SlowFactor : 1f) * drawSlow;''',
     '''        float TimeScale => Speed * (Slow ? SlowFactor : 1f) * drawSlow * (tutorial && tutorial.SlowBattle ? 0.4f : 1f);'''),
    ('''        public PauseMenu pause;''', '''        public PauseMenu pause;
        /// <summary>指挥手势被认出时胸前闪一下对应图标。</summary>
        public GestureFlash gestureFlash;'''),
    ('''            bool pinching = pinchHand >= 0 && !grabbing && (!tray || tray.Placed);''',
     '''            // 捏的起点在木框附近 = 想抓托盘（等另一只手），这一捏不交给战场，免得顺手选中 / 划线（11-04 用户：操作时不要误触）
            bool raw = pinchHand >= 0;
            if (raw && !rawPinchWas) rimPinch = tray && tray.Placed && TrayRules.InGrabZone(hand.pinchPoint, tray.Pose, tray.halfX, tray.halfZ);
            if (!raw) rimPinch = false;
            rawPinchWas = raw;
            bool pinching = raw && !rimPinch && !grabbing && (!tray || tray.Placed);'''),
    ('''                else if (Command.Selected.Count > 0) Command.Exec(gest == Gesture.Charge ? "charge" : gest == Gesture.Hold ? "hold" : "retreat");
                else gest = Gesture.None;
                if (gest != Gesture.None) LastGesture = (gest, Time.time);''',
     '''                else if (Command.Selected.Count > 0) Command.Exec(gest == Gesture.Charge ? "charge" : gest == Gesture.Hold ? "hold" : "retreat");
                else
                {
                    // 没选部队：图标发暗红 + 闷响，告诉玩家先选部队
                    if (gestureFlash) gestureFlash.Show(gest, false);
                    if (sound) sound.Cue("deny");
                    if (field) field.OnDeny();
                    gest = Gesture.None;
                }
                if (gest != Gesture.None) { LastGesture = (gest, Time.time); if (gestureFlash) gestureFlash.Show(gest, true); }'''),
    ('''        readonly GestureRecognizer gestures = new();''', '''        readonly GestureRecognizer gestures = new();
        bool rawPinchWas, rimPinch;'''),
])

# ── 暂停键：提到沙盘上方（原来在视线下 29°，正好被沙盘挡住），画在最上层
edit('Assets/_WarTable/Runtime/Game/PauseMenu.cs', [
    ('''public float btnDistance = 0.55f, btnLeft = 0.17f, btnBelow = 0.3f;
        public float btnSize = 0.05f;''',
     '''public float btnDistance = 0.5f, btnLeft = 0.2f, btnBelow = 0.1f;   // 11-04 用户：暂停键没看见 → 视线左下 22°、下 11°，在沙盘上方
        public float btnSize = 0.06f;'''),
])
edit('Assets/_WarTable/Editor/ResultBuilder.cs', [
    ('''            br.sharedMaterial = CoverBuilder.Unlit("M_P_Button", btnTex, Color.white, 3090);''',
     '''            br.sharedMaterial = CoverBuilder.Unlit("M_P_Button", btnTex, Color.white, 3090);
            br.sharedMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);   // 不被沙盘、棋子挡住'''),
])

# ── 场景接线：教学全部图标和提示条、手势反馈、手部遮挡
edit('Assets/_WarTable/Editor/GameSetup.cs', [
    ('''            game.tutorial.pinchTex = CoverBuilder.Tex("hand_pinch"); game.tutorial.palmTex = CoverBuilder.Tex("hand_palm");''',
     '''            game.tutorial.pinchTex = CoverBuilder.Tex("hand_pinch"); game.tutorial.palmTex = CoverBuilder.Tex("hand_palm");
            game.tutorial.trayTex = CoverBuilder.Tex("hand_tray"); game.tutorial.fistTex = CoverBuilder.Tex("hand_fist");
            game.tutorial.pressTex = CoverBuilder.Tex("hand_press"); game.tutorial.beckonTex = CoverBuilder.Tex("hand_beckon");'''),
    ('''            string[] steps = { "place", "goal", "select", "line", "fight", "attack" };''',
     '''            string[] steps = { "place", "goal", "tray", "select", "line", "fight", "attack", "charge", "hold", "retreat", "pause", "finish" };   // 顺序 = Tutorial.Step'''),
    ('''            game.pause.mr = game.mr; game.pause.editorEye = camT;''',
     '''            game.pause.mr = game.mr; game.pause.editorEye = camT;
            // 指挥手势反馈（11-04）
            var gf = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(gf.GetComponent<Collider>());
            gf.name = "GestureFlash"; gf.transform.SetParent(go.transform, false);
            var gfR = gf.GetComponent<Renderer>(); gfR.shadowCastingMode = ShadowCastingMode.Off;
            gfR.sharedMaterial = CoverBuilder.Unlit("M_GestureFlash", game.tutorial.fistTex, Color.white, 3088);
            game.gestureFlash = go.AddComponent<GestureFlash>();
            game.gestureFlash.quad = gfR; game.gestureFlash.mr = game.mr; game.gestureFlash.editorEye = camT;
            game.gestureFlash.fist = game.tutorial.fistTex; game.gestureFlash.press = game.tutorial.pressTex; game.gestureFlash.beckon = game.tutorial.beckonTex;
            gf.SetActive(false);
            // 手部遮挡（11-04 用户：手不要被沙盘挡住）：只写深度的手形网格，露出透视画面里的真手
            var occ = go.AddComponent<HandOccluder>();
            occ.input = game.mr; occ.material = BoardBuilder.Mat("M_HandOccluder", "WarTable/HandOccluder");'''),
])
print('all edits ok')
