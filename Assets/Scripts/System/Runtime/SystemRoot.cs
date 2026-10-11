using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using TapTap.Systems.UI;

namespace TapTap.Systems
{
    [DefaultExecutionOrder(-1000)]
    public sealed class SystemRoot : MonoBehaviour
    {
        public static SystemRoot Instance { get; private set; }
        [SerializeField] private SystemConfig config;
        public SystemConfig Config => config;
        public SystemInputRouter Input { get; private set; }
        public BindingService Binding { get; private set; }
        public SettingsService Settings { get; private set; }
        public GameSaveService Saves { get; private set; }
        public Music.ImportedMusicLibrary Music { get; private set; }
        public BattleStartData ActiveRun { get; internal set; }
        public RunResult LastResult { get; internal set; }
        public AudioVolumeService Audio { get; private set; }
        public PauseService Pause { get; private set; }
        public SceneFlowService Scenes { get; private set; }
        public SystemUIController UI { get; private set; }
        public string CurrentEntryId { get; internal set; }
        private IHudDataSource source;
        public bool Initialized { get; private set; }
        public bool ExternalCommandConsumer { get; set; }
        public event Action<GameplayCommand> CommandConsumed;
        private void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            if (!config || !config.Controls || !config.Defaults || !config.Levels || !config.UIPrefab)
            { Debug.LogError("[Spotlight] SystemRoot 配置不完整。", this); enabled = false; return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            Input = new SystemInputRouter(config.Controls); Binding = new BindingService(Input.Controls);
            Audio = new AudioVolumeService(); Pause = new PauseService();
            var store = new JsonFileStore(System.IO.Path.Combine(Application.persistentDataPath, config.SaveFolder));
            Settings = new SettingsService(store, config.Defaults, Binding, Audio, new UnityDisplaySettings());
            Saves = new GameSaveService(store); Scenes = new SceneFlowService(this);
            Music = new Music.ImportedMusicLibrary(System.IO.Path.Combine(store.DirectoryPath, "ImportedMusic"));
            UI = Instantiate(config.UIPrefab, transform); UI.gameObject.SetActive(false); UI.Bind(this); UI.gameObject.SetActive(true);
            SceneManager.sceneLoaded += OnSceneLoaded; Application.focusChanged += OnFocus;
            Initialized = true;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
        private void Start()
        {
            if (!Initialized) return;
            if (Saves.Status == SaveStatus.Corrupt || Saves.Status == SaveStatus.UnsupportedVersion) UI.Toast("进度存档无效，已按无存档处理。");
            if (Settings.StartupWarning != null) UI.Toast(Settings.StartupWarning);
        }
        private void Update()
        {
            Input?.Update(Time.unscaledDeltaTime);
            if (!ExternalCommandConsumer) ConsumeGameplayCommands();
        }
        // The placeholder scene drains requests once per frame. A real runner sets
        // ExternalCommandConsumer and calls this at its authoritative tick boundary.
        public void ConsumeGameplayCommands()
        {
            if (Input == null) return;
            while (Input.Commands.TryConsume(out var command)) { UI.DispatchCommand(command); CommandConsumed?.Invoke(command); }
        }
        private void OnFocus(bool focus) { if (!focus) { Input?.Clear(); if (UI && UI.Context == UIContext.Gameplay) UI.OpenPause(); } }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!UI || Scenes.Busy) return;
            if (scene.path == config.TitleScene) UI.ShowContext(UIContext.Title);
            else if (scene.path == config.LoadingScene) UI.ShowContext(UIContext.Loading);
            else { CurrentEntryId ??= config.Levels.FirstEntryId; UI.ShowContext(UIContext.Gameplay); }
        }
        public void AttachGameplay(IHudDataSource data, IGameplayRequestSink requests, IProgressDataProvider provider)
        {
            source = data; UI.BindHud(data, requests); provider?.ResetNewGame();
        }
        public void DetachGameplay(IHudDataSource data)
        { if (!ReferenceEquals(source, data)) return; UI?.BindHud(null, null); source = null; }
        public OperationResult NewGame()
        {
            var result = Saves.NewGame();
            if (result.Success) { ActiveRun = null; LastResult = null; UI.RefreshContinue(); UI.OpenSongSelection(); }
            return result;
        }
        public void CompleteRun(RunResult result)
        {
            if (LastResult?.RunId == result.RunId) return;
            var drop = config.Drops.Pick(new System.Random()); result.DropItemId = drop.Item.Id; result.DropQuantity = drop.Quantity;
            LastResult = result;
            var saved = Saves.Settle(result);
            UI.ShowResult(result, saved);
        }
        public OperationResult RetryResultSave() => LastResult == null ? OperationResult.Fail("没有待保存的结算。") : Saves.Settle(LastResult);
        public void AbandonGame()
        {
            if (UI.Context != UIContext.Gameplay) return;
            var result = Scenes.AbandonToTitle();
            if (!result.Success) UI.Toast(result.Error);
        }
        public void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded; Application.focusChanged -= OnFocus;
            if (UI) UI.Unbind(); Input?.Dispose(); Instance = null;
        }
    }
}
