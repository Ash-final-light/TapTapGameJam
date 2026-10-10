using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using TapTap.Danmaku;
using TapTap.Systems;
using UnityEngine;

namespace TapTap.Player
{
    public sealed class PlayerSpineView : IDisposable
    {
        public readonly GameObject Root;
        private readonly PlayerDefinition definition;
        private readonly SkeletonAnimation skeleton;
        private readonly SpriteRenderer fallback;
        private readonly Dictionary<string, GameObject> effects = new();
        private readonly Dictionary<int, List<AudioSource>> audio = new();
        private readonly Dictionary<int, string> tracks = new();
        private readonly HashSet<int> activeTracks = new();
        private int visualInstance = -1;
        private readonly SystemRoot system;
        private readonly PlayerHitRangeView hitRange;
        public bool HasSpine => skeleton != null;
        public PlayerSpineView(PlayerDefinition definition, Transform parent, SystemRoot system = null)
        {
            this.definition = definition; this.system = system;
            Root = definition.VisualPrefab ? UnityEngine.Object.Instantiate(definition.VisualPrefab, parent) : new GameObject("Player Visual");
            if (!definition.VisualPrefab) Root.transform.SetParent(parent, false);
            Root.name = "Player Visual";
            skeleton = Root.GetComponentInChildren<SkeletonAnimation>(true);
            if (!skeleton && definition.SkeletonData)
            { skeleton = Root.AddComponent<SkeletonAnimation>(); skeleton.skeletonDataAsset = definition.SkeletonData; }
            if (skeleton)
            {
                skeleton.Initialize(false); skeleton.enabled = false;
                var renderer = skeleton.GetComponent<MeshRenderer>(); if (renderer) renderer.sortingOrder = 20;
            }
            else
            {
                fallback = Root.GetComponent<SpriteRenderer>();
                if (!fallback) fallback = Root.AddComponent<SpriteRenderer>();
                fallback.sprite = definition.FallbackSprite; fallback.sortingOrder = 20;
                if (fallback.sprite) Root.transform.localScale = Vector3.one * definition.FallbackVisualSize / Mathf.Max(0.001f, fallback.sprite.bounds.size.y);
            }
            hitRange = new PlayerHitRangeView(definition, Root.transform);
        }
        public void ConsumeEvents(PlayerRuntime player, bool playAudio = true)
        {
            foreach (var e in player.Events)
            {
                string key = e.InstanceId + ":" + e.Node?.Id;
                switch (e.Kind)
                {
                    case PlayerRuntimeEventKind.Audio:
                        if (!playAudio || !e.Node.Audio) break;
                        var source = Root.AddComponent<AudioSource>(); source.playOnAwake = false; source.clip = e.Node.Audio; source.volume = e.Node.Volume;
                        if (system) system.Audio.Register(source, AudioChannel.Effects);
                        if (!audio.TryGetValue(e.InstanceId, out var sources)) audio.Add(e.InstanceId, sources = new());
                        sources.Add(source); source.Play(); break;
                    case PlayerRuntimeEventKind.EffectStarted:
                        if (!e.Node.Effect || effects.ContainsKey(key)) break;
                        var fx = UnityEngine.Object.Instantiate(e.Node.Effect, e.Node.FollowPlayer ? Root.transform : Root.transform.parent);
                        fx.transform.position = Root.transform.TransformPoint(e.Node.LocalOffset); effects.Add(key, fx); break;
                    case PlayerRuntimeEventKind.EffectStopped:
                        if (effects.Remove(key, out var effect)) Release(effect); break;
                    case PlayerRuntimeEventKind.ActionStopped:
                        if (audio.Remove(e.InstanceId, out var sounds)) foreach (var sound in sounds) ReleaseAudio(sound); break;
                }
            }
            var empty = new List<int>();
            foreach (var pair in audio)
            {
                for (int i = pair.Value.Count - 1; i >= 0; i--)
                    if (!pair.Value[i] || !pair.Value[i].isPlaying) { ReleaseAudio(pair.Value[i]); pair.Value.RemoveAt(i); }
                if (pair.Value.Count == 0) empty.Add(pair.Key);
            }
            foreach (var id in empty) audio.Remove(id);
        }
        public void Sync(PlayerRuntime player)
        {
            SetPosition(player);
            hitRange.Sync(player);
            var color = player.Motor.Flying ? new Color(0.65f, 0.9f, 1) : Color.white;
            if (player.Action?.Clip.Kind == PlayerActionKind.Skill) color = player.Action.SkillSlot == 2 ? definition.Skill2Color : definition.Skill1Color;
            color.a = player.Invulnerable ? definition.InvulnerableAlpha : 1;
            if (player.Action?.Clip.Kind == PlayerActionKind.Hurt) color = new Color(1, 0.55f, 0.55f, color.a);
            if (skeleton && skeleton.Skeleton != null)
            {
                skeleton.Skeleton.R = color.r; skeleton.Skeleton.G = color.g; skeleton.Skeleton.B = color.b; skeleton.Skeleton.A = color.a;
                skeleton.Skeleton.ScaleX = Mathf.Abs(skeleton.Skeleton.ScaleX) * player.Motor.Facing;
                Apply(player.VisualAction); skeleton.LateUpdate();
            }
            else if (fallback) { fallback.color = color; fallback.flipX = player.Motor.Facing < 0; }
        }
        public void SetPosition(PlayerRuntime player) => Root.transform.localPosition = DanmakuSimulation.ToVector(player.Actor.Pose.Position);
        private void Apply(PlayerActionInstance action)
        {
            if (visualInstance != (action?.Id ?? 0))
            { foreach (int index in new List<int>(tracks.Keys)) tracks[index] = ""; visualInstance = action?.Id ?? 0; }
            activeTracks.Clear();
            if (action != null)
                foreach (var n in action.Clip.Nodes)
                {
                    if (n.Kind != PlayerActionNodeKind.Spine || !n.Contains(Mathf.Min(action.LocalTick, action.Clip.Duration - 1))) continue;
                    string name = n.Spine.AnimationName; int index = n.Spine.TrackIndex;
                    if (string.IsNullOrEmpty(name) || skeleton.Skeleton.Data.FindAnimation(name) == null) continue;
                    activeTracks.Add(index);
                    if (!tracks.TryGetValue(index, out var old) || old != n.Id)
                    { var entry = skeleton.AnimationState.SetAnimation(index, name, n.Spine.Loop); entry.MixDuration = n.Spine.MixSeconds; tracks[index] = n.Id; }
                    var track = skeleton.AnimationState.GetCurrent(index);
                    track.MixTime = Mathf.Max(0, action.LocalTick - n.StartTick) / 60f;
                    track.TrackTime = n.Spine.SourceOffset + Mathf.Max(0, action.LocalTick - n.StartTick) / 60f * n.Spine.Speed;
                }
            var removed = new List<int>();
            foreach (var index in tracks.Keys) if (!activeTracks.Contains(index)) { skeleton.AnimationState.ClearTrack(index); removed.Add(index); }
            foreach (var index in removed) tracks.Remove(index);
            skeleton.Skeleton.SetToSetupPose(); skeleton.Update(0);
        }
        public void AdvanceTerminal(float deltaSeconds)
        { if (skeleton && skeleton.Skeleton != null) { skeleton.Update(Mathf.Max(0, deltaSeconds)); skeleton.LateUpdate(); } }
        public void SetPaused(bool paused)
        {
            foreach (var sounds in audio.Values) foreach (var source in sounds) if (source) { if (paused) source.Pause(); else source.UnPause(); }
            foreach (var effect in effects.Values) if (effect) foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>())
                if (paused) particles.Pause(); else particles.Play();
        }
        private void ReleaseAudio(AudioSource source)
        { if (!source) return; if (system) system.Audio.Unregister(source); source.Stop(); Release(source); }
        public void Dispose()
        {
            foreach (var sounds in audio.Values) foreach (var source in sounds) ReleaseAudio(source);
            foreach (var effect in effects.Values) Release(effect);
            audio.Clear(); effects.Clear(); hitRange.Dispose(); Release(Root);
        }
        private static void Release(UnityEngine.Object obj)
        { if (!obj) return; if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); }
    }
}
