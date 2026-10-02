using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;
using AshFramework.UI.ScreenAdaptation.Services;
using AshFramework.UI.ScreenAdaptation.Utilities;
using Screen = UnityEngine.Device.Screen;

#if UNITY_EDITOR
using AshFramework.UI.ScreenAdaptation.EditorIntegration;
#endif

namespace AshFramework.UI.ScreenAdaptation.Components
{
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "CanvasWidthAdjuster")]
public class CanvasWidthAdjuster : MonoBehaviour
{
    public struct AdaptationResult
    {
        public float ClippedWidthLeft;
        public float ClippedWidthRight;
        public float ClippedHeightTop;
        public float ClippedHeightBottom;
        public Rect BorderSafeArea;
        public Rect RootCanvasArea;

        public static AdaptationResult Default => new AdaptationResult()
        {
            ClippedWidthLeft = 0,
            ClippedWidthRight = 0,
            ClippedHeightTop = 0,
            ClippedHeightBottom = 0,
            BorderSafeArea = new Rect(0, 0, 0, 0),
            RootCanvasArea = new Rect(0, 0, 0, 0)
        };

        public bool IsWidthAdapt => ClippedWidthLeft > 0f || ClippedWidthRight > 0f;
        public bool IsHeightAdapted => ClippedHeightTop > 0f || ClippedHeightBottom > 0f;

        public override string ToString()
        {
            return $"Clipped LRTB ({ClippedWidthLeft:F0},{ClippedWidthRight:F0},{ClippedHeightTop:F0},{ClippedHeightBottom:F0})\n" +
                   $"BorderSafeArea: {BorderSafeArea}, RootCanvasArea: {RootCanvasArea}";
        }
    }

    #region 属性和字段

    [SerializeField]
    private RectTransform mCanvas;

    [SerializeField]
    private RectTransform mContent;

    [SerializeField]
    private RectTransform mLeft;

    [SerializeField]
    private RectTransform mRight;

    private RectTransform mTop;
    private RectTransform mBottom;

    public RectTransform Top
    {
        get
        {
            if (mTop == null)
            {
                if (mLeft == null
#if UNITY_EDITOR
                    || !Application.isPlaying
#endif
                   )
                {
                    return null;
                }

                mTop = Instantiate(mLeft, mLeft.parent);
#if UNITY_EDITOR
                mTop.gameObject.hideFlags = HideFlags.DontSave;
#endif
                mTop.name = "TopBorder (Dynamic Generated)";
                mTop.anchorMin = new Vector2(0f, 1f);
                mTop.anchorMax = new Vector2(1f, 1f);
                mTop.pivot = new Vector2(0.5f, 1f);
                mTop.sizeDelta = Vector2.zero;
                mTop.anchoredPosition = Vector2.zero;
            }

            return mTop;
        }
    }

    public RectTransform Bottom
    {
        get
        {
            if (mBottom == null)
            {
                if (mLeft == null
#if UNITY_EDITOR
                    || !Application.isPlaying
#endif
                   )
                {
                    return null;
                }

                mBottom = Instantiate(mLeft, mLeft.parent);
#if UNITY_EDITOR
                mBottom.gameObject.hideFlags = HideFlags.DontSave;
#endif
                mBottom.name = "BottomBorder (Dynamic Generated)";
                mBottom.anchorMin = new Vector2(0f, 0f);
                mBottom.anchorMax = new Vector2(1f, 0f);
                mBottom.pivot = new Vector2(0.5f, 0f);
                mBottom.sizeDelta = Vector2.zero;
                mBottom.anchoredPosition = Vector2.zero;
            }

            return mBottom;
        }
    }

    private GameObject BorderParentGo
    {
        get
        {
            return mLeft != null ? mLeft.parent.gameObject : null;
        }
    }


    public Rect BorderSafeArea => GetAdaptationResult().BorderSafeArea;

    private const float kMaxWhRatio = ScreenWidthAdapterUtils.kMaxResolutionAspectRatio;
    private const float kMinWhRatio = ScreenWidthAdapterUtils.kMinResolutionAspectRatio;
    private int mLastWidth;
    private int mLastHeight;
    private ScreenOrientation mLastOrientation;

    public string EditorDebugLastInfo { get; private set; }
    public AdaptationResult LastAdaptResult { get; private set; }

    private IScreenAdaptService AdaptService =>
        global::AshFramework.UI.ScreenAdaptation.Services.ScreenAdaptService.Instance;

