using System;
using System.Collections.Generic;
using System.Linq;
using Framework.FixedPoint;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace TapTap.Danmaku.Editor
{
    public sealed partial class DanmakuEditorWindow : EditorWindow
    {
        [SerializeField] private DanmakuSequence sequence;
        [SerializeField] private int selected, currentTick;
        [SerializeField] private bool showHitboxes = true;
        private DanmakuSimulation simulation;
        private SerializedObject serialized;
        private PreviewRenderUtility preview;
        private GameObject previewRoot;
        private DanmakuSpritePool sprites;
        private MusicTimelineTransport music;
        private bool musicSettingsOpen, snapMusic;
        private int musicBandMask = 7;
        private bool playing;
        private bool loop;
        private int solo = -1;
        private float speed = 1, pixelsPerTick = 1.2f;
        private double lastTime, accumulator;
        private Vector2 inspectorScroll, timelineScroll;
        private int dragIndex = -1, dragControl, dragMode, originalStart, originalDuration, dragTick;
        private static readonly Color[] TrackColors =
        { new(0.3f, 0.65f, 1), new(1, 0.45f, 0.35f), new(0.7f, 0.5f, 1), new(0.3f, 0.8f, 0.6f) };

        [MenuItem("工具/弹幕/弹幕编辑器")]
        public static void Open()
        {
            var window = GetWindow<DanmakuEditorWindow>("弹幕编辑器");
            window.minSize = new Vector2(880, 640);
            if (Selection.activeObject is DanmakuSequence asset) window.SetSequence(asset);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is DanmakuSequence asset)) return false;
            var window = GetWindow<DanmakuEditorWindow>("弹幕编辑器");
            window.SetSequence(asset); return true;
        }

        private void OnEnable()
        {
            timelineRatio = EditorPrefs.GetFloat(LayoutKey, 0.33f);
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.update += UpdatePlayback;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
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
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= OnUndo;
            MusicAnalysisJob.Cancel(); music?.Dispose(); music = null;
            sprites?.Dispose(); sprites = null;
            preview?.Cleanup(); preview = null; previewRoot = null;
        }

        private void OnDestroy()
        {
            if (sequence && !AssetDatabase.Contains(sequence)) DestroyImmediate(sequence);
        }

        private void OnUndo() { Rebuild(); Repaint(); }
        internal void RefreshSequence() => Rebuild();
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            playing = false; music?.Pause(); MusicAnalysisJob.Cancel();
        }
        private void SetSequence(DanmakuSequence asset)
        {
            if (sequence == asset) return;
            if (sequence && !AssetDatabase.Contains(sequence)) DestroyImmediate(sequence);
            sequence = asset; selected = currentTick = 0; selectedKey = null; solo = -1; playing = false;
            Rebuild();
        }

        private void Rebuild()
        {
            if (!sequence) return;
            selected = Mathf.Clamp(selected, 0, Math.Max(0, sequence.Clips.Count - 1));
            currentTick = Mathf.Clamp(currentTick, 0, Math.Max(1, sequence.DurationTicks));
            music?.Dispose(); music = null;
            if (sequence.Music && EditorMusicBackend.Available)
            {
                music = new MusicTimelineTransport(sequence.Music, sequence.MusicStartSeconds, new EditorMusicBackend());
                music.Seek(currentTick / 60.0);
            }
            serialized = new SerializedObject(sequence);
            simulation = new DanmakuSimulation(sequence, solo);
            simulation.Seek(currentTick);
            currentTick = simulation.Tick;
            if (simulation.Fault != null) playing = false;
            if (playing) music?.Resume();
            accumulator = 0;
            Repaint();
        }

        private void Seek(int frame)
        {
            currentTick = Mathf.Clamp(frame, 0, Math.Max(1, sequence.DurationTicks));
            simulation.Seek(currentTick);
            currentTick = simulation.Tick;
            music?.Seek(currentTick / 60.0);
            accumulator = 0; Repaint();
        }

        private void UpdatePlayback()
        {
            if (sequence && simulation != null && simulation.PlayerState.HitRadius !=
                FP64.FromFloat(Mathf.Max(0.001f, sequence.PreviewHitRadius))) Rebuild();
            double now = EditorApplication.timeSinceStartup;
            if (!playing || simulation == null) { music?.Pause(); lastTime = now; return; }
            double elapsed = Math.Max(0, now - lastTime); lastTime = now;
            if (music != null) music.Advance(elapsed); else accumulator += elapsed * speed;
            int budget = 240;
            while (budget-- > 0 && (music != null ? simulation.Tick < music.TargetTick : accumulator >= 1.0 / 60))
            {
                if (music == null) accumulator -= 1.0 / 60;
                if (simulation.Finished)
                {
                    if (loop) { simulation.Reset(sequence, solo); music?.Seek(0); music?.Resume(); break; }
                    else { playing = false; music?.Pause(); break; }
                }
                simulation.Step();
                if (simulation.Fault != null) { playing = false; music?.Pause(); break; }
            }
            if (music != null && simulation.Tick < music.TargetTick && playing)
            { playing = false; music.Seek(simulation.Tick / 60.0); Debug.LogWarning("预览补步超过预算，已暂停并将音乐定位到当前逻辑帧。"); }
            currentTick = simulation.Tick; Repaint();
        }

        private void DrawEditor()
        {
            if (!sequence || simulation == null) return;
            DrawToolbar();
            DrawMusicControls();
            timelineHeight = Mathf.Clamp(position.height * timelineRatio, 120, TimelineMaxHeight);
            float topHeight = Mathf.Max(200, position.height - timelineHeight - (sequence.Music ? 138 : musicOpen ? 116 : 94));
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(topHeight)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(260), GUILayout.ExpandWidth(true), GUILayout.Height(topHeight))) DrawPreview();
                DrawInspector();
            }
            DrawTimelineSplitter();
            DrawTransport();
            DrawTimeline();
            if (simulation.Fault != null) EditorGUILayout.HelpBox(simulation.Fault, MessageType.Error);
            else EditorGUILayout.LabelField($"活跃 {simulation.Bullets.Count}  ·  峰值 {simulation.PeakBullets}  ·  总生成 {simulation.TotalSpawned}  ·  测试命中 {simulation.HitCount}", EditorStyles.miniLabel);
            HandleKeys();
        }

        private int SnapTick(int tick) => snapMusic ? MusicTimelineDrawer.Snap(sequence, tick, pixelsPerTick, musicBandMask) : tick;
        private void DrawMusicControls()
        {
            if (!sequence.Music && !musicOpen)
            {
                return;
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(MusicAnalysisJob.Running))
                {
                    var clip = (AudioClip)EditorGUILayout.ObjectField("音乐", sequence.Music, typeof(AudioClip), false);
                    if (clip != sequence.Music)
                    { playing = false; Undo.RecordObject(sequence, "设置音乐"); sequence.Music = clip;
                        sequence.ManualMusicBpm = 0; sequence.ManualMusicBeatOffsetSeconds = 0; EditorUtility.SetDirty(sequence); Rebuild(); }
                }
                using (new EditorGUI.DisabledScope(!sequence.Music || !AssetDatabase.Contains(sequence) || MusicAnalysisJob.Running))
                    if (GUILayout.Button("解析音乐", EditorStyles.toolbarButton, GUILayout.Width(65)))
                    { playing = false; music?.Pause(); try { MusicAnalysisJob.Start(sequence, Rebuild); } catch (Exception e) { Debug.LogException(e); } }
                if (MusicAnalysisJob.Running && GUILayout.Button("取消", EditorStyles.toolbarButton, GUILayout.Width(40))) MusicAnalysisJob.Cancel();
                using (new EditorGUI.DisabledScope(!sequence.Music))
                {
                    if (GUILayout.Button(playing ? "暂停试听" : "同步试听", EditorStyles.toolbarButton, GUILayout.Width(65))) TogglePlay();
                    snapMusic = GUILayout.Toggle(snapMusic, "吸附节拍", EditorStyles.toolbarButton, GUILayout.Width(65));
                }
                if (sequence.Music && !AssetDatabase.Contains(sequence)) GUILayout.Label("请先保存再解析", EditorStyles.miniLabel);
                else if (sequence.Music && !MusicAnalysisJob.IsCurrent(sequence)) GUILayout.Label("音乐待解析", EditorStyles.miniLabel);
                if (sequence.Music && !EditorMusicBackend.Available) GUILayout.Label("当前编辑器无法试听音乐", EditorStyles.miniLabel);
                if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(40))) { inspectorPage = 1; musicSettingsOpen = true; }
                if (!sequence.Music && GUILayout.Button("收起", EditorStyles.toolbarButton, GUILayout.Width(40))) musicOpen = false;
            }
            if (sequence.Music) DrawManualBpmControls();
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
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45))) Save(false);
                if (GUILayout.Button("＋添加弹幕", EditorStyles.toolbarButton, GUILayout.Width(85))) ShowAddPopup(GUILayoutUtility.GetLastRect());
                if (!sequence.Music && GUILayout.Button("＋添加音乐", EditorStyles.toolbarButton, GUILayout.Width(85))) musicOpen = !musicOpen;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("攻击设置", EditorStyles.toolbarButton, GUILayout.Width(65))) inspectorPage = 1;
                showHitboxes = GUILayout.Toggle(showHitboxes, "判定圈", EditorStyles.toolbarButton, GUILayout.Width(60));
                if (GUILayout.Button("更多", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("另存为"), false, () => Save(true));
                    menu.AddItem(new GUIContent("打开内存示例"), false, () => { var example = DanmakuSequence.CreateExample(); example.hideFlags = HideFlags.DontSave; SetSequence(example); });
                    menu.AddItem(new GUIContent("创建八类示例资产"), false, () => SetSequence(DanmakuEditorAssets.CreateExamples()));
                    menu.AddItem(new GUIContent("运行模拟验证"), false, DanmakuValidation.Run);
                    menu.ShowAsContext();
                }
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

        private void AddBuiltin(BuiltinPattern pattern, int tick)
        {
            tick = SnapTick(tick);
            Undo.RecordObject(sequence, "添加弹幕片段");
            if (pattern == BuiltinPattern.Split && sequence.DurationTicks < 2) sequence.DurationTicks = 2;
            tick = Mathf.Clamp(tick, 0, sequence.DurationTicks - (pattern == BuiltinPattern.Split ? 2 : 1));
            var clip = new DanmakuClip
            {
                Name = DanmakuPresetCatalog.Names[(int)pattern], Pattern = pattern, StartTick = Mathf.Clamp(tick, 0, sequence.DurationTicks - 1),
                DurationTicks = Mathf.Min(180, Mathf.Max(1, sequence.DurationTicks - tick)),
                Emission = DanmakuPresetCatalog.Create(pattern), OverrideProjectile = true,
                ProjectileOverride = new ProjectileSettings { Color = TrackColors[sequence.Clips.Count % TrackColors.Length] }
            };
            if (pattern == BuiltinPattern.Split)
            {
                clip.ProjectileOverride.VisualSize = 0.3f;
                clip.Keyframes.Add(new DanmakuKeyframe());
                clip.Keyframes.Add(new DanmakuKeyframe { LocalTick = Mathf.Min(60, clip.DurationTicks - 1), Type = DanmakuKeyframeType.Split });
            }
            else DanmakuKeyframeEditing.Generate(clip, 0, 30, (clip.DurationTicks - 1) / 30 + 1);
            sequence.Clips.Add(clip); selected = sequence.Clips.Count - 1; selectedKey = null; inspectorPage = 0;
            EditorUtility.SetDirty(sequence); Rebuild();
        }

        private void AddPreset(EmissionPreset preset, int tick)
        {
            tick = SnapTick(tick);
            Undo.RecordObject(sequence, "添加预设片段");
            sequence.Clips.Add(new DanmakuClip
            {
                Name = preset.name, Pattern = preset.Pattern, StartTick = Mathf.Clamp(tick, 0, sequence.DurationTicks - 1),
                DurationTicks = Mathf.Min(preset.DurationTicks, Mathf.Max(1, sequence.DurationTicks - tick)),
                Preset = preset, OverrideEmission = false, Emission = preset.Settings.Copy(),
                Projectile = preset.Projectile, OverrideProjectile = !preset.Projectile,
                Keyframes = preset.Keyframes.ConvertAll(k => k.Copy())
            });
            selected = sequence.Clips.Count - 1; selectedKey = null; EditorUtility.SetDirty(sequence); Rebuild();
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
            GUILayout.Label("战斗预览", EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
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
            Handles.DrawWireDisc(Pixel(DanmakuSimulation.ToVector(simulation.PlayerState.Position)), Vector3.forward,
                simulation.PlayerState.HitRadius.ToFloat() * scale);
            if (showHitboxes)
                foreach (var p in simulation.Bullets)
                {
                    Handles.color = new Color(1, 1, 1, 0.65f);
                    Handles.DrawWireDisc(Pixel(DanmakuSimulation.ToVector(p.Position)), Vector3.forward, p.HitRadius.ToFloat() * scale);
                }
            foreach (var ev in sequence.Events)
                if (ev.Type == DanmakuEventType.Warning && currentTick >= ev.Tick && currentTick < ev.Tick + ev.DurationTicks)
                    GUI.Label(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 25), "预警：" + ev.Label, EditorStyles.whiteBoldLabel);
            Handles.EndGUI();
        }

        private void Property(string name, string label, bool children = false)
            => DanmakuChineseGUI.Field(serialized.FindProperty(name), label);
        private static void ClipProperty(SerializedProperty clip, string name, string label, bool children = false)
            => DanmakuChineseGUI.Field(clip.FindPropertyRelative(name), label);

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
            preset.Pattern = clip.IsSplit ? BuiltinPattern.Split : clip.Preset && !clip.OverrideEmission ? clip.Preset.Pattern : clip.Pattern;
            preset.DurationTicks = clip.DurationTicks; preset.Keyframes = clip.Keyframes.ConvertAll(k => k.Copy());
            AssetDatabase.CreateAsset(preset, path);
            if (clip.OverrideProjectile || !preset.Projectile)
            {
                var projectile = CreateInstance<ProjectileDefinition>();
                projectile.name = "子弹模板"; projectile.Settings = clip.EffectiveProjectileSettings.Copy();
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
            sequence.Clips.Add(clip); selected = sequence.Clips.Count - 1; selectedKey = null; inspectorPage = 0;
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
            solo = -1; selectedKey = null; EditorUtility.SetDirty(sequence); Rebuild();
        }

        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("|◀", EditorStyles.toolbarButton, GUILayout.Width(35))) { playing = false; Seek(0); }
                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(30))) { playing = false; Seek(currentTick - 1); }
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton, GUILayout.Width(45))) TogglePlay();
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(30))) { playing = false; Seek(currentTick + 1); }
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 62;
                int tick = DanmakuChineseGUI.TimeValue("时间", currentTick, 0, GUILayout.Width(115));
                EditorGUIUtility.labelWidth = previousLabelWidth;
                if (tick != currentTick) { playing = false; Seek(tick); }
                GUILayout.Label(frames ? $"/ {sequence.DurationTicks} 帧" : $"/ {sequence.DurationTicks / 60f:0.##} 秒", GUILayout.Width(80));
                frames = GUILayout.Toggle(frames, "显示帧", EditorStyles.toolbarButton, GUILayout.Width(55));
                DanmakuChineseGUI.ShowFrames = frames;
                loop = GUILayout.Toggle(loop, "循环", EditorStyles.toolbarButton, GUILayout.Width(45));
                using (new EditorGUI.DisabledScope(sequence.Music)) speed = EditorGUILayout.Slider(sequence.Music ? 1 : speed, 0.1f, 2, GUILayout.Width(120));
                GUILayout.Label(sequence.Music ? "音乐原速" : "倍速", GUILayout.Width(48));
                GUILayout.FlexibleSpace();
                GUILayout.Label("缩放", GUILayout.Width(30));
                pixelsPerTick = GUILayout.HorizontalSlider(pixelsPerTick, 0.5f, 6, GUILayout.Width(100));
            }
        }

        private void TogglePlay()
        {
            if (!playing && simulation.Finished) Seek(0);
            if (sequence.Music && !EditorMusicBackend.Available) { playing = false; return; }
            playing = !playing && simulation.Fault == null;
            if (playing) music?.Resume(); else music?.Pause();
            lastTime = EditorApplication.timeSinceStartup; accumulator = 0;
        }

        private void DrawTimeline()
        {
            const float labelWidth = 125, headerHeight = 25, rowHeight = 32;
            Rect viewport = GUILayoutUtility.GetRect(100, timelineHeight, GUILayout.ExpandWidth(true));
            bool insideTimeline = viewport.Contains(Event.current.mousePosition);
            float musicRows = sequence.Music ? ((musicBandsOpen ? 4 : 1) +
                (ManualMusicBeatGrid.TryCreate(sequence, out _) ? 1 : 0)) * rowHeight : 0;
            float maxTick = sequence.Music ? Mathf.Max(sequence.DurationTicks, (sequence.Music.length + sequence.MusicStartSeconds) * 60) : sequence.DurationTicks;
            float width = Mathf.Max(viewport.width - 18, labelWidth + maxTick * pixelsPerTick + 15);
            Rect content = new(0, 0, width, headerHeight + musicRows + (sequence.Clips.Count + 1) * rowHeight + 15);
            timelineScroll = GUI.BeginScrollView(viewport, timelineScroll, content);
            EditorGUI.DrawRect(content, EditorGUIUtility.isProSkin ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.88f, 0.88f, 0.88f));
            GUI.Label(new Rect(4, 0, labelWidth, 22), "发射器／片段", EditorStyles.boldLabel);
            if (sequence.Music) MusicTimelineDrawer.Draw(sequence, headerHeight, labelWidth, pixelsPerTick, rowHeight,
                Mathf.Max(labelWidth, timelineScroll.x), timelineScroll.x + viewport.width, musicBandMask, musicBandsOpen);
            int rulerStep = pixelsPerTick < 1 ? 60 : pixelsPerTick < 3 ? 30 : 10;
            for (int tick = 0; tick <= maxTick; tick += rulerStep)
            {
                float x = labelWidth + tick * pixelsPerTick;
                EditorGUI.DrawRect(new Rect(x, 22, 1, content.height - 22), new Color(0.5f, 0.5f, 0.5f, 0.2f));
                GUI.Label(new Rect(x + 2, 0, 65, 20), frames ? $"{tick} 帧" : $"{tick / 60f:0.#} 秒", EditorStyles.miniLabel);
            }
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            for (int i = 0; i < sequence.Clips.Count; i++)
            {
                var clip = sequence.Clips[i]; float y = headerHeight + musicRows + i * rowHeight;
                if (GUI.Button(new Rect(3, y, labelWidth - 6, rowHeight - 2), (clip.Muted ? "[禁用] " : "") + clip.Name, EditorStyles.miniButton)) { selected = i; selectedKey = null; inspectorPage = 0; }
                Rect r = new(labelWidth + clip.StartTick * pixelsPerTick, y + 3, Mathf.Max(4, clip.DurationTicks * pixelsPerTick), rowHeight - 6);
                Color color = TrackColors[i % TrackColors.Length]; color.a = clip.Muted ? 0.2f : selected == i ? 0.85f : 0.5f;
                EditorGUI.DrawRect(r, color); GUI.Label(new Rect(r.x + 3, r.y, r.width - 3, 16), clip.Name, EditorStyles.whiteLabel);
                DrawKeyframeMarkers(i, r, labelWidth, control);
                EditorGUIUtility.AddCursorRect(new Rect(r.x, r.y, 6, r.height), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(new Rect(r.xMax - 6, r.y, 6, r.height), MouseCursor.ResizeHorizontal);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    selected = dragIndex = i; selectedKey = null; playing = false; inspectorPage = 0;
                    originalStart = clip.StartTick; originalDuration = clip.DurationTicks;
                    dragTick = Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick);
                    dragMode = e.mousePosition.x < r.x + 6 ? -1 : e.mousePosition.x > r.xMax - 6 ? 1 : 0;
                    dragControl = control; GUIUtility.hotControl = control;
                    Undo.RecordObject(sequence, "拖动弹幕片段"); e.Use(); Repaint();
                }
            }
            float eventY = headerHeight + musicRows + sequence.Clips.Count * rowHeight;
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
                    if (dragMode == 2) DragKeyframe(clip, labelWidth);
                    else if (dragMode == 0) clip.StartTick = Mathf.Clamp(SnapTick(originalStart + delta), 0, sequence.DurationTicks - originalDuration);
                    else if (dragMode < 0)
                    {
                        clip.StartTick = Mathf.Clamp(SnapTick(originalStart + delta), 0, originalStart + originalDuration - 1);
                        clip.DurationTicks = originalStart + originalDuration - clip.StartTick;
                    }
                    else clip.DurationTicks = Mathf.Clamp(SnapTick(originalStart + originalDuration + delta) - originalStart, 1, sequence.DurationTicks - originalStart);
                    EditorUtility.SetDirty(sequence); e.Use(); Rebuild();
                }
                else if (e.type == EventType.MouseUp)
                { dragIndex = -1; GUIUtility.hotControl = 0; e.Use(); }
            }
            else if (insideTimeline && e.type == EventType.MouseDown && e.button == 0 && e.mousePosition.x >= labelWidth)
            { playing = false; Seek(SnapTick(Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick))); e.Use(); }
            if (insideTimeline && (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && e.mousePosition.x >= labelWidth)
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
            else if (e.keyCode == KeyCode.Delete) { DeleteSelection(); e.Use(); }
            else if (e.keyCode == KeyCode.D && e.control) { if (!string.IsNullOrEmpty(selectedKey)) CopyKeyframe(); else Duplicate(); e.Use(); }
            else if (e.keyCode == KeyCode.LeftArrow) { playing = false; Seek(currentTick - 1); e.Use(); }
            else if (e.keyCode == KeyCode.RightArrow) { playing = false; Seek(currentTick + 1); e.Use(); }
        }
    }
}
