using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.XR;

namespace WarTable.Campaign
{
    /// <summary>
    /// 作战地图（11-19 用户：分支航线图要做得精致，像军事地图）：一张平铺在沙盘木框里的纸质作战图（Tools/make_campaign_map.py），
    /// 上面用油性笔叠：敌军菱形军标（精英双框、Boss 带编制符号）、补给 / 军械方框、各节点之间的虚线航线；
    /// 走过的路是蓝色实线、打过的敌标被划掉；下一步能去的节点画红圈、连过去的航线变红虚线。
    /// 近边是印好的“兵力编成表”：在营 / 伤员 / 出征三行手写数字，出征行每格两侧一对加减钮（手指直接按）。
    /// 选下一站：手指着（头 → 指尖的连线）或眼睛看着某个红圈，捏一下；手够得着也可以直接用指尖点。
    /// 坐标：根节点挂在沙盘下、绕 Y 转 180°，本地 +X = 玩家右手、+Z = 远处、+Y = 上；单位 = 米（按沙盘默认大小）。
    /// </summary>
    public class CampaignMap : MonoBehaviour
    {
        // ── 地图版面（像素，和 make_campaign_map.py 一致）
        const float PW = 1.36f, PH = 1.36f / 1.2f, IW = 3072f, IH = 2560f;
        const float FX0 = 110, FY0 = 300, FX1 = 2962, FY1 = 1990;
        static readonly float[] OobCols = { 448.3f, 665f, 881.7f, 1098.3f, 1315f, 1531.7f, 1748.3f, 1965f, 2181.7f };
        const float OobCW = 216.7f, RowFit = 2254, RowWound = 2334, RowDeploy = 2437;   // 各行的中线（数字在格子里上下居中）
        const float SideL = 2352, SideR = 2930;   // 右下小纸的版心左右线（数值右对齐到 SideR）
        const float SideX0 = 2320, SideX1 = 2962;
        static readonly float[] SideRows = { 2030, 2160, 2290, 2500 };
        public static readonly string[] Troops = CampaignRules.PlayerTroops;

        // 11-20 v2 西式奇幻配色（和结算板 / 封面一套）：朱红（敌）、藏青（我）、棕褐墨（图上的线）、金、象牙白
        public static readonly Color Red = new(0.61f, 0.23f, 0.15f), Blue = new(0.13f, 0.22f, 0.42f), Pencil = new(0.31f, 0.2f, 0.13f, 0.85f),
                                     Ink = new(0.09f, 0.11f, 0.17f), Gray = new(0.31f, 0.2f, 0.13f, 0.5f),
                                     Gold = new(0.89f, 0.74f, 0.45f), Dim = new(0.45f, 0.45f, 0.5f, 0.55f), Ivory = new(0.93f, 0.9f, 0.82f), GoldDim = new(0.59f, 0.47f, 0.28f, 0.8f), Wound = new(0.93f, 0.52f, 0.42f);
        const float Lift = 0.0015f;

        public static Vector3 P(float px, float py, float y = Lift) => new((px / IW - 0.5f) * PW, y, (0.5f - py / IH) * PH);
        /// <summary>
        /// 节点在图上的位置。MapGen 的 y 是推进方向（0 → 1），x 是横向分布；第一版按“由近往远”摆，9 层挤在 75 cm 纵深里、层距 8 cm，
        /// 航线全被军标盖住 → 改成从玩家左手往右手推进（层距约 14 cm，所有节点离玩家差不多远，手指着也一样好选）。x 小 = 靠远处。
        /// </summary>
        public static Vector3 NodePos(MapNode n) => P(FX0 + (0.13f + 0.82f * Mathf.Clamp01((n.y - 0.07f) / 0.86f)) * (FX1 - FX0), FY0 + (0.1f + 0.8f * n.x) * (FY1 - FY0));
        static Vector3 StartPos => P(FX0 + 0.03f * (FX1 - FX0), FY0 + 0.5f * (FY1 - FY0));
        static float SymH(MapNode n) => n.kind == NodeKind.Boss ? 0.08f : 0.058f;
        const float PX = PW / IW;   // 一个像素多少米