    #endregion

    #region life-cycle

    private void Start()
    {
        AdaptService.RegisterCanvasWidthAdjuster(this);
#if UNITY_EDITOR
        mRunningDeviceIndexSwitch = ScreenAdaptationEditorBridge.TryBeginFirstPlaySwitch();
#endif
        AdjustContentSize();
    }

    private void OnDestroy()
    {
        AdaptService.UnregisterCanvasWidthAdjuster(this);
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (mRunningDeviceIndexSwitch)
        {
            if (ScreenAdaptationEditorBridge.TickFirstPlaySwitch())
            {
                mRunningDeviceIndexSwitch = false;
                AdjustContentSize();
            }

            return;
        }
#endif

        if (Screen.width != mLastWidth ||
            Screen.height != mLastHeight ||
            Screen.orientation != mLastOrientation
#if UNITY_EDITOR
            || EditorForceAdapt
#endif
           )
        {
#if UNITY_EDITOR
            EditorForceAdapt = false;
#endif
            AdjustContentSize();
        }
    }

    #endregion

    #region public

    public void SetAdaptBorderVisible(bool visible)
    {
        if (BorderParentGo != null)
        {
            BorderParentGo.SetActive(visible);
        }
    }

    #endregion

    #region private

    private void AdjustContentSize()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            SafeActive(mLeft, ScreenAdaptationEditorBridge.IsWideScreenAdaptationEnabled);
            SafeActive(mRight, ScreenAdaptationEditorBridge.IsWideScreenAdaptationEnabled);
            SafeActive(Top, ScreenAdaptationEditorBridge.IsWideScreenAdaptationEnabled);
            SafeActive(Bottom, ScreenAdaptationEditorBridge.IsWideScreenAdaptationEnabled);
        }

        if (!ScreenAdaptationEditorBridge.IsWideScreenAdaptationEnabled)
            return;
