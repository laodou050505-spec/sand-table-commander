using System.Collections.Generic;
using UnityEngine;
using WarTable.Sim;

namespace WarTable.Game
{
    /// <summary>
    /// 声音（11-03 用户：加上合适的音效、战斗声和 BGM）。素材都来自 ange-embed 素材库（Assets/_WarTable/Audio，来源见方案文档）。
    /// - BGM（11-04 用户：标题的 BGM 不合适）：封面放庄重、带失落王国感的管弦（bgm_title = Wasteland_01），布阵放战前集结的悬念曲（bgm_deploy），
    ///   交战管弦战斗乐（bgm_battle），结算回到安静的东方调（bgm_calm）。换曲交叉淡入淡出 2 秒。
    /// - 界面：看着菜单项 = 轻“嗒”，捏下 = 木块敲，翻页 = 翻纸，盖星 = 木印，不允许的操作 = 闷响。
    /// - 指挥：选中部队 = 木子落盘，下令 = 令牌划过桌面，冲锋再加一声战鼓，开战 = 大鼓。
    /// - 战斗：挥砍、命中、格挡、放箭、箭中都是 3D 声，从沙盘上那个士兵的位置发出（离得近的一侧更响）。
    ///   一局几十人同时打，按类别限流（每类最短间隔 + 总声部上限），音调随机 ±8%，不会糊成一片也不会机械重复。
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public AudioClip bgmCalm, bgmBattle, bgmTitle, bgmDeploy;
        public enum Track { Title, Deploy, Battle, End }
        public AudioClip hover, press, page, stamp, deny, select, order, place, drumBoom, drumTom;
        public AudioClip[] swing, hit, clash, bow, arrowHit;
        [Range(0, 1)] public float musicVolume = 1f, uiVolume = 1f, battleVolume = 1f;

        AudioSource musicA, musicB, ui;
        readonly List<AudioSource> voices = new();
        readonly Dictionary<string, float> last = new();
        readonly Dictionary<int, Agent> byId = new();
        Battle idsFor;
        AudioClip musicWant;
        float musicMix = 1f;   // 1 = A 在放
        const int Voices = 12;   // 大混战时同时最多 12 声（再多也听不出来，只会糊、变响）

        void Awake()
        {
            musicA = Src("MusicA", true); musicB = Src("MusicB", true); ui = Src("UI", false);
            for (int i = 0; i < Voices; i++)
            {
                var s = Src("Voice" + i, false);
                s.spatialBlend = 0.75f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 0.4f; s.maxDistance = 4f; s.dopplerLevel = 0f;
                voices.Add(s);
            }
        }

        void Start() => Music(Track.Title);

