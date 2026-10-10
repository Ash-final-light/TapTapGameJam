using System;
using Inputs;
using TapTap.Systems;
using TapTap.Player;
using UnityEngine;
namespace TapTap.Danmaku
{
    [DisallowMultipleComponent]
    public sealed class BattleManager : MonoBehaviour, IPauseTarget, IHudDataSource
    {
        public BattleConfig Config;
        public PlayerDefinition PlayerDefinition;
        public Vector2 PlayerSpawn = new(0, -4);
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
        private PlayerSpineView playerView;
        private Transform viewRoot;
        private AudioSource musicSource;
        private MusicTimelineTransport music;
        private readonly DanmakuTickClock clock = new();
        private bool syncCatchup;
        private double pausedMusicPosition;
        private ScoreManager scores;
        private ItemRuntime items;
        private string runId;
        private BattleStartData setup;
        private int missedThisTick;
        public event Action<RunResult> RunFinished;
        public RunResult Result { get; private set; }
        public HudSnapshot Current => new()
        {
            Portrait = PlayerDefinition ? PlayerDefinition.Portrait : null,
            Health = Simulation != null ? Mathf.CeilToInt(Simulation.Player.Health.ToFloat()) : 0,
            MaxHealth = Simulation != null ? Mathf.CeilToInt(Simulation.Player.MaxHealth.ToFloat()) : 0,
            Stamina = Simulation?.PlayerSystem.Stamina.ToFloat() ?? 0,
            MaxStamina = Simulation?.PlayerSystem.MaxStamina.ToFloat() ?? 0,
            Skill1Color = PlayerDefinition ? PlayerDefinition.Skill1Color : Color.white,
            Skill2Color = PlayerDefinition ? PlayerDefinition.Skill2Color : Color.white,
            Items = items?.Snapshot() ?? Array.Empty<ItemViewData>(), SelectedItemId = items?.SelectedId,
            Warning = Simulation?.Finished == true ? (Simulation.Player.Alive ? "歌曲完成" : "角色已倒下") : null
        };
        private void Start() { if (PlayOnStart) Play(); }
        public void Play()
        {
            Stop();
            setup = SystemRoot.Instance ? SystemRoot.Instance.ActiveRun : null;
            if (setup != null) Config = setup.Difficulty.Battle;
            if (!Config || Config.Validate() != null || !PlayerDefinition || PlayerDefinition.Validate() != null || !AimCamera)
            { Debug.LogError(!Config ? "请指定 BattleConfig。" : Config.Validate() ?? (!PlayerDefinition ? "请指定独立 PlayerDefinition。" : PlayerDefinition.Validate()) ?? "BattleManager 需要明确指定 AimCamera。", this); return; }
            system = SystemRoot.Instance;
            if (system && (!system.Initialized || system.Input == null))
            { Debug.LogError("SystemRoot 尚未初始化完成。", this); system = null; return; }
            if (!system && !StandaloneInput)
            { Debug.LogError("需要场景中的 SystemRoot，或明确指定现有 PlayerInputSampler；不会自动创建另一套输入。", this); return; }
            if (DamageConfigProvider && !(DamageConfigProvider is IDamageConfigResolver))
            { Debug.LogError("DamageConfigProvider 必须实现 IDamageConfigResolver。", this); return; }
            try { Simulation = new BattleSimulation(Config, PlayerDefinition, PlayerSpawn, DamageConfigProvider as IDamageConfigResolver, setup?.Weapon); }
            catch (ArgumentException error) { Debug.LogError(error.Message, this); system = null; return; }
            var obj = new GameObject("Battle Views"); obj.transform.SetParent(transform, false); viewRoot = obj.transform;
            views = new DanmakuSpritePool(viewRoot); views.Sync(Simulation, Config);
            playerView = new PlayerSpineView(PlayerDefinition, viewRoot, system); playerView.Sync(Simulation.PlayerSystem);
            if (Config.Sequence.Music)
            {
                musicSource = gameObject.AddComponent<AudioSource>(); musicSource.playOnAwake = false;
                if (system) system.Audio.Register(musicSource, AudioChannel.Music);
                music = new MusicTimelineTransport(Config.Sequence.Music, Config.Sequence.MusicStartSeconds, new RuntimeMusicBackend(musicSource)); music.Play();
            }
            clock.Reset(); IsRunning = true;
            Result = null; runId = Guid.NewGuid().ToString("N"); missedThisTick = 0;
            scores = setup == null ? null : new ScoreManager(system.Config.ScoreFormulas);
            Simulation.World.EnergyOrbMissed += OnOrbMissed;
            items = setup == null ? null : new ItemRuntime(setup.Items, Simulation.Buffs, () => Simulation.Tick);
            if (system)
            {
                previousExternalConsumer = system.ExternalCommandConsumer; system.ExternalCommandConsumer = true;
                system.AttachGameplay(this, items, null); system.Pause.Register(this);
            }
            Changed?.Invoke(Current);
        }
        private void Update()
        {
            AdvanceFrame(Time.unscaledDeltaTime);
            if (!IsRunning && Simulation?.Finished == true && !Simulation.Player.Alive) playerView?.AdvanceTerminal(Time.unscaledDeltaTime);
        }
        public void AdvanceFrame(double deltaSeconds)
        {
            if (!IsRunning || Simulation == null) return;
            if (!system)
            {
                if (!StandaloneInput) { Pause(); return; }
                StandaloneInput.Capture();
            }
            if (music != null && !syncCatchup) music.Advance(deltaSeconds);
            if (music == null) clock.AddTime(deltaSeconds);
            int budget = 240;
            while (budget-- > 0 && IsRunning && (music != null ? Simulation.Tick < music.TargetTick : clock.ConsumeTick()))
            {
                var input = system ? system.Input.CaptureBattle(AimCamera, transform) : StandaloneInput.ConsumeBattleTick(AimCamera, transform);
                var playerInput = system ? system.Input.CapturePlayerTick() : new PlayerTickInput(input.Movement.MoveX, fireHeld: input.Movement.FireHeld);
                if (!IsRunning) break;
                Simulation.Step(input, playerInput);
                playerView?.SetPosition(Simulation.PlayerSystem);
                playerView?.ConsumeEvents(Simulation.PlayerSystem);
                // UI/item adapters are notified only after the player resolver accepts the request.
                foreach (var e in Simulation.PlayerSystem.Events)
                    if (system && e.Kind == PlayerRuntimeEventKind.IntentExecuted && e.Intent == PlayerIntentKind.UseItem) system.UI.DispatchCommand(GameplayCommand.UseItem);
                foreach (var result in Simulation.Events) { scores?.Consume(result); Resolved?.Invoke(result); }
                while (missedThisTick > 0) { scores?.MissOrb(); missedThisTick--; }
                Changed?.Invoke(Current);
                if (Simulation.World.Fault != null) { Debug.LogError(Simulation.World.Fault, this); Pause(); }
                else if (Simulation.Finished)
                {
                    Pause(); playerView?.SetPaused(false);
                    if (scores != null && Result == null)
                    {
                        Result = scores.Finish(runId, setup, Simulation.Tick, Simulation.Player.Alive ? RunEndReason.Completed : RunEndReason.PlayerDied);
                        RunFinished?.Invoke(Result); system.CompleteRun(Result);
                    }
                }
            }
            if (music != null && IsRunning)
            {
                if (!syncCatchup && Simulation.Tick < music.TargetTick)
                { music.Pause(); pausedMusicPosition = music.Seconds; syncCatchup = true; Debug.LogWarning("战斗补步超过预算，音乐暂停等待逻辑追齐。", this); }
                else if (syncCatchup && Simulation.Tick >= music.TargetTick)
                { music.Seek(pausedMusicPosition); music.Resume(); syncCatchup = false; }
            }
            views?.Sync(Simulation, Config);
            playerView?.Sync(Simulation.PlayerSystem);
        }
        private void OnOrbMissed(int id) { missedThisTick++; }
        public void Pause() { IsRunning = false; clock.Reset(); music?.Pause(); StandaloneInput?.DiscardButtons(); Simulation?.PlayerSystem.ClearInput(); system?.Input.Clear(); playerView?.SetPaused(true); }
        public void Resume()
        { if (Simulation == null || Simulation.Finished || Simulation.World.Fault != null) return; clock.Reset(); if (!syncCatchup) music?.Resume(); playerView?.SetPaused(false); IsRunning = true; }
        public void Stop()
        {
            Pause();
            if (Simulation != null) Simulation.World.EnergyOrbMissed -= OnOrbMissed;
            if (system)
            { system.Pause.Unregister(this); system.DetachGameplay(this); system.ExternalCommandConsumer = previousExternalConsumer;
                if (musicSource) system.Audio.Unregister(musicSource); }
            music?.Dispose(); music = null; if (musicSource) ReleaseObject(musicSource); musicSource = null;
            playerView?.Dispose(); playerView = null;
            views?.Dispose(); views = null; if (viewRoot) ReleaseObject(viewRoot.gameObject); viewRoot = null;
            Simulation = null; system = null; syncCatchup = false; items = null; scores = null; setup = null;
        }
        private void OnDisable() => Stop();
        private static void ReleaseObject(UnityEngine.Object obj)
        { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
