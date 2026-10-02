#pragma warning disable IDE0010
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace AshFramework.UI.ScreenAdaptation.Android
{
public class WindowInsetsListener : AndroidJavaProxy
{
    public struct Insets
    {
        public int left, top, right, bottom;
    }

    private static Insets m_safeInset = new Insets();
    private static readonly object m_safeInsetLock = new object();

    public static Insets SafeInset
    {
        get
        {
            return m_safeInset;
        }
    }

    public void InitInsets(AndroidJavaObject decorView)
    {
        if (decorView == null)
            return;

        using (var windowInsets = decorView.Call<AndroidJavaObject>("getRootWindowInsets"))
        {
            if (windowInsets != null)
                onApplyWindowInsets(decorView, windowInsets);
        }
    }

    public WindowInsetsListener() : base("android.view.View$OnApplyWindowInsetsListener") { }

    private AndroidJavaObject onApplyWindowInsets(AndroidJavaObject view, AndroidJavaObject windowInsets)
    {
        if (view == null || windowInsets == null)
            return windowInsets;

        using (var cutout = windowInsets.Call<AndroidJavaObject>("getDisplayCutout"))
        {
            if (cutout == null)
            {
                lock (m_safeInsetLock)
                {
                    m_safeInset.left = 0;
                    m_safeInset.top = 0;
                    m_safeInset.right = 0;
                    m_safeInset.bottom = 0;
                }
            }
            else
            {
                lock (m_safeInsetLock)
                {
                    m_safeInset.left = cutout.Call<int>("getSafeInsetLeft");
                    m_safeInset.top = cutout.Call<int>("getSafeInsetTop");
                    m_safeInset.right = cutout.Call<int>("getSafeInsetRight");
                    m_safeInset.bottom = cutout.Call<int>("getSafeInsetBottom");
                }
            }
        }

        return view.Call<AndroidJavaObject>("onApplyWindowInsets", windowInsets);
    }
}

[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "AndroidSafeArea")]
public class AndroidSafeArea : MonoBehaviour
{
    private static bool m_inited;
    private static bool m_insetsListenerInstalled = false;
    private static bool m_hasNotch = false;
    private static int m_notchHeight = 0;

    void Start()
    {
        if (Application.platform != RuntimePlatform.Android)
            return;

        if (m_inited)
            return;

        try
        {
            InitializeAndroidSafeArea();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Android safe-area initialization failed; falling back to Screen.safeArea. {e.Message}");
        }
        finally
        {
            m_inited = true;
        }
    }

