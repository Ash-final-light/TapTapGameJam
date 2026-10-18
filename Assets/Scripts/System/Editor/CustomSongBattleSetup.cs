using System;
using System.IO;
using System.Linq;
using TapTap.Danmaku;
using TapTap.Systems.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Systems.Editor
{
    public static class CustomSongBattleSetup
    {
        [MenuItem("工具/共鸣舞台/接入自定义歌曲战斗")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 PlayMode。");
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            if (!config || !config.Levels) throw new InvalidOperationException("SystemConfig 配置缺失。");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景修改。");
            try
            {
                Scene scene;
                if (!File.Exists(CustomSongChallengeService.ScenePath))
                {
                    scene = EditorSceneManager.OpenScene(BossRhythmSceneSetup.ScenePath, OpenSceneMode.Single);
                    if (!EditorSceneManager.SaveScene(scene, CustomSongChallengeService.ScenePath)) throw new IOException("无法保存共鸣舞台。");
                }
                else scene = EditorSceneManager.OpenScene(CustomSongChallengeService.ScenePath, OpenSceneMode.Single);
                var battles = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<BattleManager>(true)).ToArray();
                if (battles.Length != 1) throw new InvalidOperationException("共鸣舞台需要唯一 BattleManager。");
                var manager = battles[0]; manager.PlayOnStart = false;
                manager.BossScoring.Enabled = true;
                var presentation = manager.GetComponent<CustomSongBattlePresentation>();
                if (!presentation) presentation = manager.gameObject.AddComponent<CustomSongBattlePresentation>();
                presentation.Battle = manager;
                config.CustomSongTemplate = manager.Config;
                var entries = config.Levels.Entries.ToList();
                var entry = entries.Find(e => e != null && e.Id == CustomSongChallengeService.EntryId);
                if (entry == null) { entry = new LevelEntry { Id = CustomSongChallengeService.EntryId }; entries.Add(entry); }
                entry.ScenePath = CustomSongChallengeService.ScenePath; entry.DisplayName = "共鸣舞台";
                config.Levels.Entries = entries.ToArray(); EditorUtility.SetDirty(config.Levels); EditorUtility.SetDirty(config);
                var build = EditorBuildSettings.scenes.ToList();
                var existing = build.Find(s => s.path == CustomSongChallengeService.ScenePath);
                if (existing == null) build.Add(new EditorBuildSettingsScene(CustomSongChallengeService.ScenePath, true)); else existing.enabled = true;
                EditorBuildSettings.scenes = build.ToArray();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/CustomSongBattleSetup.txt", "PASS custom scene, template, level entry and build settings\n");
            }
            finally
            {
                // A fresh batch editor starts with an untitled scene. Unity cannot
                // restore that path through RestoreSceneManagerSetup.
                if (setup.Length > 0 && setup.All(item => !string.IsNullOrEmpty(item.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
        public static void RunBatch()
        {
            try { Install(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
