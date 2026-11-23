using UnityEngine;

namespace WarTable.XR
{
    /// <summary>
    /// 手够得着的按钮直接用食指按（11-06 用户：手能碰到的键位可以直接用手点击）。
    /// 食指尖离按钮平面 2 cm 以内、落在热区矩形里就算“碰着”；从没碰着变成碰着的那一帧算按一下。
    /// 捏合中的手不算（捏的时候食指尖贴着拇指，容易擦到按钮）。
    /// </summary>
    public static class Poke
    {
        public const float Depth = 0.02f;

        /// <summary>食指尖是否碰着 t 本地 xy 平面上的矩形 hit。</summary>
        public static bool Touch(Transform t, Rect hit, in HandState h)
        {
            if (!t || !h.tracked || h.pinch || !t.gameObject.activeInHierarchy) return false;
            var tip = h.indexTip;
            var n = t.forward;
            if (Mathf.Abs(Vector3.Dot(tip - t.position, n)) > Depth) return false;
            var p = t.InverseTransformPoint(tip);
            return hit.Contains(new Vector2(p.x, p.y));
        }

        /// <summary>食指尖悬在按钮前方 Hover 以内（还没碰上）：界面层提前把按钮点亮，告诉玩家“再往前就按下了”。</summary>
        public const float Hover = 0.08f;
        public static bool Near(Transform t, Rect hit, in HandState h)
        {
            if (!t || !h.tracked || h.pinch || !t.gameObject.activeInHierarchy) return false;
            float d = Mathf.Abs(Vector3.Dot(h.indexTip - t.position, t.forward));
            if (d > Hover) return false;
            var p = t.InverseTransformPoint(h.indexTip);
            return hit.Contains(new Vector2(p.x, p.y));
        }
        public static bool Near(Transform t, Rect hit, MRInput mr)
            => mr && mr.Active && (Near(t, hit, mr.Left) || Near(t, hit, mr.Right));

        /// <summary>两只手里任意一只碰着就算。</summary>
        public static bool Touch(Transform t, Rect hit, MRInput mr)
            => mr && mr.Active && (Touch(t, hit, mr.Left) || Touch(t, hit, mr.Right));
    }
}
