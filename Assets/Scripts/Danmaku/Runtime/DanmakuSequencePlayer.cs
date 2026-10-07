using System;
using TapTap.Systems;
using UnityEngine;
namespace TapTap.Danmaku
{
    /// <summary>Fixed-player authoring preview host. Real combat is owned by BattleManager.</summary>
    [DisallowMultipleComponent]
    public sealed class DanmakuSequencePlayer : MonoBehaviour
    {
        public DanmakuSequence Sequence;
        public bool PlayOnEnable = true, Loop;
        [Range(0.1f, 4)] public float PlaybackSpeed = 1;
        public DanmakuSimulation Simulation { get; private set; }
        public bool Playing { get; private set; }
        public event Action<ProjectileHit> OnProjectileHit;
        private DanmakuSpritePool pool;
        private Transform viewRoot;
        private AudioSource source;
        private AudioVolumeService volume;
        private MusicTimelineTransport music;
        private readonly DanmakuTickClock clock = new();
        private void OnEnable() { if (PlayOnEnable && Sequence) Play(); }
        public void Play()
        {
            Stop(); if (!Sequence) return;
            Simulation = new DanmakuSimulation(Sequence); Simulation.OnHit += ForwardHit;
            var root = new GameObject("Danmaku Preview Views"); root.transform.SetParent(transform, false); viewRoot = root.transform;
            pool = new DanmakuSpritePool(viewRoot); pool.Sync(Simulation, Sequence);
            if (Sequence.Music)
            {
                source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
                volume = SystemRoot.Instance ? SystemRoot.Instance.Audio : null; volume?.Register(source, AudioChannel.Music);
                music = new MusicTimelineTransport(Sequence.Music, Sequence.MusicStartSeconds, new RuntimeMusicBackend(source)); music.Play();
            }
            Playing = true;
        }
        public void Pause() { Playing = false; clock.Reset(); music?.Pause(); }
        public void Resume()
        { if (Simulation == null || Simulation.Fault != null || Simulation.Finished) return; clock.Reset(); music?.Resume(); Playing = true; }
        public void Seek(int tick)
        {
            if (Simulation == null) Play(); if (Simulation == null) return;
            Pause(); Simulation.Seek(tick); music?.Seek(Simulation.Tick / 60.0); pool.Sync(Simulation, Sequence);
        }
        private void Update() => AdvanceFrame((double)Time.unscaledDeltaTime * Time.timeScale);
        public void AdvanceFrame(double deltaSeconds)
        {
            if (!Playing || Simulation == null || !Sequence) return;
            if (music != null) music.Advance(deltaSeconds);
            else clock.AddTime(deltaSeconds * Math.Max(0.1, Math.Min(4, PlaybackSpeed)));
            int budget = 240;
            while (Playing && budget-- > 0 && (music != null ? Simulation.Tick < music.TargetTick : clock.ConsumeTick()))
            {
                if (Simulation.Finished)
                {
                    if (Loop) { Simulation.Reset(Sequence); clock.Reset(); music?.Seek(0); music?.Resume(); break; }
                    Pause(); break;
                }
                Simulation.Step();
                if (Simulation.Fault != null) { Debug.LogError(Simulation.Fault, this); Pause(); break; }
            }
            if (Playing && music != null && Simulation.Tick < music.TargetTick)
            { Pause(); music.Seek(Simulation.Tick / 60.0); Debug.LogWarning("预览补步超过预算，已暂停。", this); }
            pool.Sync(Simulation, Sequence);
        }
        private void ForwardHit(ProjectileHit hit) => OnProjectileHit?.Invoke(hit);
        private void OnDisable() => Stop();
        private void Stop()
        {
            Pause(); if (Simulation != null) Simulation.OnHit -= ForwardHit;
            Simulation = null; music?.Dispose(); music = null;
            if (source) { volume?.Unregister(source); Release(source); } source = null; volume = null;
            pool?.Dispose(); pool = null; if (viewRoot) Release(viewRoot.gameObject); viewRoot = null;
        }
        private static void Release(UnityEngine.Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
