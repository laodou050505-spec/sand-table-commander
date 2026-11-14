using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 沙盘的整体画面（用户反馈：模型像蒙了一层雾、反光太强、质感要更强）：
    /// 关掉边缘光（几厘米的小模型处处挨着轮廓，白色边缘光叠满就像一层雾）、压低环境光、略提饱和度和贴图对比度。
    /// 写进 MiniLit 的全局 _ToyGrade（x 边缘光 y 环境光 z 饱和度 w 亮度）和 _ToyContrast。编辑器里也生效。
    /// </summary>
    [ExecuteAlways]
    public class BoardLook : MonoBehaviour
    {
        [Range(0, 1)] public float rim = 0f;
        [Range(0, 1.5f)] public float ambient = 0.75f;
        [Range(0.5f, 1.5f)] public float saturation = 1.04f;
        [Range(0.5f, 1.5f)] public float brightness = 1.0f;
        [Range(0.8f, 1.5f)] public float textureContrast = 1.1f;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            Shader.SetGlobalVector("_ToyGrade", new Vector4(rim, ambient, saturation, brightness));
            Shader.SetGlobalFloat("_ToyContrast", textureContrast);
        }
    }
}
