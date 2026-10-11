using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    public static class MusicAnalysisValidation
    {
        private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        [MenuItem("工具/弹幕/第一阶段/运行音频解析验证")]
        public static void Run()
        {
            foreach (int rate in new[] { 44100, 48000 })
            {
                var settings = new MusicAnalysisSettings();
                var silence = new MusicSpectrumAnalyzer(rate, 2, rate, settings);
                silence.Push(new float[rate * 2], rate); silence.Finish(); Assert(silence.GetMarkers().Length == 0, "静音产生候选。");
                foreach (var test in new[] { (frequency: 100, band: 0), (frequency: 1000, band: 1), (frequency: 6000, band: 2) })
                {
                    var samples = new float[rate * 2]; int onset = rate / 2;
                    for (int f = onset; f < onset + rate / 10; f++)
                    { float value = (float)Math.Sin(2 * Math.PI * test.frequency * (f - onset) / rate); samples[f * 2] = value; samples[f * 2 + 1] = -value; }
                    var analyzer = new MusicSpectrumAnalyzer(rate, 2, rate, settings); analyzer.Push(samples, rate); analyzer.Finish();
                    bool found = false;
                    foreach (var marker in analyzer.GetMarkers())
                    { Assert(marker.SampleFrame < rate && marker.SampleFrame >= 0, "候选越界。"); if (marker.Band == test.band && Math.Abs(marker.SampleFrame - onset) <= settings.WindowSize) found = true; }
                    Assert(found, "反相双声道或分频起音遗漏：" + rate + "/" + test.frequency);
                }
                var shortClip = new MusicSpectrumAnalyzer(rate, 1, 100, settings); shortClip.Push(new float[100], 100); shortClip.Finish();
                Assert(shortClip.GetMarkers().Length == 0, "短静音尾部生成候选。");
            }
            Assert(MusicTime.CompletedTick(2.5) == 150, "Tick 映射错误。");
            Assert(MusicTime.SampleAt(3, 1, 48000, 480000) == 96000, "偏移映射错误。");
            ValidateTransport();
            ValidateManualBeatGrid();
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/MusicAnalysisValidation.txt", "PASS: silence; anti-phase stereo; 44.1/48 kHz; three bands; onset tolerance; short clip; sample/tick mapping; audio authority; pause/resume/seek; delayed start; audio end; manual BPM beat spacing; fractional BPM; first-beat/music offset; negative music start; no-analysis snapping; invalid settings; BPM save/undo/redo.\n");
            Debug.Log("Music analysis validation passed. Editor audio backend available: " + EditorMusicBackend.Available);
        }
        private static void ValidateManualBeatGrid()
        {
            var music = AudioClip.Create("BPM validation", 60000, 1, 6000, false);
            var sequence = ScriptableObject.CreateInstance<DanmakuSequence>();
            var restored = ScriptableObject.CreateInstance<DanmakuSequence>();
            var window = ScriptableObject.CreateInstance<DanmakuEditorWindow>();
            int undoGroup = Undo.GetCurrentGroup(); Undo.IncrementCurrentGroup();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                sequence.Music = music; sequence.ManualMusicBpm = 120;
                Assert(ManualMusicBeatGrid.TryCreate(sequence, out var grid) && grid.Count == 20,
                    "120 BPM 的10秒音乐应有20拍，歌曲结束处不额外生成一拍。");
                Assert(grid.TickAt(0) == 0 && grid.TickAt(1) == 30 && grid.TickAt(19) == 570, "BPM 间隔错误。");
                Assert(MusicTimelineDrawer.Snap(sequence, 33, 1, 0) == 30, "未解析音乐时手动节拍不能吸附。");
                Assert(MusicTimelineDrawer.Snap(sequence, 38, 1, 7) == 38, "超过7像素的点被吸附。");
                sequence.ManualMusicBeatOffsetSeconds = 0.25f; sequence.MusicStartSeconds = 1;
                Assert(ManualMusicBeatGrid.TryCreate(sequence, out grid) && grid.TickAt(0) == 75,
                    "首拍偏移和音乐开始偏移未叠加。");
                Assert(MusicTimelineDrawer.Snap(sequence, 78, 1, 0) == 75, "首拍偏移后吸附错误。");
                sequence.ManualMusicBeatOffsetSeconds = 0; sequence.MusicStartSeconds = -1;
                Assert(ManualMusicBeatGrid.TryCreate(sequence, out grid) && grid.FirstIndex == 2 && grid.Count == 18 && grid.TickAt(2) == 0,
                    "从歌曲中段开始时生成了负时间／重复的第0帧节拍。");
                sequence.ManualMusicBpm = 137; sequence.MusicStartSeconds = 0;
                Assert(ManualMusicBeatGrid.TryCreate(sequence, out grid), "小数帧间隔的BPM不能生成节拍。");
                for (int index = 0; index < grid.EndIndex; index++)
                    Assert(Math.Abs(grid.TickAt(index) / 60.0 - index * 60.0 / 137) <= 0.5 / 60.0 + 1e-8, "节拍帧量化误差随拍数累积。");
                sequence.ManualMusicBpm = 137.5f;
                Assert(ManualMusicBeatGrid.TryCreate(sequence, out grid) && Math.Abs(grid.PeriodSeconds - 60.0 / 137.5) < 1e-10, "小数BPM精度丢失。");
                sequence.ManualMusicBpm = 0;
                Assert(!ManualMusicBeatGrid.TryCreate(sequence, out _) && MusicTimelineDrawer.Snap(sequence, 33, 1, 7) == 33, "清除BPM后还残留节拍。");
                sequence.ManualMusicBpm = float.NaN; Assert(!ManualMusicBeatGrid.TryCreate(sequence, out _), "无效BPM生成了节拍。");
                sequence.ManualMusicBpm = 120; sequence.ManualMusicBeatOffsetSeconds = music.length;
                Assert(!ManualMusicBeatGrid.TryCreate(sequence, out _), "歌曲结束处生成首拍。");
                typeof(DanmakuEditorWindow).GetMethod("SetSequence", flags).Invoke(window, new object[] { sequence });
                sequence.ManualMusicBeatOffsetSeconds = 0;
                var set = typeof(DanmakuEditorWindow).GetMethod("SetManualMusicBeatSettings", flags);
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                set.Invoke(window, new object[] { 150f, 0.2f }); Undo.FlushUndoRecordObjects();
                Assert(sequence.ManualMusicBpm == 150 && sequence.ManualMusicBeatOffsetSeconds == 0.2f, "BPM输入未保存到攻击配置。");
                Undo.PerformUndo(); Assert(sequence.ManualMusicBpm == 120 && sequence.ManualMusicBeatOffsetSeconds == 0, "BPM撤销失败。");
                Undo.PerformRedo(); Assert(sequence.ManualMusicBpm == 150 && sequence.ManualMusicBeatOffsetSeconds == 0.2f, "BPM重做失败。");
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(sequence), restored);
                Assert(restored.ManualMusicBpm == 150 && restored.ManualMusicBeatOffsetSeconds == 0.2f, "序列化丢失BPM设置。");
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup + 1); UnityEngine.Object.DestroyImmediate(window);
                if (sequence) UnityEngine.Object.DestroyImmediate(sequence);
                UnityEngine.Object.DestroyImmediate(restored); UnityEngine.Object.DestroyImmediate(music);
            }
        }
        private sealed class Playback : IMusicPlaybackBackend
        {
            public bool IsPlaying { get; private set; }
            private bool launched, paused;
            public bool HasStarted => launched && !paused;
            public int SamplePosition { get; set; }
            public void Play(AudioClip clip, int startSample) { SamplePosition = startSample; IsPlaying = launched = true; paused = false; }
            public void Pause() { IsPlaying = false; paused = true; }
            public void Resume() { IsPlaying = true; paused = false; }
            public void Seek(int sample) { SamplePosition = sample; }
            public void Stop() { IsPlaying = launched = paused = false; }
            public void Dispose() { Stop(); }
            public void End() { IsPlaying = false; }
        }
        private static void ValidateTransport()
        {
            var clip = AudioClip.Create("Transport validation", 48000 * 3, 1, 48000, false);
            try
            {
                var backend = new Playback();
                using var transport = new MusicTimelineTransport(clip, 0, backend);
                transport.Play(); backend.SamplePosition = 48000; transport.Advance(10);
                Assert(transport.TargetTick == 60, "音乐开启时逻辑未以样本位置为准。");
                transport.Pause(); backend.SamplePosition = 96000; transport.Advance(10);
                Assert(transport.TargetTick == 60, "暂停积累时间。");
                transport.Resume(); transport.Advance(0); Assert(transport.TargetTick == 120, "恢复未读取音频位置。");
                transport.Seek(0.5); Assert(!transport.Playing && transport.TargetTick == 30, "Seek 未暂停或定位失败。");
                transport.Resume(); Assert(backend.SamplePosition == 24000, "Seek 的音频样本位置错误。");
                backend.End(); transport.Advance(0); Assert(transport.AudioEnded && transport.TargetTick == 180, "歌曲结束检测错误。");
                transport.Advance(0.5); Assert(transport.TargetTick == 210, "歌曲结束后逻辑不继续推进。");
                using var delayed = new MusicTimelineTransport(clip, 1, new Playback());
                delayed.Play(); delayed.Advance(0.5); Assert(delayed.TargetTick == 30, "正偏移的等待时间错误。");
                var partialClip = AudioClip.Create("Partial final tick", 101, 1, 6000, false);
                try
                {
                    var partialBackend = new Playback(); using var partial = new MusicTimelineTransport(partialClip, 0, partialBackend);
                    partial.Play(); partialBackend.SamplePosition = 100; partial.Advance(0);
                    Assert(!partial.AudioEnded && partial.TargetTick == 1, "音频尾部播放中提前推进。");
                    partialBackend.End(); partial.Advance(0);
                    Assert(partial.AudioEnded && partial.TargetTick == 2, "不足整帧的歌曲尾部无法触发结算。");
                }
                finally { UnityEngine.Object.DestroyImmediate(partialClip); }
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
