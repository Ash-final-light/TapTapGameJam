// ContactSolver.cs
//
// Resolves a LWContact between two bodies via:
//   1. Positional correction along the contact normal (Baumgarte style).
//   2. Normal impulse driven by the combined restitution of the pair.
//   3. Optional minimum-separation-speed floor for "snappy" responses.
//
// Note on the normal convention: c.Normal points from body A toward body B,
// which is opposite of the surface-outward normal returned by Sweep / Raycast
// query hits. Callers consuming both LWContact and LWSweepHit must remember
// the asymmetry (StepContacts vs prediction queries).

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 接触求解器，按照"位置修正 + 法向冲量 + 最小分离速度"的顺序处理一个 <see cref="LWContact"/>。
    /// 法线约定为 A→B（与 Sweep / Raycast 的表面外向相反，详见 <see cref="LWContact.Normal"/>）。
    /// </summary>
    public static class ContactSolver
    {
        /// <summary>
        /// 解算单次接触：
        /// 1) Baumgarte 位置纠正；
        /// 2) 沿法线施加弹性冲量（按 <see cref="LWPhysicsConfig.RestitutionCombine"/> 合成弹性系数）；
        /// 3) 可选的最小分离速度兜底；
        /// 4) 唤醒两体（Sleeping 位被清除）。
        /// </summary>
        public static void Resolve(ref LWBody a, ref LWBody b, in LWContact c, in LWPhysicsConfig cfg)
        {
            FP64 invMassSum = a.InvMass + b.InvMass;
            if (invMassSum.Raw == 0) return; // both static; nothing to do

            // 1. Positional correction along normal.
            FP64 slop = cfg.PositionSlop;
            FP64 depth = c.Penetration - slop;
            if (depth.Raw > 0)
            {
                FP64 magnitude = depth * cfg.PositionCorrectionPercent / invMassSum;
                FPVector2 correction = c.Normal * magnitude;
                a.Position -= correction * a.InvMass;
                b.Position += correction * b.InvMass;
            }

            // 2. Normal impulse.
            FPVector2 relVel = b.Velocity - a.Velocity;
            FP64 velAlongNormal = FPVector2.Dot(relVel, c.Normal);
            // Positive means already separating along normal.
            if (velAlongNormal.Raw > 0) return;

            FP64 e;
            FP64[] m = cfg.RestitutionMatrix;
            if (m != null)
            {
                int ia = LWLayerMask.LayerIndex(a.Layer);
                int ib = LWLayerMask.LayerIndex(b.Layer);
                e = m[ia * LWLayerMask.LayerCount + ib];
            }
            else
            {
                e = CombineRestitution(a.Restitution, b.Restitution, cfg.RestitutionCombine);
            }
            FP64 j = -(FP64.One + e) * velAlongNormal / invMassSum;

            // 3. Minimum-separation-speed clamp.
            // 仅对"真正深度重叠"施加，避免慢速掠过接触被异常弹飞。
            if (cfg.MinSeparationSpeed.Raw > 0 && c.Penetration > cfg.MinSeparationPenetration)
            {
                FP64 postSpeed = velAlongNormal + j * invMassSum;
                if (postSpeed < cfg.MinSeparationSpeed)
                {
                    j += (cfg.MinSeparationSpeed - postSpeed) / invMassSum;
                }
            }

            FPVector2 impulse = c.Normal * j;
            a.Velocity -= impulse * a.InvMass;
            b.Velocity += impulse * b.InvMass;

            // Wake：仅当本体施加冲量后速度仍达到休眠阈值才重置休眠计数，
            // 否则静止/掠过接触会每帧误清休眠导致永不入睡。
            if (a.Velocity.SqrMagnitude >= cfg.SleepLinearSpeedSqr)
            {
                a.Flags &= ~LWBodyFlags.Sleeping;
                a.SleepFrames = 0;
            }
            if (b.Velocity.SqrMagnitude >= cfg.SleepLinearSpeedSqr)
            {
                b.Flags &= ~LWBodyFlags.Sleeping;
                b.SleepFrames = 0;
            }
        }

        /// <summary>按 <paramref name="mode"/> 合成两体的弹性系数。</summary>
        private static FP64 CombineRestitution(FP64 ra, FP64 rb, LWRestitutionCombine mode)
        {
            switch (mode)
            {
                case LWRestitutionCombine.Max:
                    return ra.Raw > rb.Raw ? ra : rb;
                case LWRestitutionCombine.Multiply:
                    return ra * rb;
                case LWRestitutionCombine.Average:
                    return (ra + rb) * FP64.Half;
                case LWRestitutionCombine.Min:
                default:
                    return ra.Raw < rb.Raw ? ra : rb;
            }
        }
    }
}
