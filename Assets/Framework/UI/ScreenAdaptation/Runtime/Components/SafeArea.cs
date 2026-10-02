#if UNITY_ANDROID && !UNITY_EDITOR
#define ANDROID_SAFE_AREA
#endif

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting.APIUpdating;
using AshFramework.UI.ScreenAdaptation.Android;
using AshFramework.UI.ScreenAdaptation.Utilities;
using Screen = UnityEngine.Device.Screen;

namespace AshFramework.UI.ScreenAdaptation.Components
{
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "SafeArea")]
[ExecuteAlways, DefaultExecutionOrder(-1), RequireComponent(typeof(RectTransform))]
public class SafeArea : UIBehaviour
{
    #region 序列化字段

    public float PaddingTop;
    public float PaddingBottom;

    public bool DisableTopAdapt;
    public bool DisableBottomAdapt;

    #endregion

    #region 属性和字段

    protected DrivenRectTransformTracker mTracker;

    public RectTransform RootCanvasRectTr { get; private set; }
    public CanvasWidthAdjuster WidthAdjuster { get; private set; }
    private RectTransform mSelfRectTr;
    public bool IsInit { get; private set; } = false;

    private RectTransform SelfRectTr
    {
        get
        {
            if (mSelfRectTr == null)
            {
                mSelfRectTr = transform as RectTransform;
            }

            return mSelfRectTr;
        }
    }

    private Vector3[] mCornerCache = new Vector3[4];

    public bool Dirty { get; set; } = false;

    #endregion

    #region static

    /// <summary>
    /// 该属性返回系统的SafeArea，数值单位：真实的屏幕像素 (Pixels)，在Editor下就是Game窗口下的绘制区域大小
    /// </summary>
    public static Rect SystemSafeArea
    {
        get
        {
#if ANDROID_SAFE_AREA
            return AndroidSafeArea.SafeArea;
#else
            return Screen.safeArea;
#endif
        }
    }

    public static Rect SystemSafeAreaWithWidthAdapt(Transform curTr)
    {
        if (curTr == null)
        {
            Debug.LogError("SafeArea.SystemSafeAreaWithWidthAdapt received a null Transform.");
            return SystemSafeArea;
        }

        Canvas rootCanvas = GetActiveRootCanvas(curTr.gameObject);
        var defaultSafeArea = SystemSafeArea;
        if (rootCanvas == null)
        {
            Debug.LogError($"SafeArea.SystemSafeAreaWithWidthAdapt 找不到rootCanvas, curTr:{curTr}");
            return defaultSafeArea;
        }

        var widthAdjuster = rootCanvas.GetComponent<CanvasWidthAdjuster>();
        if (widthAdjuster == null)
        {
            Debug.LogError($"SafeArea.SystemSafeAreaWithWidthAdapt 找不到rootCanvas上的CanvasWidthAdjuster组件, curTr:{curTr}");
            return defaultSafeArea;
        }

        return SystemSafeAreaWithWidthAdaptImpl(widthAdjuster);
    }

    /// <summary>
    /// 返回的Rect已经考虑了宽屏窄屏适配逻辑，数值单位：真实的屏幕像素 (Pixels)，在Editor下就是Game窗口下的绘制区域大小
    /// </summary>
    /// <returns></returns>
    private static Rect SystemSafeAreaWithWidthAdaptImpl(CanvasWidthAdjuster widthAdjuster)
    {
        var defaultSystemSafeArea = SystemSafeArea;
        if (widthAdjuster == null)
        {
            Debug.LogError("SafeArea.SystemSafeAreaWithWidthAdapt 传入的widthAdjuster为null");
            return defaultSystemSafeArea;
        }

        var canvas = widthAdjuster.GetComponent<Canvas>();
        if (canvas == null || !canvas.isRootCanvas)
        {
            Debug.LogError($"SafeArea.SystemSafeAreaWithWidthAdapt canvas == null ||!canvas.isRootCanvas canvas == null:{canvas == null}");
            return defaultSystemSafeArea;
        }

        RectTransform rootCanvasRectTr = widthAdjuster.transform as RectTransform;
        Rect systemBorderSafeArea = ScreenWidthAdapterUtils.ConvertUIRectToScreenPixelRect(widthAdjuster.BorderSafeArea, rootCanvasRectTr);
        Rect systemCombinedSafeArea = ScreenWidthAdapterUtils.RectIntersection(defaultSystemSafeArea, systemBorderSafeArea);
        return systemCombinedSafeArea;
    }

