// 自动生成：由 sandtable-xr/src/sim/swings.ts 忠实移植
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace WarTable.Sim
{
    /// <summary>
    /// 挥击动画的关键时间窗口：蓄力持续时间、hold 点、刀刃生效区间。
    /// </summary>
    public class SwingInfo
    {
        /// <summary>动画 clip 总时长（秒）。</summary>
        public double Duration;
        /// <summary>蓄力快速部分开始前可 hold 的时间点（秒）。</summary>
        public double Hold;
        /// <summary>刀刃开始生效的 clip 时间（秒）。</summary>
        public double LiveFrom;
        /// <summary>刀刃结束生效的 clip 时间（秒）。</summary>
        public double LiveTo;
    }

    /// <summary>模型空间中的一个三维点（用于返回刀刃位置）。</summary>
    public struct BladePoint
    {
        public double X;
        public double Y;
        public double Z;
    }

    /// <summary>
    /// 从 swings.json 加载烘焙武器路径数据，提供刀刃位置和挥击信息查询。
    /// </summary>
    public static class SwingData
    {
        // ── 内部数据结构 ──────────────────────────────────────────────

        private class ClipBake
        {
            public double Duration;
            public double Peak;
            /// <summary>平铺采样数组：每 6 个元素为 [grip_x, grip_y, grip_z, dir_x, dir_y, dir_z]。</summary>
            public double[] S = Array.Empty<double>();
            /// <summary>头骨高度采样序列（可选）。</summary>
            public double[]? Head;
        }

        private class ModelBake
        {
            /// <summary>站立姿势下根骨位置（XYZ 前三分量）。</summary>
            public double[] RootStand = Array.Empty<double>();
            /// <summary>骑乘姿势下根骨位置（XYZ 前三分量）。</summary>
            public double[] RootMount = Array.Empty<double>();
            /// <summary>模型站立高度。</summary>
            public double Height;
            public Dictionary<string, ClipBake> Clips = new();
        }

        // ── 路径和数据存储 ────────────────────────────────────────────

        /// <summary>
        /// swings.json 路径；外部可在调用任何方法前覆盖此值。
        /// 默认指向源码目录，部署时需更新为运行时实际路径。
        /// </summary>
        public static string DataPath = "/Users/ld/sandtable-xr/src/data/swings.json";

        private static Dictionary<string, ModelBake>? data;
        private static readonly Dictionary<string, SwingInfo> cache = new();

        // ── 初始化 ────────────────────────────────────────────────────

        /// <summary>首次使用时懒加载 JSON 数据。</summary>
        private static Dictionary<string, ModelBake> Data
        {
            get
            {
                if (data != null) return data;
                // 11-08 真机日志：DataPath 是开发机上的绝对路径，头显上读不到（每帧抛异常、没有挥砍 → 两军站在一起不打、不死人）。
                // 先从包里读（Assets/_WarTable/Resources/SimData/swings.json），读不到再退回文件路径（编辑器外的工具）
                try
                {
                    var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("SimData/swings");
                    data = ta != null ? Parse(ta.text) : LoadData(DataPath);
                }
                catch (Exception e)
                {
                    // 只报一次：读不到就用空表（没有挥砍数据，但不会每帧抛异常拖垮整个战斗更新）
                    UnityEngine.Debug.LogError("[Sim] 挥砍数据 swings.json 读不到：" + e.Message);
                    data = new Dictionary<string, ModelBake>();
                }
                return data;
            }
        }

        private static Dictionary<string, ModelBake> LoadData(string path) => Parse(File.ReadAllText(path));

        private static Dictionary<string, ModelBake> Parse(string text)
        {
            var json = JObject.Parse(text);
            var result = new Dictionary<string, ModelBake>();
            foreach (var modelProp in json.Properties())
            {
                var mo = (JObject)modelProp.Value;
                var mb = new ModelBake
                {
                    Height = (double)mo["height"]!
                };

                // rootStand/rootMount：JSON 数组 [x, y, z, qx, qy, qz, qw]，只取 XYZ 前三
                var rsArr = (JArray)mo["rootStand"]!;
                mb.RootStand = new double[3] { (double)rsArr[0], (double)rsArr[1], (double)rsArr[2] };
                var rmArr = (JArray)mo["rootMount"]!;
                mb.RootMount = new double[3] { (double)rmArr[0], (double)rmArr[1], (double)rmArr[2] };

                var clips = (JObject)mo["clips"]!;
                foreach (var clipProp in clips.Properties())
                {
                    var co = (JObject)clipProp.Value;
                    var cb = new ClipBake
                    {
                        Duration = (double)co["duration"]!,
                        Peak = (double)co["peak"]!,
                    };
                    // s 数组（平铺）
                    var sArr = (JArray)co["s"]!;
                    cb.S = new double[sArr.Count];
                    for (int i = 0; i < sArr.Count; i++) cb.S[i] = (double)sArr[i];
                    // head 数组（可选）
                    var headTok = co["head"];
                    if (headTok != null && headTok.Type == JTokenType.Array)
                    {
                        var hArr = (JArray)headTok;
                        cb.Head = new double[hArr.Count];
                        for (int i = 0; i < hArr.Count; i++) cb.Head[i] = (double)hArr[i];
                    }
                    mb.Clips[clipProp.Name] = cb;
                }
                result[modelProp.Name] = mb;
            }
            return result;
        }

        // ── 内部工具 ──────────────────────────────────────────────────

        /// <summary>第 i 帧时单位长武器刀尖的模型空间坐标。</summary>
        private static (double x, double y, double z) TipAt(double[] s, double[] root, int i, double reach)
        {
            return (
                s[i * 6]     + s[i * 6 + 3] * reach + root[0],
                s[i * 6 + 1] + s[i * 6 + 4] * reach + root[1],
                s[i * 6 + 2] + s[i * 6 + 5] * reach + root[2]
            );
        }

        // ── 公开 API ──────────────────────────────────────────────────

        /// <summary>
        /// 获取（并缓存）某个模型/clip 的挥击时间窗口信息。
        /// 抛出 <see cref="KeyNotFoundException"/> 若 model 或 clip 不存在。
        /// </summary>
        public static SwingInfo GetInfo(string model, string clip)
        {
            string key = $"{model}|{clip}";
            if (cache.TryGetValue(key, out var hit)) return hit;

            var m = Data[model];
            if (!m.Clips.TryGetValue(clip, out var b))
                throw new KeyNotFoundException($"no baked clip '{clip}' for model '{model}'");

            var root = m.RootStand;
            int n = b.S.Length / 6;
            double dt = b.Duration / (n - 1);

            var speeds = new double[n];
            double best = 0;
            int bi = 1;
            for (int i = 1; i < n; i++)
            {
                var (ax, ay, az) = TipAt(b.S, root, i - 1, 1.0);
                var (ex, ey, ez) = TipAt(b.S, root, i, 1.0);
                double v = Math.Sqrt(
                    (ex - ax) * (ex - ax) +
                    (ey - ay) * (ey - ay) +
                    (ez - az) * (ez - az)) / dt;
                speeds[i] = v;
                if (v > best) { best = v; bi = i; }
            }

            int lo = bi, hi = bi;
            while (lo > 1 && speeds[lo - 1] > best * 0.3) lo--;
            while (hi < n - 1 && speeds[hi + 1] > best * 0.3) hi++;

            // hold 在快速部分前稍微留出余量，让释放仍然有挥击感
            double hold = Math.Max(0.0, (lo - 2) * dt);
            var info = new SwingInfo
            {
                Duration = b.Duration,
                Hold     = hold,
                LiveFrom = (lo - 1) * dt,
                LiveTo   = Math.Min(b.Duration, (hi + 1) * dt)
            };
            cache[key] = info;
            return info;
        }

        /// <summary>
        /// 计算 clip 时间 t 处武器刀尖的模型空间坐标（刀刃有效点）。
        /// mounted=true 使用骑乘根骨姿势。
        /// pt 输出刀尖位置。
        /// </summary>
        public static void BladeAt(string model, string clip, double t, double reach,
            bool mounted, out BladePoint pt)
        {
            var m = Data[model];
            var b = m.Clips[clip];
            var root = mounted ? m.RootMount : m.RootStand;
            int n = b.S.Length / 6;
            double f = Math.Max(0.0, Math.Min(n - 1, (t / b.Duration) * (n - 1)));
            int i = Math.Min(n - 2, (int)Math.Floor(f));
            double k = f - i;
            var s = b.S;

            // 在第 i 和 i+1 帧之间线性插值
            double gx = s[i*6]   * (1-k) + s[(i+1)*6]   * k + root[0];
            double gy = s[i*6+1] * (1-k) + s[(i+1)*6+1] * k + root[1];
            double gz = s[i*6+2] * (1-k) + s[(i+1)*6+2] * k + root[2];
            double dx = s[i*6+3] * (1-k) + s[(i+1)*6+3] * k;
            double dy = s[i*6+4] * (1-k) + s[(i+1)*6+4] * k;
            double dz = s[i*6+5] * (1-k) + s[(i+1)*6+5] * k;

            // pt = 握柄 + reach * 方向 = 刀尖
            pt = new BladePoint { X = gx + dx * reach, Y = gy + dy * reach, Z = gz + dz * reach };
        }

        /// <summary>clip 时间 t 处头骨高度（模型空间，站立姿势）。</summary>
        public static double HeadHeight(string model, string clip, double t)
        {
            var m = Data[model];
            // fallback：clip 不存在时用 Stand
            ClipBake? b = null;
            m.Clips.TryGetValue(clip, out b);
            if (b == null) m.Clips.TryGetValue("Stand", out b);
            if (b == null) return m.Height;

            var h = b.Head;
            if (h == null || h.Length == 0) return m.Height;

            double f = Math.Max(0.0, Math.Min(h.Length - 1, (t / b.Duration) * (h.Length - 1)));
            int i = Math.Min(h.Length - 2, (int)Math.Floor(f));
            if (h.Length == 1) return h[0];
            return h[i] * (1 - (f - i)) + h[i + 1] * (f - i);
        }

        /// <summary>返回模型站立高度（头顶距脚底）。</summary>
        public static double ModelHeight(string model) => Data[model].Height;
    }
}
