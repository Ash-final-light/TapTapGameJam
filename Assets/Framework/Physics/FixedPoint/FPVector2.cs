// FPVector2.cs
//
// 2D vector backed by two FP64 components. All operations are deterministic.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Framework.FixedPoint
{
    /// <summary>
    /// 由两个 <see cref="FP64"/> 分量构成的 2D 向量，所有运算都是逐比特确定的。
    /// </summary>
    public struct FPVector2 : IEquatable<FPVector2>
    {
        /// <summary>X 分量。</summary>
        public FP64 X;

        /// <summary>Y 分量。</summary>
        public FP64 Y;

        /// <summary>零向量 (0, 0)。</summary>
        public static readonly FPVector2 Zero = new(FP64.Zero, FP64.Zero);

        /// <summary>一向量 (1, 1)。</summary>
        public static readonly FPVector2 One = new(FP64.One, FP64.One);

        /// <summary>X 轴单位向量 (1, 0)。</summary>
        public static readonly FPVector2 UnitX = new(FP64.One, FP64.Zero);

        /// <summary>Y 轴单位向量 (0, 1)。</summary>
        public static readonly FPVector2 UnitY = new(FP64.Zero, FP64.One);

        /// <summary>构造指定分量的向量。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPVector2(FP64 x, FP64 y) { X = x; Y = y; }

        /// <summary>由整数分量构造向量。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 FromInt(int x, int y) => new(FP64.FromInt(x), FP64.FromInt(y));

        /// <summary>由 <see cref="float"/> 分量构造向量。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 FromFloat(float x, float y) => new(FP64.FromFloat(x), FP64.FromFloat(y));

        // Arithmetic --------------------------------------------------------

        /// <summary>逐分量加法。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator +(FPVector2 a, FPVector2 b) => new(a.X + b.X, a.Y + b.Y);

        /// <summary>逐分量减法。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator -(FPVector2 a, FPVector2 b) => new(a.X - b.X, a.Y - b.Y);

        /// <summary>取反。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator -(FPVector2 a) => new(-a.X, -a.Y);

        /// <summary>向量乘标量。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator *(FPVector2 a, FP64 s) => new(a.X * s, a.Y * s);

        /// <summary>标量乘向量。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator *(FP64 s, FPVector2 a) => new(a.X * s, a.Y * s);

        /// <summary>向量除标量。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator /(FPVector2 a, FP64 s) => new(a.X / s, a.Y / s);

        // Geometry ----------------------------------------------------------

        /// <summary>点积 <c>a.X*b.X + a.Y*b.Y</c>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 Dot(FPVector2 a, FPVector2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>
        /// 2D 叉积，返回三维叉积的 z 分量 <c>a.X*b.Y - a.Y*b.X</c>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 Cross(FPVector2 a, FPVector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>长度的平方（无开方，建议优先用于比较）。</summary>
        public FP64 SqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => X * X + Y * Y;
        }

        /// <summary>长度（调用 <see cref="FPMath.Sqrt"/>，确定性）。</summary>
        public FP64 Magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => FPMath.Sqrt(SqrMagnitude);
        }

        /// <summary>归一化向量；当长度为 0 时返回零向量。</summary>
        public FPVector2 Normalized
        {
            get
            {
                FP64 m = Magnitude;
                if (m.Raw == 0) return Zero;
                return new FPVector2(X / m, Y / m);
            }
        }

        /// <summary>两点距离。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 Distance(FPVector2 a, FPVector2 b) => (a - b).Magnitude;

        /// <summary>两点距离的平方（无开方，建议优先用于比较）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FP64 DistanceSqr(FPVector2 a, FPVector2 b) => (a - b).SqrMagnitude;

        /// <summary>
        /// 关于由 <paramref name="normal"/> 定义的平面镜像 <paramref name="v"/>。
        /// 法线需是单位向量。
        /// </summary>
        public static FPVector2 Reflect(FPVector2 v, FPVector2 normal)
        {
            FP64 d = Dot(v, normal);
            FP64 two = FP64.FromInt(2);
            return v - normal * (d * two);
        }

        // Equality ----------------------------------------------------------

        /// <summary>判等。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPVector2 a, FPVector2 b) => a.X == b.X && a.Y == b.Y;

        /// <summary>判不等。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPVector2 a, FPVector2 b) => a.X != b.X || a.Y != b.Y;

        /// <summary>判等。</summary>
        public bool Equals(FPVector2 other) => X == other.X && Y == other.Y;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is FPVector2 v && Equals(v);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw);

        /// <summary>格式化为 <c>(x, y)</c>。</summary>
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
    }
}
