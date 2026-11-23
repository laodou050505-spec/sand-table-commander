using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using WarTable.Game;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 搭结算板（Runtime/Game/ResultBoard）和战场提示（FieldUI）的材质。由 WarTable/7 装配玩法调用。
    /// 立体字：ArtSource/UI（书法字稿 gen/*_crop.png、Cinzel 英文 en/、数字 digits/）→ Tools/cover_v5_meshes.sh → Art/Models/UI。
    /// 版式（板子本地米，x 正 = 玩家左边）：左边大字“胜 / 败”压在朱砂上；右边上排三颗朱印星，中排王国旗 + 存活 / 出战，
    /// 下排氏族旗 + 击溃 / 敌军总数；最下一行两项菜单“再战 / 回营”。
    /// </summary>
    public static class ResultBuilder
    {
        const string Dir = "Assets/_WarTable/Art/Models/UI";
        const float TextY = 0.011f, HiY = TextY + 0.0164f, InkY = TextY - 0.0173f;
        /// <summary>按钮牌的中心高度（字的中心）。</summary>
        const float PlaqueY = TextY;

        /// <summary>票根按钮：纸面宽（米）、相对字中心往玩家左手边偏多少（票根左端有一截存根，字排在右边大半张上）、字高。</summary>
        const float TicketW = 0.165f, TicketDx = 0.014f, WordZh = 0.019f, WordEn = 0.0105f;
        /// <summary>票根的热区（项本地）：整张票都算，不只是字。</summary>
        static Rect TicketHit => new Rect(TicketDx - TicketW * 0.5f, TextY - 0.027f, TicketW, 0.054f);

        /// <summary>
        /// 11-17 晚用户：这些界面虽然是好几层纸，但要“贴在一个平面上”——凑近了不能看出一层层是分开的。
        /// 原来各层前后错开几厘米（羊皮纸 −8.5 cm … 大字 +2 cm），现在每层只差 Lz = 0.4 mm（只为了排序稳定；谁盖谁靠渲染队列），
        /// 层次感交给贴图里烘好的阴影。立体字和按钮整体在前后方向压到 Thin 倍。
        /// </summary>
        const float Lz = 0.0004f, Thin = 0.25f;
        /// <summary>在 t 下面套一层前后压薄的节点，返回它（立体字挂在它下面；t 自己的缩放留给入场动画）。</summary>
        static Transform Flat(Transform t)
        {
            var f = new GameObject("Flat").transform; f.SetParent(t, false); f.localScale = new Vector3(1f, 1f, Thin); return f;
        }

        static Dictionary<string, float> ratio;
        /// <summary>拼贴件的贴图四周留了一圈阴影：想让“纸面”宽 content 米，面片要摆多宽（Tools/collage_sizes.json 记着纸面宽 / 整张宽）。</summary>
        static float Full(string name, float content)
        {
            if (ratio == null)
            {
                ratio = new Dictionary<string, float>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText("Tools/collage_sizes.json"), "\"(\\w+)\":\\s*([0-9.]+)"))
                    ratio[m.Groups[1].Value] = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            return content / (ratio.TryGetValue(name, out var r) ? r : 1f);
        }

        public static ResultBoard Build(Transform parent)
        {
            var root = new GameObject("Result").transform; root.SetParent(parent, false);
            var bone = CoverBuilder.Pbr("M_R_Bone", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 2012);
            var boneSide = CoverBuilder.Pbr("M_R_BoneSide", new Color(0.22f, 0.2f, 0.18f), 0f, 0.3f, 2010);
            var red = CoverBuilder.Unlit("M_R_Ghost", null, new Color(0.55f, 0.38f, 0.14f, 0.9f), 3004, cull: CullMode.Back);   // 队列要排在日轮（3001）之后，不然被日轮盖住看不到   // 大字身后错开的一层：暗金（原来是朱红）
            var txt = CoverBuilder.Pbr("M_R_Text", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 3050);
            var rb = root.gameObject.AddComponent<ResultBoard>();
            rb.distance = 0.5f; rb.below = 0.12f;   // 眼前略低，压在沙盘上方，不挡视线正中；11-07 拉近到伸手能点到

            // ── 墨底：两道宽墨笔刷叠成一块深色底，结算浮在沙盘 / 真实房间上时字才看得清（骨白字在浅色背景上会消失）
            // 11-17 用户：全游戏 UI 改成西式奇幻、要高级、不要中国风 → 墨笔刷底换成一张深色渐变卡片（细金线框，Tools/make_fantasy_ui.py）
            // 同日用户：“UI 面板不要这么规整”→ 底是一面不对称的旗幡（斜切上沿、偏心燕尾、断开的金线、一杆斜穿出去的长枪），
            // 后面再垫一层转了角度的剪影，露出错开的边；按钮牌一头尖一头缺口，错落着微微倾斜
            // 同日用户接着说：“不要太死板”“字不要太大，版面要更讲究更高级”（参照深蓝互动那一路的拼贴感，配色用我们自己的）
            // → 拼贴（Tools/make_collage_ui.py）：最底下一张撕边羊皮纸，上面压一张藏青纸（内容的底），左边一枚圆形日轮纸片托着大字，
            //   左上角斜贴一条花体拉丁文纸条（胜 Ad Astra / 败 Per Aspera），按钮是两张票根。每层前后错开、各转一点角度，头显里有真实的层次。
            //   下面的坐标：x 正 = 玩家左手边（和观看者看到的左右相反）；面片的 rotZ 正 = 观看者看到的逆时针。
            var paper = CoverBuilder.Quad(root, "R_Paper", CoverBuilder.Tex("fx_paper"), Color.white, new Vector3(0.03f, 0.012f, -6 * Lz), Full("fx_paper", 0.5f), 2993, rotZ: 4f);
            var card = CoverBuilder.Quad(root, "R_Card", CoverBuilder.Tex("fx_card"), Color.white, new Vector3(-0.03f, -0.004f, -5 * Lz), Full("fx_card", 0.47f), 2995, rotZ: -1.5f);
            // 用户看后指着人数下面、按钮上面那一块说“这里有点空”→ 盖一枚磨旧的暗金邮戳（圆章 + 波浪销戳线），斜着，下缘被票根压住
            var stamp = CoverBuilder.Quad(root, "R_Stamp", CoverBuilder.Tex("fx_stamp"), Color.white, new Vector3(-0.168f, -0.064f, -4 * Lz), 0.125f, 2996, rotZ: -9f);
            // ── 大字 + 日轮
            var bigAt = new Vector3(0.125f, 0.03f, 3 * Lz);
            rb.splash = CoverBuilder.Quad(root, "R_Splash", CoverBuilder.Tex("fx_sun"), Color.white, new Vector3(bigAt.x, bigAt.y, -3 * Lz), Full("fx_sun", 0.215f), 3001).GetComponent<Renderer>();   // 圆形深色纸片 + 金色铜版画日轮（原来是朱砂泼墨）
            var sWin = CoverBuilder.Quad(root, "R_StripWin", CoverBuilder.Tex("fx_strip_win"), Color.white, new Vector3(0.15f, 0.148f, -1 * Lz), Full("fx_strip_win", 0.215f), 3002, rotZ: 6f);
            var sLose = CoverBuilder.Quad(root, "R_StripLose", CoverBuilder.Tex("fx_strip_lose"), Color.white, new Vector3(0.15f, 0.148f, -1 * Lz), Full("fx_strip_lose", 0.215f), 3002, rotZ: 6f);
            rb.stripWin = sWin.gameObject; rb.stripLose = sLose.gameObject;
            rb.wipes = new[] { paper, card, sWin, sLose, stamp }.Select(t => t.GetComponent<Renderer>()).ToArray(); rb.wipeAt = new[] { 0f, 0.06f, 0.42f, 0.42f, 0.95f };
            var big = new GameObject("Big").transform; big.SetParent(root, false); big.localPosition = bigAt;
            var ghost = new GameObject("Ghost").transform; ghost.SetParent(root, false); ghost.localPosition = big.localPosition + new Vector3(-0.0035f, -0.003f, -3 * Lz);
            var bigFlat = Flat(big); var ghostFlat = Flat(ghost);   // 立体字压薄（厚 4 mm → 1 mm 左右）；入场动画仍然缩放 big / ghost 本身
            var specs = new (string f, float size, bool byH)[] { ("word_sheng", 0.078f, true), ("res_victory", 0.15f, false), ("word_bai", 0.078f, true), ("res_defeat", 0.14f, false) };
            var bigs = new List<GameObject>(); var ghosts = new List<GameObject>();
            foreach (var (f, size, byH) in specs)
            {
                bigs.Add(CoverBuilder.Piece(bigFlat, Dir, f, size, Vector3.zero, bone, boneSide, byHeight: byH)?.gameObject);
                ghosts.Add(CoverBuilder.Piece(ghostFlat, Dir, f, size, Vector3.zero, red, red, byHeight: byH)?.gameObject);
            }
            rb.bigs = bigs.ToArray(); rb.ghosts = ghosts.ToArray(); rb.bigChar = big; rb.bigGhost = ghost;

            // ── 三颗朱印星（印章贴图，挣到的亮、没挣到的暗）
            var seal = CoverBuilder.Tex("fx_star");   // 四芒金星（原来是朱印）
            rb.stars = Enumerable.Range(0, 3).Select(i =>
            {
                var q = CoverBuilder.Quad(root, "R_Star" + i, seal, Color.white, new Vector3(-0.06f - 0.034f * i, 0.07f, -1 * Lz), 1f, 3060);
                q.localScale = Vector3.one * rb.StarSize;
                q.GetComponent<Renderer>().sharedMaterial = CoverBuilder.Unlit("M_R_Star", seal, Color.white, 3060);
                return q.GetComponent<Renderer>();
            }).ToArray();

            // ── 人数：旗 + 数字（数字压在墨笔刷上）
            rb.StarSize = 0.03f; rb.digitGap = 0.0026f;
            rb.ours = Row(root, "Ours", "fx_flag_ours", 0.024f, txt);
            rb.theirs = Row(root, "Theirs", "fx_flag_foe", -0.018f, txt);

            // ── 菜单：再战 / 回营
            var hi = CoverBuilder.Tex("fx_plaque_hi"); var ink = CoverBuilder.Tex("fx_plaque");   // 两头收尖的按钮牌：平时深色金边，悬停时亮成象牙金
            var menu = new (ResultBoard.Action a, string zh, string en, float x)[] { (ResultBoard.Action.Again, "word_zaizhan", "menu_again", 0.04f), (ResultBoard.Action.Camp, "word_huiying", "menu_camp", -0.125f) };   // 11-17 用户：两个按钮往中间收
            var items = new List<ResultBoard.Item>();
            for (int i = 0; i < menu.Length; i++)
            {
                var m = menu[i];
                var it = new GameObject("Item_" + m.a).transform; it.SetParent(root, false);
                it.localPosition = new Vector3(m.x, (i == 0 ? -0.118f : -0.132f) - TextY, (2 + i) * Lz); it.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -2f : 1.5f); it.localScale = new Vector3(1f, 1f, Thin);   // 项的 rotZ 正 = 观看者看到的顺时针
                CoverBuilder.Quad(it, "R_Ink_" + m.a, ink, Color.white, new Vector3(TicketDx, PlaqueY, -0.004f), Full("fx_plaque", TicketW), 3020 + i);
                var h = CoverBuilder.Quad(it, "R_Hi_" + m.a, hi, Color.white, new Vector3(TicketDx, PlaqueY, -0.002f), Full("fx_plaque", TicketW), 3030 + i);
                var hr = h.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Reveal", 0f); hr.SetPropertyBlock(mpb);
                var zh = CoverBuilder.Piece(it, Dir, m.zh, WordZh, new Vector3(0, TextY, 0.004f), txt, txt);
                var en = CoverBuilder.Piece(it, Dir, m.en, WordEn, new Vector3(0, TextY, 0.004f), txt, txt);
                // 11-20 战役：打赢后“收兵归营”换成“继续进军 / MARCH ON”（同一个按钮，运行时只开一套字）
                Transform zh2 = null, en2 = null;
                if (m.a == ResultBoard.Action.Camp)
                {
                    zh2 = CoverBuilder.Piece(it, Dir, "word_jinjun", WordZh, new Vector3(0, TextY, 0.004f), txt, txt);
                    en2 = CoverBuilder.Piece(it, Dir, "menu_march", WordEn, new Vector3(0, TextY, 0.004f), txt, txt);
                    if (zh2) zh2.gameObject.SetActive(false); if (en2) en2.gameObject.SetActive(false);
                }
                items.Add(new ResultBoard.Item
                {
                    action = m.a, root = it, highlight = hr, zh = zh ? zh.gameObject : null, en = en ? en.gameObject : null,
                    zhAlt = zh2 ? zh2.gameObject : null, enAlt = en2 ? en2.gameObject : null,
                    hit = TicketHit,
                    texts = new[] { zh, en, zh2, en2 }.Where(x => x).SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray(),
                });
            }
            rb.items = items.ToArray();
            root.gameObject.SetActive(false);
            return rb;
        }

        /// <summary>一行人数：左边一面小旗（队伍旗面图），右边墨笔刷上“a/b”，每位 0–9 和 / 各备一个字形，运行时只开一个。</summary>
        static ResultBoard.Number Row(Transform root, string name, string flag, float y, Material txt)
        {
            var row = new GameObject(name).transform; row.SetParent(root, false); row.localPosition = new Vector3(-0.058f, y, 0f); row.localScale = new Vector3(1f, 1f, Thin);
            var ft = CoverBuilder.Tex(flag);
            CoverBuilder.Quad(row, "R_Flag_" + name, ft, new Color(1f, 1f, 1f), new Vector3(0f, -0.001f, -0.002f), 0.021f, 3040);
            CoverBuilder.Quad(row, "R_Ink_" + name, CoverBuilder.Tex("fx_rule"), Color.white, new Vector3(-0.066f, -0.0135f, -0.003f), 0.16f, 3021);
            var nums = new GameObject("Digits").transform; nums.SetParent(row, false); nums.localPosition = new Vector3(-0.022f, 0f, 0.004f);
            var files = Enumerable.Range(0, 10).Select(d => "d" + d).Append("dslash").ToArray();
            var widths = new float[files.Length];
            var slots = new ResultBoard.Slot[7];   // 最多 “999/999”
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = new GameObject("Slot" + s).transform; slot.SetParent(nums, false);
                var gs = new GameObject[files.Length];
                for (int k = 0; k < files.Length; k++)
                {
                    var pc = CoverBuilder.Piece(slot, Dir, files[k], 0.017f, Vector3.zero, txt, txt);
                    if (!pc) continue;
                    var b = CoverBuilder.Bounds(pc.gameObject);
                    var pivot = new GameObject(files[k]).transform; pivot.SetParent(slot, false);
                    pivot.position = b.center; pc.SetParent(pivot, true);
                    widths[k] = b.size.x / Mathf.Max(1e-4f, root.lossyScale.x);
                    pivot.gameObject.SetActive(false);
                    gs[k] = pivot.gameObject;
                }
                slots[s] = new ResultBoard.Slot { glyphs = gs };
            }
            return new ResultBoard.Number { slots = slots, widths = widths, root = nums };
        }

        /// <summary>游戏途中的暂停键 + 暂停菜单（继续 / 回营）。暂停键是一枚墨底金圈的圆章（ArtSource/Cover/v6/alpha/btn_pause.png），
        /// 菜单和结算板同一套墨笔刷按钮，上下排两项。</summary>
        /// <summary>11-20：给已经搭好的结算板补上“继续进军 / MARCH ON”（不用整块重搭，场景里的引用不动）。</summary>
        public static string AddMarchWords(ResultBoard rb)
        {
            var it = rb.items.First(x => x.action == ResultBoard.Action.Camp);
            if (it.zhAlt) return "已经有了";
            var txt = it.zh.GetComponentInChildren<Renderer>(true).sharedMaterial;
            var at = new Vector3(0, TextY, 0.004f);
            var zh2 = CoverBuilder.Piece(it.root, Dir, "word_jinjun", WordZh, at, txt, txt);
            var en2 = CoverBuilder.Piece(it.root, Dir, "menu_march", WordEn, at, txt, txt);
            zh2.gameObject.SetActive(false); en2.gameObject.SetActive(false);
            it.zhAlt = zh2.gameObject; it.enAlt = en2.gameObject;
            it.texts = new[] { it.zh, it.en, it.zhAlt, it.enAlt }.SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray();
            UnityEditor.EditorUtility.SetDirty(rb); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rb.gameObject.scene);
            return $"原 {it.zh.transform.localPosition} {it.zh.transform.localScale} / 新 {zh2.localPosition} {zh2.localScale}";
        }

        public static PauseMenu BuildPause(Transform parent)
        {
            var root = new GameObject("Pause").transform; root.SetParent(parent, false);
            var pm = root.gameObject.AddComponent<PauseMenu>();
            var btnTex = CoverBuilder.Tex("btn_pause");
            var btn = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(btn.GetComponent<Collider>());
            btn.name = "PauseButton"; btn.transform.SetParent(root, false);
            var br = btn.GetComponent<Renderer>(); br.shadowCastingMode = ShadowCastingMode.Off;
            br.sharedMaterial = CoverBuilder.Unlit("M_P_Button", btnTex, Color.white, 3090);
            br.sharedMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);   // 不被沙盘、棋子挡住
            pm.button = btn.transform; pm.buttonRenderer = br;
            var panel = new GameObject("Panel").transform; panel.SetParent(root, false);
            // 墨底：一道宽笔刷托住两项
            // 11-17：和结算板同一套拼贴。用户看了第一版说“还是有点死板，东西都贴在角上”→ 不再把纸条贴在左上角、票根排成一列：
            //   后面斜垫一张羊皮纸碎片（往右下露出来），藏青纸反方向转一点；左上压一枚月轮纸片（和结算的日轮同一套铜版画，暂停 = 入夜歇兵），
            //   花体纸条从月轮上斜压过去、右端伸出纸外；两张票根一左一右错开成扇形，互相压着一点
            CoverBuilder.Quad(panel, "P_Paper", CoverBuilder.Tex("fx_paper_s"), Color.white, new Vector3(-0.045f, -0.05f, -6 * Lz), Full("fx_paper_s", 0.235f), 3002, rotZ: -7f);
            CoverBuilder.Quad(panel, "P_Card", CoverBuilder.Tex("fx_card_s"), Color.white, new Vector3(0.008f, -0.022f, -5 * Lz), Full("fx_card_s", 0.25f), 3004, rotZ: 3f);
            CoverBuilder.Quad(panel, "P_Moon", CoverBuilder.Tex("fx_moon"), Color.white, new Vector3(0.074f, 0.097f, -3 * Lz), Full("fx_moon", 0.125f), 3005);
            CoverBuilder.Quad(panel, "P_Strip", CoverBuilder.Tex("fx_strip_pause"), Color.white, new Vector3(-0.038f, 0.045f, -2 * Lz), Full("fx_strip_pause", 0.205f), 3006, rotZ: -4f);
            var txt = CoverBuilder.Pbr("M_P_Text", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 3050);
            var hi = CoverBuilder.Tex("fx_plaque_hi"); var ink = CoverBuilder.Tex("fx_plaque");
            var menu = new (PauseMenu.Action a, string zh, string en, float y)[] { (PauseMenu.Action.Resume, "word_jixu", "menu_resume", -0.021f), (PauseMenu.Action.Camp, "word_huiying", "menu_camp", -0.083f) };
            var items = new List<PauseMenu.Item>();
            for (int i = 0; i < menu.Length; i++)
            {
                var m = menu[i];
                var it = new GameObject("Item_" + m.a).transform; it.SetParent(panel, false);
                it.localPosition = new Vector3(i == 0 ? 0.016f : -0.036f, m.y - TextY, (2 + i) * Lz); it.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -3.5f : 3f); it.localScale = new Vector3(1f, 1f, Thin);
                CoverBuilder.Quad(it, "P_Ink_" + m.a, ink, Color.white, new Vector3(TicketDx, PlaqueY, -0.004f), Full("fx_plaque", TicketW), 3020 + i);
                var h = CoverBuilder.Quad(it, "P_Hi_" + m.a, hi, Color.white, new Vector3(TicketDx, PlaqueY, -0.002f), Full("fx_plaque", TicketW), 3030 + i);
                var hr = h.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Reveal", 0f); hr.SetPropertyBlock(mpb);
                var zh = CoverBuilder.Piece(it, Dir, m.zh, WordZh, new Vector3(0, TextY, 0.004f), txt, txt);
                var en = CoverBuilder.Piece(it, Dir, m.en, WordEn, new Vector3(0, TextY, 0.004f), txt, txt);
                items.Add(new PauseMenu.Item
                {
                    action = m.a, root = it, highlight = hr, zh = zh ? zh.gameObject : null, en = en ? en.gameObject : null,
                    hit = TicketHit,
                    texts = new[] { zh, en }.Where(x => x).SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray(),
                });
            }
            pm.items = items.ToArray(); pm.panel = panel;
            panel.gameObject.SetActive(false); btn.SetActive(false);
            return pm;
        }

        /// <summary>战场提示的材质：半透明、双面、深度测试（棋子挡得住光环）。</summary>
        public static Material FieldMat() => CoverBuilder.Unlit("M_FieldUI", null, Color.white, 3050);
    }
}
