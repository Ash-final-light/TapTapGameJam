using MonoHook;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Hierarchy 引用辅助：标记被引用/动画节点、高亮引用方、Inspector 列出引用源，
/// 并在重命名/删除/拖拽时弹出确认。
/// </summary>
[InitializeOnLoad]
public class HierarchyRefViewer
{
    private enum RefType
    {
        Self,
        Animation,
        Other
    }

    private struct RefData
    {
        public Dictionary<int, HashSet<int>> RefSources;
        public RefType RefType;

        public RefData(Dictionary<int, HashSet<int>> refSources, RefType refType)
        {
            RefSources = refSources;
            RefType = refType;
        }
    }

    private struct RefTypeViewData
    {
        public string Title;
        public Color Color;

        public RefTypeViewData(string title, Color color)
        {
            Title = title;
            Color = color;
        }
    }

    private const string kFoldTitle = "正被以下节点关联";
    private const float kReferenceIconSize = 20f;

    private static readonly HierarchyRefViewer sInstance = new HierarchyRefViewer();

    private readonly Dictionary<RefType, RefTypeViewData> mRefTypeViewDatas = new Dictionary<RefType, RefTypeViewData>()
    {
        { RefType.Self, new RefTypeViewData("自引用", new Color(1f, 1f, 1f)) },
        { RefType.Other, new RefTypeViewData("被引用", new Color(1f, 1f, 0.45f)) },
        { RefType.Animation, new RefTypeViewData("动画节点", new Color(0.6f, 1f, 0.6f)) },
    };

    private readonly Dictionary<int, RefData> mReferences = new Dictionary<int, RefData>();
    private readonly List<IAnimationClipSource> mAnimationClipSourcesTemp = new List<IAnimationClipSource>();
    private readonly List<AnimationClip> mAnimationClipTemp = new List<AnimationClip>();
    private readonly List<AnimationClip> mAnimationClipResultTemp = new List<AnimationClip>();
    private readonly List<EditorCurveBinding> mEditorCurveBindingTemp = new List<EditorCurveBinding>();

    private readonly Color mHighLightColor = new Color(1f, 1f, 0f, 0.15f);
    private readonly string mFoldoutKey = $"{nameof(HierarchyRefViewer)}.mFoldout";

    private Texture mRefIcon;
    private bool mFoldout;
    private Type mGameObjectTreeViewItemType;
    private FieldInfo mObjectPPTRFieldInfo;

    private HierarchyRefViewer()
    {
        mFoldout = EditorPrefs.GetBool(mFoldoutKey);
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        Editor.finishedDefaultHeaderGUI += OnFinishedDefaultHeaderGUI;
        HierarchyMananger.Instance.UpdateEvent += OnUpdate;
        HierarchyMananger.Instance.Refresh(true);

        HookHierarchyRenameGo();
        HookHierarchyDeleteGo();
        HookDoDrag();
    }

