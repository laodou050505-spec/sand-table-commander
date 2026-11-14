using UnityEngine;

namespace WarTable
{
    /// <summary>风车叶轮：绕本地 Z 轴慢慢转，偶尔随“阵风”快一点（转速按真实风车：每分钟几圈）。</summary>
    public class Spin : MonoBehaviour
    {
        public float rpm = 6f;
        public float gust = 0.35f;
        float phase;

        void Start() => phase = Random.value * 100f;

        void Update()
        {
            float g = 1f + gust * (Mathf.PerlinNoise(Time.time * 0.07f + phase, 0.3f) - 0.5f) * 2f;
            transform.Rotate(0, 0, rpm * 6f * g * Time.deltaTime, Space.Self);
        }
    }
}
