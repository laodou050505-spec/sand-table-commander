using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WarTable.Game;
using WarTable.Sim;

namespace WarTable.Campaign
{
    /// <summary>
    /// 玩法版（肉鸽战役）的总控：主菜单“出征” → 作战地图（CampaignMap）→ 选下一站 →
    ///   战斗 / 精英 / Boss：用编成表里的人数开打（WarGame.Begin 向这里要两军和种子），打完结算板“再战”= 重打这一仗（兵力原样）、“回营”= 认下结果；
    ///   赢了：伤亡落进存档 → 三选一奖励卡 → 回地图；输了“回营”：什么都不扣，回地图（可以换条路，也可以再打）；
    ///   补给站：一排可以反复点的卡（治疗伤员 / 招募 / 扩编），点“出发”离开；军械库：直接三选一（保底一面纹章）。
    /// 战役固定深空风格（Settings.EraOverride，不改演示里选的风格）。每次落定都存档（CampaignStore），下次从当前节点继续。
    /// </summary>
    public class CampaignDirector : MonoBehaviour
    {
        public enum State { Off, Map, Battle, Reward, Supply, End }
        public State Now { get; private set; } = State.Off;
        public RunState Run { get; private set; }
        public CampaignMap Map { get; private set; }
        public CampaignCards Cards { get; private set; }

        WarGame game;
        Transform mapRoot, cardsRoot;
        MapNode node;
        List<TroopCount> muster = new(), fought = new();
        int attempt;
        CampaignRules.Outcome outcome; bool hasOutcome;
        readonly List<(GameObject g, bool was)> hidden = new();

        /// <summary>WarGame.Begin 要不要用战役的军队。</summary>
        public bool Pending => Now == State.Battle && node != null;

        // ───────────────────────────────────────────── 进出
        public void Enter(WarGame g)
        {
            game = g;
            Settings.EraOverride = 4;   // 深空
            Run = CampaignStore.Load();
            if (Run == null || Run.over || Run.nodes == null || Run.nodes.Count == 0) { Run = CampaignRules.NewRun(Random.Range(1, 1 << 20)); CampaignStore.Save(Run); Debug.Log("[Campaign] 新战役 种子 " + Run.seed); }
            else Debug.Log($"[Campaign] 继续战役：第 {Run.battles} 场后，停在节点 {Run.at}");
            muster = CampaignRules.DefaultMuster(Run);
            EnsureViews();
            ShowMap();
        }

        public void Exit()
        {
            Settings.EraOverride = -1;
            if (Cards) Cards.Clear();
            HideMap();
            Now = State.Off; node = null;
        }

        void EnsureViews()
        {
            if (!mapRoot)
            {
                mapRoot = new GameObject("CampaignMap").transform;
                mapRoot.SetParent(game.board, false);
                Map = mapRoot.gameObject.AddComponent<CampaignMap>();
                Map.OnPick += Arrive;
                cardsRoot = new GameObject("CampaignCards").transform; cardsRoot.SetParent(game.board, false);   // 不挂在地图下：地图重画时会清掉自己的子物体
                var cr = cardsRoot;
                Cards = cr.gameObject.AddComponent<CampaignCards>();
                Cards.OnPick += OnCard;
            }
            // 沙盘根节点按 1.15 放大（BoardMap.BoardScale）：地图根缩回 1，本地单位 = 默认大小下的米；转 180° 让 +Z 朝远处
            mapRoot.localScale = Vector3.one / BoardMap.BoardScale;
            mapRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            mapRoot.localPosition = new Vector3(0f, 0.004f / BoardMap.BoardScale, 0f);
            cardsRoot.localScale = mapRoot.localScale; cardsRoot.localRotation = mapRoot.localRotation; cardsRoot.localPosition = mapRoot.localPosition;
            Map.mr = game.mr; Map.editorCam = game.previewCamera; Map.sound = game.sound;
            Cards.mr = game.mr; Cards.editorCam = game.previewCamera; Cards.sound = game.sound;
        }

