using System.Collections.Generic;
using UnityEngine;
using WarTable.Sim;

namespace WarTable.Game
{
    /// <summary>
    /// 打击反馈（11-08 用户：攻击看着像站在那里没打中，要有明显的反馈）：
    /// 命中 = 一撮火星；格挡 = 灰白火花；阵亡 = 炸开一团（敌人倒下金色、我军倒下蓝灰，一眼看出谁在掉人）；
    /// 溃逃（11-08）= 头顶冒一团骨白色的烟，逃跑途中头顶一直飘一缕，敌我都有。
    /// 一个粒子系统 Emit 到事件位置，世界空间、带重力，0.3–0.6 秒消失。
    /// </summary>
    public class HitFx : MonoBehaviour
    {
        public Material mat;
        ParticleSystem ps;
        readonly Dictionary<int, Agent> byId = new();
        Battle idsFor;
        static readonly Color Spark = new(1f, 0.93f, 0.75f), Clash = new(0.85f, 0.87f, 0.9f),
            FoeDown = new(1f, 0.86f, 0.45f), OursDown = new(0.45f, 0.55f, 0.75f),   // 我军倒下用冷蓝灰烟尘（不用红色，不像血）
            Rout = new(0.96f, 0.95f, 0.9f), Gold = new(1f, 0.8f, 0.36f);   // 11-08 用户：溃逃要有信号——骨白色（“白旗”的意思），敌我都一样
        const float TrailEvery = 0.3f;
        float trailT;

        void Build()
        {
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.maxParticles = 3000;   // 一直运行（不自动发射，只 Emit）——不循环的话 5 秒后停掉，之后发的粒子不显示
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0.35f;
            main.startLifetime = 0.45f; main.startSpeed = 0f; main.startSize = 0.005f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                              new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var r = GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
        }

        void Burst(Vector3 at, Color c, int n, float speed, float size, float life, float k)
        {
            var ep = new ParticleSystem.EmitParams { startColor = c, applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                var v = Random.onUnitSphere; v.y = Mathf.Abs(v.y) * 0.8f + 0.3f;
                ep.position = at; ep.velocity = v * speed * Random.Range(0.5f, 1f) * k;
                ep.startSize = size * Random.Range(0.6f, 1.2f) * k; ep.startLifetime = life * Random.Range(0.7f, 1.1f);
                ps.Emit(ep, 1);
            }
        }

        // ── 阵亡光环：地上从小到大扩开一圈（LineRenderer 池），0.7 秒淡出
        const int RingCount = 24, RingSeg = 32; const float RingLife = 0.7f;
        readonly LineRenderer[] rings = new LineRenderer[RingCount];
        readonly float[] ringT = new float[RingCount];
        readonly Vector3[] ringC = new Vector3[RingCount];
        readonly Color[] ringCol = new Color[RingCount];
        readonly float[] ringK = new float[RingCount];
        int ringNext;

        void Ring(Vector3 c, Color col, float k)
        {
            int i = ringNext; ringNext = (ringNext + 1) % RingCount;
            if (!rings[i])
            {
                var l = new GameObject("KillRing" + i).AddComponent<LineRenderer>(); l.transform.SetParent(transform, false);
                l.sharedMaterial = mat; l.useWorldSpace = true; l.loop = true; l.positionCount = RingSeg;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.receiveShadows = false;
                rings[i] = l;
            }
            ringT[i] = Time.time; ringC[i] = c; ringCol[i] = col; ringK[i] = k;
            ApplyRing(i, 0f);   // 生成当帧就摆好（新建的 LineRenderer 默认宽 1 米，不先设会糊一大片白）
            rings[i].gameObject.SetActive(true);
        }

        void Update()
        {
            for (int i = 0; i < RingCount; i++)
            {
                var l = rings[i]; if (!l || !l.gameObject.activeSelf) continue;
                float u = (Time.time - ringT[i]) / RingLife;
                if (u >= 1f) { l.gameObject.SetActive(false); continue; }
                ApplyRing(i, u);
            }
        }

        void ApplyRing(int i, float u)
        {
            {
                var l = rings[i];
                float e = 1f - (1f - u) * (1f - u);
                float r = Mathf.Lerp(0.01f, 0.075f, e) * ringK[i];
                for (int j = 0; j < RingSeg; j++)
                {
                    float a = Mathf.PI * 2f * j / RingSeg;
                    l.SetPosition(j, ringC[i] + new Vector3(Mathf.Cos(a) * r, 0.002f, Mathf.Sin(a) * r));
                }
                l.widthMultiplier = 0.008f * ringK[i] * (1f - 0.5f * u);
                var c = ringCol[i]; c.a = 1f - u;
                l.startColor = l.endColor = c;
            }
        }

