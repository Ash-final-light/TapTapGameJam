#if UNITY_EDITOR
using System;
using UnityEngine;

namespace AshFramework.UI.ScreenAdaptation.EditorIntegration
{
/// <summary>
/// Keeps runtime components independent from UnityEditor assemblies while allowing
/// editor tooling to provide Simulator refresh and preview settings.
/// </summary>
public static class ScreenAdaptationEditorBridge
{
    private static Func<bool> sIsWideScreenAdaptationEnabled;
    private static Func<bool> sTryBeginFirstPlaySwitch;
    private static Func<bool> sTickFirstPlaySwitch;
    private static Func<GameObject, bool> sIsPrefabStageObject;

    public static bool IsWideScreenAdaptationEnabled =>
        sIsWideScreenAdaptationEnabled?.Invoke() ?? true;

    public static void Configure(
        Func<bool> isWideScreenAdaptationEnabled,
        Func<bool> tryBeginFirstPlaySwitch,
        Func<bool> tickFirstPlaySwitch,
        Func<GameObject, bool> isPrefabStageObject)
    {
        sIsWideScreenAdaptationEnabled = isWideScreenAdaptationEnabled;
        sTryBeginFirstPlaySwitch = tryBeginFirstPlaySwitch;
        sTickFirstPlaySwitch = tickFirstPlaySwitch;
        sIsPrefabStageObject = isPrefabStageObject;
    }

    public static bool TryBeginFirstPlaySwitch()
    {
        return sTryBeginFirstPlaySwitch?.Invoke() ?? false;
    }

    public static bool TickFirstPlaySwitch()
    {
        return sTickFirstPlaySwitch?.Invoke() ?? false;
    }

    public static bool IsPrefabStageObject(GameObject gameObject)
    {
        return gameObject != null && (sIsPrefabStageObject?.Invoke(gameObject) ?? false);
    }
}
}
#endif
