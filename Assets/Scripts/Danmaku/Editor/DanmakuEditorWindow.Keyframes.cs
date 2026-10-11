using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    public sealed partial class DanmakuEditorWindow
    {
        [SerializeField] private string selectedKey;
        private int generateStart, generateInterval = 30, generateCount = 4;
        private string pendingDeleteClip, pendingDeleteKey;

        private DanmakuKeyframe SelectedKeyframe() => selected >= 0 && selected < sequence.Clips.Count
            ? sequence.Clips[selected].Keyframes.Find(k => k.Id == selectedKey) : null;

        private void KeyframesChanged()
        { playing = false; EditorUtility.SetDirty(sequence); Rebuild(); }

        private void AddKeyframe(int index, DanmakuKeyframeType type, int localTick)
        {
            var clip = sequence.Clips[index];
            if (!DanmakuKeyframeEditing.CanAdd(clip, type)) return;
            Undo.RecordObject(sequence, "添加弹幕关键帧");
            selected = index; selectedKey = DanmakuKeyframeEditing.Add(clip, type, localTick).Id;
            KeyframesChanged();
        }

        private void DeleteKeyframe()
        {
            var key = SelectedKeyframe(); if (key == null) return;
            DeleteKeyframeById(sequence.Clips[selected].Id, key.Id);
        }

        private void DeleteKeyframeById(string clipId, string keyId)
        {
            int index = sequence.Clips.FindIndex(c => c.Id == clipId);
            if (index < 0) return;
            var clip = sequence.Clips[index];
            var key = clip.Keyframes.Find(k => k.Id == keyId);
            if (key == null) return;
            Undo.RecordObject(sequence, "删除弹幕关键帧");
            clip.Keyframes.Remove(key);
            if (selected == index && selectedKey == keyId)
            {
                // Keep the key selection identity so a repeated Delete cannot delete the clip.
                if (dragIndex == index && dragMode == 2)
                { dragIndex = -1; if (GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0; }
            }
            KeyframesChanged();
        }

        private void DeleteSelection()
        {
            if (!string.IsNullOrEmpty(selectedKey)) DeleteKeyframe();
            else Delete();
        }

        private void ApplyPendingKeyframeDeletion()
        {
            if (pendingDeleteKey == null) return;
            string clipId = pendingDeleteClip, keyId = pendingDeleteKey;
            pendingDeleteClip = pendingDeleteKey = null;
            DeleteKeyframeById(clipId, keyId);
        }

        private void CopyKeyframe()
        {
            var key = SelectedKeyframe(); if (key == null) return;
            var clip = sequence.Clips[selected];
            if (!DanmakuKeyframeEditing.CanAdd(clip, key.Type)) return;
            Undo.RecordObject(sequence, "复制弹幕关键帧");
            var copy = key.Copy();
            copy.LocalTick = Mathf.Clamp(currentTick - clip.StartTick, 0, clip.DurationTicks - 1);
            clip.Keyframes.Add(copy); selectedKey = copy.Id; KeyframesChanged();
        }

        private void DrawKeyframeInspector(SerializedProperty clipProperty)
        {
            var clip = sequence.Clips[selected];
            GUILayout.Space(6); GUILayout.Label("片段关键帧 · 右键片段条添加", EditorStyles.boldLabel);
            if (clip.IsSplit) EditorGUILayout.HelpBox("子母弹：一个发射点，一个分裂点；分裂必须晚于发射，子弹不再分裂。", MessageType.Info);
            if (clip.Keyframes.Count == 0) GUILayout.Label("没有发射点，此片段不会发射。", EditorStyles.miniLabel);
            var property = clipProperty.FindPropertyRelative("Keyframes");
            foreach (var key in clip.Keyframes.OrderBy(k => k.LocalTick))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Toggle(selectedKey == key.Id, $"{(key.Type == DanmakuKeyframeType.Emit ? "发射" : "分裂")} · {key.LocalTick} 帧 / {key.LocalTick / 60f:0.###} 秒", EditorStyles.miniButton)) selectedKey = key.Id;
                    if (GUILayout.Button(new GUIContent("删除", "仅删除这一关键帧，保留片段及其他关键帧；可撤销。"), EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        // Apply only after the Inspector commits its serialized snapshot.
                        selectedKey = key.Id; pendingDeleteClip = clip.Id; pendingDeleteKey = key.Id;
                    }
                }
            }
            int index = clip.Keyframes.FindIndex(k => k.Id == selectedKey);
            if (index >= 0)
            {
                var keyProperty = property.GetArrayElementAtIndex(index);
                DanmakuChineseGUI.Time(keyProperty.FindPropertyRelative("LocalTick"), "片段内时间");
                EditorGUILayout.LabelField("全局时间 / 秒", ((clip.StartTick + clip.Keyframes[index].LocalTick) / 60f).ToString("0.###"));
            }
            string error = clip.ValidateKeyframes();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void DrawKeyframeTools()
        {
            var clip = sequence.Clips[selected];
            if (SelectedKeyframe() != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(clip.IsSplit))
                        if (GUILayout.Button("复制选中点到播放头")) CopyKeyframe();
                    if (GUILayout.Button("删除选中点")) DeleteKeyframe();
                }
            }
            GUILayout.Label("右键时间轴片段可添加发射点；子母弹还可添加分裂点。", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawKeyframeMarkers(int index, Rect bar, float labelWidth, int control)
        {
            var clip = sequence.Clips[index]; var e = Event.current;
            DanmakuKeyframe hovered = null;
            foreach (var key in clip.Keyframes)
            {
                float x = labelWidth + (clip.StartTick + key.LocalTick) * pixelsPerTick;
                Rect hit = new(x - 6, bar.y + 14, 12, 12);
                bool isSelected = selected == index && selectedKey == key.Id;
                Color color = key.Type == DanmakuKeyframeType.Split ? new Color(1, 0.7f, 0.1f) : Color.white;
                if (isSelected) EditorGUI.DrawRect(hit, Color.black);
                if (key.Type == DanmakuKeyframeType.Split)
                {
                    EditorGUI.DrawRect(new Rect(x - 1, bar.y + 16, 2, 10), color);
                    EditorGUI.DrawRect(new Rect(x - 3, bar.y + 18, 6, 6), color);
                    EditorGUI.DrawRect(new Rect(x - 4, bar.y + 20, 8, 2), color);
                }
                else
                {
                    EditorGUI.DrawRect(new Rect(x - 1, bar.y + 17, 2, 9), color);
                    EditorGUI.DrawRect(new Rect(x - 3, bar.y + 17, 6, 4), color);
                }
                GUI.Label(hit, new GUIContent("", $"{(key.Type == DanmakuKeyframeType.Emit ? "发射" : "分裂")}：局部 {key.LocalTick} 帧 · 全局 {(clip.StartTick + key.LocalTick) / 60f:0.###} 秒"));
                if (hit.Contains(e.mousePosition)) hovered = key;
            }
            if (hovered != null && e.type == EventType.MouseDown && e.button == 0)
            {
                selected = dragIndex = index; selectedKey = hovered.Id; playing = false;
                dragMode = 2; dragTick = Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick);
                originalStart = hovered.LocalTick; dragControl = control; GUIUtility.hotControl = control;
                Undo.RecordObject(sequence, "拖动弹幕关键帧"); e.Use(); Repaint();
            }
            if (e.type != EventType.ContextClick || (!bar.Contains(e.mousePosition) && hovered == null)) return;
            selected = index; playing = false; music?.Pause();
            int local = Mathf.Clamp(SnapTick(Mathf.RoundToInt((e.mousePosition.x - labelWidth) / pixelsPerTick)) - clip.StartTick, 0, clip.DurationTicks - 1);
            var menu = new GenericMenu();
            if (hovered != null)
            {
                selectedKey = hovered.Id;
                menu.AddItem(new GUIContent("编辑此关键帧"), false, Repaint);
                if (DanmakuKeyframeEditing.CanAdd(clip, hovered.Type)) menu.AddItem(new GUIContent("复制到播放头"), false, CopyKeyframe);
                else menu.AddDisabledItem(new GUIContent("子母弹仅允许一次发射／分裂"));
                string clipId = clip.Id, keyId = hovered.Id;
                menu.AddItem(new GUIContent("删除此关键帧"), false, () => DeleteKeyframeById(clipId, keyId)); menu.AddSeparator("");
            }
            else selectedKey = null;
            if (DanmakuKeyframeEditing.CanAdd(clip, DanmakuKeyframeType.Emit))
                menu.AddItem(new GUIContent("添加发射点"), false, () => AddKeyframe(index, DanmakuKeyframeType.Emit, local));
            else menu.AddDisabledItem(new GUIContent("添加发射点（已存在）"));
            if (clip.IsSplit)
            {
                if (DanmakuKeyframeEditing.CanAdd(clip, DanmakuKeyframeType.Split))
                    menu.AddItem(new GUIContent("添加分裂点"), false, () => AddKeyframe(index, DanmakuKeyframeType.Split, local));
                else menu.AddDisabledItem(new GUIContent("添加分裂点（已存在）"));
            }
            menu.ShowAsContext(); e.Use(); Repaint();
        }

        private void DragKeyframe(DanmakuClip clip, float labelWidth)
        {
            var key = SelectedKeyframe(); if (key == null) return;
            int delta = Mathf.RoundToInt((Event.current.mousePosition.x - labelWidth) / pixelsPerTick) - dragTick;
            key.LocalTick = Mathf.Clamp(SnapTick(clip.StartTick + originalStart + delta) - clip.StartTick, 0, clip.DurationTicks - 1);
        }
    }
}
