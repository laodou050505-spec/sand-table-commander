using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WarTable.Game;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 封面 v6（10-31 用户选定概念 A：ArtSource/Cover/v6/cover_I_split_helm.png。用户给的参考图只借“高级感 + 神秘感”，要贴合本游戏风格，
    /// 不照搬碎玻璃、放射背景、白描边）。之前的兵符纹章版（土）、墨金书法版（“完全不对”）都已弃用。
    /// 独立的立体作品，不挂在沙盘下（封面阶段沙盘藏起来）。本地坐标：原点 = 构图中心，+Z 朝玩家，玩家左手边是 +X。
    ///   左 —— 双面战盔（P2 Cover_SplitHelm：左半王国骑士盔、右半兽人角盔，沿裂缝崩开），背后一泼朱砂，头顶一束暖光 + 光里的细沙，
    ///         行军地图残片和裂缝崩出的碎块在周围缓慢漂浮；
    ///   右 —— 书法标题（骨白，身后一层朱红错位重影）+ 英文，下面菜单斜排错落：每项一道墨笔刷底，被看着时刷出一道骨白笔刷、字变炭黑。
    ///   罩子 —— 半径 3 m、跟着头，把真实房间压暗（MenuCover 淡入淡出）。
    /// 贴图：ArtSource/Cover/v6/gen（Tools/gen_images.py 队列）→ Tools/ink_alpha.py 抠透明 → ArtSource/Cover/v6/alpha → Art/Textures/Cover6；
    /// 立体字：书法字稿 / Cinzel 英文 → Tools/cover_v5_meshes.sh → Art/Models/Title/v5|v6。交互在 Runtime/Game/MenuCover.cs。菜单 WarTable/6。
    /// </summary>
    public static class CoverBuilder
    {
        public const string Root = "Cover";
        const string Dir5 = "Assets/_WarTable/Art/Models/Title/v5", Dir6 = "Assets/_WarTable/Art/Models/Title/v6";
        const string TexDir = "Assets/_WarTable/Art/Textures/Cover6";
        const string StudioPath = "Assets/_WarTable/Art/Textures/CoverStudio.exr";
        const string Lit = "Universal Render Pipeline/Lit";

        static Vector3 HelmAt = new(0.1f, -0.01f, 0f);
        static float HelmH = 0.34f;

        /// <summary>标题版式（11-02 用户：标题整体排版有点欠缺）。坐标为封面本地米，玩家看过去的右边是 −x。</summary>
        public static int TitleVariant = 5;
        class Layout
        {
            public (string f, float h, float x, float y)[] chars;
            public float tilt, enW, enRot, sealRot, menuDy, z, menuScale = 1f;
            public bool centered;   // 一体构图：战盔居中，标题压在盔下，菜单一排在最下面
            public Vector3[] menuAt;  // centered 时每项的位置（x, y, 旋转）
            public Vector2 en; public Vector3 rule, seal;   // rule：x, y, 长度；seal：x, y, 大小（0 = 不放）
            // free：每个元素单独给位置（x, y, z 都给），用于有冲击力的非对称构图
            public bool free; public Vector3 helmPos; public float helmH, helmYaw, helmRoll;
            public (string f, Vector3 p, float h, float rot)[] fchars;
            public Vector3 enPos, sealPos; public float sealSize;
            public Vector3 inkPos; public float inkSize, inkRot;
            public (Vector3 p, float scale, float rot)[] fmenu;
            public (string tex, Vector3 p, float size, float rot)[] fmaps;
            public (string team, Vector3 bottom, Vector3 top, float w, float h)[] flags;
            public bool noTagline;   // 不放英文副标题
        }
        public static int LayoutCount => TitleLayouts.Length;
        static readonly Layout[] TitleLayouts =
        {
            // 0 原版：一行微扬
            new Layout { chars = new[] { ("jiang", 0.092f, -0.077f, 0.136f), ("jun", 0.08f, -0.157f, 0.126f), ("sha", 0.078f, -0.235f, 0.134f), ("pan", 0.09f, -0.316f, 0.127f) },
                         tilt = 2.5f, enW = 0.25f, en = new Vector2(-0.2f, 0.065f) },
            // 1 竖排两列（右列“将军”先读，左列“沙盘”下错半字），英文竖排贴在最右，朱印压在左列脚下
            new Layout { chars = new[] { ("jiang", 0.095f, -0.3f, 0.2f), ("jun", 0.088f, -0.3f, 0.102f), ("sha", 0.088f, -0.197f, 0.15f), ("pan", 0.095f, -0.197f, 0.052f) },
                         enW = 0.22f, en = new Vector2(-0.372f, 0.13f), enRot = 90f, seal = new Vector3(-0.255f, -0.025f, 0.042f), sealRot = -4f, menuDy = -0.07f },
            // 2 主字放大：“将”占两行高，“军沙盘”小一号横排挂在右上，下面细朱线 + 英文，朱印收尾
            new Layout { chars = new[] { ("jiang", 0.16f, -0.12f, 0.15f), ("jun", 0.068f, -0.235f, 0.192f), ("sha", 0.066f, -0.305f, 0.186f), ("pan", 0.072f, -0.375f, 0.19f) },
                         enW = 0.2f, en = new Vector2(-0.305f, 0.112f), rule = new Vector3(-0.305f, 0.137f, 0.2f), seal = new Vector3(-0.392f, 0.062f, 0.056f), sealRot = 4f, menuDy = -0.045f },
            // 3 横排精修：大小、高低对比拉开，英文两端对齐到字宽，上面一道细朱线，朱印落在行尾
            new Layout { chars = new[] { ("jiang", 0.108f, -0.1f, 0.158f), ("jun", 0.078f, -0.188f, 0.18f), ("sha", 0.082f, -0.266f, 0.142f), ("pan", 0.102f, -0.352f, 0.162f) },
                         tilt = 1.5f, enW = 0.29f, en = new Vector2(-0.226f, 0.074f), rule = new Vector3(-0.226f, 0.093f, 0.29f), seal = new Vector3(-0.392f, 0.2f, 0.054f), sealRot = -4f, menuDy = -0.02f },
            // 4 一体构图（11-02 用户：整个封面要是一个主体，不要分家）：战盔居中，标题压在盔的下沿前面，英文 + 朱印收在标题下，三个菜单一排落在最下面
            new Layout { centered = true, chars = new[] { ("jiang", 0.074f, 0.114f, -0.104f), ("jun", 0.064f, 0.04f, -0.098f), ("sha", 0.064f, -0.032f, -0.102f), ("pan", 0.074f, -0.108f, -0.1f) },
                         z = 0.03f, enW = 0.22f, en = new Vector2(0.003f, -0.158f), seal = new Vector3(-0.152f, -0.153f, 0.026f), sealRot = -4f,
                         menuScale = 0.62f, menuAt = new[] { new Vector3(0.2f, -0.222f, 2f), new Vector3(0f, -0.228f, -1.5f), new Vector3(-0.2f, -0.222f, 1.5f) } },
            // 5 斜劈（11-02 用户：居中版单调、传统、没冲击力；盔后要两面交叉的旗）：一个特大的“将”字起笔，字逐级缩小沿 ~29° 斜线落到印章，
            //   和“盔 + 交叉长幡”的一竖形成对抗；朱砂弧顺着这一笔穿过盔后和四个字，“军”压在盔下颌前，菜单沿同一斜向退台。
            //   三个独立方案 + 评审定稿（工作流 cover-bold-layout）。x 正 = 玩家左边。
            // 5 斜劈（11-02 用户：居中版单调、传统、没冲击力；盔后要两面交叉的旗）：一个特大的“将”字起笔，字逐级缩小沿斜线落到印章，
            //   和“盔 + 交叉长幡”的一竖形成对抗；朱砂弧顺着这一笔穿过盔后和四个字。x 正 = 玩家左边。
            //   11-04 用户：左边三个按钮要斜着眼睛看 → 整体收到视线 ±14° 以内：标题斜线收窄（“将”从左 21° 收到 13°），
            //   菜单从左下角挪到正中下方（水平 0°、比视线低 13°–22°），竖排三项，眼睛往下一瞟就到，不用转头。
            new Layout { free = true, helmPos = new Vector3(-0.02f, 0.15f, -0.02f), helmH = 0.3f, helmYaw = -15f, helmRoll = 6f,
                         fchars = new[] { ("jiang", new Vector3(0.19f, 0.09f, 0.08f), 0.2f, 6f), ("jun", new Vector3(0.04f, -0.005f, 0.1f), 0.13f, -4f),
                                          ("sha", new Vector3(-0.085f, -0.055f, 0.08f), 0.1f, 5f), ("pan", new Vector3(-0.19f, -0.09f, 0.09f), 0.085f, -3f) },
                         enPos = new Vector3(-0.215f, -0.168f, 0.085f), enW = 0.2f, enRot = 4f, sealPos = new Vector3(-0.29f, -0.112f, 0.085f), sealSize = 0.06f, sealRot = -8f,
                         inkPos = new Vector3(0.04f, 0.05f, -0.1f), inkSize = 0.6f, inkRot = 26f,
                         // 11-11 用户：下面按钮排版太死板 → 五项竖排同大改三行错落：“出征”放大独占一行（主按钮）；“时代 / 教学”并排、高低错开；
                         //   “设置 / 退出”小一号并排；各带 2–3.5° 转角、前后错一点。竖向从视线下 16°–30° 收到 15°–23°，左右不超过 ±13°。
                         //   顺序 = 菜单项顺序：出征、时代、教学、设置、退出（x 正 = 玩家左边）
                         // 11-13 用户：“封面整体可以往上挪一点，那几个按钮的位置有点偏”。量出来的毛病：第三行（设置 / 退出）的中点比上两行偏右 0.75 cm，
                         //   左右两列离中线不一样远（+7.0 / −8.5、+10.5 / −10.0）；“出征”的墨笔和英文副标题的墨笔在同一高度、叠在一起。
                         //   → 两行都按中线对称（±10.4、±7.8 cm，字心对字心）；整组下移 5 cm，“出征”落到副标题那道墨下面（3 cm 时两道墨的尾巴还搭在一起），标题一组和按钮一组分开；
                         //   高低错落和转角留着。封面整体上移见 MenuCover.below。
                         fmenu = new[] { (new Vector3(0.0f, -0.2f, 0.125f), 1.3f, 2f),
                                         (new Vector3(0.104f, -0.257f, 0.12f), 0.92f, -3f), (new Vector3(-0.104f, -0.264f, 0.12f), 0.92f, 2.5f),
                                         (new Vector3(0.078f, -0.312f, 0.115f), 0.74f, 3.5f), (new Vector3(-0.078f, -0.308f, 0.115f), 0.74f, -2f) },
                         fmaps = new[] { ("map_0", new Vector3(-0.42f, 0.24f, -0.06f), 0.08f, 18f), ("map_1", new Vector3(0.42f, 0.2f, 0.02f), 0.065f, -25f),
                                         ("map_2", new Vector3(0.1f, 0.34f, 0.1f), 0.055f, 40f), ("map_0", new Vector3(-0.4f, -0.02f, -0.1f), 0.07f, -12f) },
                         flags = new[] { ("kingdom", new Vector3(-0.26f, -0.1f, -0.17f), new Vector3(0.2f, 0.4f, -0.17f), 0.15f, 0.3f),
                                         ("clan", new Vector3(0.18f, -0.1f, -0.18f), new Vector3(-0.28f, 0.4f, -0.18f), 0.15f, 0.28f) } },
        };

        [MenuItem("WarTable/6 搭封面")]
        public static void Build()
        {
            var old = GameObject.Find(Root);
            if (old) Object.DestroyImmediate(old);
            var L = TitleLayouts[Mathf.Clamp(TitleVariant, 0, TitleLayouts.Length - 1)];
            if (L.free) { HelmAt = L.helmPos; HelmH = L.helmH; }
            else if (L.centered) { HelmAt = new Vector3(0f, 0.085f, 0f); HelmH = 0.3f; }
            else { HelmAt = new Vector3(0.1f, -0.01f, 0f); HelmH = 0.34f; }

            var studio = BakeStudio();
            var root = new GameObject(Root).transform;
            var rp = root.gameObject.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Custom; rp.customBakedTexture = studio;
            rp.size = Vector3.one * 2f; rp.importance = 10; rp.intensity = 0.7f;   // 11-06 用户：不要强曝光（金属顶部反射发白）

            // ── 罩子：压暗房间（最先画的透明件；不透明的封面件在它前面）
            var dim = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(dim.GetComponent<Collider>());
            dim.name = "Dimmer"; dim.transform.SetParent(root, false); dim.transform.localScale = Vector3.one * 6f;
            dim.GetComponent<Renderer>().sharedMaterial = Unlit("M_C6_Dimmer", null, new Color(0, 0, 0, 0.82f), 2990, cull: CullMode.Front);
            dim.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ── 左：战盔 + 朱砂 + 顶光 + 细沙 + 漂浮件
            var helm = Helm(root);
            if (L.free) helm.localRotation = Quaternion.Euler(0, L.helmYaw, L.helmRoll);
            Transform ink;
            if (L.free) ink = Quad(root, "InkSplash", Tex("ink_splash"), new Color(1f, 1f, 1f, 0.9f), L.inkPos, L.inkSize, 3001, rotZ: L.inkRot);
            else ink = Quad(root, "InkSplash", Tex("ink_splash"), new Color(1f, 1f, 1f, 0.9f), HelmAt + new Vector3(0.01f, 0.03f, -0.11f), 0.5f, 3001, rotZ: 14f);
            if (L.flags != null) foreach (var fl in L.flags) Banner(root, fl.team, fl.bottom, fl.top, fl.w, fl.h);
            Shaft(root);
            Sand(root);
            var drifters = new List<Transform>();
            var maps = L.free ? L.fmaps : L.centered
                ? new[] { ("map_0", new Vector3(0.25f, 0.17f, -0.05f), 0.085f, 18f), ("map_1", new Vector3(-0.24f, 0.09f, -0.02f), 0.075f, -24f), ("map_2", new Vector3(-0.17f, 0.27f, -0.08f), 0.055f, 35f), ("map_0", new Vector3(0.22f, -0.03f, 0.03f), 0.05f, -40f) }
                : new[] { ("map_0", new Vector3(0.33f, 0.12f, -0.03f), 0.1f, 18f), ("map_1", new Vector3(0.3f, -0.17f, 0.05f), 0.085f, -24f), ("map_2", new Vector3(0.05f, 0.26f, -0.07f), 0.07f, 35f), ("map_0", new Vector3(0.02f, -0.25f, 0.02f), 0.06f, -40f) };
            int qi = 3010;
            foreach (var (tex, at, size, rot) in maps)
                drifters.Add(Curled(root, "Map_" + tex, Tex(tex), new Color(0.82f, 0.78f, 0.7f, 1f), at, size, rot, qi++));
            // 裂缝碎块（程序生成的薄片）看着像渲染错误，先不放；以后用 P2 碎片模型

            // ── 右：标题（骨白 + 朱红错位重影）+ 英文 + 朱印。版式见 TitleVariant
            var bone = Pbr("M_C6_Bone", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 2012);
            var boneSide = Pbr("M_C6_BoneSide", new Color(0.22f, 0.2f, 0.18f), 0f, 0.3f, 2010);
            var red = Unlit("M_C6_Ghost", null, new Color(0.62f, 0.09f, 0.06f, 0.85f), 3000, cull: CullMode.Back);
            var title = new GameObject("Title").transform; title.SetParent(root, false);
            var introChars = new List<Transform>(); var introGhosts = new List<Transform>();
            title.localPosition = new Vector3(0, 0, L.centered ? L.z : 0.02f);
            var ghost = new GameObject("Ghost").transform; ghost.SetParent(title, false);
            ghost.localPosition = new Vector3(-0.005f, -0.004f, -0.012f);   // 重影：往右下错开、往后退
            title.localRotation = Quaternion.Euler(0, 0, L.tilt);
            if (L.free)
            {
                title.localPosition = Vector3.zero; title.localRotation = Quaternion.identity;
                foreach (var (f, pp, h, rot) in L.fchars)
                    foreach (var (par, fm, sm) in new[] { (title, bone, boneSide), (ghost, red, red) })
                    {
                        var pc = Piece(par, Dir5, f, h, pp, fm, sm);
                        if (!pc) continue;
                        var c0 = Bounds(pc.gameObject).center; pc.RotateAround(c0, root.forward, rot);
                        var pivot = new GameObject("Char_" + f).transform; pivot.SetParent(par, false);
                        pivot.position = c0; pc.SetParent(pivot, true);
                        (par == title ? introChars : introGhosts).Add(pivot);
                    }
            }
            else foreach (var (f, h, x, y) in L.chars)
            {
                Piece(title, Dir5, f, h, new Vector3(x, y, 0), bone, boneSide);
                Piece(ghost, Dir5, f, h, new Vector3(x, y, 0), red, red);
            }
            // 11-07 用户：英文版的标题也要是英文 → 另建一组 SAND / TABLE / COMMANDER（Cinzel 粗体挤出，带朱红重影），沿同一条斜线逐级排开；
            // MenuCover 按语言二选一，开场落字动画也用对应那组
            var titleEnRoot = new GameObject("TitleEn").transform; titleEnRoot.SetParent(root, false);
            var ghostEn = new GameObject("GhostEn").transform; ghostEn.SetParent(titleEnRoot, false);
            ghostEn.localPosition = new Vector3(-0.004f, -0.003f, -0.01f);
            var introCharsEn = new List<Transform>(); var introGhostsEn = new List<Transform>();
            foreach (var (f, pp, h, rot) in new[] { ("en_sand", new Vector3(0.155f, 0.088f, 0.08f), 0.078f, 4f), ("en_table", new Vector3(0.02f, 0.008f, 0.1f), 0.07f, -2f),
                                                     ("en_commander", new Vector3(-0.095f, -0.058f, 0.09f), 0.056f, 2f) })
                foreach (var (par, fm, sm) in new[] { (titleEnRoot, bone, boneSide), (ghostEn, red, red) })
                {
                    var pc = Piece(par, Dir6, f, h, pp, fm, sm);
                    if (!pc) continue;
                    var c0 = Bounds(pc.gameObject).center; pc.RotateAround(c0, root.forward, rot);
                    var pivot = new GameObject("Word_" + f).transform; pivot.SetParent(par, false);
                    pivot.position = c0; pc.SetParent(pivot, true);
                    (par == titleEnRoot ? introCharsEn : introGhostsEn).Add(pivot);
                }
            var enT = L.noTagline ? null : Piece(root, Dir5, "tagline", L.enW, L.free ? L.enPos : new Vector3(L.en.x, L.en.y, L.centered ? L.z : 0.02f), bone, boneSide, byHeight: false);
            if (enT && L.enRot != 0f)
            {
                var c0 = Bounds(enT.gameObject).center;
                enT.RotateAround(c0, root.forward, L.enRot);
            }
            if (L.rule.z > 0f)   // 细朱线：中英之间的分隔
                Prim(root, PrimitiveType.Cube, red, new Vector3(L.rule.x, L.rule.y, 0.02f), new Vector3(L.rule.z, 0.0022f, 0.001f)).name = "Rule";
            if (L.seal.z > 0f)
                Quad(root, "Seal", Tex("seal"), new Color(0.85f, 0.8f, 0.78f, 0.95f), new Vector3(L.seal.x, L.seal.y, L.centered ? L.z + 0.005f : 0.025f), L.seal.z, 3060, rotZ: L.sealRot);
            Transform seal = null, taglineBrush = null;
            if (L.free)
            {
                seal = Quad(root, "Seal", Tex("seal"), new Color(0.85f, 0.8f, 0.78f, 0.95f), L.sealPos, L.sealSize, 3060, rotZ: L.sealRot);
                // 11-06 用户：标题有些空 → 英文副标题压一道浓墨笔刷底，接在“盘”字和朱印下方，把右下这块空地收住
                if (!L.noTagline) taglineBrush = Quad(root, "TaglineBrush", Tex("brush_2"), new Color(0.08f, 0.07f, 0.065f, 0.92f), L.enPos + new Vector3(-0.005f, 0.002f, -0.01f), L.enW * 1.35f, 3002, rotZ: L.enRot);
            }

            // ── 菜单：斜排错落
            var items = new List<MenuCover.Item>();
            // 主菜单（第 0 页）：出征 / 设置 / 退出；设置页（第 1 页，11-02 用户：语言放进设置）：语言 / 战速 / 暗度 / 返回
            var menu = new (MenuCover.Action a, string zh, string en, float x, float y, float rot)[]
            {
                (MenuCover.Action.Begin, "word_chuzheng", "menu_begin", -0.165f, -0.005f + L.menuDy, 4f),
                (MenuCover.Action.Era, "word_shidai", "menu_era", -0.18f, -0.025f + L.menuDy, 0f),   // 11-11 用户：主菜单只写“时代”，点进去选（第 2 页）
                (MenuCover.Action.Tutorial, "word_jiaoxue", "menu_tutorial", -0.2f, -0.044f + L.menuDy, -2f),
                (MenuCover.Action.Settings, "word_shezhi", "menu_settings", -0.228f, -0.083f + L.menuDy, -3.5f),
                (MenuCover.Action.Quit, "word_tuichu", "menu_quit", -0.19f, -0.162f + L.menuDy, 2.5f),
                (MenuCover.Action.Language, "word_zhongwen", "menu_english", 0f, 0f, 0f),
                (MenuCover.Action.Speed, "word_zhansu", "menu_speed", 0f, 0f, 0f),
                (MenuCover.Action.Dim, "word_andu", "menu_dim", 0f, 0f, 0f),
                (MenuCover.Action.Replace, "word_dingwei", "menu_dingwei", 0f, 0f, 0f),   // 11-14 用户：设置里要能重新识别沙盘位置
                (MenuCover.Action.Back, "word_fanhui", "menu_back", 0f, 0f, 0f),
                // 时代页（第 2 页）：每个时代一项（下标 = Settings.Era），当前选中的旁边标朱红“当前”；最后返回
                // 11-13 用户：“这几个风格的名称都改一下” → 不再按年代叫（中古 / 哥特 / 现代 / 未来），每个风格一个名字：剑盾 / 铁誓 / 硝烟 / 星尘
                //   （BLADES / IRON OATH / GUNSMOKE / STARDUST）。字稿和立体字：Tools/make_era_words.py，改名改那里的 NAMES 再跑一遍
                (MenuCover.Action.EraPick, "word_jiandun", "menu_jiandun", 0f, 0f, 0f),
                (MenuCover.Action.EraPick, "word_tieshi", "menu_tieshi", 0f, 0f, 0f),
                // 11-12 用户：现代 / 未来兵种以后再做——先放进来，点了只说“敬请期待”（下标 ≥ Settings.Eras）
                (MenuCover.Action.EraPick, "word_xiaoyan", "menu_xiaoyan", 0f, 0f, 0f),
                (MenuCover.Action.EraPick, "word_xingchen", "menu_xingchen", 0f, 0f, 0f),
                (MenuCover.Action.EraPick, "word_shenkong", "menu_shenkong", 0f, 0f, 0f),   // 11-13 第五个风格：深空（Settings.EraReady[4] 为 false 时点了只亮“敬请期待”）
                (MenuCover.Action.Back, "word_fanhui", "menu_back", 0f, 0f, 0f),
            };
            var curMat = Pbr("M_C6_MenuCurrent", new Color(0.78f, 0.2f, 0.14f), 0f, 0.35f, 3050);
            // 底：三道不同的墨笔刷；亮底用最宽最实的那道（brush_1），刷出来要能铺满字的区域，字变炭黑压在上面
            var inks = new[] { Tex("brush_0"), Tex("brush_2"), Tex("brush_0") }; var hi = Tex("brush_1");
            // 笔刷贴图的墨迹不在图正中（透明度纵向重心：brush_0 在上 1/3、brush_2 居中、brush_1 在下 1/3），
            // 按重心把每道笔刷挪到字中心 TextY 上：y = TextY − (0.5 − 重心) × 高宽比 × 宽
            const float TextY = 0.011f, HiY = TextY + 0.0164f;
            var inkY = new[] { TextY - 0.0173f, TextY - 0.003f, TextY - 0.0173f };
            var txt = Pbr("M_C6_MenuText", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 3050);
            for (int i = 0; i < menu.Length; i++)
            {
                var m = menu[i];
                const int MainCount = 5;   // 主菜单项数（11-10 加“时代”）
                const int SettingsCount = 5;   // 语言 / 战速 / 暗度 / 重新定位 / 返回
                int page = i < MainCount ? 0 : i < MainCount + SettingsCount ? 1 : 2;
                int slot = page == 0 ? i : page == 1 ? i - MainCount : i - MainCount - SettingsCount;
                var it = new GameObject("Item_" + m.a).transform; it.SetParent(root, false);
                if (page == 0)
                {
                    it.localPosition = new Vector3(m.x, m.y, 0.03f);
                    it.localRotation = Quaternion.Euler(0, 0, m.rot);
                    if (L.centered && i < L.menuAt.Length) { var a = L.menuAt[i]; it.localPosition = new Vector3(a.x, a.y, L.z - 0.01f); it.localRotation = Quaternion.Euler(0, 0, a.z); it.localScale = Vector3.one * L.menuScale; }
                    if (L.free && i < L.fmenu.Length) { var fm = L.fmenu[i]; it.localPosition = fm.p; it.localRotation = Quaternion.Euler(0, 0, fm.rot); it.localScale = Vector3.one * fm.scale; }
                }
                else
                {
                    // 设置页四项：沿主菜单同一斜向退台，从主菜单第一项的位置起排，间距收紧一点
                    Vector3 p0, step; float rot, sc;
                    if (L.free) { var f0 = L.fmenu[0]; p0 = f0.p; /* 11-13：主菜单下移后从“出征”的位置起排（原来再上提 2.5 cm，第二项正好压在英文副标题那道墨上） */ step = new Vector3(0f, -0.041f, 0f);   /* 11-14：设置页 5 项——行距 4.7 → 4.1 cm、字缩到 0.82（行距 / 字高的比例和原来一样），最低一项在视线下 25° */ rot = 0f; sc = 0.82f; }   // 11-11：项缩放现在真的作用到字上了，设置 / 时代页保持原来的视觉大小
                    else if (L.centered) { var a0 = L.menuAt[0]; p0 = new Vector3(a0.x, a0.y + 0.06f, L.z - 0.01f); step = new Vector3(-0.012f, -0.055f, 0); rot = 0f; sc = L.menuScale; }
                    else { p0 = new Vector3(menu[0].x, menu[0].y, 0.03f); step = new Vector3(-0.02f, -0.062f, 0); rot = 1f; sc = 0.9f; }
                    it.localPosition = p0 + step * slot; it.localRotation = Quaternion.Euler(0, 0, rot); it.localScale = Vector3.one * sc;
                    if (page == 2 && L.free)
                    {
                        // 11-12：时代页 4 个时代 + 返回，竖排 5 项太挤（中文字比行距还高）→ 2×2 网格（中古 哥特 / 现代 未来），返回单独一行；
                        // “当前 / 敬请期待”标记放在字的正下方（见下）。x 正 = 玩家左边
                        var f0 = L.fmenu[0];
                        // 11-13：五个风格 + 返回 = 3 行 × 2 列，“返回”占最后一格（原来 2×2 + 底下单独一行返回）；行距 7.4 → 6.6 cm，最低的标记仍在视线下 25° 以内
                        int row = slot / 2, col = slot % 2; bool back = slot >= Settings.Eras;
                        it.localPosition = new Vector3(col == 0 ? 0.094f : -0.094f, f0.p.y + 0.004f - row * 0.066f, f0.p.z);   // 两列 ±9.4 cm（11-13 换了新名字，字比原来宽，±8 cm 时两列的墨笔连在一起）
                        it.localRotation = Quaternion.Euler(0, 0, back ? 0f : (slot % 3 == 0 ? 1.5f : -1.5f)); it.localScale = Vector3.one * (back ? 0.78f : 0.92f);
                    }
                }
                // 11-11：Piece 按世界尺寸定字高，会把项的缩放抵消掉（大小对比出不来）→ 子物体先按 1 倍搭好，最后再套上这一项的缩放
                var itemScale = it.localScale; it.localScale = Vector3.one;
                bool hasPips = m.a == MenuCover.Action.Speed || m.a == MenuCover.Action.Dim;
                float tx = hasPips ? 0.022f : 0f;   // 有档位点的项：字往左挪，右边留出三颗点
                var inkQ = Quad(it, "Ink" + i, inks[i % 3], new Color(0.05f, 0.045f, 0.04f, 0.93f), new Vector3(-0.01f, inkY[i % 3], -0.004f), 0.24f, 3020 + i);   // 笔刷图案重心偏起笔一侧，往右挪让字落在墨最浓处
                var h = Quad(it, "Highlight" + i, hi, new Color(0.93f, 0.9f, 0.83f, 0.97f), new Vector3(-0.008f, HiY, -0.002f), 0.25f, 3030 + i, rotZ: -1.5f);
                var hr = h.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Reveal", 0f); hr.SetPropertyBlock(mpb);
                var zh = Piece(it, Dir6, m.zh, 0.045f, new Vector3(tx, TextY, 0.004f), txt, txt);
                var en = Piece(it, Dir6, m.en, hasPips ? 0.02f : 0.024f, new Vector3(tx, TextY, 0.004f), txt, txt, byHeight: true);
                int eraIdx = m.a == MenuCover.Action.EraPick ? i - MainCount - SettingsCount : -1;
                Transform curZh = null, curEn = null;
                if (eraIdx >= 0)
                {
                    // “当前”标记：朱红小字，挂在字的右边（封面本地 −x = 玩家右手边）
                    bool soon = !Settings.Ready(eraIdx);   // 还没做的时代：标记换成“敬请期待 / COMING SOON”（点了才亮）
                    // 11-12：标记挂在字的正下方（时代页改成两列，挂在右边会压到另一列）
                    float my = TextY - 0.036f;
                    curZh = soon ? Piece(it, Dir6, "word_jingqing", 0.019f, new Vector3(0f, my, 0.004f), curMat, curMat)
                                 : Piece(it, Dir6, "word_dangqian", 0.019f, new Vector3(0f, my, 0.004f), curMat, curMat);
                    curEn = soon ? Piece(it, Dir6, "menu_soon", 0.0105f, new Vector3(0f, my + 0.003f, 0.004f), curMat, curMat, byHeight: true)
                                 : Piece(it, Dir6, "menu_current", 0.0105f, new Vector3(0f, my + 0.003f, 0.004f), curMat, curMat, byHeight: true);
                }
                // 11-07：英文单词（TUTORIAL / SETTINGS）比两个汉字宽，笔刷底和看中时的亮笔刷按中英里较宽的那个横向拉长（只拉宽、不变高）
                {
                    float tw = 0f;
                    foreach (var pc in new[] { zh, en }) if (pc) tw = Mathf.Max(tw, Bounds(pc.gameObject).size.x / Mathf.Max(1e-4f, it.lossyScale.x));
                    float need = tw * 1.3f;
                    if (need > 0.24f) { var sc = inkQ.localScale; inkQ.localScale = new Vector3(sc.x * need / 0.24f, sc.y, sc.z); }
                    if (page == 2 && need < 0.24f) { float w2 = Mathf.Max(need, 0.15f); var sc = inkQ.localScale; inkQ.localScale = new Vector3(sc.x * w2 / 0.24f, sc.y, sc.z); var sh = h.localScale; h.localScale = new Vector3(sh.x * w2 / 0.24f, sh.y, sh.z); }
                    if (need > 0.25f) { var sc = h.localScale; h.localScale = new Vector3(sc.x * need / 0.25f, sc.y, sc.z); }
                }
                Renderer[] pips = null;
                if (hasPips)
                {
                    // 三颗墨点（压扁的小球），封面本地 −x = 玩家右手边
                    pips = new Renderer[Settings.Levels];
                    for (int k = 0; k < pips.Length; k++)
                    {
                        var d = Prim(it, PrimitiveType.Sphere, txt, new Vector3(-0.072f - 0.014f * k, TextY, 0.004f), new Vector3(0.0075f, 0.0075f, 0.003f));
                        d.name = "Pip" + k; pips[k] = d.GetComponent<Renderer>(); pips[k].shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
                var hitRect = HitRect(it, new[] { zh, en }, pips, TextY);
                it.localScale = itemScale;
                items.Add(new MenuCover.Item
                {
                    action = m.a, page = page, pips = pips, root = it, hit = hitRect, highlight = hr, zh = zh ? zh.gameObject : null, en = en ? en.gameObject : null,
                    texts = new[] { zh, en }.Where(x => x).SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray(),
                    era = eraIdx, currentZh = curZh ? curZh.gameObject : null, currentEn = curEn ? curEn.gameObject : null,
                });
            }

            // 搭建时就按当前语言只显示一种（运行时 MenuCover 再按语言切换）
            bool isZh = Loc.Current == Loc.Lang.Zh;
            foreach (var x in items) { if (x.zh) x.zh.SetActive(isZh); if (x.en) x.en.SetActive(!isZh); if (x.page != 0) x.root.gameObject.SetActive(false); }
            var mc = root.gameObject.AddComponent<MenuCover>();
            mc.below = -0.04f;   // 11-13 用户：封面整体往上挪一点 → 封面原点从“眼睛下 4 cm”改到“眼睛上 4 cm”（整体抬 8 cm，约 5.6°；按钮因为另外下移了 5 cm，净抬约 2°）
            mc.items = items.ToArray(); mc.helm = helm; mc.drifters = drifters.ToArray();
            mc.dimmer = dim.GetComponent<Renderer>(); mc.titleEn = enT ? enT.gameObject : null;
            mc.title = title; mc.introChars = introChars.ToArray(); mc.introGhosts = introGhosts.ToArray();
            mc.titleEnRoot = titleEnRoot; mc.introCharsEn = introCharsEn.ToArray(); mc.introGhostsEn = introGhostsEn.ToArray(); mc.taglineBrush = taglineBrush ? taglineBrush.gameObject : null;
            mc.introInk = ink ? ink.GetComponent<Renderer>() : null; mc.introSeal = seal;
            // 重搭封面后旧 Cover 已销毁：场景里已装配的玩法（WarTable/7）要重新指向新封面，否则点“出征”游戏收不到
            var game = Object.FindAnyObjectByType<WarGame>(FindObjectsInactive.Include);
            if (game)
            {
                game.cover = root.gameObject; game.menu = mc;
                mc.mr = game.mr; mc.editorEye = game.previewCamera ? game.previewCamera.transform : null;
                EditorUtility.SetDirty(game);
            }
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[Cover] 封面 v6 已搭好");
        }

        // ---------------------------------------------------------------- 战盔与氛围

        /// <summary>双面战盔：P2 Cover_SplitHelm（缺时放一个占位球），高 27 cm，正面朝玩家略向右转（让兽人那半也看得到）。</summary>
        static Transform Helm(Transform root)
        {
            var t = new GameObject("Helm").transform; t.SetParent(root, false);
            t.localPosition = HelmAt; t.localRotation = Quaternion.Euler(0, -12f, 0);
            // 11-02 精修版（Tools/helm_retex/）：P2 网格焊接接缝后用 xatlas 重展 UV，按材质分区（银钢 / 黑铁 / 鎏金 / 角 / 骨 / 古铜铆钉）
            // 重画整套 PBR 贴图——P2 原贴图把反光直接画进了颜色、碎成几千块，看着像塑料。正面朝 −X，同 P2 惯例转 90°。
            // 11-06 用户：头盔都是破面 → 换 P2 Cover_SplitHelm3（骷髅改成闭合浮雕、整体实心，木座已在 Blender 里去掉）；缺时退回旧版
            const string dir3 = "Assets/_WarTable/Art/Models/Cover/Helm3";
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{dir3}/helm3.fbx")) return Helm3(t, dir3);
            const string dir = "Assets/_WarTable/Art/Models/Cover/Helm";
            if (AssetImporter.GetAtPath($"{dir}/helm.fbx") is ModelImporter mi && (mi.importNormals != ModelImporterNormals.Calculate || mi.normalSmoothingAngle != 60f || mi.importTangents != ModelImporterTangents.CalculateMikk))
            {
                mi.importNormals = ModelImporterNormals.Calculate; mi.normalSmoothingAngle = 60f; mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.SaveAndReimport();
            }
            Texture2D T(string f, bool srgb, bool normal = false)
            {
                var path = $"{dir}/{f}.png";
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.sRGBTexture != srgb || (ti.textureType == TextureImporterType.NormalMap) != normal || ti.anisoLevel != 8))
                {
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = srgb; ti.anisoLevel = 8; ti.maxTextureSize = 2048; ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/helm.fbx");
            if (model)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, t);
                inst.transform.localRotation = Quaternion.Euler(0, 90, 0) * inst.transform.localRotation;
                var b0 = Bounds(inst);
                inst.transform.localScale *= HelmH / Mathf.Max(1e-4f, b0.size.y / t.lossyScale.y);
                var b = Bounds(inst);
                inst.transform.position += t.position - b.center;
                var mat = BoardBuilder.Mat("M_C6_Helm", Lit, m =>
                {
                    m.SetTexture("_BaseMap", T("helm_albedo", true)); m.SetColor("_BaseColor", Color.white);
                    m.SetTexture("_MetallicGlossMap", T("helm_metal", false)); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f); m.SetFloat("_BumpScale", 0.55f);   // 11-04 用户：头盔看着破——法线细碎起伏在头显里闪成碎片，压到 0.55
                    m.SetTexture("_BumpMap", T("helm_normal", false, true)); m.EnableKeyword("_NORMALMAP");
                    m.SetTexture("_OcclusionMap", T("helm_ao", false)); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP");
                });
                foreach (var r in inst.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; }
            }
            else
            {
                var ph = BoardBuilder.Mat("M_C6_HelmPlaceholder", Lit, m => { m.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.52f)); m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.6f); });
                Prim(t, PrimitiveType.Sphere, ph, Vector3.zero, new Vector3(0.2f, HelmH, 0.22f)).name = "Placeholder";
            }
            return t;
        }

        static Transform Helm3(Transform t, string dir)
        {
            // 11-06 自查：P2 原贴图整只偏古铜、UV 有大量拉伸面，按贴图分区会串色 → 在 Blender 里按部件拆成 7 个材质槽（part0..6，
            // Tools/helm_retex/helm3_components.py 按连通块 + 位置分），贴图只留原图的明暗（灰度），颜色 / 金属度 / 光滑度按部件给。
            if (AssetImporter.GetAtPath($"{dir}/helm3.fbx") is ModelImporter mi && (mi.importNormals != ModelImporterNormals.Calculate || mi.normalSmoothingAngle != 60f || mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard || mi.materialLocation != ModelImporterMaterialLocation.InPrefab))
            {
                mi.importNormals = ModelImporterNormals.Calculate; mi.normalSmoothingAngle = 60f;   // 11-06：体素重建后是均匀三角网，Unity 按 60° 平滑
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab; mi.SaveAndReimport();
            }
            var tp = $"{dir}/helm3_albedo.png";
            if (AssetImporter.GetAtPath(tp) is TextureImporter ti && (ti.anisoLevel != 8 || ti.sRGBTexture)) { ti.anisoLevel = 8; ti.maxTextureSize = 2048; ti.sRGBTexture = false; ti.SaveAndReimport(); }
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/helm3.fbx"), t);
            inst.transform.localRotation = Quaternion.Euler(0, 90, 0) * inst.transform.localRotation;
            var b0 = Bounds(inst);
            inst.transform.localScale *= HelmH / Mathf.Max(1e-4f, b0.size.y / t.lossyScale.y);
            var b = Bounds(inst);
            inst.transform.position += t.position - b.center;
            // 部件：0 银钢（骑士半边）1 黑铁（兽人半边）2 鎏金（狮子、金边）3 角 4 骷髅 5 黄铜铆钉 6 古铜铆钉 7 内衬
            var parts = new (string n, Color c, float metal, float smooth)[]
            {
                ("Steel", new Color(0.44f, 0.45f, 0.48f), 0.9f, 0.32f), ("Iron", new Color(0.24f, 0.23f, 0.22f), 0.8f, 0.32f),
                ("Gold", new Color(0.8f, 0.6f, 0.27f), 1f, 0.48f), ("Horn", new Color(0.48f, 0.37f, 0.26f), 0f, 0.26f),
                ("Bone", new Color(0.72f, 0.66f, 0.54f), 0f, 0.22f), ("Brass", new Color(0.74f, 0.56f, 0.28f), 1f, 0.45f),
                ("Bronze", new Color(0.52f, 0.36f, 0.22f), 0.9f, 0.42f),
                ("Inner", new Color(0.03f, 0.028f, 0.026f), 0f, 0.05f),   // 7 内衬：盔壳里面的暗色椭球，从眼缝 / 骷髅眼窝看进去是黑的，挡住 P2 留在里面的夹层碎面
            };
            var mats = new Material[parts.Length];
            for (int k = 0; k < parts.Length; k++)
            {
                var pt = parts[k];
                mats[k] = BoardBuilder.Mat("M_C6_Helm3_" + pt.n, Lit, m =>
                {
                    // 灰度明暗图（线性，中位 ≈ 0.62）× 底色 / 0.62：原图的形体明暗留下，颜色按部件统一
                    // 11-06：灰度明暗图 × 部件底色（倍率 1.25，亮处不超过 1，不过曝）；内衬没有 UV，用纯色
                    m.SetTexture("_BaseMap", pt.n == "Inner" ? null : detail); m.SetColor("_BaseColor", pt.n == "Inner" ? pt.c : pt.c * 1.25f);
                    m.SetFloat("_Metallic", pt.metal); m.SetFloat("_Smoothness", pt.smooth);
                });
            }
            // 11-06：Blender 里按部件拆成 7 个子物体 part0..6（去掉了穿插面、清理过拓扑），按子物体名认部件
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var nm = r.gameObject.name;
                int k = nm.StartsWith("part") && nm.Length > 4 && char.IsDigit(nm[4]) ? nm[4] - '0' : -1;
                if (k < 0 || k >= mats.Length) { Debug.LogWarning($"[Cover] 战盔子物体 {nm} 认不出部件，按黑铁处理"); k = 1; }
                var dst = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < dst.Length; m++) dst[m] = mats[k];
                r.sharedMaterials = dst; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return t;
        }

        /// <summary>交叉长幡（11-02 用户：旗帜要跟旗杆的方向）：旗面一边贴着旗杆、顶边挂在杆顶下垂直于杆的横担上（像“幟”），整面旗随杆倾斜；
        /// 旗面朝杆外侧偏下展开（两面旗各自往外），下摆随风轻摆（WarTable/FlagWave：网格本地 x = 离杆距离，位移沿旗面法线）。
        /// 旗面图：ArtSource/Cover/v5/banner_*.png（王国蓝底金狮 / 氏族暗红牛头骨）。w = 离杆宽度，h = 沿杆长度。</summary>
        static void Banner(Transform root, string team, Vector3 bottom, Vector3 top, float w, float h)
        {
            var g = new GameObject("Banner_" + team).transform; g.SetParent(root, false);
            var wood = BoardBuilder.Mat("M_C6_BannerPole", Lit, m => { m.SetColor("_BaseColor", new Color(0.09f, 0.065f, 0.05f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.35f); });
            var brass = BoardBuilder.Mat("M_C6_BannerBrass", Lit, m => { m.SetColor("_BaseColor", new Color(0.55f, 0.4f, 0.18f)); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 0.45f); });
            var dir = (top - bottom).normalized; float len = (top - bottom).magnitude;
            var pole = Prim(g, PrimitiveType.Cylinder, wood, (top + bottom) * 0.5f, new Vector3(0.007f, len * 0.5f, 0.007f));
            pole.localRotation = Quaternion.FromToRotation(Vector3.up, dir); pole.name = "Pole";
            var tip = Prim(g, PrimitiveType.Capsule, brass, top + dir * 0.014f, new Vector3(0.011f, 0.016f, 0.011f));
            tip.localRotation = pole.localRotation; tip.name = "Finial";
            // 展开方向：在封面平面内垂直于杆、朝下的那一侧（交叉的两面旗因此各自往外）
            var fly = Vector3.Cross(dir, Vector3.forward).normalized;
            if (fly.y > 0f) fly = -fly;
            // 横担：杆顶往下 3 cm，垂直于杆，沿展开方向伸出旗宽
            var barC = top - dir * 0.03f;
            var bar = Prim(g, PrimitiveType.Cylinder, wood, barC + fly * (w * 0.5f + 0.006f), new Vector3(0.005f, (w + 0.012f) * 0.5f, 0.005f));
            bar.localRotation = Quaternion.FromToRotation(Vector3.up, fly); bar.name = "Crossbar";
            Prim(g, PrimitiveType.Sphere, brass, barC + fly * (w + 0.014f), Vector3.one * 0.009f).name = "BarEnd";
            // 旗面本地系：x = 展开方向，y = 沿杆（取让法线朝玩家的那个符号）
            float sgn = Vector3.Cross(fly, dir).z > 0f ? 1f : -1f;
            var ydir = dir * sgn;
            var tex = Tex("banner_" + team);
            var mat = BoardBuilder.Mat("M_C6_Banner_" + team, "WarTable/FlagWave", m =>
            {
                m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", new Color(0.78f, 0.76f, 0.74f));
                m.SetFloat("_Width", w); m.SetFloat("_Amp", 0.1f); m.SetFloat("_WaveLen", 1.4f); m.SetFloat("_Speed", team == "kingdom" ? 1.3f : 1.15f);
                m.SetFloat("_Ambient", 0.35f);
            });
            var cloth = new GameObject("Cloth").transform; cloth.SetParent(g, false);
            cloth.localPosition = barC + Vector3.forward * 0.002f;
            cloth.localRotation = Quaternion.LookRotation(Vector3.Cross(fly, ydir), ydir);
            cloth.gameObject.AddComponent<MeshFilter>().sharedMesh = BannerMesh($"C6Banner_{team}", w, h, sgn, fly.x > 0f);
            var r = cloth.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>旗面网格：x = 离杆距离 0..w（外缘浅燕尾），y = 沿杆从横担往下 0..h（sgn = −1 时本地 y 朝下，见 Banner）。
        /// poleOnRight：玩家看去旗杆在旗面右边（封面本地 +x 是玩家左边），贴图 u 据此翻转、正面看不镜像；v 顶 = 1。</summary>
        static Mesh BannerMesh(string name, float w, float h, float sgn, bool poleOnRight)
        {
            const int C = 16, R = 24;   // C 沿展开方向，R 沿杆
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= R; j++)
                for (int i = 0; i <= C; i++)
                {
                    float a = (float)i / C, b = (float)j / R;
                    // 外缘燕尾：沿杆中段往杆收 18%
                    float x = w * a * (1f - 0.18f * Mathf.Max(0f, 1f - Mathf.Abs(2f * b - 1f) * 1.6f) * a);
                    v.Add(new Vector3(x, -sgn * h * b, 0)); uv.Add(new Vector2(poleOnRight ? 1f - a : a, 1f - b));
                }
            for (int j = 0; j < R; j++)
                for (int i = 0; i < C; i++)
                {
                    int a0 = j * (C + 1) + i, b0 = a0 + 1, c0 = a0 + C + 1, d0 = c0 + 1;
                    t.AddRange(new[] { a0, b0, c0, b0, d0, c0 });
                }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals();
            m.bounds = new Bounds(new Vector3(w * 0.5f, -sgn * h * 0.5f, 0), new Vector3(w * 1.3f, h * 1.2f, w * 0.5f));
            return Save(m, name);
        }

        /// <summary>顶光：从战盔上方斜照下来的一束暖光（叠加的锥形光柱 + 一盏聚光灯照亮战盔）。</summary>
        static void Shaft(Transform root)
        {
            var top = HelmAt + new Vector3(-0.05f, 0.62f, -0.12f);
            var go = new GameObject("LightShaft");
            go.transform.SetParent(root, false);
            go.transform.localPosition = top;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.down, (HelmAt - top).normalized);
            go.AddComponent<MeshFilter>().sharedMesh = Cone(0.035f, 0.2f, 0.78f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Unlit("M_C6_Shaft", null, new Color(1f, 0.82f, 0.58f, 0.16f), 3005, cull: CullMode.Off, additive: true, edgeFade: 1.6f, vFade: 1f);
            r.shadowCastingMode = ShadowCastingMode.Off;
            var spot = new GameObject("Spot").AddComponent<Light>();
            spot.transform.SetParent(root, false);
            spot.transform.localPosition = top;
            spot.transform.localRotation = Quaternion.LookRotation(HelmAt - top);
            spot.type = LightType.Spot; spot.spotAngle = 36f; spot.innerSpotAngle = 18f; spot.range = 1.6f;
            spot.intensity = 0.85f; spot.color = new Color(1f, 0.86f, 0.66f); spot.shadows = LightShadows.None;
        }

        /// <summary>光柱里的细沙：慢慢往上飘的暖色小点。</summary>
        static void Sand(Transform root)
        {
            var go = new GameObject("Sand");
            go.transform.SetParent(root, false);
            go.transform.localPosition = HelmAt + new Vector3(-0.02f, 0.05f, -0.05f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0012f, 0.003f);
            main.startColor = new Color(1f, 0.85f, 0.6f, 0.8f);
            main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.prewarm = true;
            var em = ps.emission; em.rateOverTime = 24f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.32f, 0.42f, 0.22f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.004f, 0.004f); vel.y = new ParticleSystem.MinMaxCurve(0.002f, 0.008f); vel.z = new ParticleSystem.MinMaxCurve(-0.003f, 0.003f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = Unlit("M_C6_Sand", Dot(), Color.white, 3040, cull: CullMode.Off, additive: true);
            pr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>裂缝崩出的碎块：几片不规则的薄金属 / 骨片，悬在战盔裂缝两侧。</summary>
        static IEnumerable<Transform> Chips(Transform root)
        {
            var steel = BoardBuilder.Mat("M_C6_Chip", Lit, m => { m.SetColor("_BaseColor", new Color(0.55f, 0.53f, 0.5f)); m.SetFloat("_Metallic", 0.85f); m.SetFloat("_Smoothness", 0.5f); });
            var bone = BoardBuilder.Mat("M_C6_ChipBone", Lit, m => { m.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.66f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.25f); });
            var spots = new[] { new Vector3(0.0f, 0.15f, 0.04f), new Vector3(0.02f, 0.11f, 0.08f), new Vector3(-0.025f, 0.18f, 0.02f), new Vector3(0.04f, -0.06f, 0.1f), new Vector3(-0.03f, 0.03f, 0.11f), new Vector3(0.015f, 0.2f, -0.01f) };
            var rnd = new System.Random(7);
            for (int i = 0; i < spots.Length; i++)
            {
                var t = new GameObject("Chip").transform; t.SetParent(root, false);
                t.localPosition = HelmAt + spots[i];
                t.localRotation = Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360));
                var g = new GameObject("Mesh"); g.transform.SetParent(t, false);
                g.AddComponent<MeshFilter>().sharedMesh = Shard($"C6Chip{i}", 0.006f + 0.006f * (float)rnd.NextDouble(), rnd);
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = i % 3 == 2 ? bone : steel; r.shadowCastingMode = ShadowCastingMode.Off;
                yield return t;
            }
        }

        // ---------------------------------------------------------------- 立体字

        /// <summary>
        /// 一件立体字（FBX 整幅宽 1 m、原点在字中心）：按字高（或宽）缩放，中心放到 at；
        /// 法线朝玩家的子网格用 face，其余用 side。缺文件时跳过。
        /// </summary>
        internal static Transform Piece(Transform parent, string dir, string file, float size, Vector3 at, Material face, Material side, bool byHeight = true)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{file}.fbx");
            if (!src) { Debug.LogWarning($"[Cover] 缺 {dir}/{file}.fbx"); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = file;
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var me = r.GetComponent<MeshFilter>().sharedMesh;
                var share = Enumerable.Range(0, me.subMeshCount).Select(i => FacingShare(me, i, r.transform)).ToArray();
                int best = System.Array.IndexOf(share, share.Max());
                r.sharedMaterials = Enumerable.Range(0, me.subMeshCount).Select(i => i == best ? face : side).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var b = Bounds(inst);
            float s = size / Mathf.Max(1e-4f, byHeight ? b.size.y : b.size.x);
            inst.transform.localScale *= s;
            b = Bounds(inst);
            inst.transform.position += parent.TransformPoint(at) - b.center;
            return inst.transform;
        }

        // ---------------------------------------------------------------- 材质

        /// <summary>立体字的 PBR（WarTable/CoverPBR，ZTest Always：书法字网格挡不住后面物体的问题见 CoverPBR 说明）。</summary>
        internal static Material Pbr(string name, Color c, float metal, float smooth, int queue)
        {
            var m = BoardBuilder.Mat(name, "WarTable/CoverPBR", mm =>
            {
                mm.SetColor("_BaseColor", c); mm.SetFloat("_Metallic", metal); mm.SetFloat("_Smoothness", smooth);
                mm.SetFloat("_ZTest", (float)CompareFunction.Always);
            });
            m.renderQueue = queue;
            return m;
        }

        internal static Material Unlit(string name, Texture tex, Color c, int queue, CullMode cull = CullMode.Off, bool additive = false, float edgeFade = 0f, float vFade = 0f)
        {
            var m = BoardBuilder.Mat(name, "WarTable/CoverUnlit", mm =>
            {
                mm.SetTexture("_BaseMap", tex); mm.SetColor("_BaseColor", c);
                mm.SetFloat("_Cull", (float)cull); mm.SetFloat("_EdgeFade", edgeFade); mm.SetFloat("_VFade", vFade);
                mm.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mm.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                // 透明度通道按覆盖程度写：压暗罩写高 → 真实画面被压暗；叠加件（光柱、细沙）不改透明度
                mm.SetFloat("_SrcBlendA", (float)(additive ? BlendMode.Zero : BlendMode.One));
                mm.SetFloat("_DstBlendA", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                mm.SetFloat("_Reveal", 1f);
            });
            m.renderQueue = queue;
            return m;
        }

        // ---------------------------------------------------------------- 网格

        /// <summary>贴图片：size = 宽，高按贴图比例；以 at 为中心，略转 rotZ。</summary>
        internal static Transform Quad(Transform parent, string name, Texture2D tex, Color c, Vector3 at, float size, int queue, float rotZ = 0f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = at;
            g.transform.localRotation = Quaternion.Euler(0, 180f, rotZ);   // Quad 正面朝 −Z，转过来朝玩家
            float h = tex ? size / UiAspect.Of(tex) : size;
            g.transform.localScale = new Vector3(size, h, 1);
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = Unlit($"M_C6_{name}", tex, c, queue);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g.transform;
        }

        /// <summary>微微卷曲的纸片（地图残片）：宽 size，沿 x 弯成浅弧。</summary>
        static Transform Curled(Transform parent, string name, Texture2D tex, Color c, Vector3 at, float size, float rotZ, int queue)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false);
            t.localPosition = at; t.localRotation = Quaternion.Euler(12f, 180f + rotZ * 0.6f, rotZ);
            var g = new GameObject("Paper"); g.transform.SetParent(t, false);
            float h = tex ? size / UiAspect.Of(tex) : size;
            const int N = 12;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int j = 0; j <= 1; j++)
                for (int i = 0; i <= N; i++)
                {
                    float u = i / (float)N, x = (u - 0.5f) * size;
                    v.Add(new Vector3(x, (j - 0.5f) * h, -0.5f * size * (u - 0.5f) * (u - 0.5f))); uv.Add(new Vector2(u, j));
                }
            for (int i = 0; i < N; i++) { int a = i, b = i + 1, cc = i + N + 1, d = cc + 1; tri.AddRange(new[] { a, cc, b, b, cc, d }); }
            var m = new Mesh { name = $"C6Curl_{queue}" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = Save(m, $"C6Curl_{queue}");
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = Unlit($"M_C6_Map_{queue}", tex, c, queue); r.shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        /// <summary>光柱：开口圆锥，顶半径 r0、底半径 r1、长 len（沿本地 −Y 往下），uv.y 顶 1 底 0。</summary>
        static Mesh Cone(float r0, float r1, float len)
        {
            const int N = 32;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= N; i++)
            {
                float a = Mathf.PI * 2f * i / N, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v.Add(new Vector3(c * r0, 0, s * r0)); uv.Add(new Vector2(i / (float)N, 1));
                v.Add(new Vector3(c * r1, -len, s * r1)); uv.Add(new Vector2(i / (float)N, 0));
            }
            for (int i = 0; i < N; i++) { int a = i * 2; tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var m = new Mesh { name = "C6Shaft" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return Save(m, "C6Shaft");
        }

        /// <summary>不规则薄碎片：随机凸多边形挤出 3 mm。</summary>
        static Mesh Shard(string name, float r, System.Random rnd)
        {
            int n = 5 + rnd.Next(3);
            var o = new List<Vector2>();
            for (int i = 0; i < n; i++) { float a = Mathf.PI * 2f * (i + 0.3f * (float)rnd.NextDouble()) / n; float rr = r * (0.6f + 0.6f * (float)rnd.NextDouble()); o.Add(new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr)); }
            const float d = 0.0015f;
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                int c0 = v.Count; v.Add(new Vector3(0, 0, k == 0 ? d : -d));
                foreach (var p in o) v.Add(new Vector3(p.x, p.y, k == 0 ? d : -d));
                for (int i = 0; i < n; i++) { int a = c0 + 1 + i, b = c0 + 1 + (i + 1) % n; if (k == 0) tri.AddRange(new[] { c0, a, b }); else tri.AddRange(new[] { c0, b, a }); }
            }
            for (int i = 0; i < n; i++)
            {
                var p0 = o[i]; var p1 = o[(i + 1) % n]; int s = v.Count;
                v.Add(new Vector3(p0.x, p0.y, d)); v.Add(new Vector3(p1.x, p1.y, d)); v.Add(new Vector3(p1.x, p1.y, -d)); v.Add(new Vector3(p0.x, p0.y, -d));
                tri.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return Save(m, name);
        }

        static Mesh Save(Mesh m, string name)
        {
            string path = $"Assets/_WarTable/Art/Generated/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { existing.Clear(); EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>柔边圆点贴图（细沙粒子用）。</summary>
        internal static Texture2D Dot()
        {
            string path = $"{TexDir}/dot.png";
            if (!System.IO.File.Exists(path))
            {
                System.IO.Directory.CreateDirectory(TexDir);
                var t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 15.5f; t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d))); }
                System.IO.File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static Transform Prim(Transform parent, PrimitiveType type, Material m, Vector3 pos, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            return g.transform;
        }

        /// <summary>Cover6 贴图（Tools/ink_alpha.py 抠好的透明 PNG，在 ArtSource/Cover/v6/alpha）：复制进工程并导入。</summary>
        /// <summary>UI 贴图的原始宽高（补成 2 的幂之前）：显示比例按它算，UV 只取左上角这一块（见 UiTile）。</summary>
        internal static Vector2Int UiSize(string name)
        {
            string src = $"ArtSource/Cover/v6/alpha/{name}.png";
            if (!System.IO.File.Exists(src)) return Vector2Int.one;
            var b = System.IO.File.ReadAllBytes(src);   // PNG 头：宽高在第 16–23 字节（大端）
            int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return new Vector2Int(w, h);
        }

        internal static Texture2D Tex(string name)
        {
            // 11-07：本版 Unity 编辑器只对 2 的幂贴图做 ASTC 压缩（非 2 的幂一律退回 RGBA32，117 张 UI 图在包里占 144 MB）
            // → 导入时把内容缩放到 2 的幂画布（非等比缩放，UV 仍是 0–1，按钮热区不受影响），原始宽高写进文件名（legend_zh@1000x1440），
            //   显示时按 UiAspect.Of 的原始比例摆，不会被拉伸
            string src = $"ArtSource/Cover/v6/alpha/{name}.png";
            if (!System.IO.File.Exists(src)) { Debug.LogWarning("[Cover] 缺贴图 " + src); return null; }
            System.IO.Directory.CreateDirectory(TexDir);
            var sz = UiSize(name);
            string dst = $"{TexDir}/{name}@{sz.x}x{sz.y}.png";
            bool fresh = !System.IO.File.Exists(dst) || System.IO.File.GetLastWriteTimeUtc(src) > System.IO.File.GetLastWriteTimeUtc(dst);
            if (fresh)
            {
                foreach (var old in System.IO.Directory.GetFiles(TexDir, name + "*.png"))   // 旧副本（不带尺寸 / 尺寸变了）
                {
                    var on = System.IO.Path.GetFileNameWithoutExtension(old);
                    if (on == name || (on.StartsWith(name + "@") && old.Replace('\\', '/') != dst)) { System.IO.File.Delete(old); System.IO.File.Delete(old + ".meta"); }
                }
                var raw = new Texture2D(2, 2); raw.LoadImage(System.IO.File.ReadAllBytes(src));
                int w2 = Mathf.Min(2048, Mathf.NextPowerOfTwo(raw.width)), h2 = Mathf.Min(2048, Mathf.NextPowerOfTwo(raw.height));
                if (w2 != raw.width || h2 != raw.height)
                {
                    var rt = RenderTexture.GetTemporary(w2, h2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active;
                    Graphics.Blit(raw, rt);
                    RenderTexture.active = rt;
                    var pot = new Texture2D(w2, h2, TextureFormat.RGBA32, false);
                    pot.ReadPixels(new Rect(0, 0, w2, h2), 0, 0); pot.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    System.IO.File.WriteAllBytes(dst, pot.EncodeToPNG()); Object.DestroyImmediate(pot);
                }
                else System.IO.File.Copy(src, dst, true);
                Object.DestroyImmediate(raw);
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(dst);
            }
            if (AssetImporter.GetAtPath(dst) is TextureImporter ti && (fresh || !ti.alphaIsTransparency || ti.GetPlatformTextureSettings("Android").format != TextureImporterFormat.ASTC_6x6))
            {
                ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
                ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 2048;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50 });
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static float FacingShare(Mesh me, int sub, Transform t)
        {
            var tri = me.GetTriangles(sub); var v = me.vertices; int n = 0, f = 0;
            for (int i = 0; i < tri.Length; i += 3)
            {
                var nn = t.TransformDirection(Vector3.Cross(v[tri[i + 1]] - v[tri[i]], v[tri[i + 2]] - v[tri[i]]));
                if (nn.sqrMagnitude < 1e-12f) continue;
                n++; if (Mathf.Abs(nn.normalized.z) > 0.92f) f++;
            }
            return n > 0 ? f / (float)n : 0f;
        }

        /// <summary>菜单项的热区（11-02 用户：热区不要影响其他按钮）：只包住字和档位点（项本地坐标的矩形），不含墨笔刷；
        /// 左右各留 1.2 cm，高固定 5 cm（项间距 ≥ 7 cm，同页不会叠）。用网格包围盒算，不受当前语言显隐影响。</summary>
        internal static Rect HitRect(Transform item, Transform[] texts, Renderer[] pips, float textY)
        {
            float x0 = float.MaxValue, x1 = float.MinValue;
            var rs = texts.Where(x => x).SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).Concat(pips ?? new Renderer[0]);
            foreach (var r in rs)
            {
                var mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) * 2 - 1, (k & 2) - 1, (k & 4) / 2 - 1));
                    float x = item.InverseTransformPoint(r.transform.TransformPoint(c)).x;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                }
            }
            const float padX = 0.012f, h = 0.05f;
            return new Rect(x0 - padX, textY - h * 0.5f, x1 - x0 + padX * 2f, h);
        }

        internal static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>封面专用反射：远处临时摆一间暗调影棚（顶上暖色柔光、侧面冷色灯带、其余压暗），烘成反射图给封面的金属件。</summary>
        static Cubemap BakeStudio()
        {
            var at = new Vector3(0, 50, 0);
            var tmp = new GameObject("_Studio");
            Material Glow(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c); return m; }
            void Box(Vector3 pos, Vector3 size, Color c)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.transform.SetParent(tmp.transform, false);
                q.transform.position = at + pos; q.transform.localScale = size;
                q.transform.rotation = Quaternion.LookRotation(-pos.normalized);
                q.GetComponent<Renderer>().sharedMaterial = Glow(c);
            }
            Box(new Vector3(0, 1.4f, 0.2f), new Vector3(1.2f, 1.2f, 1), new Color(2.2f, 1.9f, 1.5f));
            Box(new Vector3(-0.9f, 0.5f, 1.1f), new Vector3(0.6f, 1.0f, 1), new Color(0.9f, 0.82f, 0.72f));
            Box(new Vector3(1.2f, 0.3f, 0.4f), new Vector3(0.2f, 1.2f, 1), new Color(0.6f, 0.68f, 0.85f));
            Box(new Vector3(0, -1.2f, 0), new Vector3(4f, 4f, 1), new Color(0.03f, 0.028f, 0.026f));
            var probe = new GameObject("_StudioProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(tmp.transform, false);
            probe.transform.position = at;
            probe.mode = ReflectionProbeMode.Custom;
            probe.resolution = 256; probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.05f, 0.045f, 0.04f);
            probe.size = Vector3.one * 6f;
            Lightmapping.BakeReflectionProbe(probe, StudioPath);
            Object.DestroyImmediate(tmp);
            AssetDatabase.ImportAsset(StudioPath);
            return AssetDatabase.LoadAssetAtPath<Cubemap>(StudioPath);
        }
    }
}
