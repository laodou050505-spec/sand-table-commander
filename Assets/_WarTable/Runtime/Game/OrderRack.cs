using UnityEngine;
using WarTable.XR;
using WarTable;

namespace WarTable.Game
{
    /// <summary>
    /// 令牌（11-10 用户：手势识别不好、常误触 → 去掉所有指挥手势，换成三支令牌：冲锋 / 坚守 / 撤退；扔到沙盘上任意位置 = 对全军下令）。
    ///
    /// 11-11 第二轮（用户：令牌质感不好；可以靠在沙盘上；字要在令牌上、注意英文版；当成能拿起来直接扔上沙盘的模型，现在交互不顺）：
    /// - 模型：Tools/make_token_models.py 程序建模——漆面牌身（朱 / 黑 / 靛三色，免拿错）+ 真刻槽嵌金（令徽 + 字，正反两面），中英各一套网格。
    /// - 摆放：三支靠在沙盘前沿木框外侧、偏右手（牌底立在桌上，上半截斜搭在木框上沿，字面朝玩家）。
    /// - 拿：指尖靠近某支 GrabRadius 以内就亮，捏下去拿起；从哪里捏住就从哪里提着（不瞬移），令牌像被提着一样随手摆动。
    /// - 扔：松手时用指尖最近 0.1 秒的真实速度（含竖直方向）抛出去，按重力飞；预测落点不在沙盘上、但令牌已经拿离原处 → 轻轻修正成落到沙盘上
    ///   离预测落点最近的地方（不用瞄准）；在沙盘正上方轻轻松手 = 直接掉下去。落地插进土里、摇两下、扬尘，下令。几乎没挪就松手 = 放回去。
    /// </summary>
    public class OrderRack : MonoBehaviour
    {
        public enum Order { Charge, Hold, Retreat }
        public static readonly string[] Keys = { "charge", "hold", "retreat" };

        public WarGame game;
        /// <summary>令牌模型（子物体 Body / Inlay），下标 = 令。</summary>
        public GameObject[] modelZh = new GameObject[3], modelEn = new GameObject[3];
        /// <summary>牌身漆（下标 = 令）和嵌金。</summary>
        public Material[] lacquer = new Material[3];
        public Material gold;
        /// <summary>令牌落地生效（WarGame 下令、弹“全军 · 冲锋”）。</summary>
        public event System.Action<Order> OnOrder;

        /// <summary>手正靠着某支令牌或正拿着：这一捏归令牌，不交给战场。</summary>
        public bool Captures => hover >= 0 || held >= 0;
        /// <summary>哪只手靠着 / 拿着令牌（手指提示圈变金）；−1 没有。</summary>
        public int HoverHand { get; private set; } = -1;
        /// <summary>令牌靠着放时的中间位置（教学提示的幻影手从这里出发）。</summary>
        public Vector3 SlotTop(Order o) => restPos[(int)o] + restRot[(int)o] * Vector3.up * (TokenH * 0.6f);

        // 11-12 用户：令牌有点大、和挪沙盘的圆钮重叠 → 模型（建模时 15 cm）缩到 11 cm，三支挪到右圆钮（本地 x = −0.2）更右边
        const float ModelScale = 0.73f, TokenH = 0.15f * ModelScale;
        const float Spacing = 0.058f, GrabRadius = 0.045f, Margin = 0.01f, FirstX = -0.34f;
        const float Lean = 22f;               // 靠在木框上时偏离竖直的角度
        const float Gravity = 7.5f;           // 比真实重力小一点：桌面尺度上飞得看得清
        const float MaxSpeed = 3.5f, MinMove = 0.08f;
        const float StuckScale = 0.72f, StayTime = 2.6f, PopTime = 0.22f;

