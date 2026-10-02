#if UNITY_EDITOR && ASH_FRAMEWORK_WINDOW_SYSTEM
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Framework.UI;
using UnityEditor;
using UnityEngine;

namespace AshFramework.UI.Windowing.Editor
{
/// <summary>
/// 窗口管理：运行时监控与调试、已注册窗口列表。
/// </summary>
public class PopupWindowEditorWindow : EditorWindow
{
    private const string MenuPath = "Tools/UI/窗口管理";
    private const int TabRuntime = 0;
    private const int TabRegistered = 1;

    private int mCurrentTab;
    private string mRegisteredSearch = "";
    private string mDebugWindowPath = "";
    private Type mDebugWindowType;
    private List<string> mDebugInitParams = new();
    private int mDebugLifeTypeIndex = 0;
    private Vector2 mScrollMonitor, mScrollRegistered, mScrollDebugOpen;
    private List<RegisteredWindowEntry> mRegisteredList = new();
    private double mLastRepaintTime;
    private bool mDebugOpenFoldout = true;
    private bool mClosedCachedFoldout = true;

    private static readonly WindowLifeType[] LifeTypes = { WindowLifeType.Local, WindowLifeType.Global };

    private const int ColName = 140;
    private const int ColState = 52;
    private const int ColType = 56;
    private const int ColPause = 48;

    private static PropertyInfo s_ContainerFactoryProp;

    #region Menu & Lifecycle

    [MenuItem(MenuPath, priority = 210)]
    private static void Open()
    {
        var w = GetWindow<PopupWindowEditorWindow>("窗口管理");
        w.minSize = new Vector2(360, 280);
    }

    private void OnEnable()
    {
        RefreshRegisteredList();
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        if (!Application.isPlaying)
            return;
        if (EditorApplication.timeSinceStartup - mLastRepaintTime < 0.15)
            return;
        mLastRepaintTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void OnGUI()
    {
        mCurrentTab = GUILayout.Toolbar(mCurrentTab, new[] { "运行时监控与调试", "已注册窗口列表" });
        EditorGUILayout.Space(4);

        switch (mCurrentTab)
        {
            case TabRuntime:
                DrawRuntimeTab();
                break;
            case TabRegistered:
                DrawRegisteredTab();
                break;
        }
    }

    #endregion

    #region Runtime 运行时监控与调试

    private void DrawRuntimeTab()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("请进入 Play 模式后使用本页功能。", MessageType.Info);
            return;
        }

