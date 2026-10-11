using System;

namespace TapTap.Danmaku.Editor
{
    /// <summary>Derived grid; no duplicate marker list or analysis asset is stored.</summary>
    internal readonly struct ManualMusicBeatGrid
    {
        internal readonly double OriginSeconds, PeriodSeconds;
        internal readonly int FirstIndex, EndIndex;
        internal int Count => EndIndex - FirstIndex;
        private ManualMusicBeatGrid(double origin, double period, int first, int end)
        { OriginSeconds = origin; PeriodSeconds = period; FirstIndex = first; EndIndex = end; }

        internal static bool TryCreate(DanmakuSequence sequence, out ManualMusicBeatGrid grid)
        {
            grid = default;
            if (!sequence || !sequence.Music) return false;
            double bpm = sequence.ManualMusicBpm, offset = sequence.ManualMusicBeatOffsetSeconds;
            if (!Finite(bpm) || bpm < 1 || bpm > 1000 || !Finite(offset) || offset < 0 ||
                !Finite(sequence.MusicStartSeconds) || offset >= sequence.Music.length) return false;
            double period = 60.0 / bpm, origin = sequence.MusicStartSeconds + offset;
            // The audio end is exclusive. Calculate each beat from its index, avoiding accumulated frame rounding.
            int end = (int)Math.Min(int.MaxValue, Math.Ceiling((sequence.Music.length - offset) / period - 1e-9));
            int first = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling(-origin / period - 1e-9)));
            if (first >= end || origin + (end - 1) * period > int.MaxValue / (double)DanmakuSequence.TickRate) return false;
            grid = new ManualMusicBeatGrid(origin, period, first, end); return true;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal double SecondsAt(int index) => OriginSeconds + index * PeriodSeconds;
        internal int TickAt(int index) => Math.Max(0, (int)Math.Round(SecondsAt(index) * DanmakuSequence.TickRate));
        internal int IndexAtOrAfter(double seconds)
            => (int)Math.Min(EndIndex, Math.Max(FirstIndex, Math.Ceiling((seconds - OriginSeconds) / PeriodSeconds - 1e-9)));
        internal int Snap(int tick, float pixelsPerTick)
        {
            if (pixelsPerTick <= 0 || float.IsNaN(pixelsPerTick) || float.IsInfinity(pixelsPerTick)) return tick;
            int nearest = (int)Math.Min(EndIndex - 1, Math.Max(FirstIndex,
                Math.Round((tick / (double)DanmakuSequence.TickRate - OriginSeconds) / PeriodSeconds)));
            int best = tick; double distance = 7.0 / pixelsPerTick;
            // Inspect adjacent beats too: integer frame rounding can change which point is closest.
            for (int index = Math.Max(FirstIndex, nearest - 1); index <= Math.Min(EndIndex - 1, nearest + 1); index++)
            {
                int candidate = TickAt(index); double delta = Math.Abs((double)candidate - tick);
                if (delta < distance) { best = candidate; distance = delta; }
            }
            return best;
        }
    }
}
