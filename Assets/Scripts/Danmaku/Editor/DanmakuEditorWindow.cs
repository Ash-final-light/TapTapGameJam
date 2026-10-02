using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace TapTap.Danmaku.Editor
{
    public sealed class DanmakuEditorWindow : EditorWindow
    {
        [SerializeField] private DanmakuSequence sequence;
        [SerializeField] private int selected, currentTick;
        [SerializeField] private bool showHitboxes = true;
        private DanmakuSimulation simulation;
        private SerializedObject serialized;
        private PreviewRenderUtility preview;
        private GameObject previewRoot;
        private DanmakuSpritePool sprites;
        private bool playing;
        private bool loop;
        private int solo = -1;
        private float speed = 1, pixelsPerTick = 1.2f;
        private double lastTime, accumulator;
        private Vector2 inspectorScroll, timelineScroll, libraryScroll;
        private int dragIndex = -1, dragControl, dragMode, originalStart, originalDuration, dragTick;
        private int presetDrag = -1;
        private Vector2 presetMouse;
        private static readonly Color[] TrackColors =
        { new(0.3f, 0.65f, 1), new(1, 0.45f, 0.35f), new(0.7f, 0.5f, 1), new(0.3f, 0.8f, 0.6f) };

        [MenuItem("Tools/Danmaku/Boss 弹幕编辑器")]
        public static void Open()
        {
            var window = GetWindow<DanmakuEditorWindow>("Boss 弹幕编辑器");
            window.minSize = new Vector2(880, 640);
            if (Selection.activeObject is DanmakuSequence asset) window.SetSequence(asset);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is DanmakuSequence asset)) return false;
            var window = GetWindow<DanmakuEditorWindow>("Boss 弹幕编辑器");
            window.SetSequence(asset); return true;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.update += UpdatePlayback;
            if (!sequence) { sequence = DanmakuSequence.CreateExample(); sequence.hideFlags = HideFlags.DontSave; }
            Rebuild();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new IMGUIContainer(DrawEditor) { style = { flexGrow = 1 } });
        }

        private void OnDisable()
        {
            playing = false;
            EditorApplication.update -= UpdatePlayback;
            Undo.undoRedoPerformed -= OnUndo;
            sprites?.Dispose(); sprites = null;
            preview?.Cleanup(); preview = null; previewRoot = null;
        }

        private void OnDestroy()
        {
            if (sequence && !AssetDatabase.Contains(sequence)) DestroyImmediate(sequence);
        }

        private void OnUndo() { Rebuild(); Repaint(); }
        private void SetSequence(DanmakuSequence asset)
        {
            if (sequence == asset) return;
            if (sequence && !AssetDatabase.Contains(sequence)) DestroyImmediate(sequence);
            sequence = asset; selected = currentTick = 0; solo = -1; playing = false;
            Rebuild();
        }

        private void Rebuild()
        {
            if (!sequence) return;
            selected = Mathf.Clamp(selected, 0, Math.Max(0, sequence.Clips.Count - 1));
            currentTick = Mathf.Clamp(currentTick, 0, Math.Max(1, sequence.DurationTicks));
            serialized = new SerializedObject(sequence);
            simulation = new DanmakuSimulation(sequence, solo);
            simulation.Seek(currentTick);
            currentTick = simulation.Tick;
            if (simulation.Fault != null) playing = false;
            accumulator = 0;
            Repaint();
        }

        private void Seek(int frame)
        {
            currentTick = Mathf.Clamp(frame, 0, Math.Max(1, sequence.DurationTicks));
            simulation.Seek(currentTick);
            currentTick = simulation.Tick;
            accumulator = 0; Repaint();
        }

        private void UpdatePlayback()
        {
            double now = EditorApplication.timeSinceStartup;
            if (!playing || simulation == null) { lastTime = now; return; }
            accumulator += Math.Max(0, now - lastTime) * speed;
            lastTime = now;
            const double step = 1.0 / DanmakuSequence.TickRate;
            int budget = 240;
            while (accumulator >= step && budget-- > 0)
            {
                accumulator -= step;
                if (simulation.Finished)
                {
                    if (loop) simulation.Reset(sequence, solo);
                    else { playing = false; break; }
                }
                simulation.Step();
                if (simulation.Fault != null) { playing = false; break; }
            }
            currentTick = simulation.Tick;
            Repaint();
        }

        private void DrawEditor()
        {
            if (!sequence || simulation == null) return;
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLibrary();
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(260), GUILayout.ExpandWidth(true))) DrawPreview();
                DrawInspector();
            }
            DrawTransport();
            DrawTimeline();
            if (simulation.Fault != null) EditorGUILayout.HelpBox(simulation.Fault, MessageType.Error);
            else EditorGUILayout.LabelField($"活跃 {simulation.Bullets.Count}  ·  峰值 {simulation.PeakBullets}  ·  总生成 {simulation.TotalSpawned}  ·  测试命中 {simulation.HitCount}", EditorStyles.miniLabel);
            HandleKeys();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var asset = (DanmakuSequence)EditorGUILayout.ObjectField(sequence, typeof(DanmakuSequence), false, GUILayout.MinWidth(180));
                if (asset && asset != sequence) SetSequence(asset);
                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    var fresh = CreateInstance<DanmakuSequence>(); fresh.hideFlags = HideFlags.DontSave;
                    SetSequence(fresh);
                }
                if (GUILayout.Button("示例", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    var example = DanmakuSequence.CreateExample(); example.hideFlags = HideFlags.DontSave;
                    SetSequence(example);
                }
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45))) Save(false);
                if (GUILayout.Button("另存为", EditorStyles.toolbarButton, GUILayout.Width(60))) Save(true);
                GUILayout.FlexibleSpace();
                showHitboxes = GUILayout.Toggle(showHitboxes, "判定圈", EditorStyles.toolbarButton, GUILayout.Width(60));
                if (GUILayout.Button("验证", EditorStyles.toolbarButton, GUILayout.Width(45))) DanmakuValidation.Run();
            }
        }

        private void Save(bool copy)
        {
            if (!AssetDatabase.Contains(sequence) || copy)
            {
                string path = EditorUtility.SaveFilePanelInProject("保存 Boss 弹幕", "BossSequence", "asset", "选择资产位置");
                if (string.IsNullOrEmpty(path)) return;
                var saved = Instantiate(sequence); saved.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(saved, path); SetSequence(saved);
            }
            EditorUtility.SetDirty(sequence); AssetDatabase.SaveAssets();
        }

        private void DrawLibrary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(170), GUILayout.Height(300)))
            {
                libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);
                GUILayout.Label("内置发射预设", EditorStyles.boldLabel);
                for (int i = 0; i < DanmakuPresetCatalog.Names.Length; i++)
                {
                    var e = Event.current;
                    // Button consumes MouseDown; capture it before drawing for drag initiation.
                    Vector2 mouse = e.mousePosition;
                    bool mouseDown = e.type == EventType.MouseDown;
                    if (GUILayout.Button(DanmakuPresetCatalog.Names[i], GUILayout.Height(25))) AddBuiltin((BuiltinPattern)i, currentTick);
                    Rect r = GUILayoutUtility.GetLastRect();
                    if (mouseDown && r.Contains(mouse))
                    { presetDrag = i; presetMouse = mouse; }
                    if (e.type == EventType.MouseDrag && presetDrag >= 0 && (e.mousePosition - presetMouse).sqrMagnitude > 12)
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.SetGenericData("DanmakuPattern", (BuiltinPattern)presetDrag);
                        DragAndDrop.StartDrag(DanmakuPresetCatalog.Names[presetDrag]);
                        presetDrag = -1; e.Use();
                    }
                }
                GUILayout.Label("点击添加／拖至时间轴", EditorStyles.miniLabel);
                GUILayout.Space(10);
                GUILayout.Label("项目发射预设", EditorStyles.boldLabel);
                var preset = (EmissionPreset)EditorGUILayout.ObjectField(null, typeof(EmissionPreset), false);
                if (preset) AddPreset(preset, currentTick);
                GUILayout.Space(10);
                if (GUILayout.Button("创建八类示例资产")) SetSequence(DanmakuEditorAssets.CreateExamples());
                EditorGUILayout.EndScrollView();
            }
        }

        private void AddBuiltin(BuiltinPattern pattern, int tick)
        {
            Undo.RecordObject(sequence, "添加弹幕片段");
            var clip = new DanmakuClip
            {
                Name = DanmakuPresetCatalog.Names[(int)pattern], StartTick = Mathf.Min(tick, sequence.DurationTicks - 1),
                DurationTicks = Mathf.Min(180, Mathf.Max(1, sequence.DurationTicks - tick)),
                Emission = DanmakuPresetCatalog.Create(pattern), OverrideProjectile = true,
                ProjectileOverride = new ProjectileSettings { Color = TrackColors[sequence.Clips.Count % TrackColors.Length] }
            };
            if (pattern == BuiltinPattern.Split)
            { clip.ProjectileOverride.SplitAgeTicks = 60; clip.ProjectileOverride.VisualSize = 0.3f; }
            sequence.Clips.Add(clip); selected = sequence.Clips.Count - 1;
            EditorUtility.SetDirty(sequence); Rebuild();
        }

        private void AddPreset(EmissionPreset preset, int tick)
        {
            Undo.RecordObject(sequence, "添加预设片段");
            sequence.Clips.Add(new DanmakuClip
            {
                Name = preset.name, StartTick = Mathf.Min(tick, sequence.DurationTicks - 1),
                DurationTicks = Mathf.Min(180, Mathf.Max(1, sequence.DurationTicks - tick)),
                Preset = preset, OverrideEmission = false, Emission = preset.Settings.Copy(),
                Projectile = preset.Projectile, OverrideProjectile = !preset.Projectile
            });
            selected = sequence.Clips.Count - 1; EditorUtility.SetDirty(sequence); Rebuild();
        }

        private void EnsurePreview()
        {
            if (preview != null) return;
            preview = new PreviewRenderUtility();
            preview.camera.orthographic = true;
            preview.camera.transform.position = new Vector3(0, 0, -10);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            preview.camera.nearClipPlane = 0.1f; preview.camera.farClipPlane = 30;
            previewRoot = new GameObject("Isolated Danmaku Preview") { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(previewRoot);
            sprites = new DanmakuSpritePool(previewRoot.transform);
        }

        private void DrawPreview()
        {
            GUILayout.Label("Sprite 战斗预览 · 固定测试玩家", EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(100, 285, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint || rect.width < 1 || rect.height < 1) return;
            EnsurePreview(); sprites.Sync(simulation, sequence);
            float halfHeight = Mathf.Max(sequence.FieldSize.y * 0.5f, sequence.FieldSize.x * 0.5f / (rect.width / rect.height));
            preview.camera.orthographicSize = Mathf.Max(0.1f, halfHeight * 1.08f);
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render();
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
            Handles.BeginGUI();
            float scale = rect.height / (preview.camera.orthographicSize * 2);
            Vector3 Pixel(Vector2 v) => new(rect.center.x + v.x * scale, rect.center.y - v.y * scale, 0);
            Handles.color = new Color(0.5f, 0.55f, 0.65f);
            Vector2 half = sequence.FieldSize * 0.5f;
            Handles.DrawPolyLine(Pixel(new(-half.x, -half.y)), Pixel(new(-half.x, half.y)),
                Pixel(new(half.x, half.y)), Pixel(new(half.x, -half.y)), Pixel(new(-half.x, -half.y)));
            Handles.color = Color.white;
            Handles.DrawWireDisc(Pixel(sequence.PlayerPosition), Vector3.forward, sequence.PlayerHitRadius * scale);
            if (showHitboxes)
                foreach (var p in simulation.Bullets)
                {
                    Handles.color = new Color(1, 1, 1, 0.65f);
                    Handles.DrawWireDisc(Pixel(DanmakuSimulation.ToVector(p.Position)), Vector3.forward, p.Settings.HitRadius * scale);
                }
            foreach (var ev in sequence.Events)
                if (ev.Type == DanmakuEventType.Warning && currentTick >= ev.Tick && currentTick < ev.Tick + ev.DurationTicks)
                    GUI.Label(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 25), "预警：" + ev.Label, EditorStyles.whiteBoldLabel);
            Handles.EndGUI();
        }

        private void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(270), GUILayout.Height(300)))
            {
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                serialized.Update();
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("攻击设置", EditorStyles.boldLabel);
                Property("DurationTicks", "总帧数（60 Hz）"); Property("RandomSeed", "随机种子");
                Property("MaxActiveBullets", "活跃弹上限"); Property("MaxSplitDepth", "最大分裂层级");
                Property("FieldSize", "场地大小"); Property("PlayerPosition", "测试玩家位置");
                Property("PlayerHitRadius", "玩家判定半径"); Property("PlayerSprite", "玩家 Sprite");
                Property("BossSprite", "Boss Sprite"); Property("CollisionEnabled", "测试碰撞");
                Property("ClearAtEnd", "攻击结束清弹");
                GUILayout.Space(8);
                if (selected >= 0 && selected < sequence.Clips.Count)
                {
                    GUILayout.Label("选中发射片段", EditorStyles.boldLabel);
                    var clip = serialized.FindProperty("Clips").GetArrayElementAtIndex(selected);
                    ClipProperty(clip, "Name", "名称"); ClipProperty(clip, "StartTick", "开始帧");
                    ClipProperty(clip, "DurationTicks", "持续帧数"); ClipProperty(clip, "Muted", "静音");
                    ClipProperty(clip, "EmitterPosition", "发射位置"); ClipProperty(clip, "MoveEmitter", "移动发射器");
                    if (sequence.Clips[selected].MoveEmitter) ClipProperty(clip, "EmitterEndPosition", "结束位置");
                    ClipProperty(clip, "Preset", "发射预设"); ClipProperty(clip, "OverrideEmission", "本片段覆盖发射参数");
                    if (sequence.Clips[selected].OverrideEmission || !sequence.Clips[selected].Preset)
                        ClipProperty(clip, "Emission", "发射参数", true);
                    ClipProperty(clip, "Projectile", "单弹模板"); ClipProperty(clip, "OverrideProjectile", "本片段覆盖单弹参数");
                    if (sequence.Clips[selected].OverrideProjectile || !sequence.Clips[selected].EffectiveProjectile)
                        ClipProperty(clip, "ProjectileOverride", "单弹参数", true);
                }
                Property("Events", "预警／清弹事件", true);
                bool changed = EditorGUI.EndChangeCheck();
                serialized.ApplyModifiedProperties();
                if (changed)
                {
                    Undo.RecordObject(sequence, "约束时间轴参数"); ClampTimeline();
                    EditorUtility.SetDirty(sequence); Rebuild();
                }
                if (selected >= 0 && selected < sequence.Clips.Count)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("复制")) Duplicate();
                        if (GUILayout.Button("删除")) Delete();
                        if (GUILayout.Button(solo == selected ? "取消独奏" : "独奏")) { solo = solo == selected ? -1 : selected; Rebuild(); }
                    }
                    if (GUILayout.Button("另存发射预设")) SavePreset();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void Property(string name, string label, bool children = false)
            => EditorGUILayout.PropertyField(serialized.FindProperty(name), new GUIContent(label), children);
        private static void ClipProperty(SerializedProperty clip, string name, string label, bool children = false)
            => EditorGUILayout.PropertyField(clip.FindPropertyRelative(name), new GUIContent(label), children);

        private void ClampTimeline()
        {
            sequence.DurationTicks = Mathf.Clamp(sequence.DurationTicks, 1, 36000);
            sequence.MaxActiveBullets = Mathf.Clamp(sequence.MaxActiveBullets, 1, 16384);
            sequence.FieldSize = new Vector2(Mathf.Max(1, sequence.FieldSize.x), Mathf.Max(1, sequence.FieldSize.y));
            foreach (var clip in sequence.Clips)
            {
                clip.StartTick = Mathf.Clamp(clip.StartTick, 0, sequence.DurationTicks - 1);
                clip.DurationTicks = Mathf.Clamp(clip.DurationTicks, 1, sequence.DurationTicks - clip.StartTick);
            }
        }

        private void SavePreset()
        {
            var clip = sequence.Clips[selected];
            string path = EditorUtility.SaveFilePanelInProject("另存发射预设", "EmissionPreset", "asset", "保存发射参数和当前单弹配置。");
            if (string.IsNullOrEmpty(path)) return;
            var preset = CreateInstance<EmissionPreset>();
            preset.Settings = clip.EffectiveEmission.Copy(); preset.Projectile = clip.EffectiveProjectile;
            AssetDatabase.CreateAsset(preset, path);
            if (clip.OverrideProjectile || !preset.Projectile)
            {
                var projectile = CreateInstance<ProjectileDefinition>();
                projectile.name = "Projectile"; projectile.Settings = clip.EffectiveProjectileSettings.Copy();
                AssetDatabase.AddObjectToAsset(projectile, preset); preset.Projectile = projectile;
                EditorUtility.SetDirty(preset);
            }
            AssetDatabase.SaveAssets();
        }

        private void Duplicate()
        {
            if (selected < 0 || selected >= sequence.Clips.Count) return;
            Undo.RecordObject(sequence, "复制弹幕片段");
            var clip = sequence.Clips[selected].Copy(); clip.Name += " 副本";
            clip.StartTick = Mathf.Min(currentTick, sequence.DurationTicks - 1);
            clip.DurationTicks = Mathf.Min(clip.DurationTicks, sequence.DurationTicks - clip.StartTick);
            sequence.Clips.Add(clip); selected = sequence.Clips.Count - 1;
            EditorUtility.SetDirty(sequence); Rebuild();
        }

        private void Delete()
        {
            if (selected < 0 || selected >= sequence.Clips.Count) return;
            Undo.RecordObject(sequence, "删除弹幕片段");
            sequence.Clips.RemoveAt(selected);
            sequence.Events.RemoveAll(ev => ev.ClipIndex == selected);
            foreach (var ev in sequence.Events)
            {
                if (ev.ClipIndex > selected) ev.ClipIndex--;
            }
            solo = -1; EditorUtility.SetDirty(sequence); Rebuild();
        }

        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("|◀", EditorStyles.toolbarButton, GUILayout.Width(35))) { playing = false; Seek(0); }
                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(30))) { playing = false; Seek(currentTick - 1); }
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton, GUILayout.Width(45))) TogglePlay();
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(30))) { playing = false; Seek(currentTick + 1); }
                int tick = EditorGUILayout.IntField(currentTick, GUILayout.Width(55));
                if (tick != currentTick) { playing = false; Seek(tick); }
                GUILayout.Label($"/ {sequence.DurationTicks} · {currentTick / 60f:F2} s", GUILayout.Width(135));
                loop = GUILayout.Toggle(loop, "循环", EditorStyles.toolbarButton, GUILayout.Width(45));
                speed = EditorGUILayout.Slider(speed, 0.1f, 2, GUILayout.Width(120)); GUILayout.Label("倍速", GUILayout.Width(32));
                GUILayout.FlexibleSpace();
                GUILayout.Label("缩放", GUILayout.Width(30));
                pixelsPerTick = GUILayout.HorizontalSlider(pixelsPerTick, 0.5f, 6, GUILayout.Width(100));
            }
        }

        private void TogglePlay()
        {
            if (!playing && simulation.Finished) Seek(0);
            playing = !playing && simulation.Fault == null;
            lastTime = EditorApplication.timeSinceStartup; accumulator = 0;
        }

        private void DrawTimeline()
        {
            const float labelWidth = 125, headerHeight = 25, rowHeight = 32;
            Rect viewport = GUILayoutUtility.GetRect(100, 205, GUILayout.ExpandWidth(true));
            float width = Mathf.Max(viewport.width - 18, labelWidth + sequence.DurationTicks * pixelsPerTick + 15);
            Rect content = new(0, 0, width, headerHeight + (sequence.Clips.Count + 1) * rowHeight + 15);
            timelineScroll = GUI.BeginScrollView(viewport, timelineScroll, content);
            EditorGUI.DrawRect(content, EditorGUIUtility.isProSkin ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.88f, 0.88f, 0.88f));
            GUI.Label(new Rect(4, 0, labelWidth, 22), "发射器／片段", EditorStyles.boldLabel);
            int rulerStep = pixelsPerTick < 1 ? 60 : pixelsPerTick < 3 ? 30 : 10;
            for (int tick = 0; tick <= sequence.DurationTicks; tick += rulerStep)
            {
                float x = labelWidth + tick * pixelsPerTick;
                EditorGUI.DrawRect(new Rect(x, 22, 1, content.height - 22), new Color(0.5f, 0.5f, 0.5f, 0.2f));
                GUI.Label(new Rect(x + 2, 0, 65, 20), $"{tick / 60f:0.#} s", EditorStyles.miniLabel);
            }
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            for (int i = 0; i < sequence.Clips.Count; i++)
            {
                var clip = sequence.Clips[i]; float y = headerHeight + i * rowHeight;
                if (GUI.Button(new Rect(3, y, labelWidth - 6, rowHeight - 2), (clip.Muted ? "[静音] " : "") + clip.Name, EditorStyles.miniButton)) selected = i;
                Rect r = new(labelWidth + clip.StartTick * pixelsPerTick, y + 3, Mathf.Max(4, clip.DurationTicks * pixelsPerTick), rowHeight - 6);
                Color color = TrackColors[i % TrackColors.Length]; color.a = clip.Muted ? 0.2f : selected == i ? 0.85f : 0.5f;
                EditorGUI.DrawRect(r, color); GUI.Label(r, clip.Name, EditorStyles.whiteLabel);
                EditorGUIUtility.AddCursorRect(new Rect(r.x, r.y, 6, r.height), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(new Rect(r.xMax - 6, r.y, 6, r.height), MouseCursor.ResizeHorizontal);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    selected = dragIndex = i; playing = false;
                    originalStart = clip.StartTick; originalDuration = clip.DurationTicks;
                    dragTick = Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick);
                    dragMode = e.mousePosition.x < r.x + 6 ? -1 : e.mousePosition.x > r.xMax - 6 ? 1 : 0;
                    dragControl = control; GUIUtility.hotControl = control;
                    Undo.RecordObject(sequence, "拖动弹幕片段"); e.Use(); Repaint();
                }
            }
            float eventY = headerHeight + sequence.Clips.Count * rowHeight;
            GUI.Label(new Rect(4, eventY, labelWidth, 25), "事件", EditorStyles.boldLabel);
            foreach (var ev in sequence.Events)
            {
                float x = labelWidth + ev.Tick * pixelsPerTick;
                Rect mark = new(x, eventY + 3, ev.Type == DanmakuEventType.Warning ? Mathf.Max(6, ev.DurationTicks * pixelsPerTick) : 7, 22);
                EditorGUI.DrawRect(mark, new Color(1, 0.75f, 0.2f, 0.6f));
                GUI.Label(new Rect(x + 3, eventY + 3, 100, 22), ev.Label, EditorStyles.miniLabel);
            }
            EditorGUI.DrawRect(new Rect(labelWidth + currentTick * pixelsPerTick, 0, 2, content.height), Color.white);
            if (dragIndex >= 0 && GUIUtility.hotControl == dragControl)
            {
                if (e.type == EventType.MouseDrag)
                {
                    var clip = sequence.Clips[dragIndex];
                    int delta = Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick) - dragTick;
                    if (dragMode == 0) clip.StartTick = Mathf.Clamp(originalStart + delta, 0, sequence.DurationTicks - originalDuration);
                    else if (dragMode < 0)
                    {
                        clip.StartTick = Mathf.Clamp(originalStart + delta, 0, originalStart + originalDuration - 1);
                        clip.DurationTicks = originalStart + originalDuration - clip.StartTick;
                    }
                    else clip.DurationTicks = Mathf.Clamp(originalDuration + delta, 1, sequence.DurationTicks - originalStart);
                    EditorUtility.SetDirty(sequence); e.Use(); Rebuild();
                }
                else if (e.type == EventType.MouseUp)
                { dragIndex = -1; GUIUtility.hotControl = 0; e.Use(); }
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && e.mousePosition.x >= labelWidth)
            { playing = false; Seek(Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick)); e.Use(); }
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && e.mousePosition.x >= labelWidth)
            {
                object pattern = DragAndDrop.GetGenericData("DanmakuPattern");
                var preset = DragAndDrop.objectReferences.OfType<EmissionPreset>().FirstOrDefault();
                if (pattern is BuiltinPattern || preset)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        int tick = Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick);
                        if (pattern is BuiltinPattern kind) AddBuiltin(kind, tick); else AddPreset(preset, tick);
                        DragAndDrop.SetGenericData("DanmakuPattern", null);
                    }
                    e.Use();
                }
            }
            GUI.EndScrollView();
        }

        private void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (e.keyCode == KeyCode.Space) { TogglePlay(); e.Use(); }
            else if (e.keyCode == KeyCode.Delete) { Delete(); e.Use(); }
            else if (e.keyCode == KeyCode.D && e.control) { Duplicate(); e.Use(); }
            else if (e.keyCode == KeyCode.LeftArrow) { playing = false; Seek(currentTick - 1); e.Use(); }
            else if (e.keyCode == KeyCode.RightArrow) { playing = false; Seek(currentTick + 1); e.Use(); }
        }
    }
}
