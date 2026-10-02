// LWShape.cs
//
// Discriminated 2D shape: Circle or AABB.

using System;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>形状类型枚举。</summary>
    public enum LWShapeType : byte
    {
        /// <summary>圆形。</summary>
        Circle = 0,

        /// <summary>轴对齐矩形 (Axis-Aligned Bounding Box)。</summary>
        AABB = 1,
    }

    /// <summary>
    /// 标签联合体形状：根据 <see cref="Type"/> 分别使用 <see cref="Radius"/>（圆）
    /// 或 <see cref="HalfExtents"/>（AABB）。
    /// </summary>
    public struct LWShape : IEquatable<LWShape>
    {
        /// <summary>形状种类。</summary>
        public LWShapeType Type;

        /// <summary>圆半径（仅当 <see cref="Type"/> 为 <see cref="LWShapeType.Circle"/> 时有效）。</summary>
        public FP64 Radius;

        /// <summary>AABB 半边长（仅当 <see cref="Type"/> 为 <see cref="LWShapeType.AABB"/> 时有效）。</summary>
        public FPVector2 HalfExtents;

        /// <summary>构造一个圆形。</summary>
        public static LWShape Circle(FP64 radius)
        {
            return new LWShape
            {
                Type = LWShapeType.Circle,
                Radius = radius,
                HalfExtents = FPVector2.Zero,
            };
        }

        /// <summary>构造一个 AABB（半边长向量形式）。</summary>
        public static LWShape AABB(FPVector2 halfExtents)
        {
            return new LWShape
            {
                Type = LWShapeType.AABB,
                Radius = FP64.Zero,
                HalfExtents = halfExtents,
            };
        }

        /// <summary>构造一个 AABB（分量形式）。</summary>
        public static LWShape AABB(FP64 halfWidth, FP64 halfHeight)
            => AABB(new FPVector2(halfWidth, halfHeight));

        /// <summary>
        /// 逐字段判等。注意：不同 <see cref="Type"/> 的形状一律不相等；
        /// 同 Type 时只比较语义有效的字段（另一字段由工厂置零保证一致）。
        /// </summary>
        public bool Equals(LWShape other)
            => Type == other.Type
            && Radius == other.Radius
            && HalfExtents == other.HalfExtents;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is LWShape s && Equals(s);

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine((byte)Type, Radius.Raw, HalfExtents.X.Raw, HalfExtents.Y.Raw);

        /// <summary>判等。</summary>
        public static bool operator ==(LWShape a, LWShape b) => a.Equals(b);

        /// <summary>判不等。</summary>
        public static bool operator !=(LWShape a, LWShape b) => !a.Equals(b);
    }
}
