#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using AshFramework.UI.ScreenAdaptation.Components;
using AshFramework.UI.ScreenAdaptation.Editor.Tools;
using AshFramework.UI.ScreenAdaptation.Utilities;

namespace AshFramework.UI.ScreenAdaptation.Editor.Inspectors
{
[CustomEditor(typeof(CanvasWidthAdjuster))]
public class CanvasWidthAdjusterEditor : UnityEditor.Editor
{
    private SerializedProperty mCanvasProp;
    private SerializedProperty mContentProp;
    private SerializedProperty mLeftProp;
    private SerializedProperty mRightProp;

    private CanvasWidthAdjuster mAdjuster;

    // Gizmos 设置 (静态变量以便 DrawGizmo 访问)
    private static bool sShowGizmos = false;
    private static bool sShowSafeArea = true;
    private static bool sShowBorders = true;
    private static Color sSafeAreaColor = new Color(0, 0, 1, 0.1f);
    private static Color sLeftBorderColor = new Color(0, 1, 0, 0.1f);
    private static Color sRightBorderColor = new Color(0, 1, 0, 0.1f);
    private static Color sTopBorderColor = new Color(0, 1, 0, 0.1f);
    private static Color sBottomBorderColor = new Color(0, 1, 0, 0.1f);

    private const string kShowGizmosPrefKey = "CanvasWidthAdjuster_ShowGizmos2";
    private const string kShowSafeAreaPrefKey = "CanvasWidthAdjuster_ShowSafeArea";
    private const string kShowBordersPrefKey = "CanvasWidthAdjuster_ShowBorders";
    private const string kSafeAreaColorKey = "CanvasWidthAdjuster_SafeAreaColor";
    private const string kLeftBorderColorKey = "CanvasWidthAdjuster_LeftColor";
    private const string kRightBorderColorKey = "CanvasWidthAdjuster_RightColor";
    private const string kTopBorderColorKey = "CanvasWidthAdjuster_TopColor";
    private const string kBottomBorderColorKey = "CanvasWidthAdjuster_BottomColor";
    
    private void OnEnable()
    {
        mCanvasProp = serializedObject.FindProperty("mCanvas");
        mContentProp = serializedObject.FindProperty("mContent");
        mLeftProp = serializedObject.FindProperty("mLeft");
        mRightProp = serializedObject.FindProperty("mRight");

        mAdjuster = (CanvasWidthAdjuster)target;

        // 加载持久化设置
        sShowGizmos = EditorPrefs.GetBool(kShowGizmosPrefKey, false);
        sShowSafeArea = EditorPrefs.GetBool(kShowSafeAreaPrefKey, true);
        sShowBorders = EditorPrefs.GetBool(kShowBordersPrefKey, true);
        sSafeAreaColor = LoadColor(kSafeAreaColorKey, new Color(0, 0, 1, 0.1f));
        sLeftBorderColor = LoadColor(kLeftBorderColorKey, new Color(0, 1, 0, 0.1f));
        sRightBorderColor = LoadColor(kRightBorderColorKey, new Color(0, 1, 0, 0.1f));
        sTopBorderColor = LoadColor(kTopBorderColorKey, new Color(0, 1, 0, 0.1f));
        sBottomBorderColor = LoadColor(kBottomBorderColorKey, new Color(0, 1, 0, 0.1f));
    }

    private static Color LoadColor(string key, Color defaultColor)
    {
        string hex = EditorPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(hex)) return defaultColor;
        if (ColorUtility.TryParseHtmlString("#" + hex, out Color color)) return color;
        return defaultColor;
    }

