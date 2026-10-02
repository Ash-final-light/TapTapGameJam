using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Hierarchy 组件序列化字段采集器。给 <see cref="HierarchyRefViewer"/> 提供引用扫描数据。
/// </summary>
[InitializeOnLoad]
public class HierarchyMananger
{
    /// <summary>
    /// 单个序列化字段快照。
    /// </summary>
    public struct SerializedPropertyInfo
    {
        public SerializedProperty Property { get; private set; }

        public string Name { get; private set; }

        public string TypeName { get; private set; }

        public bool IsArray { get; private set; }

        public SerializedPropertyType PropertyType { get; private set; }

        public SerializedPropertyInfo(SerializedProperty property)
        {
            Property = property;
            Name = property.name;
            TypeName = property.type;
            IsArray = property.isArray;
            PropertyType = property.propertyType;
        }
    }

    /// <summary>
    /// 单个组件及其 ObjectReference 字段。
    /// </summary>
    public struct ComponentProps
    {
        public Component Component { get; private set; }

        public int Count => mSerializedPropertyInfos.Count;

        public SerializedPropertyInfo this[int index] => mSerializedPropertyInfos[index];

        private List<SerializedPropertyInfo> mSerializedPropertyInfos;

        public ComponentProps(Component component, List<SerializedPropertyInfo> properties)
        {
            Component = component;
            mSerializedPropertyInfos = properties;
        }

        public List<SerializedPropertyInfo>.Enumerator GetEnumerator()
        {
            return mSerializedPropertyInfos.GetEnumerator();
        }
    }

    private struct HierarchyComponentInfo
    {
        public int InstanceId;
        public List<SerializedPropertyInfo> SerializedPropertyInfos;
        public SerializedObject SerializedObject;
    }

    private const string kMenuPath = "Tools/Hierarchy开关";
    private const string kSwitchKey = "HierarchyManangerSwitch";
    private const double kAutoRefreshRate = 3d;

    public static HierarchyMananger Instance { get; private set; } = new HierarchyMananger();

    public event Action UpdateEvent;

    public bool IsOn => mButton;

    private readonly HashSet<string> mIgnoreTypeNames = new HashSet<string>()
    {
        nameof(Camera),
        nameof(Canvas),
        nameof(CanvasScaler),
        nameof(GraphicRaycaster),
        nameof(MeshRenderer),
        nameof(SpriteRenderer),
        nameof(Transform),
        "Grid",
        "Tilemap",
        "TilemapRenderer",
    };

    private readonly Stack<List<SerializedPropertyInfo>> mListPool = new Stack<List<SerializedPropertyInfo>>();
    private readonly List<HierarchyComponentInfo> mHierarchyComponentInfos = new List<HierarchyComponentInfo>();
    private readonly Dictionary<Type, bool> mIgnoreTypeCache = new Dictionary<Type, bool>();
    private readonly List<Component> mGetComponentsTemp = new List<Component>();
    private readonly List<GameObject> mGetGameObjectsTemp = new List<GameObject>();

    private double mAutoRefreshTick;
    private bool mButton;

    private HierarchyMananger()
    {
        mButton = EditorPrefs.GetBool(kSwitchKey, true);
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.update += OnEditorUpdate;
        PrefabStage.prefabStageOpened += OnPrefabStageOpened;
        PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        mAutoRefreshTick = -1d;
    }

    [MenuItem(kMenuPath)]
    private static void SwitchMenu()
    {
        Instance.mButton = !EditorPrefs.GetBool(kSwitchKey, true);
        EditorPrefs.SetBool(kSwitchKey, Instance.mButton);
        if (Instance.mButton)
        {
            Instance.Refresh(true);
        }
        else
        {
            Instance.RecycleSerializedProperties(Instance.mHierarchyComponentInfos);
            Instance.FireUpdateEvent();
        }
    }

    [MenuItem(kMenuPath, true)]
    private static bool SwitchMenuBool()
    {
        Menu.SetChecked(kMenuPath, EditorPrefs.GetBool(kSwitchKey, true));
        return true;
    }

