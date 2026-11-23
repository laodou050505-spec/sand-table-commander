using System.Collections.Generic;
using UnityEngine;
using WarTable.Sim;

namespace WarTable.Game
{
    /// <summary>
    /// 战场的显示：每个模拟士兵一枚棋子（Prefabs/Figures/&lt;兵种&gt;），飞行 / 插地的箭和投矛（对象池）。
    /// 落地用地形网格的射线（Terrain 上的 MeshCollider），不是高度函数。
    /// </summary>
    public class BattleView : MonoBehaviour
    {
        [System.Serializable] public class TroopPrefab { public string troop; public GameObject prefab; }
        public TroopPrefab[] prefabs;
        /// <summary>哥特时代的兵种（Settings.Era == 1）；某个兵种缺预制体时退回原版。</summary>
        public TroopPrefab[] prefabsGothic;
        /// <summary>未来时代（Settings.Era = 3）的棋子；缺的兵种退回原版。</summary>
        public TroopPrefab[] prefabsFuture;
        /// <summary>现代时代（Settings.Era = 2，11-13）的棋子；缺的兵种退回原版。</summary>
        public TroopPrefab[] prefabsModern;
        /// <summary>深空风格（下标 4）的兵种预制体。</summary>
        public TroopPrefab[] prefabsSpace;
        /// <summary>深空的弹道：我军青蓝等离子、敌军琥珀焊枪火（复用光束预制体）。</summary>
        public Material boltMatSpaceOurs, boltMatSpaceTheirs;
        public GameObject arrowPrefab, javelinPrefab;
        /// <summary>未来时代（11-12）：箭 / 标枪都画成一道光束（细长发光柱），我军天青、敌军紫；命中后消失，不插在地上。</summary>
        public GameObject boltPrefab; public Material boltMatOurs, boltMatFoe;
        /// <summary>现代时代：枪弹画成一道暖黄的曳光（同一个光束预制体、两边同色）；标枪（钢筋投矛）照旧用标枪模型。</summary>
        public Material boltMatTracer;
        readonly List<GameObject> boltPool = new(); readonly List<Renderer> boltR = new(); readonly List<int> boltTeam = new();
        public MeshCollider terrainCollider;

        Battle battle;
        Transform armies, missiles;
        readonly Dictionary<int, FigureView> views = new();
        readonly Dictionary<string, GameObject> byTroop = new();
        readonly List<GameObject> arrowPool = new(), javelinPool = new();
        Transform board;

        public IReadOnlyDictionary<int, FigureView> Views => views;

        /// <summary>某个风格下某个兵种的棋子预制体（该风格没有就退回原版）。指挥台的风格小人用。</summary>
        public GameObject PrefabFor(int era, string troop)
        {
            var set = era == 1 ? prefabsGothic : era == 2 ? prefabsModern : era == 3 ? prefabsFuture : era == 4 ? prefabsSpace : null;
            if (set != null) foreach (var p in set) if (p.troop == troop && p.prefab) return p.prefab;
            foreach (var p in prefabs) if (p.troop == troop) return p.prefab;
            return null;
        }

        /// <summary>
        /// 戳兵互动里“朝你放一发”：一支只有样子的箭 / 光束 / 投矛从这个兵飞向玩家的手指（0.25–0.6 秒），到了就消失。不进模拟，不伤人。
        /// </summary>
        public void FireAt(FigureView v, Vector3 target)
        {
            if (!missiles || v == null || !v.Root || !isActiveAndEnabled) return;
            StartCoroutine(Fly(v, target));
        }

