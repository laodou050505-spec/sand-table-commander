using ByteDance.PICO.XR;
using UnityEngine;

namespace WarTable.XR
{
    /// <summary>
    /// 手部遮挡（11-04 用户：手不要被沙盘挡住，手应该在沙盘上）。每只手 26 个关节：每个关节一个小球，相邻关节之间一段胶囊，
    /// 半径用 SDK 给的关节半径（再放大一点，边缘不会露出沙盘的毛边）。材质 WarTable/HandOccluder：只写深度和透明度 0，
    /// 不画颜色——手前面的沙盘、棋子都被挡掉，露出透视画面里的真手，看起来手就在沙盘上面。
    /// 只在头显上工作；单个关节暂时无效沿用 0.3 秒内的旧位置，整只手都丢了才藏起来；另有掌心椭球。11-08 用户：只要手的轮廓，不要小臂。
    /// </summary>
    public class HandOccluder : MonoBehaviour
    {
        public MRInput input;
        public Material material;
        [Tooltip("关节半径放大倍数")] public float radiusScale = 1.6f;   // 11-08 用户：手的任何部分都不能漏 → 加粗（真手画面比关节数据慢一点，边缘要留余量）

        // 骨骼：每根手指 掌根 → 指尖，拇指从掌骨起；掌面用几段横向连起来（不然手掌中间会漏）
        static readonly int[][] Chains =
        {
            new[] { 1, 2, 3, 4, 5 },          // 腕 → 拇指
            new[] { 1, 6, 7, 8, 9, 10 },      // 腕 → 食指
            new[] { 1, 11, 12, 13, 14, 15 },  // 中指
            new[] { 1, 16, 17, 18, 19, 20 },  // 无名指
            new[] { 1, 21, 22, 23, 24, 25 },  // 小指
            new[] { 7, 12, 17, 22 },          // 指根横连（掌面上沿）
            new[] { 2, 6, 11, 16, 21 },       // 掌骨横连（掌面下沿）
            new[] { 0, 7 }, new[] { 0, 22 }, new[] { 0, 2 },
            // 11-08：掌面补满（掌心到各指根、相邻掌骨交叉连）
            new[] { 0, 12 }, new[] { 0, 17 }, new[] { 0, 6 }, new[] { 0, 11 }, new[] { 0, 16 }, new[] { 0, 21 },
            new[] { 2, 7 }, new[] { 6, 12 }, new[] { 11, 17 }, new[] { 16, 22 }, new[] { 3, 7 },
        };

        class HandRig
        {
            public Transform root, palm; public Transform[] joints; public Transform[] bones; public (int a, int b)[] pairs;
            public Vector3[] last; public float[] lastT; public float[] lastR;   // 关节暂时丢失时沿用最后一次的位置（0.3 秒内）
        }
        const float KeepLost = 0.3f;
        HandRig left, right;
        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
        Mesh sphere, capsule;

        void Start()
        {
            if (!material) material = new Material(Shader.Find("WarTable/HandOccluder"));
            sphere = Prim(PrimitiveType.Sphere); capsule = Prim(PrimitiveType.Cylinder);
            left = Build("OccluderL"); right = Build("OccluderR");
        }

        static Mesh Prim(PrimitiveType t)
        {
            var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); return m;
        }

        HandRig Build(string name)
        {
            var rig = new HandRig { root = new GameObject(name).transform };
            rig.root.SetParent(transform, false);
            int n = (int)HandJoint.JointMax;
            rig.joints = new Transform[n];
            for (int i = 0; i < n; i++) rig.joints[i] = Part(rig.root, "J" + i, sphere);
            var pairs = new System.Collections.Generic.List<(int, int)>();
            foreach (var c in Chains) for (int i = 0; i + 1 < c.Length; i++) pairs.Add((c[i], c[i + 1]));
            rig.pairs = pairs.ToArray();
            rig.bones = new Transform[rig.pairs.Length];
            for (int i = 0; i < rig.bones.Length; i++) rig.bones[i] = Part(rig.root, "B" + i, capsule);
            rig.palm = Part(rig.root, "Palm", sphere);      // 手掌：按掌宽 / 掌长拉伸的椭球，把掌心填满
            rig.last = new Vector3[n]; rig.lastT = new float[n]; rig.lastR = new float[n];
            for (int i = 0; i < n; i++) rig.lastT[i] = -9f;
            rig.root.gameObject.SetActive(false);
            return rig;
        }