    public void Refresh(bool immediately = false)
    {
        if (immediately)
        {
            Collect();
        }
        else
        {
            mAutoRefreshTick = -1d;
        }
    }

    public IEnumerable<ComponentProps> ForeachPropertiesInHierachy()
    {
        if (!mButton)
            yield break;

        foreach (var hierarchyComponentInfo in mHierarchyComponentInfos)
        {
            if (EditorUtility.InstanceIDToObject(hierarchyComponentInfo.InstanceId) is Component component)
                yield return new ComponentProps(component, hierarchyComponentInfo.SerializedPropertyInfos);
        }
    }

    private void OnPrefabStageOpened(PrefabStage prefabStage)
    {
        mAutoRefreshTick = -1d;
    }

    private void OnPrefabStageClosing(PrefabStage prefabStage)
    {
        mAutoRefreshTick = -1d;
    }

    private void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        mAutoRefreshTick = -1d;
    }

    private void OnHierarchyChanged()
    {
        mAutoRefreshTick = -1d;
    }

    private void OnEditorUpdate()
    {
        if (!mButton)
            return;

        if (EditorApplication.timeSinceStartup < mAutoRefreshTick)
            return;

        Collect();
    }

    private void Collect()
    {
        mAutoRefreshTick = EditorApplication.timeSinceStartup + kAutoRefreshRate;

        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (EditorApplication.isPlaying && prefabStage == null)
            return;

        RecycleSerializedProperties(mHierarchyComponentInfos);

        mGetGameObjectsTemp.Clear();
        if (prefabStage != null)
        {
            mGetGameObjectsTemp.Add(prefabStage.prefabContentsRoot);
        }
        else
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                FireUpdateEvent();
                return;
            }

            scene.GetRootGameObjects(mGetGameObjectsTemp);
        }

        foreach (var gameObject in mGetGameObjectsTemp)
        {
            if (gameObject == null)
                continue;

            mGetComponentsTemp.Clear();
            gameObject.GetComponentsInChildren(true, mGetComponentsTemp);
            foreach (var component in mGetComponentsTemp)
            {
                if (component == null)
                    continue;

                if (IsIgnoreType(component.GetType()))
                    continue;

                if (!mListPool.TryPop(out var serializedPropertyInfos))
                {
                    serializedPropertyInfos = new List<SerializedPropertyInfo>();
                }

                var serializableObj = new SerializedObject(component);
                var serializedProperty = serializableObj.GetIterator();
                while (serializedProperty.NextVisible(true))
                {
                    if (serializedProperty.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    serializedPropertyInfos.Add(new SerializedPropertyInfo(serializedProperty.Copy()));
                }

                mHierarchyComponentInfos.Add(new HierarchyComponentInfo()
                {
                    InstanceId = component.GetInstanceID(),
                    SerializedPropertyInfos = serializedPropertyInfos,
                    SerializedObject = serializableObj
                });
            }
        }

        mGetGameObjectsTemp.Clear();
        mGetComponentsTemp.Clear();
        FireUpdateEvent();
    }

    private bool IsIgnoreType(Type type)
    {
        if (!mIgnoreTypeCache.TryGetValue(type, out var ignore))
        {
            ignore = mIgnoreTypeNames.Contains(type.Name);
            mIgnoreTypeCache.Add(type, ignore);
        }

        return ignore;
    }

    private void RecycleSerializedProperties(List<HierarchyComponentInfo> hierarchyComponentInfos)
    {
        foreach (var hierarchyComponentInfo in hierarchyComponentInfos)
        {
            var properties = hierarchyComponentInfo.SerializedPropertyInfos;
            for (var i = 0; i < properties.Count; i++)
            {
                properties[i].Property.Dispose();
            }

            properties.Clear();
            mListPool.Push(properties);
            hierarchyComponentInfo.SerializedObject?.Dispose();
        }

        hierarchyComponentInfos.Clear();
    }

    private void FireUpdateEvent()
    {
        try
        {
            UpdateEvent?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
