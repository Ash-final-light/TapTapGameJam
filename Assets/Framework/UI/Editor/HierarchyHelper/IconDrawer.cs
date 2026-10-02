using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 根据组件类型给 Hierarchy 中的 Image / Text / Button 等节点画小图标。
/// </summary>
[InitializeOnLoad]
public class IconDrawer
{
    private List<(Type type, HierarchyIcon icon)> mIconInfoBuffer;
    private readonly Dictionary<int, List<HierarchyIcon>> mIcons = new Dictionary<int, List<HierarchyIcon>>();

    static IconDrawer()
    {
        _ = new IconDrawer();
    }

    private IconDrawer()
    {
        HierarchyMananger.Instance.UpdateEvent += OnUpdate;
        HierarchyIconMananger.Instance.HookOnDrawIcon(OnDrawIcon);
        HierarchyMananger.Instance.Refresh(true);
    }

    private void InitResources()
    {
        if (mIconInfoBuffer != null)
            return;

        mIconInfoBuffer = new List<(Type type, HierarchyIcon icon)>()
        {
            (typeof(Image), new HierarchyIcon(LoadBuiltinIcon("Image Icon"))),
            (typeof(Text), new HierarchyIcon(LoadBuiltinIcon("Text Icon"))),
            (typeof(Button), new HierarchyIcon(LoadBuiltinIcon("Button Icon"))),
        };
    }

    private static Texture LoadBuiltinIcon(string iconName)
    {
        var texture = EditorGUIUtility.IconContent(iconName).image;
        if (texture == null)
            texture = EditorGUIUtility.FindTexture(iconName);
        return texture;
    }

    private void OnUpdate()
    {
        InitResources();
        mIcons.Clear();

        foreach (var componentProps in HierarchyMananger.Instance.ForeachPropertiesInHierachy())
        {
            var component = componentProps.Component;
            Type type = component.GetType();

            HierarchyIcon icon = null;
            foreach (var item in mIconInfoBuffer)
            {
                if (type != item.type && !type.IsSubclassOf(item.type))
                    continue;

                icon = item.icon;
                break;
            }

            if (icon == null || icon.Texture == null)
                continue;

            var instanceID = component.gameObject.GetInstanceID();
            if (!mIcons.TryGetValue(instanceID, out var icons))
            {
                icons = new List<HierarchyIcon>();
                mIcons.Add(instanceID, icons);
            }

            icons.Add(icon);
        }
    }

    private List<HierarchyIcon> OnDrawIcon(int instanceID)
    {
        if (!mIcons.TryGetValue(instanceID, out var icons))
            return null;

        return icons;
    }
}
