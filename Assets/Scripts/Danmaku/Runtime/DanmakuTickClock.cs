using System;

namespace TapTap.Danmaku
{
    /// <summary>Shared fixed clock for player and bullets, regardless of render frame rate.</summary>
    public sealed class DanmakuTickClock
    {
        public const double TickDuration = 1.0 / DanmakuSequence.TickRate;
        private double accumulator;
        public void AddTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            accumulator += seconds;
        }

        public bool ConsumeTick()
        {
            if (accumulator + 1e-9 < TickDuration) return false;
            accumulator = Math.Max(0, accumulator - TickDuration);
            return true;
        }

        public void Reset() => accumulator = 0;
    }
}
