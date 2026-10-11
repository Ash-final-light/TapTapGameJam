using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    public static class MusicTimelineDrawer
    {
        private static readonly Color[] Colors = { new(0.95f, 0.45f, 0.35f), new(0.4f, 0.85f, 0.5f), new(0.4f, 0.65f, 1) };
        public static void Draw(DanmakuSequence sequence, float y, float labelWidth, float pixelsPerTick, float rowHeight,
            float visibleLeft, float visibleRight, int bandMask, bool showBands = true)
        {
            GUI.Label(new Rect(4, y, labelWidth - 5, rowHeight), "音乐 / 波形", EditorStyles.boldLabel);
            DrawManualBeats(sequence, y + (showBands ? 4 : 1) * rowHeight, labelWidth, pixelsPerTick, rowHeight, visibleLeft, visibleRight);
            var data = sequence.MusicAnalysis;
            if (!data || data.Source != sequence.Music || data.SampleRate <= 0) return;
            double start = sequence.MusicStartSeconds, duration = data.Duration;
            var waveform = data.WaveformMinMax;
            int buckets = waveform.Length / 2;
            float center = y + rowHeight / 2;
            for (int i = 0; i < buckets; i++)
            {
                float x = labelWidth + (float)(start + duration * i / buckets) * 60 * pixelsPerTick;
                if (x < visibleLeft || x > visibleRight || x < labelWidth) continue;
                EditorGUI.DrawRect(new Rect(x, center - waveform[i * 2 + 1] * rowHeight * 0.45f, 1,
                    Mathf.Max(1, (waveform[i * 2 + 1] - waveform[i * 2]) * rowHeight * 0.45f)), new Color(0.65f, 0.7f, 0.8f));
            }
            string[] names = { "低频提示", "中频提示", "高频提示" };
            if (showBands) for (int b = 0; b < 3; b++) GUI.Label(new Rect(4, y + (b + 1) * rowHeight, labelWidth - 5, rowHeight), names[b], EditorStyles.miniLabel);
            int first = LowerBound(data.Markers, (int)(((visibleLeft - labelWidth) / (60 * pixelsPerTick) - start) * data.SampleRate));
            int lastPixel = int.MinValue, lastBand = -1;
            for (int i = first; i < data.Markers.Length; i++)
            {
                var marker = data.Markers[i];
                float x = labelWidth + (float)(start + (double)marker.SampleFrame / data.SampleRate) * 60 * pixelsPerTick;
                if (x > visibleRight) break;
                if (x < labelWidth || (bandMask & (1 << marker.Band)) == 0) continue;
                int pixel = Mathf.RoundToInt(x);
                if (pixel == lastPixel && marker.Band == lastBand) continue;
                lastPixel = pixel; lastBand = marker.Band;
                var rect = new Rect(x, y + (showBands ? marker.Band + 1 : 0) * rowHeight + 3, 3, rowHeight - 6);
                EditorGUI.DrawRect(rect, Colors[marker.Band]);
                int tick = MusicTime.MarkerTick(marker, data, start);
                double errorMs = (tick / 60.0 - ((double)marker.SampleFrame / data.SampleRate + start)) * 1000;
                GUI.Label(new Rect(rect.x - 2, rect.y, 8, rect.height), new GUIContent("", $"{names[marker.Band]} · {marker.SampleFrame / (double)data.SampleRate:F3} 秒\n强度 {marker.Strength:F4} · 吸附帧 {tick} · 量化 {errorMs:F2} 毫秒"));
            }
        }
        private static int LowerBound(MusicMarker[] markers, int sample)
        { int lo = 0, hi = markers.Length; while (lo < hi) { int mid = (lo + hi) / 2; if (markers[mid].SampleFrame < sample) lo = mid + 1; else hi = mid; } return lo; }
        public static int Snap(DanmakuSequence sequence, int tick, float pixelsPerTick, int mask)
        {
            if (ManualMusicBeatGrid.TryCreate(sequence, out var grid)) return grid.Snap(tick, pixelsPerTick);
            var data = sequence.MusicAnalysis;
            if (!data || data.Source != sequence.Music || !MusicAnalysisJob.IsCurrent(sequence)) return tick;
            int best = tick; float distance = 7 / pixelsPerTick;
            foreach (var marker in data.Markers)
            {
                if ((mask & (1 << marker.Band)) == 0 || (double)marker.SampleFrame / data.SampleRate + sequence.MusicStartSeconds < 0) continue;
                int candidate = MusicTime.MarkerTick(marker, data, sequence.MusicStartSeconds);
                if (Mathf.Abs(candidate - tick) < distance) { distance = Mathf.Abs(candidate - tick); best = candidate; }
            }
            return best;
        }

        private static void DrawManualBeats(DanmakuSequence sequence, float y, float labelWidth, float pixelsPerTick,
            float rowHeight, float visibleLeft, float visibleRight)
        {
            if (!ManualMusicBeatGrid.TryCreate(sequence, out var grid)) return;
            GUI.Label(new Rect(4, y, labelWidth - 5, rowHeight), "BPM 节拍", EditorStyles.boldLabel);
            double firstSeconds = (visibleLeft - labelWidth - 2) / (pixelsPerTick * DanmakuSequence.TickRate);
            int first = grid.IndexAtOrAfter(firstSeconds - 0.5 / DanmakuSequence.TickRate);
            int lastPixel = int.MinValue;
            for (int index = first; index < grid.EndIndex; index++)
            {
                int tick = grid.TickAt(index);
                float x = labelWidth + tick * pixelsPerTick;
                if (x > visibleRight + 2) break;
                if (x < labelWidth || x < visibleLeft - 2) continue;
                int pixel = UnityEngine.Mathf.RoundToInt(x);
                if (pixel == lastPixel) continue;
                lastPixel = pixel;
                var point = new Rect(x - 1, y + 5, 2, rowHeight - 10);
                EditorGUI.DrawRect(point, new Color(1, 0.8f, 0.25f));
                double songSeconds = sequence.ManualMusicBeatOffsetSeconds + index * grid.PeriodSeconds;
                GUI.Label(new Rect(x - 4, y, 9, rowHeight), new GUIContent("",
                    $"第 {index + 1} 拍 · {sequence.ManualMusicBpm:0.###} BPM\n音乐 {songSeconds:0.###} 秒 · 时间轴 {tick / 60.0:0.###} 秒（{tick} 帧）"));
            }
        }
    }
}