        /// <summary>显示地图：沙盘上的地形、布景先收起来（木框留着），铺上作战图。</summary>
        void ShowMap()
        {
            game.board.gameObject.SetActive(true);
            if (hidden.Count == 0)
                foreach (Transform c in game.board)
                {
                    if (c == mapRoot || c == cardsRoot || c.name == "Frame" || !c.gameObject.activeSelf) continue;
                    hidden.Add((c.gameObject, true)); c.gameObject.SetActive(false);
                }
            mapRoot.gameObject.SetActive(true);
            Map.Build(Run, muster);
            Map.Live = true;
            Now = Run.over ? State.End : State.Map;
            if (Run.over) ShowEnd();
        }

        void HideMap()
        {
            foreach (var (g, was) in hidden) if (g) g.SetActive(was);
            hidden.Clear();
            if (mapRoot) mapRoot.gameObject.SetActive(false);
        }

        // ───────────────────────────────────────────── 到了某个节点
        void Arrive(MapNode n)
        {
            node = n;
            switch (n.kind)
            {
                case NodeKind.Supply:
                    n.done = true; Run.at = n.id; CampaignStore.Save(Run);
                    Map.Refresh(); Map.Live = false;
                    Now = State.Supply; ShowSupply();
                    break;
                case NodeKind.Ruins:
                    n.done = true; Run.at = n.id; CampaignStore.Save(Run);
                    Map.Refresh(); Map.Live = false;
                    Now = State.Reward; ShowDilemma(CampaignRules.DilemmaOf(Run, n));
                    break;
                case NodeKind.Armory:
                    n.done = true; Run.at = n.id; CampaignStore.Save(Run);
                    Map.Refresh(); Map.Live = false;
                    Now = State.Reward; ShowRewards(CampaignRules.Rewards(Run, n));
                    break;
                default:
                    fought = muster.Where(t => t.n > 0).Select(t => new TroopCount(t.id, t.n)).ToList();
                    attempt = 0; hasOutcome = false;
                    Now = State.Battle;
                    HideMap();
                    game.BeginCampaignBattle();
                    break;
            }
        }

        // ───────────────────────────────────────────── 战斗（WarGame 调）
        public (ArmySpec me, ArmySpec foe, int seed) BattleSetup() => (CampaignRules.PlayerArmy(fought), CampaignRules.EnemyArmy(Run, node), node.seed + attempt * 977);

        public void AfterSetup(Battle b) => CampaignRules.ApplyBonuses(b, Run);

        /// <summary>分出胜负：按兵种数活下来的人，算星级 / 伤亡（还不落进存档）。</summary>
        public CampaignRules.Outcome BattleOver(Battle b)
        {
            var alive = new Dictionary<string, int>();
            foreach (var a in b.Agents) if (a.Team == 0 && a.Alive) alive[a.Troop.Id] = (alive.TryGetValue(a.Troop.Id, out var k) ? k : 0) + 1;
            outcome = CampaignRules.Resolve(Run, node, fought, alive, b.Winner == 0, attempt == 0);
            hasOutcome = true;
            Debug.Log($"[Campaign] 战斗结束 胜={outcome.won} 出 {outcome.sent} 活 {outcome.alive} 阵亡 {outcome.dead} 伤 {outcome.wounded} 星 {outcome.stars} 星晶 {outcome.crystals}");
            return outcome;
        }

        /// <summary>结算板“再战”：这一仗重来，兵力原样。</summary>
        public void Retry() { attempt++; Run.retries++; hasOutcome = false; }

        /// <summary>结算板“回营”：赢了把结果落进存档并发奖励；输了什么都不扣、回地图。</summary>
        public void Continue()
        {
            if (hasOutcome && outcome.won)
            {
                CampaignRules.Commit(Run, node, fought, outcome);
                CampaignStore.Save(Run);
                muster = CampaignRules.DefaultMuster(Run);
                ShowMap(); Map.Live = false;
                if (Run.over) return;   // Boss 打赢了：ShowMap 里出结束卡
                Now = State.Reward;
                ShowRewards(CampaignRules.Rewards(Run, node));
            }
            else
            {
                muster = muster.Where(t => t.n > 0).ToList();
                ShowMap();
            }
            hasOutcome = false;
        }

        // ───────────────────────────────────────────── 卡片
        static string Sgn(int n) => (n >= 0 ? "+" : "-") + Mathf.Abs(n);

