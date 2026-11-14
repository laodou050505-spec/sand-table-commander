using UnityEngine;
using WarTable;

namespace WarTable.Game
{
    /// <summary>
    /// 浮在眼前的字和面板不能和沙盘叠在一起（11-08 结算板、11-09 教学提示条：“文字和沙盘场景的位置关系，不要重叠”）。
    /// 从眼睛看过去，在面板所占的水平角度范围里采样沙盘（含旗牌顶的高度）最高的仰角，面板下沿再高出 margin 度，
    /// 返回给 LazyFollow 用的 below（低于眼睛多少米；负数 = 高于眼睛）。沙盘没摆出来时返回 fallback。
    /// </summary>
    public static class ViewClear
    {
        /// <summary>沙盘根节点（WarGame 设）。</summary>
        public static Transform Board;

        public static float Below(Transform eye, float distance, float halfW, float halfH, float fallback, float maxAbove = 0.12f, float marginDeg = 3f, float yawDeg = 0f)
        {
            if (!Board || !eye || !Board.gameObject.activeInHierarchy) return fallback;
            var e = eye.position;
            var f = eye.forward; f.y = 0; f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
            f = Quaternion.Euler(0f, yawDeg, 0f) * f;   // 面板不在正前方时（偏右的手势提示）按它所在的方向采样
            float halfAz = Mathf.Atan2(halfW + 0.05f, distance);
            float hx = BoardMap.HalfX * BoardMap.Yard + 0.05f, hz = BoardMap.HalfZ * BoardMap.Yard + 0.05f, top = 0.16f;
            float maxEl = -1.4f;
            for (int ix = 0; ix <= 12; ix++)
                for (int iz = 0; iz <= 12; iz++)
                {
                    var p = Board.TransformPoint(new Vector3(-hx + 2f * hx * ix / 12f, top, -hz + 2f * hz * iz / 12f));
                    var d = p - e; var dh = d; dh.y = 0; float dist = dh.magnitude; if (dist < 0.05f) continue;
                    if (Mathf.Acos(Mathf.Clamp(Vector3.Dot(dh / dist, f), -1f, 1f)) > halfAz) continue;
                    maxEl = Mathf.Max(maxEl, Mathf.Atan2(d.y, dist));
                }
            if (maxEl < -1.39f) return fallback;
            float centerY = distance * Mathf.Tan(maxEl + marginDeg * Mathf.Deg2Rad) + halfH;   // 相对眼睛的高度
            return Mathf.Clamp(-centerY, -maxAbove, fallback);
        }
    }
}
