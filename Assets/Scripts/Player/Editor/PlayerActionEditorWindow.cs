using System;
using System.Collections.Generic;
using System.Linq;
using Spine.Unity;
using TapTap.Danmaku;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TapTap.Player.Editor
{
    public sealed class PlayerActionEditorWindow : EditorWindow
    {
        private const string Layout = "Assets/Scripts/Player/Editor/PlayerActionEditorWindow.uxml";
        [SerializeField] private PlayerDefinition definition;
        [SerializeField] private PlayerActionClip clip;
        [SerializeField] private int tick, selected = -1;
        private PlayerActionNodeKind addKind;
        private SerializedObject serializedClip;
        private PreviewRenderUtility preview;
        private PlayerSpineView view;
        private PlayerRuntime runtime;
        private PlayerDefinition previewDefinition;
        private PlayerActionLibrary previewLibrary;
        private bool playing, inputMode, jumpHeld, fireHeld, airborne, dirtyPreview = true;
        private short horizontal;
        private PlayerButtonEdges edges;
        private double lastUpdate, accumulated;
        private string previewError;
        private int dragMode, originStart, originEnd, undoGroup;
        private float dragX;
        private Label status;
        private readonly Color[] colors = { new(0.25f, 0.48f, 0.7f), new(0.25f, 0.56f, 0.4f), new(0.54f, 0.36f, 0.68f),
            new(0.2f, 0.57f, 0.48f), new(0.52f, 0.53f, 0.28f), new(0.7f, 0.53f, 0.2f), new(0.35f, 0.6f, 0.4f), new(0.55f, 0.37f, 0.65f) };
        [MenuItem("Tools/Player/动作片段编辑器")]
        public static void Open() => GetWindow<PlayerActionEditorWindow>("Player Action Editor").Show();
        [OnOpenAsset]
        private static bool OnOpen(int id, int line)
        {
            if (EditorUtility.InstanceIDToObject(id) is not PlayerActionClip action) return false;
            var window = GetWindow<PlayerActionEditorWindow>(); window.Select(action); window.Show(); return true;
        }
        private void OnEnable()
        {
            minSize = new Vector2(900, 620); titleContent = new GUIContent("Player Action Editor");
            if (!definition) definition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerEditorAssets.DefinitionPath);
            EditorApplication.update += UpdatePreview; Undo.undoRedoPerformed += OnUndo;
        }
        private void OnDisable() { EditorApplication.update -= UpdatePreview; Undo.undoRedoPerformed -= OnUndo; DisposePreview(); }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Layout);
            if (!tree) { rootVisualElement.Add(new Label("缺少编辑器 UXML。")); return; }
            tree.CloneTree(rootVisualElement);
            var toolbar = rootVisualElement.Q<VisualElement>("toolbar");
            var playerField = new ObjectField("玩家") { objectType = typeof(PlayerDefinition), value = definition, allowSceneObjects = false }; playerField.AddToClassList("player-object");
            playerField.RegisterValueChangedCallback(e => { definition = e.newValue as PlayerDefinition; dirtyPreview = true; RefreshLibrary(); }); toolbar.Add(playerField);
            var actionField = new ObjectField("片段") { objectType = typeof(PlayerActionClip), value = clip, allowSceneObjects = false }; actionField.AddToClassList("player-object");
            actionField.RegisterValueChangedCallback(e => Select(e.newValue as PlayerActionClip)); toolbar.Add(actionField);
            toolbar.Add(new Button(() => { AssetDatabase.SaveAssets(); RefreshStatus(); }) { text = "保存资产" });
            toolbar.Add(new Button(() => { definition = PlayerEditorAssets.EnsureAssets(); playerField.SetValueWithoutNotify(definition); RefreshLibrary(); }) { text = "创建默认玩家" });
            rootVisualElement.Q<IMGUIContainer>("preview").onGUIHandler = DrawPreview;
            rootVisualElement.Q<IMGUIContainer>("timeline").onGUIHandler = DrawTimeline;
            rootVisualElement.Q<IMGUIContainer>("inspector").onGUIHandler = DrawInspector;
            rootVisualElement.Q<IMGUIContainer>("debug").onGUIHandler = DrawDebug;
            status = rootVisualElement.Q<Label>("status"); RefreshLibrary(); RefreshStatus();
        }
        private void RefreshLibrary()
        {
            var list = rootVisualElement.Q<ScrollView>("library"); if (list == null) return; list.Clear(); list.Add(new Label("动作库"));
            if (!definition || !definition.Actions) return;
            foreach (var binding in definition.Actions.Bindings)
            {
                if (binding == null || !binding.Clip) continue;
                var action = binding.Clip; var button = new Button(() => Select(action)) { text = binding.Slot.ToString() };
                if (clip == action) button.AddToClassList("selected"); list.Add(button);
            }
        }
        private void Select(PlayerActionClip action)
        {
            clip = action; selected = -1; tick = 0; playing = false; inputMode = false; dirtyPreview = true;
            serializedClip = clip ? new SerializedObject(clip) : null;
            foreach (var objectField in rootVisualElement.Query<ObjectField>().ToList()) if (objectField.objectType == typeof(PlayerActionClip)) objectField.SetValueWithoutNotify(clip);
            RefreshLibrary(); RefreshStatus(); Repaint();
        }
        private void OnUndo() { dirtyPreview = true; serializedClip = clip ? new SerializedObject(clip) : null; RefreshStatus(); Repaint(); }
        private void RefreshStatus()
        {
            if (status == null) return;
            if (!clip) { status.text = "选择动作片段。时间为 60 Tick/s；区间开始包含，结束不包含。"; return; }
            string error = clip.Validate();
            var missing = clip.Nodes.Where(n => n != null && n.Kind == PlayerActionNodeKind.Spine && (n.Spine.Animation == null)).ToArray();
            status.text = error ?? (previewError != null ? previewError : $"{clip.DisplayName} · {clip.DurationTicks} Tick / {clip.DurationTicks / 60f:F3}s · {missing.Length} 个 Spine 节点使用占位动画。区间 [Start, End)。");
        }
        private void DrawInspector()
        {
            if (!clip) { EditorGUILayout.HelpBox("选择片段后编辑。", MessageType.Info); return; }
            serializedClip ??= new SerializedObject(clip); serializedClip.Update();
            EditorGUI.BeginChangeCheck();
            foreach (string property in new[] { "DisplayName", "DurationTicks", "ActionKind", "EndMode", "ActiveStartTick", "RecoveryStartTick", "DefaultAllowFire", "DefaultTransitions", "StaminaCost", "CooldownTicks" })
                EditorGUILayout.PropertyField(serializedClip.FindProperty(property));
            EditorGUILayout.Space(); EditorGUILayout.LabelField("节点", EditorStyles.boldLabel);
            addKind = (PlayerActionNodeKind)EditorGUILayout.EnumPopup("新增类型", addKind);
            if (GUILayout.Button("添加节点")) { serializedClip.ApplyModifiedProperties(); AddNode(); GUIUtility.ExitGUI(); }
            if (selected >= 0 && selected < clip.Nodes.Count)
            {
                var p = serializedClip.FindProperty("Nodes").GetArrayElementAtIndex(selected); var n = clip.Nodes[selected];
                foreach (string property in new[] { "Name", "Kind", "StartTick" }) EditorGUILayout.PropertyField(p.FindPropertyRelative(property));
                EditorGUILayout.PropertyField(p.FindPropertyRelative(n.IsPoint ? "Order" : "EndTick"));
                string[] properties = n.Kind switch
                {
                    PlayerActionNodeKind.Spine => Array.Empty<string>(),
                    PlayerActionNodeKind.Motion => new[] { "Motion", "SpeedScale" },
                    PlayerActionNodeKind.Transition => new[] { "AllowedIntents", "Conditions" },
                    PlayerActionNodeKind.FirePermission => new[] { "AllowFire" },
                    PlayerActionNodeKind.PointEvent => new[] { "Event", "Projectile", "ProjectileSpeed" },
                    PlayerActionNodeKind.Audio => new[] { "Audio", "Volume" },
                    PlayerActionNodeKind.Effect => new[] { "Effect", "LocalOffset", "FollowPlayer" },
                    _ => Array.Empty<string>(),
                };
                if (n.Kind == PlayerActionNodeKind.Spine) DrawSpineFields(p.FindPropertyRelative("Spine"));
                foreach (string property in properties) EditorGUILayout.PropertyField(p.FindPropertyRelative(property), true);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("复制")) EditorGUIUtility.systemCopyBuffer = "PlayerActionNode:" + JsonUtility.ToJson(n);
                if (GUILayout.Button("删除")) { Undo.RecordObject(clip, "Delete action node"); clip.Nodes.RemoveAt(selected); selected = -1; Changed(); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("粘贴节点")) { PasteNode(); GUIUtility.ExitGUI(); }
            if (EditorGUI.EndChangeCheck()) { serializedClip.ApplyModifiedProperties(); Changed(); }
            else serializedClip.ApplyModifiedProperties();
        }
        private void AddNode()
        {
            Undo.RecordObject(clip, "Add action node");
            int start = Mathf.Clamp(tick, 0, Mathf.Max(0, clip.DurationTicks - 1));
            var node = new PlayerActionNode { Kind = addKind, Name = addKind.ToString(), StartTick = start, EndTick = Mathf.Min(clip.DurationTicks, start + 6) };
            while (node.IsPoint && clip.Nodes.Any(n => n.IsPoint && n.StartTick == node.StartTick && n.Order == node.Order)) node.Order++;
            clip.Nodes.Add(node);
            selected = clip.Nodes.Count - 1; Changed();
        }
        private void DrawSpineFields(SerializedProperty spine)
        {
            EditorGUILayout.PropertyField(spine.FindPropertyRelative("Animation"));
            var fallback = spine.FindPropertyRelative("FallbackAnimation");
            var data = definition && definition.SkeletonData ? definition.SkeletonData.GetSkeletonData(true) : null;
            if (data != null && data.Animations.Count > 0)
            {
                string[] names = new string[data.Animations.Count]; for (int i = 0; i < names.Length; i++) names[i] = data.Animations.Items[i].Name;
                int selectedAnimation = Array.IndexOf(names, fallback.stringValue);
                EditorGUI.BeginChangeCheck(); int next = EditorGUILayout.Popup("占位动画", selectedAnimation, names);
                if (EditorGUI.EndChangeCheck() && next >= 0) fallback.stringValue = names[next];
            }
            else EditorGUILayout.PropertyField(fallback);
            foreach (string property in new[] { "TrackIndex", "SourceOffset", "Speed", "MixSeconds", "Loop" }) EditorGUILayout.PropertyField(spine.FindPropertyRelative(property));
        }
        private void PasteNode()
        {
            const string prefix = "PlayerActionNode:"; string text = EditorGUIUtility.systemCopyBuffer;
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) return;
            try
            {
                var node = JsonUtility.FromJson<PlayerActionNode>(text.Substring(prefix.Length)); if (node == null) return;
                Undo.RecordObject(clip, "Paste action node"); node.Id = Guid.NewGuid().ToString("N");
                int duration = Mathf.Max(1, node.EndTick - node.StartTick); node.StartTick = Mathf.Clamp(tick, 0, Mathf.Max(0, clip.DurationTicks - 1));
                node.EndTick = Mathf.Min(clip.DurationTicks, node.StartTick + duration);
                while (node.IsPoint && clip.Nodes.Any(n => n.IsPoint && n.StartTick == node.StartTick && n.Order == node.Order)) node.Order++;
                clip.Nodes.Add(node); selected = clip.Nodes.Count - 1; Changed();
            }
            catch (ArgumentException) { ShowNotification(new GUIContent("剪贴板不是有效节点。")); }
        }
        private void Changed() { EditorUtility.SetDirty(clip); dirtyPreview = true; playing = false; RefreshStatus(); Repaint(); }
        private void DrawTimeline()
        {
            if (!clip) return;
            var evt = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("上一 Tick", GUILayout.Width(80))) Seek(tick - 1);
            if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(55))) { playing = !playing; lastUpdate = EditorApplication.timeSinceStartup; accumulated = 0; }
            if (GUILayout.Button("下一 Tick", GUILayout.Width(80))) Seek(tick + 1);
            int value = EditorGUILayout.IntSlider(tick, 0, Mathf.Max(1, clip.DurationTicks)); if (value != tick) Seek(value);
            EditorGUILayout.EndHorizontal();
            float height = 42 + Enum.GetValues(typeof(PlayerActionNodeKind)).Cast<PlayerActionNodeKind>().Sum(k => Mathf.Max(1, clip.Nodes.Count(n => n != null && n.Kind == k)) * 29);
            var rect = GUILayoutUtility.GetRect(100, height, GUILayout.ExpandWidth(true));
            float left = rect.x + 85, width = Mathf.Max(1, rect.width - 90), px = width / Mathf.Max(1, clip.DurationTicks);
            for (int t = 0; t <= clip.DurationTicks; t += Mathf.Max(1, Mathf.CeilToInt(clip.DurationTicks / 6f)))
                GUI.Label(new Rect(Mathf.Clamp(left + t * px - 5, left, rect.xMax - 30), rect.y, 40, 20), t.ToString(), EditorStyles.miniLabel);
            if (clip.ActiveStartTick >= 0)
            {
                GUI.Label(new Rect(rect.x, rect.y + 18, 80, 18), "阶段");
                foreach (var pair in new[] { (0, clip.ActiveStartTick, "前摇"), (clip.ActiveStartTick, clip.RecoveryStartTick < 0 ? clip.DurationTicks : clip.RecoveryStartTick, "执行"), (clip.RecoveryStartTick, clip.DurationTicks, "后摇") })
                    if (pair.Item1 >= 0 && pair.Item2 > pair.Item1) GUI.Box(new Rect(left + pair.Item1 * px, rect.y + 18, (pair.Item2 - pair.Item1) * px, 18), pair.Item3);
            }
            float y = rect.y + 42; bool hit = false;
            foreach (PlayerActionNodeKind kind in Enum.GetValues(typeof(PlayerActionNodeKind)))
            {
                var indices = Enumerable.Range(0, clip.Nodes.Count).Where(i => clip.Nodes[i] != null && clip.Nodes[i].Kind == kind).ToArray();
                GUI.Label(new Rect(rect.x, y + 4, 80, 20), kind.ToString(), EditorStyles.miniLabel);
                if (indices.Length == 0) { EditorGUI.DrawRect(new Rect(left, y, width, 25), new Color(0.3f, 0.3f, 0.3f, 0.25f)); y += 29; continue; }
                foreach (int i in indices)
                {
                    var n = clip.Nodes[i]; var lane = new Rect(left, y, width, 25); EditorGUI.DrawRect(lane, new Color(0.3f, 0.3f, 0.3f, 0.25f));
                    var nodeRect = new Rect(left + n.StartTick * px, y + 1, n.IsPoint ? Mathf.Min(130, width - n.StartTick * px) : Mathf.Max(6, (n.EndTick - n.StartTick) * px), 23);
                    Color color = colors[Array.IndexOf(Enum.GetValues(typeof(PlayerActionNodeKind)).Cast<PlayerActionNodeKind>().ToArray(), kind)];
                    if (i == selected) color *= 1.3f; EditorGUI.DrawRect(nodeRect, color);
                    GUI.Label(nodeRect, new GUIContent((n.IsPoint ? "◆ " : "") + n.Name, n.Name + " @ " + n.StartTick + (n.IsPoint ? "" : "–" + n.EndTick)), EditorStyles.whiteMiniLabel);
                    if (evt.type == EventType.MouseDown && evt.button == 0 && nodeRect.Contains(evt.mousePosition))
                    {
                        selected = i; hit = true; dragMode = n.IsPoint ? 1 : evt.mousePosition.x < nodeRect.x + 6 ? 2 : evt.mousePosition.x > nodeRect.xMax - 6 ? 3 : 1;
                        originStart = n.StartTick; originEnd = n.EndTick; dragX = evt.mousePosition.x;
                        Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.RecordObject(clip, "Move action node"); evt.Use(); Repaint();
                        GUIUtility.hotControl = controlId;
                    }
                    y += 29;
                }
            }
            EditorGUI.DrawRect(new Rect(left + Mathf.Min(tick, clip.DurationTicks) * px, rect.y, 1, height), Color.white);
            if (!hit && evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition)) { Seek(Mathf.RoundToInt((evt.mousePosition.x - left) / px)); evt.Use(); }
            if (dragMode != 0 && selected >= 0 && selected < clip.Nodes.Count)
            {
                var n = clip.Nodes[selected];
                if (evt.type == EventType.MouseDrag)
                {
                    int delta = Mathf.RoundToInt((evt.mousePosition.x - dragX) / px);
                    if (dragMode == 1) { int size = n.IsPoint ? 1 : originEnd - originStart; n.StartTick = Mathf.Clamp(originStart + delta, 0, Mathf.Max(0, clip.DurationTicks - size)); n.EndTick = n.StartTick + size; }
                    else if (dragMode == 2) n.StartTick = Mathf.Clamp(originStart + delta, 0, n.EndTick - 1);
                    else n.EndTick = Mathf.Clamp(originEnd + delta, n.StartTick + 1, clip.DurationTicks);
                    Changed(); evt.Use();
                }
                if (evt.type == EventType.MouseUp) { dragMode = 0; GUIUtility.hotControl = 0; Undo.CollapseUndoOperations(undoGroup); evt.Use(); }
            }
        }
        private void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(100, 230, GUILayout.ExpandWidth(true));
            if (dirtyPreview) RebuildPreview();
            if (preview == null || view == null || runtime == null) { GUI.Label(rect, previewError ?? "选择玩家定义与片段。", EditorStyles.centeredGreyMiniLabel); return; }
            view.Sync(runtime);
            var renderer = view.Root.GetComponentInChildren<Renderer>();
            var bounds = renderer ? renderer.bounds : new Bounds(view.Root.transform.position, Vector3.one);
            preview.camera.orthographic = true; preview.camera.orthographicSize = Mathf.Max(0.6f, bounds.extents.y * 1.25f, bounds.extents.x * 1.25f * rect.height / Mathf.Max(1, rect.width));
            preview.camera.transform.position = bounds.center + new Vector3(0, 0, -10); preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            preview.BeginPreview(rect, GUIStyle.none); preview.Render(true); var image = preview.EndPreview(); GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, 40), $"{runtime.Motor.Locomotion} · {runtime.Action?.Clip.Kind.ToString() ?? "None"} · 无敌 {runtime.Invulnerable}\n体力 {runtime.Stamina.ToFloat():F1} · 射击许可 {runtime.FireAllowed}", EditorStyles.whiteMiniLabel);
            var p = runtime.Actor.Pose.Position; GUI.Label(new Rect(rect.x + 8, rect.yMax - 22, rect.width - 16, 20), $"逻辑位置 ({p.X.ToFloat():F2}, {p.Y.ToFloat():F2}) · 判定半径 {definition.HitRadius:F3}", EditorStyles.whiteMiniLabel);
        }
        private void DrawDebug()
        {
            EditorGUILayout.BeginHorizontal();
            bool nextInputMode = GUILayout.Toggle(inputMode, "输入调试模式", GUILayout.Width(130));
            if (nextInputMode != inputMode) { inputMode = nextInputMode; tick = 0; dirtyPreview = true; }
            bool nextAirborne = GUILayout.Toggle(airborne, "空中起点", GUILayout.Width(95)); if (nextAirborne != airborne) { airborne = nextAirborne; dirtyPreview = true; }
            jumpHeld = GUILayout.Toggle(jumpHeld, "JumpFly 保持", GUILayout.Width(120)); fireHeld = GUILayout.Toggle(fireHeld, "射击保持", GUILayout.Width(95));
            horizontal = (short)(EditorGUILayout.IntSlider("水平", horizontal == 0 ? 0 : horizontal < 0 ? -1 : 1, -1, 1) * short.MaxValue);
            EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("同 Tick：技能+冲刺+跳跃")) { edges |= PlayerButtonEdges.Skill1 | PlayerButtonEdges.Dash | PlayerButtonEdges.JumpPressed; StepPreview(); }
            if (GUILayout.Button("冲刺")) { edges |= PlayerButtonEdges.Dash; StepPreview(); }
            if (GUILayout.Button("跳跃")) { edges |= PlayerButtonEdges.JumpPressed; jumpHeld = true; StepPreview(); }
            if (GUILayout.Button("松开 JumpFly")) { edges |= PlayerButtonEdges.JumpReleased; jumpHeld = false; StepPreview(); }
            if (GUILayout.Button("受伤 / 中断")) { runtime?.ReceiveDamage(Framework.FixedPoint.FP64.One); if (runtime != null) { view?.ConsumeEvents(runtime); view?.Sync(runtime); } Repaint(); }
            EditorGUILayout.EndHorizontal();
            if (runtime == null) return;
            string pending = string.Join("、", runtime.Inputs.Pending.Select(p => $"{p.Kind}{p.Slot}({Mathf.Max(0, p.ExpireTick - runtime.Tick + 1)}T)"));
            EditorGUILayout.LabelField($"保持 {runtime.Inputs.HeldTicks} Tick · 待执行 {pending}");
            foreach (var decision in runtime.Decisions.Take(3)) EditorGUILayout.LabelField($"{decision.Intent.Kind}{decision.Intent.Slot}：{decision.Reason}");
        }
        private void Seek(int target) { playing = false; tick = Mathf.Clamp(target, 0, clip ? clip.DurationTicks : 0); dirtyPreview = true; Repaint(); }
        private void RebuildPreview()
        {
            dirtyPreview = false; DisposePreview(); previewError = null;
            if (!definition || !clip) return;
            if (clip.Validate() is string error) { previewError = error; RefreshStatus(); return; }
            try
            {
                previewDefinition = Object.Instantiate(definition); previewDefinition.hideFlags = HideFlags.HideAndDontSave;
                previewLibrary = Object.Instantiate(definition.Actions); previewLibrary.hideFlags = HideFlags.HideAndDontSave;
                previewDefinition.Actions = previewLibrary;
                var binding = previewLibrary.Bindings.FirstOrDefault(b => b.Clip == clip);
                PlayerClipSlot slot = binding?.Slot ?? PlayerClipSlot.Dash;
                if (binding == null) { previewLibrary.Bindings.RemoveAll(b => b.Slot == slot); previewLibrary.Bindings.Add(new() { Slot = slot, Clip = clip }); }
                runtime = new PlayerRuntime(previewDefinition, new Vector2(20, 20), new Vector2(0, airborne ? 0 : definition.Motor.GroundHeight), inputMode ? null : slot);
                preview = new PreviewRenderUtility(); view = new PlayerSpineView(previewDefinition, null); view.Root.hideFlags = HideFlags.HideAndDontSave; preview.AddSingleGO(view.Root);
                for (int i = 0; i < tick; i++) { runtime.BeginTick(new PlayerTickInput(horizontal, jumpHeld, fireHeld)); view.Sync(runtime); view.ConsumeEvents(runtime, false); runtime.EndTick(); }
                view.Sync(runtime);
            }
            catch (Exception e) { previewError = "预览失败：" + e.Message; DisposePreview(); }
            RefreshStatus();
        }
        private void StepPreview()
        {
            if (dirtyPreview) RebuildPreview(); if (runtime == null) return;
            runtime.BeginTick(new PlayerTickInput(horizontal, jumpHeld, fireHeld, edges)); edges = 0;
            view.Sync(runtime); view.ConsumeEvents(runtime); runtime.EndTick(); tick = runtime.Tick; Repaint();
        }
        private void UpdatePreview()
        {
            if (!playing) return; double now = EditorApplication.timeSinceStartup; accumulated += Math.Min(0.25, now - lastUpdate); lastUpdate = now;
            while (accumulated >= 1d / 60 && playing)
            { accumulated -= 1d / 60; StepPreview(); if (!inputMode && clip && tick >= clip.DurationTicks) playing = false; }
        }
        private void DisposePreview()
        {
            view?.Dispose(); view = null; preview?.Cleanup(); preview = null; runtime = null;
            if (previewLibrary) Object.DestroyImmediate(previewLibrary); if (previewDefinition) Object.DestroyImmediate(previewDefinition);
            previewLibrary = null; previewDefinition = null;
        }
    }
}
