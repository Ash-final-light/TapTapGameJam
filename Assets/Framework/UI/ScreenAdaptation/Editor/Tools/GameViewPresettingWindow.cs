#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using AshFramework.UI.ScreenAdaptation.Utilities;

namespace AshFramework.UI.ScreenAdaptation.Editor.Tools
{
public class GameViewPresettingWindow : EditorWindow
{
    [Serializable]
    public class GameViewPreset
    {
        public string Remark;
        public bool IsSimulator;
        public Rect WindowPosition;
        public int SizeIndex;
        public string ModeName;
        public string ResolutionLabel;
        public int Width;
        public int Height;
        public float Scale = 1.0f;
    }

    [Serializable]
    public class GameViewPresetsData
    {
        public List<GameViewPreset> presets = new();
    }

    #region Reflection Helpers

    private static class GameViewReflectionHelper
    {
        private static Type sGameViewType;
        private static PropertyInfo sSelectedSizeIndexProp;

        public static Type GetGameViewType()
        {
            if (sGameViewType == null)
            {
                sGameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            }

            return sGameViewType;
        }

        public static int GetSelectedSizeIndex(EditorWindow gameView)
        {
            if (sSelectedSizeIndexProp == null)
            {
                sSelectedSizeIndexProp = GetGameViewType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return (int)sSelectedSizeIndexProp.GetValue(gameView);
        }

        public static void SetSelectedSizeIndex(EditorWindow gameView, int index)
        {
            if (sSelectedSizeIndexProp == null)
            {
                sSelectedSizeIndexProp = GetGameViewType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            sSelectedSizeIndexProp.SetValue(gameView, index);
        }

        public static string GetSizeName(int index)
        {
            try
            {
                var gameViewSizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
                var instanceProp = gameViewSizesType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
                var instance = instanceProp.GetValue(null);

                var currentGroupProp = gameViewSizesType.GetProperty("currentGroup", BindingFlags.Instance | BindingFlags.Public);
                var currentGroup = currentGroupProp.GetValue(instance);

                var getGameViewSizeMethod = currentGroup.GetType().GetMethod("GetGameViewSize", new[] { typeof(int) });
                var gameViewSize = getGameViewSizeMethod.Invoke(currentGroup, new object[] { index });

                var baseTextProp = gameViewSize.GetType().GetProperty("baseText", BindingFlags.Instance | BindingFlags.Public);
                return (string)baseTextProp.GetValue(gameViewSize);
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }

        public static void GetSize(EditorWindow gameView, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (gameView == null)
            {
                return;
            }

            try
            {
                var targetSizeProp = GetGameViewType().GetProperty("targetSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (targetSizeProp != null)
                {
                    Vector2 size = (Vector2)targetSizeProp.GetValue(gameView);
                    width = (int)size.x;
                    height = (int)size.y;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static bool IsInSimulatorMode(EditorWindow gameView)
        {
            if (gameView == null)
            {
                return false;
            }

            if (gameView.titleContent != null && gameView.titleContent.text.Contains("Simulator"))
            {
                return true;
            }

            try
            {
                var lowModeProp = GetGameViewType().GetProperty("lowContextMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (lowModeProp != null && (bool)lowModeProp.GetValue(gameView))
                {
                    return true;
                }

                var showAccessoriesProp = GetGameViewType().GetProperty("showAccessories", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (showAccessoriesProp != null && (bool)showAccessoriesProp.GetValue(gameView))
                {
                    return true;
                }

                var inSimField = GetGameViewType().GetField("m_InSimulatorMode", BindingFlags.Instance | BindingFlags.NonPublic);
                if (inSimField != null && (bool)inSimField.GetValue(gameView))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return false;
        }

        public static float GetScale(EditorWindow gameView)
        {
            if (gameView == null)
            {
                return 1.0f;
            }

            try
            {
                var zoomAreaField = GetGameViewType().GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic);
                if (zoomAreaField != null)
                {
                    var zoomArea = zoomAreaField.GetValue(gameView);
                    if (zoomArea != null)
                    {
                        var scaleProp = zoomArea.GetType().GetProperty("scale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (scaleProp != null)
                        {
                            var scale = scaleProp.GetValue(zoomArea);
                            if (scale is Vector2 v2)
                            {
                                return v2.y;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return 1.0f;
        }

        public static void SetScale(EditorWindow gameView, float scale)
        {
            if (gameView == null)
            {
                return;
            }

            try
            {
                var zoomAreaField = GetGameViewType().GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic);
                if (zoomAreaField != null)
                {
                    var zoomArea = zoomAreaField.GetValue(gameView);
                    if (zoomArea != null)
                    {
                        var fiScale = zoomArea.GetType().GetField("m_Scale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (fiScale != null)
                        {
                            fiScale.SetValue(zoomArea, new Vector2(scale, scale));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    private static class SimulatorReflectionHelper
    {
        private static Type sSimulatorWindowType;
        private static FieldInfo sMMainField;
        private static PropertyInfo sDeviceIndexProp;
        private static PropertyInfo sDevicesProp;

        private const BindingFlags kNonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags kPublicInstance = BindingFlags.Public | BindingFlags.Instance;

        public static Type GetSimulatorWindowType()
        {
            if (sSimulatorWindowType == null)
            {
                sSimulatorWindowType = typeof(UnityEditor.DeviceSimulation.DeviceSimulator).Assembly.GetTypes().FirstOrDefault(t => t.FullName != null && t.FullName.Contains("UnityEditor.DeviceSimulation.SimulatorWindow"));
            }

            return sSimulatorWindowType;
        }

        private static bool TryGetMain(EditorWindow window, out object main)
        {
            main = null;
            if (window == null)
            {
                return false;
            }

            Type windowType = window.GetType();
            if (sSimulatorWindowType != null && windowType == sSimulatorWindowType)
            {
                if (sMMainField == null)
                {
                    sMMainField = sSimulatorWindowType.GetField("m_Main", kNonPublicInstance);
                }
            }
            else if (windowType.Name.Contains("GameView"))
            {
                if (sMMainField == null)
                {
                    sMMainField = windowType.GetField("m_Main", kNonPublicInstance) ??
                                  windowType.GetField("m_SimulatorState", kNonPublicInstance);
                }
            }

            if (sMMainField == null)
            {
                return false;
            }

            main = sMMainField.GetValue(window);
            return main != null;
        }

        public static int GetDeviceIndex(EditorWindow window)
        {
            if (TryGetMain(window, out object main))
            {
                if (sDeviceIndexProp == null)
                {
                    sDeviceIndexProp = main.GetType().GetProperty("deviceIndex", kPublicInstance);
                }

                return (int)sDeviceIndexProp.GetValue(main);
            }

            return 0;
        }

        public static void SetDeviceIndex(EditorWindow window, int index)
        {
            if (TryGetMain(window, out object main))
            {
                if (sDeviceIndexProp == null)
                {
                    sDeviceIndexProp = main.GetType().GetProperty("deviceIndex", kPublicInstance);
                }

                sDeviceIndexProp.SetValue(main, index);
            }
        }

        public static string GetDeviceName(EditorWindow window, int index)
        {
            if (TryGetMain(window, out object main))
            {
                if (sDevicesProp == null)
                {
                    sDevicesProp = main.GetType().GetProperty("devices", kPublicInstance);
                }

                var devices = sDevicesProp.GetValue(main) as Array;
                if (devices != null && index >= 0 && index < devices.Length)
                {
                    var device = devices.GetValue(index);
                    var displayNameProp = device.GetType().GetProperty("name", kPublicInstance);
                    return (string)displayNameProp.GetValue(device);
                }
            }

            return "Unknown Device";
        }

        public static void GetSize(EditorWindow window, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (window == null)
            {
                return;
            }

            if (TryGetMain(window, out object main))
            {
                try
                {
                    if (sDevicesProp == null)
                    {
                        sDevicesProp = main.GetType().GetProperty("devices", kPublicInstance);
                    }

                    if (sDeviceIndexProp == null)
                    {
                        sDeviceIndexProp = main.GetType().GetProperty("deviceIndex", kPublicInstance);
                    }

                    var devices = sDevicesProp.GetValue(main) as Array;
                    int index = (int)sDeviceIndexProp.GetValue(main);

                    if (devices != null && index >= 0 && index < devices.Length)
                    {
                        var device = devices.GetValue(index);
                        var fiDeviceInfo = device.GetType().GetField("deviceInfo", kPublicInstance);
                        if (fiDeviceInfo != null)
                        {
                            var deviceInfo = fiDeviceInfo.GetValue(device);
                            var screens = deviceInfo.GetType().GetField("screens", kPublicInstance).GetValue(deviceInfo) as Array;
                            var array = screens as Array;
                            var res1 = array.GetValue(0);
                            var fiW = res1.GetType().GetField("width", kPublicInstance).GetValue(res1);
                            var fiH = res1.GetType().GetField("height", kPublicInstance).GetValue(res1);
                            width = Convert.ToInt32(fiW);
                            height = Convert.ToInt32(fiH);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    #endregion

    #region 属性和字段

    private const string kFileName = "UnityGameViewPresets.json";
    private string GetFilePath() => Path.Combine(Path.GetTempPath(), kFileName);

    private GameViewPresetsData mData = new GameViewPresetsData();
    private ReorderableList mReorderableList;
    private Vector2 mScrollPos;

    private readonly Color mGameColor = new Color(0.7f, 1f, 0.7f);
    private readonly Color mSimulatorColor = new Color(0.7f, 0.7f, 1f);

    #endregion

    #region life-cycle

    [MenuItem(CanvasWidthAdjusterEditorHelper.kStrMenuItemPrefix + "/[工具] GameView视图预设", priority = 1)]
    public static void ShowWindow()
    {
        GetWindow<GameViewPresettingWindow>("GameView预设管理");
    }

    private void OnEnable()
    {
        LoadData();
        InitList();
    }

    private void OnFocus()
    {
        // 当窗口获得焦点时，从磁盘同步最新数据
        SyncDataFromFile();
    }

    private void SyncDataFromFile()
    {
        string path = GetFilePath();
        if (File.Exists(path))
        {
            try
            {
                string latestJson = File.ReadAllText(path);
                if (string.IsNullOrEmpty(latestJson))
                {
                    return;
                }

                string currentJson = JsonUtility.ToJson(mData, false);
                // 仅在磁盘内容与内存不一致时重新加载
                if (latestJson != currentJson)
                {
                    LoadData();
                    InitList();
                    Repaint();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameViewPreset] Sync failed: {e.Message}");
            }
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.7f, 1f, 0.9f);
        if (GUILayout.Button("记录当前活跃窗口配置 (Game / Simulator)", GUILayout.Height(40)))
        {
            RecordCurrentSmart();
        }

        EditorGUI.BeginDisabledGroup(mReorderableList.index < 0 || mReorderableList.index >= mData.presets.Count);
        GUI.backgroundColor = new Color(1f, 0.8f, 0.6f);
        if (GUILayout.Button("保存当前设置到选中项", GUILayout.Height(40)))
        {
            SaveToSelected();
        }

        EditorGUI.EndDisabledGroup();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        mScrollPos = EditorGUILayout.BeginScrollView(mScrollPos);
        mReorderableList.DoLayoutList();
        EditorGUILayout.EndScrollView();

        if (mData.presets.Count == 0)
        {
            EditorGUILayout.HelpBox("暂无记录。点击上方按钮记录当前窗口配置。", MessageType.Info);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("数据路径: " + GetFilePath(), EditorStyles.miniLabel);

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Load", GUILayout.Width(60)))
        {
            if (EditorUtility.DisplayDialog("确认", "是否从磁盘重新加载数据？这将清空当前内存中未保存的修改。", "确定", "取消"))
            {
                LoadData();
                InitList();
                Repaint();
            }
        }

        if (GUILayout.Button("Save", GUILayout.Width(60)))
        {
            SaveData();
            EditorUtility.DisplayDialog("提示", "数据已成功保存到磁盘临时目录。", "确定");
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
    }

    #endregion


    #region private

    private void InitList()
    {
        mReorderableList = new ReorderableList(mData.presets, typeof(GameViewPreset), true, true, false, true);

        mReorderableList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, "GameView / Simulator 预设列表", EditorStyles.boldLabel);
        };

        mReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var element = mData.presets[index];
            rect.y += 2;

            float applyWidth = 80;
            float spacing = 5;
            float contentWidth = rect.width - applyWidth - spacing;

            Rect contentRect = new Rect(rect.x, rect.y, contentWidth, rect.height - 4);
            Rect applyRect = new Rect(rect.x + contentWidth + spacing, rect.y, applyWidth, rect.height - 4);

            Color prevBgColor = GUI.backgroundColor;
            GUI.backgroundColor = element.IsSimulator ? mSimulatorColor : mGameColor;
            GUI.Box(contentRect, "", EditorStyles.helpBox);
            GUI.backgroundColor = prevBgColor;

            float typeWidth = 80;
            float resWidth = 320;
            float ratioWidth = 70;
            float screenTypeWidth = 60;
            float scaleWidth = 70;
            float remarkWidth = contentWidth - typeWidth - resWidth - ratioWidth - screenTypeWidth - scaleWidth - 30;

            float currentX = contentRect.x + 5;

            GUIStyle typeStyle = new GUIStyle(EditorStyles.boldLabel);
            typeStyle.fontSize = 12;
            typeStyle.alignment = TextAnchor.MiddleLeft;
            EditorGUI.LabelField(new Rect(currentX, contentRect.y, typeWidth, contentRect.height), element.IsSimulator ? "【模拟器】" : "【Game】", typeStyle);
            currentX += typeWidth + 2;

            GUIStyle resStyle = new GUIStyle(EditorStyles.label);
            resStyle.fontSize = 12;
            resStyle.alignment = TextAnchor.MiddleLeft;
            string resText = $"{element.ResolutionLabel} | ({element.Width}x{element.Height})";
            EditorGUI.LabelField(new Rect(currentX, contentRect.y, resWidth, contentRect.height), resText, resStyle);
            currentX += resWidth + 2;

            if (element.Width > 0 && element.Height > 0)
            {
                float ratio = (float)element.Width / element.Height;
                string ratioText = $"{ratio:F2}";

                string screenTypeText = "正常";
                Color screenTypeColor = Color.white;

                if (ratio > ScreenWidthAdapterUtils.kMaxResolutionAspectRatio)
                {
                    screenTypeText = "超宽";
                    screenTypeColor = new Color(1f, 0.7f, 0.7f); // 浅红
                }
                else if (ratio < ScreenWidthAdapterUtils.kMinResolutionAspectRatio)
                {
                    screenTypeText = "超窄";
                    screenTypeColor = new Color(0.7f, 0.7f, 1f); // 浅蓝
                }

                GUIStyle ratioStyle = new GUIStyle(EditorStyles.miniLabel);
                ratioStyle.fontSize = 11;
                ratioStyle.alignment = TextAnchor.MiddleLeft;
                EditorGUI.LabelField(new Rect(currentX, contentRect.y, ratioWidth, contentRect.height), $"比例:{ratioText}", ratioStyle);
                currentX += ratioWidth + 2;

                Color oldColor = GUI.color;
                GUI.color = screenTypeColor;
                GUIStyle stStyle = new GUIStyle(EditorStyles.boldLabel);
                stStyle.fontSize = 12;
                stStyle.alignment = TextAnchor.MiddleLeft;
                EditorGUI.LabelField(new Rect(currentX, contentRect.y, screenTypeWidth, contentRect.height), $"[{screenTypeText}]", stStyle);
                GUI.color = oldColor;
                currentX += screenTypeWidth + 2;

                if (!element.IsSimulator && Math.Abs(element.Scale - 1.0f) > 0.01f)
                {
                    GUIStyle scaleS = new GUIStyle(EditorStyles.miniLabel);
                    scaleS.normal.textColor = new Color(0.9f, 0.6f, 0.1f); // 橘黄色
                    EditorGUI.LabelField(new Rect(currentX, contentRect.y, scaleWidth, contentRect.height), $"缩放:{element.Scale:F2}x", scaleS);
                }

                currentX += scaleWidth + 2;
            }
            else
            {
                currentX += ratioWidth + screenTypeWidth + scaleWidth + 6;
            }

            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("Remark_" + index);
            GUIStyle remarkStyle = new GUIStyle(EditorStyles.textField);
            remarkStyle.fontSize = 12;
            remarkStyle.alignment = TextAnchor.MiddleLeft;
            element.Remark = EditorGUI.TextField(new Rect(currentX, contentRect.y + (contentRect.height - 22) / 2, remarkWidth, 22), element.Remark, remarkStyle);
            if (EditorGUI.EndChangeCheck())
            {
                SaveData();
            }

            GUIStyle applyButtonStyle = new GUIStyle(GUI.skin.button);
            applyButtonStyle.fontSize = 13;
            applyButtonStyle.fontStyle = FontStyle.Bold;
            if (GUI.Button(applyRect, "应用", applyButtonStyle))
            {
                ApplyPreset(element);
            }
        };

        mReorderableList.elementHeight = 45;

        mReorderableList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
        {
            SaveData();
        };

        mReorderableList.onRemoveCallback = (list) =>
        {
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            SaveData();
        };
    }


    private void RecordCurrentSmart()
    {
        EditorWindow targetWindow = EditorWindow.focusedWindow;
        Type gameViewType = GameViewReflectionHelper.GetGameViewType();
        Type simulatorType = SimulatorReflectionHelper.GetSimulatorWindowType();

        bool isValidWindow = false;
        if (targetWindow != null)
        {
            Type t = targetWindow.GetType();
            if (t == gameViewType || (simulatorType != null && t == simulatorType))
            {
                isValidWindow = true;
            }
        }

        if (!isValidWindow)
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            targetWindow = allWindows.FirstOrDefault(w => w != null && (w.GetType() == gameViewType || (simulatorType != null && w.GetType() == simulatorType)));
        }

        if (targetWindow == null)
        {
            EditorUtility.DisplayDialog("提示", "未找到活跃的 Game 或 Simulator 窗口，请先点击一下目标窗口使其获得焦点，或者确保窗口已打开。", "确定");
            return;
        }

        bool isSimulator = false;
        if (simulatorType != null && targetWindow.GetType() == simulatorType)
        {
            isSimulator = true;
        }
        else if (targetWindow.GetType() == gameViewType)
        {
            isSimulator = GameViewReflectionHelper.IsInSimulatorMode(targetWindow);
        }

        GameViewPreset preset = new GameViewPreset();
        preset.IsSimulator = isSimulator;
        preset.WindowPosition = targetWindow.position;

        if (isSimulator)
        {
            preset.SizeIndex = SimulatorReflectionHelper.GetDeviceIndex(targetWindow);
            preset.ResolutionLabel = SimulatorReflectionHelper.GetDeviceName(targetWindow, preset.SizeIndex);
            SimulatorReflectionHelper.GetSize(targetWindow, out preset.Width, out preset.Height);
            preset.Scale = 1.0f;
        }
        else
        {
            preset.SizeIndex = GameViewReflectionHelper.GetSelectedSizeIndex(targetWindow);
            preset.ResolutionLabel = GameViewReflectionHelper.GetSizeName(preset.SizeIndex);
            GameViewReflectionHelper.GetSize(targetWindow, out preset.Width, out preset.Height);
            preset.Scale = GameViewReflectionHelper.GetScale(targetWindow);
        }

        preset.Remark = (isSimulator ? "Sim: " : "Game: ") + DateTime.Now.ToString("HH:mm:ss");
        mData.presets.Add(preset);
        SaveData();
        Repaint();
    }

    private void RecordCurrent(bool isSimulator)
    {
        RecordCurrentSmart();
    }

    private void SaveToSelected()
    {
        if (mReorderableList.index < 0 || mReorderableList.index >= mData.presets.Count)
        {
            return;
        }

        EditorWindow targetWindow = EditorWindow.focusedWindow;
        Type gameViewType = GameViewReflectionHelper.GetGameViewType();
        Type simulatorType = SimulatorReflectionHelper.GetSimulatorWindowType();

        bool isValidWindow = false;
        if (targetWindow != null)
        {
            Type t = targetWindow.GetType();
            if (t == gameViewType || (simulatorType != null && t == simulatorType))
            {
                isValidWindow = true;
            }
        }

        if (!isValidWindow)
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            targetWindow = allWindows.FirstOrDefault(w => w != null && (w.GetType() == gameViewType || (simulatorType != null && w.GetType() == simulatorType)));
        }

        if (targetWindow == null)
        {
            EditorUtility.DisplayDialog("提示", "未找到活跃的 Game 或 Simulator 窗口，请先点击一下目标窗口使其获得焦点，或者确保窗口已打开。", "确定");
            return;
        }

        bool isSimulator = false;
        if (simulatorType != null && targetWindow.GetType() == simulatorType)
        {
            isSimulator = true;
        }
        else if (targetWindow.GetType() == gameViewType)
        {
            isSimulator = GameViewReflectionHelper.IsInSimulatorMode(targetWindow);
        }

        var preset = mData.presets[mReorderableList.index];
        preset.IsSimulator = isSimulator;
        preset.WindowPosition = targetWindow.position;

        if (isSimulator)
        {
            preset.SizeIndex = SimulatorReflectionHelper.GetDeviceIndex(targetWindow);
            preset.ResolutionLabel = SimulatorReflectionHelper.GetDeviceName(targetWindow, preset.SizeIndex);
            SimulatorReflectionHelper.GetSize(targetWindow, out preset.Width, out preset.Height);
            preset.Scale = 1.0f;
        }
        else
        {
            preset.SizeIndex = GameViewReflectionHelper.GetSelectedSizeIndex(targetWindow);
            preset.ResolutionLabel = GameViewReflectionHelper.GetSizeName(preset.SizeIndex);
            GameViewReflectionHelper.GetSize(targetWindow, out preset.Width, out preset.Height);
            preset.Scale = GameViewReflectionHelper.GetScale(targetWindow);
        }

        SaveData();
        Repaint();
    }

    private void ApplyPreset(GameViewPreset preset)
    {
        Type gameViewType = GameViewReflectionHelper.GetGameViewType();
        Type simulatorType = SimulatorReflectionHelper.GetSimulatorWindowType();

        var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(w => w != null && (w.GetType() == gameViewType || (simulatorType != null && w.GetType() == simulatorType)))
            .ToList();

        // 记录旧窗口的位置作为参考（优先取当前焦点窗口，否则取第一个有效窗口）
        Rect? lastPosition = null;
        if (EditorWindow.focusedWindow != null && allWindows.Contains(EditorWindow.focusedWindow))
        {
            lastPosition = EditorWindow.focusedWindow.position;
        }
        else if (allWindows.Count > 0)
        {
            lastPosition = allWindows[0].position;
        }

        EditorWindow targetWindow = null;

        foreach (var window in allWindows)
        {
            bool isCurrentlySim = false;
            if (simulatorType != null && window.GetType() == simulatorType)
            {
                isCurrentlySim = true;
            }
            else if (window.GetType() == gameViewType)
            {
                isCurrentlySim = GameViewReflectionHelper.IsInSimulatorMode(window);
            }

            if (isCurrentlySim == preset.IsSimulator && targetWindow == null)
            {
                targetWindow = window;
            }
            else
            {
                // 模式不匹配或是多余的匹配窗口，直接关闭
                window.Close();
            }
        }

        if (targetWindow == null)
        {
            Type openType = (preset.IsSimulator && simulatorType != null) ? simulatorType : gameViewType;
            if (openType != null)
            {
                targetWindow = GetWindow(openType);
            }
        }

        if (targetWindow == null)
        {
            return;
        }

        // 4. 再次确认新窗口的模式（针对 GetWindow 复用旧状态的情况）
        if (targetWindow.GetType() == gameViewType)
        {
            bool isCurrentlySim = GameViewReflectionHelper.IsInSimulatorMode(targetWindow);
            if (preset.IsSimulator != isCurrentlySim)
            {
                string targetMode = preset.IsSimulator ? "Simulator" : "Game";
                EditorUtility.DisplayDialog("提示", $"当前窗口模式 ({(isCurrentlySim ? "Simulator" : "Game")}) 与预设模式 ({targetMode}) 不匹配。请手动在窗口顶部切换模式后再试。", "确定");
                targetWindow.Focus();
                return;
            }
        }

        // 5. 应用设置：位置沿用旧位置 (x, y)，大小沿用预设 (width, height)
        // 即使重新打开了窗口，lastPosition 也会将位置继承过来
        Rect finalPos = lastPosition ?? targetWindow.position;
        targetWindow.position = new Rect(finalPos.x, finalPos.y, preset.WindowPosition.width, preset.WindowPosition.height);

        if (preset.IsSimulator)
        {
            SimulatorReflectionHelper.SetDeviceIndex(targetWindow, preset.SizeIndex);
        }
        else
        {
            GameViewReflectionHelper.SetSelectedSizeIndex(targetWindow, preset.SizeIndex);
            GameViewReflectionHelper.SetScale(targetWindow, preset.Scale);
            float targetScale = preset.Scale;
            EditorWindow windowRef = targetWindow;

            ExecuteOnceNextUpdate(() =>
            {
                if (windowRef == null)
                    return;

                GameViewReflectionHelper.SetScale(windowRef, targetScale);
                windowRef.Repaint();
            }, 1);
        }

        targetWindow.Show();
        targetWindow.Repaint();

        ExecuteOnceNextUpdate(() =>
        {
            CanvasWidthAdjusterEditorHelper.ForceAdaptAllScreenAdaptComponnents();
        }, 2);
    }

    /// <summary>
    /// 延迟指定帧数后执行一次回调
    /// </summary>
    private void ExecuteOnceNextUpdate(EditorApplication.CallbackFunction action, int delayFrames = 1)
    {
        if (delayFrames <= 0)
        {
            action?.Invoke();
            return;
        }

        int frameCount = 0;
        EditorApplication.CallbackFunction handler = null;
        handler = () =>
        {
            frameCount++;
            if (frameCount >= delayFrames)
            {
                EditorApplication.update -= handler;
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        };
        EditorApplication.update += handler;
    }

    private void SaveData()
    {
        try
        {
            string path = GetFilePath();
            string json = JsonUtility.ToJson(mData, false);
            File.WriteAllText(path, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameViewPreset] Failed to save data to {GetFilePath()}: {e.Message}");
            Debug.LogException(e);
        }
    }

    private void LoadData()
    {
        string path = GetFilePath();
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrEmpty(json))
                {
                    mData = JsonUtility.FromJson<GameViewPresetsData>(json);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameViewPreset] Failed to load data from {path}: {e.Message}");
                Debug.LogException(e);
                mData = new GameViewPresetsData();
            }
        }

        if (mData == null)
        {
            mData = new GameViewPresetsData();
        }
    }

    #endregion
}
}
#endif
