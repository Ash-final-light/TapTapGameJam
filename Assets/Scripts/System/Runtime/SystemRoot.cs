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
        public ProgressSaveService Saves { get; private set; }
        public AudioVolumeService Audio { get; private set; }
        public PauseService Pause { get; private set; }
        public SceneFlowService Scenes { get; private set; }
        public SystemUIController UI { get; private set; }
        public string CurrentEntryId { get; internal set; }
        internal bool PendingNewGame, PendingProgressRestore;
        private IHudDataSource source;
        private IProgressDataProvider progress;
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
            Saves = new ProgressSaveService(store, config.Levels); Scenes = new SceneFlowService(this);
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
            source = data; progress = provider; UI.BindHud(data, requests);
            if (PendingNewGame) { provider?.ResetNewGame(); PendingNewGame = false; }
            else if (PendingProgressRestore) { if (Saves.Current != null) provider?.Restore(Saves.Current.CharacterValues); PendingProgressRestore = false; }
        }
        public void DetachGameplay(IHudDataSource data)
        { if (!ReferenceEquals(source, data)) return; UI?.BindHud(null, null); source = null; progress = null; }
        public OperationResult SaveProgress()
        {
            if (Scenes.Busy || UI.Context != UIContext.Gameplay) return OperationResult.Fail("仅能在本局游戏中保存进度。");
            if (progress == null) return OperationResult.Fail("局外数据源尚未接入，无法保存进度。");
            var result = Saves.Save(CurrentEntryId ?? config.Levels.FirstEntryId, progress.Export()); UI.RefreshContinue(); return result;
        }
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
