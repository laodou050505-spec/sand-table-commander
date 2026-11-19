using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace WarTable.XR
{
    /// <summary>
    /// MR 启动（照《梦境玩具战》MRBootstrap）：头显上开启 PICO 视频透视（相机清屏为全透明，真实房间就是背景），追踪原点设为地面。
    /// 应用从后台恢复时 PICO 会关掉透视，需要重新打开。编辑器里不做任何事。
    /// </summary>
    public class MRRig : MonoBehaviour
    {
        public Camera cam;
        float retry;
        bool floorSet;

        void Start() => Apply();
        void OnApplicationPause(bool paused) { if (!paused) Apply(); }

        /// <summary>追踪原点已经设成地面（y = 0 是真实地板）。沉浸模式把战场铺在地板上要用。</summary>
        public static bool FloorSet { get; private set; }
        /// <summary>视频透视开着（看得到真实房间）。沉浸模式（11-16）关掉透视、相机画天空盒，玩家像站在战场上。</summary>
        public bool Passthrough { get; private set; } = true;
        public void SetPassthrough(bool on) { if (Passthrough == on) return; Passthrough = on; Apply(); }

        void Apply()
        {
            if (Application.isEditor || !XRSettings.isDeviceActive) return;
            ByteDance.PICO.XR.PXR_Manager.EnableVideoSeeThrough = Passthrough;
            if (!cam) return;
            if (Passthrough) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); }
            else { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; }   // 沉浸：不透明底色（天空是 ImmersiveStage 的天球，底色也由它改成地平线色）
        }

        void Update()
        {
            if (floorSet || !XRSettings.isDeviceActive) return;
            retry -= Time.unscaledDeltaTime;
            if (retry > 0) return;
            retry = 1f;
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs)
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                    floorSet |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            FloorSet = floorSet;
        }
    }
}
