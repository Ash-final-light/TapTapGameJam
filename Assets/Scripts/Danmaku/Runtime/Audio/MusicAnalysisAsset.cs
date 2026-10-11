using System;
using UnityEngine;
namespace TapTap.Danmaku
{
    [Serializable]
    public sealed class MusicAnalysisSettings
    {
        public int WindowSize = 2048, HopSize = 512;
        public float ThresholdMultiplier = 1.5f, MinimumIntervalSeconds = 0.12f;
        public Vector2 LowBand = new(40, 250), MidBand = new(250, 2000), HighBand = new(2000, 10000);
        public string Validate()
        {
            if (WindowSize < 128 || WindowSize > 8192 || (WindowSize & (WindowSize - 1)) != 0) return "FFT 窗口必须是 128–8192 的二次幂。";
            if (HopSize < 1 || HopSize > WindowSize || ThresholdMultiplier <= 0 || MinimumIntervalSeconds < 0) return "Hop、阈值或最小间隔无效。";
            foreach (var band in new[] { LowBand, MidBand, HighBand })
                if (band.x < 0 || band.y <= band.x) return "频段上下限无效。";
            return null;
        }
    }
    [Serializable]
    public struct MusicMarker
    {
        public int SampleFrame, Band;
        public float Strength;
    }
    [CreateAssetMenu(menuName = "弹幕/音乐解析结果")]
    public sealed class MusicAnalysisAsset : ScriptableObject
    {
        public const int CurrentAlgorithmVersion = 1;
        public AudioClip Source;
        public int SampleRate, Channels, SampleFrameCount, AlgorithmVersion;
        public string SourceFingerprint, SettingsFingerprint;
        public MusicMarker[] Markers = Array.Empty<MusicMarker>();
        public float[] WaveformMinMax = Array.Empty<float>();
        public double Duration => SampleRate > 0 ? (double)SampleFrameCount / SampleRate : 0;
    }
    public static class MusicTime
    {
        public static int EndTick(double seconds) => Math.Max(1, (int)Math.Ceiling(seconds * DanmakuSequence.TickRate - 1e-8));
        public static int CompletedTick(double seconds) => Math.Max(0, (int)Math.Floor(seconds * DanmakuSequence.TickRate + 1e-8));
        public static int MarkerTick(MusicMarker marker, MusicAnalysisAsset analysis, double musicStartSeconds)
            => Math.Max(0, (int)Math.Round(((double)marker.SampleFrame / analysis.SampleRate + musicStartSeconds) * DanmakuSequence.TickRate));
        public static int SampleAt(double battleSeconds, double musicStartSeconds, int frequency, int sampleCount)
            => Math.Max(0, Math.Min(Math.Max(0, sampleCount - 1), (int)Math.Round((battleSeconds - musicStartSeconds) * frequency)));
    }
}
