using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Systems
{
    public sealed class SceneFlowService
    {
        private readonly SystemRoot root;
        public bool Busy { get; private set; }
        public bool Failed { get; private set; }
        public float Progress { get; private set; }
        public string Error { get; private set; }
        public event Action Changed;
        private bool newGame;
        private string entryId;
        public SceneFlowService(SystemRoot root) { this.root = root; }
        public OperationResult Begin(string id, bool fresh)
        {
            if (Busy) return OperationResult.Fail("关卡正在加载，请稍候。");
            entryId = id; newGame = fresh;
            Failed = false; Error = null; Progress = 0;
            var entry = root.Config.Levels.Find(id);
            if (entry == null || !Application.CanStreamedLevelBeLoaded(entry.ScenePath) || !Application.CanStreamedLevelBeLoaded(root.Config.LoadingScene))
            { Failed = true; Error = "关卡资源未配置或未加入构建，请检查关卡入口。"; Changed?.Invoke(); return OperationResult.Fail(Error); }
            if (fresh)
            {
                var cleared = root.Saves.Clear();
                if (!cleared.Success) { Failed = true; Error = cleared.Error; Changed?.Invoke(); return cleared; }
                root.UI.RefreshContinue();
            }
            Busy = true; root.StartCoroutine(Load(entry)); return OperationResult.Ok();
        }
        public OperationResult AbandonToTitle()
        {
            if (Busy) return OperationResult.Fail("场景正在切换，请稍候。");
            if (!Application.CanStreamedLevelBeLoaded(root.Config.TitleScene)) return OperationResult.Fail("标题场景未加入构建。");
            var cleared = root.Saves.Clear();
            if (!cleared.Success) return cleared;
            root.PendingNewGame = root.PendingProgressRestore = false;
            root.CurrentEntryId = null;
            root.Input.SetGameplay(false);
            root.UI.RefreshContinue();
            Busy = true; Failed = false; Error = null;
            root.StartCoroutine(ReturnToTitle());
            return OperationResult.Ok();
        }
        private IEnumerator ReturnToTitle()
        {
            var operation = TryLoad(root.Config.TitleScene);
            if (operation == null) { root.UI.Toast(Error); yield break; }
            while (!operation.isDone) yield return null;
            Busy = false; Progress = 0;
            root.UI.ShowContext(UIContext.Title);
        }
        public OperationResult Retry() => Begin(entryId, newGame);
        private IEnumerator Load(LevelEntry entry)
        {
            root.Input.SetGameplay(false); root.UI.ShowContext(UIContext.Loading); Changed?.Invoke();
            var transit = TryLoad(root.Config.LoadingScene);
            if(transit==null) yield break;
            while (!transit.isDone) yield return null;
            float started = Time.realtimeSinceStartup;
            var operation = TryLoad(entry.ScenePath);
            if(operation==null) yield break;
            operation.allowSceneActivation = false;
            bool timeout = false;
            while (operation.progress < 0.9f || Time.realtimeSinceStartup - started < root.Config.LoadingTips.MinimumSeconds)
            {
                Progress = Mathf.Clamp01(operation.progress / 0.9f) * 0.9f;
                if (!timeout && Time.realtimeSinceStartup - started > root.Config.LoadingTips.TimeoutSeconds)
                { timeout = true; Error = "加载时间较长，正在等待当前请求结束，请勿重复加载。"; }
                Changed?.Invoke(); yield return null;
            }
            root.PendingNewGame = newGame; root.PendingProgressRestore = !newGame;
            root.CurrentEntryId = entry.Id;
            operation.allowSceneActivation = true;
            while (!operation.isDone) yield return null;
            // Start callbacks in the destination scene run after activation.
            yield return null;
            Progress = 1; Busy = false; Error = null; Changed?.Invoke(); root.UI.ShowContext(UIContext.Gameplay);
        }
        private AsyncOperation TryLoad(string path)
        {
            try { return SceneManager.LoadSceneAsync(path,LoadSceneMode.Single) ?? throw new InvalidOperationException("无法创建加载请求。"); }
            catch(Exception e) { Debug.LogWarning("[Spotlight] "+e.Message);Busy=false;Failed=true;Error="关卡加载失败，请重试。";Changed?.Invoke();return null; }
        }
    }
}
