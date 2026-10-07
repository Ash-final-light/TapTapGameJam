using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Framework.FixedPoint;
using Framework.Physics.LightweightPhysics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace TapTap.Danmaku.Editor
{
    /// <summary>Engine-backed regression checks, callable from the menu or Unity batch mode.</summary>
    public static class DanmakuValidation
    {
        [MenuItem("Tools/Danmaku/运行模拟验证")]
        public static void Run()
        {
            var results = new List<string>();
            Check("八类预设／回拖与连续播放一致", TestPresets, results);
            Check("片段结束帧不多发；结束后子弹保留", TestBoundaries, results);
            Check("单次母弹／关键帧分裂／清弹优先／死亡过滤", TestSplit, results);
            Check("多母弹回收复用／片段隔离／子模板不递归", TestSplitIsolation, results);
            Check("关键帧时间／空片段／单次限制／编辑工具", TestKeyframes, results);
            Check("编辑器添加／复制／删除／撤销／预设模板", TestEditorCommands, results);
            Check("固定玩家命中／Enter 去重／Seek 静默", TestHit, results);
            Check("复制片段参数独立", TestCopy, results);
            Check("活跃弹上限停止生成", TestCap, results);
            Check("示例资产保存与加载", TestAssetRoundtrip, results);
            Check("物理 ID 索引／移除／复用／恢复", TestBodyIndex, results);
            Check("仅弹与目标生成候选对", TestBroadphase, results);
            Check("100／1000／5000 弹模拟计时", TestLoad, results);
            Check("持续发射／回收稳态无分配", TestChurn, results);
            Check("1000 弹 Sprite 网格批绘制／对象数", TestSpriteBatch, results);
            string report = string.Join("\n", results);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/DanmakuValidation.txt", report);
            Debug.Log("Danmaku validation passed:\n" + report);
        }

        public static void RunBatchAndCreateExamples()
        {
            try { Run(); DanmakuEditorAssets.CreateExamples(); RenderPreviewArtifact(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        private static void Check(string label, Action test, List<string> report)
        {
            test(); report.Add("PASS " + label);
        }

        private static void Assert(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); }

        private static DanmakuSequence Fixture()
        {
            var s = ScriptableObject.CreateInstance<DanmakuSequence>();
            s.FieldSize = new Vector2(100, 100); s.CollisionEnabled = false;
            s.ClearAtEnd = false; s.DurationTicks = 600; s.MaxActiveBullets = 4096;
            s.Clips.Add(new DanmakuClip { Id = "fixture", DurationTicks = 60,
                Keyframes = new() { new DanmakuKeyframe() },
                ProjectileOverride = new ProjectileSettings { DurationTicks = 600 } });
            return s;
        }

        private static string State(DanmakuSimulation sim)
        {
            var sb = new StringBuilder().Append(sim.Tick).Append('|').Append(sim.TotalSpawned).Append('|').Append(sim.HitCount);
            foreach (var p in sim.Bullets)
                sb.Append('|').Append(p.Id).Append(':').Append(p.Position.X.Raw).Append(',').Append(p.Position.Y.Raw)
                    .Append(',').Append(p.BirthTick).Append(',').Append(p.RemainingHits).Append(',').Append(p.Depth);
            return sb.ToString();
        }

        private static void TestPresets()
        {
            for (int i = 0; i < 8; i++)
            {
                var s = Fixture();
                try
                {
                    s.Clips[0].Emission = DanmakuPresetCatalog.Create((BuiltinPattern)i);
                    s.Clips[0].Emission.RandomAngleDegrees = 8;
                    s.Clips[0].Pattern = (BuiltinPattern)i;
                    if (i == 7) s.Clips[0].Keyframes.Add(new DanmakuKeyframe { LocalTick = 30, Type = DanmakuKeyframeType.Split });
                    else s.Clips[0].Keyframes.Add(new DanmakuKeyframe { LocalTick = 17 });
                    var live = new DanmakuSimulation(s);
                    for (int t = 0; t < 140; t++) live.Step();
                    var seek = new DanmakuSimulation(s); seek.Seek(140);
                    Assert(State(live) == State(seek), "Seek mismatch for pattern " + i);
                    seek.Seek(80); seek.Seek(140);
                    Assert(State(live) == State(seek), "Repeated seek mismatch " + i);
                    Assert(live.Fault == null && live.TotalSpawned > 0, "Pattern failed " + i);
                }
                finally { Object.DestroyImmediate(s); }
            }
        }

        private static void TestBoundaries()
        {
            var s = Fixture();
            try
            {
                s.Clips[0].DurationTicks = 240; s.Clips[0].Emission = DanmakuPresetCatalog.Create(BuiltinPattern.Ring);
                s.Clips[0].Emission.Count = 24; s.Clips[0].Keyframes.Clear();
                DanmakuKeyframeEditing.Generate(s.Clips[0], 0, 30, 8);
                var sim = new DanmakuSimulation(s); sim.Seek(241);
                Assert(sim.TotalSpawned == 192 && sim.Bullets.Count == 192, "EndTick emitted extra wave or killed bullets");
                s.ClearAtEnd = true; sim.Reset(s); sim.Seek(600);
                Assert(sim.Bullets.Count == 0, "Sequence end did not clear");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void TestSplit()
        {
            var s = Fixture();
            try
            {
                var clip = s.Clips[0]; clip.StartTick = 20; clip.DurationTicks = 60;
                clip.Pattern = BuiltinPattern.Split; clip.Emission.Speed = 0;
                clip.ProjectileOverride.SplitCount = 8;
                clip.Keyframes.Add(new DanmakuKeyframe { LocalTick = 10, Type = DanmakuKeyframeType.Split });
                var sim = new DanmakuSimulation(s); sim.Seek(30);
                Assert(sim.Bullets.Count == 1, "Mother split before keyframe");
                sim.Step(); Assert(sim.Bullets.Count == 8 && sim.TotalSpawned == 9, "Split children must appear in keyframe tick");
                foreach (var p in sim.Bullets) Assert(p.Depth == 1 && p.BirthTick == 30, "Child generation/time wrong");
                sim.Seek(70); Assert(sim.TotalSpawned == 9, "Children split again or mother fired repeatedly");
                sim.Clear(); sim.Step(); Assert(sim.Bullets.Count == 0, "Clear spawned successors");
                s.Events.Add(new DanmakuTimelineEvent { Tick = 30, Type = DanmakuEventType.ClearBullets });
                sim.Reset(s); sim.Seek(40); Assert(sim.TotalSpawned == 1 && sim.Bullets.Count == 0, "Same-tick clear caused split");
                s.Events.Clear(); clip.ProjectileOverride.DurationTicks = 5;
                sim.Reset(s); sim.Seek(40); Assert(sim.TotalSpawned == 1 && sim.Bullets.Count == 0, "Expired mother split");
                clip.ProjectileOverride.DurationTicks = 600; clip.Emission.Speed = 6000;
                sim.Reset(s); sim.Seek(40); Assert(sim.TotalSpawned == 1 && sim.Bullets.Count == 0, "Out-of-bounds mother split");
                clip.Emission.Speed = 0; clip.EmitterPosition = Vector2.zero; s.PreviewPlayerPosition = Vector2.zero; s.CollisionEnabled = true;
                sim.Reset(s); sim.Seek(40); Assert(sim.TotalSpawned == 1 && sim.Bullets.Count == 0, "Hit mother split");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void TestKeyframes()
        {
            var s = Fixture();
            try
            {
                var clip = s.Clips[0]; clip.StartTick = 10; clip.Keyframes.Clear();
                var sim = new DanmakuSimulation(s); sim.Seek(60);
                Assert(sim.TotalSpawned == 0, "Empty clip fired implicitly");
                DanmakuKeyframeEditing.Add(clip, DanmakuKeyframeType.Emit, 7);
                DanmakuKeyframeEditing.Add(clip, DanmakuKeyframeType.Emit, 29);
                sim.Reset(s); sim.Seek(17); Assert(sim.TotalSpawned == 0, "Keyframe fired early");
                sim.Step(); Assert(sim.TotalSpawned == 1 && sim.Bullets[0].BirthTick == 17, "Keyframe time incorrect");
                sim.Seek(39); Assert(sim.TotalSpawned == 1, "Second keyframe fired early");
                sim.Step(); Assert(sim.TotalSpawned == 2, "Second keyframe missed");
                clip.Keyframes[0].LocalTick = clip.DurationTicks;
                sim.Reset(s); Assert(sim.Fault != null, "Out-of-range point was accepted");
                clip.Keyframes.Clear(); clip.Pattern = BuiltinPattern.Split;
                DanmakuKeyframeEditing.Add(clip, DanmakuKeyframeType.Emit, 5);
                Assert(!DanmakuKeyframeEditing.CanAdd(clip, DanmakuKeyframeType.Emit), "Second mother emission allowed");
                DanmakuKeyframeEditing.Add(clip, DanmakuKeyframeType.Split, 20);
                Assert(!DanmakuKeyframeEditing.CanAdd(clip, DanmakuKeyframeType.Split) && clip.ValidateKeyframes() == null, "Single split rule broken");
                clip.Keyframes[1].LocalTick = 5;
                Assert(clip.ValidateKeyframes() != null, "Same-tick mother split allowed");
                clip.Keyframes[1].LocalTick = 4;
                Assert(clip.ValidateKeyframes() != null, "Split before emission allowed");
                clip.Keyframes[1].LocalTick = 20; clip.Keyframes.RemoveAt(0);
                Assert(clip.ValidateKeyframes() != null, "Missing mother emission accepted");
                clip.Keyframes.Add(new DanmakuKeyframe { Type = DanmakuKeyframeType.Split, LocalTick = 30 });
                Assert(clip.ValidateKeyframes() != null, "Duplicate split accepted");
                clip.Pattern = BuiltinPattern.Ring;
                Assert(clip.ValidateKeyframes() != null, "Non-mother split accepted");
                clip.Keyframes.Clear(); DanmakuKeyframeEditing.Generate(clip, 0, 10, 6);
                DanmakuKeyframeEditing.Generate(clip, 0, 10, 6);
                Assert(clip.Keyframes.Count == 6, "Generator duplicated existing points");
                clip.Keyframes[1].Id = clip.Keyframes[0].Id;
                Assert(clip.ValidateKeyframes() != null, "Duplicate identity accepted");
                sim.Reset(s); Assert(sim.Fault != null, "Compilation hid invalid source identity");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void TestSplitIsolation()
        {
            var s = Fixture(); var child = ScriptableObject.CreateInstance<ProjectileDefinition>();
            try
            {
                child.Settings.DurationTicks = 600; child.Settings.SplitChild = child;
                var first = s.Clips[0]; first.Pattern = BuiltinPattern.Split; first.Emission.Count = 3;
                first.Emission.Shape = EmissionShape.Arc; first.Emission.SpawnRadius = 1; first.Emission.Speed = 0;
                first.ProjectileOverride.SplitSpeed = 0; first.ProjectileOverride.SplitCount = 8;
                first.ProjectileOverride.SplitChild = child;
                first.Keyframes.Add(new DanmakuKeyframe { LocalTick = 10, Type = DanmakuKeyframeType.Split });
                var second = first.Copy(); second.EmitterPosition = new Vector2(-10, 0); second.Emission.Count = 1;
                second.Keyframes[1].LocalTick = 20; s.Clips.Add(second);
                var sim = new DanmakuSimulation(s); sim.Seek(10);
                var positions = new List<FPVector2>();
                foreach (var p in sim.Bullets) if (p.SourceClip == 0) positions.Add(p.Position);
                sim.Step(); Assert(sim.Bullets.Count == 25 && sim.TotalSpawned == 28, "Other clip split early or parent snapshot corrupted");
                var counts = new int[3];
                foreach (var p in sim.Bullets)
                {
                    if (p.SourceClip == 1) { Assert(p.Depth == 0, "Other mother changed"); continue; }
                    int index = positions.IndexOf(p.Position); Assert(index >= 0, "Recycled parent position corrupted"); counts[index]++;
                }
                foreach (int count in counts) Assert(count == 8, "Split count per mother incorrect");
                sim.Seek(50); Assert(sim.Bullets.Count == 32 && sim.TotalSpawned == 36, "Children recursively split or clip split missed");
                s.MaxActiveBullets = 5; sim.Reset(s); sim.Seek(11);
                Assert(sim.Fault != null && sim.Bullets.Count == 5, "Split bypassed capacity limit");
            }
            finally { Object.DestroyImmediate(s); Object.DestroyImmediate(child); }
        }

        private static void TestHit()
        {
            var s = Fixture();
            try
            {
                s.CollisionEnabled = true; s.PreviewPlayerPosition = Vector2.zero;
                var clip = s.Clips[0]; clip.EmitterPosition = Vector2.zero;
                clip.DurationTicks = 1; clip.Emission.Speed = 0; clip.ProjectileOverride.HitCount = 2;
                clip.ProjectileOverride.DamageConfigId = 123;
                var sim = new DanmakuSimulation(s); int callbacks = 0;
                sim.OnHit += h => { callbacks++; Assert(h.DamageConfigId == 123, "Config id lost"); };
                sim.Step(); sim.Step(); Assert(sim.HitCount == 1 && callbacks == 1, "Stay produced repeated hits");
                sim.Seek(2); Assert(callbacks == 1 && sim.HitCount == 1, "Seek published real callback");
                clip.ProjectileOverride.HitCount = 1; sim.Reset(s); sim.Step();
                Assert(sim.Bullets.Count == 0, "HitCount death failed");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void TestEditorCommands()
        {
            var s = Fixture(); s.Clips.Clear();
            var window = ScriptableObject.CreateInstance<DanmakuEditorWindow>();
            var preset = ScriptableObject.CreateInstance<EmissionPreset>();
            int undoGroup = Undo.GetCurrentGroup(); Undo.IncrementCurrentGroup();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            void Invoke(string name, params object[] args) => typeof(DanmakuEditorWindow).GetMethod(name, flags).Invoke(window, args);
            void Set(string name, object value) => typeof(DanmakuEditorWindow).GetField(name, flags).SetValue(window, value);
            try
            {
                Invoke("SetSequence", s); Invoke("AddBuiltin", BuiltinPattern.Ring, 10);
                Assert(s.Clips.Count == 1 && s.Clips[0].Keyframes.Count == 0, "New clip has implicit points");
                Invoke("AddKeyframe", 0, DanmakuKeyframeType.Emit, 7);
                Assert(s.Clips[0].Keyframes[0].LocalTick == 7, "Context command time changed");
                Set("currentTick", 30); Invoke("CopyKeyframe");
                Assert(s.Clips[0].Keyframes.Count == 2 && s.Clips[0].Keyframes[1].LocalTick == 20, "Copy to playhead failed");
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                Invoke("DeleteKeyframe"); Undo.FlushUndoRecordObjects();
                Assert(s.Clips[0].Keyframes.Count == 1, "Delete keyframe failed");
                Undo.PerformUndo(); Assert(s.Clips[0].Keyframes.Count == 2, "Undo keyframe deletion failed");
                Undo.PerformRedo(); Assert(s.Clips[0].Keyframes.Count == 1, "Redo keyframe deletion failed");
                Invoke("Duplicate");
                Assert(s.Clips.Count == 2 && s.Clips[0].Keyframes[0].Id != s.Clips[1].Keyframes[0].Id, "Clip duplication shares keyframe identity");
                Invoke("AddBuiltin", BuiltinPattern.Split, 100);
                Invoke("AddKeyframe", 2, DanmakuKeyframeType.Emit, 0);
                Invoke("AddKeyframe", 2, DanmakuKeyframeType.Emit, 5);
                Invoke("AddKeyframe", 2, DanmakuKeyframeType.Split, 10);
                Invoke("AddKeyframe", 2, DanmakuKeyframeType.Split, 20);
                Invoke("CopyKeyframe");
                Assert(s.Clips[2].Keyframes.Count == 2 && s.Clips[2].ValidateKeyframes() == null, "Editor allows second mother emission/split");
                preset.Pattern = BuiltinPattern.Ring; preset.DurationTicks = 220;
                preset.Keyframes.Add(new DanmakuKeyframe { LocalTick = 200 });
                Invoke("AddPreset", preset, 200);
                Assert(s.Clips[3].DurationTicks == 220 && s.Clips[3].Keyframes[0].LocalTick == 200 &&
                    s.Clips[3].Keyframes[0].Id != preset.Keyframes[0].Id, "Preset template lost duration or identity");
                s.Clips[3].Keyframes[0].LocalTick = 5;
                Assert(preset.Keyframes[0].LocalTick == 200, "Preset keyframe mutated through clip");
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup + 1);
                Object.DestroyImmediate(window); Object.DestroyImmediate(preset);
                if (s) Object.DestroyImmediate(s);
            }
        }

        private static void TestCopy()
        {
            var clip = new DanmakuClip(); clip.Keyframes.Add(new DanmakuKeyframe { LocalTick = 8 }); var copy = clip.Copy();
            copy.Keyframes[0].LocalTick = 20;
            Assert(clip.Keyframes[0].LocalTick == 8 && clip.Keyframes[0].Id != copy.Keyframes[0].Id, "Keyframe copy aliased data/identity");
            copy.Emission.Count = 8; copy.ProjectileOverride.HitRadius = 2;
            Assert(copy.Id != clip.Id && clip.Emission.Count == 1 && clip.ProjectileOverride.HitRadius != 2, "Copy aliased local parameters");
        }

        private static void TestCap()
        {
            var s = Fixture();
            try
            {
                s.MaxActiveBullets = 3; s.Clips[0].Emission.Count = 5;
                var sim = new DanmakuSimulation(s); sim.Step();
                Assert(sim.Bullets.Count == 3 && sim.Fault != null, "Cap not enforced");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void TestAssetRoundtrip()
        {
            string path = "Assets/__DanmakuValidation_" + Guid.NewGuid().ToString("N") + ".asset";
            var s = Fixture(); s.Clips[0].Emission.Count = 13;
            s.Clips[0].Pattern = BuiltinPattern.Ring; s.Clips[0].Keyframes[0].LocalTick = 17;
            try
            {
                AssetDatabase.CreateAsset(s, path); AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(path);
                Assert(loaded && loaded.Clips[0].Emission.Count == 13 && loaded.Clips[0].Id == "fixture", "Serialization lost settings");
                Assert(loaded.Clips[0].Pattern == BuiltinPattern.Ring && loaded.Clips[0].Keyframes.Count == 1 &&
                    loaded.Clips[0].Keyframes[0].LocalTick == 17 && loaded.Clips[0].Keyframes[0].Id == s.Clips[0].Keyframes[0].Id, "Serialization lost keyframes");
            }
            finally { AssetDatabase.DeleteAsset(path); if (s) Object.DestroyImmediate(s); }
        }

        private static void TestLoad()
        {
            foreach (int count in new[] { 100, 1000, 5000 }) TestLoad(count);
        }

        private static void TestLoad(int count)
        {
            var s = Fixture();
            try
            {
                s.MaxActiveBullets = count + 1;
                s.CollisionEnabled = true;
                s.DurationTicks = 1200; s.Clips[0].DurationTicks = 1;
                s.Clips.Clear();
                int remaining = count;
                while (remaining > 0)
                {
                    var clip = new DanmakuClip { DurationTicks = 1, Keyframes = new() { new DanmakuKeyframe() } };
                    clip.Emission.Count = Math.Min(64, remaining);
                    clip.Emission.Speed = 0; clip.ProjectileOverride.DurationTicks = 1201;
                    s.Clips.Add(clip); remaining -= clip.Emission.Count;
                }
                var sim = new DanmakuSimulation(s); sim.Step();
                Assert(sim.Bullets.Count == count, "Load fixture wrong");
                for (int i = 0; i < 30; i++) sim.Step();
                var timer = Stopwatch.StartNew();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) sim.Step();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                timer.Stop();
                Assert(sim.Bullets.Count == count && sim.Fault == null, "Bullet load simulation failed");
                Assert(allocated == 0, "Steady simulation allocated " + allocated + " bytes");
                Debug.Log($"Danmaku {count} bullets / 600 ticks: {timer.Elapsed.TotalMilliseconds:F2} ms, {allocated} B allocated (Editor CPU only)");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void TestBodyIndex()
        {
            var world = new LWPhysicsWorld();
            var def = LWBodyDef.Dynamic(LWShape.Circle(FP64.One), FPVector2.Zero, FP64.One);
            int first = world.AddBody(def), second = world.AddBody(def);
            var snap = world.Snapshot();
            world.RemoveBody(first); int third = world.AddBody(def);
            Assert(!world.HasBody(first) && world.GetBody(second).Id == second && world.GetBody(third).Id == third, "Slot reuse corrupted ID map");
            world.Restore(snap);
            Assert(world.HasBody(first) && world.HasBody(second) && !world.HasBody(third), "Restore did not rebuild ID map");
            var body = world.GetBody(first); body.Position = FPVector2.FromInt(2, 3);
            world.SetBody(first, body);
            Assert(world.GetBody(first).Position == body.Position, "Indexed SetBody failed");
        }

        private static void TestBroadphase()
        {
            var broadphase = new BulletTargetBroadphase();
            var bodies = new LWBody[101];
            bodies[0] = new LWBody { Id = 1, Layer = 2, Mask = 1, Flags = LWBodyFlags.Alive | LWBodyFlags.Trigger,
                Shape = LWShape.Circle(FP64.One) };
            for (int i = 1; i < bodies.Length; i++) bodies[i] = new LWBody { Id = i + 1, Layer = 1, Mask = 2,
                Flags = LWBodyFlags.Alive | LWBodyFlags.Trigger, Shape = LWShape.Circle(FP64.One) };
            broadphase.Build(bodies, bodies.Length);
            Assert(broadphase.CandidateChecks == 100 && broadphase.Pairs.Count == 100, "Bullet-bullet candidate pairs generated");
        }

        private static void TestSpriteBatch()
        {
            var s = Fixture(); var root = new GameObject("Danmaku render validation");
            DanmakuSpritePool pool = null;
            try
            {
                s.Clips[0].Emission.Count = 64; s.Clips[0].Emission.SpeedLayers = 8;
                s.Clips[0].DurationTicks = 2; s.Clips[0].Keyframes.Add(new DanmakuKeyframe { LocalTick = 1 });
                var sim = new DanmakuSimulation(s); sim.Step(); sim.Step();
                pool = new DanmakuSpritePool(root.transform); pool.Sync(sim, s); pool.Sync(sim, s);
                Assert(sim.Bullets.Count == 1024 && root.transform.childCount < 10, "Per-bullet GameObjects were created");
                Assert(pool.ActiveBatchCount > 0, "No sprite geometry batches");
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    Assert(filter.sharedMesh.vertexCount <= 16000, "Mobile-safe vertex chunk exceeded");
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                long setup = GC.GetAllocatedBytesForCurrentThread() - bytes;
                for (int i = 0; i < 120; i++) pool.Sync(sim, s);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes - setup;
                watch.Stop();
                Debug.Log($"Danmaku sprite batches: {pool.ActiveBatchCount}, 1024 bullets, 120 updates {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated} B managed allocation");
                Assert(allocated == 0, "Warmed sprite update allocated " + allocated + " bytes");
            }
            finally { pool?.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(s); }
        }

        private static void TestChurn()
        {
            var s = Fixture();
            try
            {
                s.DurationTicks = 1000; s.Clips[0].DurationTicks = 1000;
                s.Clips[0].Emission.Count = 50; s.Clips[0].Emission.Speed = 0;
                DanmakuKeyframeEditing.Generate(s.Clips[0], 0, 1, 1000);
                s.Clips[0].ProjectileOverride.DurationTicks = 2;
                var sim = new DanmakuSimulation(s);
                for (int i = 0; i < 60; i++) sim.Step();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) sim.Step();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Assert(sim.TotalSpawned == 33000 && sim.Fault == null && allocated == 0, "Emission/recycling allocated " + allocated + " bytes");
            }
            finally { Object.DestroyImmediate(s); }
        }

        private static void RenderPreviewArtifact()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var s = DanmakuSequence.CreateExample();
            var preview = new PreviewRenderUtility();
            DanmakuSpritePool pool = null;
            try
            {
                var root = new GameObject("Preview artifact"); preview.AddSingleGO(root);
                pool = new DanmakuSpritePool(root.transform);
                var sim = new DanmakuSimulation(s); sim.Seek(150); pool.Sync(sim, s);
                preview.camera.orthographic = true; preview.camera.orthographicSize = 6;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.camera.transform.rotation = Quaternion.identity;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
                preview.BeginStaticPreview(new Rect(0, 0, 640, 640)); preview.Render();
                var image = preview.EndStaticPreview();
                File.WriteAllBytes("Logs/DanmakuSpritePreview.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally { pool?.Dispose(); preview.Cleanup(); Object.DestroyImmediate(s); }
        }
    }
}
