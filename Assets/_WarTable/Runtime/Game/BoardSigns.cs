using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WarTable.Command;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 沙盘上常驻的“看得懂”的标识（11-05 用户：哪边是我们哪边是敌人、怎么选中军队、怎么攻击、攻击是怎么攻击的都看不懂；
    /// 手势的交互方式可以一直显示在场景里；拖动沙盘要左右两个把手，双手捏住才能拖）。
    /// - 每支部队头顶一块旗牌：我军蓝底金边、敌军暗红底，上写“我军 / 敌军”，大字兵种（步 / 弓 / 骑 / 射）；
    ///   正对玩家、离得远也一样大（按距离缩放）；看着时放大、选中时金色描边呼吸，这就是“可以捏它”的提示。
    /// - 进攻 / 移动的去向：我军每支有军令的部队，从它脚下画一条贴地的粗箭头到目标（冲敌 = 朱红箭头指着那支敌军，
    ///   移动 / 列阵 = 金色箭头指着目的地），一眼看出“它要去哪、打谁”。
    /// - 手势说明牌：悬浮在沙盘左上方（11-06 改：玩家多用右手做手势，放左边不挡手），正对眼睛。
    /// - 两个把手：靠玩家一侧木框正中并排两个金色圆扣（11-06 改到眼前），两只手同时捏住两个圆扣才能拖动 / 转动沙盘（TrayRig 只认这两个点）。
    /// </summary>
    public class BoardSigns : MonoBehaviour
    {
        public Transform board;
        public BattleView view;
        public Material mat;
        [Tooltip("永远画在最上面的版本：手里拿着 / 手正够着的令牌用（11-10）")] public Material matTop;
        public Texture2D[] tilesZh = new Texture2D[32], tilesEn = new Texture2D[32];   // [时代 * 8 + 队 * 4 + 兵种序号]（11-11：后 8 张是哥特时代）
        /// <summary>被看着 / 被拖动瞄准时的旗牌（金光边 + 右下角“捏”徽章）。11-07 用户：眼睛看到后图标要变化，不只是放大。</summary>
        public Texture2D[] tilesHotZh = new Texture2D[32], tilesHotEn = new Texture2D[32];
        public Texture2D legendZh, legendEn, handleTex;
        [Tooltip("11-10：把手（墨底金圈圆钮 + 捏合手）和“放好了”朱漆印钮")]
        public Texture2D knobTex, readyZh, readyEn;
        [Tooltip("11-10：主将头顶的金色“帅”字牌（兵法页说“护住主将”，玩家要看得到主将在哪）")]
        public Texture2D generalZh, generalEn;
        readonly Renderer[] generals = new Renderer[2];
        /// <summary>开局调整沙盘时显示“放好了”印钮（WarGame 设）。</summary>
        public bool ShowReady;
        /// <summary>这一帧“放好了”被手指按下（或在它上面捏了一下）。</summary>
        public bool ReadyPressed { get; private set; }
        Renderer ready; bool readyTouchWas; float readyShownT = -1f;
        /// <summary>兵法页、收起后的小牌。</summary>
        public Texture2D legendTacticsZh, legendTacticsEn, legendTabZh, legendTabEn;
        public MRInput mr;
        public Transform editorEye;
        // 11-07 用户：远处的敌军不用那么大，正常大小就行 → 旗牌改成固定的实物大小（随沙盘缩放），不再按距离放大
        [Tooltip("旗牌高度（米，沙盘 1× 时）")] public float tileSize = 0.06f;

        static readonly string[] Groups = BattleCommand.AllGroups;
        readonly Dictionary<string, Renderer> tiles = new();
        Renderer legend, legendTab, handleL, handleR;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), MapId = Shader.PropertyToID("_BaseMap");

        Transform Eye => mr && mr.Active ? mr.cam.transform : editorEye;

        Renderer Quad(string name, int queue)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(g.GetComponent<Collider>());
            g.name = name; g.transform.SetParent(transform, false);
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sortingOrder = queue;
            return r;
        }

        void Ensure()
        {
            if (mpb != null) return;
            mpb = new MaterialPropertyBlock();
            foreach (var team in new[] { 0, 1 }) foreach (var g in Groups) tiles[team + g] = Quad("Tile_" + team + g, 0);
            legend = Quad("Legend", 0); legendTab = Quad("LegendTab", 0);
            handleL = Quad("HandleL", 0); handleR = Quad("HandleR", 0);
            ready = Quad("Ready", 0);
            generals[0] = Quad("General_0", 0); generals[1] = Quad("General_1", 0);
        }

        /// <summary>视线是否打中某支部队的旗牌（旗牌矩形外扩 25%，眼动有误差）。</summary>
        /// <summary>视线打中旗牌时给出离中心多远（0 = 正中，1 = 边缘，放大 1.25 倍的范围内），WarGame 用它在几块旗牌里挑最准的。</summary>
        /// <summary>我军这支部队的令牌（旗牌）现在在哪、多高（11-09：伸手捏住令牌拿起来）。</summary>
        public bool OwnTile(string group, out Vector3 pos, out float height)
        {
            foreach (var (g, t, h) in ownTiles) if (g == group && t) { pos = t.position; height = h; return true; }
            pos = default; height = 0f; return false;
        }

        public bool TileScore(int team, string group, Ray ray, out float score)
        {
            score = 1f;
            if (!tiles.TryGetValue(team + group, out var r) || !r || !r.gameObject.activeInHierarchy) return false;
            var t = r.transform;
            var o = t.InverseTransformPoint(ray.origin); var v = t.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(v.z) < 1e-5f) return false;
            float k = -o.z / v.z; if (k <= 0f) return false;
            var p = o + v * k;
            float m = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) / (0.5f * 1.25f);
            if (m >= 1f) return false;
            score = m; return true;
        }

        public bool HitTile(int team, string group, Ray ray)
        {
            if (!tiles.TryGetValue(team + group, out var r) || !r || !r.gameObject.activeInHierarchy) return false;
            var t = r.transform;
            var o = t.InverseTransformPoint(ray.origin); var v = t.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(v.z) < 1e-5f) return false;
            float k = -o.z / v.z; if (k <= 0f) return false;
            var p = o + v * k;
            return Mathf.Abs(p.x) < 0.5f * 1.25f && Mathf.Abs(p.y) < 0.5f * 1.25f;
        }

        static void Show(Renderer r, bool on) { if (r && r.gameObject.activeSelf != on) r.gameObject.SetActive(on); }

        public void HideAll()
        {
            foreach (var r in tiles.Values) Show(r, false);
            foreach (var r in badges.Values) Show(r, false);
            Show(legend, false); Show(legendTab, false); Show(handleL, false); Show(handleR, false); if (ready) Show(ready, false); ReadyPressed = false;
            foreach (var gr in generals) if (gr) Show(gr, false);
        }

        readonly List<(Transform t, float dist, float h, float w)> placed = new();
        readonly List<(string g, Transform t, float h)> ownTiles = new();
        readonly Dictionary<string, int> lastCount = new();
        readonly Dictionary<string, float> lossT = new();
        readonly Dictionary<string, Renderer> badges = new();
        /// <summary>状态小牌贴图，顺序同 States。</summary>
        public Texture2D[] statesZh = new Texture2D[7], statesEn = new Texture2D[7];
        /// <summary>11-13 用户：所有风格的名字都要跟着换 → “目标·弓兵”这类目标牌按风格各一套：下标 = 风格 × 4 + 兵种（步 / 弓 / 骑 / 骑射），空项退回 statesZh 里中古那张。</summary>
        public Texture2D[] aimZh, aimEn;
        public static readonly string[] States = { "idle", "move", "attack", "charge", "fight", "hold", "retreat", "aim_inf", "aim_arch", "aim_cav", "aim_ha" };   // aim_* = 布阵时预定的目标
        static string GroupName(Grp g) => g == Grp.Arch ? "arch" : g == Grp.Cav ? "cav" : g == Grp.Ha ? "ha" : "inf";

        /// <summary>11-06 自查：两军接战时旗牌在视野里叠在一起、分不清谁是谁 → 按视角检测重叠，近的不动，远的往上抬到刚好不重叠。</summary>
        void Separate(Transform eye)
        {
            placed.Sort((a, b) => a.dist.CompareTo(b.dist));
            for (int i = 1; i < placed.Count; i++)
                for (int pass = 0; pass < 2; pass++)
                    for (int j = 0; j < i; j++)
                    {
                        var (ti, di, hi, wi) = placed[i]; var (tj, dj, hj, wj) = placed[j];
                        // 视角坐标（弧度）：以眼为原点，横 = 右方向分量 / 距离，竖 = 上方向分量 / 距离
                        Vector3 vi = ti.position - eye.position, vj = tj.position - eye.position;
                        float xi = Vector3.Dot(vi, eye.right) / di, yi = Vector3.Dot(vi, Vector3.up) / di;
                        float xj = Vector3.Dot(vj, eye.right) / dj, yj = Vector3.Dot(vj, Vector3.up) / dj;
                        float needX = (wi / di + wj / dj) * 0.5f, needY = (hi / di + hj / dj) * 0.5f + 0.004f;
                        if (Mathf.Abs(xi - xj) >= needX || Mathf.Abs(yi - yj) >= needY) continue;
                        float up = yj + needY - yi;
                        if (up > 0f) ti.position += Vector3.up * (up * di);
                    }
        }

        /// <summary>每帧。cmd = null（封面 / 结算）时只保留把手（摆好的沙盘也能挪），其余藏起来。</summary>
        public void Tick(BattleCommand cmd, WarGame g, TrayRig tray, bool showLegend)
        {
            Ensure();
            var eye = Eye;
            if (!eye || !board || !board.gameObject.activeInHierarchy) { HideAll(); return; }
            bool zh = Loc.Current == Loc.Lang.Zh;
            float t = Time.time;

            // ── 部队旗牌
            var seen = new HashSet<string>(); placed.Clear(); ownTiles.Clear();
            int era = Settings.Era;
            if (cmd != null)
                foreach (var f in cmd.Flags())
                {
                    string key = f.team + f.group;
                    if (!tiles.TryGetValue(key, out var r)) continue;
                    seen.Add(key);
                    int gi = System.Array.IndexOf(Groups, f.group);
                    int ix = f.team * 4 + gi; if (era > 0 && tilesZh.Length >= (era + 1) * 8 && tilesZh[era * 8 + ix]) ix += era * 8;   // 11-11：哥特时代用后 8 张（兵种名 / 图标不同）
                    bool aimed = f.team != BattleCommand.Me && (cmd.DragFoe == f.group || cmd.QueuedTarget(f.group));   // 正被拖过来的部队瞄准
                    bool hot = f.hover || aimed;
                    var hotSet = zh ? tilesHotZh : tilesHotEn;
                    var tex = hot && hotSet != null && hotSet[ix] ? hotSet[ix] : (zh ? tilesZh : tilesEn)[ix];
                    var at = g.SimToWorld(f.x, g.Battle.Terrain!.Height(f.x, f.z) + 7, f.z);
                    // 11-08 用户：直接用手把这支部队的旗牌拖到地方 → 捏住时旗牌跟着手的落点走，松手部队就开过去
                    var held = cmd.Pinch.Holding;
                    bool dragged = f.team == BattleCommand.Me && held.HasValue && held.Value.unit.kind == TargetKind.Own && GroupName(held.Value.unit.group) == f.group;
                    if (dragged) { var e2 = held.Value.end; at = g.SimToWorld(e2.x, g.Battle.Terrain!.Height(e2.x, e2.z) + 9, e2.z); hot = true; tex = hotSet != null && hotSet[ix] ? hotSet[ix] : tex; }
                    float dist = Vector3.Distance(eye.position, at);
                    float pop = f.selected ? 1.12f + 0.03f * Mathf.Sin(t * 4f) : hot ? 1.06f : 1f;   // 主要靠换图，只轻轻放大
                    float scaleK = board.lossyScale.x / BoardMap.BoardScale;
                    // 11-08 用户：攻击看着像打不中 → 这支部队死了人，旗牌抖一下、闪一下（敌人掉人闪金、我军掉人闪红），远处也看得到
                    if (lastCount.TryGetValue(key, out int lc) && f.count < lc) lossT[key] = t;
                    lastCount[key] = f.count;
                    float since = lossT.TryGetValue(key, out float lt0) ? t - lt0 : 9f;
                    float shake = since < 0.35f ? (1f - since / 0.35f) : 0f;
                    var tr = r.transform;
                    tr.position = at + Vector3.up * (tileSize * scaleK * pop * 0.5f)
                                  + (eye.right * Mathf.Sin(t * 70f) * 0.004f + Vector3.up * 0.003f * Mathf.Sin(t * 53f)) * shake * scaleK;
                    tr.rotation = Quaternion.LookRotation(tr.position - eye.position, Vector3.up);
                    float h = tileSize * scaleK * pop;
                    tr.localScale = new Vector3(h * UiAspect.Of(tex, 0.78f), h, 1f);
                    // 没选中时敌军略淡；选中我军后敌军全亮（= 可以点它冲锋）
                    float a = f.team == BattleCommand.Me ? 1f : cmd.Selected.Count > 0 || f.hover ? 1f : 0.82f;
                    var c = f.selected ? new Color(1f, 0.93f, 0.7f, a) : new Color(1f, 1f, 1f, a);
                    if (shake > 0f) c = Color.Lerp(c, f.team == BattleCommand.Me ? new Color(1f, 0.35f, 0.3f, 1f) : new Color(1f, 0.9f, 0.45f, 1f), shake);
                    r.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, c); r.SetPropertyBlock(mpb);
                    var want = (dragged || f.hover && f.team == BattleCommand.Me) && matTop ? matTop : mat;
                    if (r.sharedMaterial != want) r.sharedMaterial = want;
                    Show(r, true);
                    placed.Add((tr, dist, h, tr.localScale.x));
                    if (f.team == BattleCommand.Me) ownTiles.Add((f.group, tr, h));
                }
            Separate(eye);
            foreach (var kv in tiles) if (!seen.Contains(kv.Key)) Show(kv.Value, false);
            // 状态小牌：我军旗牌正上方（11-08 用户：都站着，不知道他们在干嘛）
            var badgeSeen = new HashSet<string>();
            if (cmd != null)
                foreach (var (grp, tr, th) in ownTiles)
                {
                    var st = cmd.StateOf(grp); int si = System.Array.IndexOf(States, st);
                    var set = zh ? statesZh : statesEn;
                    if (si < 0 || set == null || si >= set.Length || !set[si]) continue;
                    var stTex = set[si];
                    if (si >= 7)   // aim_*：换成当前风格的叫法
                    {
                        var es = zh ? aimZh : aimEn; int ei = Settings.Era * 4 + (si - 7);
                        if (es != null && ei < es.Length && es[ei]) stTex = es[ei];
                    }
                    if (!badges.TryGetValue(grp, out var br)) badges[grp] = br = Quad("State_" + grp, 0);
                    float bh = th * 0.36f;
                    var bt = br.transform;
                    bt.SetPositionAndRotation(tr.position + tr.up * (th * 0.5f + bh * 0.6f), tr.rotation);
                    bt.localScale = new Vector3(bh * UiAspect.Of(stTex, 2.4f), bh, 1f);
                    br.GetPropertyBlock(mpb); mpb.SetTexture(MapId, stTex); mpb.SetColor(ColorId, Color.white); br.SetPropertyBlock(mpb);
                    Show(br, true); badgeSeen.Add(grp);
                }
            foreach (var kv in badges) if (!badgeSeen.Contains(kv.Key)) Show(kv.Value, false);
            Generals(cmd, g, eye, zh, t);

            // ── 木框四角：说明牌（右前角外侧）和左右把手
            float s = board.lossyScale.x;
            float hx = (BoardMap.HalfX * BoardMap.Yard + 0.05f), hz = (BoardMap.HalfZ * BoardMap.Yard + 0.05f);
            if (showLegend && legend)
            {
                // 11-06 用户：悬浮在沙盘左上方（玩家多用右手做手势，放左边不挡手）；11-07 用户：不要拉伸、注意尺寸、可以收起 + 兵法页。
                // 沙盘本地：玩家一侧 +z，左手边 +x → 左侧木框上方、靠远端，抬高 22 cm；整块正对眼睛（含俯仰），站着俯视、坐着平视都看得正
                float lh = LegendH;   // 11-10：手边的面板，固定 24 cm 高（离眼 0.42 m，字够大），不随沙盘缩放
                var lp = LegendSpot(eye, hx, hz, lh);
                var face = lp - eye.position; var rot = face.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(face.normalized, Vector3.up) : Quaternion.identity;
                if (legendOpen)
                {
                    var tex = legendPage == 1 ? (zh ? legendTacticsZh : legendTacticsEn) : (zh ? legendZh : legendEn);
                    var lt = legend.transform; lt.SetPositionAndRotation(lp, rot);
                    lt.localScale = new Vector3(lh * UiAspect.Of(tex, 0.7f), lh, 1f);
                    legend.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, new Color(1f, 1f, 1f, 0.97f)); legend.SetPropertyBlock(mpb);
                    Show(legend, true); Show(legendTab, false);
                }
                else
                {
                    // 收起：只留一块小牌，停在原来标题栏的位置
                    var tex = zh ? legendTabZh : legendTabEn;
                    float th = 0.05f * (legendTabHot ? 1.06f : 1f);
                    var tt = legendTab.transform; tt.SetPositionAndRotation(lp + rot * Vector3.up * (lh * 0.5f - th * 0.5f), rot);
                    tt.localScale = new Vector3(th * UiAspect.Of(tex, 3.4f), th, 1f);
                    legendTab.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, legendTabHot ? Color.white : new Color(1f, 1f, 1f, 0.9f)); legendTab.SetPropertyBlock(mpb);
                    Show(legendTab, true); Show(legend, false);
                }
                LegendTaps();
            }
            else { Show(legend, false); Show(legendTab, false); }

            if (tray && tray.Placed && handleTex)
            {
                for (int i = 0; i < 2; i++)
                {
                    var hr = i == 0 ? handleL : handleR;
                    var wp = tray.Handle(i);
                    var ht = hr.transform;
                    ht.position = wp + Vector3.up * 0.012f;
                    ht.rotation = Quaternion.LookRotation(ht.position - eye.position, Vector3.up);
                    bool hot = tray.HandleHot(i), grab = tray.Grabbing, easy = tray.EasyMove;
                    // 11-10 用户：把手要好操作 → 调整沙盘时把手放大（7.5 cm）、缓缓呼吸提示“捏这里”；开战后缩回 5 cm、半透明
                    float baseSz = easy ? 0.075f : 0.05f;
                    float breathe = easy && !grab && !hot ? 1f + 0.06f * Mathf.Sin(t * 3f) : 1f;
                    float sz = baseSz * (grab ? 1.2f : hot ? 1.12f : 1f) * breathe;
                    // 11-06 用户：拖沙盘的图标要左右对称 → 原图是右手捏合，左把手水平镜像成左手（两只手的拇指都朝中间）
                    ht.localScale = new Vector3(i == 0 ? sz : -sz, sz, 1f);
                    var c = grab ? new Color(1f, 0.85f, 0.45f, 1f) : hot ? new Color(1f, 0.95f, 0.8f, 1f) : easy ? Color.white : new Color(1f, 1f, 1f, 0.7f);
                    hr.GetPropertyBlock(mpb); mpb.SetTexture(MapId, knobTex ? knobTex : handleTex); mpb.SetColor(ColorId, c); hr.SetPropertyBlock(mpb);
                    Show(hr, true);
                }
            }
            else { Show(handleL, false); Show(handleR, false); }
            ReadyButton(eye, tray, hz);
        }

        /// <summary>两军主将头顶各一块金色“帅”字牌（3.2 cm，缓缓上下浮），主将阵亡就消失（HitFx 碎成金光）。</summary>
        void Generals(BattleCommand cmd, WarGame g, Transform eye, bool zh, float t)
        {
            var tex = zh ? generalZh : generalEn;
            for (int team = 0; team < 2; team++)
            {
                var r = generals[team]; if (!r) continue;
                WarTable.Sim.Agent a = null;
                if (cmd != null && tex && g.Battle != null && g.Battle.Commander[team].HasValue)
                    foreach (var x in g.Battle.Agents) if (x.Id == g.Battle.Commander[team].Value) { a = x; break; }
                if (a == null || !a.Alive || a.Gone) { Show(r, false); continue; }
                float k = board.lossyScale.x / BoardMap.BoardScale;
                bool mounted = a.Mount != null && a.Mount.Alive;
                var at = g.SimToWorld(a.X, a.Y + (mounted ? 5.2 : 4.0), a.Z) + Vector3.up * (0.003f * Mathf.Sin(t * 2.2f + team));
                var tr = r.transform; tr.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye.position, Vector3.up));
                float sz = 0.032f * k; tr.localScale = new Vector3(sz, sz, 1f);
                r.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, Color.white); r.SetPropertyBlock(mpb);
                Show(r, true);
            }
        }

        /// <summary>
        /// “放好了”朱漆印钮（11-10）：木框靠玩家一侧的正中（两个把手中间），立起来正对眼睛，直径 8 cm。
        /// 食指点一下（指尖碰到印面）或在它上面捏一下 = 确认；刚出现的 0.8 秒内不认（免得摆沙盘的手顺手碰到）。
        /// </summary>
        void ReadyButton(Transform eye, TrayRig tray, float hz)
        {
            ReadyPressed = false;
            var tex = Loc.Current == Loc.Lang.Zh ? readyZh : readyEn;
            if (!ShowReady || !ready || !tex || !tray || !tray.Placed) { if (ready) Show(ready, false); readyShownT = -1f; readyTouchWas = true; return; }
            if (readyShownT < 0f) readyShownT = Time.time;
            var rt = ready.transform;
            rt.position = board.TransformPoint(new Vector3(0f, 0.03f, hz + 0.02f)) + Vector3.up * 0.05f;
            rt.rotation = Quaternion.LookRotation(rt.position - eye.position, Vector3.up);
            bool near = Poke.Near(rt, new Rect(-0.5f, -0.5f, 1f, 1f), mr);
            bool touch = Poke.Touch(rt, new Rect(-0.5f, -0.5f, 1f, 1f), mr);
            if (mr && mr.Active) foreach (var h in new[] { mr.Left, mr.Right }) if (h.tracked && h.pinch && Vector3.Distance(h.pinchPoint, rt.position) < 0.05f) touch = true;
            bool live = Time.time - readyShownT > 0.8f && !tray.Grabbing;
            if (touch && !readyTouchWas && live) ReadyPressed = true;
            readyTouchWas = touch;
            float sz = 0.08f * (near || touch ? 1.1f : 1f + 0.04f * Mathf.Sin(Time.time * 3f));
            rt.localScale = new Vector3(sz, sz, 1f);
            ready.GetPropertyBlock(mpb); mpb.SetTexture(MapId, tex); mpb.SetColor(ColorId, near || touch ? Color.white : new Color(0.95f, 0.92f, 0.88f, 1f)); ready.SetPropertyBlock(mpb);
            Show(ready, true);
        }

        Vector3 legendPos; bool legendPlaced;

        /// <summary>
        /// 说明面板的位置（11-07 真机日志：交战时一半的捏合被面板吞掉——面板挡在视线和左侧敌军之间）。
        /// 面板放在沙盘左后角上方，并按眼睛的实际高度算：面板底边要高过“从眼睛看过去，面板身后那片沙盘上所有旗牌的最高视线”再留 2°，
        /// 坐着、站着都不会盖住任何部队。高度平滑跟随（头动时不抖）。
        /// </summary>
        Vector3 LegendSpot(Transform eye, float hx, float hz, float lh)
        {
            // 11-10 用户：面板要离玩家近、伸手就能点 → 放在玩家左前方：沙盘“远离玩家”的方向往左转 LegendYaw 度、离眼睛水平 LegendDist，
            // 高度抬到那个方向上沙盘轮廓之上（不挡部队），最低不低于桌面上方一点。方向按沙盘算（不跟头转），只有距离按眼睛位置算
            var e = eye.position;
            var away = -board.forward; away.y = 0; away = away.sqrMagnitude < 1e-4f ? Vector3.forward : away.normalized;
            var left = board.right; left.y = 0; left = left.sqrMagnitude < 1e-4f ? Vector3.left : left.normalized;   // 沙盘本地 +x = 玩家左手边
            float a = LegendYaw * Mathf.Deg2Rad;
            var dir = (away * Mathf.Cos(a) + left * Mathf.Sin(a)).normalized;
            var fwd = eye.forward; fwd.y = 0; fwd = fwd.sqrMagnitude < 1e-4f ? away : fwd.normalized;
            float yaw = Vector3.SignedAngle(fwd, dir, Vector3.up);
            float lw = lh * 0.72f;
            float below = ViewClear.Below(eye, LegendDist, lw * 0.5f, lh * 0.5f, 0.15f, 0.05f, 2f, yaw);
            float y = Mathf.Max(e.y - below, board.position.y + lh * 0.5f + 0.04f);
            var target = new Vector3(e.x + dir.x * LegendDist, y, e.z + dir.z * LegendDist);
            if (!legendPlaced || (legendPos - target).sqrMagnitude > 0.25f) { legendPos = target; legendPlaced = true; }
            else legendPos = Vector3.Lerp(legendPos, target, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            return legendPos;
        }
        const float LegendYaw = 40f, LegendDist = 0.42f, LegendH = 0.24f;

        // ── 说明面板：收起 / 展开、手势页 / 兵法页（11-07 用户：让玩家可以收起这个面板）
        const string LegendKey = "wartable.legend.open";
        // 11-10 用户：面板默认展开，玩家可以收起 → 每次进游戏都展开；收起只在这次游戏里记住（静态变量，不存盘）
        static bool sessionOpen = true;
        bool legendOpen = true, legendTabHot;
        // 11-14 用户：玩过新手教学之后不该每次都再提醒 → 教学结束以后（Tutorial.Done），操作说明牌默认收成右前角的小牌，想看再点开；
        // 玩家这次游戏里自己展开 / 收起过，就按他的来
        static bool sessionTouched;
        void Awake() => legendOpen = sessionTouched ? sessionOpen : !Tutorial.Done;
        void OnEnable() => SyncLegend();
        /// <summary>每局开始时调用：玩家这次游戏里没动过说明牌，就按“教学结束了没有”定展开还是收起（刚打完第一局、再来一局时就收起来了）。</summary>
        public void SyncLegend() { if (!sessionTouched) legendOpen = !Tutorial.Done; }
        int legendPage;
        float tapAwayT = 9f, tapT = -9f;
        // 标题栏三格（贴图 1000 × 1440：标题栏 y 26–176，三格等分 x 26–966）——和 make_field_ui.py 一致
        const float HeadV0 = 26f / 1440f, HeadV1 = 176f / 1440f, CellU0 = 0.026f, CellW = 0.94f / 3f;

        /// <summary>视线落在面板 / 小牌上（WarGame 据此不把这一捏交给战场）。</summary>
        public bool LegendGazed { get; private set; }

        static bool UV(Transform t, Ray r, out Vector2 uv)
        {
            uv = default;
            var o = t.InverseTransformPoint(r.origin); var v = t.InverseTransformDirection(r.direction);
            if (Mathf.Abs(v.z) < 1e-5f) return false;
            float k = -o.z / v.z; if (k <= 0f) return false;
            var p = o + v * k;
            uv = new Vector2(p.x + 0.5f, 0.5f - p.y);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        /// <summary>面板本地 uv（v 从上往下）落在标题栏哪一格：0 手势、1 兵法、2 收起，不在标题栏返回 −1。</summary>
        static int Cell(Vector2 uv)
        {
            if (uv.y < HeadV0 || uv.y > HeadV1) return -1;
            int c = Mathf.FloorToInt((uv.x - CellU0) / CellW);
            return c >= 0 && c <= 2 ? c : -1;
        }

        void Act(int cell)
        {
            if (cell == 0 || cell == 1) legendPage = cell;
            else if (cell == 2) SetOpen(false);
        }

        void SetOpen(bool open) { legendOpen = sessionOpen = open; sessionTouched = true; }

        /// <summary>每帧由 WarGame 调用：视线在面板 / 小牌上就返回 true；pinchDown 这一帧刚捏下去就执行看着的那一格。</summary>
        public bool LegendInput(Ray gaze, bool pinchDown)
        {
            LegendGazed = false; legendTabHot = false;
            // 11-08 真机日志：看面板附近的敌军时，眼动误差让视线常扫到面板，整块面板都抢捏合 → 只有看着顶上三格按钮才接这一捏
            if (legendOpen && legend && legend.gameObject.activeInHierarchy && UV(legend.transform, gaze, out var uv) && Cell(uv) >= 0)
            {
                LegendGazed = true;
                if (pinchDown) Act(Cell(uv));
            }
            else if (!legendOpen && legendTab && legendTab.gameObject.activeInHierarchy && UV(legendTab.transform, gaze, out _))
            {
                LegendGazed = legendTabHot = true;
                if (pinchDown) SetOpen(true);
            }
            return LegendGazed;
        }

        /// <summary>手指直接点标题栏的格子 / 小牌（防抖：指尖先离开 0.4 秒再碰上才算，切换后 0.6 秒内不再认）。</summary>
        void LegendTaps()
        {
            if (Keyboard.current != null)   // 编辑器：G 收起 / 展开，T 切页
            {
                if (Keyboard.current.gKey.wasPressedThisFrame) SetOpen(!legendOpen);
                if (Keyboard.current.tKey.wasPressedThisFrame) legendPage = 1 - legendPage;
            }
            if (!mr || !mr.Active) return;
            int hit = -2;
            if (legendOpen && legend && legend.gameObject.activeInHierarchy)
                for (int c = 0; c < 3; c++)
                {
                    var rect = new Rect(CellU0 + CellW * c - 0.5f, 0.5f - HeadV1, CellW, HeadV1 - HeadV0);
                    if (Poke.Touch(legend.transform, rect, mr)) { hit = c; break; }
                }
            if (!legendOpen && legendTab && legendTab.gameObject.activeInHierarchy && Poke.Touch(legendTab.transform, new Rect(-0.5f, -0.5f, 1f, 1f), mr)) hit = -1;
            if (hit == -2) { tapAwayT += Time.deltaTime; return; }
            bool ok = tapAwayT > 0.4f && Time.time - tapT > 0.6f;
            tapAwayT = 0f;
            if (!ok) return;
            tapT = Time.time;
            if (hit == -1) SetOpen(true); else Act(hit);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized; }
    }
}
