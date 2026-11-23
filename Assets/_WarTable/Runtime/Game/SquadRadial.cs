using UnityEngine;
using WarTable.Campaign;
using WarTable.Command;
using WarTable;

namespace WarTable.Game
{
    /// <summary>
    /// 小队令的画面（11-20，设计稿第 5 节）：捏住自己的旗牌不动时，旗牌脚下先画一圈金色进度弧；
    /// 满 0.4 秒后三枚小令（藏青圆章 + 指挥台同款的冲锋 / 坚守 / 撤退图标）从旗牌里弹出来，排成一把扇子；
    /// 手挪向哪一枚，哪一枚就放大变亮，其余两枚变暗；松手 = 只给这一队下这道令，三枚收回旗牌。
    /// 小令立在旗牌旁边、正对玩家的眼睛（不平铺在沙盘上，坐着也看得清）。
    /// </summary>
    public class SquadRadial : MonoBehaviour
    {
        public WarGame game;
        public Texture2D[] icons = new Texture2D[3];
        Transform[] tokens;
        MeshRenderer[] discs, glyphs;
        MeshRenderer arc;
        float show, holdShow; float[] hot = new float[3];
        string lastGroup;
        Vector3 anchor;
        static readonly Color Dim = new(0.5f, 0.5f, 0.54f, 1f);   // 没被指着的两枚压暗（不透明：后面的牌子不能透出来）

        void Build()
        {
            if (tokens != null || !CampaignArt.Ready) return;
            tokens = new Transform[3]; discs = new MeshRenderer[3]; glyphs = new MeshRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                tokens[i] = new GameObject("SquadOrder" + i).transform; tokens[i].SetParent(transform, false);
                discs[i] = CampaignArt.Quad(tokens[i], "badge_blank", 1f, Color.white, 3060);
                var g = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(g.GetComponent<Collider>());
                g.name = "Icon"; g.transform.SetParent(tokens[i], false); g.transform.localPosition = new Vector3(0, 0, -0.01f); g.transform.localScale = Vector3.one * 0.86f;
                glyphs[i] = g.GetComponent<MeshRenderer>(); glyphs[i].sharedMaterial = CampaignArt.Mat(icons[i], 3061);
                glyphs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            arc = CampaignArt.Quad(transform, "ring_0", 1f, new Color(1f, 0.82f, 0.45f), 3059);
            // 小令永远画在最上层（旗牌、状态牌都在 3050 左右，会把它挡住）
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true)) { var m = new Material(r.sharedMaterial) { renderQueue = r.sharedMaterial.renderQueue + 100 }; m.SetFloat("_ZTest", 8f); r.sharedMaterial = m; }
        }

        void LateUpdate()
        {
            Build();
            if (tokens == null) return;
            var cmd = game ? game.Command : null;
            var eye = game && game.mr && game.mr.Active ? game.mr.cam.transform : game && game.previewCamera ? game.previewCamera.transform : null;
            var radial = cmd?.Pinch.Radial;
            double hold = cmd != null ? cmd.Pinch.HoldProgress : 0;
            var held = cmd?.Pinch.Holding;
            string group = radial.HasValue ? GroupOf(radial.Value.unit) : held.HasValue ? GroupOf(held.Value.unit) : null;
            if (group != null) lastGroup = group;
            float dt = Time.deltaTime;
            show = Mathf.MoveTowards(show, radial.HasValue ? 1f : 0f, dt / (radial.HasValue ? 0.16f : 0.12f));
            holdShow = Mathf.MoveTowards(holdShow, hold > 0.15 && !radial.HasValue ? 1f : 0f, dt / 0.1f);
            // 小令的“正前方”：从玩家眼睛看向旗牌、压平到沙盘上（模拟坐标），让冲锋那枚永远在背离玩家的一侧
            if (cmd != null && eye && game.board && lastGroup != null && game.signs && game.signs.OwnTile(lastGroup, out var tp, out var th))
            {
                anchor = tp + Vector3.up * th * 0.2f;
                var f = game.board.InverseTransformDirection(tp - eye.position); f.y = 0;
                if (f.sqrMagnitude > 1e-6f) { f.Normalize(); cmd.Pinch.RadialFwd = new P2(-f.x, f.z); }
            }
            bool any = show > 0.001f || holdShow > 0.001f;
            if (!any || !eye) { if (gameObject.activeSelf && transform.childCount > 0) foreach (Transform c in transform) c.gameObject.SetActive(false); return; }
            float k = game.board.lossyScale.x / BoardMap.BoardScale;   // 沙盘放大（真人比例）时一起放大
            var face = Quaternion.LookRotation(anchor - eye.position, Vector3.up);
            // 进度弧：旗牌后面一个手画圈，跟着捏住的时间长大、变亮
            arc.gameObject.SetActive(holdShow > 0.001f);
            if (holdShow > 0.001f)
            {
                arc.transform.position = anchor; arc.transform.rotation = face;
                arc.transform.localScale = Vector3.one * 0.05f * k * (0.6f + 0.4f * (float)hold);
                CampaignArt.Tint(arc, new Color(1f, 0.82f, 0.45f, 0.85f * holdShow), (float)hold);
            }
            int pick = radial.HasValue ? radial.Value.pick : -1;
            for (int i = 0; i < 3; i++)
            {
                var t = tokens[i];
                t.gameObject.SetActive(show > 0.001f);
                if (show <= 0.001f) continue;
                hot[i] = Mathf.MoveTowards(hot[i], i == pick ? 1f : 0f, dt / 0.1f);
                // 扇形摆在旗牌上方：冲锋居中最高、坚守在左、撤退在右（和模拟层的方向一致：手往哪挪就是哪枚）
                float ang = (float)PinchCommand.RadialAngles[i];
                var local = new Vector3(Mathf.Sin(ang) * 1.05f, Mathf.Cos(ang) * 0.75f + 0.55f, 0f) * 0.042f * k;
                float e = OutBack(show);
                t.position = anchor + face * (local * e) + face * Vector3.back * (0.004f * hot[i]);
                t.rotation = face;
                t.localScale = Vector3.one * 0.03f * k * Mathf.Max(0.001f, e) * (1f + 0.3f * hot[i]);
                var col = pick < 0 ? Color.white : Color.Lerp(Dim, Color.white, hot[i]);
                CampaignArt.Tint(discs[i], col); CampaignArt.Tint(glyphs[i], col);
            }
        }

        static string GroupOf(PinchTarget t) => t.group == Command.Grp.Arch ? Sim.Groups.Arch : t.group == Command.Grp.Cav ? Sim.Groups.Cav : t.group == Command.Grp.Ha ? Sim.Groups.Ha : Sim.Groups.Inf;
        static float OutBack(float x) { const float c = 1.8f; x -= 1f; return 1f + (c + 1f) * x * x * x + c * x * x; }
    }
}
