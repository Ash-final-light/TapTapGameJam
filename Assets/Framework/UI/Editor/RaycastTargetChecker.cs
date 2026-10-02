using UnityEditor;
using UnityEngine.UI;
using UnityEngine;
using UnityEditor.SceneManagement;

public class RaycastTargetChecker : EditorWindow
{
    private Graphic[] graphics;
    private bool hideUnchecked = false;
    private bool showBorders = true;
    private Color borderColor = Color.blue;
    private Vector2 scrollPosition = Vector2.zero;
    private static RaycastTargetChecker instance = null;

    [MenuItem("Tools/RaycastTarget Checker")]
    private static void Open()
    {
        instance = instance ?? EditorWindow.GetWindow<RaycastTargetChecker>("RaycastTargets");
        instance.Show();
    }

    void OnEnable()
    {
        instance = this;
    }

    void OnDisable()
    {
        instance = null;
    }

    void OnGUI()
    {
        using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
        {
            showBorders = EditorGUILayout.Toggle("Show Gizmos", showBorders, GUILayout.Width(200.0f));
            borderColor = EditorGUILayout.ColorField(borderColor);
        }

        hideUnchecked = EditorGUILayout.Toggle("Hide Unchecked", hideUnchecked);
        GUILayout.Space(12.0f);
        Rect rect = GUILayoutUtility.GetLastRect();
        GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.25f);
        GUI.DrawTexture(new Rect(0.0f, rect.yMin + 6.0f, Screen.width, 4.0f), EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(0.0f, rect.yMin + 6.0f, Screen.width, 1.0f), EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(0.0f, rect.yMin + 9.0f, Screen.width, 1.0f), EditorGUIUtility.whiteTexture);
        GUI.color = Color.white;
        graphics = StageUtility.GetCurrentStageHandle().FindComponentsOfType<Graphic>();
        using (GUILayout.ScrollViewScope scrollViewScope = new GUILayout.ScrollViewScope(scrollPosition))
        {
            scrollPosition = scrollViewScope.scrollPosition;
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (hideUnchecked == false || graphic.raycastTarget == true)
                {
                    DrawElement(graphic);
                }
            }
        }

        Repaint();
    }

    private void DrawElement(Graphic graphic)
    {
        using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
        {
            Undo.RecordObject(graphic, "Modify RaycastTarget");
            graphic.raycastTarget = EditorGUILayout.Toggle(graphic.raycastTarget, GUILayout.Width(20));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(graphic, typeof(MaskableGraphic), true);
            EditorGUI.EndDisabledGroup();
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawGizmos(Graphic source, GizmoType gizmoType)
    {
        if (instance != null && instance.showBorders == true && source.raycastTarget == true)
        {
            Vector3[] corners = new Vector3[4];
            source.rectTransform.GetWorldCorners(corners);
            // left   - x
            // right  - z
            // top    - w
            // buttom - y
            // corners - 左下角，顺指针到右下角
            corners[0] += new Vector3(source.raycastPadding.x, source.raycastPadding.y, 0);
            corners[1] += new Vector3(source.raycastPadding.x, -source.raycastPadding.w, 0);
            corners[2] += new Vector3(-source.raycastPadding.z, -source.raycastPadding.w, 0);
            corners[3] += new Vector3(-source.raycastPadding.z, source.raycastPadding.y, 0);
            Gizmos.color = instance.borderColor;

            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);
            }

            if (Selection.activeGameObject == source.gameObject)
            {
                Gizmos.DrawLine(corners[0], corners[2]);
                Gizmos.DrawLine(corners[1], corners[3]);
            }
        }

        SceneView.RepaintAll();
    }
}
