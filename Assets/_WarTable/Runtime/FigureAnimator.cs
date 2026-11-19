using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace WarTable
{
    /// <summary>
    /// 棋子的动画（照原型 agentView.ts 的 Layer）：上下半身两层，上半身层只管 Spine 以下的骨骼。
    /// 每层按片段名播放：给了时间点就停在那一刻（攻击、格挡跟模拟层的片段时间严格一致），否则按速度循环；
    /// 换片段时交叉淡化。所有片段的时间都由这里手动推进（不交给 Playable 自己走），保证和模拟一致。
    /// 片段由编辑器搭预制体时填进 <see cref="clips"/>（原型 FBX 里的 50 段动作，名字形如 "Armature|Run"）。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FigureAnimator : MonoBehaviour
    {
        public AnimationClip[] clips;
        /// <summary>上半身层的根骨骼名（原型 UPPER_ROOT = /^mixamorig:?Spine$/）。空 = 只有一层（坐骑）。</summary>
        public string upperRoot = "";

        public Layer Lower { get; private set; }
        public Layer Upper { get; private set; }
        public bool Ready => graph.IsValid();

        PlayableGraph graph;
        Dictionary<string, AnimationClip> byName;

        public AnimationClip Get(string name)
        {
            if (byName == null)
            {
                byName = new Dictionary<string, AnimationClip>();
                foreach (var c in clips)
                {
                    if (!c) continue;
                    int bar = c.name.LastIndexOf('|');
                    byName[bar >= 0 ? c.name.Substring(bar + 1) : c.name] = c;
                }
            }
            return byName.TryGetValue(name, out var clip) ? clip : null;
        }

        public void Init()
        {
            if (graph.IsValid()) return;
            TryGetComponent(out fit);
            graph = PlayableGraph.Create(name + "_anim");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "out", GetComponent<Animator>());
            bool split = !string.IsNullOrEmpty(upperRoot) && Find(transform, upperRoot);
            var layers = AnimationLayerMixerPlayable.Create(graph, split ? 2 : 1);
            output.SetSourcePlayable(layers);
            Lower = new Layer(graph, layers, 0, this);
            if (split)
            {
                Upper = new Layer(graph, layers, 1, this);
                layers.SetLayerMaskFromAvatarMask(1, UpperMask());
                layers.SetInputWeight(1, 1f);
            }
            else Upper = Lower;
        }

        /// <summary>每帧推进时间、更新淡化权重并求值（由显示层在读完模拟状态后调用）。</summary>
        public void Tick(float dt)
        {
            if (!graph.IsValid()) return;
            Lower.Tick(dt);
            if (Upper != Lower) Upper.Tick(dt);
            graph.Evaluate(0f);
            if (fit) fit.Apply();   // 壮身躯的手臂修正（见 BodyFit），在这一帧的动作算完之后叠上去
        }

        WarTable.Game.BodyFit fit;

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n || t.name.EndsWith(":" + n)) return t;
            foreach (Transform c in t) { var f = Find(c, n); if (f) return f; }
            return null;
        }

        AvatarMask UpperMask()
        {
            var mask = new AvatarMask();
            var paths = new List<(string path, bool active)>();
            var spine = Find(transform, upperRoot);
            void Walk(Transform t, string path, bool under)
            {
                under |= t == spine;
                paths.Add((path, under));   // 根节点自己也要作为一项（空路径），否则遮罩不生效
                foreach (Transform c in t) Walk(c, path.Length == 0 ? c.name : path + "/" + c.name, under);
            }
            Walk(transform, "", false);
            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) { mask.SetTransformPath(i, paths[i].path); mask.SetTransformActive(i, paths[i].active); }
            return mask;
        }

        /// <summary>一层动画：固定 3 个输入槽轮流用——当前片段 + 最多两个正在淡出的旧片段。</summary>
        public class Layer
        {
            const int Slots = 3;
            class Slot { public AnimationClipPlayable p; public float len = 1, t, speed = 1f, w; public bool once, hold, used; }

            readonly PlayableGraph graph;
            readonly AnimationMixerPlayable mixer;
            readonly FigureAnimator owner;
            readonly Slot[] slots = new Slot[Slots];
            int cur = -1;
            float fadeRate = 1f / 0.15f;
            public string Name { get; private set; } = "";

            public Layer(PlayableGraph g, AnimationLayerMixerPlayable layers, int index, FigureAnimator owner)
            {
                graph = g; this.owner = owner;
                mixer = AnimationMixerPlayable.Create(g, Slots);
                g.Connect(mixer, 0, layers, index);
                layers.SetInputWeight(index, 1f);
                for (int i = 0; i < Slots; i++) slots[i] = new Slot();
            }

            /// <summary>播放 name：给了 time 就停在那个时间点；否则按 speed 循环（once = 播完停在最后一帧）。fade：交叉淡化秒数。</summary>
            public void Play(string name, float speed = 1f, float? time = null, bool once = false, float fade = 0.15f)
            {
                if (Name != name)
                {
                    var clip = owner.Get(name);
                    if (!clip) return;
                    // 占用权重最低的非当前槽
                    int k = -1;
                    for (int i = 0; i < Slots; i++) if (i != cur && (k < 0 || slots[i].w < slots[k].w)) k = i;
                    var s = slots[k];
                    if (s.used) { mixer.DisconnectInput(k); s.p.Destroy(); }
                    s.p = AnimationClipPlayable.Create(graph, clip);
                    s.p.SetApplyFootIK(false);
                    s.p.SetSpeed(0);
                    graph.Connect(s.p, 0, mixer, k);
                    s.used = true; s.len = Mathf.Max(clip.length, 1e-3f); s.t = 0; s.once = once;
                    s.w = cur < 0 ? 1f : 0f;
                    cur = k;
                    Name = name;
                    fadeRate = 1f / Mathf.Max(fade, 1e-3f);
                }
                var c = slots[cur];
                if (time.HasValue) { c.hold = true; c.t = Mathf.Min(time.Value, c.len - 1e-3f); }
                else { c.hold = false; c.speed = speed; }
            }

            public void Tick(float dt)
            {
                float sum = 0f;
                for (int i = 0; i < Slots; i++)
                {
                    var s = slots[i];
                    if (!s.used) continue;
                    if (!s.hold)
                    {
                        s.t += dt * s.speed;
                        s.t = s.once ? Mathf.Min(s.t, s.len - 1e-3f) : Mathf.Repeat(s.t, s.len);
                    }
                    s.w = Mathf.MoveTowards(s.w, i == cur ? 1f : 0f, dt * fadeRate);
                    s.p.SetTime(s.t);
                    sum += s.w;
                }
                for (int i = 0; i < Slots; i++)
                    mixer.SetInputWeight(i, slots[i].used && sum > 0f ? slots[i].w / sum : (i == cur ? 1f : 0f));
            }
        }
    }
}