        Transform Part(Transform parent, string name, Mesh mesh)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g.transform;
        }

        void LateUpdate()
        {
            bool on = input && input.Active;
            Pose(left, HandType.HandLeft, on);
            Pose(right, HandType.HandRight, on);
        }

        void Pose(HandRig rig, HandType type, bool on)
        {
            if (rig == null) return;
            bool got = on && PXR_HandTracking.GetJointLocations(type, ref locs) && locs.isActive != 0 && locs.jointLocations != null;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            int n = rig.joints.Length; float now = Time.time;
            var p = new Vector3[n]; var rad = new float[n]; var ok = new bool[n];
            int okCount = 0;
            for (int i = 0; i < n; i++)
            {
                // 11-08 用户：手的任何部分都不能漏——原来只要一个关节无效（握拳、手指互相挡住时常见）整只手的遮挡就关掉，沙盘盖住整只手。
                // 现在逐个关节：有效就更新；暂时无效沿用 0.3 秒内最后一次的位置；更久的才藏掉这个关节和连着它的骨段
                if (got && ((ulong)locs.jointLocations[i].locationStatus & need) != 0)
                {
                    var l = locs.jointLocations[i];
                    rig.last[i] = input.origin ? input.origin.TransformPoint(l.pose.Position.ToVector3()) : l.pose.Position.ToVector3();
                    rig.lastR[i] = Mathf.Max(0.0085f, l.radius) * radiusScale; rig.lastT[i] = now;
                }
                ok[i] = now - rig.lastT[i] < KeepLost;
                p[i] = rig.last[i]; rad[i] = rig.lastR[i];
                if (ok[i]) okCount++;
            }
            if (okCount == 0) { if (rig.root.gameObject.activeSelf) rig.root.gameObject.SetActive(false); return; }
            if (!rig.root.gameObject.activeSelf) rig.root.gameObject.SetActive(true);
            for (int i = 0; i < n; i++)
            {
                var jt = rig.joints[i];
                if (!ok[i]) { jt.localScale = Vector3.zero; continue; }
                jt.position = p[i]; jt.localScale = Vector3.one * rad[i] * 2f;
            }
            for (int i = 0; i < rig.pairs.Length; i++)
            {
                var (a, b) = rig.pairs[i]; var t = rig.bones[i];
                if (!ok[a] || !ok[b]) { t.localScale = Vector3.zero; continue; }
                var d = p[b] - p[a]; float len = d.magnitude;
                if (len < 1e-4f) { t.localScale = Vector3.zero; continue; }
                float r = Mathf.Max(rad[a], rad[b]);   // 取较粗的一端，骨段之间不留缝
                t.position = (p[a] + p[b]) * 0.5f;
                t.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
                t.localScale = new Vector3(r * 2f, len * 0.5f, r * 2f);   // Unity 圆柱高 2，半高 = 长度一半
            }
            // 手掌椭球：宽 = 食指根到小指根 + 余量，长 = 手腕到中指根，厚 3.5 cm
            const int Palm = 0, Wrist = 1, IdxProx = 7, MidProx = 12, LitProx = 22;
            if (ok[Wrist] && ok[MidProx] && ok[IdxProx] && ok[LitProx])
            {
                var fwd = p[MidProx] - p[Wrist]; var side = p[LitProx] - p[IdxProx];
                var up = Vector3.Cross(fwd, side);
                if (fwd.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-8f)
                {
                    var c = (ok[Palm] ? p[Palm] : (p[Wrist] + p[MidProx]) * 0.5f);
                    rig.palm.SetPositionAndRotation(c, Quaternion.LookRotation(fwd.normalized, up.normalized));
                    rig.palm.localScale = new Vector3(side.magnitude + 0.03f, 0.035f, fwd.magnitude + 0.02f);
                }
                else rig.palm.localScale = Vector3.zero;
            }
            else rig.palm.localScale = Vector3.zero;
        }
    }
}