        public RunState run;
        public List<TroopCount> muster = new();
        public MRInput mr;
        public Camera editorCam;
        public Game.GameAudio sound;
        /// <summary>选了某个节点（动画走完后才发）。</summary>
        public event System.Action<MapNode> OnPick;
        public bool Live = true;

        MeshRenderer paper;
        Transform nodesRoot, routesRoot, oobRoot, marker;
        class NodeView { public MapNode n; public Transform root; public MeshRenderer sym, ring, mark; public float hover, pulse; }
        readonly List<NodeView> nodes = new();
        class Edge { public int a, b; public MeshRenderer r; public MeshFilter f; public Transform arrow; }
        readonly List<Edge> edges = new();
        class Cell { public string id; public Transform fit, wound, dep; public PokeButton minus, plus; public MeshRenderer mBtn, pBtn; public string lastKey; }
        readonly List<Cell> cells = new();
        Transform capNum, crysNum, relicRoot; string capKey, crysKey, relicKey;
        readonly List<Ray> rays = new();
        int hover = -1; float moveT = -1f; MapNode moving; Vector3 moveFrom; MeshRenderer moveLine; MeshFilter moveLineF;
        float denyT = -9f;

        public void Build(RunState r, List<TroopCount> m)
        {
            run = r; muster = m;
            foreach (Transform c in transform) Destroy(c.gameObject);
            nodes.Clear(); edges.Clear(); cells.Clear(); capKey = crysKey = relicKey = null; hover = -1; moveT = -1f;
            if (!CampaignArt.Ready) return;
            // 纸
            var tex = CampaignArt.Tex(Loc.Current == Loc.Lang.Zh ? "map_zh" : "map_en");
            var pg = GameObject.CreatePrimitive(PrimitiveType.Quad); pg.name = "Paper"; Destroy(pg.GetComponent<Collider>());
            pg.transform.SetParent(transform, false); pg.transform.localPosition = Vector3.zero;
            pg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); pg.transform.localScale = new Vector3(PW, PH, 1f);
            paper = pg.GetComponent<MeshRenderer>(); paper.sharedMaterial = CampaignArt.Mat(tex, CampaignArt.QPaper);
            paper.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            routesRoot = new GameObject("Routes").transform; routesRoot.SetParent(transform, false);
            nodesRoot = new GameObject("Nodes").transform; nodesRoot.SetParent(transform, false);
            oobRoot = new GameObject("OrderOfBattle").transform; oobRoot.SetParent(transform, false);

            foreach (var n in run.nodes)
            {
                var v = new NodeView { n = n, root = new GameObject("Node" + n.id).transform };
                v.root.SetParent(nodesRoot, false); v.root.localPosition = NodePos(n);
                string key = "node_" + n.kind.ToString().ToLowerInvariant();
                float h = SymH(n);
                v.sym = CampaignArt.Flat(v.root, key, h, Color.white, Vector3.zero);   // 纹章自带颜色
                v.ring = CampaignArt.Flat(v.root, "ring_" + (n.id % 3), h * 1.35f, Gold, new Vector3(0, 0.0005f, 0), n.id * 47 % 360, CampaignArt.QTop);
                nodes.Add(v);
                foreach (var to in n.next)
                {
                    var e = new Edge { a = n.id, b = to };
                    var go = new GameObject($"Route{n.id}-{to}"); go.transform.SetParent(routesRoot, false);
                    e.f = go.AddComponent<MeshFilter>(); e.r = go.AddComponent<MeshRenderer>(); e.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    e.arrow = CampaignArt.Flat(routesRoot, "arrow", 0.013f, Red, Vector3.zero, 0, CampaignArt.QRoute + 1).transform;
                    edges.Add(e);
                }
            }
            // 出发点 → 第一层（还没出发时就是第一步能走的几条）
            foreach (var n in run.nodes.Where(n => n.layer == 0))
            {
                var e = new Edge { a = -1, b = n.id };
                var go = new GameObject($"Route-start-{n.id}"); go.transform.SetParent(routesRoot, false);
                e.f = go.AddComponent<MeshFilter>(); e.r = go.AddComponent<MeshRenderer>(); e.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                e.arrow = CampaignArt.Flat(routesRoot, "arrow", 0.013f, Red, Vector3.zero, 0, CampaignArt.QRoute + 1).transform;
                edges.Add(e);
            }
            // 行军中那一段（蓝实线，逐帧长出来）
            var ml = new GameObject("MoveLine"); ml.transform.SetParent(routesRoot, false);
            moveLineF = ml.AddComponent<MeshFilter>(); moveLine = ml.AddComponent<MeshRenderer>();
            moveLine.sharedMaterial = CampaignArt.Mat(CampaignArt.Tex("route_solid"), CampaignArt.QRoute + 2, true); CampaignArt.Tint(moveLine, Blue, 50f);
            // 我军位置：一个蓝色友军框 + 指挥旗（手画），立在当前节点旁
            marker = new GameObject("Army").transform; marker.SetParent(transform, false);
            var mk = CampaignArt.Flat(marker, "node_army", 0.06f, Color.white, new Vector3(0, 0.001f, 0), 0, CampaignArt.QTop);
            mk.transform.localPosition = new Vector3(0.0f, 0.002f, -0.006f);
            BuildOob();
            Refresh();
        }