    /// <summary>
    /// 返回的Rect是基于Canvas坐标系的SafeArea，并且已经应用了CanvasWidthAdjuster的BorderSafeArea
    /// </summary>
    /// <param name="widthAdjuster"></param>
    /// <param name="disableBottomAdapt"></param>
    /// <param name="disableTopAdapt"></param>
    /// <param name="paddingBottom"></param>
    /// <param name="paddingTop"></param>
    /// <param name="rootCanvasRectTr"></param>
    /// <returns></returns>
    private static Rect StaticGetApplySafeArea(
        RectTransform rootCanvasRectTr,
        CanvasWidthAdjuster widthAdjuster,
        bool disableBottomAdapt,
        bool disableTopAdapt,
        float paddingBottom,
        float paddingTop)
    {
        if (rootCanvasRectTr == null)
        {
            Debug.LogError("GetApplySafeArea失败, rectTrRootCanvas为空");
            return new Rect(0, 0, 0, 0);
        }

        Vector2 rootSize = rootCanvasRectTr.sizeDelta;
        Rect borderSafeArea = widthAdjuster != null ? widthAdjuster.BorderSafeArea : new Rect(0, 0, rootSize.x, rootSize.y);
        var systemSafeAreaCanvas = ScreenWidthAdapterUtils.GetSystemUISafeArea(rootCanvasRectTr);
        Rect combinedSafeArea = ScreenWidthAdapterUtils.RectIntersection(systemSafeAreaCanvas, borderSafeArea);

        // X轴适配
        float left = combinedSafeArea.xMin;
        float right = combinedSafeArea.xMax;

        // Y轴适配
        float bottom = disableBottomAdapt ? borderSafeArea.yMin : combinedSafeArea.yMin + paddingBottom;
        float top = disableTopAdapt ? borderSafeArea.yMax : combinedSafeArea.yMax + paddingTop;

        return new Rect(left, bottom, Mathf.Max(0, right - left), Mathf.Max(0, top - bottom));
    }

    /// <summary>
    /// 从curGo向父节点方向寻找, 找到rootCanvas
    /// </summary>
    /// <param name="curGo"></param> 
    /// <returns></returns>
    private static Canvas GetActiveRootCanvas(GameObject curGo)
    {
        if (curGo == null)
            return null;
        Transform parent = curGo.transform;
        while (parent != null)
        {
            Canvas curCanvas = parent.GetComponent<Canvas>();
            if (curCanvas != null && curCanvas.enabled && curCanvas.isRootCanvas)
            {
                return curCanvas;
            }

            parent = parent.parent;
        }

        return null;
    }

    #endregion

    #region life-cycle

    protected override void Awake()
    {
        base.Awake();
        AddTracker();

        // 神奇的代码：Awake需要调用一次，避免LoopScrollRect在Awake时拿不到正确尺寸导致不能实例化任何的Item ...
        SetDefaultSize();
    }

    protected override void Start()
    {
        base.Start();
        if (!IsInit)
        {
            if (!(IsInit = TryInit()))
            {
                Debug.LogError("SafeArea Init Failed!", this);
                return;
            }
        }

        ApplySafeArea();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        mTracker.Clear();
    }

#if UNITY_EDITOR
    /*
     * 神奇的代码：
     * bug复现： 屏蔽掉该部分仅Editor下生效的代码，打开SeasonActivityEntryPanel5预制体，找到Content子节点MiddleGo，移动一下位置，然后再按Ctrl+Z撤销，发现SafeArea所在的节点的AnchorMax会被设置成（0,0），进而导致界面错乱
     * 原因：Unity的Undo系统在撤销时会禁用SafeArea组件
     * 同时不知道为什么会修掉改被锁定的属性，又因为该属性被DrivenRectTransformTracker锁定了，Unity的Undo系统就把该属性设置成了默认值（0,0）
     * 所以在Editor下需要在OnDisable时清空Tracker，避免Undo系统把被锁定的属性设置成默认值
     * 在OnEnable的时候，将RectTransform设置成预期的默认值
     */
    protected override void OnDisable()
    {
        if (!Application.isPlaying)
        {
            mTracker.Clear();
        }

        base.OnDisable();
    }

