using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using AshFramework.UI.ScreenAdaptation.Components;
using Screen = UnityEngine.Device.Screen; // 避免和UnityEngine.Screen混淆！

namespace AshFramework.UI.ScreenAdaptation.Utilities
{
public static class ScreenWidthAdapterUtils
{
    public const float kMaxResolutionAspectRatio = 1452.0f / 1920.0f; // 0.756 宽高比大于这个数值，认为是超宽屏
    public const float kMinResolutionAspectRatio = 9f / 20f; // 0.45 宽高比小于这个数值，认为是超窄屏

    private static readonly MethodInfo sMiCanvasScalerHandle = typeof(CanvasScaler).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly object[] sHandleParams = new object[0];

    /// <summary>
    /// 
    /// </summary>
    /// <param name="uiRect"></param>
    /// <param name="rootCanvasRectTr"></param>
    /// <returns></returns>
    public static Rect ConvertUIRectToScreenPixelRect(Rect uiRect, RectTransform rootCanvasRectTr)
    {
        if (rootCanvasRectTr == null)
        {
            Debug.LogError("ConvertUIRectToScreenPixelRect失败, rootCanvasRectTr为空");
            return uiRect;
        }

        Vector2 rootSize = rootCanvasRectTr.rect.size;
        if (rootSize.x <= Mathf.Epsilon || rootSize.y <= Mathf.Epsilon || Screen.width <= 0 || Screen.height <= 0)
            return uiRect;

        float ratioX = Screen.width / rootSize.x;
        float ratioY = Screen.height / rootSize.y;
        return new Rect(
            uiRect.x * ratioX,
            uiRect.y * ratioY,
            uiRect.width * ratioX,
            uiRect.height * ratioY
        );
    }

    /// <summary>
    /// 该属性返回系统的SafeArea，数值单位：Canvas逻辑单位
    /// </summary>
    public static Rect GetSystemUISafeArea(RectTransform rootCanvasRectTr)
    {
        if (rootCanvasRectTr == null)
        {
            Debug.LogError("GetApplySafeArea失败, rectTrRootCanvas为空");
            return new Rect(0, 0, 0, 0);
        }

        Rect systemSafeArea = SafeArea.SystemSafeArea;
        Vector2 rootSize = rootCanvasRectTr.rect.size;
        if (Screen.width <= 0 || Screen.height <= 0)
            return new Rect(0, 0, rootSize.x, rootSize.y);

        float ratioX = rootSize.x / Screen.width;
        float ratioY = rootSize.y / Screen.height;

        /*
         * 引入 systemSafeAreaCanvas 变量的核心原因是：单位不统一。
         * SystemSafeArea 的单位是“屏幕像素” (Pixels)：
            SystemSafeArea 返回的是 Screen.safeArea，其数值是基于设备物理分辨率的（例如 2556 x 1179）。
            BorderSafeArea 的单位是“Canvas 逻辑单位” (Canvas Units)：
            CanvasWidthAdjuster 是基于 mCanvas.sizeDelta 计算的。如果你的 CanvasScaler 设置了参考分辨率（如 1920 x 1080），那么 BorderSafeArea 的数值就是基于这个 1920 x 1080 的坐标系的。
         */
        Rect systemSafeAreaCanvas = new Rect(
            systemSafeArea.x * ratioX,
            systemSafeArea.y * ratioY,
            systemSafeArea.width * ratioX,
            systemSafeArea.height * ratioY
        );

#if UNITY_EDITOR
        systemSafeAreaCanvas.xMin += SafeArea.EditorFakeSafeAreaOffsetLeft;
        systemSafeAreaCanvas.xMax += SafeArea.EditorFakeSafeAreaOffsetRight;
        systemSafeAreaCanvas.yMin += SafeArea.EditorFakeSafeAreaOffsetBottom;
        systemSafeAreaCanvas.yMax += SafeArea.EditorFakeSafeAreaOffsetTop;
#endif

        return systemSafeAreaCanvas;
    }

    public static void ForceUpdateCanvasScaler(RectTransform rectTrCanvas)
    {
        if (rectTrCanvas == null)
            return;
        rectTrCanvas.TryGetComponent<CanvasScaler>(out var scaler);
        if (scaler == null || sMiCanvasScalerHandle == null)
            return;

        try
        {
            sMiCanvasScalerHandle.Invoke(scaler, sHandleParams);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public static Rect RectIntersection(Rect r1, Rect r2)
    {
        float xMin = Mathf.Max(r1.xMin, r2.xMin);
        float yMin = Mathf.Max(r1.yMin, r2.yMin);
        float xMax = Mathf.Min(r1.xMax, r2.xMax);
        float yMax = Mathf.Min(r1.yMax, r2.yMax);
        return new Rect(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }


    /// <summary>
    /// 如果f大于0，则返回f+2；如果f等于0，返回0；否则返回f本身
    /// 用来避免UI接缝问题
    /// </summary>
    /// <param name="f"></param>
    /// <returns></returns>
    public static float NotZeroFloatAdd2(float f)
    {
        if (f > 0f)
        {
            return f + 2f;
        }
        else if (f == 0f)
        {
            return 0f;
        }
        else
        {
            return f;
        }
    }
}
}
