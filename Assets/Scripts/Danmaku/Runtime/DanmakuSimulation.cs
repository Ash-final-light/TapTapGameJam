using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using Framework.Physics.LightweightPhysics;
using UnityEngine;

namespace TapTap.Danmaku
{
    public enum ProjectileDeathReason { HitCount, Duration, Split, OutOfBounds, Clear }

    public readonly struct ProjectileHit
    {
        public readonly int ProjectileId, Tick, DamageConfigId;
        public readonly FPVector2 Position;
        public readonly int[] TargetBuffIds;
        public ProjectileHit(ProjectileState p, int tick)
        {
            ProjectileId = p.Id; Tick = tick; Position = p.Position;
            DamageConfigId = p.Settings.DamageConfigId;
            TargetBuffIds = p.Settings.TargetBuffIds;
        }
    }

    public sealed class ProjectileState
    {
        public int Id { get; internal set; }
        public int SourceClip { get; internal set; }
        public int BirthTick { get; internal set; }
        public int Depth { get; internal set; }
        public int RemainingHits { get; internal set; }
        public FPVector2 Position { get; internal set; }
        public FP64 Direction { get; internal set; }
        public ProjectileSettings Settings { get; internal set; }
        internal CompiledProjectile Definition;
        internal FP64 Radius;
    }

    internal sealed class CompiledProjectile
    {
        public ProjectileSettings Settings;
        public CompiledProjectile Child;
        public FP64 Radius;
        public FP64 ChildSpeed;
    }

    internal sealed class CompiledClip
    {
        public DanmakuClip Clip;
        public EmissionSettings Emission;
        public CompiledProjectile Projectile;
        public FPRandom Random;
        public FPVector2 Start, End, SegmentStart, SegmentEnd;
        public FP64 Direction, Arc, SpawnRadius, Speed, SpeedDelta, Rotation, AlternatePhase, RandomAngle;
    }

    /// <summary>
    /// Shared game/editor fixed-tick simulation. Tick is the number of completed steps:
    /// Reset/Seek(0) is before tick-zero emissions; Step executes [Tick, Tick+1).
    /// Seeking replays simulation without publishing gameplay callbacks.
    /// </summary>
    public sealed class DanmakuSimulation
    {
        private const ushort BulletLayer = 1, PlayerLayer = 2, DisabledLayer = 4;
        private readonly List<ProjectileState> bullets = new(256);
        private readonly Dictionary<int, ProjectileState> byId = new(256);
        private readonly Stack<ProjectileState> recycled = new(256);
        private readonly List<PendingSpawn> pending = new();
        private readonly Dictionary<int, ProjectileDeathReason> deaths = new();
        private readonly List<ProjectileHit> hits = new();
        private CompiledClip[] clips = Array.Empty<CompiledClip>();
        private DanmakuTimelineEvent[] events = Array.Empty<DanmakuTimelineEvent>();
        private LWPhysicsWorld world;
        private int duration, maxBullets, maxDepth, playerId;
        private bool clearAtEnd;
        private FPVector2 halfField, playerPosition;
        private DanmakuSequence sequence;
        private int soloClip = -1;
        private bool publishEvents = true;
        private static readonly FP64 Dt = FP64.FromFraction(1, DanmakuSequence.TickRate);

        private struct PendingSpawn
        {
            public int SourceClip, Depth;
            public FPVector2 Position;
            public FP64 Direction, Speed;
            public CompiledProjectile Definition;
        }

        public int Tick { get; private set; }
        public int TotalSpawned { get; private set; }
        public int HitCount { get; private set; }
        public int PeakBullets { get; private set; }
        public string Fault { get; private set; }
        public bool Finished => Tick >= duration;
        public IReadOnlyList<ProjectileState> Bullets => bullets;
        public IReadOnlyList<ProjectileHit> StepHits => hits;
        public event Action<ProjectileHit> OnHit;

        public DanmakuSimulation(DanmakuSequence sequence, int soloClip = -1) => Reset(sequence, soloClip);

