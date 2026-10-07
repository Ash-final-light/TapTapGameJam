using System;
using Inputs;
using TapTap.Systems;
using UnityEngine;
namespace TapTap.Danmaku
{
    [DisallowMultipleComponent]
    public sealed class BattleManager : MonoBehaviour, IPauseTarget, IHudDataSource
    {
        public BattleConfig Config;
        public Camera AimCamera;
        [Tooltip("独立场景明确指定现有采样器；存在 SystemRoot 时使用 SystemRoot.Input。")]
        public PlayerInputSampler StandaloneInput;
        public MonoBehaviour DamageConfigProvider;
        public bool PlayOnStart = true;
        public BattleSimulation Simulation { get; private set; }
        public bool IsRunning { get; private set; }
        public event Action<BattleEvent> Resolved;
        public event Action<HudSnapshot> Changed;
        private SystemRoot system;
        private bool previousExternalConsumer;
        private DanmakuSpritePool views;
        private Transform viewRoot;
        private AudioSource musicSource;
        private MusicTimelineTransport music;
        private readonly DanmakuTickClock clock = new();
        private bool syncCatchup;
        private double pausedMusicPosition;
        public HudSnapshot Current => new()
        {
            Portrait = Config ? Config.Player?.Sprite : null,
            Health = Simulation != null ? Mathf.CeilToInt(Simulation.Player.Health.ToFloat()) : 0,
            MaxHealth = Config ? Mathf.CeilToInt(Config.PlayerHealth) : 0,
            Warning = Simulation?.Finished == true ? (Simulation.Player.Alive ? "Boss 已击败" : "角色已倒下") : null
        };
        private void Start() { if (PlayOnStart) Play(); }
        public void Play()
        {
            Stop();
            if (!Config || Config.Validate() != null || !AimCamera)
            { Debug.LogError(Config ? Config.Validate() ?? "BattleManager 需要明确指定 AimCamera。" : "请指定 BattleConfig。", this); return; }
            system = SystemRoot.Instance;
            if (system && (!system.Initialized || system.Input == null))
            { Debug.LogError("SystemRoot 尚未初始化完成。", this); system = null; return; }
            if (!system && !StandaloneInput)
            { Debug.LogError("需要场景中的 SystemRoot，或明确指定现有 PlayerInputSampler；不会自动创建另一套输入。", this); return; }
            if (DamageConfigProvider && !(DamageConfigProvider is IDamageConfigResolver))
            { Debug.LogError("DamageConfigProvider 必须实现 IDamageConfigResolver。", this); return; }
            try { Simulation = new BattleSimulation(Config, DamageConfigProvider as IDamageConfigResolver); }
            catch (ArgumentException error) { Debug.LogError(error.Message, this); system = null; return; }
            var obj = new GameObject("Battle Views"); obj.transform.SetParent(transform, false); viewRoot = obj.transform;
            views = new DanmakuSpritePool(viewRoot); views.Sync(Simulation, Config);
            if (Config.Sequence.Music)
            {
                musicSource = gameObject.AddComponent<AudioSource>(); musicSource.playOnAwake = false;
                if (system) system.Audio.Register(musicSource, AudioChannel.Music);
                music = new MusicTimelineTransport(Config.Sequence.Music, Config.Sequence.MusicStartSeconds, new RuntimeMusicBackend(musicSource)); music.Play();
            }
            clock.Reset(); IsRunning = true;
            if (system)
            {
                previousExternalConsumer = system.ExternalCommandConsumer; system.ExternalCommandConsumer = true;
                system.AttachGameplay(this, null, null); system.Pause.Register(this);
            }
            Changed?.Invoke(Current);
        }
        private void Update() => AdvanceFrame(Time.unscaledDeltaTime);
        public void AdvanceFrame(double deltaSeconds)
        {
            if (!IsRunning || Simulation == null) return;
            if (!system) StandaloneInput.Capture();
            if (music != null && !syncCatchup) music.Advance(deltaSeconds);
            if (music == null) clock.AddTime(deltaSeconds);
            int budget = 240;
            while (budget-- > 0 && IsRunning && (music != null ? Simulation.Tick < music.TargetTick : clock.ConsumeTick()))
            {
                var input = system ? system.Input.CaptureBattle(AimCamera, transform) : StandaloneInput.ConsumeBattleTick(AimCamera, transform);
                if (system) system.ConsumeGameplayCommands();
                if (!IsRunning) break;
                Simulation.Step(input);
                foreach (var result in Simulation.Events) Resolved?.Invoke(result);
                if (Simulation.Events.Count > 0) Changed?.Invoke(Current);
                if (Simulation.World.Fault != null) { Debug.LogError(Simulation.World.Fault, this); Pause(); }
                else if (Simulation.Finished) Pause();
            }
            if (music != null && IsRunning)
            {
                if (!syncCatchup && Simulation.Tick < music.TargetTick)
                { music.Pause(); pausedMusicPosition = music.Seconds; syncCatchup = true; Debug.LogWarning("战斗补步超过预算，音乐暂停等待逻辑追齐。", this); }
                else if (syncCatchup && Simulation.Tick >= music.TargetTick)
                { music.Seek(pausedMusicPosition); music.Resume(); syncCatchup = false; }
            }
            views?.Sync(Simulation, Config);
        }
        public void Pause() { IsRunning = false; clock.Reset(); music?.Pause(); StandaloneInput?.DiscardButtons(); }
        public void Resume()
        { if (Simulation == null || Simulation.Finished || Simulation.World.Fault != null) return; clock.Reset(); if (!syncCatchup) music?.Resume(); IsRunning = true; }
        public void Stop()
        {
            Pause();
            if (system)
            { system.Pause.Unregister(this); system.DetachGameplay(this); system.ExternalCommandConsumer = previousExternalConsumer;
                if (musicSource) system.Audio.Unregister(musicSource); }
            music?.Dispose(); music = null; if (musicSource) ReleaseObject(musicSource); musicSource = null;
            views?.Dispose(); views = null; if (viewRoot) ReleaseObject(viewRoot.gameObject); viewRoot = null;
            Simulation = null; system = null; syncCatchup = false;
        }
        private void OnDisable() => Stop();
        private static void ReleaseObject(UnityEngine.Object obj)
        { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