        AudioSource Src(string name, bool loop)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f;
            return s;
        }

        // ------------------------------------------------------------ BGM

        /// <summary>按阶段选曲（缺哪首就退回安静曲）。换曲时两路音源交叉淡入淡出（2 秒）。</summary>
        public void Music(Track t)
        {
            var want = t switch { Track.Title => bgmTitle, Track.Deploy => bgmDeploy, Track.Battle => bgmBattle, _ => bgmCalm };
            if (!want) want = bgmCalm;
            if (!want || want == musicWant) return;
            musicWant = want;
            // 新曲放到“现在较轻”的那一路上，另一路随之淡出
            var on = musicMix > 0.5f ? musicB : musicA;
            on.clip = want; on.time = 0f; on.Play();
        }

        void Update()
        {
            if (!musicA) return;
            float target = musicA.clip == musicWant ? 1f : 0f;
            musicMix = Mathf.MoveTowards(musicMix, target, Time.unscaledDeltaTime / 2f);
            musicA.volume = musicVolume * Gain(musicA.clip) * musicMix; musicB.volume = musicVolume * Gain(musicB.clip) * (1f - musicMix);
            // 只停“不是当前曲目、且已经淡到 0”的那一路
            if (musicA.clip != musicWant && musicMix <= 0f && musicA.isPlaying) musicA.Stop();
            if (musicB.clip != musicWant && musicMix >= 1f && musicB.isPlaying) musicB.Stop();
        }

        /// <summary>每首 BGM 的增益：统一到 −29 dBFS（实测 RMS：标题 −18.8、布阵 −21.5、战斗 −20.8、结算 −19.7）。</summary>
        float Gain(AudioClip c) => !c ? 0f : c == bgmTitle ? 0.31f : c == bgmDeploy ? 0.42f : c == bgmBattle ? 0.39f : 0.34f;

        // ------------------------------------------------------------ 界面 / 指挥

        /// <summary>界面音（不带位置）：hover / press / page / stamp / deny / select / order / place / boom / tom。</summary>
        public void Cue(string kind)
        {
            var (clip, vol, gap) = kind switch
            {
                // 11-04 用户：注意音量大小是否合适 → 按实测响度（Tools/measure_audio.py）配平：每条音最后落在
                // 悬停 −30、确认类 −26～−27、落盘 −24、大鼓 −22 dBFS 左右，BGM 统一 −29，界面音都比 BGM 清楚但不扎耳
                "hover" => (hover, 0.19f, 0.06f),
                "press" => (press, 0.41f, 0.05f),
                "page" => (page, 0.44f, 0.1f),
                "stamp" => (stamp, 0.36f, 0.08f),
                "deny" => (deny, 0.37f, 0.2f),
                "select" => (select, 0.33f, 0.05f),
                "order" => (order, 0.35f, 0.1f),
                "place" => (place, 0.46f, 0.3f),
                "boom" => (drumBoom, 0.38f, 0.15f),
                "tom" => (drumTom, 0.49f, 0.15f),
                _ => ((AudioClip)null, 0f, 0f),
            };
            if (!clip || !Ready("ui." + kind, gap)) return;
            ui.pitch = 1f;
            ui.PlayOneShot(clip, vol * uiVolume);
        }

        /// <summary>下了一道军令：令牌声，冲锋 / 冲敌再加一声战鼓。</summary>
        public void Order(string cmd)
        {
            Cue("order");
            if (cmd == "charge" || cmd.StartsWith("attack")) Cue("tom");
        }

        // ------------------------------------------------------------ 战斗

        /// <summary>每帧把模拟层吐出的事件转成 3D 战斗声。toWorld：模拟坐标（码）→ 世界坐标。</summary>
        public void Battle(List<BattleEvent> events, Battle b, System.Func<double, double, double, Vector3> toWorld)
        {
            if (b == null || events == null || events.Count == 0) return;
            if (idsFor != b) { byId.Clear(); idsFor = b; }
            if (byId.Count != b.Agents.Count) { byId.Clear(); foreach (var a in b.Agents) byId[a.Id] = a; }
            foreach (var e in events)
            {
                switch (e)
                {
                    case SwingEvent s: At(s.AgentId, swing, 0.32f, "swing", 0.07f, b, toWorld); break;
                    case ShootEvent s: At(s.AgentId, bow, 0.18f, "bow", 0.09f, b, toWorld); break;
                    case HitEvent h:
                        bool shot = byId.TryGetValue(h.Attacker, out var by) && Defs.IsRanged(AgentOps.WeaponOf(by).Cls);
                        Play(shot ? arrowHit : hit, toWorld(h.X, h.Y, h.Z), shot ? (h.Killed ? 0.34f : 0.27f) : (h.Killed ? 0.28f : 0.21f), shot ? "arrowHit" : "hit", 0.06f);
                        break;
                    case BlockEvent k: Play(clash, toWorld(k.X, k.Y, k.Z), 0.24f, "clash", 0.08f); break;
                    case CommanderEvent: Cue("boom"); break;
                }
            }
        }

        void At(int id, AudioClip[] set, float vol, string key, float gap, Battle b, System.Func<double, double, double, Vector3> toWorld)
        {
            if (!byId.TryGetValue(id, out var a)) return;
            Play(set, toWorld(a.X, a.Y + 1.2, a.Z), vol, key, gap);
        }

        void Play(AudioClip[] set, Vector3 at, float vol, string key, float gap)
        {
            if (set == null || set.Length == 0 || !Ready(key, gap)) return;
            AudioSource free = null;
            foreach (var v in voices) if (!v.isPlaying) { free = v; break; }
            if (!free) return;   // 声部用完：这一下不放（大混战时本来也听不出来）
            free.transform.position = at;
            free.clip = set[Random.Range(0, set.Length)];
            free.pitch = Random.Range(0.92f, 1.08f);
            free.volume = vol * battleVolume * Random.Range(0.8f, 1f);
            free.Play();
        }

        bool Ready(string key, float gap)
        {
            float now = Time.unscaledTime;
            if (last.TryGetValue(key, out var t) && now - t < gap) return false;
            last[key] = now;
            return true;
        }

        public void StopBattle() { foreach (var v in voices) v.Stop(); }
    }
}
