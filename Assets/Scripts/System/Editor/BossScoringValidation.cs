using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using Inputs;
using TapTap.Danmaku;
using TapTap.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Systems.Editor
{
    public static class BossScoringValidation
    {
        private sealed class Fixture : IDisposable
        {
            private readonly List<Object> owned = new();
            public readonly DanmakuSequence Sequence;
            public readonly BattleConfig Config;
            public readonly PlayerDefinition Player;
            public readonly BossScoringSettings Scoring = new() { Enabled = true };
            public Fixture()
            {
                Sequence = Make<DanmakuSequence>(); Sequence.FieldSize = new Vector2(100, 100);
                Sequence.DurationTicks = 1800; Sequence.ManualMusicBpm = 120; Sequence.ManualMusicBeatOffsetSeconds = 0.2f;
                Config = Make<BattleConfig>(); Config.Sequence = Sequence;
                Player = Make<PlayerDefinition>(); Player.Actions = Make<PlayerActionLibrary>();
                Player.Weapon = Make<PlayerWeaponDefinition>(); Player.Weapon.Projectile = Make<ProjectileDefinition>();
                Player.Motor.Gravity = 0; Player.Motor.GroundHeight = 0;
            }
            public T Make<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); owned.Add(value); return value; }
            public void Dispose() { foreach (var value in owned) if (value) Object.DestroyImmediate(value); }
            public ScoreManager Scores(ScoreFormula[] formulas = null) => new(formulas, Scoring, Sequence);
            public BattleSimulation Battle() => new(Config, Player, Vector2.zero, null, Scoring);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static BattleEvent Hit(int fireTick, int hitTick) => new(hitTick, new ProjectileState { FireTick = fireTick }, FPVector2.Zero);

        [MenuItem("工具/战斗计分/验证 Boss 连击与节拍")]
        public static void Run()
        {
            var report = new List<string>();
            void Check(string name, Action action) { action(); report.Add("PASS " + name); }
            try
            {
                Check("开火时刻判定：踩拍发射、延迟命中；非踩拍发射、踩拍命中不奖励", FireTime);
                Check("基础分、节拍奖励、能量球与 MISS 独立、受伤断连、两拍边界、结算锁定", Scoring);
                Check("首拍、音乐开始偏移、提前窗口、半拍、缺失或无效 BPM 回退 60、歌曲结束", BeatOffsets);
                Check("BattleManager 使用实际 Sequence 的 BPM，未填 BPM 时按 60 判定与断连", BpmConfiguration);
                Check("高速扫掠命中 Boss、命中即回收、仅一次事件、能量球先命中挡住子弹", Contacts);
                Check("实际 BattleManager 暂停冻结、恢复续算、重开清空、独立场景结算", PauseAndRestart);
                Check("已保存场景与 BattleManager 配置绑定、HUD 引用、无输入遮挡", SceneWiring);
                TapTap.Danmaku.Editor.BattleValidation.Run(); report.Add("PASS existing battle regression");
                SpotlightRunValidation.Run(); report.Add("PASS existing scoring/save/loadout regression");
            }
            catch (Exception error) { report.Add("FAIL " + error); throw; }
            finally { Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/BossScoringValidation.txt", report); }
            Debug.Log(string.Join("\n", report));
        }

        private static void FireTime()
        {
            using var f = new Fixture(); var scores = f.Scores();
            scores.Consume(Hit(12, 30));
            Require(scores.CurrentScore == 150 && scores.Stats.OnBeatHits == 1, "踩拍发射、非踩拍命中未获得奖励。");
            scores.Consume(Hit(30, 42));
            Require(scores.CurrentScore == 250 && scores.Stats.OnBeatHits == 1 && scores.Stats.CurrentCombo == 2,
                "判定错误使用命中时刻，或非踩拍命中断连。");
            Require(f.Scores().CurrentScore == 0, "没有命中也获得分数。");
        }

        private static void Scoring()
        {
            using var f = new Fixture();
            var scores = f.Scores(new ScoreFormula[] { f.Make<OrbScoreFormula>(), f.Make<MissScoreFormula>(),
                f.Make<ComboScoreFormula>(), f.Make<CompletionScoreFormula>() });
            Require(scores.CurrentScore == 0, "战斗开始就获得完成奖励。");
            scores.Consume(Hit(12, 40)); scores.Consume(Hit(30, 42)); scores.MissOrb();
            Require(scores.CurrentScore == 200 && scores.Stats.CurrentCombo == 2, "漏球错误清空 Boss 连击。");
            scores.Consume(new BattleEvent(BattleEventType.EnergyOrbDestroyed, 43, new ProjectileState(), new ProjectileState(), FP64.One, 1));
            Require(scores.CurrentScore == 300 && scores.Stats.CurrentCombo == 2, "能量球混入 Boss 连击。");
            var player = new ActorState(1, FP64.FromInt(5), default);
            scores.Consume(new BattleEvent(BattleEventType.Damage, 44, player, new ProjectileState(), FP64.One, CombatTargetKind.Player));
            Require(scores.Stats.CurrentCombo == 0 && scores.LastFeedback == ScoreFeedback.ComboBroken, "有效受伤未断连。");
            scores.Consume(Hit(54, 65)); scores.Advance(124);
            Require(scores.Stats.CurrentCombo == 1 && scores.ComboRemaining > 0, "两拍到期前提前断连。");
            scores.Advance(125); Require(scores.Stats.CurrentCombo == 0 && scores.ComboRemaining == 0, "到期未断连。");
            var result = scores.Finish("boss-test", null, 126, RunEndReason.Completed);
            Require(result.Score == 1400 && result.Stats.MaxCombo == 2 && result.Stats.BossHits == 3 && result.Stats.OnBeatHits == 1,
                "最终成绩重复加分、统计错误或引入连击奖励。");
            Require(result.Breakdown.Any(b => b.Name == "Boss 命中" && b.Score == 300) &&
                result.Breakdown.Any(b => b.Name == "节拍奖励" && b.Score == 50), "结算缺少 Boss/节拍明细。");
            scores.Consume(Hit(72, 150)); scores.MissOrb(); scores.Advance(1000);
            Require(ReferenceEquals(result, scores.Finish("other", null, 200, RunEndReason.PlayerDied)) && scores.CurrentScore == 1400,
                "结算后继续计分或重复结算。");
        }

        private static void BeatOffsets()
        {
            using var f = new Fixture(); f.Sequence.MusicStartSeconds = 0.5f;
            var clock = new BattleBeatClock(f.Sequence);
            Require(clock.IsOnBeat(42, 0.1) && clock.IsOnBeat(36, 0.1) && !clock.IsOnBeat(35, 0.1), "首拍或提前窗口错误。");
            Require(!clock.IsOnBeat(57, 0.1), "半拍被当作主拍。");
            f.Sequence.MusicStartSeconds = -0.1f;
            Require(new BattleBeatClock(f.Sequence).IsOnBeat(6, 0.1), "从歌曲中段开始时偏移错误。");
            foreach (float bpm in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 1001f })
            {
                f.Sequence.ManualMusicBpm = bpm; var fallback = new BattleBeatClock(f.Sequence);
                Require(fallback.Valid && fallback.Bpm == 60 && fallback.PeriodSeconds == 1, "无效 BPM 未回退 60。");
                Require(fallback.FirstBeatSeconds == f.Sequence.ManualMusicBeatOffsetSeconds && fallback.IsOnBeat(6, 0.1),
                    "回退 BPM 丢失首拍或音乐开始偏移。");
            }
            Require(new BattleBeatClock(null).Bpm == 60 && new BattleBeatClock(null).IsBeatInSong(4), "没有 Sequence 时未使用 60 BPM。");
            f.Sequence.ManualMusicBpm = 120; f.Sequence.MusicStartSeconds = 0;
            Require(!new BattleBeatClock(f.Sequence).IsOnBeat(1800, 0.1), "歌曲结束后仍发放节拍奖励。");
        }

        public static void RunBpmRegression()
        {
            try
            {
                BeatOffsets(); BpmConfiguration(); FireTime(); Scoring();
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/BossBpmValidation.txt", "PASS: configured BPM from BattleManager; 60 BPM fallback; fire-time bonus; two-beat timeout; music/first-beat offsets; original scoring regression.\n");
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void BpmConfiguration()
        {
            using var f = new Fixture(); var go = new GameObject("Battle BPM validation"); go.SetActive(false);
            var input = new GameObject("Battle BPM inactive input"); input.SetActive(false);
            try
            {
                var manager = go.AddComponent<BattleManager>(); manager.Config = f.Config; manager.PlayerDefinition = f.Player;
                manager.BossScoring = f.Scoring; manager.AimCamera = go.AddComponent<Camera>(); manager.PlayOnStart = false;
                manager.StandaloneInput = input.AddComponent<PlayerInputSampler>(); manager.PlayerSpawn = Vector2.zero;
                f.Sequence.ManualMusicBpm = 90; manager.Play();
                Require(manager.IsRunning && manager.Scores.Beats.Bpm == 90, "未使用 BattleManager 指定的 Sequence BPM。");
                manager.Stop(); f.Sequence.ManualMusicBpm = 0; manager.Play();
                Require(manager.IsRunning && manager.Scores.Beats.Bpm == 60, "未填 BPM 导致战斗无法启动或回退错误。");
                manager.Scores.Consume(Hit(12, 20)); Require(manager.Scores.CurrentScore == 150, "60 BPM 回退未用于节拍奖励。");
                manager.Scores.Advance(139); Require(manager.Scores.Stats.CurrentCombo == 1, "60 BPM 两拍断连提前触发。");
                manager.Scores.Advance(140); Require(manager.Scores.Stats.CurrentCombo == 0, "60 BPM 两拍断连未在两秒触发。");
                manager.Stop();
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(input); }
        }

        private static void Contacts()
        {
            using var f = new Fixture(); var battle = f.Battle();
            for (int i = 0; i < 12; i++) battle.Step(default);
            battle.World.SpawnFriendly(f.Player.Weapon.Projectile.Settings, FPVector2.Zero, FPMath.PI_OVER_2, FP64.FromInt(600));
            battle.Step(default);
            Require(battle.Events.Count(e => e.Type == BattleEventType.BossHit) == 1 && battle.World.Bullets.Count == 0,
                "高速子弹穿过 Boss 或命中后未回收。");
            var hit = battle.Events.Single(e => e.Type == BattleEventType.BossHit);
            Require(hit.FireTick == 12 && hit.Tick == 13 && hit.TargetKind == CombatTargetKind.Boss, "开火 Tick 未随子弹传递。");
            var scores = f.Scores(); scores.Consume(hit); Require(scores.CurrentScore == 150, "真实命中事件的开火判定错误。");
            battle.Step(default); Require(!battle.Events.Any(e => e.Type == BattleEventType.BossHit), "同一子弹重复命中。");
            battle = f.Battle();
            var orb = new ProjectileSettings { Interaction = ProjectileInteraction.EnergyOrb };
            battle.World.Spawn(0, 0, new FPVector2(FP64.Zero, FP64.One), FP64.Zero, FP64.Zero,
                ProjectileWorld.CompileProjectile(orb, true), explosionTick: 100);
            battle.World.SpawnFriendly(f.Player.Weapon.Projectile.Settings, FPVector2.Zero, FPMath.PI_OVER_2, FP64.FromInt(600));
            battle.Step(default);
            Require(battle.Events.Any(e => e.Type == BattleEventType.EnergyOrbDestroyed) && !battle.Events.Any(e => e.Type == BattleEventType.BossHit),
                "同一子弹击破能量球后仍命中 Boss。");
        }

        private static void PauseAndRestart()
        {
            Require(!SystemRoot.Instance, "请在未运行游戏时验证。");
            using var f = new Fixture(); var go = new GameObject("Boss scoring pause validation"); go.SetActive(false);
            var input = new GameObject("Boss scoring inactive input"); input.SetActive(false);
            try
            {
                var manager = go.AddComponent<BattleManager>(); manager.Config = f.Config; manager.PlayerDefinition = f.Player;
                manager.BossScoring = f.Scoring; manager.AimCamera = go.AddComponent<Camera>(); manager.PlayOnStart = false;
                manager.StandaloneInput = input.AddComponent<PlayerInputSampler>(); manager.PlayerSpawn = Vector2.zero;
                manager.Play(); Require(manager.IsRunning, "独立战斗未启动。");
                manager.Scores.Consume(Hit(0, 0)); manager.AdvanceFrame(0.1);
                int tick = manager.Simulation.Tick; float remaining = manager.Scores.ComboRemaining;
                var pause = new PauseService(); pause.Register(manager);
                using (pause.Acquire())
                {
                    manager.AdvanceFrame(10);
                    Require(manager.Simulation.Tick == tick && manager.Scores.ComboRemaining == remaining && manager.Scores.Stats.CurrentCombo == 1,
                        "暂停期间仍推进或断连。");
                }
                manager.AdvanceFrame(0.1); Require(manager.Simulation.Tick > tick && manager.Scores.ComboRemaining < remaining, "恢复未继续倒计时。");
                manager.Play(); Require(manager.Scores.CurrentScore == 0 && manager.Scores.Stats.CurrentCombo == 0, "重开未清空。");
                int notifications = 0; manager.RunFinished += _ => notifications++;
                for (int i = 0; i < 9; i++) manager.AdvanceFrame(4);
                Require(manager.Result != null && notifications == 1, "独立战斗未结算或重复通知。");
                manager.Stop();
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(input); }
        }

        private static void SceneWiring()
        {
            var scene = SceneManager.GetSceneByPath(BossRhythmSceneSetup.ScenePath);
            Require(scene.isLoaded, "战斗场景未加载。");
            var battle = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattleManager>(true)).Single();
            var hud = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UI.BossRhythmHud>(true)).Single();
            Require(battle.BossScoring.Enabled && hud.Battle == battle && battle.Config && battle.Config.Sequence,
                "场景未开启计分或未绑定 BattleManager 的配置。");
            Require(new BattleBeatClock(battle.Config.Sequence).Valid && battle.Config.Validate() == null, "场景配置无效。");
            BossRhythmSceneSetup.ValidateHud(hud);
        }
    }
}