        readonly Transform[] tokens = new Transform[3];
        readonly GameObject[,] langs = new GameObject[3, 2];   // [令, 0 中 / 1 英]
        readonly Renderer[][] rends = new Renderer[3][];
        readonly Vector3[] restPos = new Vector3[3];
        readonly Quaternion[] restRot = new Quaternion[3];
        enum St { Rest, Held, Fly, Stuck, Gone, Pop, Back }
        readonly St[] st = new St[3];
        readonly float[] t0 = new float[3];
        readonly Vector3[] pos = new Vector3[3], vel = new Vector3[3], flyFrom = new Vector3[3];
        readonly Quaternion[] rotFrom = new Quaternion[3], rotTo = new Quaternion[3];
        readonly float[] flyDur = new float[3];
        readonly Vector3[] tiltAxis = new Vector3[3];
        int hover = -1, held = -1, heldHand = -1;
        Vector3 grabLocal; Quaternion heldRot; Vector3 swing;   // 捏住的那一点（令牌本地）、手里的朝向、摆动
        readonly System.Collections.Generic.Queue<(float t, Vector3 p)>[] hist = { new(), new() };
        MaterialPropertyBlock mpb;
        bool built;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), EmisId = Shader.PropertyToID("_EmissionColor");

        void Build()
        {
            built = true; mpb = new MaterialPropertyBlock();
            for (int i = 0; i < 3; i++)
            {
                var g = new GameObject("Order_" + Keys[i]).transform; g.SetParent(transform, false);
                var list = new System.Collections.Generic.List<Renderer>();
                for (int l = 0; l < 2; l++)
                {
                    var src = (l == 0 ? modelZh : modelEn)[i];
                    if (!src) continue;
                    var m = Instantiate(src, g, false); m.name = l == 0 ? "ZH" : "EN"; m.transform.localScale *= ModelScale;
                    foreach (var r in m.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterial = r.name.StartsWith("Inlay") ? gold : lacquer[i];
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        if (!r.name.StartsWith("Inlay")) list.Add(r);
                    }
                    langs[i, l] = m;
                }
                rends[i] = list.ToArray();
                tokens[i] = g; st[i] = St.Rest;
            }
        }

        /// <summary>每帧（WarGame）。fight = 交战中（只有交战时能拿令牌）；active = 显示令牌（布阵 / 交战、没暂停）。</summary>
        public void Tick(bool active, bool fight)
        {
            if (!built) Build();
            var board = game ? game.board : null;
            bool show = active && board && board.gameObject.activeInHierarchy;
            for (int i = 0; i < 3; i++) if (tokens[i].gameObject.activeSelf != show) tokens[i].gameObject.SetActive(show);
            if (!show)
            {
                // 暂停 / 结算时收起：拿着的、飞着的、插在地上的都直接回原处（否则状态卡在“拿着”，令牌再也回不来）
                hover = held = heldHand = -1; HoverHand = -1;
                for (int i = 0; i < 3; i++) { st[i] = St.Rest; tokens[i].localScale = Vector3.one; }
                return;
            }
            Layout(board);
            bool zh = Loc.Current == Loc.Lang.Zh;
            for (int i = 0; i < 3; i++)
                for (int l = 0; l < 2; l++)
                    if (langs[i, l] && langs[i, l].activeSelf != ((l == 0) == zh)) langs[i, l].SetActive((l == 0) == zh);

            var mr = game.mr;
            var hands = mr && mr.Active ? new[] { mr.Left, mr.Right } : new HandState[0];
            for (int h = 0; h < hands.Length; h++)
            {
                var q = hist[h];
                if (hands[h].tracked) q.Enqueue((Time.time, game.VirtualTip(hands[h].pinchPoint, out _)));
                while (q.Count > 0 && Time.time - q.Peek().t > 0.1f) q.Dequeue();
            }

            // ── 拿着：从捏住的那一点提着走，令牌像被提着一样随手摆
            if (held >= 0)
            {
                var hs = heldHand < hands.Length ? hands[heldHand] : default;
                if (hs.tracked && hs.pinch)
                {
                    var tip = game.VirtualTip(hs.pinchPoint, out _);
                    var v = HandVel(heldHand);
                    swing = Vector3.Lerp(swing, Vector3.ClampMagnitude(-v * 0.12f, 0.35f), 1f - Mathf.Exp(-Time.deltaTime * 10f));
                    // 目标朝向：竖着、字面朝玩家；手往哪边甩，下端就往反方向荡一点
                    var face = mr.Head - tip; face.y = 0;
                    var up = (Vector3.up - swing).normalized;
                    var target = Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? -face.normalized : Vector3.forward, up);
                    heldRot = Quaternion.Slerp(heldRot, target, 1f - Mathf.Exp(-Time.deltaTime * 9f));
                    tokens[held].SetPositionAndRotation(tip - heldRot * grabLocal, heldRot);
                }
                else Throw(held, heldHand);
            }

