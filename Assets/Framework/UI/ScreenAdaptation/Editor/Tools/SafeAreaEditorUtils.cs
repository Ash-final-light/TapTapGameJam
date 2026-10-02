#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using AshFramework.UI.ScreenAdaptation.Components;

namespace AshFramework.UI.ScreenAdaptation.Editor.Tools
{
public class SafeAreaEditorUtils
{
    [MenuItem(CanvasWidthAdjusterEditorHelper.kStrMenuItemPrefix + "/[扫描] 选中文件夹，嵌套SafeArea预制体", priority = 3)]
    public static void ScanSelectPrefabFolder()
    {
        string[] guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("请在 Project 面板中选中至少一个文件夹。");
            return;
        }

        List<string> folderPaths = new List<string>();
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
            {
                folderPaths.Add(path);
            }
        }

        if (folderPaths.Count == 0)
        {
            Debug.LogWarning("未选中任何有效的文件夹。");
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", folderPaths.ToArray());
        List<string> resultList = new List<string>();
        int count = 0;
        int total = prefabGuids.Length;
        int loadedCount = 0;

        try
        {
            StringBuilder prefabReport = new StringBuilder();
            for (int i = 0; i < total; i++)
            {
                prefabReport.Clear();
                string prefabGuid = prefabGuids[i];
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);

                if (EditorUtility.DisplayCancelableProgressBar("扫描预制体", $"正在处理 ({i + 1}/{total}): {prefabPath}", (float)i / total))
                {
                    Debug.Log("用户取消了扫描。");
                    break;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    continue;

                loadedCount++;

                SafeArea[] safeAreas = prefab.GetComponentsInChildren<SafeArea>(true);
                if (safeAreas != null && safeAreas.Length > 0)
                {
                    bool foundInPrefab = false;

                    prefabReport.AppendLine($"--------------------------------------------------------------------------------");
                    prefabReport.AppendLine($"预制体路径: {prefabPath}");

                    foreach (var sa in safeAreas)
                    {
                        // 检查父级是否也有 SafeArea
                        Transform parent = sa.transform.parent;
                        while (parent != null)
                        {
                            SafeArea parentSA = parent.GetComponent<SafeArea>();
                            if (parentSA != null)
                            {
                                if (ConditionSafeAreaArgs(parentSA))
                                {
                                    foundInPrefab = true;
                                    string objPath = GetGameObjectPath(sa.transform);
                                    string parentPath = GetGameObjectPath(parentSA.transform);

                                    prefabReport.AppendLine($"  [发现嵌套]");
                                    prefabReport.AppendLine($"    - 目标节点: {objPath}");
                                    prefabReport.AppendLine($"    - 上层节点: {parentPath} (属性已修改)");
                                    prefabReport.AppendLine($"      (PaddingTop:{parentSA.PaddingTop}, PaddingBottom:{parentSA.PaddingBottom}, DisableTop:{parentSA.DisableTopAdapt}, DisableBottom:{parentSA.DisableBottomAdapt})");
                                }
                            }

                            parent = parent.parent;
                        }
                    }

                    if (foundInPrefab)
                    {
                        resultList.Add(prefabReport.ToString());
                        count++;
                    }
                }

                // 每加载 100 个预制体，清理一次内存
                if (loadedCount % 100 == 0)
                {
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (resultList.Count > 0)
        {
            string reportPath = "SafeAreaScanReport.txt";
            StringBuilder finalReport = new StringBuilder();
            finalReport.AppendLine("================================================================================");
            finalReport.AppendLine("                SafeArea 嵌套扫描报告 (父级属性非默认)");
            finalReport.AppendLine($"                生成时间: {System.DateTime.Now}");
            finalReport.AppendLine($"                扫描文件夹: {string.Join(", ", folderPaths)}");
            finalReport.AppendLine($"                发现符合条件的预制体数量: {count}");
            finalReport.AppendLine("================================================================================");
            finalReport.AppendLine();

            foreach (var res in resultList)
            {
                finalReport.AppendLine(res);
            }

            File.WriteAllText(reportPath, finalReport.ToString());
            Debug.Log($"扫描完成，共发现 {count} 个符合条件的预制体。报告已保存至: {Path.GetFullPath(reportPath)}");

            EditorUtility.OpenWithDefaultApp(reportPath);
        }
        else
        {
            Debug.Log("未发现符合条件的嵌套 SafeArea 预制体。");
        }
    }

    [MenuItem(CanvasWidthAdjusterEditorHelper.kStrMenuItemPrefix + "/[扫描] 选中文件夹，带有SafeArea但是其父节点存在偏移旋转缩放", priority = 4)]
    public static void ScanSelectPrefabFolderInvalidSafeAreaPrefab()
    {
        string[] guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("请在 Project 面板中选中至少一个文件夹。");
            return;
        }

        List<string> folderPaths = new List<string>();
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
            {
                folderPaths.Add(path);
            }
        }

        if (folderPaths.Count == 0)
        {
            Debug.LogWarning("未选中任何有效的文件夹。");
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", folderPaths.ToArray());
        List<string> resultList = new List<string>();
        int count = 0;
        int total = prefabGuids.Length;
        int loadedCount = 0;

        try
        {
            StringBuilder prefabReport = new StringBuilder();
            // 非嵌套SafeArea
            HashSet<SafeArea> setNoNestSafeAreas = new();
            for (int i = 0; i < total; i++)
            {
                prefabReport.Clear();
                string prefabGuid = prefabGuids[i];
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);

                if (EditorUtility.DisplayCancelableProgressBar("扫描预制体", $"正在处理 ({i + 1}/{total}): {prefabPath}", (float)i / total))
                {
                    Debug.Log("用户取消了扫描。");
                    break;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    continue;

                loadedCount++;

                SafeArea[] safeAreas = prefab.GetComponentsInChildren<SafeArea>(true);
                setNoNestSafeAreas.Clear();
                foreach (var safeArea in safeAreas)
                {
                    if (safeArea != null)
                    {
                        setNoNestSafeAreas.Add(safeArea);
                    }
                }

                if (safeAreas != null && safeAreas.Length > 0)
                {
                    bool foundInPrefab = false;

                    prefabReport.AppendLine($"--------------------------------------------------------------------------------");
                    prefabReport.AppendLine($"预制体路径: {prefabPath}");

                    foreach (var sa in safeAreas)
                    {
                        // 检查父级是否也有 SafeArea
                        Transform parent = sa.transform.parent;
                        while (parent != null)
                        {
                            SafeArea parentSA = parent.GetComponent<SafeArea>();
                            if (parentSA != null)
                            {
                                setNoNestSafeAreas.Remove(sa);
                            }

                            parent = parent.parent;
                        }
                    }

                    foreach (var noNestSafeArea in setNoNestSafeAreas)
                    {
                        Transform parent = noNestSafeArea.transform.parent;
                        while (parent != null)
                        {
                            if (noNestSafeArea.gameObject.name == "NewPublicTopBarParent")
                            {
                                parent = parent.parent;
                                continue;
                            }

                            if (parent.GetComponent<CanvasWidthAdjuster>() != null)
                            {
                                parent = parent.parent;
                                continue;
                            }

                            var rectTrParent = parent as RectTransform;
                            // 父节点必须是全屏铺开，且没有旋转缩放等变换
                            if (rectTrParent != null)
                            {
                                if (rectTrParent.localScale != Vector3.one ||
                                    rectTrParent.localRotation != Quaternion.identity ||
                                    rectTrParent.offsetMax != Vector2.zero ||
                                    rectTrParent.offsetMin != Vector2.zero ||
                                    rectTrParent.anchorMin != Vector2.zero ||
                                    rectTrParent.anchorMax != Vector2.one
                                   )
                                {
                                    foundInPrefab = true;
                                    string objPath = GetGameObjectPath(noNestSafeArea.transform);
                                    string parentPath = GetGameObjectPath(rectTrParent);
                                    string localScaleStr = rectTrParent.localScale != Vector3.one ? $"localScale:{rectTrParent.localScale} " : "";
                                    string localRotationStr = rectTrParent.localRotation != Quaternion.identity ? $"localRotation:{rectTrParent.localRotation} " : "";
                                    string offsetMinStr = rectTrParent.offsetMin != Vector2.zero ? $"offsetMin:{rectTrParent.offsetMin} " : "";
                                    string offsetMaxStr = rectTrParent.offsetMax != Vector2.zero ? $"offsetMax:{rectTrParent.offsetMax} " : "";
                                    string anchorMinStr = rectTrParent.anchorMin != Vector2.zero ? $"anchorMin:{rectTrParent.anchorMin} " : "";
                                    string anchorMaxStr = rectTrParent.anchorMax != Vector2.one ? $"anchorMax:{rectTrParent.anchorMax} " : "";
                                    prefabReport.AppendLine($"  [非嵌套SafeArea的父节点]不符合条件: 存在非默认的锚点偏移 {localScaleStr} {localRotationStr} {offsetMaxStr} {offsetMinStr} {anchorMinStr} {anchorMaxStr}");
                                    prefabReport.AppendLine($"    - 目标节点: {objPath}");
                                    prefabReport.AppendLine($"    - 上层节点: {parentPath} (属性已修改)");
                                }
                            }
                            else
                            {
                                if (parent.localScale != Vector3.one ||
                                    parent.localRotation != Quaternion.identity)
                                {
                                    foundInPrefab = true;
                                    string objPath = GetGameObjectPath(noNestSafeArea.transform);
                                    string parentPath = GetGameObjectPath(parent);
                                    string localScaleStr = rectTrParent.localScale != Vector3.one ? $"localScale:{rectTrParent.localScale} " : "";
                                    string localRotationStr = rectTrParent.localRotation != Quaternion.identity ? $"localRotation:{rectTrParent.localRotation} " : "";
                                    prefabReport.AppendLine($"  [非嵌套SafeArea的父节点]不符合条件: 存在非默认的缩放/旋转 {localScaleStr} {localRotationStr}");
                                    prefabReport.AppendLine($"    - 目标节点: {objPath}");
                                    prefabReport.AppendLine($"    - 上层节点: {parentPath} (属性已修改)");
                                }
                            }
                            parent = parent.parent;
                        }
                    }

                    if (foundInPrefab)
                    {
                        resultList.Add(prefabReport.ToString());
                        count++;
                    }
                }

                // 每加载 100 个预制体，清理一次内存
                if (loadedCount % 100 == 0)
                {
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (resultList.Count > 0)
        {
            string reportPath = "SafeAreaScanReport.txt";
            StringBuilder finalReport = new StringBuilder();
            finalReport.AppendLine("================================================================================");
            finalReport.AppendLine("                SafeArea (父级属性非默认)");
            finalReport.AppendLine($"                生成时间: {System.DateTime.Now}");
            finalReport.AppendLine($"                扫描文件夹: {string.Join(", ", folderPaths)}");
            finalReport.AppendLine($"                发现符合条件的预制体数量: {count}");
            finalReport.AppendLine("================================================================================");
            finalReport.AppendLine();

            foreach (var res in resultList)
            {
                finalReport.AppendLine(res);
            }

            File.WriteAllText(reportPath, finalReport.ToString());
            Debug.Log($"扫描完成，共发现 {count} 个符合条件的预制体。报告已保存至: {Path.GetFullPath(reportPath)}");

            EditorUtility.OpenWithDefaultApp(reportPath);
        }
        else
        {
            Debug.Log("未发现符合条件的嵌套 SafeArea 预制体。");
        }
    }

    private static bool ConditionSafeAreaArgs(SafeArea sa)
    {
        return
            sa.PaddingTop != 0f ||
            sa.PaddingBottom != 0f ||
            sa.DisableTopAdapt ||
            sa.DisableBottomAdapt
            ;
    }

    private static string GetGameObjectPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
}
#endif