        void ShowRewards(List<Reward> rw)
        {
            var defs = new List<CampaignCards.Def>();
            foreach (var r in rw)
            {
                var d = new CampaignCards.Def { tag = r };
                switch (r.kind)
                {
                    case RewardKind.Reinforce: d.header = "reinforce"; d.icon = "troop_" + r.troop; d.name = CampaignArt.L("troop_" + r.troop); d.number = Sgn(r.n); break;
                    case RewardKind.Recruit: d.header = "recruit_new"; d.icon = "troop_" + r.troop; d.name = CampaignArt.L("troop_" + r.troop); d.number = Sgn(r.n); break;
                    case RewardKind.Promote: d.header = "promote"; d.icon = "badge_promote"; d.name = CampaignArt.L("troop_" + r.troop); d.desc = CampaignArt.D("promote"); break;
                    case RewardKind.Relic: d.header = "relic"; d.icon = "badge_" + r.relic; d.name = CampaignArt.L("relic_" + r.relic); d.desc = CampaignArt.D("relic_" + r.relic); break;
                    case RewardKind.Crystals: d.header = "crystals"; d.icon = "badge_crystals"; d.number = Sgn(r.n); break;
                }
                defs.Add(d);
            }
            Cards.Show(defs);
        }

        /// <summary>遗迹：两张卡——左边“交换”（付出什么 → 得到什么），右边“原样离开”。付不起的那张压暗。</summary>
        void ShowDilemma(CampaignRules.Dilemma d)
        {
            var take = new CampaignCards.Def { header = "dilemma_" + d.kind, desc = CampaignArt.D("dilemma_" + d.kind), enabled = d.ok, tag = d };
            switch (d.kind)
            {
                // 卡中间 = 得到什么，卡底红字 = 付出什么
                case "shrine": take.header = "shrine"; take.icon = "badge_" + d.relic; take.name = CampaignArt.L("relic_" + d.relic); take.desc = CampaignArt.D("relic_" + d.relic); take.cost = d.cost.ToString(); take.costIcon = "troop_" + d.troop; break;
                case "ore": take.header = "ore"; take.icon = "badge_crystals"; take.name = CampaignArt.L("crystals"); take.number = "+" + d.gain; take.cost = d.cost.ToString(); take.costIcon = "badge_heal"; take.desc = null; break;   // 卡上放不下三行：说明省掉（抬头 + 卡底的绷带已经说清了）
                default: take.header = "stragglers"; take.icon = "troop_" + d.troop; take.name = CampaignArt.L("troop_" + d.troop); take.number = "+" + d.gain; take.cost = d.cost.ToString(); take.desc = null; break;
            }
            Cards.Show(new List<CampaignCards.Def> { take, new() { header = "pass_by", icon = "badge_depart", tag = "depart" } });
        }

        void ShowSupply() => Cards.Show(SupplyDefs());

        List<CampaignCards.Def> SupplyDefs()
        {
            var defs = new List<CampaignCards.Def>();
            int wnd = Run.Total(Run.wounded);
            defs.Add(new CampaignCards.Def { header = "heal", icon = "badge_heal", name = CampaignArt.L("all_wounded"), number = wnd > 0 ? Sgn(wnd) : "0", cost = (wnd * CampaignRules.HealCost).ToString(), enabled = wnd > 0 && Run.crystals >= CampaignRules.HealCost, repeat = true, tag = "heal" });
            // 招募：兵最少的两个已解锁兵种，每次 +1（点几下招几个）
            foreach (var id in Run.unlocked.OrderBy(id => Run.Get(Run.roster, id)).Take(2))
            {
                int cost = CampaignRules.RoleOf(id) == Role.Cavalry ? CampaignRules.RecruitCost + 2 : CampaignRules.RecruitCost;
                defs.Add(new CampaignCards.Def { header = "recruit", icon = "troop_" + id, name = CampaignArt.L("troop_" + id), number = Run.Get(Run.roster, id).ToString(), cost = cost.ToString(), enabled = Run.crystals >= cost, repeat = true, tag = "recruit:" + id });
            }
            defs.Add(new CampaignCards.Def { header = "cap", icon = "badge_cap", name = CampaignArt.L("cap_plus"), number = Run.cap + "+5", cost = CampaignRules.CapCost.ToString(), enabled = Run.crystals >= CampaignRules.CapCost && Run.cap < 60, repeat = true, tag = "cap" });
            defs.Add(new CampaignCards.Def { header = "depart", icon = "badge_depart", tag = "depart" });
            return defs;
        }

