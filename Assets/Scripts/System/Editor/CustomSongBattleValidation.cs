using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TapTap.Danmaku;
using TapTap.Player;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Systems.Editor
{
    public static class CustomSongBattleValidation
    {
        [MenuItem("工具/共鸣舞台/验证生成与结算")]
        public static void Run()
        {
            var report = new List<string>(); var owned = new List<Object>();
            T Own<T>(T obj) where T : Object { owned.Add(obj); return obj; }
            void Require(bool condition, string description)
            { if (!condition) throw new InvalidOperationException(description); report.Add("PASS " + description); }
            string folder = Path.Combine(Path.GetTempPath(), "CustomSongValidation-" + Guid.NewGuid().ToString("N"));
            try
            {
                var system = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
                Require(system && system.CustomSongTemplate && system.Levels.Find(CustomSongChallengeService.EntryId)?.ScenePath == CustomSongChallengeService.ScenePath &&
                    EditorBuildSettings.scenes.Any(s => s.enabled && s.path == CustomSongChallengeService.ScenePath), "专用场景、模板、关卡入口、构建列表完整");
                var audio = Own(AudioClip.Create("SyntheticMinute", 8000 * 60, 1, 8000, false));
                var analysis = Own(ScriptableObject.CreateInstance<MusicAnalysisAsset>());
                analysis.SampleRate = 8000; analysis.SampleFrameCount = audio.samples; analysis.Channels = 1;
                analysis.Markers = Enumerable.Range(0, 49).Select(i => new MusicMarker { SampleFrame = (int)((2 + i * 1.1) * 8000), Band = i % 3, Strength = 1 }).ToArray();
                var first = Own(CustomSongSequenceGenerator.Generate("deterministic", audio, 120, .2, analysis, system.CustomSongTemplate));
                var second = Own(CustomSongSequenceGenerator.Generate("deterministic", audio, 120, .2, analysis, system.CustomSongTemplate));
                Require(JsonUtility.ToJson(first) == JsonUtility.ToJson(second), "同一歌曲及参数生成稳定片段 ID、种子和攻击");
                Require(first.Clips.Any(c => c.Name == "重音扩散") && first.Clips.Any(c => c.Name == "旋律追射") && first.Clips.Any(c => c.Name == "晶音能量球"), "三频候选分别映射圆环、瞄准扇形与能量球");
                Require(first.Clips.All(c => c.ValidateKeyframes() == null) && first.Events.Count == first.Clips.Count &&
                    first.Events.All(e => e.Tick < first.Clips[e.ClipIndex].StartTick), "生成关键帧合法且预警早于发射");
                Require(first.Clips.Zip(first.Clips.Skip(1), (a, b) => b.StartTick - a.StartTick >= 60).All(v => v), "主攻击至少间隔两拍且有恢复空档");
                Require(first.ManualMusicBpm == 120 && Mathf.Approximately(first.ManualMusicBeatOffsetSeconds, .2f) && first.DurationTicks == 3600,
                    "保存的 BPM、首拍和歌曲总时长进入正式战斗时间轴");
                analysis.Markers = Enumerable.Range(0, 100).Select(i => new MusicMarker { SampleFrame = (int)((2 + i * .5 - (i % 3 == 2 ? .003 : 0)) * 8000), Band = i % 3, Strength = 1 }).ToArray();
                var jitter = Own(CustomSongSequenceGenerator.Generate("jitter", audio, 120, 0, analysis, system.CustomSongTemplate));
                Require(jitter.Clips.Any(c => c.Name == "重音扩散") && jitter.Clips.Any(c => c.Name == "旋律追射") && jitter.Clips.Any(c => c.Name.StartsWith("晶音")),
                    "毫秒级起音抖动按实际 Tick 冷却，不锁死同一频段");
                analysis.Markers = Enumerable.Range(0, 100).SelectMany(i => new[] {
                    new MusicMarker { SampleFrame = (int)((2 + i * .5) * 8000), Band = 2, Strength = 1 },
                    new MusicMarker { SampleFrame = (int)((2 + i * .5) * 8000), Band = 0, Strength = .001f } }).ToArray();
                var leakage = Own(CustomSongSequenceGenerator.Generate("leakage", audio, 120, 0, analysis, system.CustomSongTemplate));
                Require(leakage.Clips.Count > 0 && leakage.Clips.All(c => c.Name.StartsWith("晶音")), "纯高频的微弱低频泄漏不会放大为重音攻击");
                analysis.Markers = Enumerable.Range(0, 6000).Select(i => new MusicMarker { SampleFrame = i * 80, Band = i % 3, Strength = 1 }).ToArray();
                var dense = Own(CustomSongSequenceGenerator.Generate("dense", audio, 300, 0, analysis, system.CustomSongTemplate));
                Require(dense.Clips.Count <= 80, "密集高 BPM 候选受到最小间隔和恢复段限制");
                var player = Own(Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlayerDefinition>("Assets/Player/MaidGhost.asset"))); player.MaxHealth = 10000;
                var battleConfig = Own(Object.Instantiate(system.CustomSongTemplate)); battleConfig.Sequence = dense;
                var battle = new BattleSimulation(battleConfig, player, new Vector2(0, -4));
                int maxActive = 0;
                while (!battle.Finished && battle.World.Fault == null) { battle.Step(default); maxActive = Math.Max(maxActive, battle.World.Bullets.Count); }
                Require(battle.World.Fault == null && battle.Finished && maxActive <= CustomSongSequenceGenerator.EnemyBudget,
                    "整曲模拟无容量故障，分裂子弹计入敌弹预算，峰值=" + maxActive);
                analysis.Markers = Array.Empty<MusicMarker>();
                var silent = Own(CustomSongSequenceGenerator.Generate("silence", audio, 120, 0, analysis, system.CustomSongTemplate));
                Require(silent.Clips.Count == 0, "静音不凭空生成攻击");
                var save = new GameSaveService(new JsonFileStore(folder));
                var result = new RunResult { IsCustom = true, RunId = "custom1", SongId = "custom:v1:test", Difficulty = SongDifficulty.Normal, Score = 1234, Grade = "完成" };
                Require(save.Settle(result).Success && save.Settle(result).Success && save.Record(result.SongId, result.Difficulty).Score == 1234 && save.Current.Items.Count == 0,
                    "自定义成绩保存和重复结算不发放道具");
                var restored = new GameSaveService(new JsonFileStore(folder));
                Require(restored.Record(result.SongId, result.Difficulty)?.Score == 1234, "自定义成绩重启后可读取");
                result.DropItemId = "invalid"; result.DropQuantity = 1;
                Require(!restored.Settle(result).Success, "自定义模式拒绝注入道具掉落");
                report.Add("ALL CUSTOM SONG VALIDATIONS PASSED");
                Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/CustomSongBattleValidation.txt", report);
                Debug.Log(string.Join("\n", report));
            }
            catch (Exception error)
            { Directory.CreateDirectory("Logs"); report.Add("FAIL " + error); File.WriteAllLines("Logs/CustomSongBattleValidation.txt", report); throw; }
            finally
            {
                foreach (var obj in owned) if (obj) Object.DestroyImmediate(obj);
                // Only files created by this fixture are removed; the test never writes player saves.
                foreach (var name in new[] { "game.json", "game.json.bak", "game.json.tmp" })
                { string path = Path.Combine(folder, name); if (File.Exists(path)) File.Delete(path); }
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }
        }
    }
}
