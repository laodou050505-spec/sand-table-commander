using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// UI 贴图的原始宽高比（11-07）：本版 Unity 只对 2 的幂贴图做 ASTC 压缩，所以 UI 图导入时被缩放到 2 的幂画布
    /// （CoverBuilder.Tex，文件名带原始尺寸，如 legend_zh@1000x1440），显示时必须按原始比例摆，否则会被拉伸。
    /// </summary>
    public static class UiAspect
    {
        /// <summary>宽 / 高。名字里有 @宽x高 就用它，否则用贴图实际宽高。</summary>
        public static float Of(Texture t, float fallback = 1f)
        {
            if (!t) return fallback;
            var n = t.name; int at = n.LastIndexOf('@');
            if (at >= 0)
            {
                var wh = n.Substring(at + 1).Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h) && w > 0 && h > 0) return (float)w / h;
            }
            return t.height > 0 ? (float)t.width / t.height : fallback;
        }
    }
}