        /// <summary>编辑器验收：直接打开补给站（不走地图）。</summary>
        public void TestSupply() { Map.Live = false; Now = State.Supply; ShowSupply(); }
        /// <summary>编辑器验收：直接打第 layer 层以后的第一个 kind 节点（不走地图动画）。</summary>
        public void TestNode(int layer, NodeKind kind)
        {
            var n = Run.nodes.FirstOrDefault(x => x.layer >= layer && x.kind == kind);
            if (n != null) Arrive(n);
        }

        /// <summary>编辑器验收：直接摆出第 k 个节点的三选一 / 战役结束卡。</summary>
        public void TestRewards(int k) { Map.Live = false; Now = State.Reward; ShowRewards(CampaignRules.Rewards(Run, Run.nodes[k])); }
        public void TestEnd(bool won) { Run.won = won; ShowEnd(); }

        /// <summary>编辑器验收：直接打开某种遗迹取舍（shrine / ore / stragglers）。</summary>
        public void TestDilemma(string kind)
        {
            Map.Live = false; Now = State.Reward;
            for (int s = 1; s < 500; s++) { var n = new MapNode { seed = s }; var d = CampaignRules.DilemmaOf(Run, n); if (d.kind == kind) { ShowDilemma(d); return; } }
        }

        void ShowEnd()
        {
            Map.Live = false;
            Cards.Show(new List<CampaignCards.Def>
            {
                new() { header = Run.won ? "victory" : "defeat", icon = Run.won ? "badge_veterans" : "badge_bulwark", tag = "end" },
                new() { header = "newrun", icon = "badge_depart", tag = "new" },
            });
        }

        void OnCard(CampaignCards.Def d)
        {
            if (d.tag is CampaignRules.Dilemma dl)
            {
                CampaignRules.Accept(Run, dl); CampaignStore.Save(Run);
                Debug.Log($"[Campaign] 遗迹 {dl.kind} {dl.troop} {dl.relic} 付 {dl.cost} 得 {dl.gain}");
                muster = CampaignRules.DefaultMuster(Run); ShowMap();
                return;
            }
            if (d.tag is Reward r)
            {
                CampaignRules.Apply(Run, r); CampaignStore.Save(Run);
                Debug.Log($"[Campaign] 奖励 {r.kind} {r.troop}{r.relic} {r.n}");
                muster = CampaignRules.DefaultMuster(Run);
                ShowMap();
                return;
            }
            switch (d.tag as string)
            {
                case "heal": while (Run.wounded.Count > 0 && CampaignRules.Heal(Run, Run.wounded[0].id)) { } break;
                case "cap": CampaignRules.RaiseCap(Run); break;
                case "depart":
                    CampaignStore.Save(Run); muster = CampaignRules.DefaultMuster(Run); ShowMap(); return;
                case "new":
                    Run = CampaignRules.NewRun(Random.Range(1, 1 << 20)); CampaignStore.Save(Run); muster = CampaignRules.DefaultMuster(Run); ShowMap(); return;
                case "end": ShowEnd(); return;
                default:
                    if (d.tag is string s && s.StartsWith("recruit:")) CampaignRules.Recruit(Run, s.Substring(8));
                    break;
            }
            // 补给站：花完钱刷新卡面和地图上的数字，留在这一页
            CampaignStore.Save(Run);
            muster = CampaignRules.DefaultMuster(Run); Map.muster = muster; Map.Refresh();
            StartCoroutine(RedrawSupply());
        }

        System.Collections.IEnumerator RedrawSupply()
        {
            yield return new WaitForSeconds(1.35f);   // 等章盖完、淡掉再改数字
            if (Now == State.Supply) ShowSupplyInPlace();
        }

        /// <summary>补给站花了钱：卡还是那几张，只把数字 / 能不能点重画（不整排重新入场）。</summary>
        void ShowSupplyInPlace()
        {
            var defs = SupplyDefs();
            if (Cards.Count != defs.Count) { Cards.Show(defs); return; }
            for (int i = 0; i < defs.Count; i++) Cards.Redraw(i, defs[i]);
        }
    }
}
