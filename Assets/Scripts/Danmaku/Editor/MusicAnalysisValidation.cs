using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    public static class MusicAnalysisValidation
    {
        private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        [MenuItem("Tools/Danmaku/第一阶段/运行音频解析验证")]
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
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/MusicAnalysisValidation.txt", "PASS: silence; anti-phase stereo; 44.1/48 kHz; three bands; onset tolerance; short clip; sample/tick mapping; audio authority; pause/resume/seek; delayed start; audio end.\n");
            Debug.Log("Music analysis validation passed. Editor audio backend available: " + EditorMusicBackend.Available);
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
                backend.End(); transport.Advance(0); Assert(transport.TargetTick == 180, "歌曲结束检测错误。");
                transport.Advance(0.5); Assert(transport.TargetTick == 210, "歌曲结束后逻辑不继续推进。");
                using var delayed = new MusicTimelineTransport(clip, 1, new Playback());
                delayed.Play(); delayed.Advance(0.5); Assert(delayed.TargetTick == 30, "正偏移的等待时间错误。");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
