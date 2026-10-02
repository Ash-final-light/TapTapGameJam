// Sweep.cs
//
// Shape casts: move a Circle or AABB through the world and report the first
// body hit along the direction.
//
// Implementation uses the Minkowski equivalence: sweeping shape A through a
// static shape B is the same as a ray from A's center through the Minkowski
// sum (B + reflect(A)). For our shapes:
//   - Circle vs Circle  -> ray vs circle of radius (rA + rB)
//   - Circle vs AABB    -> ray vs AABB inflated by rA (with arc corners)
//   - AABB   vs Circle  -> ray vs AABB inflated by rB (with arc corners) [Minkowski symmetry]
//   - AABB   vs AABB    -> ray vs AABB inflated by halfA (no arcs needed)
//
// When the world's broadphase exposes ISpatialIndex we trim the candidate
// body set down to the cells covering the swept volume's AABB. Bodies are
// already indexed by their own AABB so we only need to inflate the segment
// by the moving shape's extents -- target body extents are absorbed by the
// grid's per-body cell coverage.

using System.Collections.Generic;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 形状扫掠（CircleCast / BoxCast）：让一个 Circle 或 AABB 沿方向移动并报告第一个命中。
    /// 实现基于 Minkowski 等价：A 向方向扫过 B 等价于 A 的中心做一条射线 vs (B + reflect(A))。
    /// 当 broadphase 提供 <see cref="ISpatialIndex"/> 时按扫掠包围盒做候选裁剪，
    /// 否则退化为对全体的线性扫描。
    /// </summary>
    public static class Sweep
    {
        /// <summary>每线程的候选索引复用缓冲。</summary>
        [System.ThreadStatic] private static List<int> tCandidates;

        /// <summary>取得（必要时分配）当前线程的候选列表，并先清空。</summary>
        private static List<int> AcquireCandidates()
        {
            if (tCandidates == null) tCandidates = new List<int>(16);
            tCandidates.Clear();
            return tCandidates;
        }

        /// <summary>圆形扫掠（mask = AllLayers）。</summary>
        public static bool CircleCast(
            LWPhysicsWorld world,
            in FPVector2 origin, FP64 radius,
            in FPVector2 direction, FP64 maxDistance,
            out LWSweepHit hit)
            => CircleCast(world, in origin, radius, in direction, maxDistance, LWLayerMask.AllLayers, out hit);

        /// <summary>
        /// 圆形扫掠：以 <paramref name="origin"/> 为圆心、<paramref name="radius"/> 为半径的圆，
        /// 沿 <paramref name="direction"/>（自动归一化）移动至多 <paramref name="maxDistance"/>，
        /// 返回最近命中。
        /// </summary>
        public static bool CircleCast(
            LWPhysicsWorld world,
            in FPVector2 origin, FP64 radius,
            in FPVector2 direction, FP64 maxDistance,
            ushort layerMask,
            out LWSweepHit hit)
        {
            hit = default;
            FP64 dirLen = direction.Magnitude;
            if (dirLen.Raw == 0) return false;
            FPVector2 dirNorm = direction / dirLen;

            FP64 bestT = maxDistance;
            bool found = false;

            LWBody[] bodies = world.BodiesArray;
            ISpatialIndex idx = world.PrepareSpatialIndex();
            FPVector2 inflation = new(radius, radius);

            if (idx != null)
            {
                List<int> cand = AcquireCandidates();
                idx.QuerySegmentAABB(in origin, in dirNorm, maxDistance,
                                     in inflation, cand);
                for (int k = 0; k < cand.Count; k++)
                {
                    if (TryCircleCastAgainst(in origin, radius, in dirNorm, bestT, layerMask,
                                              in bodies[cand[k]], out FP64 t, out FPVector2 p, out FPVector2 n)
                        && t < bestT)
                    {
                        bestT = t;
                        hit = new LWSweepHit
                        {
                            BodyId = bodies[cand[k]].Id,
                            Distance = t,
                            Point = p,
                            Normal = n,
                        };
                        found = true;
                    }
                }
            }
            else
            {
                int cap = world.BodyCapacity;
                for (int i = 0; i < cap; i++)
                {
                    if (TryCircleCastAgainst(in origin, radius, in dirNorm, bestT, layerMask,
                                              in bodies[i], out FP64 t, out FPVector2 p, out FPVector2 n)
                        && t < bestT)
                    {
                        bestT = t;
                        hit = new LWSweepHit
                        {
                            BodyId = bodies[i].Id,
                            Distance = t,
                            Point = p,
                            Normal = n,
                        };
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>Alive / Layer 过滤后把圆扫掠分派到对应的 primitive sweep。</summary>
        private static bool TryCircleCastAgainst(
            in FPVector2 origin, FP64 radius,
            in FPVector2 dirNorm, FP64 maxDist, ushort layerMask,
            in LWBody body,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = default; point = default; normal = default;
            if ((body.Flags & LWBodyFlags.Alive) == 0) return false;
            if ((body.Layer & layerMask) == 0) return false;
            if (body.Shape.Type == LWShapeType.Circle)
            {
                return CircleVsCircle(in origin, radius, in dirNorm, maxDist,
                                       body.Position, body.Shape.Radius,
                                       out t, out point, out normal);
            }
            return MovingCircleVsStaticAABB(in origin, radius, in dirNorm, maxDist,
                                             body.Position, body.Shape.HalfExtents,
                                             out t, out point, out normal);
        }

        /// <summary>AABB 扫掠（mask = AllLayers）。</summary>
        public static bool BoxCast(
            LWPhysicsWorld world,
            in FPVector2 origin, in FPVector2 halfExtents,
            in FPVector2 direction, FP64 maxDistance,
            out LWSweepHit hit)
            => BoxCast(world, in origin, in halfExtents, in direction, maxDistance, LWLayerMask.AllLayers, out hit);

        /// <summary>
        /// AABB 扫掠：以 <paramref name="origin"/> 为中心、<paramref name="halfExtents"/> 为半边长的 AABB，
        /// 沿 <paramref name="direction"/> 移动至多 <paramref name="maxDistance"/>，返回最近命中。
        /// </summary>
        public static bool BoxCast(
            LWPhysicsWorld world,
            in FPVector2 origin, in FPVector2 halfExtents,
            in FPVector2 direction, FP64 maxDistance,
            ushort layerMask,
            out LWSweepHit hit)
        {
            hit = default;
            FP64 dirLen = direction.Magnitude;
            if (dirLen.Raw == 0) return false;
            FPVector2 dirNorm = direction / dirLen;

            FP64 bestT = maxDistance;
            bool found = false;

            LWBody[] bodies = world.BodiesArray;
            ISpatialIndex idx = world.PrepareSpatialIndex();

            if (idx != null)
            {
                List<int> cand = AcquireCandidates();
                idx.QuerySegmentAABB(in origin, in dirNorm, maxDistance,
                                     in halfExtents, cand);
                for (int k = 0; k < cand.Count; k++)
                {
                    if (TryBoxCastAgainst(in origin, in halfExtents, in dirNorm, bestT, layerMask,
                                           in bodies[cand[k]], out FP64 t, out FPVector2 p, out FPVector2 n)
                        && t < bestT)
                    {
                        bestT = t;
                        hit = new LWSweepHit
                        {
                            BodyId = bodies[cand[k]].Id,
                            Distance = t,
                            Point = p,
                            Normal = n,
                        };
                        found = true;
                    }
                }
            }
            else
            {
                int cap = world.BodyCapacity;
                for (int i = 0; i < cap; i++)
                {
                    if (TryBoxCastAgainst(in origin, in halfExtents, in dirNorm, bestT, layerMask,
                                           in bodies[i], out FP64 t, out FPVector2 p, out FPVector2 n)
                        && t < bestT)
                    {
                        bestT = t;
                        hit = new LWSweepHit
                        {
                            BodyId = bodies[i].Id,
                            Distance = t,
                            Point = p,
                            Normal = n,
                        };
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>Alive / Layer 过滤后把 AABB 扫掠分派到对应的 primitive sweep。</summary>
        private static bool TryBoxCastAgainst(
            in FPVector2 origin, in FPVector2 halfExtents,
            in FPVector2 dirNorm, FP64 maxDist, ushort layerMask,
            in LWBody body,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = default; point = default; normal = default;
            if ((body.Flags & LWBodyFlags.Alive) == 0) return false;
            if ((body.Layer & layerMask) == 0) return false;
            if (body.Shape.Type == LWShapeType.AABB)
            {
                return AABBVsAABB(in origin, in halfExtents, in dirNorm, maxDist,
                                  body.Position, body.Shape.HalfExtents,
                                  out t, out point, out normal);
            }
            return MovingAABBVsStaticCircle(in origin, in halfExtents, in dirNorm, maxDist,
                                             body.Position, body.Shape.Radius,
                                             out t, out point, out normal);
        }

        // --- Primitive sweeps ---------------------------------------------

        /// <summary>
        /// 圆 (A) 扫掠 vs 静态圆 (B)：等价于射线 vs 半径为 (rA + rB) 的圆。
        /// 法线为 B 表面外向，命中点位于 B 表面。
        /// </summary>
        public static bool CircleVsCircle(
            in FPVector2 origin, FP64 rA,
            in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 center, FP64 rB,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = FP64.Zero; point = default; normal = default;
            FP64 combined = rA + rB;
            if (!Raycast.RayCircle(in origin, in dirNorm, maxDist, in center, combined,
                                   out FP64 ht, out FPVector2 _, out FPVector2 _))
                return false;

            t = ht;
            FPVector2 sweptCenter = origin + dirNorm * ht;
            FPVector2 outwardFromB = (sweptCenter - center) / combined;
            normal = outwardFromB;
            point = center + outwardFromB * rB;
            return true;
        }

        /// <summary>
        /// 圆 (A) 扫掠 vs 静态 AABB。等价于射线 vs 被 rA 膨胀的 AABB（四角带圆弧）。
        /// </summary>
        public static bool MovingCircleVsStaticAABB(
            in FPVector2 origin, FP64 rA,
            in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 boxCenter, in FPVector2 baseHalf,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            return RayVsInflatedAABB(
                in origin, in dirNorm, maxDist,
                in boxCenter, in baseHalf, rA,
                isMovingShapeCircle: true,
                staticIsAABB: true,
                rB: FP64.Zero,
                out t, out point, out normal);
        }

        /// <summary>
        /// AABB (A) 扫掠 vs 静态圆 (B)。与 <see cref="MovingCircleVsStaticAABB"/> 的 Minkowski 形状一致，
        /// 但"运动/静态"角色互换；命中点落在圆 (B) 的表面上。
        /// </summary>
        public static bool MovingAABBVsStaticCircle(
            in FPVector2 origin, in FPVector2 halfA,
            in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 circleCenter, FP64 rB,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            return RayVsInflatedAABB(
                in origin, in dirNorm, maxDist,
                in circleCenter, in halfA, rB,
                isMovingShapeCircle: false,
                staticIsAABB: false,
                rB: rB,
                out t, out point, out normal);
        }

        /// <summary>
        /// AABB (A) 扫掠 vs 静态 AABB (B)：等价于从运动中心出发的射线 vs 被 halfA 膨胀的 B。
        /// </summary>
        public static bool AABBVsAABB(
            in FPVector2 origin, in FPVector2 halfA,
            in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 boxCenter, in FPVector2 halfB,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = FP64.Zero; point = default; normal = default;
            FPVector2 inflatedHalf = halfA + halfB;
            if (!Raycast.RayAABB(in origin, in dirNorm, maxDist,
                                 in boxCenter, in inflatedHalf,
                                 out FP64 ht, out FPVector2 _, out FPVector2 hn))
                return false;
            t = ht;
            normal = hn;
            FPVector2 sweptCenter = origin + dirNorm * ht;
            // Contact point: clamp the moving box center onto the static AABB
            // face along the slab axis.
            if (hn.X.Raw != 0)
            {
                FP64 px = boxCenter.X + (hn.X.Raw > 0 ? halfB.X : -halfB.X);
                FP64 py = FPMath.Clamp(sweptCenter.Y, boxCenter.Y - halfB.Y, boxCenter.Y + halfB.Y);
                point = new FPVector2(px, py);
            }
            else
            {
                FP64 py = boxCenter.Y + (hn.Y.Raw > 0 ? halfB.Y : -halfB.Y);
                FP64 px = FPMath.Clamp(sweptCenter.X, boxCenter.X - halfB.X, boxCenter.X + halfB.X);
                point = new FPVector2(px, py);
            }
            return true;
        }

        /// <summary>
        /// 圆与 AABB 互扫的共享实现：射线 vs 带圆角的膨胀 AABB。
        /// <list type="bullet">
        /// <item><paramref name="center"/>：Minkowski 目标的中心（静态体中心）。</item>
        /// <item><paramref name="baseHalf"/>：矩形核的半边长。</item>
        /// <item><paramref name="arcRadius"/>：四角圆弧半径（同时是膨胀厚度）。</item>
        /// <item><paramref name="isMovingShapeCircle"/>：true 表示运动形状是圆（命中点贴在静态 AABB 面上）；
        /// false 表示运动是 AABB，静态是圆（命中点贴在静态圆表面）。</item>
        /// </list>
        /// </summary>
        private static bool RayVsInflatedAABB(
            in FPVector2 origin, in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 center, in FPVector2 baseHalf, FP64 arcRadius,
            bool isMovingShapeCircle,
            bool staticIsAABB,
            FP64 rB,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = FP64.Zero; point = default; normal = default;

            FPVector2 inflatedHalf = new(baseHalf.X + arcRadius, baseHalf.Y + arcRadius);
            if (!Raycast.RayAABB(in origin, in dirNorm, maxDist,
                                 in center, in inflatedHalf,
                                 out FP64 slabT, out FPVector2 slabPoint, out FPVector2 slabNormal))
                return false;

            // Check whether the slab hit is in a corner region of the rounded
            // rectangle. If yes, refine against the appropriate corner arc.
            FPVector2 rel = slabPoint - center;
            FP64 absX = FPMath.Abs(rel.X);
            FP64 absY = FPMath.Abs(rel.Y);
            if (absX > baseHalf.X && absY > baseHalf.Y)
            {
                FPVector2 corner = center + new FPVector2(
                    rel.X.Raw >= 0 ? baseHalf.X : -baseHalf.X,
                    rel.Y.Raw >= 0 ? baseHalf.Y : -baseHalf.Y);
                if (!Raycast.RayCircle(in origin, in dirNorm, maxDist,
                                       in corner, arcRadius,
                                       out FP64 cT, out FPVector2 _, out FPVector2 cN))
                    return false;
                t = cT;
                FPVector2 sweptCenter = origin + dirNorm * cT;
                if (isMovingShapeCircle && staticIsAABB)
                {
                    // Static shape is the AABB; contact point is the corner.
                    normal = cN;
                    point = corner;
                }
                else
                {
                    // Static shape is a circle of radius rB; contact point is
                    // on the circle's surface.
                    normal = cN;
                    point = center + cN * rB;
                }
                return true;
            }

            t = slabT;
            FPVector2 sweptCenter2 = origin + dirNorm * slabT;
            if (isMovingShapeCircle && staticIsAABB)
            {
                // Static AABB face hit; project the moving circle center onto
                // that face for the contact point.
                normal = slabNormal;
                FPVector2 boxMin = center - baseHalf;
                FPVector2 boxMax = center + baseHalf;
                if (slabNormal.X.Raw != 0)
                {
                    FP64 px = slabNormal.X.Raw > 0 ? boxMax.X : boxMin.X;
                    FP64 py = FPMath.Clamp(sweptCenter2.Y, boxMin.Y, boxMax.Y);
                    point = new FPVector2(px, py);
                }
                else
                {
                    FP64 py = slabNormal.Y.Raw > 0 ? boxMax.Y : boxMin.Y;
                    FP64 px = FPMath.Clamp(sweptCenter2.X, boxMin.X, boxMax.X);
                    point = new FPVector2(px, py);
                }
            }
            else
            {
                // Static circle face-region hit. Normal is the slab axis; the
                // contact point sits on the circle.
                normal = slabNormal;
                point = center + slabNormal * rB;
            }
            return true;
        }
    }
}
