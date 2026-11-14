using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WarTable.Game;
using WarTable.XR;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 把玩法装进场景（菜单 WarTable/7）：Game 对象（WarGame + BattleView）、18 种棋子预制体、箭 / 投矛预制体、
    /// 地形碰撞体（棋子落地用射线）、封面 / 布景帅旗的引用、座位上的预览相机。搭沙盘 / 布景 / 封面之后跑。
    /// </summary>
    public static class GameSetup
    {
        const string MissileDir = "Assets/_WarTable/Prefabs/Missiles";

        [MenuItem("WarTable/7 装配玩法")]
        /// <summary>令牌材质挂质感贴图：Art/Textures/Tokens/&lt;前缀&gt;_albedo / _normal / _mask（R 金属、A 光滑度）。</summary>
        static void TokenTex(Material m, string pre, float bump)
        {
            Texture2D T(string f, bool srgb, bool normal)
            {
                var path = $"Assets/_WarTable/Art/Textures/Tokens/{pre}_{f}.png";
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.sRGBTexture != srgb || (ti.textureType == TextureImporterType.NormalMap) != normal || ti.maxTextureSize != 1024))
                { ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = srgb; ti.maxTextureSize = 1024; ti.anisoLevel = 4; ti.SaveAndReimport(); }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            m.SetTexture("_BaseMap", T("albedo", true, false));
            m.SetTexture("_BumpMap", T("normal", false, true)); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", T("mask", false, false)); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_SmoothnessTextureChannel", 0f);
        }

        public static void Setup()
        {
            var board = GameObject.Find(BoardBuilder.Root).transform;
            var old = GameObject.Find("Game");
            if (old) Object.DestroyImmediate(old);
            var go = new GameObject("Game");
            var view = go.AddComponent<BattleView>();
            var game = go.AddComponent<WarGame>();
            game.view = view;
            game.board = board;
            game.cover = GameObject.Find(CoverBuilder.Root);
            game.menu = game.cover ? game.cover.GetComponent<MenuCover>() : null;
            var props = board.Find(PropBuilder.Root);
            game.fieldStandards = props ? props.Cast<Transform>().Where(t => t.name.StartsWith("Standard_")).Select(t => t.gameObject).ToArray() : new GameObject[0];

            view.prefabs = DemoFigures.Gears.Keys.Select(id => new BattleView.TroopPrefab { troop = id, prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{DemoFigures.PrefabDir}/{id}.prefab") }).ToArray();
            // 11-10：哥特时代（封面“时代”选哥特时用）；缺的兵种运行时退回原版
            view.prefabsGothic = DemoFigures.Gears.Keys.Select(id => new BattleView.TroopPrefab { troop = id, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoFigures.PrefabPath(id, "gothic")) }).Where(p => p.prefab).ToArray();
            view.prefabsFuture = DemoFigures.Gears.Keys.Select(id => new BattleView.TroopPrefab { troop = id, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoFigures.PrefabPath(id, "future")) }).Where(p => p.prefab).ToArray();
            view.prefabsModern = DemoFigures.Gears.Keys.Select(id => new BattleView.TroopPrefab { troop = id, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoFigures.PrefabPath(id, "modern")) }).Where(p => p.prefab).ToArray();
            view.prefabsSpace = DemoFigures.Gears.Keys.Select(id => new BattleView.TroopPrefab { troop = id, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoFigures.PrefabPath(id, "space")) }).Where(p => p.prefab).ToArray();
            view.boltMatSpaceOurs = BoardBuilder.Mat("M_Bolt_SpaceOurs", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(0.45f, 0.9f, 1f)));     // 深空：我军青蓝等离子
            view.boltMatSpaceTheirs = BoardBuilder.Mat("M_Bolt_SpaceTheirs", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(1f, 0.62f, 0.2f)));   // 敌军琥珀焊枪火
            view.boltMatTracer = BoardBuilder.Mat("M_Bolt_Tracer", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(1f, 0.9f, 0.55f)));   // 现代时代的曳光
            view.arrowPrefab = Missile("arrow");
            view.javelinPrefab = Missile("javelin");
            // 11-12 未来时代：光束（细长发光柱，沿 −Z 朝前，和箭的约定一致）；我军天青、敌军紫
            {
                var root = new GameObject("bolt");
                var c = GameObject.CreatePrimitive(PrimitiveType.Capsule); Object.DestroyImmediate(c.GetComponent<Collider>());
                c.name = "Beam"; c.transform.SetParent(root.transform, false);
                c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); c.transform.localScale = new Vector3(0.0011f, 0.0075f, 0.0011f);   // 长 1.5 cm、粗 1.1 mm
                c.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                view.boltMatOurs = BoardBuilder.Mat("M_Bolt_Cyan", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(0.6f, 0.96f, 1f)));
                view.boltMatFoe = BoardBuilder.Mat("M_Bolt_Violet", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(0.82f, 0.56f, 1f)));
                var cr = c.GetComponent<Renderer>(); cr.sharedMaterial = view.boltMatOurs; cr.shadowCastingMode = ShadowCastingMode.Off;
                System.IO.Directory.CreateDirectory(MissileDir);
                view.boltPrefab = PrefabUtility.SaveAsPrefabAsset(root, $"{MissileDir}/bolt.prefab"); Object.DestroyImmediate(root);
            }

            var terrain = board.Find("Terrain").gameObject;
            if (!terrain.TryGetComponent<MeshCollider>(out var col)) col = terrain.AddComponent<MeshCollider>();
            col.sharedMesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            view.terrainCollider = col;
            // 11-11：时代的场景差异（哥特 = 焦灰荒原、布景压暗偏冷）
            {
                var el = terrain.GetComponent<EraLook>() ?? terrain.AddComponent<EraLook>();
                el.terrain = terrain.GetComponent<Renderer>(); el.ashTex = BoardBuilder.Tex("ash_turf"); el.moonTex = BoardBuilder.Tex("moon_turf");   // 11-12 未来：月壤
                el.moonDry = BoardBuilder.Tex("moon_basalt"); el.moonRoad = BoardBuilder.Tex("moon_road"); el.moonSand = BoardBuilder.Tex("moon_gravel");
                el.modTex = BoardBuilder.Tex("mod_steppe"); el.modDry = BoardBuilder.Tex("mod_camp"); el.modRoad = BoardBuilder.Tex("mod_gravel");   // 11-13 现代：干草原 / 营地干土 / 碎石路
                Texture2D ST(string n) => System.IO.File.Exists($"Assets/_WarTable/Art/Textures/{n}.png") ? BoardBuilder.Tex(n) : null;
                el.spcTex = ST("space_regolith"); el.spcDry = ST("space_ore"); el.spcRoad = ST("space_deck");   // 11-13 深空：小行星风化层 / 铁矿砂 / 钢甲板路（还没出图时为空，运行时退回月面贴图）
                { var wt = board.Find("RiverWater"); el.water = wt ? wt.GetComponent<Renderer>() : null; }
                el.props = props ? props.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterial && r.sharedMaterial.shader.name == "WarTable/MiniLit").ToArray() : new Renderer[0];
            }

            // 座位上的预览相机（编辑器试玩；头显里由 XR 相机代替）
            var preview = GameObject.Find(BoardBuilder.Preview);
            var camT = preview ? preview.transform.Find("SeatCamera") : null;
            if (!camT)
            {
                camT = new GameObject("SeatCamera").transform;
                if (preview) camT.SetParent(preview.transform, false);
                var cam = camT.gameObject.AddComponent<Camera>();
                cam.nearClipPlane = 0.02f; cam.farClipPlane = 20f; cam.fieldOfView = 62f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.78f, 0.76f, 0.73f);
                camT.gameObject.tag = "MainCamera";
            }
            camT.position = WTCapture.SeatEye;
            camT.LookAt(new Vector3(0f, -0.02f, -0.06f));
            game.previewCamera = camT.GetComponent<Camera>();
            if (!camT.GetComponent<AudioListener>()) camT.gameObject.AddComponent<AudioListener>();

            // 头显：XR 原点在地面，相机由头显驱动；手眼输入、托盘摆放
            var xrCam = BuildXR(go.transform);
            game.mr = go.AddComponent<MRInput>();
            game.mr.cam = xrCam; game.mr.origin = xrCam.transform.parent;
            game.tray = go.AddComponent<TrayRig>();
            game.tray.input = game.mr; game.tray.board = board;
            game.tray.halfX = (BoardBuilder.InnerX + BoardBuilder.FrameW) * BoardMap.BoardScale;
            game.tray.halfZ = (BoardBuilder.InnerZ + BoardBuilder.FrameW) * BoardMap.BoardScale;
            game.tray.lift = -BoardBuilder.TableY * BoardMap.BoardScale;
            if (game.menu) { game.menu.mr = game.mr; game.menu.editorEye = camT; }
            // 旧场景里主光还在 EditorOnly 的预览节点下：挪到顶层（打包会剔除 EditorOnly）
            var sunL = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sunL) { sunL.transform.SetParent(null, true); sunL.name = "Sun"; game.tray.sun = sunL.transform; }

            // 战场提示 + 结算板（11-03 UI）
            game.field = go.AddComponent<FieldUI>();
            game.field.mat = ResultBuilder.FieldMat(); game.field.view = view; game.field.board = board;
            game.result = ResultBuilder.Build(go.transform);
            game.result.mr = game.mr; game.result.editorEye = camT;
            // 声音（11-03）：BGM 流式加载，短音效解压进内存
            game.sound = go.AddComponent<GameAudio>();
            const string AD = "Assets/_WarTable/Audio";
            AudioClip C(string f, bool music = false)
            {
                string path = $"{AD}/{f}.mp3";
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp)
                {
                    var st = imp.defaultSampleSettings;
                    st.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                    st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = music ? 0.6f : 0.8f;
                    imp.defaultSampleSettings = st; imp.forceToMono = !music; imp.loadInBackground = music;
                    imp.SaveAndReimport();
                }
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!c) Debug.LogWarning("[Setup] 缺音频 " + path);
                return c;
            }
            // 玩中学引导（11-03）：一个正对玩家的水墨手势图标
            game.tutorial = go.AddComponent<Tutorial>();
            game.tutorial.pinchTex = CoverBuilder.Tex("hand_pinch"); game.tutorial.palmTex = CoverBuilder.Tex("hand_palm");
            game.tutorial.trayTex = CoverBuilder.Tex("hand_tray"); game.tutorial.fistTex = CoverBuilder.Tex("hand_fist");
            game.tutorial.pressTex = CoverBuilder.Tex("hand_press"); game.tutorial.beckonTex = CoverBuilder.Tex("hand_beckon");
            var ic = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(ic.GetComponent<Collider>());
            ic.name = "TutorialIcon"; ic.transform.SetParent(go.transform, false);
            var icR = ic.GetComponent<Renderer>(); icR.shadowCastingMode = ShadowCastingMode.Off;
            icR.sharedMaterial = CoverBuilder.Unlit("M_TutorialIcon", game.tutorial.pinchTex, Color.white, 3080);
            game.tutorial.icon = ic.transform; game.tutorial.iconRenderer = icR; ic.SetActive(false);
            // 教学短句提示条（11-04）：空节点 TipHolder 由 LazyFollow 摆在眼前，Quad 转 180° 正面朝玩家
            var holder = new GameObject("TutorialTip").transform; holder.SetParent(go.transform, false);
            var tq = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(tq.GetComponent<Collider>());
            tq.name = "Tip"; tq.transform.SetParent(holder, false); tq.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            var tqR = tq.GetComponent<Renderer>(); tqR.shadowCastingMode = ShadowCastingMode.Off;
            // 11-10：软提示（Tutorial.Hint 顺序）：每条两行提示条 tip_<键>，学会时的朱印条 learn_<键>（摆沙盘两步和“手看不到”没有）
            var keys = WarTable.Game.Tutorial.Keys;
            game.tutorial.tipZh = keys.Select(k => CoverBuilder.Tex("tip_" + k + "_zh")).ToArray();
            game.tutorial.tipEn = keys.Select(k => CoverBuilder.Tex("tip_" + k + "_en")).ToArray();
            game.tutorial.learnZh = keys.Select(k => k is "palm" or "adjust" or "hands" ? null : CoverBuilder.Tex("learn_" + k + "_zh")).ToArray();
            game.tutorial.learnEn = keys.Select(k => k is "palm" or "adjust" or "hands" ? null : CoverBuilder.Tex("learn_" + k + "_en")).ToArray();
            tqR.sharedMaterial = CoverBuilder.Unlit("M_TutorialTip", game.tutorial.tipZh[2], Color.white, 3085);
            game.tutorial.tip = tqR; tq.SetActive(false);
            // 游戏途中的返回键（11-04）
            game.pause = ResultBuilder.BuildPause(go.transform);
            game.pause.mr = game.mr; game.pause.editorEye = camT; game.pause.board = board;
            // 沙盘常驻标识（11-05）
            var signs = go.AddComponent<BoardSigns>();
            signs.board = board; signs.view = view; signs.mr = game.mr; signs.editorEye = camT;
            // 11-10 用户：平视时后面的令牌跑到帅旗前面 → 令牌改回正常深度测试（被帅旗 / 树 / 房子正确挡住）；
            // 正被手拿着 / 手正够着的那块用 matTop（永远画在最上面），免得被手的遮挡轮廓吞掉
            signs.mat = CoverBuilder.Unlit("M_BoardSign", null, Color.white, 3080);
            signs.mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            signs.matTop = CoverBuilder.Unlit("M_BoardSignTop", null, Color.white, 3082);
            signs.matTop.SetFloat("_ZTest", (float)CompareFunction.Always);
            signs.knobTex = CoverBuilder.Tex("handle_knob");
            signs.readyZh = CoverBuilder.Tex("btn_ready_zh"); signs.readyEn = CoverBuilder.Tex("btn_ready_en");
            string[] gk = {"inf","arch","cav","ha"};
            // 11-11：前 8 张中古、后 8 张哥特（文件名多一个 _g：兵种名铁卫 / 射手 / 铁骑 / 猎骑、图标不同），BoardSigns 按 Settings.Era 选
            // 11-12：每个时代 8 张（Settings.EraSuffix：_g 哥特、_m 现代、_f 未来）；还没出图的时代留空，运行时退回中古
            int tn = Settings.Eras * 8;
            signs.tilesZh = new Texture2D[tn]; signs.tilesEn = new Texture2D[tn]; signs.tilesHotZh = new Texture2D[tn]; signs.tilesHotEn = new Texture2D[tn];
            for (int era = 0; era < Settings.Eras; era++) for (int ti = 0; ti < 2; ti++) for (int gi = 0; gi < 4; gi++)
            {
                int ix = era * 8 + ti * 4 + gi; string b = "tile_" + ti + "_" + gk[gi] + (era > 0 ? "_" + Settings.EraSuffix[era] : "");
                if (!System.IO.File.Exists($"ArtSource/Cover/v6/alpha/{b}_zh.png")) continue;
                signs.tilesZh[ix] = CoverBuilder.Tex(b + "_zh"); signs.tilesEn[ix] = CoverBuilder.Tex(b + "_en");
                signs.tilesHotZh[ix] = CoverBuilder.Tex(b + "_zh_hot"); signs.tilesHotEn[ix] = CoverBuilder.Tex(b + "_en_hot");
            }
            signs.legendZh = CoverBuilder.Tex("legend_zh"); signs.legendEn = CoverBuilder.Tex("legend_en");
            signs.legendTacticsZh = CoverBuilder.Tex("legend_tactics_zh"); signs.legendTacticsEn = CoverBuilder.Tex("legend_tactics_en");
            signs.legendTabZh = CoverBuilder.Tex("legend_tab_zh"); signs.legendTabEn = CoverBuilder.Tex("legend_tab_en");
            signs.statesZh = WarTable.Game.BoardSigns.States.Select(k => CoverBuilder.Tex("state_" + k + "_zh")).ToArray();
            signs.statesEn = WarTable.Game.BoardSigns.States.Select(k => CoverBuilder.Tex("state_" + k + "_en")).ToArray();
            {
                // 11-13：目标牌按风格各一套（state_aim_<兵种>_<后缀>_<语言>）
                string[] ag = { "inf", "arch", "cav", "ha" };
                Texture2D AT(string n) => System.IO.File.Exists($"ArtSource/Cover/v6/alpha/{n}.png") ? CoverBuilder.Tex(n) : null;
                signs.aimZh = new Texture2D[Settings.Eras * 4]; signs.aimEn = new Texture2D[Settings.Eras * 4];
                for (int era = 1; era < Settings.Eras; era++) for (int gi = 0; gi < 4; gi++)
                {
                    signs.aimZh[era * 4 + gi] = AT($"state_aim_{ag[gi]}_{Settings.EraSuffix[era]}_zh");
                    signs.aimEn[era * 4 + gi] = AT($"state_aim_{ag[gi]}_{Settings.EraSuffix[era]}_en");
                }
            }
            signs.handleTex = CoverBuilder.Tex("hand_pinch");   // 把手 = 捏合图标（提示“捏这里”）
            // 11-10 用户：兵法里说主将，但看不到主将在哪 → 主将头顶金色“帅”字牌
            signs.generalZh = CoverBuilder.Tex("badge_general_zh"); signs.generalEn = CoverBuilder.Tex("badge_general_en");
            game.signs = signs;
            // 11-10 用户：手势难识别、常误触 → 沙盘右侧令架上三支令牌（冲锋 / 坚守 / 撤退），扔到沙盘上任意位置 = 对全军下令
            {
                var ro = new GameObject("OrderRack"); ro.transform.SetParent(go.transform, false);
                var rack = ro.AddComponent<OrderRack>();
                rack.game = game; game.orders = rack;
                // 11-11 第二轮：程序建模的令牌（Tools/make_token_models.py）：漆面牌身 + 刻槽嵌金，中英各一套；
                // 三支三色（朱 / 黑 / 靛，免拿错），URP Lit 漆面有反射高光、嵌金是金属
                const string tokDir = "Assets/_WarTable/Art/Models/Tokens";
                var lacq = new[] { new Color(0.42f, 0.05f, 0.03f), new Color(0.045f, 0.04f, 0.038f), new Color(0.05f, 0.075f, 0.2f) };
                for (int i = 0; i < 3; i++)
                {
                    string k = OrderRack.Keys[i];
                    foreach (var l in new[] { "zh", "en" })
                        if (AssetImporter.GetAtPath($"{tokDir}/token_{k}_{l}.fbx") is ModelImporter mi && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importNormals != ModelImporterNormals.Import))
                        { mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importNormals = ModelImporterNormals.Import; mi.SaveAndReimport(); }
                    rack.modelZh[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{tokDir}/token_{k}_zh.fbx");
                    rack.modelEn[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{tokDir}/token_{k}_en.fbx");
                    var c = lacq[i];
                    rack.lacquer[i] = BoardBuilder.Mat($"M_Token_{k}", "Universal Render Pipeline/Lit", mm =>
                    {
                        mm.SetColor("_BaseColor", c); mm.SetFloat("_Metallic", 0f); mm.SetFloat("_Smoothness", 1f);
                        TokenTex(mm, "lacquer", 0.3f);   // 11-12 用户：令牌缺少质感 → 刷痕 / 漆层深浅 / 细磨痕（Tools/make_token_textures.py）
                        mm.EnableKeyword("_EMISSION"); mm.SetColor("_EmissionColor", Color.black); mm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    });
                }
                rack.gold = BoardBuilder.Mat("M_Token_Gold", "Universal Render Pipeline/Lit", mm =>
                {
                    mm.SetColor("_BaseColor", new Color(0.86f, 0.62f, 0.26f)); mm.SetFloat("_Metallic", 1f); mm.SetFloat("_Smoothness", 1f);
                    TokenTex(mm, "gold", 0.35f);   // 拉丝 + 轻微锤痕
                });
            }
            // 指挥手势反馈（11-04）
            var gf = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(gf.GetComponent<Collider>());
            gf.name = "GestureFlash"; gf.transform.SetParent(go.transform, false);
            var gfR = gf.GetComponent<Renderer>(); gfR.shadowCastingMode = ShadowCastingMode.Off;
            gfR.sharedMaterial = CoverBuilder.Unlit("M_GestureFlash", game.tutorial.fistTex, Color.white, 3088);
            game.gestureFlash = go.AddComponent<GestureFlash>();
            game.gestureFlash.quad = gfR; game.gestureFlash.mr = game.mr; game.gestureFlash.editorEye = camT;
            {   // 打击反馈（11-08）：命中火星 / 格挡火花 / 阵亡炸开
                var hx = go.transform.Find("HitFx"); if (hx) Object.DestroyImmediate(hx.gameObject);
                var hfx = new GameObject("HitFx"); hfx.transform.SetParent(go.transform, false);
                game.hitFx = hfx.AddComponent<HitFx>();
                // 粒子要读顶点色（敌我颜色）→ 用 SoftParticle（篝火同款）
                game.hitFx.mat = BoardBuilder.Mat("M_HitFx", "WarTable/SoftParticle", m => { m.SetTexture("_MainTex", CoverBuilder.Dot()); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.renderQueue = 3075; });   // 不用叠加：亮草地上叠加色会被吃掉
            }
            game.gestureFlash.fist = game.tutorial.fistTex; game.gestureFlash.press = game.tutorial.pressTex; game.gestureFlash.beckon = game.tutorial.beckonTex;
            {
                string[] gs = { "charge", "hold", "retreat" }, ws = { "all", "inf", "arch", "cav", "ha", "some" };
                game.gestureFlash.badgesZh = new Texture2D[18]; game.gestureFlash.badgesEn = new Texture2D[18];
                for (int a = 0; a < 3; a++) for (int b = 0; b < 6; b++)
                {
                    game.gestureFlash.badgesZh[a * 6 + b] = CoverBuilder.Tex($"order_{gs[a]}_{ws[b]}_zh");
                    game.gestureFlash.badgesEn[a * 6 + b] = CoverBuilder.Tex($"order_{gs[a]}_{ws[b]}_en");
                }
                // 11-13：各风格自己的叫法（Tools/era_names.py → make_order_badges.py）；没有图的项留空，运行时退回上面那套
                Texture2D OT(string n) => System.IO.File.Exists($"ArtSource/Cover/v6/alpha/{n}.png") ? CoverBuilder.Tex(n) : null;
                game.gestureFlash.eraBadgesZh = new Texture2D[Settings.Eras * 18]; game.gestureFlash.eraBadgesEn = new Texture2D[Settings.Eras * 18];
                for (int era = 1; era < Settings.Eras; era++) for (int a = 0; a < 3; a++) for (int b = 1; b <= 4; b++)
                {
                    game.gestureFlash.eraBadgesZh[era * 18 + a * 6 + b] = OT($"order_{gs[a]}_{ws[b]}_{Settings.EraSuffix[era]}_zh");
                    game.gestureFlash.eraBadgesEn[era * 18 + a * 6 + b] = OT($"order_{gs[a]}_{ws[b]}_{Settings.EraSuffix[era]}_en");
                }
            }
            gf.SetActive(false);
            // 手部遮挡（11-04 用户：手不要被沙盘挡住）：只写深度的手形网格，露出透视画面里的真手
            var occ = go.AddComponent<HandOccluder>();
            occ.input = game.mr; occ.material = BoardBuilder.Mat("M_HandOccluder", "WarTable/HandOccluder");
            var snd = game.sound;
            snd.bgmCalm = C("BGM/bgm_calm", true); snd.bgmBattle = C("BGM/bgm_battle", true); snd.bgmTitle = C("BGM/bgm_title", true); snd.bgmDeploy = C("BGM/bgm_deploy", true);
            snd.hover = C("UI/ui_hover"); snd.press = C("UI/ui_press"); snd.page = C("UI/ui_page"); snd.stamp = C("UI/ui_stamp");
            snd.deny = C("UI/ui_deny"); snd.select = C("UI/ui_select"); snd.order = C("UI/ui_order"); snd.place = C("UI/ui_place");
            snd.drumBoom = C("UI/drum_boom"); snd.drumTom = C("UI/drum_tom");
            snd.swing = new[] { C("Battle/swing_0"), C("Battle/swing_1") };
            snd.hit = new[] { C("Battle/hit_0") };
            snd.clash = new[] { C("Battle/clash_0") };
            snd.bow = new[] { C("Battle/bow_0") };
            snd.arrowHit = new[] { C("Battle/arrowhit_0"), C("Battle/arrowhit_1") };
            // 11-11：哥特配色（帅旗 / 帐篷 / 三角旗 / 结算小旗）——放在最后，结算板等都搭好了再扫
            { var el = board.GetComponentInChildren<EraLook>(true); if (el) GothicEra.Setup(board, el); }
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorSceneManager.SaveScene(go.scene);
            Debug.Log($"[GameSetup] 玩法已装配：棋子 {view.prefabs.Count(p => p.prefab)}/{view.prefabs.Length}");
        }

        /// <summary>
        /// XR 装置（照《梦境玩具战》）：XR Origin（PXR_Manager 开眼动、MRRig 开透视 + 地面原点）/ XR Camera（TrackedPoseDriver、AudioListener）。
        /// 编辑器里这台相机不启用（SeatCamera 试玩），头显上由 MRInput.Active 判断。
        /// </summary>
        static Camera BuildXR(Transform root)
        {
            var origin = new GameObject("XR Origin").transform;
            origin.SetParent(root, false);
            var camGO = new GameObject("XR Camera");
            camGO.transform.SetParent(origin, false);
            camGO.transform.localPosition = new Vector3(0, 1.2f, 0.9f);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
            camGO.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;   // MR 透视下后处理会破坏 alpha
            camGO.AddComponent<AudioListener>();
            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            var pxr = origin.gameObject.AddComponent<ByteDance.PICO.XR.PXR_Manager>();
            pxr.eyeTracking = true;
            var pc = ByteDance.PICO.XR.PXR_ProjectSetting.GetProjectConfig();
            if (pc != null) { pc.handTracking = true; pc.eyeTracking = true; pc.videoSeeThrough = true; EditorUtility.SetDirty(pc); }
            origin.gameObject.AddComponent<MRRig>().cam = cam;
            return cam;
        }

        /// <summary>箭 / 投矛预制体：原型道具（1 单位 = 1 码），按棋子英雄比例放大，原件材质。</summary>
        static GameObject Missile(string model)
        {
            System.IO.Directory.CreateDirectory(MissileDir);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{DemoFigures.Dir}/props/{model}.fbx");
            var root = new GameObject(model);
            var holder = new GameObject("Scale").transform;
            holder.SetParent(root.transform, false);
            holder.localScale = Vector3.one * DemoFigures.Scale;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, holder);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var tex = System.IO.Directory.Exists($"{DemoFigures.Dir}/props/{model}_tex") ? System.IO.Directory.GetFiles($"{DemoFigures.Dir}/props/{model}_tex", "Color_*.png").Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).FirstOrDefault() : null;
            var mat = BoardBuilder.Mat($"M_Demo_{model}", "WarTable/MiniLit", m => { if (tex) m.SetTexture("_BaseMap", tex); });
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; }
            var pf = PrefabUtility.SaveAsPrefabAsset(root, $"{MissileDir}/{model}.prefab");
            Object.DestroyImmediate(root);
            return pf;
        }
    }
}
