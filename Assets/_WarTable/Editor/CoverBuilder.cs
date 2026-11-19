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
        const string HeroStudioPath = "Assets/_WarTable/Art/Textures/CoverHeroStudio.exr";
        // ── 11-19 傍晚（用户看了下午那版：“你自己看看氛围一样吗？头顶不用打光，照参考图的感觉做”）
        //   D2 样图是整体偏暖的暗调：主角深炮铜色、身上没有白亮的高光、头顶没有光；亮点只有面罩青光、岩座熔岩光、身后的金线日轮和暖色尘雾。
        //   所以拼贴封面：不要头顶的光柱和顶灯（Shaft），主光压低到正前偏侧，主角用自己的一份没有顶光的反射环境（BakeHeroStudio），
        //   面罩 / 熔岩自发光和小光晕、身后的暖色尘光在 CoverHeroBuilder 里搭。
        static readonly Vector3 HeroAim = new(0f, 0.09f, 0f), KeyAt = new(0.30f, 0.30f, 0.62f), RimAt = new(-0.40f, 0.26f, -0.34f);
        const float KeyI = 1.7f, RimI = 0.75f;
        static readonly Color KeyC = new(1f, 0.78f, 0.55f), RimC = new(0.72f, 0.82f, 1f);

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
            public bool collage;     // 11-18：西式奇幻的封面（MR：没有底板；盔 + 双旗 + 身后一轮金线日轮，立体大字斜压在盔前，署名条和票根按钮悬在下面）
            public Vector3 creditPos; public float creditH, creditRot;   // 出品署名纸条：中心位置、纸条高（0 = 不放）、转角
        }
        /// <summary>
        /// 11-19 晚 用户：“标题分成两段，两段形成一个尖角，角度不要太大”。0 = 原来的一行斜排（TitleWords 里的位置 / 转角）；
        /// 1 = V 形（两段往中间落下，尖朝下）；2 = 倒 V 形（尖朝上）。两个词的内端在 TitleApex 处相会（中间留 TitleGap），各斜 TitleDeg 度，字高 TitleH。
        /// 出品署名摆在折线下面正中。
        /// </summary>
        public static int TitleShape = 2;   // 11-19 晚 用户选了倒 V 形
        public static float TitleDeg = 7f, TitleH = 0.052f, TitleGap = 0.022f, TitleDepth = 0.5f;   // 11-19 晚再续 用户：“去掉标题中间的点，错位多一点、角度小一点”：12° → 7°，菱形去掉、缺口 3 → 2.2 cm
        public static bool TitleGemOn = false;
        public static Vector3 TitleApex = new(0f, -0.13f, 0.15f);   // 11-19 晚再续 用户“再往上挪一点”：−0.146 → −0.13
          // 玩家机位看：尖角压住岩座下沿（原 −0.205 中间空了一大块；11-19 晚用户“标题往上挪一点”：−0.162 → −0.146）
        /// <summary>11-19 晚 用户：“两段文字可以适当错位” → 两个词各自再挪一点、角度也不完全一样（TRIPO 高一点、往前一点、斜得多一点；CRAFT 低一点、往后一点）。x 正 = 玩家左边。</summary>
        public static readonly Vector3[] TitleShift = { new(0.006f, 0.019f, 0.016f), new(-0.006f, -0.015f, -0.008f) };   // 错位加大（原 TRIPO +1.1 / CRAFT −0.9 cm）
        public static readonly float[] TitleDegAdd = { 1f, -1.5f };
        /// <summary>按 TitleShape 算一个词的中心和转角（k = 0 是 TRIPO，在玩家左边；w = 没转之前的字宽）。转角正 = 观看者看到的顺时针。</summary>
        static (Vector3 p, float rot) WordPose(int k, float w, Vector3 p0, float rot0)
        {
            if (TitleShape == 0) return (p0, rot0);
            float side = k == 0 ? 1f : -1f, deg = TitleDeg + TitleDegAdd[k], th = deg * Mathf.Deg2Rad, up = TitleShape == 1 ? 1f : -1f;
            // 内端中线落在 TitleApex；再往外让出 h·sinθ/2，两个词转过去以后内侧的角（V 形是下角、倒 V 形是上角）正好碰上
            var p = TitleApex + TitleShift[k] + new Vector3(side * (TitleGap * 0.5f + TitleH * 0.5f * Mathf.Sin(th) + w * 0.5f * Mathf.Cos(th)), up * w * 0.5f * Mathf.Sin(th), 0f);
            return (p, side * up * deg);
        }
        /// <summary>两个词摆完以后，折线最低点的 y（署名摆在它下面）。</summary>
        static float titleLow;
        /// <summary>游戏名的两个词（文件名、位置、字高、转角）。x 正 = 玩家左边。</summary>
        static readonly (string f, Vector3 p, float h, float rot)[] TitleWords =
            { ("en_tripo", new Vector3(0.212f, -0.158f, 0.15f), 0.072f, -7f), ("en_craft", new Vector3(-0.206f, -0.116f, 0.15f), 0.072f, -7f) };   // 两词各往外挪：原来在中间撞成 TRIPOCRAFT   // 11-18：加大、往上压到岩石和脚，斜度加到 7°   // 一行，横在主角脚下，往右上斜 5°（转角负 = 观看者看到的逆时针）
        /// <summary>
        /// 菜单纸片（定稿样图 D2）：每项 = 键（Tools/make_cover_shards.py 的贴图名）、位置、纸片高（米）、倾斜（观看者看到的逆时针为正）。x 正 = 玩家左手边。
        /// 大小悬殊、各自倾斜、互相压着一点，分在主角左右；三页（主菜单 / 设置 / 风格）用同样的左右两列。
        /// </summary>
        static readonly (string key, Vector3 p, float h, float tilt)[] Shards =
        {
            // 主菜单（11-19 晚加“演示”，六项左右各三）：出征（最大）/ 风格 / 教学 在左，演示（第二大）/ 设置 / 退出 在右。顺序 = 下面 menu 里的顺序
            ("begin", new Vector3(0.236f, 0.122f, 0.13f), 0.106f, -9f), ("demo", new Vector3(-0.238f, 0.112f, 0.125f), 0.07f, 8f),
            ("era", new Vector3(0.222f, 0.034f, 0.115f), 0.07f, -3f), ("tutorial", new Vector3(0.236f, -0.044f, 0.14f), 0.058f, 7f),
            ("settings", new Vector3(-0.23f, 0.03f, 0.12f), 0.064f, -4f), ("quit", new Vector3(-0.212f, -0.048f, 0.145f), 0.066f, 5f),
            // 设置页：语言 / 战速 / 暗度 在左，重新定位 / 返回 在右
            ("language", new Vector3(0.212f, 0.124f, 0.13f), 0.078f, -8f), ("speed", new Vector3(0.206f, 0.038f, 0.12f), 0.07f, 4f), ("dim", new Vector3(0.214f, -0.044f, 0.145f), 0.068f, -5f),
            ("replace", new Vector3(-0.232f, 0.076f, 0.125f), 0.064f, 9f), ("back", new Vector3(-0.186f, -0.022f, 0.15f), 0.072f, -6f),
            // 风格页：剑盾 / 铁誓 / 硝烟 在左，星尘 / 深空 / 返回 在右
            ("era0", new Vector3(0.202f, 0.126f, 0.13f), 0.078f, -8f), ("era1", new Vector3(0.218f, 0.042f, 0.12f), 0.066f, 5f), ("era2", new Vector3(0.21f, -0.042f, 0.145f), 0.066f, -4f),
            ("era3", new Vector3(-0.219f, 0.108f, 0.125f), 0.068f, 8f), ("era4", new Vector3(-0.228f, 0.024f, 0.15f), 0.066f, -4f), ("back", new Vector3(-0.186f, -0.058f, 0.135f), 0.07f, 4f),
        };
        /// <summary>
        /// 11-19 晚 主菜单六项排成放射状（用户：“六个标签要指向中心，别乱”“风格的位置要是演示”“注意它们之间的位置、玩家的视角”）：
        /// 每张纸片的内端落在主角周围半径 FanRin 的一圈上，长边沿着从 FanC 射出的射线（上 / 中 / 下三条，夹角 FanDeg）指向主角中心。
        /// 左：出征 / 演示 / 教学，右：风格 / 设置 / 退出。纸片宽按各自语言的字宽（中文都是两个字，一样长）。
        /// 玩家视角（头显里封面原点在眼前 0.8 m、高出眼睛 4 cm、正对）：中文版各项中心在左右 16° 以内、最外 22°，上下 +1°～+16°，互不重叠（英文版纸片长，最宽的 SHOWCASE 中心 21°）
        /// （试算脚本 /tmp/sp7/tools/fan2.py，量法见交接文档）。顺序和 menu 里主菜单的顺序一致：出征、演示、风格、教学、设置、退出。
        /// 11-19 晚续 用户：“面板大小不要一样，角度也不要一样，不要太死板” → 每项自己的射线角、纸片高、内圈半径，再加几度歪斜（不完全对准中心）；左右不镜像。
        ///   试算 /tmp/sp7/tools/fan3.py（中文版中心左右 ≤16°、最外 23°，上下 0°～+17°，互不重叠）。
        /// 11-19 晚再续 用户：“六个长条朝向中间，不要加大角度，角度尽量不要太相似” → 位置仍沿射线排，但纸条自己的倾斜（tilt）和射线角分开：
        ///   收小到 3°～16°、六张各不相同，方向都朝主角（左上 / 右下往右下斜，左下 / 右上往右上斜）。
        /// side：+1 = 玩家左边（封面本地 +x）；deg：锚点所在的射线角（上为正）；h：纸片高；rin：内端离中心多远；tilt：纸条倾斜（观看者看到的逆时针为正）。
        /// </summary>
        static readonly (int side, float deg, float h, float rin, float tilt)[] Fan =
        {
            // 11-19 晚续 用户：“面板大小不要被字的大小限制，长一点，和之前一样”→ 纸条改回长条（Tools/make_cover_shards.py 的 MAIN_ASPECT），
            //   平时尺寸收小一点（被看着的那张会放大 HotGrow 并换成象牙白）。中文版中心左右 16°～20°、最外 28.5°，互不重叠
            (1, 30f, 0.072f, 0.150f, -16f),  // 出征（最大）
            (1, 1f, 0.056f, 0.142f, -4f),    // 演示（第二大）
            (-1, 23f, 0.052f, 0.146f, 13f),  // 风格
            (1, -27f, 0.048f, 0.150f, 9f),   // 教学
            (-1, -5f, 0.050f, 0.148f, 3f),   // 设置
            (-1, -32f, 0.054f, 0.144f, -11f), // 退出
        };
        public static float FanCY = 0.07f, FanZ = 0.06f;
        /// <summary>设置页（语言 / 战速 / 暗度 | 重新定位 / 返回），排法同 Fan（试算 /tmp/sp7/tools/fan4.py：中心左右 ≤20°，互不重叠）。</summary>
        static readonly (int side, float deg, float h, float rin, float tilt)[] FanSettings =
        {
            (1, 28f, 0.054f, 0.148f, -14f), (1, 0f, 0.050f, 0.142f, -3f), (1, -27f, 0.050f, 0.150f, 10f),
            (-1, 16f, 0.050f, 0.146f, 12f), (-1, -20f, 0.054f, 0.146f, -7f),
        };
        /// <summary>风格页（剑盾 / 铁誓 / 硝烟 | 星尘 / 深空 / 返回）。</summary>
        static readonly (int side, float deg, float h, float rin, float tilt)[] FanEra =
        {
            (1, 30f, 0.054f, 0.150f, -15f), (1, 1f, 0.050f, 0.142f, -5f), (1, -27f, 0.050f, 0.150f, 9f),
            (-1, 24f, 0.052f, 0.146f, 14f), (-1, -3f, 0.050f, 0.148f, 4f), (-1, -31f, 0.050f, 0.144f, -11f),
        };
        static Dictionary<string, (float zh, float en)> shardW;
        /// <summary>主菜单六张纸片按语言的整张宽（像素，含阴影边）：Tools/cover_shards.json 的 w_zh / w_en。</summary>
        static float ShardW(string key, bool zh)
        {
            if (shardW == null)
            {
                shardW = new Dictionary<string, (float, float)>();
                var rx = new System.Text.RegularExpressions.Regex("\"(\\w+)\":\\s*\\{[^}]*\"w_zh\":\\s*([0-9.]+),\\s*\"w_en\":\\s*([0-9.]+)");
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(System.IO.File.ReadAllText("Tools/cover_shards.json")))
                    shardW[m.Groups[1].Value] = (float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            return shardW.TryGetValue(key, out var v) ? (zh ? v.zh : v.en) : ShardPx(key).w;
        }
        static Dictionary<string, (float w, float h, float pad, float glow)> shardPx;
        /// <summary>纸片贴图的像素尺寸（Tools/cover_shards.json）：整张宽、高，四周阴影边，发光图比它每边多出多少。</summary>
        static (float w, float h, float pad, float glow) ShardPx(string key)
        {
            if (shardPx == null)
            {
                shardPx = new Dictionary<string, (float, float, float, float)>();
                var rx = new System.Text.RegularExpressions.Regex("\"(\\w+)\":\\s*\\{\\s*\"w\":\\s*([0-9.]+),\\s*\"h\":\\s*([0-9.]+),\\s*\"pad\":\\s*([0-9.]+),\\s*\"glow_pad\":\\s*([0-9.]+)");
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(System.IO.File.ReadAllText("Tools/cover_shards.json")))
                {
                    float F(int i) => float.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                    shardPx[m.Groups[1].Value] = (F(2), F(3), F(4), F(5));
                }
            }
            return shardPx.TryGetValue(key, out var v) ? v : (700f, 250f, 12f, 46f);
        }
        /// <summary>同一件小东西（一张票根 + 上面的字）里各层之间只留的一点点间距（11-17 用户：界面的几层要贴在一个平面上，凑近不能看出是分开的）。</summary>
        const float Lz = 0.0004f;
        /// <summary>票根按钮：纸面宽、相对字中心往玩家左手边偏多少（左端有一截存根，字排在右边大半张上）。fx_ticket（Tools/make_collage_ui.py 的 flat_ticket，3.6 : 1 的扁票根）贴图里纸面宽 / 整张宽 = 0.9714。</summary>
        const float TicketW = 0.17f, TicketDx = 0.012f, TicketRatio = 0.9714f, TicketH = 0.047f;   // fx_ticket：840×250 的图里纸面 816×226
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
            new Layout { free = true, collage = true, helmPos = new Vector3(0f, 0.12f, -0.02f), helmH = 0.3f, helmYaw = 0f, helmRoll = 0f,   // 11-18：盔不要了，helmPos 只用来定顶光和细沙的位置（主角头顶）
                         // 11-18 用户：游戏改名 Tripo craft（没有中文名）→ 中文版和英文版的标题都是 TRIPO / CRAFT 两个词（Cinzel 立体字，Tools/make_title_words.py），
                         //   沿原来那条斜线两级落下；旧名字的英文副标题和朱印去掉，副标题原来的位置放出品署名（creditPos，Tools/make_brand_credit.py）
                         fchars = TitleWords, noTagline = true,
                         enPos = new Vector3(-0.215f, -0.168f, 0.085f), enW = 0.2f, enRot = 4f, sealPos = new Vector3(-0.29f, -0.112f, 0.085f), sealSize = 0f, sealRot = -8f,
                         creditPos = new Vector3(0.236f, -0.272f, 0.16f), creditH = 0.028f, creditRot = 7f,   // 游戏名左端的正下方，和游戏名同一个斜度、左端对齐
                         // 11-18：朱砂弧换成盔后面的一轮金线日轮（fx_halo：只有线、没有底色，直径 84 cm，慢慢转）
                         inkPos = new Vector3(0f, 0.1f, -0.17f), inkSize = 0.66f, inkRot = 0f,
                         // 11-11 用户：下面按钮排版太死板 → 五项竖排同大改三行错落：“出征”放大独占一行（主按钮）；“时代 / 教学”并排、高低错开；
                         //   “设置 / 退出”小一号并排；各带 2–3.5° 转角、前后错一点。竖向从视线下 16°–30° 收到 15°–23°，左右不超过 ±13°。
                         //   顺序 = 菜单项顺序：出征、时代、教学、设置、退出（x 正 = 玩家左边）
                         // 11-13 用户：“封面整体可以往上挪一点，那几个按钮的位置有点偏”。量出来的毛病：第三行（设置 / 退出）的中点比上两行偏右 0.75 cm，
                         //   左右两列离中线不一样远（+7.0 / −8.5、+10.5 / −10.0）；“出征”的墨笔和英文副标题的墨笔在同一高度、叠在一起。
                         //   → 两行都按中线对称（±10.4、±7.8 cm，字心对字心）；整组下移 5 cm，“出征”落到副标题那道墨下面（3 cm 时两道墨的尾巴还搭在一起），标题一组和按钮一组分开；
                         //   高低错落和转角留着。封面整体上移见 MenuCover.below。
                         // 11-18：五张票根，三行错落（出征最大），悬在标题下面
                         fmenu = new[] { (new Vector3(0.0f, -0.207f, 0.125f), 1.3f, 2f),
                                         (new Vector3(0.104f, -0.265f, 0.12f), 0.95f, -3f), (new Vector3(-0.104f, -0.272f, 0.12f), 0.95f, 2.5f),
                                         (new Vector3(0.08f, -0.318f, 0.115f), 0.8f, 3.5f), (new Vector3(-0.08f, -0.314f, 0.115f), 0.8f, -2f) },
                         fmaps = new[] { ("map_0", new Vector3(-0.42f, 0.24f, -0.06f), 0.08f, 18f), ("map_1", new Vector3(0.42f, 0.2f, 0.02f), 0.065f, -25f),
                                         ("map_2", new Vector3(0.1f, 0.34f, 0.1f), 0.055f, 40f), ("map_0", new Vector3(-0.4f, -0.02f, -0.1f), 0.07f, -12f) },
                         flags = null },   // 军旗现在由主角握着（CoverHeroBuilder）
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
            dim.GetComponent<Renderer>().sharedMaterial = Unlit("M_C6_Dimmer", null, new Color(0.015f, 0.016f, 0.03f, 0.9f), 2990, cull: CullMode.Front);   // 0.9：头显里真实房间还留一点轮廓（参考图里房间是虚的、暗的，不是全黑）   // 11-18：压得更暗、带一点蓝，暖光才打得出来
            dim.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ── 灯光（11-18 用户：和样图比"完全不是一个味道"）：样图是一盏硬暖主光从右上打下来 + 一道冷侧光勾边 + 深阴影；
            //   原来只靠场景环境光，封面一片平灰。
            // 11-19 过曝巡检（记忆 moonlit-no-overexposure 那条规则同样适用）修了两处：
            //   1) **RimLight 朝向错了，一直没照到主角**：原来 euler (12, −126, 0) → 和主角中心夹角 68.4°，而光锥半角只有 46° → 这盏 1.6 强度的灯整个白费，
            //      样图那道冷侧光勾边从来没出现过（实测：只开 RimLight 和完全关灯的渲染结果逐位相同）。现在朝主角中心 (24.4, 305.5, 0)。
            //   2) **主角盔甲死白 15.1%**（盔顶 / 肩甲板 / 胸甲糊成一片，贴图辛苦做的深钢蓝和崩漆全丢）：四盏灯（这里两盏 + 光柱 Spot 1.3 + 场景 Sun 1.25，
            //      cullingMask 全是 −1）叠加过头。摆正 RimLight 后重新定强度：主 1.35→0.70、轮 1.6→0.65、光柱 1.3→0.85
            //      → 死白 15.10%→5.26%，主角贴图饱和度 32.8→39.0（零光照时贴图本身是 126.4）。再往下压只是更暗、细节不再增加。
            //   验收：`WTCapture.Seat` 拍图 + 「隐藏雕像作差」得到主角精确掩膜（矩形框里只有 58% 是主角，会被背景污染，别用矩形框量）。
            //   **没动场景级的 ambientSkyColor / reflectionIntensity 和 Sun** —— 沙盘也靠它们，是用户审过的定稿。
            // 注意：上面那句"灯只照封面这一层（Cover 层）"是假的——工程里**没有 Cover 层**（只有 Default/TransparentFX/Ignore Raycast/Water/UI），
            //   封面 88 个渲染器全在 Default，四盏灯 cullingMask 都是 −1。真要隔离得先建层再设 mask，现在封��灯也照沙盘、Sun 也照封面。
            // 11-19 下午（用户：“你再调整试试”，和 D2 样图并排看主角还是发浅）：两处
            //   1) 光柱 LightShaft 原来是双面叠加的圆锥，朝玩家那面盖在主角身上 → 改成只画后半面（见 Shaft()）。
            //   2) 场景的 Sun 不再照主角：主角渲染器放到第 2 个光照层（CoverHeroBuilder.HeroLayer），封面三盏灯同时照第 1、2 层（HeroLit），Sun 只照第 1 层。
            //      管线资产 PC_RPAsset 的光照层（m_SupportsLightLayers）本来就开着；Sun 和沙盘一点没动。
            //   之后主光改暖、加强（1.15，色 1/0.74/0.46）、轮廓光 0.9：主角掩膜内 死白 3.03%→0.60%，最暗 10% 的亮度 69→30，饱和度 39.9→57.8。
            //   量法：/tmp/sp6/measure.py（隐藏雕像作差取掩膜；拍之前把 PreviewOnly / WarTable_Board 两个根对象关掉，用 GetRootGameObjects 还原）。
            if (L.collage)
            {
                var keyGo = new GameObject("KeyLight"); keyGo.transform.SetParent(root, false);
                keyGo.transform.localPosition = KeyAt;   // 11-19 傍晚：原来在右上方 (−0.35, 0.55, 0.45) 往下打，盔顶和肩甲顶面最亮；现在压低、挪到正前偏左，光打在胸口和面罩上
                keyGo.transform.localRotation = Quaternion.LookRotation(HeroAim - KeyAt);
                var key = keyGo.AddComponent<Light>();
                key.type = LightType.Spot; key.color = KeyC; key.intensity = KeyI; key.range = 2.2f;   // 11-19：1.35 把盔甲打成死白 → 0.70；下午去掉罩在主角身上的光柱和 Sun 之后改成更暖的 1.15（见上面“11-19 下午”）
                key.spotAngle = 70f; key.innerSpotAngle = 30f; key.shadows = LightShadows.Soft; key.shadowStrength = 0.85f; key.shadowBias = 0.02f;
                HeroLit(key);
                var rimGo = new GameObject("RimLight"); rimGo.transform.SetParent(root, false);
                rimGo.transform.localPosition = RimAt;   // 11-19 傍晚：换到主光的对侧后方，只勾一道边
                rimGo.transform.localRotation = Quaternion.LookRotation(HeroAim - RimAt);   // 11-19：原来 (12,−126,0) 主角在光锥外，这盏灯一直白费
                var rim = rimGo.AddComponent<Light>();
                rim.type = LightType.Spot; rim.color = RimC; rim.intensity = RimI; rim.range = 2f;   // 11-19：摆正后 1.6 太冲 → 0.65；下午主角不再吃 Sun 以后提到 0.9；颜色从 (0.62,0.76,1) 收淡——从轮廓光那一侧 45° 看主角蓝得发艳、显卡通
                rim.spotAngle = 92f; rim.innerSpotAngle = 40f; rim.shadows = LightShadows.None;
                HeroLit(rim);
            }

            // ── 左：战盔 + 朱砂 + 顶光 + 细沙 + 漂浮件
            Transform helm = null; CoverHero hero = null;
            if (L.collage) hero = CoverHeroBuilder.Build(root);   // 11-18：中间是一尊会动的重甲骑士（部件没做好时返回 null，先空着）
            else { helm = Helm(root); if (L.free) helm.localRotation = Quaternion.Euler(0, L.helmYaw, L.helmRoll); }
            Transform ink;
            if (L.collage) ink = Quad(root, "InkSplash", Tex("fx_halo"), new Color(1f, 1f, 1f, 0.82f), L.inkPos, L.inkSize, 2992, rotZ: L.inkRot);   // 11-18 用户：别成一张金盘子 → 透明度压到一半，只剩一圈金线   // 盔后面的金线日轮（开场时从左往右显出来，之后慢慢转）
            else if (L.free) ink = Quad(root, "InkSplash", Tex("ink_splash"), new Color(1f, 1f, 1f, 0.9f), L.inkPos, L.inkSize, 3001, rotZ: L.inkRot);
            else ink = Quad(root, "InkSplash", Tex("ink_splash"), new Color(1f, 1f, 1f, 0.9f), HelmAt + new Vector3(0.01f, 0.03f, -0.11f), 0.5f, 3001, rotZ: 14f);
            if (L.flags != null) foreach (var fl in L.flags) Banner(root, fl.team, fl.bottom, fl.top, fl.w, fl.h);
            if (!L.collage) Shaft(root);   // 11-19 傍晚 用户：“头顶不用打光”——拼贴封面不要顶上的光柱和顶灯
            if (L.collage) Embers(root);   // 11-18 用户：氛围要跟样图一样 → 岩座周围飘起暖色余烬
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
            var bone = L.collage ? Pbr("M_C6_TitleSteel", new Color(0.84f, 0.81f, 0.75f), 0.42f, 0.48f, 2012)   // 11-19 晚 用户：“标题太暗”“还是不够亮” → 亮的做旧钢（原 0.40/0.415/0.45、金属 0.8 正面几乎是黑的；第一轮 0.62 / 金属 0.55 仍偏暗）；试过象牙色金属 0.35（像塑料）、亮钢金属 0.8（发灰发平）   // 11-18：游戏名是做旧钢的正面 + 黄铜倒角侧面（样图 D2），不要重影
                                 : Pbr("M_C6_Bone", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 2012);
            var boneSide = L.collage ? Pbr("M_C6_TitleSide", new Color(0.62f, 0.45f, 0.19f), 1f, 0.5f, 2010)   // 11-18：大字的侧面是暗铜（正面象牙白），转头时侧面带一点金属反光
                                     : Pbr("M_C6_BoneSide", new Color(0.22f, 0.2f, 0.18f), 0f, 0.3f, 2010);
            var red = L.collage ? Unlit("M_C6_Ghost", null, new Color(0.55f, 0.38f, 0.14f, 0.9f), 3004, cull: CullMode.Back)   // 11-18 拼贴封面：重影是暗金（原来朱红）
                                : Unlit("M_C6_Ghost", null, new Color(0.62f, 0.09f, 0.06f, 0.85f), 3000, cull: CullMode.Back);
            var title = new GameObject("Title").transform; title.SetParent(root, false);
            var introChars = new List<Transform>(); var introGhosts = new List<Transform>();
            title.localPosition = new Vector3(0, 0, L.centered ? L.z : 0.02f);
            var ghost = new GameObject("Ghost").transform; ghost.SetParent(title, false);
            ghost.localPosition = L.collage ? new Vector3(-0.006f, -0.005f, -0.016f) : new Vector3(-0.005f, -0.004f, -0.012f);   // 重影：往右下错开、往后退
            title.localRotation = Quaternion.Euler(0, 0, L.tilt);
            if (L.free)
            {
                title.localPosition = Vector3.zero; title.localRotation = Quaternion.identity;
                int wk = 0;
                foreach (var (f, pp, h, rot) in L.fchars)
                {
                    foreach (var (par, fm, sm) in new[] { (title, bone, boneSide), (ghost, red, red) })
                    {
                        if (L.collage && par == ghost) continue;
                        var pc = Piece(par, f.StartsWith("en_") ? Dir6 : Dir5, f, TitleShape == 0 ? h : TitleH, pp, fm, sm);
                        if (!pc) continue;
                        var c0 = Bounds(pc.gameObject).center; var pose = WordPose(wk, Bounds(pc.gameObject).size.x, c0, rot);
                        pc.position += pose.p - c0; c0 = pose.p; pc.RotateAround(c0, root.forward, pose.rot);
                        if (TitleShape != 0) pc.localScale = Vector3.Scale(pc.localScale, new Vector3(1f, 1f, TitleDepth));
                        if (par == title) titleLow = Mathf.Min(wk == 0 ? float.MaxValue : titleLow, Bounds(pc.gameObject).min.y);
                        var pivot = new GameObject("Char_" + f).transform; pivot.SetParent(par, false);
                        pivot.position = c0; pc.SetParent(pivot, true);
                        (par == title ? introChars : introGhosts).Add(pivot);
                    }
                    wk++;
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
            ghostEn.localPosition = L.collage ? new Vector3(-0.006f, -0.005f, -0.016f) : new Vector3(-0.004f, -0.003f, -0.01f);
            var introCharsEn = new List<Transform>(); var introGhostsEn = new List<Transform>();
            if (L.free && TitleShape != 0 && TitleGemOn)   // 尖角处一颗黄铜菱形（两个语言共用；11-19 晚用户要去掉）
            {
                var gem = Prim(root, PrimitiveType.Cube, boneSide, TitleApex + (TitleShift[0] + TitleShift[1]) * 0.5f + new Vector3(0f, 0f, 0.016f), new Vector3(TitleH * 0.34f, TitleH * 0.34f, 0.006f));
                gem.name = "TitleGem"; gem.localRotation = Quaternion.Euler(0f, 0f, 45f); gem.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            int wkEn = 0;
            foreach (var (f, pp, h, rot) in TitleWords)   // 11-18：改名后英文版标题和中文版是同一组词
            {
                foreach (var (par, fm, sm) in new[] { (titleEnRoot, bone, boneSide), (ghostEn, red, red) })
                {
                    if (L.collage && par == ghostEn) continue;
                    var pc = Piece(par, Dir6, f, TitleShape == 0 ? h : TitleH, pp, fm, sm);
                    if (!pc) continue;
                    var c0 = Bounds(pc.gameObject).center; var pose = WordPose(wkEn, Bounds(pc.gameObject).size.x, c0, rot);
                    pc.position += pose.p - c0; c0 = pose.p; pc.RotateAround(c0, root.forward, pose.rot);
                    if (TitleShape != 0) pc.localScale = Vector3.Scale(pc.localScale, new Vector3(1f, 1f, TitleDepth));
                    var pivot = new GameObject("Word_" + f).transform; pivot.SetParent(par, false);
                    pivot.position = c0; pc.SetParent(pivot, true);
                    (par == titleEnRoot ? introCharsEn : introGhostsEn).Add(pivot);
                }
                wkEn++;
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
                if (L.sealSize > 0f) seal = Quad(root, "Seal", Tex("seal"), new Color(0.85f, 0.8f, 0.78f, 0.95f), L.sealPos, L.sealSize, 3060, rotZ: L.sealRot);
                // 11-06 用户：标题有些空 → 英文副标题压一道浓墨笔刷底，接在“盘”字和朱印下方，把右下这块空地收住
                if (!L.noTagline) taglineBrush = Quad(root, "TaglineBrush", Tex("brush_2"), new Color(0.08f, 0.07f, 0.065f, 0.92f), L.enPos + new Vector3(-0.005f, 0.002f, -0.01f), L.enW * 1.35f, 3002, rotZ: L.enRot);
            }

            // ── 出品署名（11-18，规则见记忆 tripo-brand-credit）：官方 TRIPO 标志 + “Tripo XR 出品 / Tripo XR Developers”，一条藏青纸条，跟语言切换
            GameObject creditZh = null, creditEn = null;
            if (L.creditH > 0f)
                foreach (var lang in new[] { "zh", "en" })
                {
                    var tex = BrandTex($"Assets/_WarTable/Art/Brand/credit_{lang}.png");
                    if (!tex) continue;
                    // 纸条贴图四周有 14 px 的阴影边、总高 200 px：面片高 = 纸条高 × 200 / 172；两种语言的纸条左端（标志那头）对齐在同一处
                    float qh = L.creditH * 200f / 172f, qw = qh * tex.width / tex.height;
                    // 标题折成两段时：署名摆在折线下面正中、不斜（11-19 晚再续 用户：署名也往上挪，离标题最低点 3 → 1.6 cm）
                    var creditPos = TitleShape == 0 ? L.creditPos : new Vector3(0f, titleLow - 0.016f, TitleApex.z + 0.01f); float creditRot = TitleShape == 0 ? L.creditRot : 0f;
                    float wZh = qh * 1105f / 200f, dx = lang == "zh" ? 0f : -(qw - wZh) * 0.5f;
                    var q = Quad(root, "Credit_" + lang, tex, Color.white, creditPos + Quaternion.Euler(0, 0, creditRot) * new Vector3(TitleShape == 0 ? dx : 0f, 0f, 0f), qw, 3062, rotZ: creditRot);
                    if (lang == "zh") creditZh = q.gameObject; else creditEn = q.gameObject;
                }

            // ── 菜单：斜排错落
            var items = new List<MenuCover.Item>();
            // 主菜单（第 0 页）：出征 / 设置 / 退出；设置页（第 1 页，11-02 用户：语言放进设置）：语言 / 战速 / 暗度 / 返回
            var menu = new (MenuCover.Action a, string zh, string en, float x, float y, float rot)[]
            {
                (MenuCover.Action.Begin, "word_chuzheng", "menu_begin", -0.165f, -0.005f + L.menuDy, 4f),
                (MenuCover.Action.Demo, "word_yanshi", "menu_demo", -0.17f, -0.015f + L.menuDy, 2f),   // 11-19 晚：只有拼贴封面（collage）有这一项的纸片
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
            var curMat = Pbr("M_C6_MenuCurrent", L.collage ? new Color(0.89f, 0.72f, 0.42f) : new Color(0.78f, 0.2f, 0.14f), 0f, 0.35f, 3050);   // “当前 / 敬请期待”：拼贴封面里是黄铜色（原来朱红）
            var plaque = L.collage ? Tex("fx_ticket") : null; var plaqueHi = L.collage ? Tex("fx_ticket_hi") : null;
            // 底：三道不同的墨笔刷；亮底用最宽最实的那道（brush_1），刷出来要能铺满字的区域，字变炭黑压在上面
            var inks = new[] { Tex("brush_0"), Tex("brush_2"), Tex("brush_0") }; var hi = Tex("brush_1");
            // 笔刷贴图的墨迹不在图正中（透明度纵向重心：brush_0 在上 1/3、brush_2 居中、brush_1 在下 1/3），
            // 按重心把每道笔刷挪到字中心 TextY 上：y = TextY − (0.5 − 重心) × 高宽比 × 宽
            const float TextY = 0.011f, HiY = TextY + 0.0164f;
            var inkY = new[] { TextY - 0.0173f, TextY - 0.003f, TextY - 0.0173f };
            var txt = Pbr("M_C6_MenuText", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 3050);
            const int MainCount = 6;       // 主菜单项数（11-10 加“时代”，11-19 加“演示”）
            const int SettingsCount = 5;   // 语言 / 战速 / 暗度 / 重新定位 / 返回
            for (int i = 0; i < menu.Length; i++)
            {
                var m = menu[i];
                int page = i < MainCount ? 0 : i < MainCount + SettingsCount ? 1 : 2;
                int slot = page == 0 ? i : page == 1 ? i - MainCount : i - MainCount - SettingsCount;
                var it = new GameObject("Item_" + m.a).transform; it.SetParent(root, false);
                if (L.collage)
                {
                    // 11-18 定稿样图 D2：每项是一张印着大字的撕边纸片（中 / 英各一张贴图，同样大小），背后一圈暖金色的光（指着时从左往右亮出来）
                    var sd = Shards[i]; var px = ShardPx(sd.key); float k = sd.h / px.h, w = px.w * k;
                    {
                        // 放射状（见 Fan / FanSettings / FanEra）：项的原点 = 内端（锚点），纸片沿射线往外伸；翻页时从主角身边往外长出来
                        // 11-19 晚 用户：“其他页也要像标题页一样，按钮都朝向中心” → 三页都这样排
                        var fs = (page == 0 ? Fan : page == 1 ? FanSettings : FanEra)[slot]; float a = fs.deg * Mathf.Deg2Rad, kk = fs.h / px.h;
                        var outDir = new Vector3(fs.side * Mathf.Cos(a), Mathf.Sin(a), 0f);
                        it.localPosition = new Vector3(0f, FanCY, FanZ + slot * Lz) + outDir * fs.rin;
                        it.localRotation = Quaternion.Euler(0f, 0f, -fs.tilt);
                        var local = Quaternion.Inverse(it.localRotation) * outDir;               // 射线方向在项本地（±x）
                        Transform Shard(string tex, float pxW, float extra, int queue, float z, out Rect hit)
                        {
                            float ww = pxW * kk, cx = Mathf.Sign(local.x) * (pxW - 2f * px.pad) * kk * 0.5f;   // 纸面（不含阴影边）的内端正好落在锚点
                            hit = new Rect(cx - (pxW - 2f * px.pad) * kk * 0.5f, -fs.h * 0.5f, (pxW - 2f * px.pad) * kk, fs.h);
                            return Quad(it, tex, Tex(tex), Color.white, new Vector3(cx, 0f, z), ww + extra * kk, queue);
                        }
                        float wz = ShardW(sd.key, true), we = ShardW(sd.key, false);
                        var gz = Shard($"shard_{sd.key}_glow_zh", wz, px.glow * 2f, 3010 + i, -0.0012f, out var hitZ).GetComponent<Renderer>();
                        var ge = Shard($"shard_{sd.key}_glow_en", we, px.glow * 2f, 3010 + i, -0.0012f, out var hitE).GetComponent<Renderer>();
                        foreach (var g in new[] { gz, ge }) { var pb = new MaterialPropertyBlock(); pb.SetFloat("_Reveal", 0f); g.SetPropertyBlock(pb); }
                        var sz = Shard($"shard_{sd.key}_zh", wz, 0f, 3020 + i, 0f, out _);
                        var se = Shard($"shard_{sd.key}_en", we, 0f, 3020 + i, 0f, out _);
                        // 11-19 晚续 用户：“看向哪个，哪个就变大、变成白色” → 每张叠一张同形状的象牙白版（平时全透明，MenuCover 按指着的程度淡入，同时整张放大）
                        var hz = Shard($"shard_{sd.key}_zh_hot", wz, 0f, 3040 + i, Lz, out _); hz.SetParent(sz, true);
                        var he = Shard($"shard_{sd.key}_en_hot", we, 0f, 3040 + i, Lz, out _); he.SetParent(se, true);
                        foreach (var q in new[] { hz, he }) { var r = q.GetComponent<Renderer>(); var pb = new MaterialPropertyBlock(); pb.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f)); r.SetPropertyBlock(pb); }
                        // 档位点（战速 / 暗度）：纸片右端留出来的那一截（贴图里离右边 54 / 92 / 130 px）；项本地 −x = 玩家右手边
                        Renderer[] fps = null;
                        if (m.a == MenuCover.Action.Speed || m.a == MenuCover.Action.Dim)
                        {
                            float cxp = sz.localPosition.x, wwp = wz * kk;
                            fps = new Renderer[Settings.Levels];
                            for (int q = 0; q < fps.Length; q++)
                            {
                                float fromRight = px.pad + 130f - 38f * q;
                                var d = Prim(it, PrimitiveType.Sphere, txt, new Vector3(cxp - (wwp * 0.5f - fromRight * kk), 0f, 0.001f), new Vector3(fs.h * 0.13f, fs.h * 0.13f, 0.002f));
                                d.name = "Pip" + q; fps[q] = d.GetComponent<Renderer>(); fps[q].shadowCastingMode = ShadowCastingMode.Off;
                            }
                        }
                        // “当前 / 敬请期待”：黄铜色小字，挂在纸片右下角外面
                        int fera = m.a == MenuCover.Action.EraPick ? i - MainCount - SettingsCount : -1;
                        Transform fcz = null, fce = null;
                        if (fera >= 0)
                        {
                            // 放射排布里挂在纸片正下方、居中（原来在右下角外面，放射后挤到了纸片内端的角上、和金线圈混在一起，看不清）
                            bool soon = !Settings.Ready(fera); var at = new Vector3(sz.localPosition.x, -fs.h * 0.5f - 0.012f, 0.001f);
                            fcz = Piece(it, Dir6, soon ? "word_jingqing" : "word_dangqian", 0.016f, at, curMat, curMat, byHeight: true);   // 按字高（原来 0.012 是按字宽，太小）
                            fce = Piece(it, Dir6, soon ? "menu_soon" : "menu_current", 0.011f, at, curMat, curMat, byHeight: true);
                            foreach (var c in new[] { fcz, fce }) if (c) { var b0 = Bounds(c.gameObject).center; c.localScale *= 1.7f; c.position += b0 - Bounds(c.gameObject).center; }   // 放射版里实测还是太小（字宽约 1 cm）→ 原地放大
                        }
                        items.Add(new MenuCover.Item
                        {
                            action = m.a, page = page, pips = fps, root = it, hit = hitZ, highlight = gz, hitZh = hitZ, hitEn = hitE, glowZh = gz, glowEn = ge,
                            zh = sz.gameObject, en = se.gameObject, texts = new Renderer[0], hot = new[] { hz.GetComponent<Renderer>(), he.GetComponent<Renderer>() },
                            era = fera, currentZh = fcz ? fcz.gameObject : null, currentEn = fce ? fce.gameObject : null,
                        });
                        continue;
                    }
#pragma warning disable CS0162   // 下面是放射排布之前的老写法，留着对照
                    it.localPosition = sd.p; it.localRotation = Quaternion.Euler(0, 0, -sd.tilt);
                    var glow = Quad(it, "Glow" + i, Tex($"shard_{sd.key}_glow"), Color.white, new Vector3(0f, 0f, -0.0012f), (px.w + px.glow * 2f) * k, 3010 + i);
                    var gr = glow.GetComponent<Renderer>(); var gpb = new MaterialPropertyBlock(); gpb.SetFloat("_Reveal", 0f); gr.SetPropertyBlock(gpb);
                    var qz = Quad(it, "ShardZh" + i, Tex($"shard_{sd.key}_zh"), Color.white, Vector3.zero, w, 3020 + i);
                    var qe = Quad(it, "ShardEn" + i, Tex($"shard_{sd.key}_en"), Color.white, Vector3.zero, w, 3020 + i);
                    bool pip = m.a == MenuCover.Action.Speed || m.a == MenuCover.Action.Dim;
                    Renderer[] ps = null;
                    if (pip)
                    {
                        // 三颗档位点：纸片右端留出来的那一截（贴图里离右边 54 / 92 / 130 px）。封面本地 −x = 玩家右手边
                        ps = new Renderer[Settings.Levels];
                        for (int q = 0; q < ps.Length; q++)
                        {
                            float fromRight = px.pad + 130f - 38f * q;
                            var d = Prim(it, PrimitiveType.Sphere, txt, new Vector3(-(w * 0.5f - fromRight * k), 0f, 0.001f), new Vector3(sd.h * 0.13f, sd.h * 0.13f, 0.002f));
                            d.name = "Pip" + q; ps[q] = d.GetComponent<Renderer>(); ps[q].shadowCastingMode = ShadowCastingMode.Off;
                        }
                    }
                    int era = m.a == MenuCover.Action.EraPick ? i - MainCount - SettingsCount : -1;
                    Transform cz = null, ce = null;
                    if (era >= 0)
                    {
                        // “当前 / 敬请期待”：黄铜色小字，挂在纸片右下角外面
                        bool soon = !Settings.Ready(era); var at = new Vector3(-w * 0.22f, -sd.h * 0.5f - 0.009f, 0.001f);
                        cz = Piece(it, Dir6, soon ? "word_jingqing" : "word_dangqian", 0.012f, at, curMat, curMat);
                        ce = Piece(it, Dir6, soon ? "menu_soon" : "menu_current", 0.0075f, at, curMat, curMat, byHeight: true);
                    }
                    items.Add(new MenuCover.Item
                    {
                        action = m.a, page = page, pips = ps, root = it, hit = new Rect(-w * 0.5f, -sd.h * 0.5f, w, sd.h), highlight = gr,
                        zh = qz.gameObject, en = qe.gameObject, texts = new Renderer[0],
                        era = era, currentZh = cz ? cz.gameObject : null, currentEn = ce ? ce.gameObject : null,
                    });
                    continue;
                }
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
                    if (L.collage) { var f0 = L.fmenu[0]; p0 = new Vector3(slot % 2 == 0 ? 0.014f : -0.014f, f0.p.y + 0.012f, f0.p.z + slot * Lz); step = new Vector3(0f, -0.0425f, 0f); rot = slot % 2 == 0 ? 1.5f : -1.5f; sc = 0.76f; }   // 拼贴封面的设置页：五张票根一左一右错开叠下去
                    else if (L.free) { var f0 = L.fmenu[0]; p0 = f0.p; step = new Vector3(0f, -0.041f, 0f); rot = 0f; sc = 0.82f; }
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
                float ticketW = TicketW, textW = 0f;
                float tx = hasPips ? 0.022f : 0f;   // 有档位点的项：字往左挪，右边留出三颗点
                var inkQ = L.collage ? Quad(it, "Ink" + i, plaque, Color.white, new Vector3(TicketDx, TextY, -0.0022f), TicketW / TicketRatio, 3020 + i)
                                     : Quad(it, "Ink" + i, inks[i % 3], new Color(0.05f, 0.045f, 0.04f, 0.93f), new Vector3(-0.01f, inkY[i % 3], -0.004f), 0.24f, 3020 + i);   // 笔刷图案重心偏起笔一侧，往右挪让字落在墨最浓处
                var h = L.collage ? Quad(it, "Highlight" + i, plaqueHi, Color.white, new Vector3(TicketDx, TextY, -0.0018f), TicketW / TicketRatio, 3040 + i)
                                  : Quad(it, "Highlight" + i, hi, new Color(0.93f, 0.9f, 0.83f, 0.97f), new Vector3(-0.008f, HiY, -0.002f), 0.25f, 3030 + i, rotZ: -1.5f);
                var hr = h.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Reveal", 0f); hr.SetPropertyBlock(mpb);
                // 11-17 用户：字不要太大 → 拼贴封面里中文字高 2.6 cm（原 4.5）、英文 1.3 cm（原 2.4）；“出征”再乘这一项的 1.25 倍
                var zh = Piece(it, Dir6, m.zh, L.collage ? 0.026f : 0.045f, new Vector3(tx, TextY, L.collage ? 0.0005f : 0.004f), txt, txt);
                var en = Piece(it, Dir6, m.en, L.collage ? (hasPips ? 0.0115f : 0.013f) : hasPips ? 0.02f : 0.024f, new Vector3(tx, TextY, L.collage ? 0.0005f : 0.004f), txt, txt, byHeight: true);
                int eraIdx = m.a == MenuCover.Action.EraPick ? i - MainCount - SettingsCount : -1;
                Transform curZh = null, curEn = null;
                if (eraIdx >= 0)
                {
                    // “当前”标记：朱红小字，挂在字的右边（封面本地 −x = 玩家右手边）
                    bool soon = !Settings.Ready(eraIdx);   // 还没做的时代：标记换成“敬请期待 / COMING SOON”（点了才亮）
                    // 11-12：标记挂在字的正下方（时代页改成两列，挂在右边会压到另一列）
                    float my = TextY - (L.collage ? 0.037f : 0.036f), mzh = L.collage ? 0.0125f : 0.019f, men = L.collage ? 0.0078f : 0.0105f, mz = L.collage ? 0.0005f : 0.004f;   // 拼贴封面：小一号，挂在票根下沿外面
                    curZh = soon ? Piece(it, Dir6, "word_jingqing", mzh, new Vector3(0f, my, mz), curMat, curMat)
                                 : Piece(it, Dir6, "word_dangqian", mzh, new Vector3(0f, my, mz), curMat, curMat);
                    curEn = soon ? Piece(it, Dir6, "menu_soon", men, new Vector3(0f, my + (L.collage ? 0.001f : 0.003f), mz), curMat, curMat, byHeight: true)
                                 : Piece(it, Dir6, "menu_current", men, new Vector3(0f, my + (L.collage ? 0.001f : 0.003f), mz), curMat, curMat, byHeight: true);
                }
                // 11-07：英文单词（TUTORIAL / SETTINGS）比两个汉字宽，笔刷底和看中时的亮笔刷按中英里较宽的那个横向拉长（只拉宽、不变高）
                {
                    float tw = 0f;
                    foreach (var pc in new[] { zh, en }) if (pc) tw = Mathf.Max(tw, Bounds(pc.gameObject).size.x / Mathf.Max(1e-4f, it.lossyScale.x));
                    textW = tw;
                    float need = tw * 1.3f;
                    if (L.collage)
                    {
                        // 票根：字（加档位点）放不下就横向拉长（只拉宽、不变高）
                        need = tw + 0.058f + (hasPips ? 0.05f : 0f);
                        ticketW = Mathf.Max(TicketW, need);
                        if (ticketW > TicketW) foreach (var q in new[] { inkQ, h }) { var sc = q.localScale; q.localScale = new Vector3(sc.x * ticketW / TicketW, sc.y, sc.z); }
                        if (hasPips) foreach (var q in new[] { inkQ, h }) q.localPosition += new Vector3(-0.012f, 0f, 0f);   // 带档位点的：票根往右挪，把右边三颗点包进来
                    }
                    else if (need > 0.24f) { var sc = inkQ.localScale; inkQ.localScale = new Vector3(sc.x * need / 0.24f, sc.y, sc.z); }
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
                        var d = L.collage ? Prim(it, PrimitiveType.Sphere, txt, new Vector3(tx - textW * 0.5f - 0.016f - 0.012f * k, TextY, 0.0005f), new Vector3(0.0058f, 0.0058f, 0.0015f))
                                          : Prim(it, PrimitiveType.Sphere, txt, new Vector3(-0.072f - 0.014f * k, TextY, 0.004f), new Vector3(0.0075f, 0.0075f, 0.003f));
                        d.name = "Pip" + k; pips[k] = d.GetComponent<Renderer>(); pips[k].shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
                var hitRect = L.collage ? new Rect(inkQ.localPosition.x - ticketW * 0.5f, TextY - TicketH * 0.5f, ticketW, TicketH)   // 拼贴封面：整张票根都算热区
                                        : HitRect(it, new[] { zh, en }, pips, TextY);
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
            mc.below = 0.04f;   // 11-19 晚 用户两次“整个标题页整体往下挪一点”：−0.04（原点高出眼睛 4 cm）→ 0 → 0.04（原点低于眼睛 4 cm），共下移 8 cm
            //   // 11-13 用户：封面整体往上挪一点 → 封面原点从“眼睛下 4 cm”改到“眼睛上 4 cm”（整体抬 8 cm，约 5.6°；按钮因为另外下移了 5 cm，净抬约 2°）
            mc.items = items.ToArray(); mc.helm = helm; mc.drifters = drifters.ToArray();
            mc.dimmer = dim.GetComponent<Renderer>(); mc.titleEn = enT ? enT.gameObject : null; mc.creditZh = creditZh; mc.creditEn = creditEn;
            if (L.collage)
            {
                var wr = new List<Renderer>(); var wt = new List<float>();
                void W(Transform t, float at) { if (t) { wr.Add(t.GetComponent<Renderer>()); wt.Add(at); } }
                W(creditZh ? creditZh.transform : null, 1.0f); W(creditEn ? creditEn.transform : null, 1.0f);
                mc.introWipes = wr.ToArray(); mc.introWipeAt = wt.ToArray(); mc.halo = ink; mc.hero = hero;
            }
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

        /// <summary>封面的灯同时照第 1 层（封面其他件）和主角所在的第 2 个光照层；场景的 Sun 只照第 1 层，所以不照主角。</summary>
        internal static void HeroLit(Light l)
        {
            int m = 1 | (int)CoverHeroBuilder.HeroLayer;
            l.renderingLayerMask = m; UnityEngine.Rendering.Universal.LightExtensions.GetUniversalAdditionalLightData(l).renderingLayers = (uint)m;
        }

        /// <summary>顶光：从战盔上方斜照下来的一束暖光（叠加的锥形光柱 + 一盏聚光灯照亮战盔）。</summary>
        static void Shaft(Transform root)
        {
            var top = HelmAt + new Vector3(-0.05f, 0.62f, -0.12f);
            var go = new GameObject("LightShaft");
            go.transform.SetParent(root, false);
            go.transform.localPosition = top;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.down, (HelmAt - top).normalized);
            go.AddComponent<MeshFilter>().sharedMesh = Cone(0.03f, 0.32f, 0.92f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Unlit("M_C6_Shaft", null, new Color(1f, 0.8f, 0.55f, 0.42f), 3005, cull: CullMode.Front, additive: true, edgeFade: 1.6f, vFade: 1f);   // 11-19 下午：只画圆锥远离玩家的那半面（Cull Front）——原来双面叠加，朝玩家那面直接盖在主角身上（中心 +66 亮度的一层暖雾，暗部全被抬亮，主角发浅）；后半面被主角挡住，光只落在他身后和四周。少了一面所以 0.26 → 0.42
            r.shadowCastingMode = ShadowCastingMode.Off;
            var spot = new GameObject("Spot").AddComponent<Light>();
            spot.transform.SetParent(root, false);
            spot.transform.localPosition = top;
            spot.transform.localRotation = Quaternion.LookRotation(HelmAt - top);
            spot.type = LightType.Spot; spot.spotAngle = 36f; spot.innerSpotAngle = 18f; spot.range = 1.6f;
            spot.intensity = 0.85f; spot.color = new Color(1f, 0.86f, 0.66f); spot.shadows = LightShadows.None;   // 11-19：和主光 / 轮廓光一起压，见上面过曝巡检
            HeroLit(spot);
        }

        /// <summary>岩座周围往上飘的余烬火星（11-18：定稿样图 D2 里雕像脚下有飘动的火星和尘，氛围靠它们撑起来）。</summary>
        static void Embers(Transform root)
        {
            var go = new GameObject("Embers");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, -0.13f, 0.02f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.012f, 0.042f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0016f, 0.0042f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.56f, 0.2f, 1f), new Color(1f, 0.82f, 0.42f, 1f));
            main.maxParticles = 90; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = -0.006f;   // 往上飘
            var em = ps.emission; em.rateOverTime = 14f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.2f, 0.02f, 0.12f);
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(1f, 0.6f, 0.22f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.4f), 0.4f), new GradientColorKey(new Color(0.8f, 0.4f, 0.12f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) },
            });
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.03f; noise.frequency = 0.4f; noise.scrollSpeed = 0.18f;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = Unlit("M_C6_Ember", Dot(), Color.white, 3006, cull: CullMode.Off, additive: true);
            rend.renderMode = ParticleSystemRenderMode.Billboard; rend.shadowCastingMode = ShadowCastingMode.Off;
            ps.Simulate(4f, true, true); ps.Play();
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

        /// <summary>公司标志一类的品牌图（Assets/_WarTable/Art/Brand）：不压缩、不改尺寸、不拉伸；带多级渐远（离远了不闪）。</summary>
        internal static Texture2D BrandTex(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("[Cover] 缺品牌图 " + path); return null; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.npotScale != TextureImporterNPOTScale.None || !ti.mipmapEnabled || !ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp)
            {
                ti.textureType = TextureImporterType.Default; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = true; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 8; ti.ClearPlatformTextureSettings("Android");
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
        /// <summary>主角自己的反射环境（11-19 傍晚）：通用那份正上方有一块很亮的灯箱（亮度 2.2），金属盔顶和肩甲顶面反射它 → 头顶发白。
        /// 这份没有顶光：齐腰高度一圈暖色灯箱（明暗交替，封面跟着玩家转到哪个朝向都差不多）+ 地面一片暗橙（熔岩的反光），头顶是黑的。</summary>
        internal static Cubemap BakeHeroStudio()
        {
            var at = new Vector3(0, 60, 0);
            var tmp = new GameObject("_HeroStudio");
            void Box(Vector3 pos, Vector3 size, Color c)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.transform.SetParent(tmp.transform, false);
                q.transform.position = at + pos; q.transform.localScale = size;
                q.transform.rotation = Quaternion.LookRotation(-pos.normalized);
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c);
                q.GetComponent<Renderer>().sharedMaterial = m;
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f; bool hot = i % 2 == 0;
                Box(new Vector3(Mathf.Cos(a) * 1.5f, hot ? 0.42f : 0.2f, Mathf.Sin(a) * 1.5f), hot ? new Vector3(1.0f, 0.75f, 1) : new Vector3(0.7f, 0.9f, 1), hot ? HeroStudioHot : HeroStudioDim);
            }
            Box(new Vector3(0, -1.2f, 0), new Vector3(4f, 4f, 1), HeroStudioFloor);
            var probe = new GameObject("_HeroStudioProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(tmp.transform, false);
            probe.transform.position = at;
            probe.mode = ReflectionProbeMode.Custom;
            probe.resolution = 256; probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.03f, 0.027f, 0.025f);
            probe.size = Vector3.one * 6f;
            Lightmapping.BakeReflectionProbe(probe, HeroStudioPath);
            Object.DestroyImmediate(tmp);
            AssetDatabase.ImportAsset(HeroStudioPath);
            return AssetDatabase.LoadAssetAtPath<Cubemap>(HeroStudioPath);
        }
        static readonly Color HeroStudioHot = new(1.45f, 1.12f, 0.80f), HeroStudioDim = new(0.42f, 0.34f, 0.30f), HeroStudioFloor = new(0.40f, 0.15f, 0.05f);

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
