using UnityEngine;

namespace WarTable.EditorTools
{
    /// <summary>编辑器截图：临时相机渲染到 PNG。默认机位 = 坐在桌前的玩家（眼睛离桌面约 42 cm、离沙盘中心约 62 cm）。</summary>
    public static class WTCapture
    {
        public static readonly Vector3 SeatEye = new Vector3(0f, 0.46f, 0.84f);

        public static string Shot(string path, Vector3 pos, Vector3 lookAt, float fov = 50f, int w = 1600, int h = 1000, Color? bg = null)
        {
            var go = new GameObject("__wtcap");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(lookAt - pos);
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = bg ?? new Color(0.78f, 0.76f, 0.72f);
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                return path;
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static string Seat(string path, float fov = 55f) => Shot(path, SeatEye, new Vector3(0f, -0.02f, -0.06f), fov);
    }
}
