// Math128.cs
//
// Minimal 128-bit integer helpers built on top of `long` / `ulong` so that
// FP64 / FPMath stay portable to Unity (Mono / IL2CPP, .NET Standard 2.1)
// where System.Int128 is not available.
//
// Determinism requirements:
//   * Pure integer arithmetic and bitwise ops only. ECMA-335 / C# spec
//     guarantees bit-exact behaviour on every conforming runtime
//     (CoreCLR / Mono / IL2CPP / NativeAOT, x64 / ARM64 / 32-bit ARM).
//   * Every operation that relies on two's-complement wrap-around
//     (`~x + 1`, `low - d` underflow, large additions inside `MulFull`,
//     ulong→long narrowing for values > long.MaxValue) is wrapped in an
//     explicit `unchecked { ... }` block so it is immune to callers that
//     compile with `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>`
//     or invoke us from inside a `checked { ... }` region.
//
// We only need two primitives:
//   1) 64×64 → 128 signed multiply (for FP64 * and the lookup interpolation
//      paths in FPMath).
//   2) 128 ÷ 64 → 64 unsigned division with remainder (for FP64 /, Sqrt, the
//      LUT index calculation, and MakeRatio).

using System;

namespace Framework.FixedPoint
{
    /// <summary>
    /// FP64 / FPMath 内部使用的 128 位整数辅助：所有运算都只依赖 <see cref="long"/> /
    /// <see cref="ulong"/>，因此可同时跑在 .NET 8（服务器）与 Unity Mono / IL2CPP
    /// （.NET Standard 2.1）上，不依赖 .NET 7+ 的 <c>System.Int128</c>。
    /// 所有需要二补码 wrap-around 的步骤显式 <c>unchecked</c>，
    /// 与调用方的 <c>checked</c>/<c>unchecked</c> 上下文解耦，跨运行时位级一致。
    /// </summary>
    internal static class Math128
    {
        /// <summary>
        /// 有符号 64×64 → 128 乘法，结果以 <c>(high, low)</c> 二元组形式返回，
        /// 满足 <c>value = ((long)high) * 2^64 + low</c>（128 位二补码）。
        /// </summary>
        internal static void MulFull(long a, long b, out ulong high, out ulong low)
        {
            // 全段显式 unchecked：取负公式 `~x + 1`（x==0 时 wraparound 到 0）、
            // ulong→long 隐式转换、以及 high 的累加在 checked 上下文里都会抛 OverflowException。
            unchecked
            {
                bool neg = (a < 0) ^ (b < 0);
                ulong ua = a < 0 ? (~(ulong)a + 1UL) : (ulong)a;
                ulong ub = b < 0 ? (~(ulong)b + 1UL) : (ulong)b;

                // Schoolbook 32x32 multiplications, each fits in 64 bit.
                ulong aLo = ua & 0xFFFFFFFFUL;
                ulong aHi = ua >> 32;
                ulong bLo = ub & 0xFFFFFFFFUL;
                ulong bHi = ub >> 32;

                ulong p00 = aLo * bLo;
                ulong p01 = aLo * bHi;
                ulong p10 = aHi * bLo;
                ulong p11 = aHi * bHi;

                ulong middle = (p00 >> 32) + (p01 & 0xFFFFFFFFUL) + (p10 & 0xFFFFFFFFUL);
                low = (p00 & 0xFFFFFFFFUL) | (middle << 32);
                high = p11 + (p01 >> 32) + (p10 >> 32) + (middle >> 32);

                if (neg)
                {
                    // Two's-complement negation of the 128-bit value.
                    ulong newLow = ~low + 1UL;
                    ulong newHigh = ~high + (newLow == 0UL ? 1UL : 0UL);
                    low = newLow;
                    high = newHigh;
                }
            }
        }

        /// <summary>
        /// 把 128 位有符号值 <c>(high, low)</c> 算术右移 <paramref name="shift"/> 位后
        /// 截取低 64 位（与 <c>(long)(Int128 &gt;&gt; shift)</c> 行为等价）。
        /// 仅支持 <c>0 &lt;= shift &lt;= 63</c>，覆盖本库的所有调用点。
        /// </summary>
        internal static long Shr128ToLong(ulong high, ulong low, int shift)
        {
            // ulong → long 在 checked 下对 > long.MaxValue 的位模式会抛。
            unchecked
            {
                if (shift == 0) return (long)low;
                // bits from `high` fill the upper part of the result; bits from `low`
                // get pushed down into the lower part.
                ulong combined = (low >> shift) | (high << (64 - shift));
                return (long)combined;
            }
        }

        /// <summary>
        /// 无符号 128÷64 → 64 整除，附带 64 位余数。
        /// 实现采用比特级长除法（128 次迭代），简单可靠；性能足以满足客户端预测路径。
        /// 调用方需保证商能装进 64 位（即 <paramref name="nHi"/> &lt; <paramref name="d"/>），
        /// 否则商高 64 位会被丢弃。
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="d"/> 为 0。</exception>
        internal static ulong DivU128By64(ulong nHi, ulong nLo, ulong d, out ulong remainder)
        {
            if (d == 0UL) throw new DivideByZeroException();

            // Fast path: dividend fits in 64 bits, fall through to native /, %.
            if (nHi == 0UL)
            {
                remainder = nLo % d;
                return nLo / d;
            }

            // 长除法主循环：`newR - d` 在 topBit 被吃掉时是 ulong 下的 wraparound 减法
            // （数学上 (2r+bit) - d ≥ 0，因为 2r+bit 是 65 位整数且 ≥ d；在 ulong 里
            // 表现为 underflow，需要显式 unchecked 绕开 checked 检查）。
            unchecked
            {
                ulong q = 0UL;
                ulong r = 0UL;

                // High 64 bits: only updates `r`, any quotient bit set here would
                // overflow 64 bits and is discarded (caller's contract).
                for (int i = 63; i >= 0; i--)
                {
                    ulong bit = (nHi >> i) & 1UL;
                    ulong topBitOfR = r >> 63;
                    ulong newR = (r << 1) | bit;
                    if (topBitOfR != 0UL || newR >= d)
                    {
                        r = newR - d;
                    }
                    else
                    {
                        r = newR;
                    }
                }

                // Low 64 bits: now record quotient bits.
                for (int i = 63; i >= 0; i--)
                {
                    ulong bit = (nLo >> i) & 1UL;
                    ulong topBitOfR = r >> 63;
                    ulong newR = (r << 1) | bit;
                    if (topBitOfR != 0UL || newR >= d)
                    {
                        r = newR - d;
                        q |= 1UL << i;
                    }
                    else
                    {
                        r = newR;
                    }
                }

                remainder = r;
                return q;
            }
        }
    }
}
