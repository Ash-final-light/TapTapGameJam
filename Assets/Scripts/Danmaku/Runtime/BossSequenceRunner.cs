using System;
using Framework.FixedPoint;
using UnityEngine;
namespace TapTap.Danmaku
{
    internal sealed class CompiledClip
    {
        public DanmakuClip Clip;
        public EmissionSettings Emission;
        public CompiledProjectile Projectile;
        public FPRandom Random;
        public FPVector2 Start, End, SegmentStart, SegmentEnd;
        public FP64 Direction, Arc, SpawnRadius, Speed, SpeedDelta, Rotation, AlternatePhase, RandomAngle;
    }

    public sealed class BossSequenceRunner
    {
        private readonly ProjectileWorld world;
        private CompiledClip[] clips;
        private DanmakuTimelineEvent[] events;
        private DanmakuSequence sequence;
        private int soloClip;
        private int localTick;
        public bool Finished => localTick >= sequence.DurationTicks;
        public BossSequenceRunner(DanmakuSequence source, ProjectileWorld world, int solo = -1)
        {
            this.world = world; sequence = source; soloClip = solo;
            clips = new CompiledClip[source.Clips.Count];
            for (int i = 0; i < clips.Length; i++)
            {
                DanmakuClip clip = source.Clips[i]?.Copy() ?? new DanmakuClip { Muted = true };
                var s = (source.Clips[i]?.EffectiveEmission ?? new EmissionSettings()).Copy();
                // Preserve original stable identity for independently seeded random streams.
                clip.Id = source.Clips[i]?.Id ?? i.ToString();
                clips[i] = new CompiledClip
                {
                    Clip = clip, Emission = s,
                    Projectile = ProjectileWorld.CompileProjectile(source.Clips[i]?.EffectiveProjectileSettings ?? new ProjectileSettings(), 0),
                    Random = new FPRandom(StreamSeed(source.RandomSeed, clip.Id)),
                    Start = DanmakuSimulation.ToFP(clip.EmitterPosition), End = DanmakuSimulation.ToFP(clip.EmitterEndPosition),
                    SegmentStart = DanmakuSimulation.ToFP(s.SegmentStart), SegmentEnd = DanmakuSimulation.ToFP(s.SegmentEnd),
                    Direction = Degrees(s.DirectionDegrees), Arc = Degrees(s.ArcDegrees),
                    SpawnRadius = FP64.FromFloat(Math.Max(0, s.SpawnRadius)),
                    Speed = FP64.FromFloat(Math.Max(0, s.Speed)), SpeedDelta = FP64.FromFloat(s.SpeedLayerDelta),
                    Rotation = Degrees(s.RotationDegreesPerSecond), AlternatePhase = Degrees(s.AlternatePhaseDegrees),
                    RandomAngle = Degrees(s.RandomAngleDegrees)
                };
            }
            events = source.Events.ConvertAll(e => new DanmakuTimelineEvent
            { Tick = e.Tick, Type = e.Type, DurationTicks = e.DurationTicks, ClipIndex = e.ClipIndex, Label = e.Label }).ToArray();
        }
        public void Step()
        {
            if (Finished) return;
            foreach (var e in events)
                if (e.Tick == localTick && e.Type == DanmakuEventType.ClearBullets) world.ClearEnemy(e.ClipIndex);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c.Clip.Muted || (soloClip >= 0 && soloClip != i)) continue;
                int local = localTick - c.Clip.StartTick;
                if (local >= 0 && local < Math.Max(1, c.Clip.DurationTicks))
                {
                    int interval = Math.Max(1, c.Emission.IntervalTicks);
                    if (local % interval == 0) Emit(c, i, local, local / interval);
                }
            }
            localTick++;
        }
        public void AfterStep()
        { if (Finished && sequence.ClearAtEnd) world.ClearEnemy(); }
        public Vector2 EmitterPosition(int index)
        {
            if (index < 0 || index >= clips.Length) return Vector2.zero;
            var c = clips[index];
            return DanmakuSimulation.ToVector(Emitter(c, Math.Max(0, Math.Min(c.Clip.DurationTicks, localTick - c.Clip.StartTick))));
        }

        private FPVector2 Emitter(CompiledClip c, int local)
        {
            if (!c.Clip.MoveEmitter) return c.Start;
            return c.Start + (c.End - c.Start) * FP64.FromFraction(local, Math.Max(1, c.Clip.DurationTicks));
        }

        private void Emit(CompiledClip c, int source, int local, int round)
        {
            var s = c.Emission;
            int count = Math.Max(1, Math.Min(64, s.Count)), layers = Math.Max(1, Math.Min(8, s.SpeedLayers));
            FPVector2 origin = Emitter(c, local);
            FP64 center = c.Direction + c.Rotation * FP64.FromFraction(local, DanmakuSequence.TickRate);
            if (s.AimAtPlayer)
            {
                FPVector2 delta = world.PlayerState.Position - origin;
                center += FPMath.Atan2(delta.Y, delta.X);
            }
            if (round % 2 != 0) center += c.AlternatePhase;
            for (int i = 0; i < count; i++)
            {
                FP64 angle = center;
                if (s.Shape == EmissionShape.Ring) angle += FPMath.PI_TIMES_2 * FP64.FromFraction(i, count);
                else if (s.Shape == EmissionShape.Arc && count > 1)
                    angle += c.Arc * (FP64.FromFraction(i, count - 1) - FP64.Half);
                if (c.RandomAngle.Raw > 0) angle += c.Random.Range(-c.RandomAngle, c.RandomAngle);
                FPVector2 point = origin;
                if (s.Shape == EmissionShape.Segment)
                    point += c.SegmentStart + (c.SegmentEnd - c.SegmentStart) *
                        (count == 1 ? FP64.Half : FP64.FromFraction(i, count - 1));
                point += DirectionVector(angle) * c.SpawnRadius;
                for (int layer = 0; layer < layers; layer++)
                    world.Spawn(source, 0, point, angle, FPMath.Max(FP64.Zero, c.Speed + c.SpeedDelta * FP64.FromInt(layer)), c.Projectile);
            }
        }

        private static ulong StreamSeed(int seed, string id)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL ^ (uint)seed;
                foreach (char c in id ?? string.Empty) { hash ^= c; hash *= 1099511628211UL; }
                return hash;
            }
        }

        private static FP64 Degrees(float value) => FP64.FromFloat(value) * FPMath.Deg2Rad;
        private static FPVector2 DirectionVector(FP64 angle) => new(FPMath.Cos(angle), FPMath.Sin(angle));
    }
}
