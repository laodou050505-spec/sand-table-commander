using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WarTable.Game
{
    /// <summary>
    /// 放大模式的舞台。
    /// 11-17 用户：放大改成真人比例（兵和人一样大），**去掉天空盒，就用真实空间**——8 倍 + 天空盒 + 延伸大地的那一版（11-16）作废。
    /// 所以这里只剩几件事：
    ///  · 藏掉边框这些“桌面摆件”（放大后是几十米长的一圈墙）；
    ///  · 头显相机的远裁剪面从 30 m 拉到 600 m（战场有一百多米；不拉的话远处被一个跟着头转的平面切掉，看着晕）；
    ///  · 阴影距离从 2.2 m 拉到 45 m（不拉的话阴影的边界跟着头在地上扫）；退出时都还原；
    ///  · 切换时黑场淡出 0.25 秒 → 切换 → 淡入 0.4 秒（一帧硬切很突兀）。
    /// 透视一直开着：没有东西的地方就是真实房间。
    /// </summary>
    public class ImmersiveStage : MonoBehaviour
    {
        public Transform board;
        public Camera xrCam, editorCam;
        [Tooltip("放大时藏起来的东西：边框")] public GameObject[] tableOnly = new GameObject[0];
        [Tooltip("切换时盖在眼前的黑片的材质（无光照、永远画在最上面）")] public Material fadeMat;

        public bool On { get; private set; }
        /// <summary>正在淡出 / 淡入（这期间不接受再次切换）。</summary>
        public bool Switching { get; private set; }

        const float FarClip = 600f, ShadowDist = 45f;
        MaterialPropertyBlock mpb;
        bool saved; float farXr, farEd, shadowWas;
        Transform fadeXr, fadeEd; Renderer fadeXrR, fadeEdR;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        static UniversalRenderPipelineAsset Urp => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        /// <summary>带过渡的切换：黑场淡出 → 执行 change（放大 / 缩回沙盘，返回切换之后是不是在放大模式）→ 淡入。</summary>
        public void Switch(System.Func<bool> change)
        {
            if (Switching || !isActiveAndEnabled) return;
            StartCoroutine(Run(change));
        }

        System.Collections.IEnumerator Run(System.Func<bool> change)
        {
            Switching = true;
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime) { Fade(t / 0.25f); yield return null; }
            Fade(1f);
            Set(change());
            yield return null; yield return null;   // 等两帧：沙盘、指挥台都摆到位了再亮
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) { Fade(1f - t / 0.4f); yield return null; }
            Fade(0f);
            Switching = false;
        }

        /// <summary>眼前的黑片：a = 0 收起，1 全黑。头显相机和编辑器相机各挂一片（离眼 6 cm，比什么都近）。</summary>
        void Fade(float a)
        {
            if (!fadeMat) return;
            One(xrCam, ref fadeXr, ref fadeXrR); One(editorCam, ref fadeEd, ref fadeEdR);
            void One(Camera cam, ref Transform t, ref Renderer r)
            {
                if (!cam) return;
                if (!t)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                    q.name = "ImmersiveFade"; t = q.transform; t.SetParent(cam.transform, false);
                    t.localPosition = new Vector3(0f, 0f, 0.06f); t.localScale = new Vector3(0.6f, 0.6f, 1f);
                    r = q.GetComponent<Renderer>(); r.sharedMaterial = fadeMat; r.shadowCastingMode = ShadowCastingMode.Off;
                }
                bool show = a > 0.001f;
                if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
                if (!show) return;
                mpb ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb); mpb.SetColor(ColorId, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, a))); r.SetPropertyBlock(mpb);
            }
        }

        public void Set(bool on)
        {
            if (On == on) return;
            On = on;
            if (on && !saved)
            {
                saved = true;
                if (xrCam) farXr = xrCam.farClipPlane;
                if (editorCam) farEd = editorCam.farClipPlane;
                if (Urp) shadowWas = Urp.shadowDistance;
            }
            foreach (var g in tableOnly) if (g && g.activeSelf == on) g.SetActive(!on);
            if (!saved) return;
            if (xrCam) xrCam.farClipPlane = on ? FarClip : farXr;
            if (editorCam) editorCam.farClipPlane = on ? FarClip : farEd;
            if (Urp) Urp.shadowDistance = on ? ShadowDist : shadowWas;
        }

        void OnDestroy()
        {
            // 管线资源上的阴影距离是运行时改的：退出时改回去（编辑器里不然会把改动留在资源上）
            if (saved && Urp) Urp.shadowDistance = shadowWas;
        }
    }
}
