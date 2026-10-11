using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TapTap.Danmaku.Editor
{
    public sealed partial class DanmakuEditorWindow
    {
        private int inspectorPage;
        private bool emissionAdvanced, projectileAdvanced, appearanceOpen = true, attackAdvanced, keysOpen;
        private bool frames, musicOpen, musicBandsOpen;
        private float timelineRatio = 0.33f, timelineHeight, splitStartY, splitStartHeight;
        private int splitterControl;
        private Rect splitterRect;
        private string scheduleSignature;
        private int scheduleMode;
        private bool applySchedule;
        private bool applySplitSchedule;
        private string LayoutKey => "TapTap.Danmaku.TimelineRatio." + Application.dataPath;
        private float TimelineMaxHeight => Mathf.Max(120, position.height - (sequence && sequence.Music ? 352 : 330));

        private void DrawManualBpmControls()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 75;
                EditorGUI.BeginChangeCheck();
                float bpm = EditorGUILayout.FloatField(new GUIContent("手动 BPM", "每分钟拍数。输入后自动生成节拍点，无需解析音乐；范围1–1000，0关闭。手动节拍优先用于吸附。"),
                    sequence.ManualMusicBpm, GUILayout.Width(175));
                EditorGUIUtility.labelWidth = 115;
                float offset = EditorGUILayout.FloatField(new GUIContent("首拍偏移（秒）", "第一拍在音乐文件中的位置；用于对齐前奏。音乐开始偏移会额外作用于整条节拍轨道。"),
                    sequence.ManualMusicBeatOffsetSeconds, GUILayout.Width(205));
                bool changed = EditorGUI.EndChangeCheck();
                EditorGUIUtility.labelWidth = previousLabelWidth;
                bool clear = GUILayout.Button("清除节拍", EditorStyles.toolbarButton, GUILayout.Width(65));
                if (changed || clear) SetManualMusicBeatSettings(clear ? 0 : bpm, offset);
                GUILayout.Space(8);
                if (ManualMusicBeatGrid.TryCreate(sequence, out var grid))
                    GUILayout.Label($"每拍 {grid.PeriodSeconds:0.###} 秒 · {grid.Count} 个节拍点", EditorStyles.miniLabel);
                else GUILayout.Label(sequence.ManualMusicBpm <= 0 ? "输入 BPM 自动生成节拍点" : "首拍不在可见音乐范围内", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
        }
        private void SetManualMusicBeatSettings(float bpm, float offset)
        {
            if (float.IsNaN(bpm) || float.IsInfinity(bpm) || float.IsNaN(offset) || float.IsInfinity(offset)) return;
            bpm = bpm <= 0 ? 0 : Mathf.Clamp(bpm, 1, 1000); offset = Mathf.Max(0, offset);
            if (sequence.ManualMusicBpm == bpm && sequence.ManualMusicBeatOffsetSeconds == offset) return;
            Undo.RecordObject(sequence, "设置音乐 BPM 节拍");
            sequence.ManualMusicBpm = bpm; sequence.ManualMusicBeatOffsetSeconds = offset;
            EditorUtility.SetDirty(sequence); Repaint();
        }

        private void DrawTimelineSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(0, 7, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) splitterRect = rect;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(rect, rect.Contains(e.mousePosition) || GUIUtility.hotControl == control
                ? new Color(0.3f, 0.65f, 1) : new Color(0.27f, 0.29f, 0.32f));
            GUI.Label(rect, new GUIContent("", "拖动以调整时间轴高度；双击恢复默认高度。"));
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                if (e.clickCount == 2) { timelineRatio = 0.33f; EditorPrefs.SetFloat(LayoutKey, timelineRatio); }
                else { splitterControl = control; GUIUtility.hotControl = control; splitStartY = e.mousePosition.y; splitStartHeight = timelineHeight; }
                e.Use(); Repaint();
            }
            else if (GUIUtility.hotControl == splitterControl && splitterControl != 0)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float max = TimelineMaxHeight;
                    timelineRatio = Mathf.Clamp(splitStartHeight + splitStartY - e.mousePosition.y, 120, max) / Mathf.Max(1, position.height);
                    e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseUp)
                { GUIUtility.hotControl = 0; splitterControl = 0; EditorPrefs.SetFloat(LayoutKey, timelineRatio); e.Use(); }
            }
        }

        private void ShowAddPopup(Rect anchor) => PopupWindow.Show(anchor, new AddPopup(this));
        private static string PresetDisplayName(EmissionPreset preset)
            => preset.name == preset.Pattern.ToString() ? DanmakuPresetCatalog.Names[(int)preset.Pattern] : preset.name;
        private sealed class AddPopup : PopupWindowContent
        {
            private readonly DanmakuEditorWindow owner;
            private readonly EmissionPreset[] presets;
            private int page;
            private Vector2 scroll;
            private string search = "";
            private static readonly string[] Descriptions =
            { "朝指定方向发射", "发射时朝向玩家", "向指定角度范围散开", "向四周均匀扩散", "发射方向持续旋转", "从一排位置同时发射", "相邻轮次错开角度", "母弹在指定时间分裂" };
            internal AddPopup(DanmakuEditorWindow owner)
            {
                this.owner = owner;
                presets = AssetDatabase.FindAssets("t:EmissionPreset").Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<EmissionPreset>).Where(p => p).OrderBy(p => p.name).ToArray();
            }
            public override Vector2 GetWindowSize() => new(340, 380);
            public override void OnGUI(Rect rect)
            {
                page = GUILayout.Toolbar(page, new[] { "内置花样", "我的预设" });
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (page == 0)
                {
                    for (int i = 0; i < DanmakuPresetCatalog.Names.Length; i++)
                        if (GUILayout.Button(new GUIContent(DanmakuPresetCatalog.Names[i] + "  ·  " + Descriptions[i]), GUILayout.Height(30)))
                        { owner.AddBuiltin((BuiltinPattern)i, owner.currentTick); owner.inspectorPage = 0; editorWindow.Close(); GUIUtility.ExitGUI(); }
                    GUILayout.Space(8);
                    if (GUILayout.Button("添加空白片段", GUILayout.Height(26)))
                    { owner.AddBlank(); owner.inspectorPage = 0; editorWindow.Close(); GUIUtility.ExitGUI(); }
                }
                else
                {
                    search = EditorGUILayout.TextField("名称筛选", search);
                    foreach (var preset in presets)
                        if (preset && (preset.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            PresetDisplayName(preset).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                            GUILayout.Button(new GUIContent(PresetDisplayName(preset), AssetDatabase.GetAssetPath(preset)), GUILayout.Height(28)))
                        { owner.AddPreset(preset, owner.currentTick); owner.inspectorPage = 0; editorWindow.Close(); GUIUtility.ExitGUI(); }
                    if (presets.Length == 0) EditorGUILayout.HelpBox("暂无项目预设。选中片段后点击“保存为预设”。", MessageType.Info);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void AddBlank()
        {
            Undo.RecordObject(sequence, "添加空白弹幕片段");
            sequence.Clips.Add(new DanmakuClip { StartTick = Mathf.Clamp(currentTick, 0, sequence.DurationTicks - 1),
                DurationTicks = Mathf.Min(180, sequence.DurationTicks - Mathf.Clamp(currentTick, 0, sequence.DurationTicks - 1)),
                OverrideProjectile = true });
            selected = sequence.Clips.Count - 1; selectedKey = null; KeyframesChanged();
        }

        private void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(330), GUILayout.ExpandHeight(true)))
            {
                int page = GUILayout.Toolbar(inspectorPage, new[] { "片段", "攻击设置", "预览设置" });
                if (page != inspectorPage) { inspectorPage = page; inspectorScroll = Vector2.zero; }
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                DanmakuChineseGUI.ShowFrames = frames;
                serialized.Update(); EditorGUI.BeginChangeCheck();
                if (inspectorPage == 1) DrawAttackSettings();
                else if (inspectorPage == 2) DrawPreviewSettings();
                else if (selected >= 0 && selected < sequence.Clips.Count) DrawClipSettings();
                else EditorGUILayout.HelpBox("点击“＋添加弹幕”选择花样，然后在时间轴选中片段。", MessageType.Info);
                bool changed = EditorGUI.EndChangeCheck(); serialized.ApplyModifiedProperties();
                if (changed)
                { Undo.RecordObject(sequence, "约束时间轴参数"); ClampTimeline(); playing = false; EditorUtility.SetDirty(sequence); Rebuild(); }
                ApplyPendingKeyframeDeletion();
                if (applySchedule) { applySchedule = false; ApplyEmissionSchedule(); }
                if (applySplitSchedule) { applySplitSchedule = false; ApplySplitSchedule(); }
                if (inspectorPage == 0 && selected >= 0 && selected < sequence.Clips.Count)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("复制片段")) Duplicate();
                        if (GUILayout.Button("删除片段")) Delete();
                        if (GUILayout.Button(solo == selected ? "恢复全部" : "仅预览此段")) { solo = solo == selected ? -1 : selected; Rebuild(); }
                    }
                    if (GUILayout.Button("保存为预设")) SavePreset();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        private void DrawAttackSettings()
        {
            GUILayout.Label("整场攻击", EditorStyles.boldLabel);
            Property("DurationTicks", "攻击总时长"); Property("FieldSize", "场地大小"); Property("ClearAtEnd", "攻击结束清弹");
            Property("Events", "预警／清弹事件", true);
            attackAdvanced = EditorGUILayout.Foldout(attackAdvanced, "更多攻击设置", true);
            if (attackAdvanced) { Property("RandomSeed", "随机种子"); Property("MaxActiveBullets", "同屏子弹上限"); }
            if (sequence.Music)
            {
                musicSettingsOpen = EditorGUILayout.Foldout(musicSettingsOpen, "音乐高级设置", true);
                if (musicSettingsOpen)
                {
                    Property("MusicStartSeconds", "音乐开始偏移（秒）"); Property("MusicAnalysisSettings", "解析参数", true);
                    musicBandsOpen = EditorGUILayout.Toggle("展开三频段轨道", musicBandsOpen);
                    musicBandMask = EditorGUILayout.MaskField("参与吸附的频段", musicBandMask, new[] { "低频", "中频", "高频" });
                }
            }
        }
        private void DrawPreviewSettings()
        {
            GUILayout.Label("预览环境", EditorStyles.boldLabel);
            Property("PreviewPlayerPosition", "测试玩家位置"); Property("PreviewPlayerConfig", "预览玩家配置");
            EditorGUILayout.LabelField("玩家判定半径", sequence.PreviewHitRadius.ToString("0.###"));
            Property("BossSprite", "首领图片"); Property("CollisionEnabled", "测试碰撞");
            showHitboxes = EditorGUILayout.Toggle("显示判定圈", showHitboxes);
        }
        private void DrawClipSettings()
        {
            var data = sequence.Clips[selected];
            var clip = serialized.FindProperty("Clips").GetArrayElementAtIndex(selected);
            GUILayout.Label("当前弹幕片段", EditorStyles.boldLabel);
            ClipProperty(clip, "Name", "名称"); ClipProperty(clip, "StartTick", "开始时间");
            ClipProperty(clip, "DurationTicks", "片段长度"); ClipProperty(clip, "Muted", "禁用此片段");
            ClipProperty(clip, "EmitterPosition", "发射位置");
            GUILayout.Label("片段结束停止发射，已有子弹按自身寿命消失。", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6);
            ClipProperty(clip, "Preset", "发射预设");
            if (clip.FindPropertyRelative("Preset").objectReferenceValue != data.Preset)
            {
                EditorGUILayout.HelpBox("预设引用已修改，参数将在下一次刷新后显示。", MessageType.Info); return;
            }
            bool localEmission = clip.FindPropertyRelative("OverrideEmission").boolValue || !data.Preset;
            if (!localEmission && GUILayout.Button("调整此片段的发射参数"))
            {
                serialized.ApplyModifiedProperties(); Undo.RecordObject(sequence, "复制预设参数到片段");
                data.Emission = data.EffectiveEmission.Copy(); data.Pattern = data.Preset.Pattern; data.OverrideEmission = true;
                EditorUtility.SetDirty(sequence); serialized.Update(); localEmission = true;
            }
            using (new EditorGUI.DisabledScope(!localEmission))
            {
                if (localEmission)
                {
                    var pattern = clip.FindPropertyRelative("Pattern");
                    int previous = pattern.enumValueIndex; DanmakuChineseGUI.Field(pattern);
                    if (previous != pattern.enumValueIndex)
                    {
                        // Preserve timing and projectile data; initialize the newly selected emission shape.
                        var settings = DanmakuPresetCatalog.Create((BuiltinPattern)pattern.enumValueIndex);
                        var emission = clip.FindPropertyRelative("Emission");
                        emission.FindPropertyRelative("Shape").enumValueIndex = (int)settings.Shape;
                        emission.FindPropertyRelative("AimAtPlayer").boolValue = settings.AimAtPlayer;
                        emission.FindPropertyRelative("RotationDegreesPerSecond").floatValue = settings.RotationDegreesPerSecond;
                        emission.FindPropertyRelative("AlternatePhaseDegrees").floatValue = settings.AlternatePhaseDegrees;
                        emission.FindPropertyRelative("DirectionDegrees").floatValue = settings.DirectionDegrees;
                    }
                    DanmakuChineseGUI.Emission(clip.FindPropertyRelative("Emission"), (BuiltinPattern)pattern.enumValueIndex, ref emissionAdvanced);
                }
                else
                {
                    using (var preset = new SerializedObject(data.Preset))
                        DanmakuChineseGUI.Emission(preset.FindProperty("Settings"), data.Preset.Pattern, ref emissionAdvanced);
                }
            }
            if (emissionAdvanced)
            {
                ClipProperty(clip, "MoveEmitter", "移动发射器");
                if (clip.FindPropertyRelative("MoveEmitter").boolValue) ClipProperty(clip, "EmitterEndPosition", "移动结束位置");
                if (data.Preset) ClipProperty(clip, "OverrideEmission", "使用本片段发射参数");
            }
            GUILayout.Space(6); DrawEmissionSchedule(clip);
            ClipProperty(clip, "Projectile", "子弹模板");
            if (clip.FindPropertyRelative("Projectile").objectReferenceValue != data.Projectile) return;
            bool localProjectile = clip.FindPropertyRelative("OverrideProjectile").boolValue || !data.EffectiveProjectile;
            if (!localProjectile && GUILayout.Button("调整此片段的子弹参数"))
            {
                serialized.ApplyModifiedProperties(); Undo.RecordObject(sequence, "复制子弹参数到片段");
                data.ProjectileOverride = data.EffectiveProjectileSettings.Copy(); data.OverrideProjectile = true;
                EditorUtility.SetDirty(sequence); serialized.Update(); localProjectile = true;
            }
            bool split = localEmission ? clip.FindPropertyRelative("Pattern").enumValueIndex == (int)BuiltinPattern.Split : data.IsSplit;
            using (new EditorGUI.DisabledScope(!localProjectile))
            {
                if (localProjectile) DanmakuChineseGUI.Projectile(clip.FindPropertyRelative("ProjectileOverride"), split, ref appearanceOpen, ref projectileAdvanced);
                else using (var projectile = new SerializedObject(data.EffectiveProjectile))
                    DanmakuChineseGUI.Projectile(projectile.FindProperty("Settings"), split, ref appearanceOpen, ref projectileAdvanced);
            }
            if (projectileAdvanced && data.EffectiveProjectile) ClipProperty(clip, "OverrideProjectile", "使用本片段子弹参数");
            keysOpen = EditorGUILayout.Foldout(keysOpen, "逐次调整发射时间", true);
            if (keysOpen) { DrawKeyframeInspector(clip); DrawKeyframeTools(); }
            string error = data.ValidateKeyframes();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void DrawEmissionSchedule(SerializedProperty clipProperty)
        {
            var clip = sequence.Clips[selected];
            GUILayout.Label("发射时间", EditorStyles.boldLabel);
            if (clip.IsSplit)
            {
                var points = clipProperty.FindPropertyRelative("Keyframes");
                for (int i = 0; i < clip.Keyframes.Count; i++)
                    DanmakuChineseGUI.Time(points.GetArrayElementAtIndex(i).FindPropertyRelative("LocalTick"),
                        clip.Keyframes[i].Type == DanmakuKeyframeType.Emit ? "发射时间" : "分裂时间");
                if (clip.Keyframes.Count == 0) EditorGUILayout.HelpBox("这是空白片段，可在下方展开列表或右键时间轴添加发射点和分裂点。", MessageType.Info);
                using (new EditorGUI.DisabledScope(clip.DurationTicks < 2))
                    if (GUILayout.Button("设置一次发射＋一次分裂（替换已有点）")) applySplitSchedule = true;
                return;
            }
            var ticks = clip.Keyframes.Where(k => k.Type == DanmakuKeyframeType.Emit).OrderBy(k => k.LocalTick).Select(k => k.LocalTick).ToArray();
            string signature = clip.Id + ":" + string.Join(",", ticks);
            if (signature != scheduleSignature)
            {
                scheduleSignature = signature; generateStart = ticks.Length > 0 ? ticks[0] : 0;
                generateCount = Mathf.Max(1, ticks.Length); generateInterval = ticks.Length > 1 ? ticks[1] - ticks[0] : 30;
                bool regular = generateInterval > 0 && ticks.Select((t, i) => t == generateStart + i * generateInterval).All(v => v);
                scheduleMode = ticks.Length == 1 ? 0 : ticks.Length > 1 && regular ? 1 : 2;
            }
            scheduleMode = EditorGUILayout.Popup("发射方式", scheduleMode, new[] { "发射一次", "连续发射", "自定义时间" });
            if (scheduleMode == 2)
            {
                EditorGUILayout.LabelField("当前发射点", ticks.Length + " 个");
                if (ticks.Length == 0) EditorGUILayout.HelpBox("尚无发射点。选择“发射一次”或“连续发射”后点击应用。", MessageType.Info);
                return;
            }
            generateStart = DanmakuChineseGUI.TimeValue("首次发射", generateStart);
            if (scheduleMode == 1)
            {
                generateInterval = DanmakuChineseGUI.TimeValue("发射间隔", generateInterval, 1);
                generateCount = Mathf.Clamp(EditorGUILayout.IntField("发射次数", generateCount), 1, 36000);
            }
            int count = scheduleMode == 0 ? 1 : generateCount;
            int duration = clipProperty.FindPropertyRelative("DurationTicks").intValue;
            bool valid = (long)generateStart + (long)(count - 1) * generateInterval < duration;
            GUILayout.Label(scheduleMode == 0 ? "发射一次" : $"每 {generateInterval / 60f:0.###} 秒发射一次，共 {count} 次", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("应用发射时间（替换已有时间点）")) applySchedule = true;
            if (!valid) EditorGUILayout.HelpBox("最后一次发射必须早于片段结束，请减少次数或增加片段长度。", MessageType.Warning);
        }
        private void ApplyEmissionSchedule()
        {
            if (selected < 0 || selected >= sequence.Clips.Count) return;
            var clip = sequence.Clips[selected]; int count = scheduleMode == 0 ? 1 : generateCount;
            if (clip.IsSplit || scheduleMode == 2 || generateInterval < 1 || generateStart < 0 || count < 1 ||
                (long)generateStart + (long)(count - 1) * generateInterval >= clip.DurationTicks) return;
            Undo.RecordObject(sequence, "设置发射时间");
            clip.Keyframes.Clear();
            DanmakuKeyframeEditing.Generate(clip, generateStart, generateInterval, count);
            selectedKey = null; scheduleSignature = null; KeyframesChanged();
        }
        private void ApplySplitSchedule()
        {
            if (selected < 0 || selected >= sequence.Clips.Count) return;
            var clip = sequence.Clips[selected];
            if (!clip.IsSplit || clip.DurationTicks < 2) return;
            Undo.RecordObject(sequence, "设置子母弹发射时间");
            clip.Keyframes.Clear(); clip.Keyframes.Add(new DanmakuKeyframe());
            clip.Keyframes.Add(new DanmakuKeyframe { LocalTick = Mathf.Min(60, clip.DurationTicks - 1), Type = DanmakuKeyframeType.Split });
            selectedKey = null; KeyframesChanged();
        }
    }
}
