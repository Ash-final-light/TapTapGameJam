#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using AshFramework.UI.ScreenAdaptation.Components;
using AshFramework.UI.ScreenAdaptation.Utilities;

namespace AshFramework.UI.ScreenAdaptation.Editor.Inspectors
{
[CustomEditor(typeof(SafeArea))]
public class SafeAreaEditor : UnityEditor.Editor
{
    #region 属性和字段

    private SafeArea mTarget;
    private SerializedProperty mPaddingTopProp;
    private SerializedProperty mPaddingBottomProp;
    private SerializedProperty mDisableTopAdaptProp;
    private SerializedProperty mDisableBottomAdaptProp;

    private static bool sShowGizmos = false;
    private static bool sShowApplyArea = true;
    private static bool sShowSystemArea = true;
    private static Color sApplySafeAreaColor = new Color(0, 1, 0, 0.5f);
    private static Color sSystemSafeAreaColor = new Color(1, 0, 0, 0.5f);

    private const string kShowGizmosPrefKey = "SafeArea_ShowGizmos2";
    private const string kShowApplyAreaPrefKey = "SafeArea_ShowApplyArea";
    private const string kShowSystemAreaPrefKey = "SafeArea_ShowSystemArea";
    private const string kApplySafeAreaColorKey = "SafeArea_ApplyColor";
    private const string kSystemSafeAreaColorKey = "SafeArea_SystemColor";

    #endregion

    #region life-cycle

    private void OnEnable()
    {
        mTarget = (SafeArea)target;
        mPaddingTopProp = serializedObject.FindProperty("PaddingTop");
        mPaddingBottomProp = serializedObject.FindProperty("PaddingBottom");
        mDisableTopAdaptProp = serializedObject.FindProperty("DisableTopAdapt");
        mDisableBottomAdaptProp = serializedObject.FindProperty("DisableBottomAdapt");

        sShowGizmos = EditorPrefs.GetBool(kShowGizmosPrefKey, false);
        sShowApplyArea = EditorPrefs.GetBool(kShowApplyAreaPrefKey, true);
        sShowSystemArea = EditorPrefs.GetBool(kShowSystemAreaPrefKey, true);
        sApplySafeAreaColor = LoadColor(kApplySafeAreaColorKey, new Color(0, 1, 0, 0.5f));
        sSystemSafeAreaColor = LoadColor(kSystemSafeAreaColorKey, new Color(1, 0, 0, 0.5f));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("适配设置", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(mPaddingTopProp, new GUIContent("上方内缩 (PaddingTop)"));
            EditorGUILayout.PropertyField(mPaddingBottomProp, new GUIContent("下方内缩 (PaddingBottom)"));
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(mDisableTopAdaptProp, new GUIContent("禁用上方适配 (DisableTop)"));
            EditorGUILayout.PropertyField(mDisableBottomAdaptProp, new GUIContent("禁用下方适配 (DisableBottom)"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Gizmos 绘制设置", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            sShowGizmos = EditorGUILayout.Toggle("显示适配辅助区域", sShowGizmos);

            EditorGUI.BeginDisabledGroup(!sShowGizmos);
            EditorGUILayout.BeginHorizontal();
            const float width = 120f;
            sShowApplyArea = EditorGUILayout.ToggleLeft("ApplySafeArea", sShowApplyArea, GUILayout.Width(width));
            sApplySafeAreaColor = EditorGUILayout.ColorField("", sApplySafeAreaColor);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            sShowSystemArea = EditorGUILayout.ToggleLeft("SystemSafeArea", sShowSystemArea, GUILayout.Width(width));
            sSystemSafeAreaColor = EditorGUILayout.ColorField("", sSystemSafeAreaColor);
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(kShowGizmosPrefKey, sShowGizmos);
                EditorPrefs.SetBool(kShowApplyAreaPrefKey, sShowApplyArea);
                EditorPrefs.SetBool(kShowSystemAreaPrefKey, sShowSystemArea);
                SaveColor(kApplySafeAreaColorKey, sApplySafeAreaColor);
                SaveColor(kSystemSafeAreaColorKey, sSystemSafeAreaColor);
                SceneView.RepaintAll();
            }
        }

        EditorGUILayout.Space();
        DrawStatusInfo();

        serializedObject.ApplyModifiedProperties();

        if (!Application.isPlaying && GUI.changed)
        {
            EditorUtility.SetDirty(mTarget);
        }
    }

    #endregion

    #region private

    private void DrawStatusInfo()
    {
        DrawStatusInfo(mTarget);
    }

    public static void DrawStatusInfo(SafeArea target)
    {
        if (target == null)
            return;
        EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (!target.IsInit)
            {
                EditorGUILayout.HelpBox("组件尚未初始化或未在激活的 Canvas 下", MessageType.Warning);
            }
            else
            {
                var applyArea = target.GetApplySafeArea();
                var systemArea = ScreenWidthAdapterUtils.GetSystemUISafeArea(target.RootCanvasRectTr);
                EditorGUILayout.LabelField("SystemSafeArea(Pixel):", SafeArea.SystemSafeArea.ToString());
                EditorGUILayout.LabelField("SystemSafeArea(UI):", systemArea.ToString());
                if (target.WidthAdjuster != null)
                {
                    EditorGUILayout.LabelField("Border Rect:", target.WidthAdjuster.BorderSafeArea.ToString());
                }

                EditorGUILayout.LabelField("Apply Rect:", applyArea.ToString());
            }
        }
    }

