using System;
using System.IO;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WarTable.EditorTools
{
    /// <summary>打包（菜单 WarTable/打包 APK（PICO））→ Builds/WarTable_MMdd_HHmm.apk。做法照《黄昏琴村》TKBuild（几个 PICO 项目踩过的坑）：
    /// - 切平台后必须先停：同一次调用里打包会漏掉 PICO 清单注入，头显里变成 2D 窗口（WTManifestGuard 兜底）；
    /// - PXR_ProjectSetting 打开手部 / 眼动 / 透视，否则真机读不到手部数据、清单没有权限；
    /// - 打包前清掉 Gradle 里的旧资源目录（残留分片曾让引擎加载时卡死）；先重建封面 / 装配并保存场景，免得弹“要保存吗”卡住编辑器；
    /// - Activity 入口、IL2CPP + ARM64、minSdk 29。
    /// 安装：adb install -r -g（-g 自动授予手部 / 眼动 / 空间数据权限，否则首次启动卡在头显里看不见的权限弹窗）。</summary>
    public static class WTBuild
    {
        public const string Package = "com.toybox.wartable";
        const string Scene = "Assets/_WarTable/Scenes/Board.unity";
        const string Icon = "Assets/_WarTable/Art/Icon/icon_1024.png";

        [MenuItem("WarTable/0 PICO 工程设置（手部 / 眼动 / 透视）")]
        public static string EnsurePico()
        {
            const string path = "Assets/Resources/PXR_ProjectSetting.asset";
            var c = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(path);
            if (!c)
            {
                c = ScriptableObject.CreateInstance<PXR_ProjectSetting>();
                c.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
                AssetDatabase.CreateAsset(c, path);
            }
            c.portalInited = true;
            c.handTracking = true;
            c.eyeTracking = true;
            c.eyetrackingCalibration = true;   // 11-09 别人试玩眼动不准：没校准过的玩家，系统启动时先引导校准
            c.videoSeeThrough = true;
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return $"hand={c.handTracking} eye={c.eyeTracking} vst={c.videoSeeThrough} support={c.handTrackingSupportType}";
        }

        /// <summary>应用图标：Art/Icon/icon_1024.png（封面双面战盔 + 朱砂斜劈 + 墨黑底 + 细金边，ArtSource/Icon 合成）。默认 + 安卓各尺寸同一张。</summary>
        public static string SetIcon()
        {
            // 11-08 用户：图标看着还是以前那张。原因：安卓“自适应图标”的前景 / 背景两层都塞了整张图，桌面只显示中间 2/3，
            // 头盔被放得很大、金框和旗都被切掉，看上去就是旧图。现在：自适应背景 = 纯深色，前景 = 去掉金框的画面缩进安全区；
            // 圆形图标单独一张（画面缩到 86%）；旧式方形图标用完整设计稿
            string dir = System.IO.Path.GetDirectoryName(Icon).Replace('\\', '/');
            Texture2D Load(string path)
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.textureType != TextureImporterType.Default || ti.npotScale != TextureImporterNPOTScale.None || ti.maxTextureSize < 1024 || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.mipmapEnabled || !ti.alphaIsTransparency))
                { ti.textureType = TextureImporterType.Default; ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var tex = Load(Icon);
            if (!tex) return "找不到图标 " + Icon;
            var round = Load(dir + "/icon_round.png") ?? tex;
            var fg = Load(dir + "/icon_fg.png"); var bg = Load(dir + "/icon_bg.png");
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
            int n = 0;
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round, UnityEditor.Android.AndroidPlatformIconKind.Adaptive })
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var ic in icons)
                {
                    var arr = new Texture2D[ic.maxLayerCount];
                    if (kind == UnityEditor.Android.AndroidPlatformIconKind.Adaptive && arr.Length >= 2 && fg && bg) { arr[0] = bg; arr[1] = fg; }   // Unity：第 0 层背景、第 1 层前景
                    else for (int i = 0; i < arr.Length; i++) arr[i] = kind == UnityEditor.Android.AndroidPlatformIconKind.Round ? round : tex;
                    ic.SetTextures(arr); n++;
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            AssetDatabase.SaveAssets();
            return $"图标已设置（安卓 {n} 个尺寸）";
        }

        [MenuItem("WarTable/切到 Android 平台")]
        public static string SwitchAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return "已经是 Android";
            return EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android) ? "已切到 Android，等编译完成后再打包" : "切换 Android 平台失败";
        }

        [MenuItem("WarTable/打包 APK（PICO）")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return SwitchAndroid();
            EnsurePico();
            SetIcon();
            // 重建封面、重新装配玩法并保存场景：打进包的是干净场景（截图时临时改过的东西不带进去），也不会弹“要保存吗”
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);
            CoverBuilder.TitleVariant = 5;
            CoverBuilder.Build();
            GameSetup.Setup();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, Package);
            PlayerSettings.productName = "将军沙盘";
            PlayerSettings.companyName = "toybox";
            PlayerSettings.bundleVersion = "0.1." + DateTime.Now.ToString("MMdd");
            PlayerSettings.Android.bundleVersionCode = int.Parse(DateTime.Now.ToString("MMddHH"));
            var gradleAssets = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets"));
            if (Directory.Exists(gradleAssets)) { Directory.Delete(gradleAssets, true); Debug.Log("[WT] 清掉旧的 Gradle 资源目录"); }
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Builds"));
            var apk = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Builds/WarTable_{DateTime.Now:MMdd_HHmm}.apk"));
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var s = rep.summary;
            string msg = $"{s.result} 用时 {s.totalTime.TotalMinutes:0.0} 分钟 大小 {s.totalSize / 1048576f:0.0} MB 错误 {s.totalErrors} → {apk}";
            File.AppendAllText("/tmp/wt_build.txt", DateTime.Now.ToString("HH:mm:ss ") + msg + "\n");
            Debug.Log("[WT] 打包：" + msg);
            return msg;
        }
    }

#if UNITY_ANDROID
    /// <summary>清单守卫：Gradle 工程生成后（PICO 清单注入之后）检查 VR 标记和手部 / 眼动权限，缺了就让打包失败（否则头显里是 2D 窗口）。</summary>
    internal class WTManifestGuard : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var m = File.ReadAllText(Path.Combine(path, "src/main/AndroidManifest.xml"));
            foreach (var need in new[] { "pvr.app.type", "com.picovr.permission.HAND_TRACKING", "com.picovr.permission.EYE_TRACKING" })
                if (!m.Contains(need))
                    throw new BuildFailedException($"[WT] AndroidManifest 缺少 {need}：PICO 清单注入没生效，装到头显会变成 2D 窗口。确认 XR 加载器（PICO）已启用、平台已切到 Android 并等编译完再打包。");
        }
    }
#endif
}
