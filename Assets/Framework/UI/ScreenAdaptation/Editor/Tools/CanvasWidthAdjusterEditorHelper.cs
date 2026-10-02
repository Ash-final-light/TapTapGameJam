#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Reflection;
using AshFramework.UI.ScreenAdaptation.Components;
using AshFramework.UI.ScreenAdaptation.EditorIntegration;

namespace AshFramework.UI.ScreenAdaptation.Editor.Tools
{
/// <summary>
/// 封装「首次 Play 时通过切换 1 次 deviceIndex 再切回来刷新 Simulator 尺寸」的逻辑。
/// 反射结果缓存在静态变量；无 Simulator 或数量为 0 不执行；多个 SimulatorWindow 时只操作第一个；
/// 通过 TryBegin 保证同一会话内只有 1 个调用方真正执行切换。
/// </summary>
public static class SimulatorFirstPlayRefresh
{
    #region 属性和字段

    private const BindingFlags kNonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags kPublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary> 切换总帧数：第 0 帧切到下一 index（或 0），第 1 帧切回 origin。 </summary>
    private const int kSwitchFrameCount = 2;

    private static Type sSimulatorWindowType;
    private static FieldInfo sMMainField;
    private static Type sDeviceSimulatorMainType;
    private static PropertyInfo sDeviceIndexProp;
    private static PropertyInfo sDevicesProp;

    private static bool sBegun;
    private static int sOriginIndex;
    private static int sDeviceCount;
    private static int sFrame;
    private const string kSessionKeyFirstPlayDone = "CanvasWidthAdjuster_FirstPlayDone";

    #endregion

    #region static-private

