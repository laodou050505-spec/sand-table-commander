using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 一小群鸟在沙盘上空绕圈（场景要有动感）：每只鸟是程序网格（身体 + 两片翅膀），沿各自略有偏差的椭圆航线滑翔，
    /// 隔一阵扑几下翅膀。所有鸟共用一个圆心和转向，互相不会撞上（同一圈上相位错开、半径和高度错开）。
    /// 尺寸按棋子比例：一只乌鸦约 0.8 cm。
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        public int count = 5;
        public float radius = 0.12f;      // 本地米
        public float height = 0.2f;
        public float speed = 0.035f;      // 米/秒
        public Color color = new Color(0.16f, 0.15f, 0.15f);
        public int seed = 1;

        struct Bird { public Transform t, wl, wr; public float phase, r, h, flap, flapT; }
        Bird[] birds;
        static Mesh body, wing;
        float dir;

        void Start()
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            dir = R() < 0.5f ? 1f : -1f;
            var mat = new Material(Shader.Find("WarTable/MiniLit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Cull", 0);
            body ??= MakeBody();
            wing ??= MakeWing();
            birds = new Bird[count];
            for (int i = 0; i < count; i++)
            {
                var b = new Bird();
                b.t = new GameObject("Bird").transform;
                b.t.SetParent(transform, false);
                Add(b.t, body, mat);
                b.wl = new GameObject("WingL").transform; b.wl.SetParent(b.t, false); Add(b.wl, wing, mat);
                b.wr = new GameObject("WingR").transform; b.wr.SetParent(b.t, false); Add(b.wr, wing, mat); b.wr.localScale = new Vector3(-1, 1, 1);
                b.phase = i * Mathf.PI * 2f / count * 0.35f + R() * 0.15f;     // 前后错开排成松散一串
                b.r = radius * (0.85f + 0.3f * R());
                b.h = height * (0.92f + 0.16f * R());
                b.flapT = R() * 3f;
                birds[i] = b;
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
            for (int i = 0; i < birds.Length; i++)
            {
                ref var b = ref birds[i];
                float a = now * w + b.phase;
                // 椭圆略压扁、半径缓慢起伏，看着不像机械转圈
                float rr = b.r * (1f + 0.12f * Mathf.Sin(now * 0.13f + i));
                var p = new Vector3(Mathf.Cos(a) * rr, b.h + 0.01f * Mathf.Sin(now * 0.4f + i * 1.7f), Mathf.Sin(a) * rr * 0.75f);
                var vel = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a) * 0.75f) * dir;
                b.t.localPosition = p;
                b.t.localRotation = Quaternion.LookRotation(vel, Vector3.up) * Quaternion.Euler(0, 0, -12f * dir);
                // 扑翼：每 3~5 秒扑 3 下，其余时间平展滑翔
                b.flapT -= Time.deltaTime;
                if (b.flapT < 0) { b.flap = 3f; b.flapT = 3f + (i * 0.37f % 2f); }
                float ang = 6f;
                if (b.flap > 0) { b.flap -= Time.deltaTime * 4f; ang = 6f + 34f * Mathf.Sin(b.flap * Mathf.PI * 2f); }
                b.wl.localRotation = Quaternion.Euler(0, 0, ang);
                b.wr.localRotation = Quaternion.Euler(0, 0, -ang);
            }
        }

        static Mesh MakeBody()
        {
            // 细长菱形身体 + 尾巴（单位：米，沿 +Z 朝前）
            const float L = 0.008f, W = 0.0012f, H = 0.001f;
            var v = new[] { new Vector3(0, 0, L * 0.5f), new Vector3(-W, 0, 0), new Vector3(W, 0, 0), new Vector3(0, H, 0), new Vector3(0, -H * 0.6f, 0), new Vector3(0, 0, -L * 0.3f),
                            new Vector3(-W * 1.4f, 0, -L * 0.5f), new Vector3(W * 1.4f, 0, -L * 0.5f) };
            var t = new[] { 0, 1, 3, 0, 3, 2, 0, 4, 1, 0, 2, 4, 5, 3, 1, 5, 2, 3, 5, 1, 4, 5, 4, 2, 5, 6, 7 };
            var m = new Mesh { name = "BirdBody", vertices = v, triangles = t };
            m.RecalculateNormals();
            return m;
        }

        static Mesh MakeWing()
        {
            // 左翼（右翼镜像）：根部在身体两侧，翼尖后掠
            var v = new[] { new Vector3(0, 0, 0.0015f), new Vector3(0, 0, -0.0015f), new Vector3(-0.0055f, 0, -0.0012f), new Vector3(-0.0035f, 0, 0.0012f) };
            var m = new Mesh { name = "BirdWing", vertices = v, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            m.RecalculateNormals();
            return m;
        }
    }
}
