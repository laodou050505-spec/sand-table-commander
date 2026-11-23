using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 编辑器自由视角的状态。打包后的游戏里相机不存在，这里永远是 false / null，View() 原样返回传进来的相机。
    /// 游戏里所有“鼠标 → 射线”的地方都走 View()：自由视角开着时用正在显示的那台相机发射线，看到哪里就能点到哪里。
    /// </summary>
    public static class FlyCam
    {
        public static bool On;
        public static Camera Cam;
        /// <summary>正按住右键飞行：这时 WASD / QE 归相机，游戏快捷键暂停。</summary>
        public static bool Looking;
        /// <summary>这一帧右键“点了一下没拖”（自由视角下右键拖动是转视角，单击才算戳兵）。</summary>
        public static bool RightClick;
        public static Camera View(Camera fallback) => On && Cam ? Cam : fallback;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器专用的自由相机（11-23：用户暂时没有头显，先在 Unity 的 Game 窗口里看效果）。
    /// 整个类只在编辑器里编译，打进安装包的代码里没有它，头显视角不受任何影响。
    /// 进入运行模式自动生成并默认打开（视野 80°，接近头显看到的范围，先对准封面）；它是另开的一台相机，
    /// 叠在座位相机（SeatCamera）上面，座位相机本身不动——封面会跟着座位相机走，所以不能直接挪座位相机。
    /// 操作（和 Scene 窗口一样）：按住右键 = 转视角，同时 WASD 前后左右、Q / E 降 / 升、Shift 加速、滚轮调速度；
    /// 没按右键时滚轮 = 前后推拉；中键拖动 = 平移；Backspace 回到起始视角；Tab 在自由视角 / 座位视角之间切换。
    /// 左键照常点按钮、拖令牌；没按右键时键盘快捷键照常。
    /// </summary>
    public class EditorFlyCam : MonoBehaviour
    {
        const float Fov = 80f;
        Camera cam, seat;
        float yaw, pitch, speed = 3f;
        bool placed, touched;
        Vector2 rightDownAt; float rightDrag;
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            FlyCam.On = false; FlyCam.Cam = null; FlyCam.Looking = FlyCam.RightClick = false;
            var go = new GameObject("[编辑器自由相机]");   // 别加 HideFlags.DontSave：那样退出运行模式不会销毁，每玩一次多一台相机
            go.AddComponent<EditorFlyCam>();
        }

        void Start()
        {
            if (!UnityEngine.XR.XRSettings.isDeviceActive) Toggle(true);   // 默认就开：座位相机视野窄，封面上下会被切掉
        }

        Camera Seat()
        {
            if (seat && seat.isActiveAndEnabled) return seat;
            var game = FindAnyObjectByType<WarGame>();
            seat = game && game.previewCamera ? game.previewCamera : Camera.main;
            return seat;
        }

        void Toggle(bool on)
        {
            FlyCam.On = on;
            if (!cam)
            {
                cam = gameObject.AddComponent<Camera>();
                cam.enabled = false;
                FlyCam.Cam = cam;
            }
            var s = Seat();
            if (on && s)
            {
                cam.CopyFrom(s);
                cam.targetTexture = null;
                cam.depth = s.depth + 10f;
                cam.nearClipPlane = 0.02f; cam.farClipPlane = 500f;   // 座位相机只看 20 m，机甲在 10 m 开外，绕远一点就会被切掉
                cam.fieldOfView = Fov;
                var src = s.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                var dst = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                if (src) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; }
                if (!placed) ResetView();
            }
            cam.enabled = on;
            if (!on) FlyCam.Looking = false;
        }

        /// <summary>起始视角：站在座位相机的位置，平视、正对封面（封面没开就照座位相机的朝向）。</summary>
        void ResetView()
        {
            var s = Seat(); if (!s) return;
            var pos = s.transform.position;
            var cover = FindAnyObjectByType<MenuCover>();
            Vector3 dir = cover && cover.isActiveAndEnabled ? cover.transform.position - pos : s.transform.forward;
            if (dir.sqrMagnitude < 1e-4f) dir = s.transform.forward;
            Place(pos, Quaternion.LookRotation(dir.normalized, Vector3.up));
            touched = false;
        }

        void Place(Vector3 pos, Quaternion rot)
        {
            transform.SetPositionAndRotation(pos, rot);
            var e = rot.eulerAngles; yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
            placed = true;
        }

        /// <summary>脚本自检用：打开自由视角并把相机摆到 pos、看向 target。</summary>
        public Camera Look(Vector3 pos, Vector3 target)
        {
            if (!FlyCam.On) Toggle(true);
            Place(pos, Quaternion.LookRotation(target - pos, Vector3.up)); touched = true;
            return cam;
        }

        void Update()
        {
            FlyCam.RightClick = false;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var ms = UnityEngine.InputSystem.Mouse.current;
            if (kb == null || ms == null) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) { if (FlyCam.On) Toggle(false); return; }   // 编辑器里连着头显串流时不抢视角
            if (kb.tabKey.wasPressedThisFrame) Toggle(!FlyCam.On);
            if (!FlyCam.On) return;

            var s = Seat();
            if (s) { cam.clearFlags = s.clearFlags; cam.backgroundColor = s.backgroundColor; }   // 沉浸模式会改底色，跟着座位相机走
            if (kb.backspaceKey.wasPressedThisFrame) ResetView();
            if (!touched && Time.timeSinceLevelLoad < 3f) ResetView();   // 开场头几秒封面还在就位，一直对准它，直到玩家自己动了相机

            float dt = Time.unscaledDeltaTime;   // 游戏暂停 / 慢放时相机照样能动
            var mp = ms.position.ReadValue();
            if (ms.rightButton.wasPressedThisFrame) { rightDownAt = mp; rightDrag = 0f; }
            FlyCam.Looking = ms.rightButton.isPressed;
            if (FlyCam.Looking)
            {
                var d = ms.delta.ReadValue();
                rightDrag += d.magnitude;
                yaw += d.x * 0.15f; pitch = Mathf.Clamp(pitch - d.y * 0.15f, -89f, 89f);
                if (rightDrag > 4f) touched = true;
            }
            if (ms.rightButton.wasReleasedThisFrame && rightDrag <= 4f) FlyCam.RightClick = true;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            float scroll = ms.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (FlyCam.Looking) speed = Mathf.Clamp(speed * (scroll > 0 ? 1.15f : 1f / 1.15f), 0.2f, 40f);
                else { transform.position += transform.forward * (Mathf.Sign(scroll) * speed * 0.08f); touched = true; }
            }

            if (FlyCam.Looking)
            {
                var move = Vector3.zero;
                if (kb.wKey.isPressed) move += transform.forward;
                if (kb.sKey.isPressed) move -= transform.forward;
                if (kb.dKey.isPressed) move += transform.right;
                if (kb.aKey.isPressed) move -= transform.right;
                if (kb.eKey.isPressed) move += Vector3.up;
                if (kb.qKey.isPressed) move -= Vector3.up;
                float v = speed * (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 4f : 1f);
                if (move != Vector3.zero) { transform.position += move.normalized * (v * dt); touched = true; }
            }

            if (ms.middleButton.isPressed)
            {
                var d = ms.delta.ReadValue() * (0.004f * speed);
                transform.position += -transform.right * d.x - transform.up * d.y;
                if (d != Vector2.zero) touched = true;
            }
        }

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            string t = FlyCam.On
                ? $"<b>自由视角</b>  按住右键：拖动转视角 · WASD 移动 · Q/E 降/升 · Shift 加速 · 滚轮调速度 {speed:0.0} m/s　｜　滚轮 推拉 · 中键拖动 平移 · Backspace 回起始视角 · Tab 座位视角　｜　左键照常点"
                : "座位视角（和头显同位置）　Tab：自由视角";
            var r = new Rect(12, Screen.height - 30, Screen.width - 24, 24);
            GUI.color = new Color(0, 0, 0, 0.7f); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), t, style);
            GUI.color = Color.white; GUI.Label(r, t, style);
        }

        void OnDestroy() { FlyCam.On = false; FlyCam.Cam = null; FlyCam.Looking = FlyCam.RightClick = false; }
    }
#endif
}