        var wm = WindowManager.Instance;
        if (wm == null)
        {
            EditorGUILayout.HelpBox("当前场景中未找到 WindowManager。", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
        mScrollMonitor = EditorGUILayout.BeginScrollView(mScrollMonitor);

        bool opening = wm.OpeningWindow;
        bool hasQueue = wm.HasQueueOp;
        EditorGUILayout.LabelField("正在打开窗口", opening ? "true" : "false");
        EditorGUILayout.LabelField("队列中有待打开", hasQueue ? "true" : "false");

        object currentOp = GetCurrentPopupOperation(wm);
        if (currentOp != null)
        {
            string path = GetOperationWindowPath(currentOp);
            EditorGUILayout.LabelField("当前操作窗口路径", path ?? "-");
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("已打开窗口", EditorStyles.miniBoldLabel);
        var windowsSnapshot = wm.Windows.ToList();
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("窗口名", GUILayout.Width(ColName));
        EditorGUILayout.LabelField("状态", GUILayout.Width(ColState));
        EditorGUILayout.LabelField("类型", GUILayout.Width(ColType));
        EditorGUILayout.LabelField("Pause", GUILayout.Width(ColPause));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("", GUILayout.Width(44));
        EditorGUILayout.EndHorizontal();
        int count = 0;
        foreach (var w in windowsSnapshot)
        {
            count++;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"{w.WindowName}", EditorStyles.label, GUILayout.Width(ColName)))
            {
                Selection.activeGameObject = w.gameObject;
                EditorGUIUtility.PingObject(w.gameObject);
            }
            EditorGUILayout.LabelField($"{w.State}", GUILayout.Width(ColState));
            EditorGUILayout.LabelField($"{w.WindowType}", GUILayout.Width(ColType));
            EditorGUILayout.LabelField(w.IsPause ? "true" : "false", GUILayout.Width(ColPause));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("关闭", GUILayout.Width(44)))
            {
                DebugClose(wm, w);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }
        if (count == 0)
        {
            EditorGUILayout.LabelField("（无）", GUILayout.Height(18));
        }

        var (closedCached, cachedContainer) = GetClosedCachedWindows(wm);
        if (closedCached.Count > 0)
        {
            EditorGUILayout.Space(6);
            mClosedCachedFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(mClosedCachedFoldout, "已关闭未销毁（缓存）");
            if (mClosedCachedFoldout)
            {
                const int colScript = 120;
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUILayout.LabelField("窗口名", GUILayout.Width(ColName));
                EditorGUILayout.LabelField("脚本", GUILayout.Width(colScript));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("", GUILayout.Width(56));
                EditorGUILayout.LabelField("", GUILayout.Width(44));
                EditorGUILayout.EndHorizontal();
                foreach (var w in closedCached)
                {
                    if (w == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(w.WindowName ?? "-", EditorStyles.label, GUILayout.Width(ColName)))
                    {
                        LocateCachedWindowInHierarchy(w);
                    }
                    string typeName = w.GetType().Name;
                    if (GUILayout.Button(typeName ?? "-", EditorStyles.label, GUILayout.Width(colScript)))
                    {
                        LocateCachedWindowInHierarchy(w);
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("填入路径", GUILayout.Width(56)))
                    {
                        mDebugWindowPath = w.WindowPath ?? w.WindowName ?? "";
                        SyncDebugWindowTypeFromPath();
                    }
                    if (GUILayout.Button("销毁", GUILayout.Width(44)))
                    {
                        TryDestroyWindowViaFactory(cachedContainer, w);
                        Repaint();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);
        mDebugOpenFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(mDebugOpenFoldout, "调试打开");
        if (mDebugOpenFoldout)
        {
            mScrollDebugOpen = EditorGUILayout.BeginScrollView(mScrollDebugOpen);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("窗口路径", GUILayout.Width(70));
            mDebugWindowPath = EditorGUILayout.TextField(mDebugWindowPath);
            if (GUILayout.Button("从列表选", GUILayout.Width(60)))
                ShowRegisteredWindowPathMenu();
            EditorGUILayout.EndHorizontal();

            SyncDebugWindowTypeFromPath();

            GetInitMethodInfo(mDebugWindowType, out var initMethod, out var initParamTypes);
            int paramCount = initParamTypes?.Length ?? 0;
            while (mDebugInitParams.Count < paramCount)
                mDebugInitParams.Add("");
            while (mDebugInitParams.Count > paramCount)
                mDebugInitParams.RemoveAt(mDebugInitParams.Count - 1);
            if (initMethod != null && initParamTypes != null && paramCount > 0)
            {
                for (int i = 0; i < paramCount; i++)
                {
                    var paramType = initParamTypes[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Init 参数{i + 1} ({paramType.Name})", GUILayout.Width(140));
                    DrawInitParamField(paramType, i);
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("生命周期", GUILayout.Width(42));
            mDebugLifeTypeIndex = EditorGUILayout.Popup(mDebugLifeTypeIndex, new[] { "局部 (Local)", "全局 (Global)" }, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("打开窗口 (Popup)"))
            {
                if (string.IsNullOrEmpty(mDebugWindowPath))
                {
                    EditorUtility.DisplayDialog("窗口管理", "请填写窗口路径。", "确定");
                }
                else
                {
                    Action<GameObject> init = null;
                    if (mDebugWindowType != null && initMethod != null && initParamTypes != null)
                    {
                        bool hasAnyParam = mDebugInitParams.Any(p => !string.IsNullOrWhiteSpace(p));
                        if (hasAnyParam || paramCount == 0)
                        {
                            var windowType = mDebugWindowType;
                            var method = initMethod;
                            var paramTypes = initParamTypes;
                            var paramStrings = mDebugInitParams.ToList();
                            init = go => InvokeWindowInit(go, windowType, method, paramTypes, paramStrings);
                        }
                    }
                    var operation = new PopupWindowOperation(mDebugWindowPath, init, false, LifeTypes[mDebugLifeTypeIndex], null, false);
                    wm.Popup(null, operation);
                    Repaint();
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("关闭最后 (CloseLast)"))
        {
            var lastWindow = wm.GetTopWindow();
            if (lastWindow != null)
            {
                wm.CloseLast(false);
            }
            Repaint();
        }
        if (GUILayout.Button("关闭全部 (CloseAll)"))
        {
            wm.CloseAll(false);
            Repaint();
        }
        EditorGUILayout.EndHorizontal();
    }

    private static void DebugClose(WindowManager wm, PopupWindow window)
    {
        wm.Close(window.gameObject, false);
    }

    private void SyncDebugWindowTypeFromPath()
    {
        if (string.IsNullOrEmpty(mDebugWindowPath))
            mDebugWindowType = null;
        else
        {
            var entry = mRegisteredList.FirstOrDefault(r => r.Path == mDebugWindowPath);
            mDebugWindowType = entry.Type;
        }
    }

    private void ShowRegisteredWindowPathMenu()
    {
        int idx = mRegisteredList.FindIndex(r => r.Path == mDebugWindowPath);
        if (idx < 0) idx = 0;
        var menu = new GenericMenu();
        for (int i = 0; i < mRegisteredList.Count; i++)
        {
            var r = mRegisteredList[i];
            int j = i;
            menu.AddItem(new GUIContent(r.Path), j == idx, () =>
            {
                mDebugWindowPath = mRegisteredList[j].Path;
                mDebugWindowType = mRegisteredList[j].Type;
            });
        }
        if (mRegisteredList.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("（无已注册窗口）"));
        }
        menu.ShowAsContext();
    }

    private static IPopupWindowsFactory GetFactoryFromContainer(WindowContainer container)
    {
        if (container == null) return null;
        try
        {
            if (s_ContainerFactoryProp == null)
            {
                s_ContainerFactoryProp = typeof(WindowContainer).GetProperty("PopupWindowsFactory", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            return s_ContainerFactoryProp?.GetValue(container) as IPopupWindowsFactory;
        }
        catch { return null; }
    }

    private static (List<PopupWindow> list, WindowContainer container) GetClosedCachedWindows(WindowManager wm)
    {
        List<PopupWindow> list = new();
        var container = UnityEngine.Object.FindObjectOfType<WindowContainer>();
        if (container == null) return (list, null);
        var factory = GetFactoryFromContainer(container);
        if (factory == null) return (list, container);
        try
        {
            object rawFactory = factory;
            var t = rawFactory.GetType();
            var inner = t.GetField("mPopupWindowsFactory", BindingFlags.Instance | BindingFlags.NonPublic);
            if (inner != null)
            {
                rawFactory = inner.GetValue(rawFactory) ?? rawFactory;
            }
            t = rawFactory.GetType();
            var dictField = t.GetField("mCachedWindows", BindingFlags.Instance | BindingFlags.NonPublic);
            if (dictField == null) return (list, container);
            var dict = dictField.GetValue(rawFactory);
            if (dict == null) return (list, container);
            var valuesProp = dict.GetType().GetProperty("Values", BindingFlags.Public | BindingFlags.Instance);
            if (valuesProp == null) return (list, container);
            var values = valuesProp.GetValue(dict) as System.Collections.IEnumerable;
            if (values == null) return (list, container);
            HashSet<PopupWindow> openSet = new(wm.Windows);
            var windowProp = typeof(CachedWindow).GetProperty("Window", BindingFlags.Public | BindingFlags.Instance);
            foreach (var item in values)
            {
                if (item == null) continue;
                var window = windowProp?.GetValue(item) as PopupWindow;
                if (window != null && !openSet.Contains(window))
                    list.Add(window);
            }
        }
        catch { }
        return (list, container);
    }

    private static void TryDestroyWindowViaFactory(WindowContainer container, PopupWindow window)
    {
        var factory = GetFactoryFromContainer(container);
        if (factory == null || window == null) return;
        factory.DestroyWindows(w => w == window);
    }

    private static object GetCurrentPopupOperation(WindowManager wm)
    {
        try
        {
            var f = typeof(WindowManager).GetField("mCurPopupWindowBehaviour", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) return null;
            var behaviour = f.GetValue(wm);
            if (behaviour == null) return null;
            var prop = behaviour.GetType().GetProperty("PopupWindowOperation", BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(behaviour);
        }
        catch { return null; }
    }

    private static string GetOperationWindowPath(object operation)
    {
        if (operation == null) return null;
        try
        {
            var p = operation.GetType().GetProperty("WindowPath", BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(operation) as string;
        }
        catch { return null; }
    }

    private static void GetInitMethodInfo(Type windowType, out MethodInfo method, out Type[] paramTypes)
    {
        method = null;
        paramTypes = null;
        if (windowType == null) return;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        MethodInfo best = null;
        int bestLen = -1;
        foreach (var m in windowType.GetMethods(flags))
        {
            if (m.Name != "Init") continue;
            var ps = m.GetParameters();
            if (ps.Length > bestLen)
            {
                bestLen = ps.Length;
                best = m;
            }
        }
        if (best == null) return;
        method = best;
        paramTypes = best.GetParameters().Select(p => p.ParameterType).ToArray();
    }

    private static bool IsBasicInitParamType(Type t)
    {
        if (t == null) return false;
        return t.IsEnum || t == typeof(string) || t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double)
            || t == typeof(bool) || t == typeof(uint) || t == typeof(byte) || t == typeof(short);
    }

    private void DrawInitParamField(Type paramType, int paramIndex)
    {
        string current = paramIndex < mDebugInitParams.Count ? mDebugInitParams[paramIndex] : "";
        if (paramType.IsEnum)
        {
            Enum enumVal;
            try
            {
                enumVal = (Enum)Enum.Parse(paramType, string.IsNullOrEmpty(current) ? "0" : current, true);
            }
            catch
            {
                var values = Enum.GetValues(paramType);
                enumVal = (Enum)(values.Length > 0 ? values.GetValue(0) : Enum.ToObject(paramType, 0));
            }
            var newVal = EditorGUILayout.EnumPopup(enumVal);
            mDebugInitParams[paramIndex] = newVal.ToString();
            return;
        }
        if (paramType == typeof(int))
        {
            int v = int.TryParse(current, out var i) ? i : 0;
            mDebugInitParams[paramIndex] = EditorGUILayout.IntField(v).ToString();
            return;
        }
        if (paramType == typeof(long))
        {
            long v = long.TryParse(current, out var l) ? l : 0L;
            mDebugInitParams[paramIndex] = EditorGUILayout.LongField(v).ToString();
            return;
        }
        if (paramType == typeof(float))
        {
            float v = float.TryParse(current, out var f) ? f : 0f;
            mDebugInitParams[paramIndex] = EditorGUILayout.FloatField(v).ToString();
            return;
        }
        if (paramType == typeof(double))
        {
            double v = double.TryParse(current, out var d) ? d : 0.0;
            mDebugInitParams[paramIndex] = EditorGUILayout.DoubleField(v).ToString();
            return;
        }
        if (paramType == typeof(bool))
        {
            bool v = bool.TryParse(current, out var b) && b;
            mDebugInitParams[paramIndex] = EditorGUILayout.Toggle(v).ToString();
            return;
        }
        if (paramType == typeof(uint) || paramType == typeof(byte) || paramType == typeof(short))
        {
            mDebugInitParams[paramIndex] = EditorGUILayout.TextField(current);
            return;
        }
        if (paramType == typeof(string))
        {
            mDebugInitParams[paramIndex] = EditorGUILayout.TextField(current);
            return;
        }
        mDebugInitParams[paramIndex] = EditorGUILayout.TextArea(current, GUILayout.MinHeight(36));
    }

    private static void InvokeWindowInit(GameObject go, Type windowType, MethodInfo initMethod, Type[] paramTypes, List<string> paramStrings)
    {
        if (go == null || initMethod == null) return;
        var comp = go.GetComponent(windowType) as MonoBehaviour;
        if (comp == null) return;
        int n = paramTypes?.Length ?? 0;
        var args = new object[n];
        for (int i = 0; i < n; i++)
        {
            var paramType = paramTypes[i];
            string s = (paramStrings != null && i < paramStrings.Count) ? paramStrings[i]?.Trim() : "";
            object value = ParseInitParam(s, paramType);
            if (value == null && paramType.IsValueType)
            {
                value = Activator.CreateInstance(paramType);
            }
            args[i] = value;
        }
        try
        {
            initMethod.Invoke(comp, args);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private static object ParseInitParam(string s, Type paramType)
    {
        if (string.IsNullOrEmpty(s))
        {
            if (paramType == typeof(string)) return "";
            if (paramType != null && paramType.IsEnum)
            {
                var values = Enum.GetValues(paramType);
                return values.Length > 0 ? values.GetValue(0) : Enum.ToObject(paramType, 0);
            }
            return paramType?.IsValueType == true ? Activator.CreateInstance(paramType) : null;
        }
        if (paramType != null && paramType.IsEnum)
        {
            try { return Enum.Parse(paramType, s, true); }
            catch { return Enum.GetValues(paramType).Length > 0 ? Enum.GetValues(paramType).GetValue(0) : Enum.ToObject(paramType, 0); }
        }
        if (paramType == typeof(string)) return s;
        if (paramType == typeof(int)) return int.TryParse(s, out var i) ? i : (object)0;
        if (paramType == typeof(long)) return long.TryParse(s, out var l) ? l : (object)0L;
        if (paramType == typeof(float)) return float.TryParse(s, out var f) ? f : (object)0f;
        if (paramType == typeof(double)) return double.TryParse(s, out var d) ? d : (object)0.0;
        if (paramType == typeof(bool)) return bool.TryParse(s, out var b) ? b : (object)false;
        if (paramType == typeof(uint)) return uint.TryParse(s, out var u) ? u : (object)0u;
        if (paramType == typeof(byte)) return byte.TryParse(s, out var x) ? x : (object)(byte)0;
        if (paramType == typeof(short)) return short.TryParse(s, out var sh) ? sh : (object)(short)0;
        try
        {
            foreach (var m in typeof(JsonUtility).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "FromJson" || !m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;
                var fromJson = m.MakeGenericMethod(paramType);
                return fromJson.Invoke(null, new object[] { s });
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Init 参数 JSON 反序列化失败: {ex.Message}");
        }
        return null;
    }

    #endregion

    #region Registered 已注册窗口列表

    private struct RegisteredWindowEntry
    {
        public string Path;
        public string TypeName;
        public Type Type;
    }

    private void RefreshRegisteredList()
    {
        mRegisteredList.Clear();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic)
                continue;
            try
            {
                foreach (var type in asm.GetExportedTypes())
                {
                    var attrs = type.GetCustomAttributes(typeof(UIWindowAttribute), true);
                    if (attrs == null || attrs.Length == 0) continue;
                    var attr = attrs[0] as UIWindowAttribute;
                    if (attr == null || string.IsNullOrEmpty(attr.UIPath)) continue;
                    mRegisteredList.Add(new RegisteredWindowEntry
                    {
                        Path = attr.UIPath,
                        TypeName = type.FullName,
                        Type = type
                    });
                }
            }
            catch (ReflectionTypeLoadException) { }
            catch (NotSupportedException) { }
        }
        mRegisteredList = mRegisteredList.OrderBy(x => x.Path).ToList();
    }

    private static void LocateCachedWindowInHierarchy(PopupWindow w)
    {
        if (w == null || w.gameObject == null) return;
        Selection.activeGameObject = w.gameObject;
        EditorGUIUtility.PingObject(w.gameObject);
    }

    private static void LocateRegisteredEntryScript(RegisteredWindowEntry r)
    {
        if (r.Type == null) return;
        var guids = AssetDatabase.FindAssets(r.Type.Name + " t:MonoScript");
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == r.Type)
            {
                Selection.activeObject = script;
                EditorGUIUtility.PingObject(script);
                return;
            }
        }
    }

    private static void LocateRegisteredEntryPrefab(RegisteredWindowEntry r)
    {
        if (string.IsNullOrEmpty(r.Path)) return;
        string windowName = Path.GetFileName(r.Path);
        if (string.IsNullOrEmpty(windowName)) return;
        var guids = AssetDatabase.FindAssets("t:Prefab " + windowName);
        string bestPath = null;
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path)) continue;
            string name = Path.GetFileNameWithoutExtension(path);
            if (name != windowName) continue;
            bool hasUI = path.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0;
            if (bestPath == null)
            {
                bestPath = path;
            }
            else if (hasUI && bestPath.IndexOf("UI", StringComparison.OrdinalIgnoreCase) < 0)
            {
                bestPath = path;
            }
        }
        if (bestPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(bestPath);
            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
        }
    }

    private void DrawRegisteredTab()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("搜索路径/类型", GUILayout.Width(90));
        mRegisteredSearch = EditorGUILayout.TextField(mRegisteredSearch);
        if (GUILayout.Button("刷新列表", GUILayout.Width(70)))
            RefreshRegisteredList();
        EditorGUILayout.EndHorizontal();

        mScrollRegistered = EditorGUILayout.BeginScrollView(mScrollRegistered);

        var search = mRegisteredSearch?.Trim().ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrEmpty(search)
            ? mRegisteredList
            : mRegisteredList.Where(r => (r.Path?.ToLowerInvariant().Contains(search) ?? false) ||
                                         (r.TypeName?.ToLowerInvariant().Contains(search) ?? false)).ToList();

        const int pathWidth = 200;
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("窗口路径", GUILayout.Width(pathWidth));
        EditorGUILayout.LabelField("窗口脚本", GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < filtered.Count; i++)
        {
            var r = filtered[i];
            Rect rowRect = EditorGUILayout.GetControlRect(false, 22);
            if (i % 2 == 1)
            {
                EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.03f));
            }
            float x = rowRect.x;
            Rect pathRect = new Rect(x, rowRect.y, pathWidth, rowRect.height);
            x += pathWidth + 4;
            Rect scriptRect = new Rect(x, rowRect.y, rowRect.width - pathWidth - 8, rowRect.height);
            if (GUI.Button(pathRect, r.Path ?? "", EditorStyles.label))
                LocateRegisteredEntryPrefab(r);
            if (GUI.Button(scriptRect, r.TypeName ?? "-", EditorStyles.label))
                LocateRegisteredEntryScript(r);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"共 {filtered.Count} 条（全量 {mRegisteredList.Count}）  ·  点击路径定位预制体，点击脚本定位脚本", EditorStyles.miniLabel);
    }

    #endregion
}
}
#endif
