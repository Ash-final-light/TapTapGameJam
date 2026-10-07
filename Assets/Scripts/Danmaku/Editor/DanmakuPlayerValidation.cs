using System;
using System.Collections.Generic;
using System.IO;
using Framework.FixedPoint;
using Inputs;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Danmaku.Editor
{
    public static class DanmakuPlayerValidation
    {
        [MenuItem("Tools/Danmaku/运行玩家移动与判定验证")]
        public static void Run()
        {
            var results = new List<string>();
            Check("按键跨无 Tick 帧保留／多 Tick 只消费一次", TestInputBuffer, results);
            Check("普通／低速／摇杆强度／斜向限速／判定半径边界", TestMotor, results);
            Check("30／60／144 FPS 执行相同 Tick 后位置逐比特一致", TestFrameRates, results);
            Check("移动后的玩家位置用于当 Tick 自机狙", TestAim, results);
            Check("动态目标位置／半径／碰撞开关／命中状态快照", TestTarget, results);
            Check("播放器暂停不积累时间／恢复无补跑", TestPause, results);
            Check("固定预览共享玩家配置半径", TestPreviewConfig, results);
            Check("输入消费／定点移动／目标更新稳态无分配", TestAllocation, results);
            Directory.CreateDirectory("Logs");
            string report = string.Join("\n", results);
            File.WriteAllText("Logs/DanmakuPlayerValidation.txt", report);
            Debug.Log("Danmaku player validation passed:\n" + report);
        }

        public static void RunBatch()
        {
            try { Run(); DanmakuValidation.Run(); DanmakuEditorAssets.CreatePlayerConfig(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        private static void Check(string label, Action test, List<string> report) { test(); report.Add("PASS " + label); }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static bool Near(FP64 value, float expected, float tolerance = 0.0001f) => Mathf.Abs(value.ToFloat() - expected) < tolerance;
        private static DanmakuPlayerConfig Config()
        {
            var config = ScriptableObject.CreateInstance<DanmakuPlayerConfig>();
            config.NormalMoveSpeed = 6; config.FocusMoveSpeed = 3; config.HitRadius = 0.1f;
            return config;
        }
        private static DanmakuSequence Sequence()
        {
            var sequence = ScriptableObject.CreateInstance<DanmakuSequence>();
            sequence.FieldSize = new Vector2(100, 100); sequence.DurationTicks = 2000;
            sequence.ClearAtEnd = false; sequence.CollisionEnabled = false;
            return sequence;
        }

        private static void TestInputBuffer()
        {
            var buffer = new InputTickBuffer();
            buffer.Capture(Vector2.right, true, true); buffer.QueueBomb();
            // A render frame with no logic tick must not lose this edge.
            buffer.Capture(Vector2.right, true, true);
            var first = buffer.ConsumeTick(); var second = buffer.ConsumeTick();
            Assert(first.BombPressed && !second.BombPressed && second.MoveX == short.MaxValue && second.Focused && second.FireHeld, "Input consumption wrong");
            buffer.QueueBomb(); buffer.QueueBomb(); buffer.QueuePause();
            Assert(buffer.ConsumeTick().PausePressed && buffer.ConsumeTick().BombPressed && !buffer.ConsumeTick().BombPressed, "Multiple button edges lost/repeated");
            buffer.QueueBomb(); buffer.DiscardButtons();
            Assert(!buffer.ConsumeTick().BombPressed, "Paused button edge leaked");
            buffer.Clear(); Assert(buffer.ConsumeTick().Move == FPVector2.Zero, "Clear did not reset movement");
        }

        private static void TestMotor()
        {
            var config = Config();
            try
            {
                var motor = new DanmakuPlayerMotor(config, new Vector2(100, 100), Vector2.zero);
                var right = new InputSnapshot(short.MaxValue, 0);
                for (int i = 0; i < 60; i++) motor.Step(right);
                Assert(Near(motor.State.Position.X, 6), "Normal speed wrong");
                Assert(motor.State.PreviousPosition.X < motor.State.Position.X, "Previous position missing");
                motor.Reset(FPVector2.Zero);
                var focus = new InputSnapshot(short.MaxValue, 0, focused: true);
                for (int i = 0; i < 60; i++) motor.Step(focus);
                Assert(Near(motor.State.Position.X, 3) && motor.State.Focused, "Focus speed wrong");
                motor.Reset(FPVector2.Zero);
                var diagonal = new InputSnapshot(short.MaxValue, short.MaxValue);
                for (int i = 0; i < 60; i++) motor.Step(diagonal);
                Assert(Near(motor.State.Position.Magnitude, 6), "Diagonal moved faster");
                motor.Reset(FPVector2.Zero);
                var half = new InputSnapshot(16384, 0);
                for (int i = 0; i < 60; i++) motor.Step(half);
                Assert(Near(motor.State.Position.X, 3, 0.0002f), "Stick strength lost");
                motor = new DanmakuPlayerMotor(config, new Vector2(2, 2), Vector2.zero);
                for (int i = 0; i < 60; i++) motor.Step(diagonal);
                Assert(Near(motor.State.Position.X, 0.9f) && Near(motor.State.Position.Y, 0.9f), "Hitbox crossed field boundary");
                config.HitRadius = 20;
                motor = new DanmakuPlayerMotor(config, Vector2.one, new Vector2(5, 5)); motor.Step(right);
                Assert(motor.State.Position == FPVector2.Zero, "Tiny field clamping failed");
            }
            finally { Object.DestroyImmediate(config); }
        }

        private static void TestFrameRates()
        {
            var config = Config();
            try
            {
                FPVector2? expected = null;
                foreach (int fps in new[] { 30, 60, 144 })
                {
                    var motor = new DanmakuPlayerMotor(config, new Vector2(100, 100), Vector2.zero);
                    var clock = new DanmakuTickClock(); int ticks = 0;
                    for (int frame = 0; frame < fps * 2; frame++)
                    {
                        clock.AddTime(1.0 / fps);
                        while (clock.ConsumeTick()) { motor.Step(new InputSnapshot(short.MaxValue, 0)); ticks++; }
                    }
                    Assert(ticks == 120, "FPS clock produced " + ticks + " ticks");
                    if (expected.HasValue) Assert(motor.State.Position == expected.Value, "FPS changed fixed-point movement");
                    expected = motor.State.Position;
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        private static void TestAim()
        {
            var config = Config(); var sequence = Sequence();
            try
            {
                var clip = new DanmakuClip { DurationTicks = 1, EmitterPosition = Vector2.zero, Keyframes = new() { new DanmakuKeyframe() } };
                clip.Emission.AimAtPlayer = true; clip.Emission.DirectionDegrees = 0;
                clip.Emission.Speed = 0; clip.Emission.Count = 1;
                sequence.Clips.Add(clip);
                var motor = new DanmakuPlayerMotor(config, sequence.FieldSize, Vector2.up);
                var simulation = new DanmakuSimulation(sequence);
                motor.Step(new InputSnapshot(short.MaxValue, 0));
                simulation.SetPlayerState(motor.State); simulation.Step();
                FP64 expected = FPMath.Atan2(motor.State.Position.Y, motor.State.Position.X);
                Assert(simulation.Bullets.Count == 1 && simulation.Bullets[0].Direction == expected, "Aim sampled stale player position");
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(sequence); }
        }

        private static void TestTarget()
        {
            var sequence = Sequence();
            try
            {
                var clip = new DanmakuClip { DurationTicks = 1, EmitterPosition = Vector2.zero, Keyframes = new() { new DanmakuKeyframe() } };
                clip.Emission.Speed = 0; clip.ProjectileOverride.HitCount = 5;
                sequence.Clips.Add(clip);
                var sim = new DanmakuSimulation(sequence); ProjectileHit latest = default;
                sim.OnHit += hit => latest = hit;
                sim.SetPlayerState(new DanmakuPlayerState(FPVector2.FromInt(2, 0), FPVector2.Zero, FP64.FromFloat(0.1f)));
                sim.Step(); Assert(sim.HitCount == 0, "Far target hit");
                sim.SetPlayerState(new DanmakuPlayerState(FPVector2.Zero, FPVector2.FromInt(2, 0), FP64.FromFloat(0.1f), false));
                sim.Step(); Assert(sim.HitCount == 0, "Disabled target hit");
                sim.SetPlayerState(new DanmakuPlayerState(FPVector2.Zero, FPVector2.Zero, FP64.FromFloat(0.1f)));
                sim.Step(); sim.Step(); Assert(sim.HitCount == 1, "Dynamic target position or Enter dedup failed");
                var position = FPVector2.FromFloat(0.25f, 0);
                sim.SetPlayerState(new DanmakuPlayerState(position, FPVector2.Zero, FP64.FromFloat(0.1f))); sim.Step();
                sim.SetPlayerState(new DanmakuPlayerState(position, position, FP64.FromFloat(0.3f))); sim.Step();
                Assert(sim.HitCount == 2 && latest.PlayerPosition == position && Near(latest.PlayerHitRadius, 0.3f), "Radius/body update or hit snapshot failed");
            }
            finally { Object.DestroyImmediate(sequence); }
        }

        private static void TestPause()
        {
            var sequence = Sequence(); var root = new GameObject("Player clock validation");
            try
            {
                var player = root.AddComponent<DanmakuSequencePlayer>(); player.PlayOnEnable = false;
                player.Sequence = sequence; player.Play(); player.AdvanceFrame(0.5);
                Assert(player.Simulation.Tick == 30, "Runner clock wrong");
                player.Pause(); player.AdvanceFrame(10); Assert(player.Simulation.Tick == 30, "Paused runner moved");
                player.Resume(); player.AdvanceFrame(DanmakuTickClock.TickDuration);
                Assert(player.Simulation.Tick == 31, "Resume caught up paused time");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(sequence); }
        }

        private static void TestPreviewConfig()
        {
            var sequence = Sequence(); var config = Config();
            try
            {
                sequence.PreviewPlayerPosition = new Vector2(2, 3);
                sequence.PreviewPlayerConfig = config;
                var sim = new DanmakuSimulation(sequence);
                Assert(Near(sim.PlayerState.HitRadius, config.HitRadius), "Preview did not use player config");
                Assert(sim.PlayerState.Position == FPVector2.FromInt(2, 3), "Preview position incorrect");
            }
            finally { Object.DestroyImmediate(sequence); Object.DestroyImmediate(config); }
        }

        private static void TestAllocation()
        {
            var config = Config(); var sequence = Sequence();
            try
            {
                var motor = new DanmakuPlayerMotor(config, sequence.FieldSize, Vector2.zero);
                var sim = new DanmakuSimulation(sequence); var buffer = new InputTickBuffer();
                buffer.Capture(Vector2.right, false, false);
                for (int i = 0; i < 30; i++) { motor.Step(buffer.ConsumeTick()); sim.SetPlayerState(motor.State); sim.Step(); }
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) { motor.Step(buffer.ConsumeTick()); sim.SetPlayerState(motor.State); sim.Step(); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                Assert(allocated == 0, "Player pipeline allocated " + allocated + " bytes");
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(sequence); }
        }
    }
}
