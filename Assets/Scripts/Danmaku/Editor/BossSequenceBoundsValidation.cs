using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using TapTap.Player;
using TapTap.Player.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Danmaku.Editor
{
    public static class BossSequenceBoundsValidation
    {
        private const string SequencePath = "Assets/DanmakuExamples/BossSequence.asset";
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.002f;

        // One-time migration through AssetDatabase; existing clips, IDs and timing stay intact.
        public static void ConfigureBossSequenceBatch()
        {
            try
            {
                ConfigureBossSequence();
                VerifyOriginalRhythm();
                Run();
                PlayerSystemValidation.Run(); BattleValidation.Run(); EnergyOrbValidation.Run();
                var scenes = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
                try
                {
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(TapTap.Systems.Editor.BossRhythmSceneSetup.ScenePath);
                    TapTap.Systems.Editor.BossScoringValidation.Run();
                }
                finally
                {
                    if (scenes.Any(s => s.isLoaded)) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(scenes);
                }
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void ConfigureBossSequence()
        {
            var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(SequencePath);
            Assert(sequence, "BossSequence SO 缺失。");
            Undo.RecordObject(sequence, "Boss 使用可视边界"); sequence.UseCameraBounds = true;
            EditorUtility.SetDirty(sequence); AssetDatabase.SaveAssetIfDirty(sequence);
        }

        private static void VerifyOriginalRhythm()
        {
            var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(SequencePath);
            Assert(sequence.Clips.Count >= 4, "原有开场片段缺失。");
            int[][] keys = { new[] {81, 174, 267, 360}, new[] {11, 104, 197, 290}, new[] {81, 174, 267, 360}, new[] {11, 104, 197, 290} };
            for (int i = 0; i < keys.Length; i++)
                Assert(sequence.Clips[i].StartTick == (i < 2 ? 0 : 372) && sequence.Clips[i].Keyframes.Select(k => k.LocalTick).SequenceEqual(keys[i]), "原有手动节拍改变。");
            Assert(Near(sequence.ManualMusicBpm, 155) && Near(sequence.ManualMusicBeatOffsetSeconds, 0.19f), "原有音乐对齐改变。");
        }

        [MenuItem("工具/弹幕/验证 Boss 可视边界与编排")]
        public static void Run()
        {
            var report = new List<string>();
            try
            {
                TestProjectionAndMovement(report);
                TestProjectileBounds(report);
                var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(SequencePath);
                Assert(sequence && sequence.UseCameraBounds, "正式 Boss SO 未启用可视边界。");
                for (int i = 0; i < sequence.Clips.Count; i++)
                {
                    var clip = sequence.Clips[i];
                    Assert(clip != null && clip.StartTick >= 0 && clip.DurationTicks > 0 && clip.EndTick <= sequence.DurationTicks, "片段越出攻击总时长。");
                    Assert(clip.ValidateKeyframes() == null, clip.ValidateKeyframes());
                    report.Add($"PASS clip {i + 1}: start={clip.StartTick}; local={string.Join(",", clip.Keyframes.Select(k => k.LocalTick))}; speed={clip.EffectiveEmission.Speed}");
                }
                int lastEmit = sequence.Clips.SelectMany(c => c.Keyframes.Where(k => k.Type == DanmakuKeyframeType.Emit).Select(k => c.StartTick + k.LocalTick)).DefaultIfEmpty(-1).Max();
                report.Add($"PASS SO: field={sequence.FieldSize}; duration={sequence.DurationTicks}; lastEmit={lastEmit}; musicSeconds={(sequence.Music ? sequence.Music.length : 0):0.###}");
                if (sequence.Music && !MusicAnalysisJob.IsCurrent(sequence)) report.Add("NOTE 离线分析与当前音乐不匹配；后续编辑应重新解析。手动 BPM 不受影响。");
            }
            catch (Exception error) { report.Add("FAIL " + error); throw; }
            finally
            { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/BossSequenceBoundsValidation.txt", string.Join("\n", report)); }
            Debug.Log(string.Join("\n", report));
        }

        private static void TestProjectionAndMovement(List<string> report)
        {
            var cameraObject = new GameObject("Bounds validation camera");
            var field = new GameObject("Bounds validation field");
            var definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            var target = new RenderTexture(1920, 1080, 0);
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.targetTexture = target; camera.orthographic = true; camera.orthographicSize = 5.94f;
                camera.transform.position = new Vector3(0, 0, -10);
                foreach (float aspect in new[] { 16f / 9, 4f / 3, 21f / 9, 9f / 16 })
                {
                    camera.aspect = aspect;
                    Assert(BattleInputProjection.TryGetVisibleBounds(camera, field.transform, out var bounds), "投影失败。");
                    Assert(Near(bounds.width, 11.88f * aspect) && Near(bounds.height, 11.88f), "窗口比例未影响可视范围。");
                    var motor = new PlayerMotor(definition, new Vector2(9, 11), new Vector2(0, -4), bounds);
                    for (int i = 0; i < 600; i++) motor.Step(short.MaxValue, PlayerMotionMode.Normal, FP64.One, 1);
                    Assert(Near(motor.State.Position.X.ToFloat(), bounds.xMax - definition.HitRadius), "右边界仍被旧 SO 限制。");
                    Assert(Near(motor.State.Position.Y.ToFloat(), -4), "地面高度改变。");
                    for (int i = 0; i < 1200; i++) motor.Step(short.MinValue + 1, PlayerMotionMode.Dash, FP64.One, -1);
                    Assert(Near(motor.State.Position.X.ToFloat(), bounds.xMin + definition.HitRadius), "冲刺穿出左边界。");
                    motor.Jump(); motor.Step(0, PlayerMotionMode.Normal, FP64.One, 1);
                    Assert(motor.State.Position.Y.ToFloat() > -4, "跳跃失效。");
                    motor.SetFlight(true);
                    for (int i = 0; i < 600; i++) motor.Step(0, PlayerMotionMode.Flight, FP64.One, 1);
                    Assert(Near(motor.State.Position.Y.ToFloat(), bounds.yMax - definition.HitRadius), "飞行未到达可见顶部。");
                    Assert(motor.VerticalSpeed == FP64.Zero, "碰顶未清除上升速度。");
                    motor.SetFlight(false);
                    for (int i = 0; i < 300; i++) motor.Step(0, PlayerMotionMode.Normal, FP64.One, 1);
                    Assert(motor.Grounded && Near(motor.State.Position.Y.ToFloat(), -4), "松开飞行未落回原地面。");
                    var smaller = new Rect(-2, -5, 4, 8); motor.SetBounds(smaller);
                    Assert(motor.State.Position == motor.State.PreviousPosition && smaller.Contains(DanmakuSimulation.ToVector(motor.State.Position)), "缩窗夹位／历史位置错误。");
                }
                camera.aspect = 16f / 9; camera.transform.position = new Vector3(3, 2, -10);
                field.transform.position = new Vector3(1, 1, 0); field.transform.localScale = new Vector3(2, 2, 1);
                Assert(BattleInputProjection.TryGetVisibleBounds(camera, field.transform, out var offset), "偏移／缩放投影失败。");
                Assert(Near(offset.center.x, 1) && Near(offset.center.y, 0.5f), "未使用战斗局部坐标。");
                camera.transform.rotation = Quaternion.Euler(0, 0, 30);
                Assert(!BattleInputProjection.TryGetVisibleBounds(camera, field.transform, out _), "旋转画面应安全回退。");
                var legacy = new PlayerMotor(definition, new Vector2(9, 11), new Vector2(0, -4));
                for (int i = 0; i < 600; i++) legacy.Step(short.MaxValue, PlayerMotionMode.Normal, FP64.One, 1);
                Assert(Near(legacy.State.Position.X.ToFloat(), 4.42f), "未启用可视边界的配置不再兼容。");
                report.Add("PASS 16:9 / 4:3 / 21:9 / portrait; left/right/dash/top; jump/flight/release/ground; resize; translated/scaled field; rotated camera fallback; legacy bounds");
            }
            finally { Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(field); Object.DestroyImmediate(definition); Object.DestroyImmediate(target); }
        }

        private static void TestProjectileBounds(List<string> report)
        {
            var sequence = ScriptableObject.CreateInstance<DanmakuSequence>();
            var definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            var library = ScriptableObject.CreateInstance<PlayerActionLibrary>();
            var weapon = ScriptableObject.CreateInstance<PlayerWeaponDefinition>();
            var projectile = ScriptableObject.CreateInstance<ProjectileDefinition>();
            var config = ScriptableObject.CreateInstance<BattleConfig>();
            try
            {
                sequence.DurationTicks = 600; sequence.ClearAtEnd = false;
                config.Sequence = sequence; definition.Actions = library; definition.Weapon = weapon; weapon.Projectile = projectile;
                var bounds = new Rect(-10, -6, 20, 12);
                var battle = new BattleSimulation(config, definition, new Vector2(8, -4), bounds: bounds);
                Assert(Near(battle.Player.Pose.Position.X.ToFloat(), 8), "出生被旧场地提前夹位。");
                battle.World.SpawnFriendly(projectile.Settings, FPVector2.FromInt(8, 0), FP64.Zero, FP64.Zero);
                battle.Step(default);
                Assert(battle.World.Bullets.Count == 1, "仍在可见画面中的子弹被旧场地回收。");
                battle.SetBounds(new Rect(5, -6, 10, 12)); battle.Step(default);
                Assert(battle.World.Bullets.Count == 1, "偏心视野错误回收子弹。");
                battle.SetBounds(new Rect(-2, -6, 4, 12)); battle.Step(default);
                Assert(battle.World.Bullets.Count == 0 && battle.Player.Pose.Position.X.ToFloat() < 2, "缩窗没有同步玩家和子弹边界。");
                report.Add("PASS battle spawn; visible projectile retained outside original 9x11; offset bounds; resize synchronizes movement and projectile culling");
            }
            finally
            { foreach (var value in new Object[] { sequence, definition, library, weapon, projectile, config }) Object.DestroyImmediate(value); }
        }
    }
}
