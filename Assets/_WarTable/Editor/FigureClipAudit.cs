using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 棋子动作的“自己穿自己”巡检 + 手臂修正角度的自动拟合（11-13 用户：深空的兵种要注意动作是否适合他现在的身躯——
    /// 重型机甲的胸、肩、前臂比人厚一大圈，原来给人做的动作套上去，手臂会插进胸口 / 大腿，盾和武器会埋进身体）。
    /// 量法：把这个兵种“实际会播的动作”各取几帧，烘焙蒙皮网格，按“主要受哪根骨头带动”把顶点分成 躯干（含头、腿、上臂）/ 左前臂和手 / 右前臂和手 / 左腿 / 右腿；
    /// 然后数：前臂和手的顶点有多少落在躯干里面、左腿的顶点有多少落在右腿里面、盾和武器的顶点有多少落在躯干里面。
    /// “在里面”= 从这个点朝 +X 打一条射线，穿过躯干三角面的次数是奇数（朝 +Y 再打一条，两条不一致就朝 +Z 打第三条定——躯干在肘部 / 髋部是开口的）。
    /// 同一个兵种的中古版当基线一起算：动作本身就贴得近的（抱拳、持弓）基线也有数，只看比基线多出来的。
    /// 骑手：游戏里下半身一直是骑姿、上半身才播动作，量的时候也这样摆。
    /// 用法（cs.py）：FigureClipAudit.Report("space", ids) 看问题；FigureClipAudit.Fit("space", ids) 拟合角度写进 ArtSource/Space/bodyfit.json，然后重建预制体。
    /// </summary>
    public static class FigureClipAudit
    {
        public struct Row { public string id, clip; public float armL, armR, legs, shield, weapon, w; }

        /// <summary>这个兵种在游戏里实际会播的动作和权重（常驻的姿势权重大：待命、走、端枪；倒地之类的小）。</summary>
        public static Dictionary<string, float> Used(string id, string era)
        {
            var g = DemoFigures.GearOf(id, era); var d = new Dictionary<string, float>();
            void Add(float w, params string[] cs) { foreach (var c in cs) d[c] = Mathf.Max(w, d.TryGetValue(c, out var o) ? o : 0f); }
            void Melee(string ready, float w)
            {
                if (ready == "Ready1H") Add(w, "Swing1H_R", "Swing1H_L", "Chop1H", "Thrust1H", "Parry1H");
                else if (ready == "Ready2H") Add(w, "Swing2H_R", "Swing2H_L", "Chop2H", "Thrust2H", "Parry2H");
                else if (ready == "Ready2HL") Add(w, "SwingPole_R", "SwingPole_L", "ChopPole", "ThrustPole", "Parry2HL");
            }
            bool mounted = g.mount != null, rifleBows = era == "modern" || era == "space";
            string baseReady = DemoFigures.GearOf(id, null).ready;
            if (!mounted) { Add(3f, "Walk"); Add(2f, "Run"); Add(1f, "Walkbackwards"); }   // 骑手的上半身不播走 / 跑
            Add(0.5f, "CombatWound", "CombatCritical", "Kick", "Victory"); Add(0.1f, "Death", "DeathBack", "Knockdown");
            if (g.shield != null) { Add(1f, "ShieldBlock"); Add(0.5f, "ShieldBash"); }
            string ready = baseReady == "ReadyBow" && rifleBows ? "ReadyRifle" : baseReady;
            if (id == "cl_wolfarcher") ready = rifleBows ? "ReadyRifle" : "ReadyBow";
            if (mounted) { Add(4f, "Mount"); if (id == "kd_knight") Add(2f, "Couch"); if (id == "cl_wolfrider") { Add(3f, "Ready2HL"); Melee("Ready2HL", 1f); } }
            if (ready != "Mount") Add(4f, ready);
            if (ready == "ReadyRifle") { Add(3f, "HoldRifle"); Add(1f, "AttackRifle", "LoadRifle"); }
            if (ready == "ReadyBow") Add(2f, "HoldBow", "LoadBow", "AttackBow");
            if (ready == "HoldThrown") Add(1f, "LoadThrown", "AttackThrown");
            Melee(ready, 1f);
            // 副武器（短剑 / 手斧 = 单手；长柄斧 = 双手）
            if (id == "kd_spearman" || id == "kd_archer" || id == "kd_crossbow" || id == "kd_knight" || id == "cl_skirmisher" || id == "cl_pikeman" || id == "cl_hunter" || id == "cl_wolfarcher") { Add(1f, "Ready1H"); Add(0.3f, "Sheath"); Melee("Ready1H", 0.7f); }
            if (id == "cl_wolfrider") { Add(0.7f, "Ready2H"); Add(0.3f, "Sheath"); Melee("Ready2H", 0.5f); }
            return d;
        }

        /// <summary>一个兵种的测量现场：实例、低模、顶点分类都只准备一次，之后可以换着角度反复量。</summary>
        public class Session : System.IDisposable
        {
            public readonly string id, era; GameObject inst; Transform rider; SkinnedMeshRenderer smr; int[] cat, tris; List<int> bodyTri = new(), rlegTri = new();
            int[] ptsL, ptsR, ptsLeg; MeshFilter[] shieldMf, weaponMf; Mesh baked = new Mesh(); bool mounted;
            Dictionary<string, AnimationClip> clips = new(); public WarTable.Game.BodyFit fit;
            List<(Transform t, Vector3 p, Quaternion r)> seat = new();
            public bool Ok => inst;
            public GameObject Instance => inst;

            public Session(string id, string era)
            {
                this.id = id; this.era = era;
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(DemoFigures.PrefabPath(id, era));
                if (!pf) return;
                inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); inst.transform.position = new Vector3(40f, 0f, 0f);
                rider = DemoFigures.Find(inst.transform, "Rider");
                smr = rider.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => r.sharedMesh.vertexCount).First();   // 低模，够用也快
                var mesh = smr.sharedMesh; var bones = smr.bones; var bw = mesh.boneWeights; tris = mesh.triangles;
                // 顶点分类：0 躯干（含头、上臂）1 左前臂 + 手 2 右前臂 + 手 3 左腿 4 右腿
                int Cat(string b)
                {
                    b = b.Substring(b.IndexOf(':') + 1);
                    if (b.StartsWith("LeftForeArm") || b.StartsWith("LeftHand")) return 1;
                    if (b.StartsWith("RightForeArm") || b.StartsWith("RightHand")) return 2;
                    if (b.StartsWith("LeftUpLeg") || b.StartsWith("LeftLeg") || b.StartsWith("LeftFoot") || b.StartsWith("LeftToe")) return 3;
                    if (b.StartsWith("RightUpLeg") || b.StartsWith("RightLeg") || b.StartsWith("RightFoot") || b.StartsWith("RightToe")) return 4;
                    return 0;
                }
                var boneCat = bones.Select(b => b ? Cat(b.name) : 0).ToArray();
                cat = new int[bw.Length];
                for (int i = 0; i < bw.Length; i++) cat[i] = boneCat[bw[i].boneIndex0];
                var gear = DemoFigures.GearOf(id, era); mounted = gear.mount != null;
                // 躯干三角面（三个顶点都不是前臂 / 手）；腿算躯干的一部分（手会插进大腿）。右腿三角面单独一份，给“左腿插右腿”用
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = cat[tris[i]], b = cat[tris[i + 1]], c = cat[tris[i + 2]];
                    if (a != 1 && a != 2 && b != 1 && b != 2 && c != 1 && c != 2) { bodyTri.Add(tris[i]); bodyTri.Add(tris[i + 1]); bodyTri.Add(tris[i + 2]); }
                    if (a == 4 && b == 4 && c == 4) { rlegTri.Add(tris[i]); rlegTri.Add(tris[i + 1]); rlegTri.Add(tris[i + 2]); }
                }
                int[] Pick(int c, int max) { var all = Enumerable.Range(0, cat.Length).Where(i => cat[i] == c).ToArray(); int step = Mathf.Max(1, all.Length / max); return all.Where((_, k) => k % step == 0).ToArray(); }
                ptsL = Pick(1, 140); ptsR = Pick(2, 140); ptsLeg = Pick(3, 140);
                // 盾和武器：挂在骨头上的静态网格（只算当前显示的那件）
                MeshFilter[] Static(string point) { var at = DemoFigures.Find(rider, point); return at ? at.GetComponentsInChildren<MeshFilter>(false).Where(m => !m.name.EndsWith("_LOD1") && m.sharedMesh).ToArray() : new MeshFilter[0]; }
                shieldMf = gear.shield != null ? Static("ShieldL") : new MeshFilter[0];
                weaponMf = Static(gear.grip);
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath($"{DemoFigures.Dir}/troops/{DemoFigures.ModelOf(id, era)}.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")))
                    clips[clip.name.Substring(clip.name.LastIndexOf('|') + 1)] = clip;
                fit = rider.GetComponent<WarTable.Game.BodyFit>();
                // 骑手：游戏里下半身一直是骑姿（Mount），上半身才播动作 → 记下骑姿时不归上半身管的骨头，量每段动作时放回去
                if (mounted && clips.TryGetValue("Mount", out var mc))
                {
                    mc.SampleAnimation(rider.gameObject, 0.2f);
                    var spine = DemoFigures.Find(rider, "mixamorig:Spine");
                    foreach (var t in rider.GetComponentsInChildren<Transform>()) if (t != rider && !(spine && (t == spine || t.IsChildOf(spine)))) seat.Add((t, t.localPosition, t.localRotation));
                }
            }

            /// <summary>临时改手臂修正角度（没有组件就现加一个）。</summary>
            public void SetFit(float[] v)
            {
                if (!fit)
                {
                    // 现加：坐标轴要在绑定姿势下记，另起一个干净的实例来量
                    fit = rider.gameObject.AddComponent<WarTable.Game.BodyFit>();
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(DemoFigures.PrefabPath(id, era)); var tmp = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    var tr = DemoFigures.Find(tmp.transform, "Rider");
                    var tf = tr.gameObject.AddComponent<WarTable.Game.BodyFit>(); tf.Bind(tr, DemoFigures.Find(tr, "mixamorig:LeftArm"), DemoFigures.Find(tr, "mixamorig:RightArm"));
                    fit.armL = DemoFigures.Find(rider, "mixamorig:LeftArm"); fit.armR = DemoFigures.Find(rider, "mixamorig:RightArm"); fit.chest = fit.armL.parent.parent;
                    fit.upInChest = tf.upInChest; fit.rightInChest = tf.rightInChest; fit.fwdInChest = tf.fwdInChest;
                    Object.DestroyImmediate(tmp);
                }
                fit.abductL = v[0]; fit.flexL = v[1]; fit.abductR = v[2]; fit.flexR = v[3];
            }

            /// <summary>把动作摆到某一刻（含骑姿下半身和手臂修正）。量和截图都用它。</summary>
            public bool PoseAt(string clip, float t01)
            {
                if (!clips.TryGetValue(clip, out var c)) return false;
                c.SampleAnimation(rider.gameObject, c.length * t01);
                foreach (var (t, p, r) in seat) { t.localPosition = p; t.localRotation = r; }
                if (fit) fit.Apply();
                return true;
            }

            public Row Measure(string clip, int frames)
            {
                var row = new Row { id = id, clip = clip };
                for (int f = 0; f < frames; f++)
                {
                    if (!PoseAt(clip, (f + 0.5f) / frames)) return row;
                    smr.BakeMesh(baked, true);
                    var l2w = smr.transform.localToWorldMatrix; var v = baked.vertices;
                    for (int i = 0; i < v.Length; i++) v[i] = l2w.MultiplyPoint3x4(v[i]);
                    var body = new Solid(v, bodyTri); var rleg = new Solid(v, rlegTri);
                    float Frac(IEnumerable<Vector3> pts, Solid s) { int n = 0, hit = 0; foreach (var p in pts) { n++; if (s.Inside(p)) hit++; } return n == 0 ? 0f : (float)hit / n; }
                    IEnumerable<Vector3> Of(MeshFilter[] mfs) { foreach (var mf in mfs) { var m = mf.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; int step = Mathf.Max(1, vs.Length / 160); for (int i = 0; i < vs.Length; i += step) yield return m.MultiplyPoint3x4(vs[i]); } }
                    // 取各帧的平均（一闪而过的不如一直穿着的严重）
                    row.armL += Frac(ptsL.Select(i => v[i]), body) / frames;
                    row.armR += Frac(ptsR.Select(i => v[i]), body) / frames;
                    if (!mounted) row.legs += Frac(ptsLeg.Select(i => v[i]), rleg) / frames;
                    row.shield += Frac(Of(shieldMf), body) / frames;
                    row.weapon += Frac(Of(weaponMf), body) / frames;
                }
                return row;
            }

            public List<Row> MeasureUsed(int frames)
            {
                var o = new List<Row>();
                foreach (var kv in Used(id, era)) { if (!clips.ContainsKey(kv.Key)) continue; var r = Measure(kv.Key, frames); r.w = kv.Value; o.Add(r); }
                return o;
            }

            public void Dispose() { if (baked) Object.DestroyImmediate(baked); if (inst) Object.DestroyImmediate(inst); }
        }

        /// <summary>加权总分：左臂一侧（左前臂 + 盾）、右臂一侧（右前臂 + 武器）。</summary>
        static (float l, float r) Score(List<Row> rows)
        {
            float w = rows.Sum(x => x.w); if (w <= 0f) return (0f, 0f);
            return (rows.Sum(x => x.w * (x.armL + 0.7f * x.shield)) / w, rows.Sum(x => x.w * (x.armR + 0.7f * x.weapon)) / w);
        }

        /// <summary>
        /// 给一批兵种拟合手臂修正角度，写进 ArtSource/&lt;风格&gt;/bodyfit.json（DemoFigures 搭预制体时读）。
        /// 左右臂互不影响（躯干实体里不含前臂），所以一轮试角度同时给两条臂用，左右各取自己最好的。角度大了姿势会变形，所以每度有一点代价。
        /// </summary>
        public static string Fit(string era, string[] ids, int frames = 3)
        {
            string path = $"ArtSource/{char.ToUpper(era[0]) + era.Substring(1)}/bodyfit.json";
            var table = new Dictionary<string, float[]>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (System.IO.File.Exists(path))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(path), "\"([a-z_]+)\"\\s*:\\s*\\[([^\\]]*)\\]"))
                    table[m.Groups[1].Value] = m.Groups[2].Value.Split(',').Select(x => float.Parse(x.Trim(), inv)).ToArray();
            var sb = new System.Text.StringBuilder();
            float[] abds = { 0, 5, 10, 15, 20, 25 }, flxs = { 0, 6, 12, 18 };
            const float CostAbd = 0.0010f, CostFlx = 0.0015f;   // 每度的代价（第一遍 0.003 / 0.004 太重：张 15° 要先省下 4.5 个百分点才划算，结果大多数兵种一度都没动）
            foreach (var id in ids)
            {
                using var s = new Session(id, era); if (!s.Ok) { sb.AppendLine(id + " 没有预制体"); continue; }
                s.SetFit(new float[4]); var (l0, r0) = Score(s.MeasureUsed(frames));
                float bl = l0, br = r0; float[] best = new float[4];
                foreach (var a in abds) foreach (var f in flxs)
                {
                    if (a == 0 && f == 0) continue;
                    s.SetFit(new[] { a, f, a, f }); var (l, r) = Score(s.MeasureUsed(frames));
                    float cost = a * CostAbd + f * CostFlx;
                    if (l + cost < bl) { bl = l + cost; best[0] = a; best[1] = f; }
                    if (r + cost < br) { br = r + cost; best[2] = a; best[3] = f; }
                }
                s.SetFit(best); var (l1, r1) = Score(s.MeasureUsed(frames));
                table[DemoFigures.ModelOf(id, era)] = best;
                sb.AppendLine($"{id}: 左 张 {best[0]}° 送 {best[1]}°（{l0 * 100:F1}% → {l1 * 100:F1}%）  右 张 {best[2]}° 送 {best[3]}°（{r0 * 100:F1}% → {r1 * 100:F1}%）");
            }
            System.IO.File.WriteAllText(path, "{\n" + string.Join(",\n", table.OrderBy(k => k.Key).Select(k => $"  \"{k.Key}\": [{string.Join(", ", k.Value.Select(x => x.ToString(inv)))}]")) + "\n}\n");
            return sb.ToString();
        }

        /// <summary>一组三角面围成的实体：射线奇偶法判点在不在里面。</summary>
        class Solid
        {
            readonly Vector3[] a, b, c; readonly Vector3 lo, hi;
            public Solid(Vector3[] v, List<int> t)
            {
                int n = t.Count / 3; a = new Vector3[n]; b = new Vector3[n]; c = new Vector3[n];
                lo = Vector3.one * 1e9f; hi = -lo;
                for (int i = 0; i < n; i++)
                {
                    a[i] = v[t[i * 3]]; b[i] = v[t[i * 3 + 1]]; c[i] = v[t[i * 3 + 2]];
                    lo = Vector3.Min(lo, Vector3.Min(a[i], Vector3.Min(b[i], c[i]))); hi = Vector3.Max(hi, Vector3.Max(a[i], Vector3.Max(b[i], c[i])));
                }
            }
            public bool Inside(Vector3 p)
            {
                if (a.Length == 0 || p.x < lo.x || p.y < lo.y || p.z < lo.z || p.x > hi.x || p.y > hi.y || p.z > hi.z) return false;
                bool x = Odd(p, 0); bool y = Odd(p, 1);
                if (x == y) return x;
                return Odd(p, 2);
            }
            // 朝第 ax 根轴的正方向打射线，数穿过的三角面
            bool Odd(Vector3 p, int ax)
            {
                int u = (ax + 1) % 3, w = (ax + 2) % 3; float pu = p[u], pw = p[w], pa = p[ax]; int n = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    float au = a[i][u], bu = b[i][u], cu = c[i][u];
                    if ((au < pu && bu < pu && cu < pu) || (au > pu && bu > pu && cu > pu)) continue;
                    float aw = a[i][w], bw_ = b[i][w], cw = c[i][w];
                    if ((aw < pw && bw_ < pw && cw < pw) || (aw > pw && bw_ > pw && cw > pw)) continue;
                    // 投影到 (u, w) 平面上的重心坐标
                    float d = (bw_ - cw) * (au - cu) + (cu - bu) * (aw - cw);
                    if (Mathf.Abs(d) < 1e-14f) continue;
                    float l0 = ((bw_ - cw) * (pu - cu) + (cu - bu) * (pw - cw)) / d, l1 = ((cw - aw) * (pu - cu) + (au - cu) * (pw - cw)) / d, l2 = 1f - l0 - l1;
                    if (l0 < 0f || l1 < 0f || l2 < 0f) continue;
                    if (l0 * a[i][ax] + l1 * b[i][ax] + l2 * c[i][ax] > pa) n++;
                }
                return (n & 1) == 1;
            }
        }

        /// <summary>报告：每个兵种在“实际用到的动作”上，比基线（中古版同一段动作）多出 minExtra 以上的项，按 权重 × 多出来的量 排。数字 = 各帧平均有多少比例的点在身体里。</summary>
        public static string Report(string era, string[] ids, int frames = 4, float minExtra = 0.06f)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var id in ids)
            {
                using var s = new Session(id, era); using var b0 = new Session(id, null);
                if (!s.Ok) { sb.AppendLine(id + " 没有预制体"); continue; }
                var cur = s.MeasureUsed(frames); var bas = b0.Ok ? b0.MeasureUsed(frames).ToDictionary(r => r.clip) : new Dictionary<string, Row>();
                var (sl, sr) = Score(cur);
                var lines = new List<(float sev, string txt)>();
                foreach (var r in cur)
                {
                    bas.TryGetValue(r.clip, out var b);
                    void Add(string what, float now, float was) { if (now - was >= minExtra) lines.Add(((now - was) * r.w, $"{r.clip} {what} {now * 100:F0}%（基线 {was * 100:F0}%）")); }
                    Add("左臂进身体", r.armL, b.armL); Add("右臂进身体", r.armR, b.armR); Add("两腿互穿", r.legs, b.legs); Add("盾进身体", r.shield, b.shield); Add("武器进身体", r.weapon, b.weapon);
                }
                sb.AppendLine($"{id} 总分 左 {sl * 100:F1}% 右 {sr * 100:F1}%（{lines.Count} 项）: " + string.Join("；", lines.OrderByDescending(l => l.sev).Take(8).Select(l => l.txt)));
            }
            return sb.ToString();
        }
    }
}