    private void HookHierarchyRenameGo()
    {
        mGameObjectTreeViewItemType = Type.GetType("UnityEditor.GameObjectTreeViewItem, UnityEditor.dll");
        mObjectPPTRFieldInfo = mGameObjectTreeViewItemType.GetField("m_ObjectPPTR", BindingFlags.Instance | BindingFlags.NonPublic);

        var originType = Type.GetType("UnityEditor.GameObjectTreeViewGUI, UnityEditor.dll");
        var targetType = typeof(HierarchyRefViewer);
        MethodInfo origin = originType.GetMethod(nameof(BeginRename), BindingFlags.Instance | BindingFlags.Public);
        MethodInfo target = targetType.GetMethod(nameof(BeginRename), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo proxy = targetType.GetMethod(nameof(BeginRenameProxy), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodHook hook = new MethodHook(origin, target, proxy);
        hook.Install();
    }

    private void HookHierarchyDeleteGo()
    {
        var originType = Type.GetType("UnityEditor.SceneHierarchy, UnityEditor.dll");
        var targetType = typeof(HierarchyRefViewer);
        MethodInfo origin = originType.GetMethod(nameof(DeleteGO), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo target = targetType.GetMethod(nameof(DeleteGO), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo proxy = targetType.GetMethod(nameof(DeleteGOProxy), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodHook hook = new MethodHook(origin, target, proxy);
        hook.Install();
    }

    private void HookDoDrag()
    {
        var originType = Type.GetType("UnityEditor.GameObjectsTreeViewDragging, UnityEditor.dll");
        var targetType = typeof(HierarchyRefViewer);
        MethodInfo origin = originType.GetMethod(nameof(DoDrag), BindingFlags.Instance | BindingFlags.Public);
        MethodInfo target = targetType.GetMethod(nameof(DoDrag), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo proxy = targetType.GetMethod(nameof(DoDragProxy), BindingFlags.Instance | BindingFlags.NonPublic);
        MethodHook hook = new MethodHook(origin, target, proxy);
        hook.Install();
    }

    private void OnUpdate()
    {
        RefreshBuffer();
    }

    private void RefreshBuffer()
    {
        mReferences.Clear();
        foreach (var componentProps in HierarchyMananger.Instance.ForeachPropertiesInHierachy())
        {
            var component = componentProps.Component;
            if (component.hideFlags != HideFlags.None)
                continue;

            int ownerId = component.gameObject.GetInstanceID();
            int componentID = component.GetInstanceID();

            if (component is Animation || component is Animator)
            {
                CollectAnimationClips(component.gameObject);
                foreach (var animationClip in mAnimationClipResultTemp)
                {
                    if (animationClip == null)
                        continue;

                    mEditorCurveBindingTemp.AddRange(AnimationUtility.GetCurveBindings(animationClip));
                    mEditorCurveBindingTemp.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(animationClip));
                    foreach (var binding in mEditorCurveBindingTemp)
                    {
                        var target = component.transform.Find(binding.path);
                        if (target == null)
                            continue;

                        AddRefTypeData(target.gameObject.GetInstanceID(), componentID, ownerId, RefType.Animation);
                    }

                    mEditorCurveBindingTemp.Clear();
                }

                mAnimationClipResultTemp.Clear();
            }

            foreach (var serializedProperty in componentProps)
            {
                if (serializedProperty.PropertyType != SerializedPropertyType.ObjectReference)
                    continue;

                if (serializedProperty.Property.objectReferenceValue == null)
                    continue;

                int targetID = (serializedProperty.Property.objectReferenceValue is Component referance) ?
                    referance.gameObject.GetInstanceID() : serializedProperty.Property.objectReferenceValue.GetInstanceID();

                AddRefTypeData(targetID, componentID, ownerId, targetID == ownerId ? RefType.Self : RefType.Other);
            }
        }
    }

    private void CollectAnimationClips(GameObject gameObject)
    {
        mAnimationClipResultTemp.Clear();
        if (gameObject == null)
            return;

        var clips = AnimationUtility.GetAnimationClips(gameObject);
        if (clips != null)
            mAnimationClipResultTemp.AddRange(clips);

        mAnimationClipSourcesTemp.Clear();
        gameObject.GetComponents(mAnimationClipSourcesTemp);
        int count = mAnimationClipSourcesTemp.Count;
        for (int i = 0; i < count; i++)
        {
            mAnimationClipTemp.Clear();
            mAnimationClipSourcesTemp[i].GetAnimationClips(mAnimationClipTemp);
            mAnimationClipResultTemp.AddRange(mAnimationClipTemp);
        }

        mAnimationClipSourcesTemp.Clear();
        mAnimationClipTemp.Clear();
    }

    private void AddRefTypeData(int targetID, int componentID, int ownerId, RefType refType)
    {
        if (!mReferences.TryGetValue(targetID, out var refData))
        {
            refData = new RefData(new Dictionary<int, HashSet<int>>(), refType);
            mReferences.Add(targetID, refData);
        }
        else if ((int)refType > (int)refData.RefType)
        {
            refData.RefType = refType;
            mReferences[targetID] = refData;
        }

        if (!refData.RefSources.TryGetValue(ownerId, out var refTypeDatas))
        {
            refTypeDatas = new HashSet<int>();
            refData.RefSources.Add(ownerId, refTypeDatas);
        }

        refTypeDatas.Add(componentID);
    }

    private void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (mReferences.TryGetValue(instanceID, out var refData))
        {
            var refTypeViewData = mRefTypeViewDatas[refData.RefType];
            var titleContent = EditorGUIUtility.TrTempContent(refTypeViewData.Title);

            var colorTemp = GUI.color;
            GUI.color = refTypeViewData.Color;
            float size = Math.Min(selectionRect.width, GUI.skin.label.CalcSize(titleContent).x);
            Rect rect = new Rect(selectionRect.xMax - size, selectionRect.y, size, selectionRect.height);

            EditorGUI.DropShadowLabel(rect, titleContent);

            var icon = GetRefIcon();
            if (icon != null)
            {
                size = Math.Min(selectionRect.width, kReferenceIconSize);
                rect = new Rect(rect.xMax - rect.width - size, rect.y, kReferenceIconSize, kReferenceIconSize);
                GUI.DrawTexture(rect, icon);
            }

            GUI.color = colorTemp;
        }

        if (Selection.count != 1)
            return;

        if (Selection.activeObject == null)
            return;

        if (Selection.activeObject is not GameObject selectedGo)
            return;

        if (!mReferences.TryGetValue(selectedGo.GetInstanceID(), out refData))
            return;

        if (!refData.RefSources.ContainsKey(instanceID))
            return;

        EditorGUI.DrawRect(selectionRect, mHighLightColor);
    }

    private Texture GetRefIcon()
    {
        if (mRefIcon != null)
            return mRefIcon;

        mRefIcon = EditorGUIUtility.IconContent("sv_icon_dot0_pix16_gizmo").image;
        if (mRefIcon == null)
            mRefIcon = EditorGUIUtility.FindTexture("sv_icon_dot0_pix16_gizmo");
        return mRefIcon;
    }

    private void OnFinishedDefaultHeaderGUI(Editor editor)
    {
        if (editor.targets.Length != 1)
            return;

        if (editor.target is not GameObject go)
            return;

        int instanceID = go.GetInstanceID();
        if (!mReferences.TryGetValue(instanceID, out var refData))
            return;

        if (refData.RefSources.Count <= 0)
            return;

        EditorGUI.indentLevel++;

        GUILayout.BeginHorizontal("DD HeaderStyle");
        EditorGUI.BeginChangeCheck();
        mFoldout = EditorGUILayout.Foldout(mFoldout, kFoldTitle);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetBool(mFoldoutKey, mFoldout);

        GUILayout.EndHorizontal();

        if (!mFoldout)
        {
            EditorGUI.indentLevel--;
            return;
        }

        foreach (var kv in refData.RefSources)
        {
            GameObject owner = (GameObject)EditorUtility.InstanceIDToObject(kv.Key);
            if (owner == null)
                continue;

            EditorGUILayout.LabelField($"{owner.name}:");
            GUI.enabled = false;
            EditorGUI.indentLevel++;
            foreach (var refInstanceId in kv.Value)
            {
                EditorGUILayout.ObjectField(EditorUtility.InstanceIDToObject(refInstanceId), typeof(Component), true);
            }

            EditorGUI.indentLevel--;
            GUI.enabled = true;
        }

        EditorGUI.indentLevel--;
    }

    private bool BeginRename(TreeViewItem item, float delay)
    {
        if (sInstance.mGameObjectTreeViewItemType.IsAssignableFrom(item.GetType()))
        {
            GameObject gameObject = (GameObject)sInstance.mObjectPPTRFieldInfo.GetValue(item);
            if (sInstance.mReferences.TryGetValue(gameObject.GetInstanceID(), out var refData))
            {
                if (refData.RefType == RefType.Animation)
                {
                    if (!ShowConfirm("警告！", "更改动画节点名称会导致动画失效！！！！！！！"))
                        return false;
                }
            }
        }

        return BeginRenameProxy(item, delay);
    }

    private void DeleteGO()
    {
        foreach (var gameObject in Selection.gameObjects)
        {
            var instanceID = gameObject.GetInstanceID();
            if (!sInstance.mReferences.TryGetValue(instanceID, out var refData))
                continue;

            if (refData.RefType == RefType.Animation)
            {
                if (!ShowConfirm("警告！", "删除动画节点会导致动画失效！！！！！！！"))
                    return;
            }

            if (refData.RefType == RefType.Other)
            {
                if (!ShowConfirm("警告！", "删除被引用节点会导致脚本引用丢失！！！！！！"))
                    return;
            }

            break;
        }

        DeleteGOProxy();
    }

    private DragAndDropVisualMode DoDrag(TreeViewItem parentItem, TreeViewItem targetItem, bool perform, int dropPos)
    {
        if (perform)
        {
            GameObject parentGameObject = null;
            if (parentItem != null && sInstance.mGameObjectTreeViewItemType.IsAssignableFrom(parentItem.GetType()))
            {
                parentGameObject = (GameObject)sInstance.mObjectPPTRFieldInfo.GetValue(parentItem);
            }

            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is not GameObject gameObject)
                    continue;

                if (!sInstance.mReferences.TryGetValue(gameObject.GetInstanceID(), out var refData))
                    continue;

                if (refData.RefType != RefType.Animation)
                    continue;

                if (gameObject.transform.parent != null && gameObject.transform.parent.gameObject != parentGameObject)
                {
                    if (!ShowConfirm("警告！", "移动动画节点会导致动画失效！！！！！！！"))
                        return DragAndDropVisualMode.Rejected;
                }

                break;
            }
        }

        return DoDragProxy(parentItem, targetItem, perform, dropPos);
    }

    private static bool ShowConfirm(string title, string message)
    {
        return EditorUtility.DisplayDialog(title, message, "确认", "取消");
    }

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    private bool BeginRenameProxy(TreeViewItem item, float delay)
    {
        PadProxyBody();
        return false;
    }

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    private void DeleteGOProxy()
    {
        PadProxyBody();
    }

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    private DragAndDropVisualMode DoDragProxy(TreeViewItem parentItem, TreeViewItem targetItem, bool perform, int dropPos)
    {
        PadProxyBody();
        return DragAndDropVisualMode.Rejected;
    }

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    private static int PadProxyBody()
    {
        int pad = 0;
        pad += 1;
        pad += 2;
        pad += 3;
        pad += 4;
        pad += 5;
        return pad;
    }
}