        public void Reset(DanmakuSequence source, int solo = -1)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            sequence = source; soloClip = solo;
            Tick = TotalSpawned = HitCount = PeakBullets = 0;
            Fault = null;
            foreach (var bullet in bullets) recycled.Push(bullet);
            bullets.Clear(); byId.Clear(); pending.Clear(); deaths.Clear(); hits.Clear();
            duration = Math.Max(1, source.DurationTicks);
            maxBullets = Math.Max(1, source.MaxActiveBullets);
            maxDepth = Math.Max(0, Math.Min(4, source.MaxSplitDepth));
            clearAtEnd = source.ClearAtEnd;
            halfField = ToFP(source.FieldSize) * FP64.Half;
            playerPosition = ToFP(source.PlayerPosition);
            var config = LWPhysicsConfig.Default;
            config.InitialBodyCapacity = Math.Max(16, maxBullets + 1);
            config.SleepLinearSpeedSqr = FP64.Zero; // Stop/restart bullets must remain externally controllable.
            world = new LWPhysicsWorld(config, new BulletTargetBroadphase());
            var player = LWBodyDef.Static(LWShape.Circle(FP64.FromFloat(Math.Max(0.001f, source.PlayerHitRadius))), playerPosition);
            player.Layer = PlayerLayer; player.Mask = source.CollisionEnabled ? BulletLayer : DisabledLayer;
            player.Flags |= LWBodyFlags.Trigger;
            playerId = world.AddBody(player);
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
                    Projectile = CompileProjectile(source.Clips[i]?.EffectiveProjectileSettings ?? new ProjectileSettings(), 0),
                    Random = new FPRandom(StreamSeed(source.RandomSeed, clip.Id)),
                    Start = ToFP(clip.EmitterPosition), End = ToFP(clip.EmitterEndPosition),
                    SegmentStart = ToFP(s.SegmentStart), SegmentEnd = ToFP(s.SegmentEnd),
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

        public void Seek(int targetTick)
        {
            targetTick = Math.Max(0, Math.Min(duration, targetTick));
            Reset(sequence, soloClip);
            publishEvents = false;
            try { while (Tick < targetTick && Fault == null) Step(); }
            finally { publishEvents = true; }
        }

