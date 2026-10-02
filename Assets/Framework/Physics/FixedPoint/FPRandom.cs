// FPRandom.cs
//
// Deterministic xorshift64* generator producing FP64 / FPVector2 samples.
// Identical seeds always produce identical sequences across runs and machines.

using System;

namespace Framework.FixedPoint
{
    /// <summary>
    /// 基于 xorshift64* 的确定性随机源，提供 ulong / FP64 / FPVector2 等采样。
    /// 相同种子在任何机器、任何 .NET 运行时上产生相同序列。
    /// </summary>
    public sealed class FPRandom
    {
        /// <summary>当前内部状态。</summary>
        private ulong mState;

        /// <summary>
        /// 由 <paramref name="seed"/> 初始化生成器。xorshift64* 不接受 0，
        /// 因此 0 会被替换成稳定的非零常量。
        /// </summary>
        public FPRandom(ulong seed)
        {
            mState = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        /// <summary>当前内部状态（也可视作下一次输出依赖的"种子"）。</summary>
        public ulong Seed => mState;

        /// <summary>重新设置种子。</summary>
        public void Reset(ulong seed)
        {
            mState = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        /// <summary>返回原始 64 位输出，使用 Marsaglia 的 xorshift64*。</summary>
        public ulong NextULong()
        {
            ulong x = mState;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            mState = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>
        /// 返回 [0, 1) 区间的 FP64：取高 32 位作为 Q32.32 的小数部分。
        /// </summary>
        public FP64 NextUnit()
        {
            ulong r = NextULong() >> 32;
            return FP64.FromRaw((long)r);
        }

        /// <summary>返回 [0, <paramref name="max"/>) 的整数（要求 <c>max &gt; 0</c>）。</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> 不大于 0。</exception>
        public int NextInt(int max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            return (int)((NextULong() >> 33) % (ulong)max);
        }

        /// <summary>返回 [<paramref name="min"/>, <paramref name="max"/>) 的整数。</summary>
        /// <exception cref="ArgumentException"><paramref name="max"/> 不大于 <paramref name="min"/>。</exception>
        public int Range(int min, int max)
        {
            if (max <= min) throw new ArgumentException("max must be > min");
            int range = max - min;
            return min + NextInt(range);
        }

        /// <summary>
        /// 返回 [<paramref name="min"/>, <paramref name="max"/>) 的 FP64。
        /// 行为与整数版 <see cref="Range(int, int)"/> 一致，越界时抛异常。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="max"/> 不大于 <paramref name="min"/>。</exception>
        public FP64 Range(FP64 min, FP64 max)
        {
            if (max.Raw <= min.Raw)
                throw new ArgumentException("max must be > min");
            FP64 t = NextUnit();
            return min + (max - min) * t;
        }

        /// <summary>在单位圆上均匀采样一个方向向量。</summary>
        public FPVector2 OnUnitCircle()
        {
            FP64 angle = Range(FP64.Zero, FPMath.PI_TIMES_2);
            return new FPVector2(FPMath.Cos(angle), FPMath.Sin(angle));
        }
    }
}
