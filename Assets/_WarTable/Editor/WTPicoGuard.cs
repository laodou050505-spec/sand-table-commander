using System;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace WarTable.EditorTools
{
    /// <summary>
    /// PICO 工程设置守卫（11-05：手部 / 眼动 / 透视总被改回关闭，打包时清单缺 HAND_TRACKING 权限、装上去会变成 2D 窗口）。
    /// 被改回时全部字段都是 SDK 默认值（连 openMRC 也是）——像是 SDK 某处读不到这份设置、新建了一份默认的覆盖上去；
    /// Project Settings 窗口开着时它每次重绘也会写回旧状态。这里三道保险：
    /// ① 编辑器每 0.5 秒检查一次，被改回就重新打开；② 任何保存这份设置之前先把三项置为开启；③ 打包生成 Gradle 工程前（早于 PICO 写清单）再确认一次。
    /// 每次发现被改回都打一条警告 + 调用栈，方便找出是谁改的。
    /// </summary>
    [InitializeOnLoad]
    public static class WTPicoGuard
    {
        public const string Path = "Assets/Resources/PXR_ProjectSetting.asset";
        static double next;

        static WTPicoGuard() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.5;
            Fix("定时检查", true);
        }

        /// <summary>手部 / 眼动 / 透视没开就打开。save = 立即写盘。返回是否改动过。</summary>
        public static bool Fix(string why, bool save)
        {
            var c = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(Path);
            if (!c) return false;
            if (c.handTracking && c.eyeTracking && c.videoSeeThrough && c.portalInited) return false;
            c.portalInited = true; c.handTracking = true; c.eyeTracking = true; c.videoSeeThrough = true;
            c.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
            EditorUtility.SetDirty(c);
            if (save) AssetDatabase.SaveAssets();
            Debug.LogWarning($"[WT] PICO 设置被改回关闭（{why}），已重新打开\n{Environment.StackTrace}");
            return true;
        }
    }

    /// <summary>保存这份设置之前先把三项置为开启：不管是谁在保存，写进文件的都是开启。</summary>
    public class WTPicoSaveGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (var p in paths)
                if (p == WTPicoGuard.Path) { WTPicoGuard.Fix("保存前", false); break; }
            return paths;
        }
    }

#if UNITY_ANDROID
    /// <summary>生成 Gradle 工程后、PICO 写清单之前（PICO 的 callbackOrder = 10000）再确认一次。</summary>
    internal class WTPicoBuildGuard : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 9000;
        public void OnPostGenerateGradleAndroidProject(string path) => WTPicoGuard.Fix("生成 Gradle 工程", true);
    }
#endif

    /// <summary>打包开始前确认一次。</summary>
    internal class WTPicoPreBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => WTPicoGuard.Fix("打包开始", true);
    }
}
