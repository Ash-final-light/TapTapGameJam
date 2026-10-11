using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Danmaku.Editor
{
    // Display names live in the editor; serialized names and enum values remain stable.
    internal static class DanmakuChineseGUI
    {
        internal static bool ShowFrames;
        private static readonly Dictionary<string, string> Labels = new()
        {
            { "Pattern", "弹幕花样" }, { "Settings", "参数" }, { "Emission", "发射参数" },
            { "Projectile", "子弹模板" }, { "DurationTicks", "持续时间" }, { "StartTick", "开始时间" },
            { "LocalTick", "片段内时间" }, { "Tick", "发生时间" }, { "Keyframes", "发射／分裂时间" },
            { "Type", "类型" }, { "Name", "名称" }, { "Muted", "禁用此片段" },
            { "Shape", "发射形状" }, { "Count", "每次发射颗数" }, { "DirectionDegrees", "发射方向（°）" },
            { "ArcDegrees", "扇形角度（°）" }, { "SpawnRadius", "出生偏移距离" },
            { "AimAtPlayer", "发射时瞄准玩家" }, { "RotationDegreesPerSecond", "旋转速度（°/秒）" },
            { "AlternatePhaseDegrees", "相邻轮次交错角度（°）" }, { "Speed", "弹速（单位/秒）" },
            { "SegmentStart", "线阵起点" }, { "SegmentEnd", "线阵终点" },
            { "SpawnPosition", "出生位置方式" }, { "RandomSpawnMin", "随机区域左下角" },
            { "RandomSpawnMax", "随机区域右上角" }, { "PlayerProtectionDistance", "玩家保护距离" },
            { "Sprite", "子弹图片" }, { "Color", "颜色" }, { "VisualSize", "显示大小" },
            { "HitRadius", "碰撞判定半径" }, { "Interaction", "子弹类型" }, { "EnergyOrb", "能量球膨胀设置" },
            { "ContactDamage", "母球接触造成伤害" }, { "FinalVisualSize", "膨胀后显示大小" },
            { "FinalHitRadius", "膨胀后判定半径" }, { "GrowthCurve", "膨胀变化方式" },
            { "SplitChild", "分裂子弹模板" }, { "SplitCount", "分裂子弹颗数" },
            { "SplitSpeed", "分裂子弹速度" }, { "InheritDirection", "继承母弹方向" },
            { "RandomSeed", "随机种子" }, { "MaxActiveBullets", "同屏子弹上限" },
            { "Music", "音乐" }, { "MusicAnalysis", "音乐解析结果" }, { "MusicAnalysisSettings", "音乐解析设置" },
            { "MusicStartSeconds", "音乐开始偏移（秒）" }, { "FieldSize", "场地大小" },
            { "ManualMusicBpm", "手动 BPM（0关闭）" }, { "ManualMusicBeatOffsetSeconds", "首拍偏移（秒）" },
            { "PreviewPlayerPosition", "测试玩家位置" }, { "PreviewPlayerConfig", "预览玩家配置" },
            { "BossSprite", "首领图片" }, { "CollisionEnabled", "测试碰撞" }, { "ClearAtEnd", "攻击结束清弹" },
            { "Clips", "弹幕片段" }, { "Events", "预警／清弹事件" }, { "Label", "预警文字" },
            { "ClipIndex", "目标片段编号（-1为全部）" }, { "EmitterPosition", "发射位置" },
            { "EmitterEndPosition", "移动结束位置" }, { "MoveEmitter", "移动发射器" }, { "Preset", "发射预设" },
            { "OverrideEmission", "使用本片段发射参数" }, { "OverrideProjectile", "使用本片段子弹参数" },
            { "ProjectileOverride", "本片段子弹参数" }, { "WindowSize", "频谱窗口大小" },
            { "HopSize", "频谱采样步长" }, { "ThresholdMultiplier", "候选点检测阈值" },
            { "MinimumIntervalSeconds", "候选点最小间隔（秒）" }, { "LowBand", "低频范围（赫兹）" },
            { "MidBand", "中频范围（赫兹）" }, { "HighBand", "高频范围（赫兹）" }
        };
        private static readonly Dictionary<string, string> Choices = new()
        {
            { "Point", "同点" }, { "Arc", "扇形" }, { "Ring", "圆环" }, { "Segment", "线阵" },
            { "Single", "直线发射" }, { "AimedFan", "瞄准扇形" }, { "Fan", "固定扇形" },
            { "Spiral", "旋转扫射" }, { "Wall", "横排墙弹" }, { "Petals", "交错圆环" }, { "Split", "子母弹" },
            { "Emitter", "发射器位置" }, { "RandomArea", "随机区域" }, { "Ordinary", "普通子弹" },
            { "EnergyOrb", "能量球" }, { "Linear", "匀速膨胀" }, { "SmoothStep", "平滑膨胀" },
            { "Explicit", "直接设置伤害" }, { "ConfigId", "使用伤害配置" },
            { "Emit", "发射" }, { "Warning", "预警" }, { "ClearBullets", "清除子弹" }
        };

        internal static string Label(string name) => Labels.TryGetValue(name, out var label) ? label : name;
        internal static void Time(SerializedProperty property, string label, int minimum = 0)
        {
            if (ShowFrames)
            {
                int value = EditorGUILayout.IntField(label + "（帧）", property.intValue);
                if (value != property.intValue) property.intValue = Mathf.Max(minimum, value);
            }
            else
            {
                float seconds = property.intValue / (float)DanmakuSequence.TickRate;
                float value = EditorGUILayout.FloatField(label + "（秒）", seconds);
                if (value != seconds && !float.IsNaN(value) && !float.IsInfinity(value))
                    property.intValue = Mathf.Max(minimum, Mathf.RoundToInt(Mathf.Clamp(value, 0, 600) * DanmakuSequence.TickRate));
            }
        }
        internal static int TimeValue(string label, int ticks, int minimum = 0, params GUILayoutOption[] options)
        {
            if (ShowFrames) return Mathf.Clamp(EditorGUILayout.IntField(label + "（帧）", ticks, options), minimum, 36000);
            float value = EditorGUILayout.FloatField(label + "（秒）", ticks / 60f, options);
            return float.IsNaN(value) || float.IsInfinity(value) ? ticks : Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(value, 0, 600) * 60), minimum, 36000);
        }
        internal static void Field(SerializedProperty property, string label = null)
        {
            if (property == null) return;
            label ??= Label(property.name);
            if (property.propertyType == SerializedPropertyType.Integer &&
                (property.name == "DurationTicks" || property.name == "StartTick" || property.name == "LocalTick" || property.name == "Tick"))
            { Time(property, label, property.name == "DurationTicks" ? 1 : 0); return; }
            if (property.propertyType == SerializedPropertyType.Enum)
            {
                var names = Array.ConvertAll(property.enumNames, n => Choices.TryGetValue(n, out var text) ? text : n);
                if (property.name == "Pattern") names = DanmakuPresetCatalog.Names;
                int value = EditorGUILayout.Popup(label, property.enumValueIndex, names);
                if (value != property.enumValueIndex) property.enumValueIndex = value;
                return;
            }
            if (property.propertyType != SerializedPropertyType.Generic)
            { EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), false); return; }
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label, true);
            if (!property.isExpanded) return;
            EditorGUI.indentLevel++;
            if (property.isArray)
            {
                int count = Mathf.Clamp(EditorGUILayout.IntField("数量", property.arraySize), 0, 36000);
                if (count != property.arraySize) property.arraySize = count;
                for (int i = 0; i < property.arraySize; i++) Field(property.GetArrayElementAtIndex(i), "第 " + (i + 1) + " 项");
            }
            else
            {
                var child = property.Copy(); var end = property.GetEndProperty();
                if (child.NextVisible(true))
                    do { if (SerializedProperty.EqualContents(child, end)) break; Field(child); }
                    while (child.NextVisible(false));
            }
            EditorGUI.indentLevel--;
        }
        internal static void Relative(SerializedProperty parent, string name, string label = null)
            => Field(parent.FindPropertyRelative(name), label);

        internal static void Direction(SerializedProperty value, bool aimed)
        {
            EditorGUILayout.PropertyField(value, new GUIContent(aimed ? "瞄准偏移（°）" : "发射方向（°）", "0°向右，90°向上，-90°向下；瞄准时0°表示正对玩家。"));
            if (!aimed)
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (var pair in new[] { ("↑ 上", 90f), ("↓ 下", -90f), ("← 左", 180f), ("→ 右", 0f) })
                        if (GUILayout.Button(pair.Item1, EditorStyles.miniButton)) value.floatValue = pair.Item2;
                }
        }
        internal static void Emission(SerializedProperty settings, BuiltinPattern pattern, ref bool advanced)
        {
            Relative(settings, "Count"); Relative(settings, "Speed");
            bool aimed = settings.FindPropertyRelative("AimAtPlayer").boolValue;
            Direction(settings.FindPropertyRelative("DirectionDegrees"), aimed);
            if ((EmissionShape)settings.FindPropertyRelative("Shape").enumValueIndex == EmissionShape.Arc) Relative(settings, "ArcDegrees");
            if (pattern == BuiltinPattern.Spiral) Relative(settings, "RotationDegreesPerSecond");
            if (pattern == BuiltinPattern.Petals) Relative(settings, "AlternatePhaseDegrees");
            if ((EmissionShape)settings.FindPropertyRelative("Shape").enumValueIndex == EmissionShape.Segment)
            { Relative(settings, "SegmentStart"); Relative(settings, "SegmentEnd"); }
            advanced = EditorGUILayout.Foldout(advanced, "更多发射设置", true);
            if (!advanced) return;
            Relative(settings, "Shape"); Relative(settings, "AimAtPlayer"); Relative(settings, "SpawnRadius");
            if (pattern != BuiltinPattern.Spiral) Relative(settings, "RotationDegreesPerSecond");
            if (pattern != BuiltinPattern.Petals) Relative(settings, "AlternatePhaseDegrees");
            Relative(settings, "SpawnPosition");
            if ((SpawnPositionPolicy)settings.FindPropertyRelative("SpawnPosition").enumValueIndex == SpawnPositionPolicy.RandomArea)
            { Relative(settings, "RandomSpawnMin"); Relative(settings, "RandomSpawnMax"); Relative(settings, "PlayerProtectionDistance"); }
        }
        internal static void Projectile(SerializedProperty settings, bool split, ref bool appearance, ref bool advanced)
        {
            appearance = EditorGUILayout.Foldout(appearance, "子弹外观与寿命", true);
            if (appearance)
            {
                Relative(settings, "Sprite"); Relative(settings, "Color"); Relative(settings, "VisualSize");
                Relative(settings, "HitRadius"); Relative(settings, "DurationTicks", "子弹存活时间");
            }
            if (split)
            {
                GUILayout.Label("分裂子弹", EditorStyles.boldLabel);
                Relative(settings, "SplitChild"); Relative(settings, "SplitCount"); Relative(settings, "SplitSpeed"); Relative(settings, "InheritDirection");
            }
            if (!split && settings.FindPropertyRelative("Interaction").enumValueIndex != (int)ProjectileInteraction.EnergyOrb) return;
            advanced = EditorGUILayout.Foldout(advanced, "能量球设置", true);
            if (!advanced) return;
            if (split) Relative(settings, "Interaction");
            if (settings.FindPropertyRelative("Interaction").enumValueIndex == (int)ProjectileInteraction.EnergyOrb)
                Relative(settings, "EnergyOrb");
        }
    }

    [CustomEditor(typeof(EmissionPreset))]
    internal sealed class EmissionPresetChineseInspector : UnityEditor.Editor
    {
        private bool advanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DanmakuChineseGUI.Field(serializedObject.FindProperty("Pattern"));
            DanmakuChineseGUI.Field(serializedObject.FindProperty("DurationTicks"));
            DanmakuChineseGUI.Emission(serializedObject.FindProperty("Settings"), ((EmissionPreset)target).Pattern, ref advanced);
            DanmakuChineseGUI.Field(serializedObject.FindProperty("Projectile"));
            DanmakuChineseGUI.Field(serializedObject.FindProperty("Keyframes"));
            serializedObject.ApplyModifiedProperties();
        }
    }
    [CustomEditor(typeof(ProjectileDefinition))]
    internal sealed class ProjectileChineseInspector : UnityEditor.Editor
    {
        private bool appearance = true, advanced, split;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            split = EditorGUILayout.Toggle("配置分裂子弹", split);
            DanmakuChineseGUI.Projectile(serializedObject.FindProperty("Settings"), split, ref appearance, ref advanced);
            serializedObject.ApplyModifiedProperties();
        }
    }
    [CustomEditor(typeof(DanmakuSequence))]
    internal sealed class SequenceChineseInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("打开中文弹幕编辑器")) { Selection.activeObject = target; DanmakuEditorWindow.Open(); }
            serializedObject.Update();
            var property = serializedObject.GetIterator();
            if (property.NextVisible(true))
                do { if (property.name != "m_Script") DanmakuChineseGUI.Field(property); } while (property.NextVisible(false));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