#endif

        mLastWidth = Screen.width;
        mLastHeight = Screen.height;
        mLastOrientation = Screen.orientation;

        if (mCanvas == null || mContent == null)
        {
            Debug.LogWarning($"{nameof(CanvasWidthAdjuster)} on '{name}' requires both Canvas and Content references.", this);
            return;
        }

        // 强制刷新 CanvasScaler
        ScreenWidthAdapterUtils.ForceUpdateCanvasScaler(mCanvas);

        var rectTransform = mContent;
        rectTransform.sizeDelta = Vector2.zero;
        var result = GetAdaptationResult();
        LastAdaptResult = result;
        if (result.IsWidthAdapt)
        {
            // Left-Bottom
            rectTransform.offsetMin = new Vector2(result.ClippedWidthLeft, 0f);
            // Right-Top
            rectTransform.offsetMax = new Vector2(-result.ClippedWidthRight, 0f);

            if (mLeft != null && mRight != null)
            {
                SafeSetSizeDelta(mLeft, new Vector2(result.ClippedWidthLeft, mLeft.sizeDelta.y));
                SafeSetSizeDelta(mRight, new Vector2(result.ClippedWidthRight, mRight.sizeDelta.y));
            }

            SafeActive(Top, false);
            SafeActive(Bottom, false);
        }
        else if (result.IsHeightAdapted)
        {
            // Left-Bottom
            rectTransform.offsetMin = new Vector2(0f, result.ClippedHeightBottom);
            // Right-Top
            rectTransform.offsetMax = new Vector2(0f, -result.ClippedHeightTop);
            if (Top != null && Bottom != null)
            {
                SafeSetSizeDelta(Top, new Vector2(Top.sizeDelta.x, result.ClippedHeightTop));
                SafeSetSizeDelta(Bottom, new Vector2(Bottom.sizeDelta.x, result.ClippedHeightBottom));
            }

            SafeActive(mLeft, false);
            SafeActive(mRight, false);
        }
        else
        {
            // Left-Bottom
            rectTransform.offsetMin = new Vector2(result.ClippedWidthLeft, result.ClippedHeightBottom);
            // Right-Top
            rectTransform.offsetMax = new Vector2(-result.ClippedWidthRight, -result.ClippedHeightTop);

            SafeActive(mLeft, false);
            SafeActive(mRight, false);
            SafeActive(Top, false);
            SafeActive(Bottom, false);
        }
    }

    private AdaptationResult GetAdaptationResult()
    {
        AdaptationResult result = AdaptationResult.Default;
        if (mCanvas == null)
            return result;

        var sizeDelta = mCanvas.rect.size;
        result.RootCanvasArea = new Rect(0, 0, sizeDelta.x, sizeDelta.y);
#if UNITY_EDITOR
        if (!ScreenAdaptationEditorBridge.IsWideScreenAdaptationEnabled && !EditorForceApplyInEditMode)
        {
            result.BorderSafeArea = result.RootCanvasArea;
            return result;
        }
#endif
        var currentWidth = sizeDelta.x;
        var currentHeight = sizeDelta.y;
        if (currentWidth <= Mathf.Epsilon || currentHeight <= Mathf.Epsilon)
        {
            result.BorderSafeArea = result.RootCanvasArea;
            return result;
        }

        float currentRatio = currentWidth / currentHeight;
        var systemUISafeArea = ScreenWidthAdapterUtils.GetSystemUISafeArea(mCanvas);
        /*
           窄屏的阈值是9:21
           假设有一个手机，屏幕比例是 9:21.01， 并且带有刘海
           此时的黑边的高度值很小，刘海的高度值有大于黑边。
           那么，需要将黑边的高度值对齐到刘海
           所以底下在计算BorderSafeArea的时候，需要对systemUISafeArea取交集区域。
        */
        if (currentRatio > kMaxWhRatio)
        {
            var newWidth = currentHeight * kMaxWhRatio;
            float totalClippedWidth = currentWidth - newWidth;
            float initialHalfClippedWidth = totalClippedWidth / 2f;
            Rect borderSafeArea = new Rect(initialHalfClippedWidth, 0, newWidth, currentHeight);
            float rawClippedLeft = Mathf.Max(borderSafeArea.xMin, systemUISafeArea.xMin);
            float rawClippedRight = Mathf.Max(currentWidth - borderSafeArea.xMax, currentWidth - systemUISafeArea.xMax);
            result.BorderSafeArea = new Rect(rawClippedLeft, 0, currentWidth - rawClippedLeft - rawClippedRight, currentHeight);
            result.ClippedWidthLeft = ScreenWidthAdapterUtils.NotZeroFloatAdd2(rawClippedLeft);
            result.ClippedWidthRight = ScreenWidthAdapterUtils.NotZeroFloatAdd2(rawClippedRight);
        }
        else if (currentRatio < kMinWhRatio)
        {
            var newHeight = currentWidth / kMinWhRatio;
            float totalClippedHeight = currentHeight - newHeight;
            float initialHalfClippedHeight = totalClippedHeight / 2f;
            Rect borderSafeArea = new Rect(0, initialHalfClippedHeight, currentWidth, newHeight);
            float rawClippedBottom = Mathf.Max(borderSafeArea.yMin, systemUISafeArea.yMin);
            float rawClippedTop = Mathf.Max(currentHeight - borderSafeArea.yMax, currentHeight - systemUISafeArea.yMax);
            result.BorderSafeArea = new Rect(0, rawClippedBottom, currentWidth, currentHeight - rawClippedBottom - rawClippedTop);
            result.ClippedHeightBottom = ScreenWidthAdapterUtils.NotZeroFloatAdd2(rawClippedBottom);
            result.ClippedHeightTop = ScreenWidthAdapterUtils.NotZeroFloatAdd2(rawClippedTop);
        }
        else
        {
            var defaultBorderSafeArea = new Rect(0, 0, currentWidth, currentHeight);
            result.BorderSafeArea = defaultBorderSafeArea;
        }

#if UNITY_EDITOR
        EditorDebugLastInfo = result.ToString();
#endif
        return result;
    }
    
    #endregion

    #region static

    private static void SafeActive(RectTransform rectTr, bool value)
    {
        if (rectTr == null)
            return;
        rectTr.gameObject.SetActive(value);
    }

    private static void SafeSetSizeDelta(RectTransform rectTr, Vector2 size)
    {
        if (rectTr == null)
            return;
        SafeActive(rectTr, true);
        rectTr.sizeDelta = size;
    }

    #endregion

    #region Editor-Only

#if UNITY_EDITOR
    public bool EditorForceApplyInEditMode { get; set; } = false;
    private bool mRunningDeviceIndexSwitch;
    public RectTransform CanvasRectTr => mCanvas;
    public bool EditorForceAdapt { get; set; } = false;

#endif

    #endregion
}
}
