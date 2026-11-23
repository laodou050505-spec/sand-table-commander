using UnityEngine;
using UnityEngine.InputSystem;

namespace WarTable.XR
{
    /// <summary>
    /// 立体按钮的手部判定（11-16 用户：别人试玩时不会玩，要“一看就会”——看得见的大按钮，伸手一按就行）。
    /// 按钮帽 t：原点在帽顶中心，本地 +Y 朝外（按下的反方向）。三种按法都算，玩家怎么伸手都对：
    ///   ① 食指（没捏着）从上面按到帽顶；② 在按钮上捏一下；③ 编辑器里鼠标点。
    /// 防误触：手指要先离开 AwayTime 秒再碰上才算一次；按完 Cooldown 秒内不再认；live = false 时只显示悬停、不响应。
    /// </summary>
    public class PokeButton
    {
        public Transform t;
        /// <summary>帽顶半径（米）。</summary>
        public float radius = 0.03f;
        /// <summary>手指悬在按钮上方（还没按下）：界面层把按钮提亮。</summary>
        public bool Hover { get; private set; }
        /// <summary>手指正压着。</summary>
        public bool Down { get; private set; }
        /// <summary>这一帧按下了。</summary>
        public bool Pressed { get; private set; }
        /// <summary>哪只手悬着 / 压着（0 左 1 右 −1 没有）。</summary>
        public int Hand { get; private set; } = -1;

        const float AwayTime = 0.2f, Cooldown = 0.6f, TouchAbove = 0.012f, TouchBelow = 0.05f, HoverAbove = 0.09f;
        float awayT = 9f, lastT = -9f;

        public void Reset() { Hover = Down = Pressed = false; Hand = -1; awayT = 9f; }

        public void Tick(MRInput mr, bool live, Camera editorCam = null)
        {
            Pressed = false; Hover = false; Hand = -1;
            if (!t || !t.gameObject.activeInHierarchy) { Down = false; awayT = 9f; return; }
            var c = t.position; var n = t.up;
            bool touch = false, pinchHit = false;
            if (mr && mr.Active)
                for (int i = 0; i < 2; i++)
                {
                    var h = i == 0 ? mr.Left : mr.Right;
                    if (!h.tracked) continue;
                    var d = (h.pinch ? h.pinchPoint : h.indexTip) - c;
                    float along = Vector3.Dot(d, n), lateral = (d - n * along).magnitude;
                    if (lateral < radius * 1.6f && along > -TouchBelow && along < HoverAbove) { Hover = true; Hand = i; }
                    if (!h.pinch && lateral < radius && along > -TouchBelow && along < TouchAbove) { touch = true; Hand = i; }
                    if ((i == 0 ? mr.PinchDownL : mr.PinchDownR) && d.magnitude < radius * 1.4f) { pinchHit = true; Hand = i; }
                }
            else if (editorCam && Mouse.current != null)
            {
                var ray = WarTable.Game.FlyCam.View(editorCam).ScreenPointToRay(Mouse.current.position.ReadValue());
                float dist = Vector3.Cross(ray.direction, c - ray.origin).magnitude;
                if (dist < radius * 1.3f) { Hover = true; if (Mouse.current.leftButton.wasPressedThisFrame) pinchHit = true; }
            }
            bool ok = live && Time.time - lastT > Cooldown;
            if ((touch && !Down && awayT > AwayTime || pinchHit) && ok) { Pressed = true; lastT = Time.time; }
            if (touch || pinchHit) awayT = 0f; else awayT += Time.deltaTime;
            Down = touch;
        }
    }
}