        public void Step()
        {
            if (Finished || Fault != null) return;
            hits.Clear(); deaths.Clear();
            foreach (var e in events)
                if (e.Tick == Tick && e.Type == DanmakuEventType.ClearBullets) Clear(e.ClipIndex);
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                Spawn(p.SourceClip, p.Depth, p.Position, p.Direction, p.Speed, p.Definition);
            }
            pending.Clear();
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c.Clip.Muted || (soloClip >= 0 && soloClip != i)) continue;
                int local = Tick - c.Clip.StartTick;
                if (local < 0 || local >= Math.Max(1, c.Clip.DurationTicks)) continue;
                int interval = Math.Max(1, c.Emission.IntervalTicks);
                if (local % interval == 0) Emit(c, i, local, local / interval);
            }
            if (Fault != null) return;
            world.Step(); // Sole owner of position integration.
            Tick++;
            for (int i = 0; i < bullets.Count; i++) bullets[i].Position = world.GetBody(bullets[i].Id).Position;
            for (int eventIndex = 0; eventIndex < world.TriggerEvents.Count; eventIndex++)
            {
                var e = world.TriggerEvents[eventIndex];
                if (e.Phase != LWTriggerPhase.Enter) continue;
                int other = e.BodyA == playerId ? e.BodyB : e.BodyB == playerId ? e.BodyA : 0;
                if (!byId.TryGetValue(other, out var p)) continue;
                var hit = new ProjectileHit(p, Tick);
                hits.Add(hit); HitCount++;
                if (--p.RemainingHits <= 0) deaths[p.Id] = ProjectileDeathReason.HitCount;
            }
            foreach (var p in bullets)
            {
                if (deaths.ContainsKey(p.Id)) continue;
                int age = Tick - p.BirthTick;
                if (FPMath.Abs(p.Position.X) > halfField.X + p.Radius || FPMath.Abs(p.Position.Y) > halfField.Y + p.Radius)
                    deaths[p.Id] = ProjectileDeathReason.OutOfBounds;
                else if (p.Settings.SplitAgeTicks > 0 && age >= p.Settings.SplitAgeTicks)
                    deaths[p.Id] = ProjectileDeathReason.Split;
                else if (age >= Math.Max(1, p.Settings.DurationTicks))
                    deaths[p.Id] = ProjectileDeathReason.Duration;
            }
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var p = bullets[i];
                if (!deaths.TryGetValue(p.Id, out var reason)) continue;
                if (reason == ProjectileDeathReason.Split && p.Depth < maxDepth) QueueChildren(p);
                RemoveAt(i, p);
            }
            if (Finished && clearAtEnd) Clear();
            // Publish only after state transitions; seek never calls real damage/buff consumers.
            if (publishEvents && OnHit != null)
                foreach (var hit in hits) OnHit(hit);
        }

        public void Clear(int clipIndex = -1)
        {
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var p = bullets[i];
                if (clipIndex >= 0 && p.SourceClip != clipIndex) continue;
                RemoveAt(i, p);
            }
            pending.RemoveAll(p => clipIndex < 0 || p.SourceClip == clipIndex);
        }

        public Vector2 EmitterPosition(int index)
        {
            if (index < 0 || index >= clips.Length) return Vector2.zero;
            var c = clips[index];
            return ToVector(Emitter(c, Math.Max(0, Math.Min(c.Clip.DurationTicks, Tick - c.Clip.StartTick))));
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
                FPVector2 delta = playerPosition - origin;
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
                    Spawn(source, 0, point, angle, FPMath.Max(FP64.Zero, c.Speed + c.SpeedDelta * FP64.FromInt(layer)), c.Projectile);
            }
        }

        private void Spawn(int source, int depth, FPVector2 position, FP64 angle, FP64 speed, CompiledProjectile definition)
        {
            if (Fault != null) return;
            if (bullets.Count >= maxBullets)
            {
                Fault = $"Tick {Tick}：片段 {source + 1} 超过活跃弹上限 {maxBullets}。请调整间隔／寿命／分裂数量。";
                return;
            }
            var def = LWBodyDef.Dynamic(LWShape.Circle(definition.Radius), position, FP64.One);
            def.Layer = BulletLayer; def.Mask = PlayerLayer; def.Flags = LWBodyFlags.Trigger;
            def.Velocity = DirectionVector(angle) * speed;
            int id = world.AddBody(def);
            var p = recycled.Count > 0 ? recycled.Pop() : new ProjectileState();
            p.Id = id; p.SourceClip = source; p.BirthTick = Tick; p.Depth = depth;
            p.RemainingHits = Math.Max(1, definition.Settings.HitCount); p.Position = position;
            p.Direction = angle; p.Settings = definition.Settings; p.Definition = definition; p.Radius = definition.Radius;
            bullets.Add(p); byId.Add(id, p); TotalSpawned++;
            PeakBullets = Math.Max(PeakBullets, bullets.Count);
        }

        private void RemoveAt(int index, ProjectileState p)
        {
            world.RemoveBody(p.Id); byId.Remove(p.Id);
            int last = bullets.Count - 1;
            bullets[index] = bullets[last]; bullets.RemoveAt(last);
            recycled.Push(p);
        }

        private void QueueChildren(ProjectileState parent)
        {
            int count = Math.Max(1, Math.Min(64, parent.Settings.SplitCount));
            for (int i = 0; i < count; i++)
                pending.Add(new PendingSpawn
                {
                    SourceClip = parent.SourceClip, Depth = parent.Depth + 1, Position = parent.Position,
                    Direction = (parent.Settings.InheritDirection ? parent.Direction : FP64.Zero) + FPMath.PI_TIMES_2 * FP64.FromFraction(i, count),
                    Speed = parent.Definition.ChildSpeed, Definition = parent.Definition.Child
                });
        }

        private static CompiledProjectile CompileProjectile(ProjectileSettings source, int depth)
        {
            var settings = source.Copy();
            var definition = new CompiledProjectile
            {
                Settings = settings, Radius = FP64.FromFloat(Math.Max(0.001f, settings.HitRadius)),
                ChildSpeed = FP64.FromFloat(Math.Max(0, settings.SplitSpeed))
            };
            if (settings.SplitAgeTicks > 0)
            {
                if (settings.SplitChild && depth < 4) definition.Child = CompileProjectile(settings.SplitChild.Settings, depth + 1);
                else
                {
                    var child = settings.Copy(); child.SplitAgeTicks = 0; child.SplitChild = null;
                    child.VisualSize *= 0.65f;
                    definition.Child = CompileProjectile(child, depth + 1);
                }
            }
            return definition;
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

        public static FPVector2 ToFP(Vector2 v) => FPVector2.FromFloat(v.x, v.y);
        public static Vector2 ToVector(FPVector2 v) => new(v.X.ToFloat(), v.Y.ToFloat());
        private static FP64 Degrees(float value) => FP64.FromFloat(value) * FPMath.Deg2Rad;
        private static FPVector2 DirectionVector(FP64 angle) => new(FPMath.Cos(angle), FPMath.Sin(angle));
    }
}
