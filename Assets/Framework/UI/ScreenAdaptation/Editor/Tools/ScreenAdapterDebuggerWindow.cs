#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AshFramework.UI.ScreenAdaptation.Components;
using AshFramework.UI.ScreenAdaptation.Editor.Inspectors;

namespace AshFramework.UI.ScreenAdaptation.Editor.Tools
{
/// <summary>
/// 用于调试CanvasWidthAdjuster和SafeArea的窗口
/// 展示当前所有CanvasWidthAdjuster 和 SafeArea的适配情况
/// </summary>
public class ScreenAdapterDebuggerWindow : EditorWindow
{
    private struct ComponentEntry
    {
        public Component Component;
        public GameObject GameObject;
        public bool GameObjectActive;
        public bool ComponentEnabled;
        public string HierarchyPath;
        public bool Expanded;
    }

    #region 属性和字段

    private const double kRefreshInterval = 0.3;
    private const int kColumnStatusWidth = 120;
    private const int kColumnComponentWidth = 80;
    private const int kFakeOffsetMax = 300;
    private static readonly Color sColorActive = new(0.2f, 0.8f, 0.2f);
    private static readonly Color sColorInactive = Color.gray;

    private int mEditorFakeOffsetLeft = 0;
    private int mEditorFakeOffsetRight = 0;
    private int mEditorFakeOffsetTop = 0;
    private int mEditorFakeOffsetBottom = 0;

    private readonly List<ComponentEntry> mCanvasWidthAdjusterEntries = new();
    private readonly List<ComponentEntry> mSafeAreaEntries = new();
    private Vector2 mScrollCanvasWidthAdjuster = Vector2.zero;
    private Vector2 mScrollSafeArea = Vector2.zero;
    private double mLastRefreshTime = 0;
    private string mLastUpdateTimeString = "";
    private readonly HashSet<int> mExpandedInstanceIds = new();

    #endregion


    #region life-cycle

    [MenuItem(CanvasWidthAdjusterEditorHelper.kStrMenuItemPrefix + "/[工具] 屏幕适配Debugger", priority = 2)]
    private static void Open()
    {
        GetWindow<ScreenAdapterDebuggerWindow>("屏幕适配组件Debugger窗口");
    }

