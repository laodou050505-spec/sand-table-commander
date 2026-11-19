using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 场景阶段的“摆棋”：按开局布阵把两军棋子摆上沙盘，用来看比例和画面。
    /// 有合格的 P2 预制体就用它，没有就放一个同高度的占位小人（灰色胶囊 + 队伍色底座）。
    /// 菜单 WarTable/2 摆放两军（预览）。
    /// </summary>
    public static class ArmyPreview
    {
        public const string Root = "Armies_Preview";
        /// <summary>P2 模型的正面朝向（绕 y 的角度）：导入后量出来再填。</summary>
        public static float ModelYaw = 0f;

        /// <summary>（已停用）P2 重做的棋子：用户要求角色沿用原型，这批不再使用。</summary>
        public static void ImportAll()
        {
            int n = 0;
            foreach (var d in Troops.All)
                if (WTArt.ImportFigure(d.id, d.team, d.height, d.mounted)) n++;
            Debug.Log($"[ArmyPreview] 棋子预制体 {n}/{Troops.All.Length}");
        }

        [MenuItem("WarTable/2 摆放两军（预览）")]
        public static void Build()
        {
            var old = GameObject.Find(Root);
            if (old) Object.DestroyImmediate(old);
            var board = GameObject.Find(BoardBuilder.Root);
            var root = new GameObject(Root);
            if (board) root.transform.SetParent(board.transform, false);
            var rnd = new System.Random(7);
            int real = 0, fake = 0;
            foreach (var b in Troops.Opening)
            {
                var d = Troops.Get(b.troop);
                // 用户要求：棋子沿用原型角色（Prefabs/Figures，见 DemoFigures）
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{DemoFigures.PrefabDir}/{b.troop}.prefab");
                var group = new GameObject(b.troop).transform;
                group.SetParent(root.transform, false);
                foreach (var s in Troops.LineSlots(b))
                {
                    float x = s.x + ((float)rnd.NextDouble() - 0.5f) * 0.25f, z = s.y + ((float)rnd.NextDouble() - 0.5f) * 0.25f;
                    // 落在实际地形网格上（不是高度函数：网格三角面和函数之间有几毫米差），整个棋子顺着坡面摆——像真微缩模型放在坡上
                    var (pos, up) = Ground(x, z);
                    // 我军面朝 −z（朝敌人），敌军面朝 +z；手摆的棋子略有歪斜
                    float yaw = (d.team == 0 ? 180f : 0f) + ModelYaw + ((float)rnd.NextDouble() - 0.5f) * 10f;
                    GameObject go;
                    if (pf) { go = (GameObject)PrefabUtility.InstantiatePrefab(pf, group); DemoFigures.Pose(go, b.troop, (float)rnd.NextDouble() * 0.6f); real++; }
                    else { go = Placeholder(d); go.transform.SetParent(group, false); fake++; }
                    go.transform.localPosition = pos;
                    go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0, yaw, 0);
                    // 底座顺坡贴地，人物保持直立（整个棋子跟着坡倾斜看着像要倒）
                    var fig = go.transform.Find("Figure");
                    if (fig) fig.rotation = Quaternion.FromToRotation(fig.up, go.transform.parent.up) * fig.rotation;
                }
            }
            ReleaseGround();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[ArmyPreview] P2 棋子 {real}，占位 {fake}");
        }

        static MeshCollider groundCol;

        /// <summary>地形网格上 (x, z) 码处的落点（沙盘本地米）和法线（本地）。首次调用时给地形临时加 MeshCollider，Build 结束时删掉。</summary>
        public static (Vector3 pos, Vector3 up) Ground(float x, float z)
        {
            var board = GameObject.Find(BoardBuilder.Root).transform;
            if (!groundCol) groundCol = board.Find("Terrain").gameObject.AddComponent<MeshCollider>();
            var wp = board.TransformPoint(new Vector3(x, 30f, z) * BoardMap.Yard);
            if (groundCol.Raycast(new Ray(wp, Vector3.down), out var hit, 5f))
                return (board.InverseTransformPoint(hit.point), board.InverseTransformDirection(hit.normal).normalized);
            return (new Vector3(x, BoardMap.Height(x, z), z) * BoardMap.Yard, Vector3.up);
        }

        public static void ReleaseGround() { if (groundCol) Object.DestroyImmediate(groundCol); }

        static GameObject Placeholder(Troops.Def d)
        {
            var go = new GameObject(d.id + "_placeholder");
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            float h = d.height - 0.002f, w = d.mounted ? 0.016f : 0.009f;
            body.transform.localPosition = new Vector3(0, 0.002f + h * 0.5f, 0);
            body.transform.localScale = new Vector3(w, h * 0.5f, d.mounted ? 0.03f : w);
            body.GetComponent<MeshRenderer>().sharedMaterial = BoardBuilder.Mat("M_Placeholder", "WarTable/MiniLit", m => m.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.68f)));
            var bas = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(bas.GetComponent<Collider>());
            bas.transform.SetParent(go.transform, false);
            bas.transform.localPosition = new Vector3(0, 0.001f, 0);
            bas.transform.localScale = d.mounted ? new Vector3(0.016f, 0.001f, 0.026f) : new Vector3(0.0135f, 0.001f, 0.0135f);
            bas.GetComponent<MeshRenderer>().sharedMaterial = BoardBuilder.Mat(d.team == 0 ? "M_TeamBlue" : "M_TeamRed", "WarTable/MiniLit",
                m => m.SetColor("_BaseColor", d.team == 0 ? new Color(0.16f, 0.36f, 0.78f) : new Color(0.72f, 0.14f, 0.12f)));
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }
    }
}