    protected override void OnEnable()
    {
        if (!Application.isPlaying)
        {
            AddTracker();
            SetDefaultSize();
        }

        base.OnEnable();
    }

#endif
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Dirty = true;
    }

    private void Update()
    {
        if (IsInit && Dirty)
        {
            ApplySafeArea();
            Dirty = false;
        }
    }

    #endregion


    #region private

    private bool TryInit()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return false;
        }

        if (canvas.enabled)
        {
            RootCanvasRectTr = canvas.rootCanvas.GetComponent<RectTransform>();
        }
        else // canvas.enabled == false的情况下, canvas.rootCanvas返回的值是自身, 不符合预期. 详情: https://leiting.feishu.cn/wiki/HBT6wpxiIiniwvkZVtIcEqFjnSc
        {
            var activeRootCanvas = GetActiveRootCanvas(gameObject);
            if (activeRootCanvas == null)
            {
                Debug.LogError("SafeArea.TryInit初始化失败, 找不到rootCanvas", this);
                return false;
            }

            RootCanvasRectTr = activeRootCanvas.GetComponent<RectTransform>();
        }

        if (RootCanvasRectTr != null)
        {
            WidthAdjuster = RootCanvasRectTr.GetComponent<CanvasWidthAdjuster>();
        }

        if (RootCanvasRectTr == null)
        {
            Debug.LogError("SafeArea.TryInit: 找不到rootCanvas的RectTransform", this);
            return false;
        }

        return true;
    }

    public Rect GetApplySafeArea()
    {
        return StaticGetApplySafeArea(RootCanvasRectTr, WidthAdjuster, DisableBottomAdapt, DisableTopAdapt, PaddingBottom, PaddingTop);
    }

    private void SetDefaultSize()
    {
        SelfRectTr.anchorMin = Vector2.zero;
        SelfRectTr.anchorMax = Vector2.one;
        SelfRectTr.pivot = Vector2.zero;
        SelfRectTr.sizeDelta = Vector2.zero;
        SelfRectTr.anchoredPosition = Vector2.zero;
    }

    public void ApplySafeArea()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && !EditorForceApplyInEditMode)
            return;
        if (global::AshFramework.UI.ScreenAdaptation.EditorIntegration.ScreenAdaptationEditorBridge.IsPrefabStageObject(gameObject))
            return;
#endif
        if (!IsInit)
            return;

        Rect area = GetApplySafeArea();
        AddTracker();
        SetDefaultSize();
        ApplySafeAreaIsolated(area);
    }

    /// <summary>
    /// 将当前Content的位置和大小调整到SafeArea区域，并且不受父级布局的影响
    /// </summary>
    /// <param name="area"></param>
    private void ApplySafeAreaIsolated(Rect area)
    {
        // pivot定在左下角
        SetPanelSizeDelta(area);

        // 拿到在根Canvas下的左下角世界坐标A
        RootCanvasRectTr.GetWorldCorners(mCornerCache);

        // 世界坐标A转换到当前RectTransform的局部坐标系下，得到坐标B realPosition
        Vector2 realPosition = SelfRectTr.worldToLocalMatrix.MultiplyPoint3x4(mCornerCache[0]);

        // area.position带了安全区域的偏移信息，realPosition抵消父级坐标系的影响
        SelfRectTr.anchoredPosition += new Vector2(realPosition.x + area.position.x, realPosition.y + area.position.y);
    }

    private void SetPanelSizeDelta(Rect area)
    {
        var localRect = SelfRectTr.rect.size;
        var offset = area.size - localRect;
        SelfRectTr.sizeDelta += offset;
    }

    private void AddTracker()
    {
        mTracker.Clear();
        var drivenProperties = DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition |
                               DrivenTransformProperties.SizeDelta | DrivenTransformProperties.Pivot;
        mTracker.Add(this, SelfRectTr, drivenProperties);
    }

    #endregion

    #region Editor-Only

#if UNITY_EDITOR
    public bool EditorForceApplyInEditMode { get; set; } = false;
    public static float EditorFakeSafeAreaOffsetLeft { get; private set; } = 0;
    public static float EditorFakeSafeAreaOffsetRight { get; private set; } = 0;
    public static float EditorFakeSafeAreaOffsetTop { get; private set; } = 0;
    public static float EditorFakeSafeAreaOffsetBottom { get; private set; } = 0;

    public static void SetEditorFakeSafeAreaOffset(float top, float bottom, float left, float right)
    {
        EditorFakeSafeAreaOffsetLeft = left;
        EditorFakeSafeAreaOffsetRight = right;
        EditorFakeSafeAreaOffsetTop = top;
        EditorFakeSafeAreaOffsetBottom = bottom;
    }

    /// <summary>
    /// 供 Editor 工具显式触发一次适配（如 QuickValidatePanelScreenAdapt）。
    /// 调用前需把 EditorForceApplyInEditMode 置为 true，否则 ApplySafeArea 仍会因 !isPlaying 而 early-return。
    /// </summary>
    public void EditorTryApplyOnce()
    {
        if (!IsInit && !(IsInit = TryInit()))
            return;

        Dirty = true;
        ApplySafeArea();
    }

#endif

    #endregion
}
}
