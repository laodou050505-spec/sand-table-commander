using System.Collections.Generic;
using UnityEngine;
using WarTable.Command;

namespace WarTable.Game
{
    /// <summary>
    /// 战场提示（11-03 用户：场景里不要大段文字，要玩中学）：全是贴着地形的光环和线，不写字。
    /// - 己方每支部队脚下一圈骨白淡光环；视线落上去变亮（= 可以捏它）；选中变金色、加粗、轻轻呼吸。
    /// - 选中己方部队后，敌军脚下浮出暗红淡光环（= 可以冲它）；看着某支敌军时它亮起。
    /// - 划线时沙盘上一道金线（长度 = 队宽）和一个朝向箭头。
    /// - 下令的一瞬那支部队的光环弹一圈：冲锋 / 冲敌朱红外扩、坚守骨白内收、撤退青灰外扩、移动 / 列阵金色小弹。
    /// - 布阵时我方帅旗脚下一圈金光呼吸（= 看着帅旗捏一下开战），看着时更亮。
    /// 顶点逐个贴到地形上（BattleView.Ground）再抬 0.35 码，丘陵上不穿地、不悬空；
    /// 材质 WarTable/CoverUnlit（半透明、深度测试，棋子挡得住光环）。
    /// </summary>
    public class FieldUI : MonoBehaviour
    {
        public Material mat;
        public BattleView view;
        public Transform board;

        class Part { public GameObject go; public Mesh mesh; public MeshRenderer r; public bool used; }
        readonly Dictionary<string, Part> parts = new();
        readonly Dictionary<string, (float t, string kind)> pulses = new();
        readonly List<Vector3> vs = new(); readonly List<Vector2> us = new(); readonly List<int> ts = new();
        Texture2D soft;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), MapId = Shader.PropertyToID("_BaseMap");
        static readonly Color Bone = new(0.93f, 0.89f, 0.8f), Gold = new(1f, 0.8f, 0.36f), Blood = new(0.85f, 0.16f, 0.1f), Slate = new(0.55f, 0.68f, 0.85f);
        const int Seg = 48, LineSeg = 16;
        const float Lift = 0.35f;   // 码

