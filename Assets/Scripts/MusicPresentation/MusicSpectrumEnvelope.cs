using System;
using UnityEngine;

namespace TapTap.MusicPresentation
{
    /// <summary>Reusable three-band envelope. Input bins cover 0..outputSampleRate/2.</summary>
    public sealed class MusicSpectrumEnvelope
    {
        public const int BandCount = 3;
        private readonly float[] values = new float[BandCount];
        private readonly float[] peaks = new float[BandCount];
        private readonly int[] firstBins = new int[BandCount], lastBins = new int[BandCount];
        private int cachedRate, cachedSize;
        public float Low => values[0];
        public float Mid => values[1];
        public float High => values[2];
        public float Amplitude => (Low + Mid + High) / BandCount;
        public float this[int band] => values[Mathf.Clamp(band, 0, BandCount - 1)];

        public void Reset()
        {
            Array.Clear(values, 0, values.Length);
            Array.Clear(peaks, 0, peaks.Length);
        }

        public void Sample(float[] left, float[] right, int outputSampleRate, float deltaSeconds,
            float sensitivity, float minimumPeak, float peakReleaseSeconds, float attackSeconds, float releaseSeconds)
        {
            if (left == null || left.Length == 0 || outputSampleRate <= 0)
                throw new ArgumentException("频谱数据与输出采样率必须有效。");
            if (right != null && right.Length != left.Length)
                throw new ArgumentException("左右声道频谱长度必须一致。");
            if (cachedSize != left.Length || cachedRate != outputSampleRate)
            {
                cachedSize = left.Length; cachedRate = outputSampleRate;
                SetBins(0, 40, 250); SetBins(1, 250, 2000); SetBins(2, 2000, 10000);
            }
            float dt = FiniteNonNegative(deltaSeconds);
            float peakDecay = Mathf.Exp(-dt / Mathf.Max(0.001f, FiniteNonNegative(peakReleaseSeconds)));
            float floor = Mathf.Max(0.000001f, FiniteNonNegative(minimumPeak));
            float gain = FiniteNonNegative(sensitivity);
            for (int band = 0; band < BandCount; band++)
            {
                double sum = 0;
                int count = lastBins[band] - firstBins[band];
                for (int bin = firstBins[band]; bin < lastBins[band]; bin++)
                {
                    float magnitude = FiniteNonNegative(left[bin]);
                    if (right != null) magnitude = magnitude * 0.5f + FiniteNonNegative(right[bin]) * 0.5f;
                    sum += (double)magnitude * magnitude;
                }
                float energy = count > 0 ? (float)Math.Sqrt(sum / count) : 0;
                peaks[band] = Mathf.Max(floor, Mathf.Max(energy, peaks[band] * peakDecay));
                float target = Mathf.Clamp01(energy / peaks[band] * gain);
                values[band] = Smooth(values[band], target, dt, target > values[band] ? attackSeconds : releaseSeconds);
            }
        }

        public void Release(float deltaSeconds, float releaseSeconds)
        {
            for (int band = 0; band < BandCount; band++)
                values[band] = Smooth(values[band], 0, deltaSeconds, releaseSeconds);
        }

        public static float Smooth(float current, float target, float deltaSeconds, float timeConstant)
        {
            float dt = FiniteNonNegative(deltaSeconds);
            float tau = Mathf.Max(0.001f, FiniteNonNegative(timeConstant));
            return Mathf.Clamp01(current + (target - current) * (1 - Mathf.Exp(-dt / tau)));
        }

        private void SetBins(int band, float lowHz, float highHz)
        {
            float binsPerHz = 2f * cachedSize / cachedRate;
            firstBins[band] = Mathf.Clamp(Mathf.CeilToInt(lowHz * binsPerHz), 0, cachedSize);
            lastBins[band] = Mathf.Clamp(Mathf.CeilToInt(highHz * binsPerHz), firstBins[band], cachedSize);
        }

        private static float FiniteNonNegative(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
    }
}