        void BuildOob()
        {
            for (int i = 0; i < Troops.Length; i++)
            {
                var c = new Cell { id = Troops[i] };
                float x = OobCols[i];
                c.minus = new PokeButton { radius = 0.016f }; c.plus = new PokeButton { radius = 0.016f };
                var bm = new GameObject("Minus_" + c.id).transform; bm.SetParent(oobRoot, false); bm.localPosition = P(x - OobCW * 0.27f, RowDeploy, 0.003f); c.minus.t = bm;
                var bp = new GameObject("Plus_" + c.id).transform; bp.SetParent(oobRoot, false); bp.localPosition = P(x + OobCW * 0.27f, RowDeploy, 0.003f); c.plus.t = bp;
                c.mBtn = CampaignArt.Flat(bm, "minus", 0.02f, Color.white, Vector3.zero, 0, CampaignArt.QTop);
                c.pBtn = CampaignArt.Flat(bp, "plus", 0.02f, Color.white, Vector3.zero, 0, CampaignArt.QTop);
                cells.Add(c);
            }
        }

        static string Key(params int[] a) => string.Join(",", a);

        /// <summary>按存档重画：节点状态、航线颜色、编成表数字、统率 / 星晶 / 纹章。</summary>
        public void Refresh()
        {
            if (run == null || paper == null) return;
            var reach = new HashSet<int>(run.Reachable().Select(n => n.id));
            var path = Path();
            foreach (var v in nodes)
            {
                bool done = v.n.done;
                if (v.mark) Destroy(v.mark.gameObject);
                v.mark = null;
                if (done) v.mark = CampaignArt.Flat(v.root, v.n.kind is NodeKind.Supply or NodeKind.Armory or NodeKind.Ruins ? "check" : "cross", SymH(v.n) * 0.85f, Pencil, new Vector3(0, 0.001f, 0), v.n.id * 31 % 40 - 20, CampaignArt.QTop);
                v.ring.gameObject.SetActive(reach.Contains(v.n.id) && moveT < 0f);
                // 去不了的节点（走过的岔路另一头、更远的层）淡一点；能去的、走过的照常
                bool past = !done && !reach.Contains(v.n.id) && run.at >= 0 && v.n.layer <= run.Node(run.at).layer;
                CampaignArt.Tint(v.sym, past ? new Color(1f, 1f, 1f, 0.38f) : Color.white);
            }
            var dash = CampaignArt.Tex("route_dash"); var solid = CampaignArt.Tex("route_solid");
            foreach (var e in edges)
            {
                var a = e.a >= 0 ? run.Node(e.a) : null; var b = run.Node(e.b);
                bool walked = (a == null || path.Contains(e.a)) && path.Contains(e.b) && b.done;
                bool next = reach.Contains(e.b) && (run.at == e.a);
                var pa = a != null ? NodePos(a) : StartPos; var pb = NodePos(b);
                var d = (pb - pa).normalized;
                float ra = a != null ? SymH(a) * 0.55f : 0.035f, rb = SymH(b) * 0.6f;
                var s = pa + d * ra; var t = pb - d * rb;
                e.f.sharedMesh = CampaignArt.Strip(e.f.sharedMesh, new[] { s, t }, walked ? 0.0035f : next ? 0.0052f : 0.0042f, walked ? 0.05f : next ? 0.028f : 0.024f);   // 墨点虚线：一张贴图 4 个点
                e.r.sharedMaterial = CampaignArt.Mat(walked ? solid : dash, CampaignArt.QRoute, true);
                CampaignArt.Tint(e.r, walked ? Blue : next ? Red : Pencil, 50f);   // 平铺的条带 uv.x 会超过 1，显出值给大（不然被 _Reveal 裁掉）；不能太大：着色器是半精度，过万时 smoothstep 两端相等变 NaN
                e.arrow.gameObject.SetActive(next && moveT < 0f);
                if (next)
                {
                    e.arrow.localPosition = t - d * 0.004f + Vector3.up * 0.0004f;
                    e.arrow.localRotation = Quaternion.AngleAxis(-Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                }
            }
            if (moveT < 0f) marker.localPosition = (run.at >= 0 ? NodePos(run.Node(run.at)) : StartPos) + new Vector3(0f, 0f, -0.045f);
            moveLine.gameObject.SetActive(false);
            RefreshOob();
        }

        /// <summary>走过的节点（按顺序）：从起点一路连到现在的位置。存档里没记路径，按“已完成且相连”往回推。</summary>
        HashSet<int> Path()
        {
            var s = new HashSet<int>();
            if (run.at < 0) return s;
            int cur = run.at; s.Add(cur);
            for (int guard = 0; guard < 20; guard++)
            {
                var prev = run.nodes.FirstOrDefault(n => n.done && n.next.Contains(cur));
                if (prev == null) break;
                s.Add(prev.id); cur = prev.id;
            }
            return s;
        }

        void RefreshOob()
        {
            int tot = muster.Sum(t => t.n);
            foreach (var c in cells)
            {
                bool open = run.unlocked.Contains(c.id);
                int fit = run.Get(run.roster, c.id), wnd = run.Get(run.wounded, c.id), dep = muster.FirstOrDefault(t => t.id == c.id)?.n ?? 0;
                string key = Key(open ? 1 : 0, fit, wnd, dep, (int)Loc.Current);
                c.minus.t.gameObject.SetActive(open && Live); c.plus.t.gameObject.SetActive(open && Live);
                CampaignArt.Tint(c.mBtn, dep > 0 ? Color.white : Dim); CampaignArt.Tint(c.pBtn, dep < fit && tot < run.cap ? Color.white : Dim);
                if (key == c.lastKey) continue;
                c.lastKey = key;
                foreach (var t in new[] { c.fit, c.wound, c.dep }) if (t) Destroy(t.gameObject);
                int i = System.Array.IndexOf(Troops, c.id); float x = OobCols[i];
                if (!open)
                {
                    // 还没编入：三格都画一道短横
                    c.fit = CampaignArt.Number(oobRoot, "-", 0.022f, GoldDim, true, 0.5f); c.fit.localPosition = P(x, RowFit, 0.002f);
                    c.wound = CampaignArt.Number(oobRoot, "-", 0.022f, GoldDim, true, 0.5f); c.wound.localPosition = P(x, RowWound, 0.002f);
                    c.dep = CampaignArt.Number(oobRoot, "-", 0.024f, GoldDim, true, 0.5f); c.dep.localPosition = P(x, RowDeploy, 0.002f);
                    continue;
                }
                c.fit = CampaignArt.Number(oobRoot, fit.ToString(), 0.022f, Ivory, true, 0.5f); c.fit.localPosition = P(x, RowFit, 0.002f);
                c.wound = CampaignArt.Number(oobRoot, wnd > 0 ? wnd.ToString() : "-", 0.022f, wnd > 0 ? Wound : GoldDim, true, 0.5f); c.wound.localPosition = P(x, RowWound, 0.002f);
                c.dep = CampaignArt.Number(oobRoot, dep.ToString(), 0.028f, dep > 0 ? Gold : GoldDim, true, 0.5f); c.dep.localPosition = P(x, RowDeploy, 0.002f);
            }
            string ck = Key(tot, run.cap);
            if (ck != capKey) { capKey = ck; if (capNum) Destroy(capNum.gameObject); capNum = CampaignArt.Number(oobRoot, tot + "/" + run.cap, 0.028f, tot > 0 ? Blue : Red, true, 1f); capNum.localPosition = P(SideR, (SideRows[0] + SideRows[1]) / 2, 0.002f); }
            string kk = Key(run.crystals);
            if (kk != crysKey) { crysKey = kk; if (crysNum) Destroy(crysNum.gameObject); crysNum = CampaignArt.Number(oobRoot, run.crystals.ToString(), 0.028f, Blue, true, 1f); crysNum.localPosition = P(SideR, (SideRows[1] + SideRows[2]) / 2, 0.002f); }
            string rk = string.Join(",", run.relics);
            if (rk != relicKey)
            {
                relicKey = rk; if (relicRoot) Destroy(relicRoot.gameObject);
                relicRoot = new GameObject("Relics").transform; relicRoot.SetParent(oobRoot, false);
                for (int i = 0; i < run.relics.Count && i < 7; i++)
                    CampaignArt.Flat(relicRoot, "badge_" + run.relics[i], 0.026f, Color.white, P((SideL + SideR) / 2 + (i - 3) * 84, SideRows[2] + 128, 0.002f), 0, CampaignArt.QTop);   // 和底图上七个圈对齐
            }
        }

        public bool Moving => moveT >= 0f;

        void Update()
        {
            if (run == null || paper == null) return;
            float dt = Time.deltaTime;
            // ── 走向选中的节点：蓝实线从当前位置长出去，我军标跟着滑过去
            if (moveT >= 0f)
            {
                moveT += dt / 1.3f;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(moveT));
                var to = NodePos(moving);
                var d = (to - moveFrom).normalized;
                var s = moveFrom + d * 0.03f; var e = Vector3.Lerp(s, to - d * 0.032f, k);
                moveLine.gameObject.SetActive(true);
                moveLineF.sharedMesh = CampaignArt.Strip(moveLineF.sharedMesh, new[] { s + Vector3.up * 0.0003f, e + Vector3.up * 0.0003f }, 0.008f, 0.05f);
                marker.localPosition = Vector3.Lerp(moveFrom, to, k) + new Vector3(0f, 0.004f * Mathf.Sin(k * Mathf.PI), -0.045f);
                if (moveT >= 1.25f) { var n = moving; moveT = -1f; moving = null; OnPick?.Invoke(n); }
                return;
            }
            if (!Live) return;
            // ── 编成表加减
            int tot = muster.Sum(t => t.n);
            foreach (var c in cells)
            {
                if (!c.minus.t.gameObject.activeSelf) continue;
                c.minus.Tick(mr, true, editorCam); c.plus.Tick(mr, true, editorCam);
                if (c.minus.Pressed) Bump(c.id, -1);
                if (c.plus.Pressed) Bump(c.id, +1);
                c.mBtn.transform.localScale = Vector3.one * 0.018f * (c.minus.Hover ? 1.25f : 1f);
                c.pBtn.transform.localScale = Vector3.one * 0.018f * (c.plus.Hover ? 1.25f : 1f);
            }
            // ── 选下一站
            var reach = run.Reachable().Select(n => n.id).ToHashSet();
            int h = -1; bool tap = false;
            rays.Clear();
            if (mr && mr.Active) { mr.Pointers(rays); tap = mr.PinchDown; }
            else if (editorCam && Mouse.current != null) { rays.Add(WarTable.Game.FlyCam.View(editorCam).ScreenPointToRay(Mouse.current.position.ReadValue())); tap = Mouse.current.leftButton.wasPressedThisFrame; }
            var plane = new Plane(transform.up, transform.TransformPoint(Vector3.zero));
            float sc = transform.lossyScale.x;
            foreach (var ray in rays)
            {
                if (!plane.Raycast(ray, out float dist)) continue;
                var lp = transform.InverseTransformPoint(ray.GetPoint(dist));
                float best = 0.055f;
                foreach (var v in nodes)
                {
                    if (!reach.Contains(v.n.id)) continue;
                    var q = NodePos(v.n); float dd = new Vector2(lp.x - q.x, lp.z - q.z).magnitude;
                    if (dd < best) { best = dd; h = v.n.id; }
                }
                if (h >= 0) break;
            }
            // 指尖直接点到红圈上
            int touch = -1;
            if (mr && mr.Active)
                for (int i = 0; i < 2 && touch < 0; i++)
                {
                    var hs = i == 0 ? mr.Left : mr.Right;
                    if (!hs.tracked || hs.pinch) continue;
                    var lp = transform.InverseTransformPoint(hs.indexTip);
                    if (lp.y < -0.01f || lp.y > 0.02f) continue;
                    foreach (var v in nodes)
                        if (reach.Contains(v.n.id) && new Vector2(lp.x - NodePos(v.n).x, lp.z - NodePos(v.n).z).magnitude < 0.035f) { touch = v.n.id; break; }
                }
            if (touch >= 0) h = touch;
            if (h != hover && h >= 0 && sound) sound.Cue("hover");
            hover = h;
            foreach (var v in nodes)
            {
                v.hover = Mathf.MoveTowards(v.hover, v.n.id == hover ? 1f : 0f, dt / 0.15f);
                if (!v.ring.gameObject.activeSelf) continue;
                v.pulse += dt;
                float s = SymH(v.n) * 1.35f * (1f + 0.03f * Mathf.Sin(v.pulse * 3f) + 0.18f * v.hover);
                v.ring.transform.localScale = Vector3.one * s;
                v.ring.transform.localRotation = Quaternion.AngleAxis(v.pulse * 12f + v.n.id * 47, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);   // 刻度圈慢慢转
                CampaignArt.Tint(v.ring, new Color(Gold.r, Gold.g, Gold.b, 0.8f + 0.2f * v.hover));
                v.sym.transform.localScale = Vector3.one * SymH(v.n) * (1f + 0.12f * v.hover);
            }
            if ((tap && h >= 0) || (touch >= 0 && touchWas < 0))
            {
                if (muster.Sum(t => t.n) == 0 && run.Node(h).kind is NodeKind.Battle or NodeKind.Elite or NodeKind.Boss)
                {
                    if (sound && Time.time - denyT > 0.4f) sound.Cue("deny");
                    denyT = Time.time;
                }
                else Go(run.Node(h));
            }
            touchWas = touch;
            RefreshOob();
        }
        int touchWas = -1;