        void Init()
        {
            if (soft) return;
            mpb = new MaterialPropertyBlock();
            // 横向柔边：uv.x 0→1 是从内沿到外沿（线是从一侧到另一侧），中间最实
            soft = new Texture2D(64, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FieldSoft" };
            for (int x = 0; x < 64; x++)
            {
                float a = Mathf.Pow(Mathf.Sin(Mathf.PI * (x + 0.5f) / 64f), 1.2f);
                for (int y = 0; y < 2; y++) soft.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            soft.Apply();
        }

        /// <summary>下令时调用（BattleCommand.Ordered）：这些部队的光环弹一圈。</summary>
        public void OnOrder(string cmd, IReadOnlyList<string> groups)
        {
            foreach (var g in groups) pulses[g] = (Time.time, cmd);
        }

        /// <summary>引导要强调的目标（Tutorial.Tick 给）：某支部队 / 帅旗光环大幅呼吸，可选一条示范虚线。</summary>
        public struct Focus
        {
            public int team; public string group; public string alsoOwn; public bool standard;
            public bool hintLine; public double x0, z0, x1, z1;
            /// <summary>教学的落点金圈（11-09：“拿到金圈里松手”），沙盘坐标（码）。</summary>
            public bool ring; public double rx, rz;
        }
        public Focus focus;
        /// <summary>捏住帅旗的进度 0–1（WarGame 给）：满了开战。</summary>
        public float standardHold;

        float denyT = -9f;
        /// <summary>这一下不行（没选部队就点敌军、布阵时冲敌）：被看着的那支部队光环闪一下暗红。</summary>
        public void OnDeny() => denyT = Time.time;

        // ------------------------------------------------------------ 摆沙盘：按掌进度环（世界坐标，不挂在沙盘下，沙盘这时还藏着）

        GameObject palmGo; Mesh palmMesh; MeshRenderer palmR;
        /// <summary>摆沙盘阶段：手掌按在桌上时，手掌周围一圈金环随进度画满（1 秒）；没按时什么都不画。</summary>
        public void Palm(Vector3? center, float progress)
        {
            Init();
            if (!palmGo)
            {
                palmGo = new GameObject("UI_Palm");
                palmMesh = new Mesh { name = "UI_Palm" }; palmMesh.MarkDynamic();
                palmGo.AddComponent<MeshFilter>().sharedMesh = palmMesh;
                palmR = palmGo.AddComponent<MeshRenderer>(); palmR.sharedMaterial = mat; palmR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            bool on = center.HasValue && progress > 0.02f;
            palmGo.SetActive(on);
            if (!on) return;
            vs.Clear(); us.Clear(); ts.Clear();
            const float r = 0.085f, w = 0.012f;
            int seg = Mathf.Max(2, Mathf.CeilToInt(Seg * progress));
            for (int i = 0; i <= seg; i++)
            {
                float ang = Mathf.PI * 0.5f - progress * Mathf.PI * 2f * i / seg, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                for (int j = 0; j < 2; j++)
                {
                    float rr = r + (j == 0 ? -w : w) * 0.5f;
                    vs.Add(center.Value + new Vector3(cs * rr, 0.004f, sn * rr)); us.Add(new Vector2(j, 0.5f));
                }
            }
            Strip(seg);
            palmMesh.Clear(); palmMesh.SetVertices(vs); palmMesh.SetUVs(0, us); palmMesh.SetTriangles(ts, 0); palmMesh.RecalculateBounds();
            mpb.SetTexture(MapId, soft); mpb.SetColor(ColorId, new Color(Gold.r, Gold.g, Gold.b, 0.5f + 0.45f * progress));
            palmR.SetPropertyBlock(mpb);
        }

        /// <summary>每帧（模拟步进之后）。cmd = null 时全部藏起来（封面、结算）。</summary>
        public void Tick(BattleCommand cmd, bool deploying, Transform standard)
        {
            if (cmd == null || !board || !view) { HideAll(); return; }
            Init();
            foreach (var p in parts.Values) p.used = false;
            bool anySel = cmd.Selected.Count > 0;
            float t = Time.time;
            foreach (var f in cmd.Flags())
            {
                var (rx, rz, rr) = cmd.Ring(f.group, f.team);
                float ux = -(float)rx, uz = (float)rz, r = (float)rr;
                string key = f.team + f.group;
                // 引导强调：这支部队的光环大幅呼吸（比悬停更显眼），外面再套一圈慢慢外扩的涟漪
                bool focused = focus.group == f.group && focus.team == f.team || f.team == BattleCommand.Me && focus.alsoOwn == f.group;
                if (focused)
                {
                    float k = (t * 0.7f) % 1f;
                    Ring("f" + key, ux, uz, r * (1f + 0.6f * k), 0.5f, f.team == BattleCommand.Me ? Gold : Blood, 0.8f * (1f - k));
                }
                if (f.team == BattleCommand.Me)
                {
                    var it = cmd.Intent(f.group);
                    if (it.HasValue) Arrow("a" + key, ux, uz, -(float)it.Value.x, (float)it.Value.z, r, it.Value.kind == "attack" ? Blood : Gold, it.Value.kind == "aim" ? (f.selected ? 0.8f : 0.5f) : f.selected ? 0.95f : 0.72f, t);
                    float a = f.selected ? 0.8f + 0.12f * Mathf.Sin(t * 4f) : f.hover ? 0.6f : focused ? 0.45f + 0.3f * Mathf.Sin(t * 4f) : 0.22f;
                    Ring(key, ux, uz, r, f.selected ? 0.9f : 0.6f, f.selected ? Gold : Bone, a);
                    if (pulses.TryGetValue(f.group, out var pl))
                    {
                        float k = (t - pl.t) / 0.6f;
                        if (k >= 1f) pulses.Remove(f.group);
                        else
                        {
                            var (pc, s0, s1) = pl.kind switch
                            {
                                "charge" or "attack" => (Blood, 1f, 2f),
                                "hold" => (Bone, 1.7f, 1f),
                                "retreat" => (Slate, 1f, 1.8f),
                                _ => (Gold, 1f, 1.35f),
                            };
                            float e = 1f - (1f - k) * (1f - k);
                            Ring("p" + key, ux, uz, r * Mathf.Lerp(s0, s1, e), 0.3f + 1.2f * (1f - k), pc, 0.9f * (1f - k));
                        }
                    }
                }
                else if (f.hover || anySel || focused)
                {
                    float flash = f.hover ? Mathf.Clamp01(1f - (t - denyT) / 0.35f) : 0f;   // 拒绝：闷一下暗红闪光
                    Ring(key, ux, uz, r * (1f + 0.15f * flash), f.hover ? 0.9f + 0.6f * flash : 0.5f, Blood, f.hover ? Mathf.Max(anySel ? 0.9f : 0.45f, flash) : 0.2f);
                }
            }
            var line = cmd.PreviewLine;
            // 11-07：捏住一支部队拖动——从它脚下拉一支箭头到手的落点；落在敌军上箭头变朱红、那支敌军光环亮起，否则终点一个金圈
            var drag = cmd.Pinch.DragPreview;
            if (drag.HasValue)
            {
                var gp = drag.Value.unit.group; var dg = gp == Grp.Arch ? WarTable.Sim.Groups.Arch : gp == Grp.Cav ? WarTable.Sim.Groups.Cav : gp == Grp.Ha ? WarTable.Sim.Groups.Ha : WarTable.Sim.Groups.Inf;
                var (rx0, rz0, rr0) = cmd.Ring(dg);
                var end = drag.Value.end; var foe = cmd.DragFoe;
                if (foe != null)
                {
                    var (fx, fz, fr) = cmd.Ring(foe, BattleCommand.Foe);
                    Arrow("drag", -(float)rx0, (float)rz0, -(float)fx, (float)fz, (float)rr0, Blood, 0.95f, t);
                    Ring("dragFoe", -(float)fx, (float)fz, (float)fr * (1.05f + 0.06f * Mathf.Sin(t * 8f)), 1.1f, Blood, 0.95f);
                }
                else
                {
                    Arrow("drag", -(float)rx0, (float)rz0, -(float)end.x, (float)end.z, (float)rr0, Gold, 0.95f, t);
                    Ring("dragEnd", -(float)end.x, (float)end.z, 3.2f, 0.8f, Gold, 0.9f);
                }
            }
            if (line.HasValue) Line(line.Value);
            else if (focus.hintLine) Dashes(focus.x0, focus.z0, focus.x1, focus.z1, t);
            if (focus.ring)
            {
                float k = (t * 0.8f) % 1f;
                Ring("tutRing", -(float)focus.rx, (float)focus.rz, 6f, 1.1f, Gold, 0.95f);
                Ring("tutRing2", -(float)focus.rx, (float)focus.rz, 6f * (1f + 0.5f * k), 0.6f, Gold, 0.7f * (1f - k));
            }
            if (deploying && standard)
            {
                bool look = cmd.HoverTarget is { kind: TargetKind.Standard };
                var lp = board.InverseTransformPoint(standard.position) / BoardMap.Yard;
                float br = 0.5f + 0.5f * Mathf.Sin(t * 2.6f);
                Ring("std", lp.x, lp.z, look ? 4.2f : 3.6f + 0.5f * br, 0.8f, Gold, look ? 0.9f : 0.3f + 0.3f * br);
                // 11-09 防误触：帅旗要捏住停 0.7 秒才开战——捏住时帅旗脚下一道金弧随进度画满
                if (standardHold > 0f)
                {
                    Ring("stdHoldBg", lp.x, lp.z, 6f, 1.4f, Bone, 0.35f);
                    Ring("stdHold", lp.x, lp.z, 6f, 1.6f, Gold, 1f, standardHold);
                }
                if (focus.standard)
                {
                    float k = (t * 0.7f) % 1f;
                    Ring("fstd", lp.x, lp.z, 4f * (1f + 0.7f * k), 0.6f, Gold, 0.85f * (1f - k));
                }
            }
            foreach (var p in parts.Values) if (p.go.activeSelf != p.used) p.go.SetActive(p.used);
        }

        public void HideAll() { foreach (var p in parts.Values) if (p.go.activeSelf) p.go.SetActive(false); }

        public void Clear()
        {
            foreach (var p in parts.Values) { if (p.go) Destroy(p.go); if (p.mesh) Destroy(p.mesh); }
            parts.Clear(); pulses.Clear();
        }

        // ------------------------------------------------------------ 网格

        Vector3 OnGround(float ux, float uz)
        {
            var (pos, up) = view.Ground(ux, uz);
            return pos + up * (Lift * BoardMap.Yard);
        }

        void Ring(string key, float ux, float uz, float r, float w, Color c, float a, float frac = 1f)
        {
            vs.Clear(); us.Clear();
            for (int i = 0; i <= Seg; i++)
            {
                float ang = Mathf.PI * 0.5f - i * Mathf.PI * 2f * Mathf.Clamp01(frac) / Seg, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                for (int j = 0; j < 2; j++)
                {
                    float rr = Mathf.Max(0.1f, r + (j == 0 ? -w : w) * 0.5f);
                    vs.Add(OnGround(ux + cs * rr, uz + sn * rr)); us.Add(new Vector2(j, 0.5f));
                }
            }
            Strip(Seg);
            Commit(key, c, a);
        }

        /// <summary>划线预览：沿线一条贴地金带（宽 0.8 码）+ 线中点前方一个朝向箭头。</summary>
        /// <summary>示范虚线（模拟坐标两端）：一节一节的金色短划，从起点往终点依次亮起，像笔在走——示意“这样拖一条线”。</summary>
        void Dashes(double x0, double z0, double x1, double z1, float t)
        {
            const int N = 7;
            var a = new Vector2(-(float)x0, (float)z0); var b = new Vector2(-(float)x1, (float)z1);
            var dir = (b - a); float len = dir.magnitude; if (len < 1e-3f) return; dir /= len;
            var side = new Vector2(-dir.y, dir.x);
            float run = (t * 0.8f) % 1.4f;   // 0–1 画出来，再停 0.4
            for (int i = 0; i < N; i++)
            {
                float u0 = (float)i / N, u1 = u0 + 0.6f / N;
                float vis = Mathf.Clamp01((run - u0) * 6f);
                if (vis <= 0f) continue;
                vs.Clear(); us.Clear();
                for (int j = 0; j <= 2; j++)
                {
                    var m = a + dir * (len * Mathf.Lerp(u0, u1, j / 2f));
                    for (int k = 0; k < 2; k++) { var p = m + side * (k == 0 ? -0.8f : 0.8f); vs.Add(OnGround(p.x, p.y)); us.Add(new Vector2(k, 0.5f)); }
                }
                Strip(2);
                Commit("dash" + i, Gold, 0.95f * vis);
            }
        }

        void Line(LineOrder o)
        {
            float yaw = (float)o.yaw, len = Mathf.Max(1f, (float)o.length);
            var c = new Vector2(-(float)o.x, (float)o.z);
            // 模拟坐标里右 = (cos, −sin)、前 = (−sin, −cos)；Unity 本地 x = −模拟 x
            var right = new Vector2(-Mathf.Cos(yaw), -Mathf.Sin(yaw));
            var fwd = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
            vs.Clear(); us.Clear();
            for (int i = 0; i <= LineSeg; i++)
            {
                var m = c + right * (len * ((float)i / LineSeg - 0.5f));
                for (int j = 0; j < 2; j++)
                {
                    var p = m + fwd * (j == 0 ? -0.4f : 0.4f);
                    vs.Add(OnGround(p.x, p.y)); us.Add(new Vector2(j, 0.5f));
                }
            }
            Strip(LineSeg);
            Commit("line", Gold, 0.95f);
            // 箭头：实心三角
            vs.Clear(); us.Clear(); ts.Clear();
            Vector2 tip = c + fwd * 3.2f, b0 = c + fwd * 1.2f + right * 1.3f, b1 = c + fwd * 1.2f - right * 1.3f;
            foreach (var p in new[] { tip, b0, b1 }) { vs.Add(OnGround(p.x, p.y)); us.Add(new Vector2(0.5f, 0.5f)); }
            ts.AddRange(new[] { 0, 1, 2 });
            Commit("arrow", Gold, 0.95f);
        }

        /// <summary>贴地箭头（Unity 本地码）：从部队光环边起，到目标前 2 码止；箭杆宽 1.1 码，杆上一节节亮光往前流动（看得出方向）。</summary>
        void Arrow(string key, float x0, float z0, float x1, float z1, float startGap, Color c, float a, float t)
        {
            var p0 = new Vector2(x0, z0); var p1 = new Vector2(x1, z1); var d = p1 - p0; float len = d.magnitude;
            if (len < startGap + 6f) return;
            d /= len; var side = new Vector2(-d.y, d.x);
            var s0 = p0 + d * startGap; var tip = p1 - d * 2f; var neck = tip - d * 3.2f;
            float shaft = (neck - s0).magnitude; int n = Mathf.Max(2, Mathf.CeilToInt(shaft / 2.5f));
            vs.Clear(); us.Clear();
            for (int i = 0; i <= n; i++)
            {
                var m = Vector2.Lerp(s0, neck, (float)i / n);
                for (int j = 0; j < 2; j++) { var q = m + side * (j == 0 ? -0.9f : 0.9f); vs.Add(OnGround(q.x, q.y)); us.Add(new Vector2(j, 0.5f)); }
            }
            Strip(n);
            int bi = vs.Count;
            foreach (var q in new[] { tip, neck + side * 2.8f, neck - side * 2.8f }) { vs.Add(OnGround(q.x, q.y)); us.Add(new Vector2(0.5f, 0.5f)); }
            ts.AddRange(new[] { bi, bi + 1, bi + 2 });
            Commit(key, c, a * (0.85f + 0.15f * Mathf.Sin(t * 5f)));
        }

        void Strip(int seg)
        {
            ts.Clear();
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                ts.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
        }

        void Commit(string key, Color c, float a)
        {
            if (!parts.TryGetValue(key, out var p))
            {
                var go = new GameObject("UI_" + key);
                go.transform.SetParent(board, false);
                var mesh = new Mesh { name = "UI_" + key };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                p = parts[key] = new Part { go = go, mesh = mesh, r = r };
            }
            if (p.mesh.vertexCount != vs.Count) p.mesh.Clear();
            p.mesh.SetVertices(vs); p.mesh.SetUVs(0, us); p.mesh.SetTriangles(ts, 0);
            p.mesh.RecalculateBounds();
            mpb.SetTexture(MapId, soft);
            mpb.SetColor(ColorId, new Color(c.r, c.g, c.b, a));
            p.r.SetPropertyBlock(mpb);
            p.used = true;
        }
    }
}
