using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hierarchy 行内组件图标数据。
/// </summary>
public class HierarchyIcon
{
    public Texture Texture { get; private set; }

    public Color? Color { get; private set; }

    public float Blank { get; private set; }

    public HierarchyIcon(Texture texture, float blank = 0f)
    {
        Texture = texture;
        Color = null;
        Blank = blank;
    }

    public HierarchyIcon(Texture texture, Color color, float blank = 0f)
    {
        Texture = texture;
        Color = color;
        Blank = blank;
    }
}

/// <summary>
/// 在 Hierarchy 物体名称右侧绘制组件小图标。
/// </summary>
[InitializeOnLoad]
public class HierarchyIconMananger
{
    private const string kSwitchKey = "HierarchyIconSwitch";
    private const string kMenuPath = "Tools/组件图标显示开关";

    public static HierarchyIconMananger Instance { get; private set; } = new HierarchyIconMananger();

    private readonly HashSet<Func<int, List<HierarchyIcon>>> mDrawList = new HashSet<Func<int, List<HierarchyIcon>>>();
    private readonly Dictionary<int, float> mNameWidths = new Dictionary<int, float>();
    private readonly GUIContent mNameContent = new GUIContent();
    private bool mButton;

    private HierarchyIconMananger()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        mButton = EditorPrefs.GetBool(kSwitchKey, true);
    }

    [MenuItem(kMenuPath)]
    private static void SwitchMenu()
    {
        Instance.mButton = !EditorPrefs.GetBool(kSwitchKey, true);
        EditorPrefs.SetBool(kSwitchKey, Instance.mButton);
    }

    [MenuItem(kMenuPath, true)]
    private static bool SwitchMenuBool()
    {
        Menu.SetChecked(kMenuPath, EditorPrefs.GetBool(kSwitchKey, true));
        return true;
    }

    public void HookOnDrawIcon(Func<int, List<HierarchyIcon>> callback)
    {
        mDrawList.Add(callback);
    }

    private void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (!mButton || Event.current.type != EventType.Repaint)
            return;

        var drawRect = default(Rect);
        var hasDrawRect = false;
        foreach (var func in mDrawList)
        {
            var icons = func(instanceID);
            if (icons == null || icons.Count == 0)
                continue;

            if (!hasDrawRect && !TryGetDrawRect(instanceID, selectionRect, out drawRect))
                return;

            hasDrawRect = true;
            foreach (var icon in icons)
            {
                if (icon.Texture == null)
                    continue;

                GUI.DrawTexture(drawRect, icon.Texture, ScaleMode.StretchToFill, true, 0.0f, icon.Color ?? GUI.color, 0f, 0f);
                drawRect.x += drawRect.width + icon.Blank;
            }
        }
    }

    private bool TryGetDrawRect(int instanceID, Rect selectionRect, out Rect drawRect)
    {
        drawRect = default;
        if (!mNameWidths.TryGetValue(instanceID, out var nameWidth))
        {
            if (EditorUtility.InstanceIDToObject(instanceID) is not GameObject viewObj)
                return false;

            mNameContent.text = viewObj.name;
            nameWidth = GUI.skin.label.CalcSize(mNameContent).x;
            mNameWidths.Add(instanceID, nameWidth);
        }

        var size = selectionRect.height;
        var x = selectionRect.x + nameWidth + size * 1.5f;
        drawRect = new Rect(x, selectionRect.y, size, size);
        return true;
    }

    private void OnHierarchyChanged()
    {
        mNameWidths.Clear();
    }
}
