using System;
using System.Collections.Generic;
namespace TapTap.Danmaku
{
    /// <summary>Incremental multichannel onset analysis; no Unity object or asset operations.</summary>
    public sealed class MusicSpectrumAnalyzer
    {
        private readonly MusicAnalysisSettings settings;
        private readonly int rate, channels, frames, n, hop;
        private readonly float[][] ring;
        private readonly double[] real, imaginary, previousPower, power, window;
        private readonly double[] last = new double[3], beforeLast = new double[3], average = new double[3], threshold = new double[3];
        private readonly int[] lastMarker = { int.MinValue / 2, int.MinValue / 2, int.MinValue / 2 };
        private readonly int[] lowBins = new int[3], highBins = new int[3];
        private readonly List<MusicMarker> markers = new();
        private readonly float[] waveform = new float[4096];
        private int consumed, cursor, lastCenter, spectra;
        public MusicSpectrumAnalyzer(int sampleRate, int channelCount, int sampleFrames, MusicAnalysisSettings settings)
        {
            string error = settings.Validate(); if (error != null) throw new ArgumentException(error);
            if (sampleRate <= 0 || channelCount <= 0 || sampleFrames <= 0) throw new ArgumentException("音频样本信息无效。");
            this.settings = settings; rate = sampleRate; channels = channelCount; frames = sampleFrames; n = settings.WindowSize; hop = settings.HopSize;
            ring = new float[channels][]; for (int c = 0; c < channels; c++) ring[c] = new float[n];
            real = new double[n]; imaginary = new double[n]; window = new double[n];
            previousPower = new double[n / 2 + 1]; power = new double[n / 2 + 1];
            for (int i = 0; i < n; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            var bands = new[] { settings.LowBand, settings.MidBand, settings.HighBand };
            for (int b = 0; b < 3; b++)
            { lowBins[b] = Math.Min(n / 2, (int)Math.Ceiling(bands[b].x * n / rate)); highBins[b] = Math.Min(n / 2, (int)Math.Floor(bands[b].y * n / rate)); }
        }
        public void Push(float[] interleaved, int validFrames)
        {
            for (int f = 0; f < validFrames; f++)
            {
                int bucket = Math.Min(2047, (int)((long)consumed * 2048 / frames));
                for (int c = 0; c < channels; c++)
                {
                    float value = interleaved[f * channels + c]; ring[c][cursor] = value;
                    waveform[bucket * 2] = Math.Min(waveform[bucket * 2], value); waveform[bucket * 2 + 1] = Math.Max(waveform[bucket * 2 + 1], value);
                }
                cursor = (cursor + 1) % n; consumed++;
                if (consumed >= n && (consumed - n) % hop == 0) Spectrum();
            }
        }
        public void Finish()
        {
            // Analyze the short/tail window without allowing padding to become a late marker.
            int end = consumed;
            if (end < n || (end - n) % hop != 0)
            {
                int count = end < n ? n - end : hop - (end - n) % hop;
                for (int i = 0; i < count; i++)
                { for (int c = 0; c < channels; c++) ring[c][cursor] = 0; cursor = (cursor + 1) % n; consumed++; }
                Spectrum();
            }
            for (int b = 0; b < 3; b++) AddPeak(b, 0);
            markers.Sort((a, b) => { int c = a.SampleFrame.CompareTo(b.SampleFrame); return c != 0 ? c : a.Band.CompareTo(b.Band); });
        }
        public MusicMarker[] GetMarkers() => markers.ToArray();
        public float[] GetWaveform() => waveform;
        private void Spectrum()
        {
            Array.Clear(power, 0, power.Length);
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < n; i++) { real[i] = ring[c][(cursor + i) % n] * window[i]; imaginary[i] = 0; }
                FFT(real, imaginary);
                for (int i = 0; i < power.Length; i++) power[i] += Math.Sqrt(real[i] * real[i] + imaginary[i] * imaginary[i]) / (n * channels);
            }
            int center = consumed - n / 2;
            for (int b = 0; b < 3; b++)
            {
                double flux = 0;
                for (int i = lowBins[b]; i <= highBins[b]; i++) flux += Math.Max(0, power[i] - previousPower[i]);
                AddPeak(b, flux);
                beforeLast[b] = last[b]; last[b] = flux;
                threshold[b] = Math.Max(1e-5, average[b] * settings.ThresholdMultiplier);
                average[b] += (flux - average[b]) * 0.04;
            }
            Array.Copy(power, previousPower, power.Length); lastCenter = center; spectra++;
        }
        private void AddPeak(int band, double next)
        {
            if (spectra == 0 || last[band] <= beforeLast[band] || last[band] < next || last[band] <= threshold[band]) return;
            if (lastCenter < 0 || lastCenter >= frames || lastCenter - lastMarker[band] < settings.MinimumIntervalSeconds * rate) return;
            markers.Add(new MusicMarker { Band = band, SampleFrame = lastCenter, Strength = (float)last[band] }); lastMarker[band] = lastCenter;
        }
        private static void FFT(double[] re, double[] im)
        {
            int size = re.Length;
            for (int i = 1, j = 0; i < size; i++)
            {
                int bit = size >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int length = 2; length <= size; length <<= 1)
            {
                double angle = -2 * Math.PI / length, stepRe = Math.Cos(angle), stepIm = Math.Sin(angle);
                for (int start = 0; start < size; start += length)
                {
                    double wr = 1, wi = 0;
                    for (int j = 0; j < length / 2; j++)
                    {
                        int a = start + j, b = a + length / 2;
                        double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                        double nr = wr * stepRe - wi * stepIm; wi = wr * stepIm + wi * stepRe; wr = nr;
                    }
                }
            }
        }
    }
}