    private void OnEnable()
    {
        mLastRefreshTime = EditorApplication.timeSinceStartup;
        RefreshData();
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        ForceScreenAdapt(0f, 0f, 0f, 0f);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("模拟SafeArea上下偏移 (Canvas UI单位)", EditorStyles.boldLabel);
        bool offsetValueChanged = false;
        if (GUILayout.Button($"重置", GUILayout.Width(80)))
        {
            offsetValueChanged = true;
            mEditorFakeOffsetTop = 0;
            mEditorFakeOffsetBottom = 0;
            mEditorFakeOffsetLeft = 0;
            mEditorFakeOffsetRight = 0;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUI.BeginChangeCheck();
        mEditorFakeOffsetTop = EditorGUILayout.IntSlider("上偏移 (Top)", mEditorFakeOffsetTop, 0, -kFakeOffsetMax);
        mEditorFakeOffsetBottom = EditorGUILayout.IntSlider("下偏移 (Bottom)", mEditorFakeOffsetBottom, 0, kFakeOffsetMax);
        mEditorFakeOffsetLeft = EditorGUILayout.IntSlider("左偏移 (Left)", mEditorFakeOffsetLeft, 0, kFakeOffsetMax);
        mEditorFakeOffsetRight = EditorGUILayout.IntSlider("右偏移 (Right)", mEditorFakeOffsetRight, 0, -kFakeOffsetMax);
        if (EditorGUI.EndChangeCheck() || offsetValueChanged)
            ForceScreenAdapt((float)mEditorFakeOffsetTop, (float)mEditorFakeOffsetBottom, (float)mEditorFakeOffsetLeft, (float)mEditorFakeOffsetRight);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("最后更新: " + mLastUpdateTimeString, EditorStyles.miniLabel);
        EditorGUILayout.Space(8);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("CanvasWidthAdjuster", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        mScrollCanvasWidthAdjuster = EditorGUILayout.BeginScrollView(mScrollCanvasWidthAdjuster, GUILayout.Height(200));
        DrawEntryList(mCanvasWidthAdjusterEntries, isCanvasWidthAdjuster: true);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(4);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("SafeArea", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        mScrollSafeArea = EditorGUILayout.BeginScrollView(mScrollSafeArea, GUILayout.Height(600));
        DrawEntryList(mSafeAreaEntries, isCanvasWidthAdjuster: false);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(4);
        EditorGUILayout.EndVertical();
    }

    #endregion

    #region private

    private void OnEditorUpdate()
    {
        if (this == null || !this)
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now - mLastRefreshTime >= kRefreshInterval)
        {
            mLastRefreshTime = now;
            RefreshData();
            Repaint();
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null) return "";
        var list = new List<string>();
        var current = transform;
        while (current != null)
        {
            list.Add(current.name);
            current = current.parent;
        }

        list.Reverse();
        return string.Join("/", list);
    }

    private void RefreshData()
    {
        mCanvasWidthAdjusterEntries.Clear();
        mSafeAreaEntries.Clear();

        var adjusters = FindObjectsOfType<CanvasWidthAdjuster>(true);
        var safeAreas = FindObjectsOfType<SafeArea>(true);

        foreach (var c in adjusters)
        {
            if (c == null) continue;
            var go = c.gameObject;
            if (!go.scene.IsValid()) continue;
            mCanvasWidthAdjusterEntries.Add(new ComponentEntry
            {
                Component = c,
                GameObject = go,
                GameObjectActive = go.activeInHierarchy,
                ComponentEnabled = c.enabled,
                HierarchyPath = GetHierarchyPath(go.transform),
                Expanded = mExpandedInstanceIds.Contains(go.GetInstanceID())
            });
        }

        foreach (var c in safeAreas)
        {
            if (c == null) continue;
            var go = c.gameObject;
            if (!go.scene.IsValid()) continue;
            mSafeAreaEntries.Add(new ComponentEntry
            {
                Component = c,
                GameObject = go,
                GameObjectActive = go.activeInHierarchy,
                ComponentEnabled = c.enabled,
                HierarchyPath = GetHierarchyPath(go.transform),
                Expanded = mExpandedInstanceIds.Contains(go.GetInstanceID())
            });
        }

        mLastUpdateTimeString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void ForceScreenAdapt(float top, float bottom, float left, float right)
    {
        SafeArea.SetEditorFakeSafeAreaOffset(top, bottom, left, right);
        foreach (var entry in mSafeAreaEntries)
        {
            var sa = entry.Component as SafeArea;
            if (sa == null)
                continue;
            if (!sa.gameObject.scene.IsValid())
                continue;
            sa.Dirty = true;
        }

        foreach (var entry in mCanvasWidthAdjusterEntries)
        {
            var adjuster = entry.Component as CanvasWidthAdjuster;
            if (adjuster == null)
                continue;
            if (!adjuster.gameObject.scene.IsValid())
                continue;
            adjuster.EditorForceAdapt = true;
        }
    }

    private void DrawTableHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField("对象", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("GameObject 状态", EditorStyles.boldLabel, GUILayout.Width(kColumnStatusWidth));
        EditorGUILayout.LabelField("组件状态", EditorStyles.boldLabel, GUILayout.Width(kColumnComponentWidth));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEntryList(List<ComponentEntry> entries, bool isCanvasWidthAdjuster)
    {
        DrawTableHeader();

        if (entries == null || entries.Count == 0)
        {
            EditorGUILayout.HelpBox("(无)", MessageType.None);
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.GameObject == null)
                continue;

            int instanceId = entry.GameObject.GetInstanceID();
            bool expanded = entry.Expanded;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            bool newExpanded = EditorGUILayout.Foldout(expanded, "");
            if (newExpanded != expanded)
            {
                if (newExpanded) mExpandedInstanceIds.Add(instanceId);
                else mExpandedInstanceIds.Remove(instanceId);
                entry.Expanded = newExpanded;
                entries[i] = entry;
                expanded = newExpanded;
            }

            EditorGUILayout.ObjectField(entry.GameObject, typeof(GameObject), true, GUILayout.ExpandWidth(true), GUILayout.MinHeight(20));

            Color prevColor = GUI.color;
            GUI.color = entry.GameObjectActive ? sColorActive : sColorInactive;
            GUILayout.Label(entry.GameObjectActive ? "Active" : "Inactive", GUILayout.Width(kColumnStatusWidth));
            GUI.color = entry.ComponentEnabled ? sColorActive : sColorInactive;
            GUILayout.Label(entry.ComponentEnabled ? "On" : "Off", GUILayout.Width(kColumnComponentWidth));
            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();

            if (expanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("GameObject 路径", entry.HierarchyPath, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(4);
                if (isCanvasWidthAdjuster && entry.Component is CanvasWidthAdjuster adjuster)
                    CanvasWidthAdjusterEditor.DrawAdaptationStatus(adjuster);
                else if (!isCanvasWidthAdjuster && entry.Component is SafeArea safeArea)
                    SafeAreaEditor.DrawStatusInfo(safeArea);
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }
    }

    #endregion
}
}
#endif