    private static bool TryGetFirstSimulatorMain(out EditorWindow window, out object main)
    {
        window = null;
        main = null;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<EditorWindow>();
            if (all == null || all.Length == 0)
                return false;
            var first = all.FirstOrDefault(w => w != null && w.GetType().FullName != null && w.GetType().FullName.Contains("SimulatorWindow"));
            if (first == null)
                return false;
            window = first;
            if (sSimulatorWindowType == null)
            {
                sSimulatorWindowType = first.GetType();
                sMMainField = sSimulatorWindowType.GetField("m_Main", kNonPublicInstance);
            }

            if (sMMainField == null)
                return false;
            main = sMMainField.GetValue(first);
            if (main == null)
                return false;
            if (sDeviceSimulatorMainType == null)
            {
                sDeviceSimulatorMainType = main.GetType();
                sDeviceIndexProp = sDeviceSimulatorMainType.GetProperty("deviceIndex", kPublicInstance);
                sDevicesProp = sDeviceSimulatorMainType.GetProperty("devices", kPublicInstance);
            }

            if (sDeviceIndexProp == null || sDevicesProp == null)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool Tick()
    {
        if (!sBegun)
            return false;
        int targetIndex = sFrame == 0
            ? (sOriginIndex >= sDeviceCount - 1 ? 0 : sOriginIndex + 1)
            : sOriginIndex;
        SetSimulatorDeviceIndex(targetIndex);
        sFrame++;
        if (sFrame >= kSwitchFrameCount)
        {
            sBegun = false;
            MarkFirstPlayDone();
            return true;
        }

        return false;
    }

    private static bool IsFirstPlayInSession()
    {
        return !SessionState.GetBool(kSessionKeyFirstPlayDone, false);
    }

    private static void MarkFirstPlayDone()
    {
        SessionState.SetBool(kSessionKeyFirstPlayDone, true);
    }


    /// <summary>
    /// 获取当前（第一个）Simulator 的 deviceIndex 和设备数量。无 Simulator 或数量为 0 返回 false。
    /// </summary>
    private static bool TryGetSimulatorDeviceIndexAndCount(out int index, out int count)
    {
        index = 0;
        count = 0;
        if (!TryGetFirstSimulatorMain(out _, out object main))
            return false;
        try
        {
            index = (int)sDeviceIndexProp.GetValue(main);
            var devices = sDevicesProp.GetValue(main) as Array;
            count = devices != null ? devices.Length : 0;
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置（第一个）Simulator 的 deviceIndex。
    /// </summary>
    private static bool SetSimulatorDeviceIndex(int index)
    {
        if (!TryGetFirstSimulatorMain(out _, out object main))
            return false;
        try
        {
            sDeviceIndexProp.SetValue(main, index);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region static-public

    /// <summary>
    /// 尝试开始「首次 Play 的 deviceIndex 切换」。仅当本会话尚未做过且当前存在 Simulator 且只有 1 个调用方能成功。
    /// 固定切换 1 次再切回（共 kSwitchFrameCount 帧）。
    /// 返回 true 表示本实例应负责在 Update 中调用 Tick，直到完成。
    /// </summary>
    public static bool TryBeginFirstPlaySwitch()
    {
        if (!IsFirstPlayInSession())
            return false;
        if (sBegun)
            return false;
        if (!TryGetSimulatorDeviceIndexAndCount(out int index, out int count) || count <= 0)
            return false;
        sBegun = true;
        sOriginIndex = index;
        sDeviceCount = count;
        sFrame = 0;
        return true;
    }


    public static bool TickFirstPlaySwitch()
    {
        return Tick();
    }

    #endregion
}

[InitializeOnLoad]
public static class CanvasWidthAdjusterEditorHelper
{
    #region 属性和字段

    public const string kStrMenuItemPrefix = "Tools/屏幕适配"; 
    
    private const string kPrefKey = "M88_EnableWideNarrowScreenAdapterInEditor";
    private const string kMenuItemName = kStrMenuItemPrefix + "/启用全局超宽屏适配";
    private static bool? sEnableWideScreenAdapterInEditor;
    private static readonly MethodInfo sMiAdjustContentSize = typeof(CanvasWidthAdjuster).GetMethod("AdjustContentSize", BindingFlags.Instance | BindingFlags.NonPublic);

    #endregion

    static CanvasWidthAdjusterEditorHelper()
    {
        ScreenAdaptationEditorBridge.Configure(
            () => EnableWideScreenAdapterInEditor,
            SimulatorFirstPlayRefresh.TryBeginFirstPlaySwitch,
            SimulatorFirstPlayRefresh.TickFirstPlaySwitch,
            IsPrefabStageObject);
    }

    private static bool IsPrefabStageObject(GameObject gameObject)
    {
        var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        return prefabStage != null && prefabStage.scene.handle == gameObject.scene.handle;
    }

    public static bool EnableWideScreenAdapterInEditor
    {
        get
        {
            if (!sEnableWideScreenAdapterInEditor.HasValue)
            {
                sEnableWideScreenAdapterInEditor = EditorPrefs.GetBool(kPrefKey, false);
            }

            return sEnableWideScreenAdapterInEditor.Value;
        }
        set
        {
            if (sEnableWideScreenAdapterInEditor != value)
            {
                sEnableWideScreenAdapterInEditor = value;
                EditorPrefs.SetBool(kPrefKey, value);
                CallCanvasWidthAdjusterLogic();
            }
        }
    }


    private static void CallCanvasWidthAdjusterLogic()
    {
        try
        {
            var adjusters = GameObject.FindObjectsOfType<CanvasWidthAdjuster>(false);
            foreach (var adjuster in adjusters)
            {
                if (adjuster != null)
                {
                    sMiAdjustContentSize.Invoke(adjuster, Array.Empty<object>());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public static void ForceAdaptAllScreenAdaptComponnents()
    {
        try
        {
            var adjusters = GameObject.FindObjectsOfType<CanvasWidthAdjuster>(true);
            foreach (var adjuster in adjusters)
            {
                if (adjuster != null)
                {
                    adjuster.EditorForceAdapt = true;
                }
            }

            var safeAreas = GameObject.FindObjectsOfType<SafeArea>(true);
            foreach (var safeArea in safeAreas)
            {
                if (safeArea != null)
                {
                    safeArea.Dirty = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
    
    [MenuItem(kMenuItemName, priority = 5)]
    public static void ToggleWideScreenAdapter()
    {
        EnableWideScreenAdapterInEditor = !EnableWideScreenAdapterInEditor;
    }

    [MenuItem(kMenuItemName, true)]
    public static bool ToggleWideScreenAdapterValidate()
    {
        Menu.SetChecked(kMenuItemName, EnableWideScreenAdapterInEditor);
        return true;
    }
}
}
#endif
