using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 未来时代的巡逻无人机（11-12 用户：“注意别把场景做得太空”；未来时代没有飞鸟，天上换成它）：
    /// 几架小无人机沿各自略有偏差的椭圆航线慢慢巡逻，机身轻轻起伏、转弯时侧倾，肚子下面一盏阵营色的灯。
    /// 和 BirdFlock 一样：共用圆心和转向、相位 / 半径 / 高度错开，互相不会撞。尺寸按棋子比例，一架约 0.9 cm。
    /// </summary>
    public class DroneFlock : MonoBehaviour
    {
        public int count = 3;
        public float radius = 0.12f;      // 本地米
        public float height = 0.16f;
        public float speed = 0.022f;      // 米/秒
        public Color body = new Color(0.86f, 0.86f, 0.84f);
        public Color glow = new Color(0.3f, 0.9f, 1f);
        public int seed = 1;

        struct Drone { public Transform t; public float phase, r, h; }
        Drone[] drones;
        static Mesh hull, lamp;
        float dir;

        void Start()
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            dir = R() < 0.5f ? 1f : -1f;
            var sh = Shader.Find("WarTable/MiniLit");
            var bodyMat = new Material(sh); bodyMat.SetColor("_BaseColor", body); bodyMat.SetFloat("_Gloss", 0.4f);
            var lampMat = new Material(sh); lampMat.SetColor("_BaseColor", glow); lampMat.SetColor("_EmissionColor", glow * 0.75f);
            hull ??= MakeHull();
            lamp ??= MakeLamp();
            drones = new Drone[count];
            for (int i = 0; i < count; i++)
            {
                var d = new Drone();
                d.t = new GameObject("Drone").transform;
                d.t.SetParent(transform, false);
                Add(d.t, hull, bodyMat);
                var l = new GameObject("Lamp").transform; l.SetParent(d.t, false); Add(l, lamp, lampMat);
                d.phase = i * Mathf.PI * 2f / count + R() * 0.4f;       // 均匀散开在一圈上
                d.r = radius * (0.8f + 0.4f * R());
                d.h = height * (0.9f + 0.2f * R());
                drones[i] = d;
            }
        }

        static void Add(Transform t, Mesh m, Material mat)
        {
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = m;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            float w = speed / radius * dir;
            float now = Time.time;
            for (int i = 0; i < drones.Length; i++)
            {
                ref var d = ref drones[i];
                float a = now * w + d.phase;
                float rr = d.r * (1f + 0.1f * Mathf.Sin(now * 0.11f + i * 2.1f));
                var p = new Vector3(Mathf.Cos(a) * rr, d.h + 0.004f * Mathf.Sin(now * 1.3f + i * 1.7f), Mathf.Sin(a) * rr * 0.7f);
                var vel = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a) * 0.7f) * dir;
                d.t.localPosition = p;
                d.t.localRotation = Quaternion.LookRotation(vel, Vector3.up) * Quaternion.Euler(4f, 0, -9f * dir);
            }
        }

        static Mesh MakeHull()
        {
            // 扁平的六边碟形机身 + 两侧短翼（单位：米，沿 +Z 朝前）
            const float R = 0.0036f, H = 0.0011f;
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
            var top = new Vector3(0, H, 0); var bot = new Vector3(0, -H * 0.7f, 0);
            for (int k = 0; k < 6; k++)
            {
                float a0 = k * Mathf.PI / 3f, a1 = (k + 1) * Mathf.PI / 3f;
                var p0 = new Vector3(Mathf.Sin(a0) * R * 0.8f, 0, Mathf.Cos(a0) * R * 1.25f); var p1 = new Vector3(Mathf.Sin(a1) * R * 0.8f, 0, Mathf.Cos(a1) * R * 1.25f);
                Tri(top, p0, p1); Tri(bot, p1, p0);
            }
            foreach (float s in new[] { -1f, 1f })      // 短翼：薄三角片（材质双面不需要，正反各一片）
            {
                var a = new Vector3(s * R * 0.6f, 0, R * 0.5f); var b = new Vector3(s * R * 0.6f, 0, -R * 0.9f); var c = new Vector3(s * R * 1.9f, 0.0002f, -R * 1.1f);
                if (s < 0) { Tri(a, c, b); Tri(a, b, c); } else { Tri(a, b, c); Tri(a, c, b); }
            }
            var m = new Mesh { name = "DroneHull" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh MakeLamp()
        {
            // 肚子下面的小灯：倒四棱锥
            const float r = 0.0011f;
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
            var tip = new Vector3(0, -0.0021f, 0); float y = -0.0006f;
            var q = new[] { new Vector3(r, y, r), new Vector3(r, y, -r), new Vector3(-r, y, -r), new Vector3(-r, y, r) };
            for (int k = 0; k < 4; k++) Tri(tip, q[k], q[(k + 1) % 4]);
            var m = new Mesh { name = "DroneLamp" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
