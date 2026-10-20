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
        [SerializeField] private bool showAdvanced, showAnimationAdvanced;
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
        [MenuItem("Tools/角色/角色编辑器")]
        public static void Open() => GetWindow<PlayerActionEditorWindow>("角色编辑器").Show();
        [OnOpenAsset]
        private static bool OnOpen(int id, int line)
        {
            if (EditorUtility.InstanceIDToObject(id) is not PlayerActionClip action) return false;
            var window = GetWindow<PlayerActionEditorWindow>(); window.Select(action); window.Show(); return true;
        }
        private void OnEnable()
        {
            minSize = new Vector2(760, 480); titleContent = new GUIContent("角色编辑器");
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
            var playerField = new ObjectField("角色") { objectType = typeof(PlayerDefinition), value = definition, allowSceneObjects = false }; playerField.AddToClassList("player-object");
            playerField.RegisterValueChangedCallback(e => { definition = e.newValue as PlayerDefinition; dirtyPreview = true; RefreshLibrary(); }); toolbar.Add(playerField);
            var actionField = new ObjectField("动作") { objectType = typeof(PlayerActionClip), value = clip, allowSceneObjects = false }; actionField.AddToClassList("player-object");
            actionField.RegisterValueChangedCallback(e => Select(e.newValue as PlayerActionClip)); toolbar.Add(actionField);
            toolbar.Add(new Button(() => { AssetDatabase.SaveAssets(); RefreshStatus(); }) { text = "保存" });
            toolbar.Add(new Button(() =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("创建默认角色"), false, () =>
                {
                    definition = PlayerEditorAssets.EnsureAssets(); playerField.SetValueWithoutNotify(definition);
                    dirtyPreview = true; RefreshLibrary(); RefreshStatus();
                });
                menu.ShowAsContext();
            }) { text = "更多" });
            rootVisualElement.Q<IMGUIContainer>("preview").onGUIHandler = DrawPreview;
            rootVisualElement.Q<IMGUIContainer>("timeline").onGUIHandler = DrawTimeline;
            rootVisualElement.Q<IMGUIContainer>("inspector").onGUIHandler = DrawInspector;
            rootVisualElement.Q<IMGUIContainer>("debug").onGUIHandler = DrawDebug;
            rootVisualElement.Q<Foldout>("debugFoldout").RegisterValueChangedCallback(e =>
            {
                if (e.newValue) return;
                inputMode = false; jumpHeld = fireHeld = airborne = false; horizontal = 0; edges = 0;
                Seek(0);
            });
            status = rootVisualElement.Q<Label>("status"); RefreshLibrary(); RefreshStatus();
        }
        private void RefreshLibrary()
        {
            var list = rootVisualElement.Q<ScrollView>("library"); if (list == null) return; list.Clear(); list.Add(new Label("动作库"));
            if (!definition || !definition.Actions) { list.Add(new Label("请先选择角色")); return; }
            foreach (var binding in definition.Actions.Bindings)
            {
                if (binding == null || !binding.Clip) continue;
                var action = binding.Clip; var button = new Button(() => Select(action)) { text = Localize(binding.Slot.ToString()), tooltip = ActionName(action) };
                if (clip == action) button.AddToClassList("selected"); list.Add(button);
            }
            if (!clip && definition.Actions.Bindings.FirstOrDefault(b => b != null && b.Clip)?.Clip is PlayerActionClip first) Select(first);
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
            if (!clip) { status.text = "请选择动作。每秒 60 帧。"; return; }
            string error = clip.Validate();
            var missing = clip.Nodes.Where(n => n != null && n.Kind == PlayerActionNodeKind.Spine && (n.Spine.Animation == null)).ToArray();
            status.text = error ?? (previewError ?? $"{ActionName(clip)} · {clip.DurationTicks} 帧 / {clip.DurationTicks / 60f:F2} 秒" +
                (missing.Length > 0 ? $" · {missing.Length} 个动画节点使用备用动画" : ""));
            status.tooltip = "每秒 60 帧；节点包含开始帧，不包含结束帧。";
        }
        private void DrawInspector()
        {
            if (!clip) { EditorGUILayout.HelpBox("选择片段后编辑。", MessageType.Info); return; }
            serializedClip ??= new SerializedObject(clip); serializedClip.Update();
            EditorGUILayout.LabelField("动作设置", EditorStyles.boldLabel);
            foreach (string property in new[] { "DisplayName", "DurationTicks", "ActionKind", "EndMode" }) DrawProperty(serializedClip.FindProperty(property));
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "高级设置", true);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                foreach (string property in new[] { "ActiveStartTick", "RecoveryStartTick", "DefaultAllowFire", "DefaultTransitions", "StaminaCost", "CooldownTicks" })
                    DrawProperty(serializedClip.FindProperty(property));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("节点", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            addKind = (PlayerActionNodeKind)EnumPopup("", addKind);
            bool addNode = GUILayout.Button("添加", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
            if (addNode) { serializedClip.ApplyModifiedProperties(); AddNode(); GUIUtility.ExitGUI(); }
            if (selected >= 0 && selected < clip.Nodes.Count)
            {
                var p = serializedClip.FindProperty("Nodes").GetArrayElementAtIndex(selected); var n = clip.Nodes[selected];
                foreach (string property in new[] { "Name", "Kind", "StartTick" }) DrawProperty(p.FindPropertyRelative(property));
                var kind = (PlayerActionNodeKind)p.FindPropertyRelative("Kind").intValue;
                bool isPoint = kind is PlayerActionNodeKind.PointEvent or PlayerActionNodeKind.Audio;
                DrawProperty(p.FindPropertyRelative(isPoint ? "Order" : "EndTick"));
                string[] properties = kind switch
                {
                    PlayerActionNodeKind.Spine => Array.Empty<string>(),
                    PlayerActionNodeKind.Motion => new[] { "Motion", "SpeedScale" },
                    PlayerActionNodeKind.Transition => new[] { "AllowedIntents", "Conditions" },
                    PlayerActionNodeKind.FirePermission => new[] { "AllowFire" },
                    PlayerActionNodeKind.PointEvent => new[] { "Event", "Projectile", "ProjectileSpeed" },
                    PlayerActionNodeKind.Audio => new[] { "Audio", "Volume" },
                    PlayerActionNodeKind.Effect => new[] { "Effect", "LocalOffset", "FollowPlayer", "MirrorWithFacing" },
                    _ => Array.Empty<string>(),
                };
                if (kind == PlayerActionNodeKind.Spine) DrawSpineFields(p.FindPropertyRelative("Spine"));
                foreach (string property in properties) DrawProperty(p.FindPropertyRelative(property));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("复制")) { if (serializedClip.ApplyModifiedProperties()) Changed(); EditorGUIUtility.systemCopyBuffer = "PlayerActionNode:" + JsonUtility.ToJson(n); }
                if (GUILayout.Button("删除")) { serializedClip.ApplyModifiedProperties(); Undo.RecordObject(clip, "删除动作节点"); clip.Nodes.RemoveAt(selected); selected = -1; Changed(); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            else EditorGUILayout.HelpBox("点击时间轴上的节点进行编辑。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorGUIUtility.systemCopyBuffer.StartsWith("PlayerActionNode:", StringComparison.Ordinal)))
                if (GUILayout.Button("粘贴节点")) { serializedClip.ApplyModifiedProperties(); PasteNode(); GUIUtility.ExitGUI(); }
            if (serializedClip.ApplyModifiedProperties()) Changed();
        }
        private static string ActionName(PlayerActionClip action) => Localize(string.IsNullOrWhiteSpace(action.DisplayName) ? action.name : action.DisplayName);
        private static string Localize(string text) => text switch
        {
            "Idle" => "待机", "Move" => "移动", "Jump" => "跳跃", "Fall" => "下落", "FlyStart" => "开始飞行",
            "FlyLoop" => "持续飞行", "FlyEnd" => "结束飞行", "Land" => "落地", "Dash" => "冲刺", "Skill" => "技能",
            "Skill1" => "技能一", "Skill2" => "技能二", "Hurt" => "受伤", "Dead" => "死亡", "None" => "无", "All" => "全部",
            "Spine" => "动画", "Motion" => "移动控制", "Invulnerability" => "无敌", "Transition" => "动作接续",
            "FirePermission" => "射击许可", "PointEvent" => "玩法事件", "Audio" => "音效", "Effect" => "特效",
            "Complete" => "播放后结束", "Loop" => "循环播放", "HoldLast" => "停在最后一帧",
            "Normal" => "正常移动", "Flight" => "飞行", "Disabled" => "禁止移动", "Fire" => "射击", "Fly" => "飞行", "UseItem" => "使用道具",
            "JumpImpulse" => "起跳冲量", "EmitProjectile" => "发射子弹", "SkillPulse" => "技能发射",
            "Airborne" => "在空中", "Grounded" => "在地面", "JumpHeld" => "按住跳跃", "HoldThreshold" => "达到长按时间",
            "HasFlightStamina" => "飞行体力充足", "Rising" => "上升", "Falling" => "下落", "Flying" => "飞行中",
            "DisplayName" => "动作名称", "DurationTicks" => "时长（帧）", "ActionKind" => "动作类型", "EndMode" => "结束方式",
            "ActiveStartTick" => "执行开始帧", "RecoveryStartTick" => "后摇开始帧", "DefaultAllowFire" => "默认允许射击",
            "DefaultTransitions" => "默认可接续", "StaminaCost" => "体力消耗", "CooldownTicks" => "冷却（帧）",
            "Name" => "名称", "Kind" => "类型", "StartTick" => "开始帧", "EndTick" => "结束帧", "Order" => "同帧顺序",
            "SpeedScale" => "速度倍率", "AllowedIntents" => "可接续动作", "Conditions" => "接续条件", "AllowFire" => "允许射击",
            "Event" => "事件类型", "Projectile" => "子弹", "ProjectileSpeed" => "子弹速度", "Volume" => "音量",
            "LocalOffset" => "位置偏移", "FollowPlayer" => "跟随角色", "MirrorWithFacing" => "随朝向镜像（战斗坐标）", "Animation" => "动画资源", "FallbackAnimation" => "备用动画",
            "TrackIndex" => "动画轨道", "SourceOffset" => "起播时间（秒）", "Speed" => "播放速度", "MixSeconds" => "混合时间（秒）",
            null => "",
            _ => text.StartsWith("Spine ", StringComparison.Ordinal) ? "动画 · " + text.Substring(6) : text,
        };
        private static int EnumPopup(string label, Enum value)
        {
            var values = Enum.GetValues(value.GetType()).Cast<Enum>().ToArray();
            return EditorGUILayout.IntPopup(label, Convert.ToInt32(value), values.Select(v => Localize(v.ToString())).ToArray(), values.Select(Convert.ToInt32).ToArray());
        }
        private void DrawProperty(SerializedProperty property)
        {
            var label = new GUIContent(Localize(property.name), property.tooltip);
            if (property.name is "DefaultTransitions" or "AllowedIntents")
            {
                EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel(label);
                var mask = (PlayerIntentMask)property.intValue;
                var bits = new[] { PlayerIntentMask.Skill, PlayerIntentMask.Dash, PlayerIntentMask.Jump, PlayerIntentMask.Fly, PlayerIntentMask.Fire, PlayerIntentMask.UseItem };
                string summary = mask == PlayerIntentMask.None ? "无" : mask == PlayerIntentMask.All ? "全部" :
                    string.Join("、", bits.Where(bit => (mask & bit) != 0).Select(bit => Localize(bit.ToString())));
                if (EditorGUILayout.DropdownButton(new GUIContent(summary), FocusType.Keyboard))
                {
                    var menu = new GenericMenu(); string path = property.propertyPath;
                    menu.AddItem(new GUIContent("无"), mask == PlayerIntentMask.None, () => SetIntentMask(path, PlayerIntentMask.None));
                    menu.AddItem(new GUIContent("全部"), mask == PlayerIntentMask.All, () => SetIntentMask(path, PlayerIntentMask.All));
                    menu.AddSeparator("");
                    foreach (var bit in bits) menu.AddItem(new GUIContent(Localize(bit.ToString())), (mask & bit) != 0, () => SetIntentMask(path, mask ^ bit));
                    menu.ShowAsContext();
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label, true);
                if (!property.isExpanded) return;
                EditorGUI.indentLevel++;
                property.arraySize = Mathf.Max(0, EditorGUILayout.IntField("条件数量", property.arraySize));
                for (int i = 0; i < property.arraySize; i++) DrawEnum(property.GetArrayElementAtIndex(i), new GUIContent($"条件 {i + 1}"));
                EditorGUI.indentLevel--;
            }
            else if (property.propertyType == SerializedPropertyType.Enum) DrawEnum(property, label);
            else if (property.name is "Name" or "DisplayName")
            {
                EditorGUI.BeginChangeCheck(); string value = EditorGUILayout.TextField(label, Localize(property.stringValue));
                if (EditorGUI.EndChangeCheck()) property.stringValue = value;
            }
            else EditorGUILayout.PropertyField(property, label, true);
        }
        private static void DrawEnum(SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup(label, property.enumValueIndex, property.enumNames.Select(Localize).Select(name => new GUIContent(name)).ToArray());
            if (EditorGUI.EndChangeCheck()) property.enumValueIndex = index;
        }
        private void SetIntentMask(string path, PlayerIntentMask value)
        {
            if (!clip || serializedClip == null) return;
            serializedClip.Update();
            var property = serializedClip.FindProperty(path); if (property == null) return;
            property.intValue = (int)value; serializedClip.ApplyModifiedProperties(); Changed();
        }
        private void AddNode()
        {
            Undo.RecordObject(clip, "添加动作节点");
            int start = Mathf.Clamp(tick, 0, Mathf.Max(0, clip.DurationTicks - 1));
            var node = new PlayerActionNode { Kind = addKind, Name = Localize(addKind.ToString()), StartTick = start, EndTick = Mathf.Min(clip.DurationTicks, start + 6), Event = PlayerPointEventKind.JumpImpulse };
            while (node.IsPoint && clip.Nodes.Any(n => n.IsPoint && n.StartTick == node.StartTick && n.Order == node.Order)) node.Order++;
            clip.Nodes.Add(node);
            selected = clip.Nodes.Count - 1; Changed();
        }
        private void DrawSpineFields(SerializedProperty spine)
        {
            DrawProperty(spine.FindPropertyRelative("Animation"));
            var fallback = spine.FindPropertyRelative("FallbackAnimation");
            var data = definition && definition.SkeletonData ? definition.SkeletonData.GetSkeletonData(true) : null;
            if (data != null && data.Animations.Count > 0)
            {
                string[] names = new string[data.Animations.Count]; for (int i = 0; i < names.Length; i++) names[i] = data.Animations.Items[i].Name;
                int selectedAnimation = Array.IndexOf(names, fallback.stringValue);
                EditorGUI.BeginChangeCheck(); int next = EditorGUILayout.Popup("备用动画", selectedAnimation, names);
                if (EditorGUI.EndChangeCheck() && next >= 0) fallback.stringValue = names[next];
            }
            else DrawProperty(fallback);
            DrawProperty(spine.FindPropertyRelative("Speed")); DrawProperty(spine.FindPropertyRelative("Loop"));
            showAnimationAdvanced = EditorGUILayout.Foldout(showAnimationAdvanced, "动画高级设置", true);
            if (showAnimationAdvanced)
                foreach (string property in new[] { "TrackIndex", "SourceOffset", "MixSeconds" }) DrawProperty(spine.FindPropertyRelative(property));
        }
        private void PasteNode()
        {
            const string prefix = "PlayerActionNode:"; string text = EditorGUIUtility.systemCopyBuffer;
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) return;
            try
            {
                var node = JsonUtility.FromJson<PlayerActionNode>(text.Substring(prefix.Length)); if (node == null) return;
                Undo.RecordObject(clip, "粘贴动作节点"); node.Id = Guid.NewGuid().ToString("N");
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
            if (GUILayout.Button("上一帧", GUILayout.Width(56))) Seek(tick - 1);
            if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(55))) { playing = !playing; lastUpdate = EditorApplication.timeSinceStartup; accumulated = 0; }
            if (GUILayout.Button("下一帧", GUILayout.Width(56))) Seek(tick + 1);
            int value = EditorGUILayout.IntSlider(tick, 0, Mathf.Max(1, clip.DurationTicks)); if (value != tick) Seek(value);
            EditorGUILayout.EndHorizontal();
            var kinds = Enum.GetValues(typeof(PlayerActionNodeKind)).Cast<PlayerActionNodeKind>().Where(k => clip.Nodes.Any(n => n != null && n.Kind == k)).ToArray();
            float headerHeight = clip.ActiveStartTick >= 0 ? 42 : 24;
            float height = headerHeight + Mathf.Max(1, clip.Nodes.Count(n => n != null)) * 29;
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
            float y = rect.y + headerHeight; bool hit = false;
            if (kinds.Length == 0) GUI.Label(new Rect(left, y, width, 25), "还没有节点，点击右侧“添加”。", EditorStyles.centeredGreyMiniLabel);
            foreach (PlayerActionNodeKind kind in kinds)
            {
                var indices = Enumerable.Range(0, clip.Nodes.Count).Where(i => clip.Nodes[i] != null && clip.Nodes[i].Kind == kind).ToArray();
                GUI.Label(new Rect(rect.x, y + 4, 80, 20), Localize(kind.ToString()), EditorStyles.miniLabel);
                foreach (int i in indices)
                {
                    var n = clip.Nodes[i]; var lane = new Rect(left, y, width, 25); EditorGUI.DrawRect(lane, new Color(0.3f, 0.3f, 0.3f, 0.25f));
                    var nodeRect = new Rect(left + n.StartTick * px, y + 1, n.IsPoint ? Mathf.Min(130, width - n.StartTick * px) : Mathf.Max(6, (n.EndTick - n.StartTick) * px), 23);
                    Color color = colors[Array.IndexOf(Enum.GetValues(typeof(PlayerActionNodeKind)).Cast<PlayerActionNodeKind>().ToArray(), kind)];
                    if (i == selected) color *= 1.3f; EditorGUI.DrawRect(nodeRect, color);
                    GUI.Label(nodeRect, new GUIContent((n.IsPoint ? "◆ " : "") + Localize(n.Name), Localize(n.Name) + " · " + n.StartTick + (n.IsPoint ? " 帧" : "–" + n.EndTick + " 帧")), EditorStyles.whiteMiniLabel);
                    if (evt.type == EventType.MouseDown && evt.button == 0 && nodeRect.Contains(evt.mousePosition))
                    {
                        selected = i; hit = true; dragMode = n.IsPoint ? 1 : evt.mousePosition.x < nodeRect.x + 6 ? 2 : evt.mousePosition.x > nodeRect.xMax - 6 ? 3 : 1;
                        originStart = n.StartTick; originEnd = n.EndTick; dragX = evt.mousePosition.x;
                        Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.RecordObject(clip, "移动动作节点"); evt.Use(); Repaint();
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
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, 20), $"{Localize(runtime.Motor.Locomotion.ToString())} · {Localize(runtime.Action?.Clip.Kind.ToString() ?? "None")}", EditorStyles.whiteMiniLabel);
        }
        private void DrawDebug()
        {
            EditorGUILayout.BeginHorizontal();
            bool nextInputMode = GUILayout.Toggle(inputMode, "输入调试模式", GUILayout.Width(130));
            if (nextInputMode != inputMode) { inputMode = nextInputMode; tick = 0; dirtyPreview = true; }
            bool nextAirborne = GUILayout.Toggle(airborne, "空中起点", GUILayout.Width(95)); if (nextAirborne != airborne) { airborne = nextAirborne; dirtyPreview = true; }
            jumpHeld = GUILayout.Toggle(jumpHeld, "按住跳跃 / 飞行", GUILayout.Width(130)); fireHeld = GUILayout.Toggle(fireHeld, "按住射击", GUILayout.Width(95));
            horizontal = (short)(EditorGUILayout.IntSlider("水平", horizontal == 0 ? 0 : horizontal < 0 ? -1 : 1, -1, 1) * short.MaxValue);
            EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("同帧：技能+冲刺+跳跃")) { edges |= PlayerButtonEdges.Skill1 | PlayerButtonEdges.Dash | PlayerButtonEdges.JumpPressed; StepPreview(); }
            if (GUILayout.Button("冲刺")) { edges |= PlayerButtonEdges.Dash; StepPreview(); }
            if (GUILayout.Button("跳跃")) { edges |= PlayerButtonEdges.JumpPressed; jumpHeld = true; StepPreview(); }
            if (GUILayout.Button("松开跳跃 / 飞行")) { edges |= PlayerButtonEdges.JumpReleased; jumpHeld = false; StepPreview(); }
            if (GUILayout.Button("受伤 / 中断")) { runtime?.ReceiveDamage(Framework.FixedPoint.FP64.One); if (runtime != null) { view?.ConsumeEvents(runtime); view?.Sync(runtime); } Repaint(); }
            EditorGUILayout.EndHorizontal();
            if (runtime == null) return;
            var position = runtime.Actor.Pose.Position;
            EditorGUILayout.LabelField($"体力 {runtime.Stamina.ToFloat():F1} · 无敌 {(runtime.Invulnerable ? "是" : "否")} · 射击 {(runtime.FireAllowed ? "允许" : "禁止")} · 位置 ({position.X.ToFloat():F2}, {position.Y.ToFloat():F2}) · 判定半径 {definition.HitRadius:F3}");
            string pending = string.Join("、", runtime.Inputs.Pending.Select(p => $"{IntentName(p.Kind, p.Slot)}（剩余 {Mathf.Max(0, p.ExpireTick - runtime.Tick + 1)} 帧）"));
            EditorGUILayout.LabelField($"保持 {runtime.Inputs.HeldTicks} 帧 · 待执行 {(pending.Length == 0 ? "无" : pending)}");
            foreach (var decision in runtime.Decisions.Take(3)) EditorGUILayout.LabelField($"{IntentName(decision.Intent.Kind, decision.Intent.Slot)}：{decision.Reason}");
        }
        private static string IntentName(PlayerIntentKind kind, int slot) => Localize(kind.ToString()) + (kind == PlayerIntentKind.Skill ? $" {slot}" : "");
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
