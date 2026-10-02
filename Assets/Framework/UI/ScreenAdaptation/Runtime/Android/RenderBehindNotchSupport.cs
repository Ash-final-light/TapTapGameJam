using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace AshFramework.UI.ScreenAdaptation.Android
{
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "RenderBehindNotchSupport")]
public class RenderBehindNotchSupport : MonoBehaviour
{
    public bool RenderBehindNotch = true;

    // Constants from https://developer.android.com/reference/android/view/WindowManager.LayoutParams.html
    private const int LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES = 1;
    private const int LAYOUT_IN_DISPLAY_CUTOUT_MODE_NEVER = 2;

    public void SetRenderBehindNotch(bool enabled)
    {
        if (Application.platform != RuntimePlatform.Android)
            return;

        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                // Supported on Android 9 Pie (API 28) and later
                if (version.GetStatic<int>("SDK_INT") < 28)
                    return;
            }

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
                AndroidJavaObject windowattributes = window.Call<AndroidJavaObject>("getAttributes");
                windowattributes.Set("layoutInDisplayCutoutMode", enabled ?
                    LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES :
                    LAYOUT_IN_DISPLAY_CUTOUT_MODE_NEVER);

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        if (window != null && windowattributes != null)
                        {
                            window.Call("setAttributes", windowattributes);
                            windowattributes.Dispose();
                            window.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }));
            }
        }
        catch (AndroidJavaException e)
        {
            Debug.LogWarning($"Unable to configure Android display cutout mode: {e.Message}", this);
        }
    }

    void Awake()
    {
        SetRenderBehindNotch(RenderBehindNotch);
    }
}
}
