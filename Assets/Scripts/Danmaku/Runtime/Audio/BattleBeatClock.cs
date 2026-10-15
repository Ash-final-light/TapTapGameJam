using System;

namespace TapTap.Danmaku
{
    /// <summary>Shared absolute beat grid for fire-time judgement and HUD presentation.</summary>
    public readonly struct BattleBeatClock
    {
        public const double DefaultBpm = 60;
        public readonly double Bpm, FirstBeatSeconds, MusicStartSeconds, DurationSeconds;
        public bool Valid => Bpm > 0;
        public double PeriodSeconds => Valid ? 60.0 / Bpm : 0;

        public BattleBeatClock(DanmakuSequence sequence)
        {
            Bpm = DefaultBpm; FirstBeatSeconds = MusicStartSeconds = 0;
            DurationSeconds = double.PositiveInfinity;
            if (!sequence) return;
            double bpm = sequence.ManualMusicBpm, first = sequence.ManualMusicBeatOffsetSeconds;
            double duration = sequence.Music ? (double)sequence.Music.samples / sequence.Music.frequency
                : Math.Max(1, sequence.DurationTicks) / (double)DanmakuSequence.TickRate;
            if (Finite(bpm) && bpm >= 1 && bpm <= 1000) Bpm = bpm;
            DurationSeconds = duration;
            FirstBeatSeconds = Finite(first) && first >= 0 && first < duration ? first : 0;
            MusicStartSeconds = Finite(sequence.MusicStartSeconds) ? sequence.MusicStartSeconds : 0;
        }

        public double SongSecondsAtTick(int tick) => tick / (double)DanmakuSequence.TickRate - MusicStartSeconds;
        public double BeatPosition(double songSeconds) => Valid ? (songSeconds - FirstBeatSeconds) / PeriodSeconds : 0;
        public double SongSecondsAtBeat(long index) => FirstBeatSeconds + index * PeriodSeconds;
        public bool IsBeatInSong(long index) => Valid && index >= 0 && SongSecondsAtBeat(index) < DurationSeconds;

        public bool IsOnBeat(int fireTick, double windowSeconds)
        {
            if (!Valid || fireTick < 0 || !Finite(windowSeconds) || windowSeconds < 0) return false;
            double seconds = SongSecondsAtTick(fireTick);
            if (seconds < 0 || seconds >= DurationSeconds) return false;
            long nearest = Math.Max(0, (long)Math.Round(BeatPosition(seconds)));
            if (!IsBeatInSong(nearest)) nearest--;
            return IsBeatInSong(nearest) && Math.Abs(seconds - SongSecondsAtBeat(nearest)) <= windowSeconds + 1e-7;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
