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

        public static ResultBoard Build(Transform parent)
        {
            var root = new GameObject("Result").transform; root.SetParent(parent, false);
            var bone = CoverBuilder.Pbr("M_R_Bone", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 2012);
            var boneSide = CoverBuilder.Pbr("M_R_BoneSide", new Color(0.22f, 0.2f, 0.18f), 0f, 0.3f, 2010);
            var red = CoverBuilder.Unlit("M_R_Ghost", null, new Color(0.62f, 0.09f, 0.06f, 0.85f), 3000, cull: CullMode.Back);
            var txt = CoverBuilder.Pbr("M_R_Text", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 3050);
            var rb = root.gameObject.AddComponent<ResultBoard>();
            rb.distance = 0.5f; rb.below = 0.12f;   // 眼前略低，压在沙盘上方，不挡视线正中；11-07 拉近到伸手能点到

            // ── 墨底：两道宽墨笔刷叠成一块深色底，结算浮在沙盘 / 真实房间上时字才看得清（骨白字在浅色背景上会消失）
            var wash = CoverBuilder.Tex("brush_1");
            CoverBuilder.Quad(root, "R_Wash0", wash, new Color(0.04f, 0.035f, 0.03f, 0.88f), new Vector3(-0.01f, 0.07f, -0.06f), 0.58f, 2995, rotZ: 3f);
            CoverBuilder.Quad(root, "R_Wash1", wash, new Color(0.04f, 0.035f, 0.03f, 0.85f), new Vector3(0.01f, -0.075f, -0.062f), 0.6f, 2996, rotZ: 177f);
            // ── 大字 + 朱砂
            rb.splash = CoverBuilder.Quad(root, "R_Splash", CoverBuilder.Tex("ink_splash"), new Color(1f, 1f, 1f, 0.9f), new Vector3(0.1f, 0.03f, -0.03f), 0.34f, 3001, rotZ: 24f).GetComponent<Renderer>();
            var big = new GameObject("Big").transform; big.SetParent(root, false); big.localPosition = new Vector3(0.1f, 0.03f, 0.02f);
            var ghost = new GameObject("Ghost").transform; ghost.SetParent(root, false); ghost.localPosition = big.localPosition + new Vector3(-0.006f, -0.005f, -0.012f);
            var specs = new (string f, float size, bool byH)[] { ("word_sheng", 0.2f, true), ("res_victory", 0.2f, false), ("word_bai", 0.2f, true), ("res_defeat", 0.2f, false) };
            var bigs = new List<GameObject>(); var ghosts = new List<GameObject>();
            foreach (var (f, size, byH) in specs)
            {
                bigs.Add(CoverBuilder.Piece(big, Dir, f, size, Vector3.zero, bone, boneSide, byHeight: byH)?.gameObject);
                ghosts.Add(CoverBuilder.Piece(ghost, Dir, f, size, Vector3.zero, red, red, byHeight: byH)?.gameObject);
            }
            rb.bigs = bigs.ToArray(); rb.ghosts = ghosts.ToArray(); rb.bigChar = big; rb.bigGhost = ghost;

            // ── 三颗朱印星（印章贴图，挣到的亮、没挣到的暗）
            var seal = CoverBuilder.Tex("seal");
            rb.stars = Enumerable.Range(0, 3).Select(i =>
            {
                var q = CoverBuilder.Quad(root, "R_Star" + i, seal, Color.white, new Vector3(-0.045f - 0.048f * i, 0.11f, 0.02f), 1f, 3060, rotZ: (i - 1) * 6f);
                q.localScale = Vector3.one * rb.StarSize;
                q.GetComponent<Renderer>().sharedMaterial = CoverBuilder.Unlit("M_R_Star", seal, Color.white, 3060);
                return q.GetComponent<Renderer>();
            }).ToArray();

            // ── 人数：旗 + 数字（数字压在墨笔刷上）
            rb.ours = Row(root, "Ours", "banner_kingdom", 0.045f, txt);
            rb.theirs = Row(root, "Theirs", "banner_clan", -0.012f, txt);

            // ── 菜单：再战 / 回营
            var hi = CoverBuilder.Tex("brush_1"); var ink = CoverBuilder.Tex("brush_0");
            var menu = new (ResultBoard.Action a, string zh, string en, float x)[] { (ResultBoard.Action.Again, "word_zaizhan", "menu_again", 0.1f), (ResultBoard.Action.Camp, "word_huiying", "menu_camp", -0.1f) };
            var items = new List<ResultBoard.Item>();
            for (int i = 0; i < menu.Length; i++)
            {
                var m = menu[i];
                var it = new GameObject("Item_" + m.a).transform; it.SetParent(root, false);
                it.localPosition = new Vector3(m.x, -0.11f, 0.03f); it.localRotation = Quaternion.Euler(0, 0, i == 0 ? 2f : -2f);
                CoverBuilder.Quad(it, "R_Ink_" + m.a, ink, new Color(0.05f, 0.045f, 0.04f, 0.93f), new Vector3(-0.01f, InkY, -0.004f), 0.2f, 3020 + i);
                var h = CoverBuilder.Quad(it, "R_Hi_" + m.a, hi, new Color(0.93f, 0.9f, 0.83f, 0.97f), new Vector3(-0.008f, HiY, -0.002f), 0.21f, 3030 + i, rotZ: -1.5f);
                var hr = h.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Reveal", 0f); hr.SetPropertyBlock(mpb);
                var zh = CoverBuilder.Piece(it, Dir, m.zh, 0.042f, new Vector3(0, TextY, 0.004f), txt, txt);
                var en = CoverBuilder.Piece(it, Dir, m.en, 0.022f, new Vector3(0, TextY, 0.004f), txt, txt);
                items.Add(new ResultBoard.Item
                {
                    action = m.a, root = it, highlight = hr, zh = zh ? zh.gameObject : null, en = en ? en.gameObject : null,
                    hit = CoverBuilder.HitRect(it, new[] { zh, en }, null, TextY),
                    texts = new[] { zh, en }.Where(x => x).SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray(),
                });
            }
            rb.items = items.ToArray();
            root.gameObject.SetActive(false);
            return rb;
        }

        /// <summary>一行人数：左边一面小旗（队伍旗面图），右边墨笔刷上“a/b”，每位 0–9 和 / 各备一个字形，运行时只开一个。</summary>
        static ResultBoard.Number Row(Transform root, string name, string flag, float y, Material txt)
        {
            var row = new GameObject(name).transform; row.SetParent(root, false); row.localPosition = new Vector3(-0.03f, y, 0.02f);
            var ft = CoverBuilder.Tex(flag);
            CoverBuilder.Quad(row, "R_Flag_" + name, ft, new Color(1f, 1f, 1f), new Vector3(-0.002f, 0.002f, 0f), 0.024f, 3040, rotZ: -6f);
            CoverBuilder.Quad(row, "R_Ink_" + name, CoverBuilder.Tex("brush_2"), new Color(0.05f, 0.045f, 0.04f, 0.9f), new Vector3(-0.1f, -0.003f, -0.004f), 0.17f, 3021);
            var nums = new GameObject("Digits").transform; nums.SetParent(row, false); nums.localPosition = new Vector3(-0.04f, 0f, 0.004f);
            var files = Enumerable.Range(0, 10).Select(d => "d" + d).Append("dslash").ToArray();
            var widths = new float[files.Length];
            var slots = new ResultBoard.Slot[7];   // 最多 “999/999”
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = new GameObject("Slot" + s).transform; slot.SetParent(nums, false);
                var gs = new GameObject[files.Length];
                for (int k = 0; k < files.Length; k++)
                {
                    var pc = CoverBuilder.Piece(slot, Dir, files[k], 0.028f, Vector3.zero, txt, txt);
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
            CoverBuilder.Quad(panel, "P_Wash", CoverBuilder.Tex("brush_1"), new Color(0.04f, 0.035f, 0.03f, 0.9f), new Vector3(0f, 0.035f, -0.03f), 0.4f, 3005, rotZ: 2f);
            CoverBuilder.Quad(panel, "P_Wash2", CoverBuilder.Tex("brush_1"), new Color(0.04f, 0.035f, 0.03f, 0.88f), new Vector3(0f, -0.03f, -0.032f), 0.38f, 3004, rotZ: 178f);
            var txt = CoverBuilder.Pbr("M_P_Text", new Color(0.9f, 0.86f, 0.78f), 0f, 0.35f, 3050);
            var hi = CoverBuilder.Tex("brush_1"); var ink = CoverBuilder.Tex("brush_0");
            var menu = new (PauseMenu.Action a, string zh, string en, float y)[] { (PauseMenu.Action.Resume, "word_jixu", "menu_resume", 0.035f), (PauseMenu.Action.Camp, "word_huiying", "menu_camp", -0.045f) };
            var items = new List<PauseMenu.Item>();
            for (int i = 0; i < menu.Length; i++)
            {
                var m = menu[i];
                var it = new GameObject("Item_" + m.a).transform; it.SetParent(panel, false);
                it.localPosition = new Vector3(0f, m.y, 0.01f);
                CoverBuilder.Quad(it, "P_Ink_" + m.a, ink, new Color(0.05f, 0.045f, 0.04f, 0.93f), new Vector3(-0.01f, InkY, -0.004f), 0.2f, 3020 + i);
                var h = CoverBuilder.Quad(it, "P_Hi_" + m.a, hi, new Color(0.93f, 0.9f, 0.83f, 0.97f), new Vector3(-0.008f, HiY, -0.002f), 0.21f, 3030 + i, rotZ: -1.5f);
                var hr = h.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Reveal", 0f); hr.SetPropertyBlock(mpb);
                var zh = CoverBuilder.Piece(it, Dir, m.zh, 0.042f, new Vector3(0, TextY, 0.004f), txt, txt);
                var en = CoverBuilder.Piece(it, Dir, m.en, 0.022f, new Vector3(0, TextY, 0.004f), txt, txt);
                items.Add(new PauseMenu.Item
                {
                    action = m.a, root = it, highlight = hr, zh = zh ? zh.gameObject : null, en = en ? en.gameObject : null,
                    hit = CoverBuilder.HitRect(it, new[] { zh, en }, null, TextY),
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
