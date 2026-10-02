// Trajectory.cs
//
// Predicts the bounce path of a circle launched from `origin` with
// `initialVelocity`. Each segment is produced by sweeping the circle against
// the world via CircleCast and reflecting the velocity off the surface
// normal. Useful for client-side aim-line and pre-launch UI.

using System.Collections.Generic;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 多段反弹轨迹中的一段。<see cref="From"/> → <see cref="To"/> 为本段路径，
    /// 命中时 <see cref="DidHit"/> = true，并给出命中体 / 法线 / 反弹后的速度。
    /// </summary>
    public struct LWTrajectorySegment
    {
        /// <summary>段起点（世界坐标）。</summary>
        public FPVector2 From;

        /// <summary>段终点（世界坐标，命中点或边界点）。</summary>
        public FPVector2 To;

        /// <summary>段开始时的速度。</summary>
        public FPVector2 IncomingVelocity;

        /// <summary>段结束时的速度（未命中时与 IncomingVelocity 相等）。</summary>
        public FPVector2 OutgoingVelocity;

        /// <summary>命中的 body ID；未命中时为 0。</summary>
        public int BodyId;

        /// <summary>命中点的表面外向法线；未命中时为 0 向量。</summary>
        public FPVector2 Normal;

        /// <summary>本段是否以命中结束（false 表示距离/段数耗尽）。</summary>
        public bool DidHit;
    }

    /// <summary>
    /// 预测圆形发射体的多段反弹轨迹（典型用于客户端瞄准线 / 预发射 UI）。
    /// 每段都用 <see cref="Sweep.CircleCast"/> 计算首次命中，并按表面法线做完全弹性反射。
    /// </summary>
    public static class Trajectory
    {
        /// <summary>预测轨迹（mask = AllLayers）。</summary>
        public static int Predict(
            LWPhysicsWorld world,
            in FPVector2 origin,
            in FPVector2 initialVelocity,
            FP64 radius,
            int maxSegments,
            FP64 maxTotalDistance,
            List<LWTrajectorySegment> output)
            => Predict(world, in origin, in initialVelocity, radius, maxSegments, maxTotalDistance,
                       LWLayerMask.AllLayers, output);

        /// <summary>
        /// 预测轨迹。最多产出 <paramref name="maxSegments"/> 段，累计前进距离不超过
        /// <paramref name="maxTotalDistance"/>。结果会先清空再追加到 <paramref name="output"/>。
        /// </summary>
        /// <param name="world">目标物理世界。</param>
        /// <param name="origin">起点。</param>
        /// <param name="initialVelocity">初速度（方向与大小都被使用，大小用于决定反弹后是否继续）。</param>
        /// <param name="radius">扫掠圆半径。</param>
        /// <param name="maxSegments">最大段数。</param>
        /// <param name="maxTotalDistance">总路程上限。</param>
        /// <param name="layerMask">参与检测的 Layer 掩码。</param>
        /// <param name="output">输出段列表。</param>
        /// <returns>实际产出的段数。</returns>
        public static int Predict(
            LWPhysicsWorld world,
            in FPVector2 origin,
            in FPVector2 initialVelocity,
            FP64 radius,
            int maxSegments,
            FP64 maxTotalDistance,
            ushort layerMask,
            List<LWTrajectorySegment> output)
        {
            output.Clear();
            if (maxSegments <= 0 || maxTotalDistance.Raw <= 0) return 0;

            FPVector2 pos = origin;
            FPVector2 vel = initialVelocity;
            FP64 remaining = maxTotalDistance;

            for (int s = 0; s < maxSegments; s++)
            {
                FP64 vMag = vel.Magnitude;
                if (vMag.Raw == 0) break;
                FPVector2 dir = vel / vMag;

                bool ok = Sweep.CircleCast(world, in pos, radius, in dir, remaining, layerMask, out LWSweepHit hit);
                if (ok)
                {
                    FPVector2 newPos = pos + dir * hit.Distance;
                    FPVector2 newVel = FPVector2.Reflect(vel, hit.Normal);
                    output.Add(new LWTrajectorySegment
                    {
                        From = pos,
                        To = newPos,
                        IncomingVelocity = vel,
                        OutgoingVelocity = newVel,
                        BodyId = hit.BodyId,
                        Normal = hit.Normal,
                        DidHit = true,
                    });
                    pos = newPos;
                    vel = newVel;
                    remaining -= hit.Distance;
                    if (remaining.Raw <= 0) break;
                }
                else
                {
                    FPVector2 newPos = pos + dir * remaining;
                    output.Add(new LWTrajectorySegment
                    {
                        From = pos,
                        To = newPos,
                        IncomingVelocity = vel,
                        OutgoingVelocity = vel,
                        BodyId = 0,
                        Normal = FPVector2.Zero,
                        DidHit = false,
                    });
                    break;
                }
            }
            return output.Count;
        }
    }
}
