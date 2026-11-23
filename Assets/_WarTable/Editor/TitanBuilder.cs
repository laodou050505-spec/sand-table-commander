using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using WarTable.Game;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 标题页巨型机甲（11-20）：导入设置 + 材质 + 动画控制器 + 摆进封面。
    /// 模型 Art/Models/Titan/Titan.fbx 来自 Tools/titan_rig.py + titan_idle.py（骨骼 + 待机动作，已逐帧查过穿模）；
    /// 贴图来自 Tools/titan_tex.py（颜色 4K、金属光滑 / 法线 2K、发光 1K）。
    /// 用法：cs.py 'result.Log(WarTable.EditorTools.TitanBuilder.Add());'（只增删封面下的 Titan，不重搭整个封面）
    /// </summary>
    public static class TitanBuilder
    {
        const string Dir = "Assets/_WarTable/Art/Models/Titan/";
        public const string FBX = Dir + "Titan.fbx";

        static void Tex(string name, int max, bool linear, bool normal = false, bool alpha = false, TextureImporterFormat block = TextureImporterFormat.ASTC_6x6)
        {
            var path = Dir + name;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !linear; ti.maxTextureSize = max; ti.mipmapEnabled = true; ti.anisoLevel = 16; ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter; ti.filterMode = FilterMode.Trilinear; ti.npotScale = TextureImporterNPOTScale.None;   // 各向异性 16：斜着看的面也清楚；Kaiser 生成 mipmap，远一点也不糊
            ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true; a.maxTextureSize = max; a.format = block; a.compressionQuality = 100; ti.SetPlatformTextureSettings(a);   // 压缩质量拉满（只多花导入时间）
            ti.SaveAndReimport();
        }

        public static string Setup()
        {
            // 11-20 / 11-23 用户：机甲要 4K、头显里要清楚 → 颜色、法线 4K ASTC 4×4（最清楚的一档），金属光滑 4K ASTC 5×5，发光 2K
            //（11-23 查出：WTArt 的“贴图不超过 2K”曾把这里的 4K 压回 2K，现已让 Titan 目录跳过那条规则）
            Tex("Titan_albedo.png", 4096, false, block: TextureImporterFormat.ASTC_4x4);
            Tex("Titan_metal_smooth.png", 4096, true, alpha: true, block: TextureImporterFormat.ASTC_5x5);
            Tex("Titan_normal.png", 4096, true, normal: true, block: TextureImporterFormat.ASTC_4x4);
            Tex("Titan_emission.png", 2048, false, block: TextureImporterFormat.ASTC_6x6);
            var mi = (ModelImporter)AssetImporter.GetAtPath(FBX);
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = true; mi.useFileScale = true; mi.globalScale = 1f;
            mi.isReadable = false; mi.optimizeMeshPolygons = true; mi.optimizeMeshVertices = true;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false;
            // 两段动作：Idle（8 s）、Walk（3.2 s 一个步态），都循环；FBX 里的名字是 “TitanRig|TitanRig|Idle” 这种
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips) { c.loopTime = true; c.name = c.takeName.Contains("Power") ? "Power" : "Idle"; c.loopTime = c.name == "Idle"; c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true; c.lockRootRotation = false; }
            mi.clipAnimations = clips;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.SaveAndReimport();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "M_Titan.mat");
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, Dir + "M_Titan.mat"); }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Titan_albedo.png")); mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Titan_metal_smooth.png")); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 0.45f); mat.SetFloat("_SmoothnessTextureChannel", 0f);   // 11-20 用户：不要太反光、粗糙一点 → 光滑度整体乘 0.45
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Titan_normal.png")); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Titan_emission.png")); mat.SetColor("_EmissionColor", Color.black); mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.SetFloat("_EnvironmentReflections", 1f); mat.SetFloat("_SpecularHighlights", 1f);
            EditorUtility.SetDirty(mat);

            var all = AssetDatabase.LoadAllAssetsAtPath(FBX).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            var idle = all.FirstOrDefault(c => c.name == "Idle"); var power = all.FirstOrDefault(c => c.name == "Power");
            var ctrlPath = Dir + "Titan.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            // 平时 Idle 循环；TitanBackdrop 每隔十几秒发一次 Power 触发器 → 播一遍大动作（7 s）→ 自己淡回 Idle
            ctrl.AddParameter("Power", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;
            var si = sm.AddState("Idle"); si.motion = idle; var sp = sm.AddState("Power"); sp.motion = power; sm.defaultState = si;
            var t1 = si.AddTransition(sp); t1.hasExitTime = false; t1.duration = 0.5f; t1.AddCondition(AnimatorConditionMode.If, 0, "Power");
            var t2 = sp.AddTransition(si); t2.hasExitTime = true; t2.exitTime = 0.93f; t2.duration = 0.5f;
            AssetDatabase.SaveAssets();
            return $"动作 {string.Join(", ", all.Select(c => c.name + " " + c.length.ToString("0.0") + "s"))}";
        }

        /// <summary>在封面根节点下（重新）放机甲 + 两盏专用灯；Dimmer 罩子放大到半径 20 m，不然机甲在罩子外面会被压暗。</summary>
        public static string Add(Transform cover = null)
        {
            string log = Setup();
            cover ??= GameObject.Find("Cover").transform;
            var old = cover.Find("Titan"); if (old) Object.DestroyImmediate(old.gameObject);
            var dim = cover.Find("Dimmer"); if (dim) dim.localScale = Vector3.one * 40f;
            var root = new GameObject("Titan").transform; root.SetParent(cover, false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            model.name = "TitanModel";
            var anim = model.GetComponent<Animator>(); if (!anim) anim = model.AddComponent<Animator>();   // Unity 对象不能用 ?? 判空
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Dir + "Titan.controller");
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.applyRootMotion = false;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "M_Titan.mat");
            var rends = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in rends)
            {
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.renderingLayerMask = TitanBackdrop.TitanLayer;
                r.updateWhenOffscreen = true; r.quality = SkinQuality.Bone1;   // 每个顶点只跟一根骨头（零件整块动）
            }
            var bd = root.gameObject.AddComponent<TitanBackdrop>();
            bd.model = model.transform; bd.glowRenderers = rends; bd.animator = anim;
            var mc = cover.GetComponent<MenuCover>();
            if (mc) { bd.mr = mc.mr; bd.editorEye = mc.editorEye; bd.sound = mc.sound; }
            // 灯：暖色主光从玩家左上方打过来（照亮正面的金边），冷色轮廓光从机甲右后方勾出剪影；只照机甲这一层
            Light MakeLight(string n, Color c, float angle)
            {
                var go = new GameObject(n); go.transform.SetParent(model.transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.color = c; l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f;
                l.range = 16f;   // 射程是世界单位，不随模型缩放
                l.shadows = LightShadows.None; l.renderingLayerMask = (int)TitanBackdrop.TitanLayer; l.cullingMask = ~0;
                UnityEngine.Rendering.Universal.LightExtensions.GetUniversalAdditionalLightData(l).renderingLayers = TitanBackdrop.TitanLayer;   // URP 的灯光层要设在这里
                return l;
            }
            // 灯挂在模型下（模型按 height 整体放大，所以位置 / 射程都用“模型高 = 1”的单位）
            // 模型的正面是本地 −X（FBX 轴转换），左手边 +Y 在骨架里对应本地 +Z。灯的位置按这个摆：
            var key = MakeLight("TitanKey", new Color(1f, 0.8f, 0.6f), 55f);      // 暖主光：正面偏右上
            key.transform.localPosition = new Vector3(-1.3f, 1.2f, -0.5f); key.transform.LookAt(model.transform.TransformPoint(new Vector3(0f, 0.15f, 0f)));
            var rim = MakeLight("TitanRim", new Color(0.55f, 0.75f, 1f), 70f);    // 冷轮廓光：左后上方，勾出剪影
            rim.transform.localPosition = new Vector3(0.9f, 1.1f, 0.8f); rim.transform.LookAt(model.transform.TransformPoint(new Vector3(0f, 0.2f, 0f)));
            var fill = MakeLight("TitanFill", new Color(0.65f, 0.72f, 0.85f), 80f);   // 冷补光：正面偏下，让暗部看得清甲片
            fill.transform.localPosition = new Vector3(-1.2f, -0.1f, 0.6f); fill.transform.LookAt(model.transform.TransformPoint(new Vector3(0f, 0.0f, 0f)));
            bd.keyLight = key; bd.rimLight = rim; bd.fillLight = fill;
            EditorUtility.SetDirty(cover.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cover.gameObject.scene);
            return log + "；放好了，蒙皮网格 " + rends.Length + " 个，骨骼 " + model.GetComponentsInChildren<Transform>().Length;
        }
    }
}
