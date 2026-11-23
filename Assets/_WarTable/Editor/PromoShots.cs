using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 11-19 Vibe Lab 宣传图（图标 / 海报 / 详情截图）用的临时截图助手，做完可删。
    /// 游戏画面一律引擎实拍：黑底、白底各拍一张 → Python 里差分取透明度，再叠到“白模房间转写实照片”的背景上（MR 透视）。
    /// </summary>
    public static class PromoShots
    {
        public const int W = 2560, H = 1440;

        static GameObject Root(string n) => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == n);

        /// <summary>拍一对图（黑底 / 白底）。hideNames：拍的时候临时隐藏的物体名（任意层级）。</summary>
        public static string Pair(string path, Vector3 pos, Vector3 look, float fov, string hideNames = "Dimmer")
        {
            var hide = new List<GameObject>();
            var names = hideNames.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet();
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (names.Contains(t.name) && t.gameObject.activeSelf) hide.Add(t.gameObject);
            try
            {
                foreach (var g in hide) g.SetActive(false);
                WTCapture.Shot(path + "_k.png", pos, look, fov, W, H, Color.black);
                WTCapture.Shot(path + "_w.png", pos, look, fov, W, H, Color.white);
            }
            finally { foreach (var g in hide) g.SetActive(true); }
            return path + " 隐藏了 " + hide.Count + " 件";
        }

        /// <summary>只拍白模房间（桌子 + 临时搭的墙和地板），游戏内容全部隐藏。</summary>
        public static string Room(string path, Vector3 pos, Vector3 look, float fov)
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var was = roots.ToDictionary(g => g, g => g.activeSelf);
            var made = new List<GameObject>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.8f));
            GameObject Box(string n, Vector3 c, Vector3 s)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "__room_" + n; g.transform.position = c; g.transform.localScale = s;
                g.GetComponent<Renderer>().sharedMaterial = mat; made.Add(g); return g;
            }
            try
            {
                foreach (var g in roots) g.SetActive(g.name == "PreviewOnly" || g.name == "Sun");
                var desk = Root("PreviewOnly"); foreach (Transform t in desk.transform) t.gameObject.SetActive(t.name == "Desk");
                Box("Floor", new Vector3(0f, -0.78f, 0f), new Vector3(8f, 0.02f, 8f));
                Box("BackWall", new Vector3(0f, 0.6f, -1.9f), new Vector3(8f, 3f, 0.05f));
                Box("LeftWall", new Vector3(-2.4f, 0.6f, 0f), new Vector3(0.05f, 3f, 8f));
                Box("RightWall", new Vector3(2.4f, 0.6f, 0f), new Vector3(0.05f, 3f, 8f));
                Box("Shelf", new Vector3(1.4f, -0.1f, -1.7f), new Vector3(1.2f, 1.4f, 0.35f));
                Box("Cabinet", new Vector3(-1.3f, -0.35f, -1.7f), new Vector3(1.4f, 0.85f, 0.4f));
                var l = new GameObject("__room_light").AddComponent<Light>(); l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(50f, -30f, 0f); l.intensity = 1.1f; made.Add(l.gameObject);
                WTCapture.Shot(path, pos, look, fov, W, H, new Color(0.9f, 0.9f, 0.92f));
            }
            finally
            {
                foreach (var g in made) Object.DestroyImmediate(g);
                foreach (var kv in was) kv.Key.SetActive(kv.Value);
                var desk = Root("PreviewOnly"); if (desk) foreach (Transform t in desk.transform) t.gameObject.SetActive(true);
            }
            return path;
        }
    }
}
