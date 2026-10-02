// Narrowphase.cs
//
// Pairwise overlap tests producing a single LWContact. The contact normal
// always points from A toward B. Penetration is positive when overlapping.

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 精确阶段：对每一对候选体做精确的重叠检测，产出 <see cref="LWContact"/>。
    /// 约定法线指向 A→B，<c>Penetration</c> 在重叠时为正。
    /// </summary>
    public static class Narrowphase
    {
        /// <summary>
        /// 入口分派：根据两体的 Shape 类型分别走 Circle/Circle、Circle/AABB、AABB/AABB。
        /// </summary>
        public static bool Test(in LWBody a, in LWBody b, out LWContact contact)
        {
            if (a.Shape.Type == LWShapeType.Circle && b.Shape.Type == LWShapeType.Circle)
                return CircleVsCircle(a.Position, a.Shape.Radius, b.Position, b.Shape.Radius, out contact);

            if (a.Shape.Type == LWShapeType.Circle && b.Shape.Type == LWShapeType.AABB)
                return CircleVsAABB(a.Position, a.Shape.Radius, b.Position, b.Shape.HalfExtents, out contact, flipNormal: false);

            if (a.Shape.Type == LWShapeType.AABB && b.Shape.Type == LWShapeType.Circle)
                return CircleVsAABB(b.Position, b.Shape.Radius, a.Position, a.Shape.HalfExtents, out contact, flipNormal: true);

            return AABBVsAABB(a.Position, a.Shape.HalfExtents, b.Position, b.Shape.HalfExtents, out contact);
        }

        /// <summary>
        /// 圆与圆检测：两圆心距离平方与半径之和平方比较。
        /// 圆心重合时按确定性方向 (+X) 取法线。
        /// </summary>
        public static bool CircleVsCircle(
            in FPVector2 posA, FP64 rA,
            in FPVector2 posB, FP64 rB,
            out LWContact contact)
        {
            FPVector2 delta = posB - posA;
            FP64 distSq = delta.SqrMagnitude;
            FP64 r = rA + rB;
            FP64 rSq = r * r;
            if (distSq >= rSq)
            {
                contact = default;
                return false;
            }

            FPVector2 normal;
            FP64 dist;
            if (distSq.Raw == 0)
            {
                // Coincident centers: pick an arbitrary axis. Deterministic
                // (always +X) so snapshots remain reproducible.
                normal = FPVector2.UnitX;
                dist = FP64.Zero;
            }
            else
            {
                dist = FPMath.Sqrt(distSq);
                normal = delta / dist;
            }

            contact = new LWContact
            {
                Normal = normal,
                Penetration = r - dist,
                Point = posA + normal * rA,
            };
            return true;
        }

        /// <summary>
        /// 圆 (A) vs AABB (B)。<paramref name="flipNormal"/> 为 true 时表示
        /// <see cref="Test"/> 把 AABB 当成了 A、圆当成了 B，
        /// 因此需要把法线翻转以保持"A→B"的约定。
        /// </summary>
        public static bool CircleVsAABB(
            in FPVector2 circleCenter, FP64 circleRadius,
            in FPVector2 boxCenter, in FPVector2 boxHalf,
            out LWContact contact,
            bool flipNormal)
        {
            FPVector2 rel = circleCenter - boxCenter;
            FP64 clampedX = FPMath.Clamp(rel.X, -boxHalf.X, boxHalf.X);
            FP64 clampedY = FPMath.Clamp(rel.Y, -boxHalf.Y, boxHalf.Y);
            FPVector2 closestRel = new(clampedX, clampedY);

            FPVector2 diff = rel - closestRel; // from closest-on-box to circle-center
            FP64 distSq = diff.SqrMagnitude;
            FP64 rSq = circleRadius * circleRadius;

            FPVector2 normalCircleToBox; // pre-flip: from circle (A) to box (B)
            FP64 penetration;
            FPVector2 contactPoint;

            if (distSq.Raw == 0)
            {
                // Circle center is inside the AABB. Resolve along axis of least
                // remaining face distance.
                FP64 leftDist = rel.X + boxHalf.X;   // toward -X face
                FP64 rightDist = boxHalf.X - rel.X;  // toward +X face
                FP64 bottomDist = rel.Y + boxHalf.Y; // toward -Y face
                FP64 topDist = boxHalf.Y - rel.Y;    // toward +Y face

                FP64 minX = leftDist < rightDist ? leftDist : rightDist;
                FP64 minY = bottomDist < topDist ? bottomDist : topDist;
                if (minX < minY)
                {
                    FP64 sign = leftDist < rightDist ? FP64.NegOne : FP64.One;
                    normalCircleToBox = new FPVector2(-sign, FP64.Zero);
                    penetration = circleRadius + minX;
                    contactPoint = boxCenter + new FPVector2(sign * boxHalf.X, rel.Y);
                }
                else
                {
                    FP64 sign = bottomDist < topDist ? FP64.NegOne : FP64.One;
                    normalCircleToBox = new FPVector2(FP64.Zero, -sign);
                    penetration = circleRadius + minY;
                    contactPoint = boxCenter + new FPVector2(rel.X, sign * boxHalf.Y);
                }
            }
            else
            {
                if (distSq >= rSq)
                {
                    contact = default;
                    return false;
                }
                FP64 dist = FPMath.Sqrt(distSq);
                FPVector2 circleToBox = -diff / dist; // box is opposite of `diff` direction
                normalCircleToBox = circleToBox;
                penetration = circleRadius - dist;
                contactPoint = boxCenter + closestRel;
            }

            FPVector2 normal = flipNormal ? -normalCircleToBox : normalCircleToBox;
            contact = new LWContact
            {
                Normal = normal,
                Penetration = penetration,
                Point = contactPoint,
            };
            return true;
        }

        /// <summary>
        /// AABB vs AABB：选 X/Y 两轴中 overlap 较小的一轴作为分离方向。
        /// </summary>
        public static bool AABBVsAABB(
            in FPVector2 posA, in FPVector2 halfA,
            in FPVector2 posB, in FPVector2 halfB,
            out LWContact contact)
        {
            FPVector2 delta = posB - posA;
            FP64 overlapX = (halfA.X + halfB.X) - FPMath.Abs(delta.X);
            FP64 overlapY = (halfA.Y + halfB.Y) - FPMath.Abs(delta.Y);
            if (overlapX.Raw <= 0 || overlapY.Raw <= 0)
            {
                contact = default;
                return false;
            }

            if (overlapX < overlapY)
            {
                FP64 sign = delta.X.Raw >= 0 ? FP64.One : FP64.NegOne;
                contact = new LWContact
                {
                    Normal = new FPVector2(sign, FP64.Zero),
                    Penetration = overlapX,
                    Point = posA + new FPVector2(sign * halfA.X, FP64.Zero),
                };
            }
            else
            {
                FP64 sign = delta.Y.Raw >= 0 ? FP64.One : FP64.NegOne;
                contact = new LWContact
                {
                    Normal = new FPVector2(FP64.Zero, sign),
                    Penetration = overlapY,
                    Point = posA + new FPVector2(FP64.Zero, sign * halfA.Y),
                };
            }
            return true;
        }
    }
}