    private static void SaveColor(string key, Color color)
    {
        EditorPrefs.SetString(key, ColorUtility.ToHtmlStringRGBA(color));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("基础配置", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(mCanvasProp, new GUIContent("Canvas (用于计算尺寸)"));
        EditorGUILayout.PropertyField(mContentProp, new GUIContent("Content (受适配影响的内容)"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("黑边物件配置", EditorStyles.boldLabel);

        DrawBorderProperty(mLeftProp, "左侧黑边 (Left)");
        DrawBorderProperty(mRightProp, "右侧黑边 (Right)");

        DrawRuntimeBorderProperty(mAdjuster.Top, "上方黑边 (Top)");
        DrawRuntimeBorderProperty(mAdjuster.Bottom, "下方黑边 (Bottom)");

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Gizmos 绘制设置", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        sShowGizmos = EditorGUILayout.Toggle("显示适配辅助区域", sShowGizmos);

        if (sShowGizmos)
        {
            EditorGUILayout.BeginHorizontal();
            sShowSafeArea = EditorGUILayout.ToggleLeft("显示安全区域", sShowSafeArea, GUILayout.Width(120));
            sShowBorders = EditorGUILayout.ToggleLeft("显示黑边区域", sShowBorders, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            if (sShowSafeArea)
            {
                sSafeAreaColor = EditorGUILayout.ColorField("安全区域颜色", sSafeAreaColor);
            }

            if (sShowBorders)
            {
                EditorGUILayout.BeginHorizontal();
                sLeftBorderColor = EditorGUILayout.ColorField("左黑边", sLeftBorderColor);
                sRightBorderColor = EditorGUILayout.ColorField("右黑边", sRightBorderColor);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                sTopBorderColor = EditorGUILayout.ColorField("上黑边", sTopBorderColor);
                sBottomBorderColor = EditorGUILayout.ColorField("下黑边", sBottomBorderColor);
                EditorGUILayout.EndHorizontal();
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(kShowGizmosPrefKey, sShowGizmos);
            EditorPrefs.SetBool(kShowSafeAreaPrefKey, sShowSafeArea);
            EditorPrefs.SetBool(kShowBordersPrefKey, sShowBorders);
            SaveColor(kSafeAreaColorKey, sSafeAreaColor);
            SaveColor(kLeftBorderColorKey, sLeftBorderColor);
            SaveColor(kRightBorderColorKey, sRightBorderColor);
            SaveColor(kTopBorderColorKey, sTopBorderColor);
            SaveColor(kBottomBorderColorKey, sBottomBorderColor);
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        DrawAdaptationStatus();

        serializedObject.ApplyModifiedProperties();

        if (Application.isPlaying)
        {
            Repaint();
        }
    }

    [DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy | GizmoType.NotInSelectionHierarchy)]
    private static void DrawGizmos(CanvasWidthAdjuster adjuster, GizmoType gizmoType)
    {
        if (!sShowGizmos || adjuster == null || adjuster.Top == null)
            return;

        RectTransform canvasRectTr = adjuster.CanvasRectTr;
        if (canvasRectTr == null)
            return;

        var result = adjuster.LastAdaptResult;

        Vector3[] corners = new Vector3[4];
        canvasRectTr.GetWorldCorners(corners);

        float worldWidth = Vector3.Distance(corners[0], corners[3]);
        float worldHeight = Vector3.Distance(corners[0], corners[1]);

        var canvasRect = canvasRectTr.rect;
        float canvasWidth = canvasRect.width;
        float canvasHeight = canvasRect.height;
        if (canvasWidth <= 0 || canvasHeight <= 0)
            return;

        // 1. 绘制安全区域
        if (sShowSafeArea)
        {
            Rect safeRect = result.BorderSafeArea;
            Vector3 safeBottomLeft = corners[0] + (canvasRectTr.right * (safeRect.x / canvasWidth) * worldWidth)
                                                + (canvasRectTr.up * (safeRect.y / canvasHeight) * worldHeight);
            Vector3 safeTopRight = corners[0] + (canvasRectTr.right * (safeRect.xMax / canvasWidth) * worldWidth)
                                              + (canvasRectTr.up * (safeRect.yMax / canvasHeight) * worldHeight);

            Vector3[] safeCorners = { safeBottomLeft, new(safeBottomLeft.x, safeTopRight.y, safeBottomLeft.z), safeTopRight, new Vector3(safeTopRight.x, safeBottomLeft.y, safeTopRight.z) };
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.DrawSolidRectangleWithOutline(safeCorners, sSafeAreaColor, Color.clear);

            Handles.Label(safeBottomLeft + Vector3.down * 0.05f, $"BorderSafeRect:{result.BorderSafeArea} CanvasArea{result.RootCanvasArea}");
        }

        // 2. 绘制黑边区域
        if (sShowBorders)
        {
            // 宽度方向裁剪 (左/右)
            if (result.IsWidthAdapt)
            {
                float leftClipWorld = (result.ClippedWidthLeft / canvasWidth) * worldWidth;
                float rightClipWorld = (result.ClippedWidthRight / canvasWidth) * worldWidth;

                if (leftClipWorld > 0)
                {
                    Vector3[] leftCorners = new Vector3[] { corners[0], corners[1], corners[1] + canvasRectTr.right * leftClipWorld, corners[0] + canvasRectTr.right * leftClipWorld };
                    Handles.DrawSolidRectangleWithOutline(leftCorners, sLeftBorderColor, Color.clear);
                }

                if (rightClipWorld > 0)
                {
                    Vector3[] rightCorners = new Vector3[] { corners[3] - canvasRectTr.right * rightClipWorld, corners[2] - canvasRectTr.right * rightClipWorld, corners[2], corners[3] };
                    Handles.DrawSolidRectangleWithOutline(rightCorners, sRightBorderColor, Color.clear);
                }
            }

            // 高度方向裁剪 (上/下)
            if (result.IsHeightAdapted)
            {
                float bottomClipWorld = (result.ClippedHeightBottom / canvasHeight) * worldHeight;
                float topClipWorld = (result.ClippedHeightTop / canvasHeight) * worldHeight;

                if (bottomClipWorld > 0)
                {
                    Vector3[] bottomCorners = new Vector3[] { corners[0], corners[0] + canvasRectTr.up * bottomClipWorld, corners[3] + canvasRectTr.up * bottomClipWorld, corners[3] };
                    Handles.DrawSolidRectangleWithOutline(bottomCorners, sBottomBorderColor, Color.clear);
                }

                if (topClipWorld > 0)
                {
                    Vector3[] topCorners = new Vector3[] { corners[1] - canvasRectTr.up * topClipWorld, corners[1], corners[2], corners[2] - canvasRectTr.up * topClipWorld };
                    Handles.DrawSolidRectangleWithOutline(topCorners, sTopBorderColor, Color.clear);
                }
            }
        }
    }

    private void DrawBorderProperty(SerializedProperty prop, string label)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(prop, new GUIContent(label));
        RectTransform rectTr = prop.objectReferenceValue as RectTransform;
        DrawBorderStatusUI(rectTr, prop == mLeftProp || prop == mRightProp);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuntimeBorderProperty(RectTransform rectTr, string label)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth - 2));
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(rectTr, typeof(RectTransform), true);
        }

        DrawBorderStatusUI(rectTr, false);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBorderStatusUI(RectTransform rectTr, bool checkWidth)
    {
        bool isActive = rectTr != null && rectTr.gameObject.activeInHierarchy;
        GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
        statusStyle.normal.textColor = isActive ? Color.green : Color.gray;
        GUILayout.Label(" ●", statusStyle, GUILayout.Width(20));
        if (rectTr != null)
        {
            string sizeLabel = checkWidth ? $"W:{rectTr.rect.width:F1}" : $"H:{rectTr.rect.height:F1}";
            GUILayout.Label(sizeLabel, GUILayout.Width(60));
        }
        else
        {
            GUILayout.Label("---", GUILayout.Width(60));
        }
    }

    private void DrawAdaptationStatus()
    {
        DrawAdaptationStatus(mAdjuster);
    }

    /// <summary>
    /// 绘制适配监控信息，可供 Debugger 窗口等复用
    /// </summary>
    public static void DrawAdaptationStatus(CanvasWidthAdjuster adjuster)
    {
        if (adjuster == null) return;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("适配监控", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"EditorDebugLastInfo: {adjuster.EditorDebugLastInfo}", GUILayout.Height(30f));
        float minRatio = ScreenWidthAdapterUtils.kMinResolutionAspectRatio;
        float maxRatio = ScreenWidthAdapterUtils.kMaxResolutionAspectRatio;
        float currentRatio = 0f;
        RectTransform canvasRect = adjuster.CanvasRectTr;
        if (canvasRect != null)
        {
            currentRatio = canvasRect.sizeDelta.x / canvasRect.sizeDelta.y;
        }
        else
        {
            currentRatio = (float)Screen.width / Screen.height;
            EditorGUILayout.HelpBox("未指定 Canvas，使用 Screen.width/height 进行比例预览", MessageType.None);
        }

        EditorGUILayout.LabelField("当前宽高比:", $"{currentRatio:F3}");
        EditorGUILayout.Space(12);
        DrawRatioProgressBar(currentRatio, minRatio, maxRatio);
        EditorGUILayout.Space(12);
        string statusText = "正常 (Normal)";
        Color statusColor = Color.cyan;
        if (currentRatio > maxRatio)
        {
            statusText = "超宽屏适配中 (Wide)";
            statusColor = new Color(1f, 0.5f, 0f);
        }
        else if (currentRatio < minRatio)
        {
            statusText = "超窄屏适配中 (Narrow)";
            statusColor = Color.yellow;
        }

        GUI.contentColor = statusColor;
        EditorGUILayout.LabelField("当前状态:", statusText, EditorStyles.boldLabel);
        GUI.contentColor = Color.white;
        EditorGUILayout.EndVertical();
    }

    private static void DrawRatioProgressBar(float current, float min, float max)
    {
        float start = 0.3f;
        float end = 1.0f;
        Rect rect = GUILayoutUtility.GetRect(18, 30, "TextField");
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        float GetX(float val) => rect.x + (Mathf.Clamp(val, start, end) - start) / (end - start) * rect.width;
        DrawMark(rect, GetX(min), "Min", Color.gray, false);
        DrawMark(rect, GetX(max), "Max", Color.gray, false);
        Color barColor = Color.cyan;
        if (current > max) barColor = new Color(1f, 0.5f, 0f);
        else if (current < min) barColor = Color.yellow;
        float currentX = GetX(current);
        Rect barRect = new Rect(rect.x, rect.y + 10, currentX - rect.x, rect.height - 20);
        EditorGUI.DrawRect(barRect, barColor * 0.4f);
        DrawMark(rect, currentX, current.ToString("F3"), barColor, true);
    }

    private static void DrawMark(Rect barRect, float x, string label, Color color, bool isCurrent)
    {
        float lineY = barRect.y + 5;
        float lineHeight = barRect.height - 10;
        EditorGUI.DrawRect(new Rect(x - 1, lineY, 2, lineHeight), color);
        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
        labelStyle.normal.textColor = color;
        labelStyle.alignment = TextAnchor.UpperCenter;
        float labelY = isCurrent ? barRect.y - 14 : barRect.y + barRect.height - 2;
        EditorGUI.LabelField(new Rect(x - 25, labelY, 50, 14), label, labelStyle);
    }
}
}
#endif