        void Bump(string id, int d)
        {
            var e = muster.FirstOrDefault(t => t.id == id);
            int cur = e?.n ?? 0, fit = run.Get(run.roster, id), tot = muster.Sum(t => t.n);
            int nv = cur + d;
            if (nv < 0 || nv > fit || (d > 0 && tot >= run.cap)) { if (sound) sound.Cue("deny"); return; }
            if (e == null) muster.Add(new TroopCount(id, nv)); else e.n = nv;
            muster.RemoveAll(t => t.n <= 0);
            if (sound) sound.Cue("select");
        }

        /// <summary>出发去某个节点：画线 + 走过去，到了发 OnPick。</summary>
        public void Go(MapNode n)
        {
            if (moveT >= 0f || n == null) return;
            moving = n; moveT = 0f;
            moveFrom = run.at >= 0 ? NodePos(run.Node(run.at)) : StartPos;
            foreach (var v in nodes) v.ring.gameObject.SetActive(false);
            foreach (var e in edges) e.arrow.gameObject.SetActive(false);
            if (sound) sound.Cue("press");
            Debug.Log($"[Campaign] 出发 → 节点 {n.id}（{n.kind}，第 {n.layer} 层）");
        }

        /// <summary>编辑器验收：直接选第 k 个能去的节点。</summary>
        public void PickReachable(int k) { var r = run.Reachable().ToList(); if (r.Count > 0) Go(r[Mathf.Clamp(k, 0, r.Count - 1)]); }
    }
}