            // ── 靠近哪支（交战中、手里没拿着时）
            int best = -1, bestH = -1; float bd = 9f, second = 9f;
            if (fight && held < 0)
                for (int h = 0; h < hands.Length; h++)
                {
                    if (!hands[h].tracked) continue;
                    var tip = game.VirtualTip(hands[h].pinchPoint, out _);
                    for (int i = 0; i < 3; i++)
                    {
                        if (st[i] != St.Rest) continue;
                        var a = restPos[i] + restRot[i] * Vector3.up * (TokenH * 0.1f); var b = restPos[i] + restRot[i] * Vector3.up * TokenH;
                        float d = SegDist(tip, a, b);
                        if (d < bd) { second = bd; bd = d; best = i; bestH = h; } else if (d < second) second = d;
                    }
                }
            bool near = best >= 0 && bd < GrabRadius && second - bd > Margin;
            hover = near ? best : -1;
            HoverHand = held >= 0 ? heldHand : near ? bestH : -1;
            if (near && (bestH == 0 ? mr.PinchDownL : mr.PinchDownR)) Pick(best, bestH, hands[bestH]);

            // ── 各支令牌
            for (int i = 0; i < 3; i++)
            {
                var tt = tokens[i]; var col = Color.white; var glow = Color.black;
                switch (st[i])
                {
                    case St.Rest:
                    {
                        // 靠近时往外抬一点、微微亮
                        var lift = i == hover ? restRot[i] * Vector3.back * 0.008f : Vector3.zero;
                        tt.SetPositionAndRotation(restPos[i] + lift, restRot[i]); tt.localScale = Vector3.one;
                        if (!fight) col = new Color(0.6f, 0.6f, 0.6f, 1f);
                        else if (i == hover) glow = new Color(0.22f, 0.16f, 0.06f);
                        break;
                    }
                    case St.Held: glow = new Color(0.16f, 0.12f, 0.05f); break;
                    case St.Fly:
                    {
                        float dt = Time.deltaTime, u = Mathf.Clamp01((Time.time - t0[i]) / flyDur[i]);
                        var p1 = pos[i] + vel[i] * dt; vel[i] += Vector3.down * Gravity * dt;
                        tt.rotation = Quaternion.Slerp(rotFrom[i], rotTo[i], Mathf.SmoothStep(0f, 1f, u));
                        tt.localScale = Vector3.one * Mathf.Lerp(1f, StuckScale, u);
                        if (game.SweepBoard(pos[i], p1, out var hit) || u >= 1f)
                        {
                            var land = u >= 1f && !game.SweepBoard(pos[i], p1, out _) ? (game.DropOnBoard(p1) ?? p1) : hit;
                            tt.SetPositionAndRotation(land - Vector3.up * 0.006f, rotTo[i]); tt.localScale = Vector3.one * StuckScale;
                            st[i] = St.Stuck; t0[i] = Time.time; Landed(i);
                        }
                        else { pos[i] = p1; tt.position = p1; }
                        break;
                    }
                    case St.Stuck:
                    {
                        float u = Time.time - t0[i];
                        // 插进土里摇两下：倾角按阻尼振荡回到落地姿态
                        float wob = Mathf.Exp(-u * 7f) * Mathf.Sin(u * 28f) * 12f;
                        tt.rotation = Quaternion.AngleAxis(wob, tiltAxis[i]) * rotTo[i];
                        if (u > StayTime)
                        {
                            float k = Mathf.Clamp01((u - StayTime) / 0.25f);
                            tt.localScale = Vector3.one * StuckScale * (1f - k);
                            if (k >= 1f) { st[i] = St.Pop; t0[i] = Time.time; }
                        }
                        break;
                    }
                    case St.Back:
                    {
                        // 没扔上沙盘：沿短弧飞回原处
                        float u = Mathf.Clamp01((Time.time - t0[i]) / 0.3f), e = 1f - (1f - u) * (1f - u);
                        tt.SetPositionAndRotation(Vector3.Lerp(flyFrom[i], restPos[i], e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.03f, Quaternion.Slerp(rotFrom[i], restRot[i], e));
                        if (u >= 1f) { st[i] = St.Rest; if (game.sound) game.sound.Cue("hover"); }
                        break;
                    }
                    case St.Pop:
                    {
                        // 回到原处：从 0 长回原大（像变回去），不在空中飞
                        float u = Mathf.Clamp01((Time.time - t0[i]) / PopTime);
                        tt.SetPositionAndRotation(restPos[i], restRot[i]); tt.localScale = Vector3.one * (1f - (1f - u) * (1f - u));
                        if (u >= 1f) { st[i] = St.Rest; tt.localScale = Vector3.one; }
                        break;
                    }
                }
                foreach (var r in rends[i])
                {
                    r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, col * lacquer[i].GetColor(ColorId)); mpb.SetColor(EmisId, glow); r.SetPropertyBlock(mpb);
                }
            }
        }

        /// <summary>三支令牌的靠放位置：沙盘前沿木框外侧、偏右手，牌底在桌面上，上半截斜搭在木框上沿。</summary>
        void Layout(Transform board)
        {
            float bk = board.lossyScale.x / BoardMap.BoardScale;
            float tableY = board.position.y - (game.tray ? game.tray.lift : 0.052f) * bk;
            var away = -board.forward; away.y = 0; away.Normalize();          // 从玩家往沙盘里
            var right = -board.right; right.y = 0; right.Normalize();         // 玩家右手方向
            // 木框外沿（本地 z = 0.552、上沿 y = 0.012，11-11 量过：外沿世界 z = 0.635、顶 0.014、桌面 −0.052）
            var edge0 = board.TransformPoint(new Vector3(FirstX, 0.0122f, 0.552f));
            float rise = Mathf.Max(0.02f, edge0.y - tableY);
            float back = rise * Mathf.Tan(Lean * Mathf.Deg2Rad);
            for (int i = 0; i < 3; i++)
            {
                var edge = edge0 + right * (i * Spacing);
                var bottom = new Vector3(edge.x, tableY, edge.z) - away * (back + 0.004f);
                var axis = (edge + Vector3.up * 0.002f - bottom).normalized;
                var fwd = Vector3.ProjectOnPlane(away, axis).normalized;            // 本地 +Z 朝沙盘里 → 字面（−Z）朝玩家
                restPos[i] = bottom; restRot[i] = Quaternion.LookRotation(fwd, axis);
            }
        }

        void Pick(int i, int h, HandState hs)
        {
            held = i; heldHand = h; st[i] = St.Held; hover = -1; swing = Vector3.zero;
            var tip = game.VirtualTip(hs.pinchPoint, out _);
            // 从捏住的地方提起：记下捏点在令牌本地的位置（限制在牌身范围里），不把令牌瞬移到手上
            var lp = Quaternion.Inverse(restRot[i]) * (tip - restPos[i]);
            lp.x = Mathf.Clamp(lp.x, -0.015f, 0.015f); lp.y = Mathf.Clamp(lp.y, TokenH * 0.25f, TokenH * 0.95f); lp.z = 0f;
            grabLocal = lp; heldRot = restRot[i];
            if (game.sound) game.sound.Cue("select");
            Debug.Log("[WT-IN] order-pick " + Keys[i]);
        }

        Vector3 HandVel(int h)
        {
            var q = hist[h];
            if (q.Count < 2) return Vector3.zero;
            var arr = q.ToArray(); float dt = arr[^1].t - arr[0].t;
            return dt > 0.02f ? (arr[^1].p - arr[0].p) / dt : Vector3.zero;
        }

        void Throw(int i, int h)
        {
            held = -1; heldHand = -1;
            var tt = tokens[i];
            var v = Vector3.ClampMagnitude(HandVel(h), MaxSpeed);
            var p0 = tt.position;   // 底尖（先着地的地方）
            flyFrom[i] = tt.position; rotFrom[i] = tt.rotation; t0[i] = Time.time;
            float moved = Vector3.Distance(tt.position, restPos[i]);
            var over = game.DropOnBoard(p0);
            // 预测落点（按真实抛物线）
            Vector3? land = Predict(p0, v, out float tFly);
            if (!land.HasValue && moved > MinMove)
            {
                // 不用瞄准：落到沙盘上离预测落点（没有就离手）最近的地方
                var b = game.board; float hx = BoardMap.HalfX * BoardMap.Yard * 0.88f, hz = BoardMap.HalfZ * BoardMap.Yard * 0.88f;
                var guess = p0 + new Vector3(v.x, 0, v.z) * 0.3f;
                var lp = b.InverseTransformPoint(guess); lp.x = Mathf.Clamp(lp.x, -hx, hx); lp.z = Mathf.Clamp(lp.z, -hz, hz);
                land = game.DropOnBoard(b.TransformPoint(lp));
                if (land.HasValue)
                {
                    // 反算一条落到那里的抛物线（飞行时间按距离，0.35–0.8 秒）
                    var d = land.Value - p0; tFly = Mathf.Clamp(new Vector2(d.x, d.z).magnitude / 1.6f, 0.35f, 0.8f);
                    v = d / tFly + 0.5f * Gravity * tFly * Vector3.up;
                }
            }
            if (!land.HasValue && over.HasValue) { land = over; v = Vector3.zero; tFly = Mathf.Sqrt(2f * Mathf.Max(0.01f, p0.y - over.Value.y) / Gravity); }
            if (land.HasValue)
            {
                // 落地姿态：底尖朝下插进土里、顺着飞行方向往前倾 15°、字面朝玩家
                var dir = new Vector3(v.x, 0, v.z); if (dir.sqrMagnitude < 1e-4f) dir = land.Value - game.mr.Head; dir.y = 0; dir.Normalize();
                tiltAxis[i] = Vector3.Cross(Vector3.up, dir);
                var face = land.Value - game.mr.Head; face.y = 0;
                rotTo[i] = Quaternion.AngleAxis(15f, tiltAxis[i]) * Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward, Vector3.up);
                pos[i] = tt.position; vel[i] = v; flyDur[i] = Mathf.Max(0.12f, tFly); st[i] = St.Fly;
            }
            else { st[i] = St.Back; }
            Debug.Log($"[WT-IN] order-throw {Keys[i]} land={(land.HasValue ? "board" : "back")} v={v.magnitude:0.00} moved={moved:0.00}");
        }

        /// <summary>按抛物线一步步往前推，找第一次碰到沙盘地形的点（最多 1.5 秒；掉到桌面以下就算没落在沙盘上）。</summary>
        Vector3? Predict(Vector3 p, Vector3 v, out float t)
        {
            const float dt = 1f / 60f; t = 0f;
            float floor = game.board.position.y - 0.2f;
            for (int k = 0; k < 90; k++)
            {
                var p1 = p + v * dt; v += Vector3.down * Gravity * dt; t += dt;
                if (game.SweepBoard(p, p1, out var hit)) return hit;
                if (p1.y < floor) return null;
                p = p1;
            }
            return null;
        }

        void Landed(int i)
        {
            if (game.sound) game.sound.Cue("stamp");
            if (game.hitFx) game.hitFx.Dust(tokens[i].position, game.board.lossyScale.x / BoardMap.BoardScale);
            OnOrder?.Invoke((Order)i);
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