        /// <summary>令牌插进沙盘：扬起一小撮土色尘 + 一圈地面光环（11-10）。</summary>
        public void Dust(Vector3 at, float k)
        {
            if (!ps) Build();
            if (!ps.isPlaying) ps.Play();
            Burst(at, new Color(0.72f, 0.6f, 0.42f), 22, 0.28f, 0.016f, 0.7f, k);
            Ring(at + Vector3.up * 0.002f, new Color(1f, 0.82f, 0.45f), k);
        }

        void Flash(Vector3 at, Color c, float size, float life, float k)
        {
            var ep = new ParticleSystem.EmitParams { startColor = c, applyShapeToPosition = false, position = at, velocity = Vector3.zero, startSize = size * k, startLifetime = life };
            ps.Emit(ep, 1);
        }

        /// <summary>每帧把这一帧的战斗事件变成特效。k = 沙盘缩放（世界尺寸跟着沙盘变）。</summary>
        public void Play(List<BattleEvent> events, Battle b, System.Func<double, double, double, Vector3> toWorld, float k)
        {
            if (b == null) return;
            if (!ps) Build();
            // 溃逃中的兵：头顶每 0.3 秒冒一缕骨白色的烟，一路跑一路飘（逃跑本身照旧）
            if (Time.time - trailT >= TrailEvery)
            {
                trailT = Time.time;
                if (!ps.isPlaying) ps.Play();
                var tp = new ParticleSystem.EmitParams { startColor = new Color(Rout.r, Rout.g, Rout.b, 0.85f), applyShapeToPosition = false };
                foreach (var a in b.Agents)
                {
                    if (!a.Routed || !a.Alive || a.Gone) continue;
                    tp.position = toWorld(a.X, a.Y + (a.Mount != null && a.Mount.Alive ? 3.2 : 2.3), a.Z);
                    tp.velocity = new Vector3(Random.Range(-0.01f, 0.01f), 0.12f, Random.Range(-0.01f, 0.01f)) * k;
                    tp.startSize = 0.022f * k; tp.startLifetime = 0.8f;   // 11-08 用户看不到 → 放大一倍
                    ps.Emit(tp, 1);
                }
            }
            if (events == null || events.Count == 0) return;
            if (!ps.isPlaying) ps.Play();
            if (idsFor != b) { byId.Clear(); idsFor = b; }
            if (byId.Count != b.Agents.Count) { byId.Clear(); foreach (var a in b.Agents) byId[a.Id] = a; }
            foreach (var e in events)
                switch (e)
                {
                    case HitEvent h:
                    {
                        var at = toWorld(h.X, h.Y, h.Z);
                        if (h.Killed)
                        {
                            bool foe = byId.TryGetValue(h.Target, out var t) && t.Team == 1;
                            var c = foe ? FoeDown : OursDown;
                            // 11-08 用户：头显里看不到特效 → 放大到 1 米外也看得清：地上扩一圈光环 + 大亮斑 + 一团粒子
                            Ring(toWorld(h.X, h.Y - 1.3, h.Z), c, k);
                            Flash(at, c, 0.1f, 0.3f, k);
                            Burst(at, c, 30, 0.4f, 0.02f, 0.8f, k);
                            Burst(at, Spark, 12, 0.5f, 0.012f, 0.45f, k);
                        }
                        else { Flash(at, Spark, 0.032f, 0.14f, k); Burst(at, Spark, h.Damage > 15 ? 10 : 7, 0.34f, 0.013f, 0.4f, k); }
                        break;
                    }
                    case BlockEvent bl: Burst(toWorld(bl.X, bl.Y, bl.Z), Clash, 6, 0.26f, 0.01f, 0.3f, k); break;
                    case CommanderEvent ce when b.Commander[ce.Team].HasValue && byId.TryGetValue(b.Commander[ce.Team].Value, out var cm):
                    {
                        // 11-10：主将阵亡——头顶的“帅”字牌碎成一团金光，地上连扩三圈（全军都会动摇，要让玩家一眼看到）
                        var at = toWorld(cm.X, cm.Y + 2.5, cm.Z);
                        Flash(at, Gold, 0.14f, 0.5f, k);
                        Burst(at, Gold, 40, 0.55f, 0.02f, 1.0f, k);
                        for (int r = 0; r < 3; r++) Ring(toWorld(cm.X, cm.Y - 1.3 + r * 0.01, cm.Z), Gold, k * (1f + r * 0.5f));
                        break;
                    }
                    case RoutEvent re when byId.TryGetValue(re.AgentId, out var ra):
                    {
                        // 崩溃的那一下：头顶一团骨白色的烟往上冒（不是光环——光环是阵亡）
                        var at = toWorld(ra.X, ra.Y + 2.2, ra.Z);
                        Flash(at, Rout, 0.09f, 0.4f, k);
                        Burst(at, Rout, 22, 0.32f, 0.024f, 0.85f, k);
                        break;
                    }
                }
        }
    }
}
