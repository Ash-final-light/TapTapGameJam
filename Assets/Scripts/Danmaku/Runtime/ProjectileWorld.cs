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
        public readonly FPVector2 PlayerPosition;
        public readonly FP64 PlayerHitRadius;
        public readonly int[] TargetBuffIds;
        public ProjectileHit(ProjectileState p, int tick, in DanmakuPlayerState player)
        {
            ProjectileId = p.Id; Tick = tick; Position = p.Position;
            DamageConfigId = p.Settings.DamageConfigId;
            TargetBuffIds = p.Settings.TargetBuffIds;
            PlayerPosition = player.Position; PlayerHitRadius = player.HitRadius;
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
        public ProjectileFaction Faction { get; internal set; }
        public FPVector2 PreviousPosition { get; internal set; }
        public int OwnerId { get; internal set; }
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

    public sealed class ProjectileWorld
    {
        internal const ushort EnemyBulletLayer = 1, PlayerLayer = 2, FriendlyBulletLayer = 4, BossLayer = 8;
        private readonly List<ProjectileState> bullets = new(256);
        private readonly Dictionary<int, ProjectileState> byId = new(256);
        private readonly Stack<ProjectileState> recycled = new(256);
        private readonly List<PendingSpawn> pending = new();
        private readonly Dictionary<int, ProjectileDeathReason> deaths = new();
        private HashSet<int> touching = new(), nextTouching = new();
        private readonly LWPhysicsWorld world;
        private readonly int maxBullets, maxDepth, playerId;
        private int bossId;
        private readonly FPVector2 halfField;
        public int Tick { get; private set; }
        public int TotalSpawned { get; private set; }
        public int PeakBullets { get; private set; }
        public string Fault { get; private set; }
        public DanmakuPlayerState PlayerState { get; private set; }
        public DanmakuPlayerState BossState { get; private set; }
        public IReadOnlyList<ProjectileState> Bullets => bullets;
        public readonly List<CombatContact> Contacts = new();
        private struct PendingSpawn
        {
            public int SourceClip, Depth, OwnerId;
            public ProjectileFaction Faction;
            public FPVector2 Position;
            public FP64 Direction, Speed;
            public CompiledProjectile Definition;
        }
        public ProjectileWorld(DanmakuSequence sequence, int capacity = 0)
        {
            maxBullets = Math.Max(1, capacity > 0 ? capacity : sequence.MaxActiveBullets);
            maxDepth = Math.Max(0, Math.Min(4, sequence.MaxSplitDepth));
            halfField = DanmakuSimulation.ToFP(sequence.FieldSize) * FP64.Half;
            var config = LWPhysicsConfig.Default;
            config.InitialBodyCapacity = Math.Max(16, maxBullets + 2);
            config.SleepLinearSpeedSqr = FP64.Zero;
            world = new LWPhysicsWorld(config, new BulletTargetBroadphase(EnemyBulletLayer | FriendlyBulletLayer, PlayerLayer | BossLayer));
            PlayerState = new DanmakuPlayerState(DanmakuSimulation.ToFP(sequence.PreviewPlayerPosition), DanmakuSimulation.ToFP(sequence.PreviewPlayerPosition), FP64.FromFloat(Math.Max(0.001f, sequence.PreviewHitRadius)), sequence.CollisionEnabled);
            var def = LWBodyDef.Static(LWShape.Circle(PlayerState.HitRadius), PlayerState.Position);
            def.Layer = PlayerLayer; def.Mask = sequence.CollisionEnabled ? EnemyBulletLayer : (ushort)0;
            def.Flags |= LWBodyFlags.Trigger; playerId = world.AddBody(def);
        }
        public void SetPlayerState(in DanmakuPlayerState state)
        { PlayerState = state; SetTarget(playerId, state, EnemyBulletLayer); }
        public void SetBossState(in DanmakuPlayerState state)
        {
            BossState = state;
            if (bossId == 0)
            {
                var def = LWBodyDef.Static(LWShape.Circle(state.HitRadius), state.Position);
                def.Layer = BossLayer; def.Mask = FriendlyBulletLayer; def.Flags |= LWBodyFlags.Trigger;
                bossId = world.AddBody(def);
            }
            SetTarget(bossId, state, FriendlyBulletLayer);
        }
        private void SetTarget(int id, in DanmakuPlayerState state, ushort mask)
        {
            var body = world.GetBody(id); body.Position = state.Position;
            body.Shape = LWShape.Circle(state.HitRadius); body.Mask = state.CollisionEnabled ? mask : (ushort)0;
            world.SetBody(id, body);
        }
        public void SpawnFriendly(ProjectileSettings settings, FPVector2 position, FP64 angle, FP64 speed)
        { Spawn(-1, 0, position, angle, speed, CompileProjectile(settings, 0), ProjectileFaction.Player, 1); }
        internal void SpawnFriendly(CompiledProjectile definition, FPVector2 position, FP64 angle, FP64 speed)
        { Spawn(-1, 0, position, angle, speed, definition, ProjectileFaction.Player, 1); }
        public void BeginTick()
        {
            deaths.Clear(); Contacts.Clear();
            foreach (var p in pending) Spawn(p.SourceClip, p.Depth, p.Position, p.Direction, p.Speed, p.Definition, p.Faction, p.OwnerId);
            pending.Clear();
        }
        public void Advance()
        {
            if (Fault != null) return;
            foreach (var p in bullets) p.PreviousPosition = p.Position;
            world.Step(); Tick++; nextTouching.Clear();
            foreach (var p in bullets)
            {
                p.Position = world.GetBody(p.Id).Position;
                var target = p.Faction == ProjectileFaction.Enemy ? PlayerState : BossState;
                if (!target.CollisionEnabled || (p.Faction == ProjectileFaction.Player && bossId == 0)) continue;
                FP64 radius = p.Radius + target.HitRadius;
                // A bullet created after the motor moved cannot hit the earlier actor path.
                var targetStart = p.BirthTick == Tick - 1 ? target.Position : target.PreviousPosition;
                FPVector2 start = p.PreviousPosition - targetStart;
                FPVector2 delta = p.Position - target.Position - start;
                bool endInside = (p.Position - target.Position).SqrMagnitude <= radius * radius;
                if (endInside) nextTouching.Add(p.Id);
                if (!touching.Contains(p.Id) && SweptCircle(start, delta, radius, out FP64 time))
                    Contacts.Add(new CombatContact(p.Id, p.Faction == ProjectileFaction.Enemy ? 1 : 2, time));
            }
            (touching, nextTouching) = (nextTouching, touching);
            Contacts.Sort(CombatContact.Compare);
        }
        internal static bool SweptCircle(FPVector2 start, FPVector2 delta, FP64 radius, out FP64 time)
        {
            time = FP64.Zero;
            FP64 c = start.SqrMagnitude - radius * radius;
            if (c <= FP64.Zero) return true;
            FP64 a = delta.SqrMagnitude;
            if (a <= FP64.Zero) return false;
            FP64 b = start.X * delta.X + start.Y * delta.Y;
            if (b >= FP64.Zero) return false;
            FP64 disc = b * b - a * c;
            if (disc < FP64.Zero) return false;
            time = (-b - FPMath.Sqrt(disc)) / a;
            return time >= FP64.Zero && time <= FP64.One;
        }
        internal bool TryGet(int id, out ProjectileState p) => byId.TryGetValue(id, out p) && !deaths.ContainsKey(id);
        internal void ConsumeHit(ProjectileState p)
        { if (--p.RemainingHits <= 0) deaths[p.Id] = ProjectileDeathReason.HitCount; }
        public void CommitLifecycle()
        {
            foreach (var p in bullets)
            {
                if (deaths.ContainsKey(p.Id)) continue;
                int age = Tick - p.BirthTick;
                if (FPMath.Abs(p.Position.X) > halfField.X + p.Radius || FPMath.Abs(p.Position.Y) > halfField.Y + p.Radius) deaths[p.Id] = ProjectileDeathReason.OutOfBounds;
                else if (p.Settings.SplitAgeTicks > 0 && age >= p.Settings.SplitAgeTicks) deaths[p.Id] = ProjectileDeathReason.Split;
                else if (age >= Math.Max(1, p.Settings.DurationTicks)) deaths[p.Id] = ProjectileDeathReason.Duration;
            }
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var p = bullets[i];
                if (!deaths.TryGetValue(p.Id, out var reason)) continue;
                if (reason == ProjectileDeathReason.Split && p.Depth < maxDepth) QueueChildren(p);
                RemoveAt(i, p);
            }
        }
        public void ClearEnemy(int clip = -1) => Clear(clip, true);
        public void Clear(int clip = -1, bool enemyOnly = false)
        {
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var p = bullets[i];
                if ((clip < 0 || p.SourceClip == clip) && (!enemyOnly || p.Faction == ProjectileFaction.Enemy)) RemoveAt(i, p);
            }
            for (int i = pending.Count - 1; i >= 0; i--)
                if ((clip < 0 || pending[i].SourceClip == clip) && (!enemyOnly || pending[i].Faction == ProjectileFaction.Enemy)) pending.RemoveAt(i);
        }
        internal void Spawn(int source, int depth, FPVector2 position, FP64 angle, FP64 speed, CompiledProjectile definition, ProjectileFaction faction = ProjectileFaction.Enemy, int ownerId = 2)
        {
            if (Fault != null) return;
            if (bullets.Count >= maxBullets)
            {
                Fault = $"Tick {Tick}：片段 {source + 1} 超过活跃弹上限 {maxBullets}。请调整间隔／寿命／分裂数量。";
                return;
            }
            var def = LWBodyDef.Dynamic(LWShape.Circle(definition.Radius), position, FP64.One);
            def.Layer = faction == ProjectileFaction.Enemy ? EnemyBulletLayer : FriendlyBulletLayer; def.Mask = faction == ProjectileFaction.Enemy ? PlayerLayer : BossLayer; def.Flags = LWBodyFlags.Trigger;
            def.Velocity = DirectionVector(angle) * speed;
            int id = world.AddBody(def);
            var p = recycled.Count > 0 ? recycled.Pop() : new ProjectileState();
            p.Id = id; p.Faction = faction; p.OwnerId = ownerId; p.PreviousPosition = position; p.SourceClip = source; p.BirthTick = Tick; p.Depth = depth;
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
            touching.Remove(p.Id); nextTouching.Remove(p.Id); recycled.Push(p);
        }

        private void QueueChildren(ProjectileState parent)
        {
            int count = Math.Max(1, Math.Min(64, parent.Settings.SplitCount));
            for (int i = 0; i < count; i++)
                pending.Add(new PendingSpawn
                {
                    Faction = parent.Faction, OwnerId = parent.OwnerId, SourceClip = parent.SourceClip, Depth = parent.Depth + 1, Position = parent.Position,
                    Direction = (parent.Settings.InheritDirection ? parent.Direction : FP64.Zero) + FPMath.PI_TIMES_2 * FP64.FromFraction(i, count),
                    Speed = parent.Definition.ChildSpeed, Definition = parent.Definition.Child
                });
        }

        internal static CompiledProjectile CompileProjectile(ProjectileSettings source, int depth)
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

        private static FPVector2 DirectionVector(FP64 angle) => new(FPMath.Cos(angle), FPMath.Sin(angle));
    }
}