    #endregion

    #region static

    private static Color LoadColor(string key, Color defaultColor)
    {
        string hex = EditorPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(hex))
            return defaultColor;
        if (ColorUtility.TryParseHtmlString("#" + hex, out Color color))
            return color;
        return defaultColor;
    }

    private static void SaveColor(string key, Color color)
    {
        EditorPrefs.SetString(key, ColorUtility.ToHtmlStringRGBA(color));
    }

    [DrawGizmo(GizmoType.Active | GizmoType.Selected)]
    private static void DrawGizmos(SafeArea safeArea, GizmoType gizmoType)
    {
        if (!sShowGizmos || safeArea == null)
            return;

        if (safeArea.RootCanvasRectTr == null)
            return;

        RectTransform rootCanvasTr = safeArea.RootCanvasRectTr;
        Vector3[] corners = new Vector3[4];
        rootCanvasTr.GetWorldCorners(corners);

        float worldWidth = Vector3.Distance(corners[0], corners[3]);
        float worldHeight = Vector3.Distance(corners[0], corners[1]);

        var sizeDelta = rootCanvasTr.rect.size;
        float canvasWidth = sizeDelta.x;
        float canvasHeight = sizeDelta.y;

        if (canvasWidth <= 0 || canvasHeight <= 0)
            return;

        // 获取区域
        Rect applyArea = safeArea.GetApplySafeArea();
        Rect systemArea = ScreenWidthAdapterUtils.GetSystemUISafeArea(safeArea.RootCanvasRectTr);

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        if (sShowSystemArea)
            DrawRectInWorld(rootCanvasTr, corners, systemArea, canvasWidth, canvasHeight, worldWidth, worldHeight, sSystemSafeAreaColor, "System Safe Area");

        if (sShowApplyArea)
            DrawRectInWorld(rootCanvasTr, corners, applyArea, canvasWidth, canvasHeight, worldWidth, worldHeight, sApplySafeAreaColor, "Applied Safe Area");
    }

    private static void DrawRectInWorld(RectTransform rootTr, Vector3[] corners, Rect area, float cW, float cH, float wW, float wH, Color color, string label)
    {
        var right = rootTr.right;
        var up = rootTr.up;
        Vector3 bottomLeft = corners[0] + (right * (area.x / cW) * wW) + (up * (area.y / cH) * wH);
        Vector3 topLeft = corners[0] + (right * (area.x / cW) * wW) + (up * (area.yMax / cH) * wH);
        Vector3 topRight = corners[0] + (right * (area.xMax / cW) * wW) + (up * (area.yMax / cH) * wH);
        Vector3 bottomRight = corners[0] + (right * (area.xMax / cW) * wW) + (up * (area.y / cH) * wH);

        Vector3[] rectCorners = { bottomLeft, topLeft, topRight, bottomRight };
        Handles.DrawSolidRectangleWithOutline(rectCorners, color, color * 2f);
        GUIStyle labelStyle = new GUIStyle
        {
            normal = { textColor = color * 2f },
            fontSize = 12
        };
        Handles.Label(bottomLeft + Vector3.down * 0.1f, $"{label}: {area}", labelStyle);
    }

    #endregion
}
}
#endif
