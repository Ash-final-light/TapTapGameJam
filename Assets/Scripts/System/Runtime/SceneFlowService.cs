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
        public SceneFlowService(SystemRoot root) { this.root = root; }
        public OperationResult StartRun(BattleStartData setup)
        {
            if (Busy) return OperationResult.Fail("关卡正在加载，请稍候。");
            if (setup == null) return OperationResult.Fail("本局配置缺失。");
            string error = setup.Validate(root.Config, root.Saves);
            if (error != null) return OperationResult.Fail(error);
            var previous = root.ActiveRun; root.ActiveRun = setup;
            var result = Begin(setup.Difficulty.LevelEntryId);
            if (!result.Success) root.ActiveRun = previous;
            else root.LastResult = null;
            return result;
        }
        public OperationResult RetryRun() => StartRun(root.ActiveRun);
        private OperationResult Begin(string id)
        {
            if (Busy) return OperationResult.Fail("关卡正在加载，请稍候。");
            Failed = false; Error = null; Progress = 0;
            var entry = root.Config.Levels.Find(id);
            if (entry == null || !Application.CanStreamedLevelBeLoaded(entry.ScenePath) || !Application.CanStreamedLevelBeLoaded(root.Config.LoadingScene))
            { Failed = true; Error = "关卡资源未配置或未加入构建，请检查关卡入口。"; Changed?.Invoke(); return OperationResult.Fail(Error); }
            Busy = true; root.StartCoroutine(Load(entry)); return OperationResult.Ok();
        }
        public OperationResult AbandonToTitle()
            => ReturnToMenu(false);
        public OperationResult AbandonToMusic()
            => ReturnToMenu(true);
        private OperationResult ReturnToMenu(bool music)
        {
            if (Busy) return OperationResult.Fail("场景正在切换，请稍候。");
            if (!Application.CanStreamedLevelBeLoaded(root.Config.TitleScene)) return OperationResult.Fail("标题场景未加入构建。");
            root.CurrentEntryId = null;
            root.Input.SetGameplay(false);
            root.UI.RefreshContinue();
            Busy = true; Failed = false; Error = null;
            root.StartCoroutine(ReturnToTitle(music));
            return OperationResult.Ok();
        }
        private IEnumerator ReturnToTitle(bool music)
        {
            var operation = TryLoad(root.Config.TitleScene);
            if (operation == null) { root.UI.Toast(Error); yield break; }
            while (!operation.isDone) yield return null;
            Busy = false; Progress = 0;
            root.CustomSongs.ReleasePrepared();
            root.UI.ShowContext(music ? UIContext.ImportedMusic : UIContext.Title);
        }
        public OperationResult Retry() => RetryRun();
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
            root.CurrentEntryId = entry.Id;
            operation.allowSceneActivation = true;
            while (!operation.isDone) yield return null;
            // Start callbacks in the destination scene run after activation.
            yield return null;
            Progress = 1; Busy = false; Error = null; Changed?.Invoke(); root.UI.ShowContext(root.LastResult == null ? UIContext.Gameplay : UIContext.Result);
        }
        private AsyncOperation TryLoad(string path)
        {
            try { return SceneManager.LoadSceneAsync(path,LoadSceneMode.Single) ?? throw new InvalidOperationException("无法创建加载请求。"); }
            catch(Exception e) { Debug.LogWarning("[Spotlight] "+e.Message);Busy=false;Failed=true;Error="关卡加载失败，请重试。";Changed?.Invoke();return null; }
        }
    }
}
