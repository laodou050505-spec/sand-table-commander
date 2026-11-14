using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 帅旗（用户要求“更醒目、更鲜艳”）：石座 + 高旗杆 + 金枪头 + 燕尾旗面，旗面用 WarTable/FlagWave 随风飘。
    /// 纹章图是 nano banana 生成的 ArtSource/Banners/standard_*.png（拷到 Art/Textures）。尺寸单位：沙盘本地米（1 码 = 1 cm）。
    /// </summary>
    public static class StandardBuilder
    {
        public const float PoleH = 0.165f, FlagW = 0.105f, FlagH = 0.068f, Notch = 0.026f;

        public static GameObject Build(string team, Transform parent, bool plinth = true)
        {
            var root = new GameObject("Standard_" + team);
            root.transform.SetParent(parent, false);
            bool kd = team == "kingdom";

            var stone = BoardBuilder.Mat("M_StdStone", "WarTable/MiniLit", m => { m.SetColor("_BaseColor", new Color(0.55f, 0.53f, 0.5f)); m.SetFloat("_Gloss", 0.05f); });
            var wood = BoardBuilder.Mat("M_StdPole", "WarTable/MiniLit", m => { m.SetColor("_BaseColor", kd ? new Color(0.36f, 0.24f, 0.15f) : new Color(0.24f, 0.17f, 0.12f)); m.SetFloat("_Gloss", 0.15f); });
            var gold = BoardBuilder.Mat("M_StdGold", "WarTable/MiniLit", m => { m.SetColor("_BaseColor", new Color(0.95f, 0.72f, 0.25f)); m.SetFloat("_Gloss", 0.9f); m.SetFloat("_GlossSize", 64f); m.SetFloat("_RimStrength", 0.4f); });
            var iron = BoardBuilder.Mat("M_StdIron", "WarTable/MiniLit", m => { m.SetColor("_BaseColor", new Color(0.3f, 0.3f, 0.32f)); m.SetFloat("_Gloss", 0.6f); });

            // 石座：一块矮圆台 + 几块压脚石（封面上插在兵符上的小旗不要）
            if (plinth) Prim(PrimitiveType.Cylinder, root.transform, stone, new Vector3(0, 0.002f, 0), new Vector3(0.022f, 0.002f, 0.022f));
            if (plinth) Prim(PrimitiveType.Cylinder, root.transform, stone, new Vector3(0, 0.0055f, 0), new Vector3(0.014f, 0.0016f, 0.014f));
            for (int i = 0; i < (plinth ? 5 : 0); i++)
            {
                float a = i * 1.257f + 0.3f;
                Prim(PrimitiveType.Sphere, root.transform, stone, new Vector3(Mathf.Cos(a) * 0.0075f, 0.0075f, Mathf.Sin(a) * 0.0075f), new Vector3(0.006f, 0.004f, 0.005f) * (0.8f + 0.1f * (i % 3)));
            }
            // 旗杆 + 金箍 + 枪头
            Prim(PrimitiveType.Cylinder, root.transform, wood, new Vector3(0, PoleH * 0.5f, 0), new Vector3(0.0034f, PoleH * 0.5f, 0.0034f));
            Prim(PrimitiveType.Cylinder, root.transform, gold, new Vector3(0, PoleH - 0.004f, 0), new Vector3(0.0048f, 0.0012f, 0.0048f));
            Prim(PrimitiveType.Sphere, root.transform, gold, new Vector3(0, PoleH + 0.002f, 0), Vector3.one * 0.0062f);
            // 菱形枪头：外壳拉长 y，里面放一个转 45° 的扁方块
            var tipHolder = new GameObject("Spearhead").transform;
            tipHolder.SetParent(root.transform, false);
            tipHolder.localPosition = new Vector3(0, PoleH + 0.0115f, 0);
            tipHolder.localScale = new Vector3(1f, 2.4f, 1f);
            Prim(PrimitiveType.Cube, tipHolder, kd ? gold : iron, Vector3.zero, new Vector3(0.0042f, 0.0042f, 0.0012f), Quaternion.Euler(0, 0, 45));
            // 旗面靠杆的一边用三道金箍系在旗杆上（不加横杆：旗面整面在飘，静止的横杆会和旗面脱开）
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cylinder, root.transform, gold, new Vector3(0, PoleH - 0.008f - FlagH * (0.08f + 0.42f * i), 0), new Vector3(0.0044f, 0.0009f, 0.0044f));

            // 旗面
            var tex = BoardBuilder.Tex("standard_" + team);
            var flagMat = BoardBuilder.Mat("M_Standard_" + team, "WarTable/FlagWave", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", tex ? Color.white : (kd ? new Color(0.1f, 0.25f, 0.75f) : new Color(0.7f, 0.08f, 0.06f)));
                m.SetFloat("_Width", FlagW); m.SetFloat("_Amp", 0.11f); m.SetFloat("_WaveLen", 0.85f); m.SetFloat("_Speed", kd ? 2.3f : 2.1f);
            });
            var flag = new GameObject("Flag");
            flag.transform.SetParent(root.transform, false);
            flag.transform.localPosition = new Vector3(0.0025f, PoleH - 0.008f - FlagH, 0);
            flag.AddComponent<MeshFilter>().sharedMesh = FlagMesh("Flag_" + team);
            var mr = flag.AddComponent<MeshRenderer>();
            mr.sharedMaterial = flagMat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            // 摆动会超出静态包围盒，放大一点免得被视锥剔除
            flag.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(new Vector3(FlagW * 0.5f, FlagH * 0.5f, 0), new Vector3(FlagW * 1.2f, FlagH * 1.4f, FlagW * 0.5f));
            return root;
        }

        /// <summary>燕尾旗面网格：每一行按燕尾形状收窄（行内均匀分布），u = x / 宽，v = y / 高。</summary>
        static Mesh FlagMesh(string name)
        {
            const int C = 36, R = 20;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= R; j++)
            {
                float vv = (float)j / R;
                float xmax = FlagW - Notch * (1f - Mathf.Abs(2f * vv - 1f));
                for (int i = 0; i <= C; i++)
                {
                    float x = xmax * i / C;
                    v.Add(new Vector3(x, vv * FlagH, 0));
                    uv.Add(new Vector2(x / FlagW, vv));
                }
            }
            for (int j = 0; j < R; j++)
                for (int i = 0; i < C; i++)
                {
                    int a = j * (C + 1) + i, b = a + 1, c = a + C + 1, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });
                }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            // 已有就复用：删了重建会让场景里别的帅旗（布景里的、封面上的）引用断掉
            string path = $"Assets/_WarTable/Art/Generated/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { existing.Clear(); EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
