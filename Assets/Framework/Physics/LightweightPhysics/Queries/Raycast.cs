// Raycast.cs
//
// Ray queries against the bodies in an LWPhysicsWorld. Direction is
// normalized internally so callers may pass any non-zero vector.
//
// When the world's broadphase exposes ISpatialIndex (UniformGridBroadphase
// today) we narrow the candidate set down to the cells covered by the ray's
// AABB. For a brute-force broadphase or any other implementation that does
// not expose a spatial index we fall back to the linear body scan.

using System.Collections.Generic;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 针对 <see cref="LWPhysicsWorld"/> 中刚体的射线查询。
    /// 内部会对方向做归一化，调用方可传任意非零向量。
    /// 当 broadphase 提供 <see cref="ISpatialIndex"/> 时走候选裁剪路径，
    /// 否则退化为对全体的线性扫描。
    /// </summary>
    public static class Raycast
    {
        /// <summary>
        /// 每线程的候选索引复用缓冲。每次 Cast/CastAll 调用前会先清空，
        /// 因此不可在嵌套调用中复用；本文件中的辅助函数不会嵌套触发本路径。
        /// </summary>
        [System.ThreadStatic] private static List<int> tCandidates;

        /// <summary>取得（必要时分配）当前线程的候选列表，并先清空。</summary>
        private static List<int> AcquireCandidates()
        {
            if (tCandidates == null) tCandidates = new List<int>(16);
            tCandidates.Clear();
            return tCandidates;
        }

        /// <summary>
        /// 投射一条射线，返回最近的命中点（mask = AllLayers）。
        /// </summary>
        public static bool Cast(
            LWPhysicsWorld world,
            in FPVector2 origin,
            in FPVector2 direction,
            FP64 maxDistance,
            out LWRaycastHit hit)
            => Cast(world, in origin, in direction, maxDistance, LWLayerMask.AllLayers, out hit);

        /// <summary>
        /// 投射一条射线，返回最近的命中点。
        /// </summary>
        /// <param name="world">目标物理世界。</param>
        /// <param name="origin">射线起点（世界坐标）。</param>
        /// <param name="direction">射线方向（非零即可，内部会归一化）。</param>
        /// <param name="maxDistance">允许命中的最大距离。</param>
        /// <param name="layerMask">参与检测的 Layer 掩码。</param>
        /// <param name="hit">最近命中点（仅当返回 true 时有效）。</param>
        /// <returns>是否命中。</returns>
        public static bool Cast(
            LWPhysicsWorld world,
            in FPVector2 origin,
            in FPVector2 direction,
            FP64 maxDistance,
            ushort layerMask,
            out LWRaycastHit hit)
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
                                     FPVector2.Zero, cand);
                for (int k = 0; k < cand.Count; k++)
                {
                    if (TestBody(in origin, in dirNorm, bestT, layerMask,
                                 in bodies[cand[k]], out FP64 t, out FPVector2 p, out FPVector2 n)
                        && t < bestT)
                    {
                        bestT = t;
                        hit = new LWRaycastHit
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
                    if (TestBody(in origin, in dirNorm, bestT, layerMask,
                                 in bodies[i], out FP64 t, out FPVector2 p, out FPVector2 n)
                        && t < bestT)
                    {
                        bestT = t;
                        hit = new LWRaycastHit
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

        /// <summary>
        /// 投射一条射线，把所有命中体追加到 <paramref name="results"/> 并按距离升序（mask = AllLayers）。
        /// </summary>
        /// <returns>命中数量。</returns>
        public static int CastAll(
            LWPhysicsWorld world,
            in FPVector2 origin,
            in FPVector2 direction,
            FP64 maxDistance,
            List<LWRaycastHit> results)
            => CastAll(world, in origin, in direction, maxDistance, LWLayerMask.AllLayers, results);

        /// <summary>
        /// 投射一条射线，把所有命中体追加到 <paramref name="results"/> 并按距离升序。
        /// </summary>
        /// <returns>命中数量。</returns>
        public static int CastAll(
            LWPhysicsWorld world,
            in FPVector2 origin,
            in FPVector2 direction,
            FP64 maxDistance,
            ushort layerMask,
            List<LWRaycastHit> results)
        {
            results.Clear();
            FP64 dirLen = direction.Magnitude;
            if (dirLen.Raw == 0) return 0;
            FPVector2 dirNorm = direction / dirLen;

            LWBody[] bodies = world.BodiesArray;
            ISpatialIndex idx = world.PrepareSpatialIndex();

            if (idx != null)
            {
                List<int> cand = AcquireCandidates();
                idx.QuerySegmentAABB(in origin, in dirNorm, maxDistance,
                                     FPVector2.Zero, cand);
                for (int k = 0; k < cand.Count; k++)
                {
                    if (TestBody(in origin, in dirNorm, maxDistance, layerMask,
                                 in bodies[cand[k]], out FP64 t, out FPVector2 p, out FPVector2 n))
                    {
                        results.Add(new LWRaycastHit
                        {
                            BodyId = bodies[cand[k]].Id,
                            Distance = t,
                            Point = p,
                            Normal = n,
                        });
                    }
                }
            }
            else
            {
                int cap = world.BodyCapacity;
                for (int i = 0; i < cap; i++)
                {
                    if (TestBody(in origin, in dirNorm, maxDistance, layerMask,
                                 in bodies[i], out FP64 t, out FPVector2 p, out FPVector2 n))
                    {
                        results.Add(new LWRaycastHit
                        {
                            BodyId = bodies[i].Id,
                            Distance = t,
                            Point = p,
                            Normal = n,
                        });
                    }
                }
            }

            // Stable insertion sort by Distance. Determinism matters more than
            // micro-perf for the typical hit count.
            for (int i = 1; i < results.Count; i++)
            {
                LWRaycastHit cur = results[i];
                int j = i - 1;
                while (j >= 0 && results[j].Distance > cur.Distance)
                {
                    results[j + 1] = results[j];
                    j--;
                }
                results[j + 1] = cur;
            }
            return results.Count;
        }

        /// <summary>组合 Alive / Layer 过滤后，把射线检测分派到 <see cref="RayBody"/>。</summary>
        private static bool TestBody(
            in FPVector2 origin, in FPVector2 dirNorm, FP64 maxDist, ushort layerMask,
            in LWBody body, out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = default; point = default; normal = default;
            if ((body.Flags & LWBodyFlags.Alive) == 0) return false;
            if ((body.Layer & layerMask) == 0) return false;
            return RayBody(in origin, in dirNorm, maxDist, in body, out t, out point, out normal);
        }

        // ----- Primitive ray vs shape ---------------------------------------

        /// <summary>把射线 vs body 分派到 <see cref="RayCircle"/> 或 <see cref="RayAABB"/>。</summary>
        public static bool RayBody(
            in FPVector2 origin, in FPVector2 dirNorm, FP64 maxDist,
            in LWBody body, out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            if (body.Shape.Type == LWShapeType.Circle)
                return RayCircle(in origin, in dirNorm, maxDist, body.Position, body.Shape.Radius,
                                 out t, out point, out normal);
            return RayAABB(in origin, in dirNorm, maxDist, body.Position, body.Shape.HalfExtents,
                           out t, out point, out normal);
        }

        /// <summary>
        /// 射线 vs 圆：经典判别式解法。起点在圆内部时按 Unity 约定不算命中。
        /// </summary>
        public static bool RayCircle(
            in FPVector2 origin, in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 center, FP64 radius,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = FP64.Zero;
            point = default;
            normal = default;

            FPVector2 m = origin - center;
            FP64 b = FPVector2.Dot(m, dirNorm);
            FP64 mm = m.SqrMagnitude;
            FP64 rr = radius * radius;
            FP64 c = mm - rr;

            // Origin inside the circle: skip (Unity-like convention).
            if (c.Raw < 0) return false;
            // Outside and moving away.
            if (c.Raw > 0 && b.Raw > 0) return false;

            FP64 disc = b * b - c;
            if (disc.Raw < 0) return false;

            FP64 sq = FPMath.Sqrt(disc);
            FP64 hit = -b - sq;
            if (hit.Raw < 0) return false;
            if (hit > maxDist) return false;

            t = hit;
            point = origin + dirNorm * hit;
            normal = (point - center) / radius;
            return true;
        }

        /// <summary>
        /// 射线 vs AABB：slab 法。命中时返回沿入射方向的法线轴 (±X 或 ±Y)。
        /// </summary>
        public static bool RayAABB(
            in FPVector2 origin, in FPVector2 dirNorm, FP64 maxDist,
            in FPVector2 boxCenter, in FPVector2 halfExtents,
            out FP64 t, out FPVector2 point, out FPVector2 normal)
        {
            t = FP64.Zero;
            point = default;
            normal = default;

            FPVector2 boxMin = boxCenter - halfExtents;
            FPVector2 boxMax = boxCenter + halfExtents;

            FP64 tNear = FP64.MinValue;
            FP64 tFar = FP64.MaxValue;
            int hitAxis = -1;
            FP64 hitSign = FP64.Zero;

            // X slab.
            if (dirNorm.X.Raw == 0)
            {
                if (origin.X < boxMin.X || origin.X > boxMax.X) return false;
            }
            else
            {
                FP64 inv = FP64.One / dirNorm.X;
                FP64 t1 = (boxMin.X - origin.X) * inv;
                FP64 t2 = (boxMax.X - origin.X) * inv;
                FP64 tNearX, tFarX;
                FP64 signX;
                if (t1 < t2) { tNearX = t1; tFarX = t2; signX = FP64.NegOne; }
                else { tNearX = t2; tFarX = t1; signX = FP64.One; }
                if (tNearX > tNear) { tNear = tNearX; hitAxis = 0; hitSign = signX; }
                if (tFarX < tFar) tFar = tFarX;
                if (tNear > tFar) return false;
            }

            // Y slab.
            if (dirNorm.Y.Raw == 0)
            {
                if (origin.Y < boxMin.Y || origin.Y > boxMax.Y) return false;
            }
            else
            {
                FP64 inv = FP64.One / dirNorm.Y;
                FP64 t1 = (boxMin.Y - origin.Y) * inv;
                FP64 t2 = (boxMax.Y - origin.Y) * inv;
                FP64 tNearY, tFarY;
                FP64 signY;
                if (t1 < t2) { tNearY = t1; tFarY = t2; signY = FP64.NegOne; }
                else { tNearY = t2; tFarY = t1; signY = FP64.One; }
                if (tNearY > tNear) { tNear = tNearY; hitAxis = 1; hitSign = signY; }
                if (tFarY < tFar) tFar = tFarY;
                if (tNear > tFar) return false;
            }

            if (tNear.Raw < 0) return false;     // started inside box
            if (tNear > maxDist) return false;

            t = tNear;
            point = origin + dirNorm * tNear;
            normal = hitAxis == 0
                ? new FPVector2(hitSign, FP64.Zero)
                : new FPVector2(FP64.Zero, hitSign);
            return true;
        }
    }
}