    private static void InitializeAndroidSafeArea()
    {

        DeviceNotchDataManager.Instance.Init();

        string model;
        using (var buildClass = new AndroidJavaClass("android.os.Build"))
        {
            model = buildClass.GetStatic<string>("MODEL");
            Debug.Log("Model: " + model);
        }

        // 提前判断是否没有渲染到刘海区
        if (!DeviceNotchDataManager.Instance.DoRenderBehindNotch(model))
        {
            return;
        }

        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            if (version.GetStatic<int>("SDK_INT") >= 28)
            {
                // Android P及以上，使用标准API判断
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                        {
                            using (var decorView = window.Call<AndroidJavaObject>("getDecorView"))
                            {
                                var windowInsetsListener = new WindowInsetsListener();
                                windowInsetsListener.InitInsets(decorView);
                                decorView.Call("setOnApplyWindowInsetsListener", windowInsetsListener);
                                m_insetsListenerInstalled = true;
                            }
                        }
                    }
                }
            }
            else
            {
                // Android O及以下，先用厂商自己的API判断
                try
                {
                    using (AndroidJavaClass notchToolClass = new AndroidJavaClass("notchtools.geek.com.notchtools.NotchTools"))
                    using (AndroidJavaClass playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject currentActivityObject = playerClass.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (AndroidJavaObject window = currentActivityObject.Call<AndroidJavaObject>("getWindow"))
                    using (AndroidJavaObject notchTool = notchToolClass.CallStatic<AndroidJavaObject>("getFullScreenTools"))
                    {
                        m_hasNotch = notchTool.Call<bool>("isNotchScreen", window);
                        m_notchHeight = notchTool.Call<int>("getNotchHeight", window);
                        Debug.LogFormat("HasNotch: {0}, NotchHeight: {1}", m_hasNotch, m_notchHeight);
                    }
                }
                catch (AndroidJavaException e)
                {
                    Debug.LogWarning($"Vendor notch API is unavailable; using optional device data fallback. {e.Message}");
                }

                if (!m_hasNotch || m_notchHeight <= 0)
                {
                    // 如果API没有取到，尝试从配置表取
                    m_hasNotch = DeviceNotchDataManager.Instance.HasNotch(model, out m_notchHeight);
                }
            }
        }

        Debug.Log("AndroidSafeArea inited");
    }


    private static Rect safeAreaAndroidO()
    {
        var rect = new Rect();

        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft:
                rect.x = m_notchHeight;
                rect.y = 0;
                rect.width = Screen.width - m_notchHeight;
                rect.height = Screen.height;
                break;

            case ScreenOrientation.LandscapeRight:
                rect.x = 0;
                rect.y = 0;
                rect.width = Screen.width - m_notchHeight;
                rect.height = Screen.height;
                break;

            case ScreenOrientation.Portrait:
                rect.x = 0;
                rect.y = m_notchHeight;
                rect.width = Screen.width;
                rect.height = Screen.height - m_notchHeight;
                break;

            case ScreenOrientation.PortraitUpsideDown:
                rect.x = 0;
                rect.y = 0;
                rect.width = Screen.width;
                rect.height = Screen.height - m_notchHeight;
                break;

            default:
                rect.x = 0;
                rect.y = 0;
                rect.width = Screen.width;
                rect.height = Screen.height;
                break;
        }

        return rect;
    }

    public static bool HasNotch()
    {
        Rect area = SafeArea;
        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft:
            case ScreenOrientation.LandscapeRight:
                if (area.x > 0 || area.width < Screen.width)
                    return true;
                else
                    return false;

            case ScreenOrientation.Portrait:
            case ScreenOrientation.PortraitUpsideDown:
                if (area.y > 0 || area.height < Screen.height)
                    return true;
                else
                    return false;

            default:
                return false;
        }
    }

    public static Rect SafeArea
    {
        get
        {
            if (!m_inited)
                return Screen.safeArea;

            if (!m_insetsListenerInstalled)
            {
                if (!m_hasNotch)
                {
                    return Screen.safeArea;
                }
                else
                {
                    return safeAreaAndroidO();
                }
            }

            // Scale in case resolution doesn't match the native one
            if (Display.main == null || Display.main.systemWidth <= 0 || Display.main.systemHeight <= 0)
                return Screen.safeArea;

            float xScale = (float)Screen.width / Display.main.systemWidth;
            float yScale = (float)Screen.height / Display.main.systemHeight;

            Rect safeArea = new Rect
            {
                x = Mathf.Round(WindowInsetsListener.SafeInset.left * xScale),
                width = Mathf.Round(Screen.width - (WindowInsetsListener.SafeInset.left + WindowInsetsListener.SafeInset.right) * xScale),
                y = Mathf.Round(WindowInsetsListener.SafeInset.bottom * yScale),
                height = Mathf.Round(Screen.height - (WindowInsetsListener.SafeInset.bottom + WindowInsetsListener.SafeInset.top) * yScale)
            };

            return safeArea;
        }
    }

    public static int NotchHeight
    {
        get
        {
            if (!HasNotch())
                return 0;

            Rect area = SafeArea;
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeLeft:
                    return (int)area.x;

                case ScreenOrientation.LandscapeRight:
                    return Screen.width - (int)area.width;

                case ScreenOrientation.Portrait:
                    return (int)area.y;

                case ScreenOrientation.PortraitUpsideDown:
                    return Screen.height - (int)area.height;

                default:
                    return 0;
            }
        }
    }
}
}
#pragma warning restore IDE0010
