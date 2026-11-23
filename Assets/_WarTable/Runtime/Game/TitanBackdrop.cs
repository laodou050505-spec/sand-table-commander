using UnityEngine;
using WarTable.XR;

namespace WarTable.Game
{
    /// <summary>
    /// 标题页背后的巨型机甲（11-20；之后也是最终 Boss）。固定站在房间里一个地方（封面出现 1 秒后钉住位置和朝向，之后不跟玩家视角走），
    /// 脚踩真实地面（头显上用地面高度；没有地面信息就按头高 − 1.4 m；脚底按实测网格最低点对齐），面朝玩家。
    /// 动作：平时待机（背壳环视、呼吸、炮管慢转）；每隔 powerMin–powerMax 秒来一次大动作（蓄力下沉 → 抬炮狂转、链锯抬起 → 收回）。
    /// 入场：先一片黑，观察窗和指示灯先亮（一声闷响），两盏专用灯（只照机甲这一层）慢慢推亮。
    /// </summary>
    public class TitanBackdrop : MonoBehaviour
    {
        public const uint TitanLayer = 1u << 2;
        public MRInput mr;
        public Transform editorEye;
        public GameAudio sound;
        [Tooltip("离封面多远（米，玩家视线更远处）")] public float distance = 8.5f;
        [Tooltip("整台机甲的高度（米）")] public float height = 9.5f;
        [Tooltip("左右偏移（米，正值 = 玩家右手边）")] public float sideOffset = 0.6f;
        [Tooltip("转身多少度（略侧身更有压迫感）")] public float yaw = 15f;
        [Tooltip("两次大动作之间的间隔（秒）")] public float powerMin = 12f, powerMax = 16f;
        public Transform model;
        public Animator animator;
        public Light keyLight, rimLight, fillLight;
        public Renderer[] glowRenderers;
        public float keyIntensity = 160f, rimIntensity = 110f, fillIntensity = 45f;
        [ColorUsage(false, true)] public Color glowColor = new(0.35f, 1.3f, 1.6f);

        const float FootOffset = -0.5f;
        float t0, nextPower; bool boomed, anchored; float lastFloor = float.NaN, headFloor;
        Vector3 anchorPos; Quaternion anchorRot;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), PowerId = Animator.StringToHash("Power");

        void OnEnable()
        {
            t0 = Time.time; boomed = false; anchored = false;
            nextPower = Time.time + 6f;   // 进来 6 秒先来一次，让人一眼看到它的力量
            var mc = GetComponentInParent<MenuCover>();   // 声音在运行时才由 WarGame 塞给封面
            if (mc) { if (!sound) sound = mc.sound; if (!mr) mr = mc.mr; if (!editorEye) editorEye = mc.editorEye; }
            if (model)
            {
                if (model.parent) model.SetParent(null, true);   // 封面开局时整体缩小淡出，机甲不能跟着缩
                model.gameObject.SetActive(true);
            }
            Apply(0f);
        }

        void OnDisable() { if (model) model.gameObject.SetActive(false); }   // 封面关掉（进游戏）时机甲一起藏起来

        float FloorY()
        {
            // 有真实地面（追踪原点 = 地板）就用它；没有就按“钉住那一刻的头高 − 1.4 m”，锁死，不跟着玩家低头抬头上下浮
            if (mr && mr.Active) return MRRig.FloorSet && mr.origin ? mr.origin.position.y : (anchored ? headFloor : mr.Head.y - 1.4f);
            return editorEye ? editorEye.position.y - 1.25f : transform.position.y - 1.2f;
        }

        void LateUpdate()
        {
            if (!model) return;
            // 钉位置：等 1 秒（头显追踪就绪、封面摆好），按玩家眼睛的位置和水平朝向钉住，之后不再跟随
            var eye = mr && mr.Active ? mr.cam.transform : editorEye;
            if (!anchored && eye)
            {
                var f = eye.forward; f.y = 0f;
                anchorPos = eye.position; anchorRot = Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward, Vector3.up);
                if (Time.time - t0 > 1.0f) { headFloor = mr && mr.Active ? mr.Head.y - 1.4f : 0f; anchored = true; Debug.Log($"[Titan] 钉住 眼睛 {anchorPos} 朝向 {f} 地面 {FloorY():0.00}"); }
            }
            // 封面在眼前约 0.8 m，机甲在封面后方 distance 米
            var p = anchorPos + anchorRot * new Vector3(sideOffset, 0f, 0.8f + distance);
            // 脚踩地：模型原点在正中，脚底在 −0.5（11-20 按待机姿势烘出蒙皮顶点实测 −0.500；别用 SkinnedMeshRenderer.bounds 量，它按骨骼估、偏大 0.14，会悬空）
            float floor = FloorY();
            if (!Mathf.Approximately(floor, lastFloor)) { if (!float.IsNaN(lastFloor) && Mathf.Abs(floor - lastFloor) > 0.05f) Debug.Log($"[Titan] 地面高度变了 {lastFloor:0.00} → {floor:0.00}（FloorSet={MRRig.FloorSet}）"); lastFloor = floor; }
            p.y = floor - FootOffset * height;
            model.position = p;
            // 面朝玩家；模型的正面是本地 −X（FBX 轴转换的结果），所以再转 90°
            model.rotation = anchorRot * Quaternion.Euler(0f, 180f + yaw, 0f) * Quaternion.Euler(0f, 90f, 0f);   // anchorRot 朝着玩家视线方向（背对玩家），转 180° 才是面朝玩家
            model.localScale = Vector3.one * height;
            if (animator && Time.time > nextPower) { animator.SetTrigger(PowerId); nextPower = Time.time + Random.Range(powerMin, powerMax) + 7f; }
            Apply(Time.time - t0);
        }

        /// <summary>入场的节奏：0–0.6 s 全黑，0.6–1.4 s 观察窗亮起（一声闷响），1.0–3.2 s 灯光推亮。</summary>
        void Apply(float t)
        {
            float glow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.4f, t));
            float light = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.0f, 3.2f, t));
            if (t > 0.6f && !boomed) { boomed = true; if (sound) sound.Cue("boom"); }
            // 灯挂在模型下面，机甲放大后灯离得更远：射程跟着放大，亮度按距离平方补回来（以 5.2 m 时调好的亮度为准）
            float k = height / 5.2f, kk = k * k;
            if (keyLight) { keyLight.range = 16f * k; keyLight.intensity = keyIntensity * kk * light; }
            if (rimLight) { rimLight.range = 16f * k; rimLight.intensity = rimIntensity * kk * light; }
            if (fillLight) { fillLight.range = 16f * k; fillLight.intensity = fillIntensity * kk * light; }
            if (glowRenderers == null) return;
            mpb ??= new MaterialPropertyBlock();
            float breathe = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.6f);
            foreach (var r in glowRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb); mpb.SetColor(EmissionId, glowColor * (glow * breathe)); r.SetPropertyBlock(mpb);
            }
        }
    }
}
