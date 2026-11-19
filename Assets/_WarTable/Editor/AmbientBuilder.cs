using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 场景里会动的小东西（用户反馈过“场景必须有动态”）：篝火的火苗和烟、几群绕圈的鸟。
    /// 风车叶轮在 PropBuilder 里，河水流动在 ResinWater 着色器里。由 PropBuilder.Build 最后调用。
    /// </summary>
    public static class AmbientBuilder
    {
        public const string Root = "Ambient";
        const string PuffPath = "Assets/_WarTable/Art/Generated/SoftPuff.png";

        public static void Build(Transform board)
        {
            var old = board.Find(Root);
            if (old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(Root).transform;
            root.SetParent(board, false);

            var puff = PuffTexture();
            var smokeMat = BoardBuilder.Mat("M_Smoke", "WarTable/SoftParticle", m => { m.SetTexture("_MainTex", puff); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); });
            var fireMat = BoardBuilder.Mat("M_Flame", "WarTable/SoftParticle", m => { m.SetTexture("_MainTex", puff); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One); });

            int fires = 0;
            var props = board.Find(PropBuilder.Root);
            if (props)
                foreach (Transform t in props)
                {
                    if (!t.name.StartsWith("Campfire")) continue;
                    var b = Bounds(t);
                    var p = board.InverseTransformPoint(new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.center.z));
                    Smoke(root, p, smokeMat);
                    Flame(root, p, fireMat);
                    fires++;
                }

            // 鸟群：对岸树林上空、村庄上空、战场高空各一群（码 → 本地米）
            Flock(root, new Vector3(53, 0, -22), 6, 0.10f, 0.20f, 5);
            Flock(root, new Vector3(-50, 0, 8), 4, 0.08f, 0.17f, 9);
            Flock(root, new Vector3(4, 0, -6), 5, 0.26f, 0.34f, 13);
            Debug.Log($"[Ambient] 篝火 {fires}，鸟群 3");
        }

        /// <summary>
        /// 未来时代的环境动态（11-12 用户：“注意别把场景做得太空”；飞鸟、炊烟在未来时代藏起来了）：巡逻无人机——
        /// 光棱大营和村子上空是白机身天青灯，蜂群大营上空是黑机身紫灯。单独一组 "AmbientFuture"（挂 EraOnly，只在未来时代出现）。
        /// </summary>
        public static void BuildFuture(Transform board)
        {
            var old = board.Find("AmbientFuture");
            if (old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("AmbientFuture").transform;
            root.SetParent(board, false);
            void Drones(Vector3 yards, int n, float r, float h, int seed, bool swarm)
            {
                var go = new GameObject("Drones");
                go.transform.SetParent(root, false);
                go.transform.localPosition = yards * BoardMap.Yard;
                var f = go.AddComponent<DroneFlock>();
                f.count = n; f.radius = r; f.height = h; f.seed = seed;
                if (swarm) { f.body = new Color(0.2f, 0.19f, 0.27f); f.glow = new Color(0.72f, 0.42f, 1f); }
            }
            // 圆心和半径要保证整圈都在木框以内（11-13 运行时看到有几架飞到沙盘外面去了）：
            // DroneFlock 的实际半径最大是 radius × 1.2（每架的偏差）× 1.1（缓慢起伏）= 1.32 倍，Z 方向再 × 0.7；木框内沿是 X ±60 码、Z ±50 码，留 4 码余量
            void Safe(Vector3 c, int n, float r, float h, int seed, bool swarm)
            {
                float ex = r * 1.32f / BoardMap.Yard, ez = ex * 0.7f;
                if (Mathf.Abs(c.x) + ex > BoardMap.HalfX - 4f || Mathf.Abs(c.z) + ez > BoardMap.HalfZ - 4f)
                    Debug.LogError($"[Ambient] 无人机航线会飞出木框：圆心 ({c.x},{c.z}) 半径 {r}");
                Drones(c, n, r, h, seed, swarm);
            }
            Safe(new Vector3(2, 0, 36), 3, 0.10f, 0.13f, 3, false);        // 光棱大营
            Safe(new Vector3(-46, 0, 8), 3, 0.07f, 0.15f, 7, false);       // 村子（居住舱）
            Safe(new Vector3(0, 0, -36), 3, 0.10f, 0.13f, 11, true);       // 蜂群大营
            Safe(new Vector3(46, 0, -24), 2, 0.07f, 0.17f, 15, true);      // 对岸
            { var aeo = root.gameObject.AddComponent<WarTable.Game.EraOnly>(); aeo.era = 3; aeo.alsoIn = new[] { 4 }; }   // 深空风格共用巡逻无人机
            root.gameObject.SetActive(false);
        }

        static void Flock(Transform root, Vector3 yards, int n, float r, float h, int seed)
        {
            var go = new GameObject("Birds");
            go.transform.SetParent(root, false);
            go.transform.localPosition = yards * BoardMap.Yard;
            var f = go.AddComponent<BirdFlock>();
            f.count = n; f.radius = r; f.height = h; f.seed = seed;
        }

        static ParticleSystem NewPS(Transform root, string name, Vector3 local, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);     // 发射方向朝上
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;
            main.prewarm = true;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.0012f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Smoke(Transform root, Vector3 p, Material mat)
        {
            var ps = NewPS(root, "Smoke", p, mat);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.008f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.005f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.62f, 0.6f, 0.58f, 0.38f);
            main.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 4f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 4f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.75f, 0.72f, 0.7f), 0), new GradientColorKey(new Color(0.9f, 0.9f, 0.9f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            // 微风：烟往玩家左后方慢慢飘
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.0015f, 0.003f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(-0.002f, -0.0008f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        }

        static void Flame(Transform root, Vector3 p, Material mat)
        {
            var ps = NewPS(root, "Flame", p, mat);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.004f, 0.007f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0018f, 0.0028f);
            main.startColor = new Color(1f, 0.62f, 0.2f, 0.9f);
            main.maxParticles = 20;
            var em = ps.emission; em.rateOverTime = 22f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0), new GradientColorKey(new Color(0.9f, 0.25f, 0.08f), 1) },
                      new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        /// <summary>64×64 柔和圆点（中心不透明、边缘渐隐），烟和火苗共用。</summary>
        static Texture2D PuffTexture()
        {
            if (!System.IO.File.Exists(PuffPath))
            {
                var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f;
                        float a = Mathf.Clamp01(1f - d); a = a * a * (3 - 2 * a);
                        t.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                System.IO.File.WriteAllBytes(PuffPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(PuffPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(PuffPath);
                imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PuffPath);
        }

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
