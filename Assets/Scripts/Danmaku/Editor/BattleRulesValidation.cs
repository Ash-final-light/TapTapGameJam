using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Framework.FixedPoint;
using TapTap.Player;
using TapTap.Systems;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Danmaku.Editor
{
    public static class BattleRulesValidation
    {
        [MenuItem("工具/弹幕/验证固定伤害与歌曲结算")]
        public static void Run()
        {
            DanmakuSimpleValidation.Run();
            BattleValidation.Run();
            DanmakuPlayerValidation.Run();
            TapTap.Player.Editor.PlayerSystemValidation.Run();
            TapTap.Systems.Editor.SpotlightRunValidation.Run();
            ValidateResultFlow();
            File.WriteAllText("Logs/BattleRulesValidation.txt",
                "PASS：固定1伤害、命中一次回收、Boss无血量且不挡子弹、能量球、预览与玩家动作、BPM与音乐传输、歌曲完成／角色倒下进入同一结算页面、直接运行场景的歌曲匹配、结算与存档只执行一次。\n");
        }
        public static void RunBatch()
        {
            try { Run(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void ValidateResultFlow()
        {
            Require(!SystemRoot.Instance, "请在未运行游戏时验证结算流程。");
            var owned = new List<Object>();
            T Make<T>() where T : ScriptableObject
            { var value = ScriptableObject.CreateInstance<T>(); owned.Add(value); return value; }
            GameObject rootObject = null, managerObject = null;
            try
            {
                var config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset")); owned.Add(config);
                config.SaveFolder = Path.Combine(Path.GetTempPath(), "TapTapBattleRules", Guid.NewGuid().ToString("N"));
                var battle = Make<BattleConfig>(); battle.Sequence = Make<DanmakuSequence>(); battle.Sequence.DurationTicks = 2;
                var song = Make<SongDefinition>(); song.Id = "settlement-test"; song.Title = "结算验证";
                song.Difficulties = new[] { new SongDifficultyConfig { Battle = battle, Rating = Make<ThresholdRatingFormula>() } };
                config.Songs = Make<SongCatalog>(); config.Songs.Songs = new[] { song };
                var player = Make<PlayerDefinition>(); player.Actions = Make<PlayerActionLibrary>();
                player.Weapon = Make<PlayerWeaponDefinition>(); player.Weapon.Projectile = Make<ProjectileDefinition>();
                player.Motor.Gravity = 0; player.Motor.GroundHeight = 0; player.MaxHealth = 1;
                config.Weapons = new[] { player.Weapon };
                rootObject = new GameObject("Result flow validation"); rootObject.SetActive(false);
                var root = rootObject.AddComponent<SystemRoot>();
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(SystemRoot).GetField("config", flags).SetValue(root, config);
                // Wire the real services without executing play-mode-only DontDestroyOnLoad.
                void SetRoot(string property, object value) => typeof(SystemRoot).GetField("<" + property + ">k__BackingField", flags).SetValue(root, value);
                typeof(SystemRoot).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, root);
                SetRoot("Input", new SystemInputRouter(config.Controls));
                SetRoot("Binding", new BindingService(root.Input.Controls));
                SetRoot("Audio", new AudioVolumeService()); SetRoot("Pause", new PauseService());
                var store = new JsonFileStore(config.SaveFolder);
                SetRoot("Saves", new GameSaveService(store)); SetRoot("Scenes", new SceneFlowService(root));
                SetRoot("Settings", new TapTap.Systems.SettingsService(store, config.Defaults, root.Binding, root.Audio, new Display()));
                SetRoot("UI", Object.Instantiate(config.UIPrefab, rootObject.transform));
                root.UI.Bind(root); SetRoot("Initialized", true);
                managerObject = new GameObject("Battle result validation"); managerObject.SetActive(false);
                var manager = managerObject.AddComponent<BattleManager>(); manager.PlayOnStart = false;
                manager.Config = battle; manager.PlayerDefinition = player; manager.PlayerSpawn = Vector2.zero;
                manager.AimCamera = managerObject.AddComponent<Camera>();
                int notifications = 0; manager.RunFinished += _ => notifications++;
                root.UI.ShowContext(UIContext.Gameplay);
                manager.Play();
                Require(manager.IsRunning && root.ActiveRun?.Song == song, "直接运行场景未匹配歌曲上下文。");
                manager.AdvanceFrame(1.0 / 60); Require(manager.Result == null, "时间轴未结束就结算。");
                manager.AdvanceFrame(1.0 / 60);
                Verify(RunEndReason.Completed, "歌曲完成");
                var first = manager.Result; int itemsAfterFirst = root.Saves.Quantity(first.DropItemId);
                manager.AdvanceFrame(10); manager.Resume(); manager.AdvanceFrame(10);
                Require(ReferenceEquals(manager.Result, first) && notifications == 1 && root.Saves.Quantity(first.DropItemId) == itemsAfterFirst,
                    "结束后重复推进产生了二次结算／奖励。");

                root.UI.ShowContext(UIContext.Gameplay); manager.Play();
                manager.Simulation.PlayerSystem.ReceiveDamage(FP64.One); manager.AdvanceFrame(0);
                Verify(RunEndReason.PlayerDied, "角色倒下");
                Require(notifications == 2, "角色倒下未通知一次结算。");

                // Audio can end between fixed ticks and before a longer authored timeline.
                var clip = AudioClip.Create("Settlement final audio sample", 101, 1, 6000, false); owned.Add(clip);
                battle.Sequence.Music = clip; battle.Sequence.DurationTicks = 600;
                root.UI.ShowContext(UIContext.Gameplay); manager.Play();
                var musicField = typeof(BattleManager).GetField("music", flags);
                ((MusicTimelineTransport)musicField.GetValue(manager)).Dispose();
                var backend = new Playback(); var transport = new MusicTimelineTransport(clip, 0, backend);
                musicField.SetValue(manager, transport); transport.Play(); backend.SamplePosition = 100;
                manager.AdvanceFrame(0); Require(manager.Result == null && manager.Simulation.Tick == 1, "音频最后样本播放前提前结算。");
                backend.IsPlaying = false; manager.AdvanceFrame(0);
                Verify(RunEndReason.Completed, "歌曲完成");
                Require(notifications == 3 && manager.Simulation.Tick == 2 && !manager.Simulation.BossTimeline.Finished,
                    "音乐结束未进入结算，或错误等待弹幕时间轴结束。");
                File.WriteAllText("Logs/BattleResultFlowValidation.txt",
                    "PASS：真实 SystemRoot／BattleManager／ResultView；直接运行场景匹配歌曲；时间轴结束与角色倒下共用结算；非整帧音频结束提前于时间轴时结算；保存成功；结束原因显示；只发出一次结算事件且不重复发奖。使用独立临时存档。\n");

                void Verify(RunEndReason reason, string label)
                {
                    Require(manager.Result != null && manager.Result.Stats.EndReason == reason && !manager.IsRunning,
                        "结束原因或停止战斗状态错误。");
                    Require(root.LastResult == manager.Result && root.UI.Context == UIContext.Result && root.UI.Result.gameObject.activeSelf,
                        "没有进入统一歌曲结算页面。");
                    Require(root.UI.Result.Statistics.text.Contains(label) && root.Saves.Status == SaveStatus.Valid,
                        "结算原因文案或存档失败。");
                }
            }
            finally
            {
                if (managerObject) { managerObject.GetComponent<BattleManager>().Stop(); Object.DestroyImmediate(managerObject); }
                if (rootObject)
                {
                    var root = rootObject.GetComponent<SystemRoot>();
                    if (root.Input != null) { root.Input.Controls.Disable(); Object.DestroyImmediate(root.Input.Controls); }
                    if (root.UI)
                    {
                        var references = typeof(TapTap.Systems.UI.SystemUIController).GetField("references", BindingFlags.NonPublic | BindingFlags.Instance)
                            .GetValue(root.UI) as UnityEngine.InputSystem.InputActionReference[];
                        if (references != null) foreach (var reference in references) if (reference) Object.DestroyImmediate(reference);
                    }
                    typeof(SystemRoot).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
                    Object.DestroyImmediate(rootObject);
                }
                foreach (var value in owned) if (value) Object.DestroyImmediate(value);
            }
        }
        private sealed class Playback : IMusicPlaybackBackend
        {
            public bool IsPlaying { get; set; }
            public bool HasStarted { get; private set; }
            public int SamplePosition { get; set; }
            public void Play(AudioClip clip, int sample) { HasStarted = true; IsPlaying = true; SamplePosition = sample; }
            public void Pause() => IsPlaying = false;
            public void Resume() => IsPlaying = true;
            public void Seek(int sample) => SamplePosition = sample;
            public void Stop() { HasStarted = false; IsPlaying = false; }
            public void Dispose() => Stop();
        }
        private sealed class Display : IDisplaySettings
        {
            public bool Supports(int width, int height, DisplayMode mode) => true;
            public void Apply(SettingsData data) { }
            public void CaptureActual(SettingsData data) { }
        }
    }
}