        System.Collections.IEnumerator Fly(FigureView v, Vector3 target)
        {
            var a = v.Agent; var w = AgentOps.WeaponOf(a);
            int era = Settings.Era;
            bool thrown = w.Cls == WeaponClass.Thrown;
            bool bolt = !thrown && boltPrefab && (era == 3 || era == 4 && boltMatSpaceOurs || era == 2 && boltMatTracer);
            var pf = thrown ? javelinPrefab : bolt ? boltPrefab : arrowPrefab;
            if (!pf) yield break;
            var go = Instantiate(pf, missiles, false);
            if (bolt)
            {
                var m = era == 2 ? boltMatTracer : era == 4 ? (a.Team == 0 ? boltMatSpaceOurs : boltMatSpaceTheirs) : (a.Team == 0 ? boltMatOurs : boltMatFoe);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) if (m) r.sharedMaterial = m;
            }
            float k = board.lossyScale.x;
            var from = v.Root.transform.position + board.up * (1.5f * BoardMap.Yard * k);
            float dist = Vector3.Distance(from, target), dur = Mathf.Clamp(dist / (0.9f * k), 0.25f, 0.6f);
            float arc = bolt ? 0f : dist * 0.12f;
            var prev = from;
            for (float t = 0f; t < dur && go; t += Time.deltaTime)
            {
                float u = t / dur;
                var p = Vector3.Lerp(from, target, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * arc);
                var d = p - prev;
                go.transform.position = p;
                if (d.sqrMagnitude > 1e-10f) go.transform.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);   // 箭 / 光束的约定：沿 −Z 朝前
                prev = p;
                yield return null;
            }
            if (go) Destroy(go);
        }

        /// <summary>两军整体缩放（换风格时旧的缩下去、新的长出来）。</summary>
        public void ArmiesScale(float k) { if (armies) armies.localScale = Vector3.one * k; }

        public void Build(Battle b, Transform boardRoot)
        {
            Clear();
            battle = b;
            board = boardRoot;
            byTroop.Clear();
            foreach (var p in prefabs) byTroop[p.troop] = p.prefab;
            var eraSet = Settings.Era == 1 ? prefabsGothic : Settings.Era == 2 ? prefabsModern : Settings.Era == 3 ? prefabsFuture : Settings.Era == 4 ? prefabsSpace : null;
            if (eraSet != null) foreach (var p in eraSet) if (p.prefab) byTroop[p.troop] = p.prefab;
            armies = new GameObject("Armies").transform;
            armies.SetParent(boardRoot, false);
            missiles = new GameObject("Missiles").transform;
            missiles.SetParent(boardRoot, false);
            foreach (var a in b.Agents) Spawn(a);
        }

        public void Clear()
        {
            foreach (var v in views.Values) v.Destroy();
            views.Clear();
            arrowPool.Clear(); javelinPool.Clear(); boltPool.Clear(); boltR.Clear(); boltTeam.Clear();
            if (armies) Destroy(armies.gameObject);
            if (missiles) Destroy(missiles.gameObject);
            FigureView.Victor = null;
        }

        void Spawn(Agent a)
        {
            if (views.ContainsKey(a.Id)) return;
            // 11-20 新敌种没有自己的模型：借 Troop.Model 那个兵种的棋子（放大 / 染色以示区别，玩法做完再统一建模）
            if ((!byTroop.TryGetValue(a.Troop.Id, out var pf) || !pf) && (!byTroop.TryGetValue(a.Troop.Model, out pf) || !pf)) return;
            var v = new FigureView(a, pf, armies, Ground);
            views[a.Id] = v;
            if (a.Troop.ViewScale != 1) v.Root.transform.localScale *= (float)a.Troop.ViewScale;
            if (a.Troop.Special == "sapper")
            {
                // 爆破兵：整个人偏橙红（身上绑着炸药的感觉），一眼和狂战士分开
                var mpb = new MaterialPropertyBlock();
                foreach (var r in v.Root.GetComponentsInChildren<Renderer>(true))
                {
                    r.GetPropertyBlock(mpb);
                    var baseCol = r.sharedMaterial && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.white;
                    mpb.SetColor("_BaseColor", baseCol * new Color(1.35f, 0.72f, 0.42f, 1f)); r.SetPropertyBlock(mpb);
                }
            }
        }

        /// <summary>地形网格上 Unity 本地坐标 (x, z) 码处的落点（本地米）和法线（本地）。</summary>
        public (Vector3 pos, Vector3 up) Ground(float ux, float uz)
        {
            var wp = board.TransformPoint(new Vector3(ux, 30f, uz) * BoardMap.Yard);
            if (terrainCollider && terrainCollider.Raycast(new Ray(wp, -board.up), out var hit, 5f))
                return (board.InverseTransformPoint(hit.point), board.InverseTransformDirection(hit.normal).normalized);
            return (new Vector3(ux, BoardMap.Height(ux, uz), uz) * BoardMap.Yard, Vector3.up);
        }

        /// <summary>每帧（模拟步进之后）：处理刷兵事件、更新所有棋子和飞行物。</summary>
        public void Sync(float dt, List<BattleEvent> events)
        {
            if (battle == null) return;
            if (events != null)
                foreach (var e in events)
                    if (e is SpawnEvent s) { var a = battle.Get(s.AgentId); if (a != null) Spawn(a); }
            if (battle.Winner.HasValue) FigureView.Victor = battle.Winner;
            foreach (var v in views.Values) v.Update(dt);
            SyncMissiles();
        }

        void SyncMissiles()
        {
            int na = 0, nj = 0, nb = 0; bool tracer = Settings.Era == 2 && boltMatTracer; bool space = Settings.Era == 4 && boltMatSpaceOurs; bool bolts = (Settings.Era == 3 || tracer || space) && boltPrefab;
            foreach (var p in battle.Projectiles)
            {
                if (p.Taken) continue;
                if (bolts && !(tracer && p.Kind == "javelin"))
                {
                    if (p.Stuck) continue;   // 光束命中就没了
                    // 没射中的会一直飞出沙盘、飞到一米多高（模拟里不删）；木箭太小看不出来，发光的光束很显眼 → 出了沙盘范围就不画
                    if (p.Y > 30 || System.Math.Abs(p.X) > BoardMap.HalfX + 2 || System.Math.Abs(p.Z) > BoardMap.HalfZ + 2) continue;
                    if (nb >= boltPool.Count) { var bg = Instantiate(boltPrefab, missiles, false); boltPool.Add(bg); boltR.Add(bg.GetComponentInChildren<Renderer>()); boltTeam.Add(-1); }
                    var bm = boltPool[nb];
                    if (!bm.activeSelf) bm.SetActive(true);
                    int bk = tracer ? 2 : p.Team;
                    if (boltTeam[nb] != bk) { boltTeam[nb] = bk; boltR[nb].sharedMaterial = tracer ? boltMatTracer : space ? (p.Team == WarTable.Command.BattleCommand.Me ? boltMatSpaceOurs : boltMatSpaceTheirs) : p.Team == WarTable.Command.BattleCommand.Me ? boltMatOurs : boltMatFoe; }
                    bm.transform.localPosition = new Vector3(-(float)p.X, (float)p.Y, (float)p.Z) * BoardMap.Yard;
                    var bv = new Vector3(-(float)p.Vx, (float)p.Vy, (float)p.Vz);
                    if (bv.sqrMagnitude > 1e-6f) bm.transform.localRotation = Quaternion.LookRotation(-bv.normalized, Vector3.up);
                    nb++; continue;
                }
                bool jav = p.Kind == "javelin";
                var pool = jav ? javelinPool : arrowPool;
                int i = jav ? nj++ : na++;
                if (i >= pool.Count)
                {
                    var go = Instantiate(jav ? javelinPrefab : arrowPrefab, missiles, false);
                    pool.Add(go);
                }
                var m = pool[i];
                if (!m.activeSelf) m.SetActive(true);
                m.transform.localPosition = new Vector3(-(float)p.X, (float)p.Y, (float)p.Z) * BoardMap.Yard;
                var v = new Vector3(-(float)p.Vx, (float)p.Vy, (float)p.Vz);
                // 武器模型沿 −Z（原型约定）：尖端朝飞行方向；插地的保持最后的方向（速度停在插入那一刻）
                if (v.sqrMagnitude > 1e-6f) m.transform.localRotation = Quaternion.LookRotation(-v.normalized, Vector3.up);
            }
            for (int i = na; i < arrowPool.Count; i++) if (arrowPool[i].activeSelf) arrowPool[i].SetActive(false);
            for (int i = nj; i < javelinPool.Count; i++) if (javelinPool[i].activeSelf) javelinPool[i].SetActive(false);
            for (int i = nb; i < boltPool.Count; i++) if (boltPool[i].activeSelf) boltPool[i].SetActive(false);
        }
    }
}
