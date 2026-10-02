// FP64.cs
//
// Q32.32 deterministic fixed-point number backed by a single long.
// Real value = Raw / 2^32. Arithmetic is bit-exact across machines.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Framework.FixedPoint
{
    /// <summary>
    /// Q32.32 确定性定点数，底层用一个 <see cref="long"/> 存储原始位 (<see cref="Raw"/>)。
    /// 实际数值等于 <c>Raw / 2^32</c>，需要 128 位中间值的算术（乘 / 除）走
    /// <see cref="Math128"/> 的 <c>long</c>/<c>ulong</c> 实现，跨机器、跨运行时
    /// （服务器 .NET 8 / Unity Mono / IL2CPP）逐比特一致。
    /// </summary>
    public readonly struct FP64 : IEquatable<FP64>, IComparable<FP64>
    {
        /// <summary>小数位数（固定为 32）。</summary>
        public const int FractionalBits = 32;

        /// <summary>数值 1 对应的 Raw（即 2^32）。</summary>
        public const long OneRaw = 1L << FractionalBits;

        /// <summary>数值 0.5 对应的 Raw（即 2^31）。</summary>
        public const long HalfRaw = 1L << (FractionalBits - 1);

        /// <summary>底层 Q32.32 原始位。</summary>
        public readonly long Raw;

        /// <summary>常量 0。</summary>
        public static readonly FP64 Zero = new(0);

        /// <summary>常量 1。</summary>
        public static readonly FP64 One = new(OneRaw);

        /// <summary>常量 -1。</summary>
        public static readonly FP64 NegOne = new(-OneRaw);

        /// <summary>常量 0.5。</summary>
        public static readonly FP64 Half = new(HalfRaw);

        /// <summary>最小正数（Raw = 1，约 2.3e-10）。</summary>
        public static readonly FP64 Epsilon = new(1);

        /// <summary>可表示的最小值（Raw = long.MinValue）。</summary>
        public static readonly FP64 MinValue = new(long.MinValue);

        /// <summary>可表示的最大值（Raw = long.MaxValue）。</summary>
        public static readonly FP64 MaxValue = new(long.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FP64(long raw) { Raw = raw; }

        /// <summary>
        /// 直接用 Q32.32 原始位构造 FP64，跳过任何缩放。用于反序列化或精细的常量定义。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 FromRaw(long raw) => new(raw);

        /// <summary>由整数构造 FP64。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 FromInt(int v) => new((long)v << FractionalBits);

        /// <summary>由 long 构造 FP64（注意：|v| 必须 &lt; 2^31，否则会溢出）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 FromLong(long v) => new(v << FractionalBits);

        /// <summary>
        /// 由 <see cref="float"/> 构造 FP64。使用 <see cref="Math.Round(double)"/>（银行家舍入），
        /// 输入相同时跨平台逐比特一致。
        /// </summary>
        public static FP64 FromFloat(float v) => new((long)Math.Round((double)v * OneRaw));

        /// <summary>
        /// 由 <see cref="double"/> 构造 FP64。使用银行家舍入，跨平台逐比特一致。
        /// </summary>
        public static FP64 FromDouble(double v) => new((long)Math.Round(v * OneRaw));

        /// <summary>
        /// 由 <c>num/den</c> 构造 FP64，使用一次整除避免 <c>FromInt(num) / FromInt(den)</c> 的累积舍入误差。
        /// 舍入方向为 floor（向 -∞），与 <c>operator /</c> 一致。
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="den"/> 为 0。</exception>
        public static FP64 FromFraction(int num, int den)
        {
            if (den == 0) throw new DivideByZeroException();
            long scaled = (long)num << FractionalBits;
            long q = scaled / den;
            long r = scaled % den;
            // Floor rounding to match operator / so negative fractions round
            // consistently across the library.
            if (r != 0 && ((scaled < 0) != (den < 0))) q--;
            return new FP64(q);
        }

        /// <summary>转换为 float（仅做表示性输出，不应在确定性逻辑中使用）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat() => (float)((double)Raw / OneRaw);

        /// <summary>转换为 double（仅做表示性输出，不应在确定性逻辑中使用）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble() => (double)Raw / OneRaw;

        /// <summary>
        /// 向下取整（向 -∞）到 int。等价于 <c>Math.Floor</c> 后转 int。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToIntFloor() => (int)(Raw >> FractionalBits);

        /// <summary>
        /// 截断取整（向 0）到 int。等价于 C# 的 <c>(int)double</c> 转换语义。
        /// </summary>
        public int ToIntTruncate()
        {
            return (int)(Raw >= 0 ? (Raw >> FractionalBits) : -((-Raw) >> FractionalBits));
        }

        // Arithmetic --------------------------------------------------------

        /// <summary>逐位加法（<c>a.Raw + b.Raw</c>）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 operator +(FP64 a, FP64 b) => new(a.Raw + b.Raw);

        /// <summary>逐位减法（<c>a.Raw - b.Raw</c>）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 operator -(FP64 a, FP64 b) => new(a.Raw - b.Raw);

        /// <summary>取负。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 operator -(FP64 a) => new(-a.Raw);

        /// <summary>
        /// 乘法 <c>(a.Raw * b.Raw) &gt;&gt; 32</c>。
        /// 64×64 全宽乘借由 <see cref="Math128.MulFull"/> 完成（schoolbook 32×32 拼装），
        /// 然后把 128 位结果算术右移 32 截到 <c>long</c>。
        /// 截位与 <see cref="Math128.Shr128ToLong"/> 中的算术右移一致即为 floor 舍入（向 -∞）。
        /// </summary>
        public static FP64 operator *(FP64 a, FP64 b)
        {
            Math128.MulFull(a.Raw, b.Raw, out ulong pHi, out ulong pLo);
            return new FP64(Math128.Shr128ToLong(pHi, pLo, FractionalBits));
        }

        /// <summary>
        /// 除法 <c>(a.Raw &lt;&lt; 32) / b.Raw</c>。
        /// 把分子构造成 128 位有符号 <c>(numHi, numLo)</c>，调用
        /// <see cref="Math128.DivU128By64"/> 做无符号长除法，最后按操作数符号回填，
        /// 并按余数做 floor 舍入修正（与 <c>operator *</c> / <c>FromFraction</c> 保持一致）。
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="b"/> 为 0。</exception>
        public static FP64 operator /(FP64 a, FP64 b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();

            // 全段 unchecked：二补码取负 (`~x + 1`)、ulong→long 隐式截取、`-(long)uQ`
            // 在 checked 上下文下都会抛 OverflowException；这里显式解耦，跨平台一致。
            unchecked
            {
                // 把 a.Raw 左移 32 位扩展为 128 位（含符号位）。
                ulong numLo = (ulong)a.Raw << FractionalBits;
                ulong numHi = (ulong)(a.Raw >> FractionalBits);

                bool numNeg = a.Raw < 0;
                bool denNeg = b.Raw < 0;
                bool resNeg = numNeg ^ denNeg;

                if (numNeg)
                {
                    // 128 位二补码取负：低半 +1，必要时把进位带到高半。
                    ulong newLo = ~numLo + 1UL;
                    ulong newHi = ~numHi + (newLo == 0UL ? 1UL : 0UL);
                    numLo = newLo;
                    numHi = newHi;
                }
                ulong uDen = denNeg ? (~(ulong)b.Raw + 1UL) : (ulong)b.Raw;

                ulong uQ = Math128.DivU128By64(numHi, numLo, uDen, out ulong rem);
                long q = resNeg ? -(long)uQ : (long)uQ;
                if (rem != 0UL && resNeg) q--;   // floor 修正
                return new FP64(q);
            }
        }

        /// <summary>
        /// 取模，余数符号与 <paramref name="b"/> 一致（floor 约定）。
        /// 满足恒等式 <c>a = floor(a/b) * b + (a % b)</c>，与 <c>operator /</c> 配套。
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="b"/> 为 0。</exception>
        public static FP64 operator %(FP64 a, FP64 b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();
            long r = a.Raw % b.Raw;
            if (r != 0 && ((a.Raw < 0) != (b.Raw < 0))) r += b.Raw;
            return new FP64(r);
        }

        // Comparison --------------------------------------------------------

        /// <summary>判等。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FP64 a, FP64 b) => a.Raw == b.Raw;

        /// <summary>判不等。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FP64 a, FP64 b) => a.Raw != b.Raw;

        /// <summary>小于。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FP64 a, FP64 b) => a.Raw < b.Raw;

        /// <summary>大于。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FP64 a, FP64 b) => a.Raw > b.Raw;

        /// <summary>小于等于。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FP64 a, FP64 b) => a.Raw <= b.Raw;

        /// <summary>大于等于。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FP64 a, FP64 b) => a.Raw >= b.Raw;

        /// <summary>判等。</summary>
        public bool Equals(FP64 other) => Raw == other.Raw;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is FP64 f && Raw == f.Raw;

        /// <inheritdoc/>
        public override int GetHashCode() => Raw.GetHashCode();

        /// <inheritdoc/>
        public int CompareTo(FP64 other) => Raw.CompareTo(other.Raw);

        /// <summary>使用不变区域格式化为不超过 10 位小数的字符串（仅用于日志和断言）。</summary>
        public override string ToString()
            => ToDouble().ToString("0.##########", CultureInfo.InvariantCulture);
    }
}
