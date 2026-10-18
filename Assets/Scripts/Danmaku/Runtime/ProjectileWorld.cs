using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using Framework.Physics.LightweightPhysics;
using UnityEngine;
namespace TapTap.Danmaku
{
    public enum ProjectileDeathReason { Hit, Duration, Split, OutOfBounds, Clear, Destroyed }

    public readonly struct ProjectileHit
    {
        public readonly int ProjectileId, Tick;
        public readonly FPVector2 Position;
        public readonly FPVector2 PlayerPosition;
        public readonly FP64 PlayerHitRadius;
        public ProjectileHit(ProjectileState p, int tick, in DanmakuPlayerState player)
        {
            ProjectileId = p.Id; Tick = tick; Position = p.Position;
            PlayerPosition = player.Position; PlayerHitRadius = player.HitRadius;
        }
    }

    public sealed class ProjectileState
    {
        public int Id { get; internal set; }
        public int SourceClip { get; internal set; }
        public int BirthTick { get; internal set; }
        public int FireTick { get; internal set; }
        public int Depth { get; internal set; }
        public FPVector2 Position { get; internal set; }
        public FP64 Direction { get; internal set; }
        public ProjectileSettings Settings { get; internal set; }
        public ProjectileFaction Faction { get; internal set; }
        public FPVector2 PreviousPosition { get; internal set; }
        public int OwnerId { get; internal set; }
        public bool IsEnergyOrb { get; internal set; }
        public FP64 Health { get; internal set; }
        public FP64 HitRadius => Radius;
        public FP64 VisualSize { get; internal set; }
        public FP64 GrowthProgress { get; internal set; }
        public int ExplosionTick { get; internal set; }
        internal CompiledProjectile Definition;
        internal FP64 Radius, PreviousRadius;
    }

    internal sealed class CompiledProjectile
    {
        public ProjectileSettings Settings;
        public CompiledProjectile Child;
        public FP64 Radius;
        public FP64 ChildSpeed;
        public FP64 VisualSize, FinalVisualSize, FinalRadius;
    }

    public sealed class ProjectileWorld
    {
        internal const ushort EnemyBulletLayer = 1, PlayerLayer = 2, FriendlyBulletLayer = 4;
        private readonly List<ProjectileState> bullets = new(256);
        private readonly Dictionary<int, ProjectileState> byId = new(256);
        private readonly Stack<ProjectileState> recycled = new(256);
        private readonly List<SplitSpawn> splits = new();
        private readonly Dictionary<int, ProjectileDeathReason> deaths = new();
        private HashSet<ContactPair> touching = new(), nextTouching = new();
        private readonly EnergyOrbTargetGrid orbGrid = new();
        private readonly HashSet<int> orbCandidates = new();
        public int EnergyOrbCandidateChecks { get; private set; }
        private readonly struct ContactPair : IEquatable<ContactPair>
        {
            public readonly int Source, Target;
            public readonly CombatTargetKind Kind;
            public ContactPair(int source, int target, CombatTargetKind kind) { Source = source; Target = target; Kind = kind; }
            public bool Equals(ContactPair other) => Source == other.Source && Target == other.Target && Kind == other.Kind;
            public override bool Equals(object obj) => obj is ContactPair pair && Equals(pair);
            public override int GetHashCode() => unchecked((Source * 397 ^ Target) * 397 ^ (int)Kind);
        }
        private readonly LWPhysicsWorld world;
        private readonly int maxBullets, playerId;
        private FPVector2 fieldMin, fieldMax;
        public int Tick { get; private set; }
        public int TotalSpawned { get; private set; }
        public int PeakBullets { get; private set; }
        public string Fault { get; private set; }
        public DanmakuPlayerState PlayerState { get; private set; }
        public FPVector2 BossPosition { get; private set; }
        private FP64 bossRadius;
        private bool bossTargetEnabled;
        public IReadOnlyList<ProjectileState> Bullets => bullets;
        public readonly List<CombatContact> Contacts = new();
        public event Action<int> EnergyOrbMissed;
        private struct SplitSpawn
        {
            public int SourceClip, OwnerId, Count;
            public ProjectileFaction Faction;
            public FPVector2 Position;
            public FP64 Direction, Speed;
            public CompiledProjectile Definition;
        }
        public ProjectileWorld(DanmakuSequence sequence, int capacity = 0, Rect? bounds = null)
        {
            maxBullets = Math.Max(1, capacity > 0 ? capacity : sequence.MaxActiveBullets);
            SetBounds(bounds ?? new Rect(-sequence.FieldSize * 0.5f, sequence.FieldSize));
            var config = LWPhysicsConfig.Default;
            config.InitialBodyCapacity = Math.Max(16, maxBullets + 2);
            config.SleepLinearSpeedSqr = FP64.Zero;
            world = new LWPhysicsWorld(config, new BulletTargetBroadphase(EnemyBulletLayer, PlayerLayer));
            PlayerState = new DanmakuPlayerState(DanmakuSimulation.ToFP(sequence.PreviewPlayerPosition), DanmakuSimulation.ToFP(sequence.PreviewPlayerPosition), FP64.FromFloat(Math.Max(0.001f, sequence.PreviewHitRadius)), sequence.CollisionEnabled);
            var def = LWBodyDef.Static(LWShape.Circle(PlayerState.HitRadius), PlayerState.Position);
            def.Layer = PlayerLayer; def.Mask = sequence.CollisionEnabled ? EnemyBulletLayer : (ushort)0;
            def.Flags |= LWBodyFlags.Trigger; playerId = world.AddBody(def);
        }
        public void SetBounds(Rect bounds)
        { fieldMin = DanmakuSimulation.ToFP(bounds.min); fieldMax = DanmakuSimulation.ToFP(bounds.max); }
        private bool OutsideField(ProjectileState p) => p.Position.X < fieldMin.X - p.Radius || p.Position.X > fieldMax.X + p.Radius ||
            p.Position.Y < fieldMin.Y - p.Radius || p.Position.Y > fieldMax.Y + p.Radius;

        public void SetPlayerState(in DanmakuPlayerState state)
        { PlayerState = state; SetTarget(playerId, state, EnemyBulletLayer); }
        public void SetBossTarget(FPVector2 position, FP64 radius)
        { BossPosition = position; bossRadius = radius; bossTargetEnabled = radius > FP64.Zero; }
        private void SetTarget(int id, in DanmakuPlayerState state, ushort mask)
        {
            var body = world.GetBody(id); body.Position = state.Position;
            body.Shape = LWShape.Circle(state.HitRadius); body.Mask = state.CollisionEnabled ? mask : (ushort)0;
            world.SetBody(id, body);
        }
        public void SpawnFriendly(ProjectileSettings settings, FPVector2 position, FP64 angle, FP64 speed)
        { Spawn(-1, 0, position, angle, speed, CompileProjectile(settings), ProjectileFaction.Player, 1); }
        internal void SpawnFriendly(CompiledProjectile definition, FPVector2 position, FP64 angle, FP64 speed)
        { Spawn(-1, 0, position, angle, speed, definition, ProjectileFaction.Player, 1); }
        public void BeginTick()
        {
            deaths.Clear(); Contacts.Clear();
        }
        public void Advance()
        {
            if (Fault != null) return;
            foreach (var p in bullets)
            { p.PreviousPosition = p.Position; p.PreviousRadius = p.Radius; UpdateGrowth(p, Tick + 1); }
            world.Step(); Tick++; nextTouching.Clear();
            foreach (var p in bullets) p.Position = world.GetBody(p.Id).Position;
            orbGrid.Rebuild(bullets); EnergyOrbCandidateChecks = 0;
            foreach (var p in bullets)
            {
                if (p.Faction == ProjectileFaction.Player)
                {
                    if (bossTargetEnabled)
                        AddContact(p, 2, CombatTargetKind.Boss, BossPosition, BossPosition, bossRadius, bossRadius);
                    orbGrid.Query(p, orbCandidates);
                    foreach (int id in orbCandidates)
                    {
                        if (!TryGet(id, out var orb)) continue;
                        EnergyOrbCandidateChecks++;
                        AddContact(p, orb.Id, CombatTargetKind.EnergyOrb, orb.PreviousPosition, orb.Position, orb.PreviousRadius, orb.Radius);
                    }
                }
                if (p.Faction != ProjectileFaction.Enemy || (p.IsEnergyOrb && !p.Settings.EnergyOrb.ContactDamage)) continue;
                var target = PlayerState;
                if (!target.CollisionEnabled) continue;
                // A bullet created after the motor moved cannot hit the earlier actor path.
                var targetStart = p.BirthTick == Tick - 1 ? target.Position : target.PreviousPosition;
                AddContact(p, 1, CombatTargetKind.Player,
                    targetStart, target.Position, target.HitRadius, target.HitRadius);
            }
            (touching, nextTouching) = (nextTouching, touching);
            Contacts.Sort(CombatContact.Compare);
        }
        private void AddContact(ProjectileState p, int targetId, CombatTargetKind kind, FPVector2 targetStart,
            FPVector2 targetEnd, FP64 targetRadiusStart, FP64 targetRadiusEnd)
        {
            var pair = new ContactPair(p.Id, targetId, kind);
            FP64 radiusStart = p.PreviousRadius + targetRadiusStart, radiusEnd = p.Radius + targetRadiusEnd;
            FPVector2 start = p.PreviousPosition - targetStart;
            FPVector2 end = p.Position - targetEnd;
            if (end.SqrMagnitude <= radiusEnd * radiusEnd) nextTouching.Add(pair);
            if (!touching.Contains(pair) && SweptGrowingCircle(start, end - start, radiusStart, radiusEnd, out FP64 time))
                Contacts.Add(new CombatContact(p.Id, targetId, kind, time));
        }
        internal static bool SweptCircle(FPVector2 start, FPVector2 delta, FP64 radius, out FP64 time)
            => SweptGrowingCircle(start, delta, radius, radius, out time);
        // Relative motion and a linearly varying radius within each fixed tick.
        private static bool SweptGrowingCircle(FPVector2 start, FPVector2 delta, FP64 radiusStart, FP64 radiusEnd, out FP64 time)
        {
            time = FP64.Zero;
            FP64 growth = radiusEnd - radiusStart;
            FP64 c = start.SqrMagnitude - radiusStart * radiusStart;
            if (c <= FP64.Zero) return true;
            FP64 a = delta.SqrMagnitude - growth * growth;
            FP64 b = start.X * delta.X + start.Y * delta.Y - radiusStart * growth;
            if (a >= FP64.Zero && b >= FP64.Zero) return false;
            if (a == FP64.Zero)
            {
                if (b >= FP64.Zero) return false;
                time = -c / (FP64.FromInt(2) * b);
                return time >= FP64.Zero && time <= FP64.One;
            }
            FP64 disc = b * b - a * c;
            if (disc < FP64.Zero) return false;
            FP64 root = FPMath.Sqrt(disc);
            FP64 first = (-b - root) / a, second = (-b + root) / a;
            bool firstValid = first >= FP64.Zero && first <= FP64.One;
            bool secondValid = second >= FP64.Zero && second <= FP64.One;
            if (!firstValid && !secondValid) return false;
            time = firstValid && secondValid ? FPMath.Min(first, second) : firstValid ? first : second;
            return true;
        }
        internal bool TryGet(int id, out ProjectileState p) => byId.TryGetValue(id, out p) && !deaths.ContainsKey(id);
        internal void ConsumeHit(ProjectileState p)
        {
            // Every projectile is consumed by its first effective hit.
            deaths[p.Id] = ProjectileDeathReason.Hit;
        }
        internal bool DamageEnergyOrb(ProjectileState p, FP64 damage)
        {
            if (!p.IsEnergyOrb || !TryGet(p.Id, out _)) return false;
            p.Health = FPMath.Max(FP64.Zero, p.Health - damage);
            if (p.Health > FP64.Zero) return false;
            deaths[p.Id] = ProjectileDeathReason.Destroyed; return true;
        }
        public void CommitLifecycle()
        {
            foreach (var p in bullets)
            {
                if (deaths.ContainsKey(p.Id)) continue;
                int age = Tick - p.BirthTick;
                if (OutsideField(p)) deaths[p.Id] = ProjectileDeathReason.OutOfBounds;
                else if (age >= Math.Max(1, p.Settings.DurationTicks)) deaths[p.Id] = ProjectileDeathReason.Duration;
            }
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var p = bullets[i];
                if (!deaths.TryGetValue(p.Id, out var reason)) continue;
                if (p.IsEnergyOrb && (reason == ProjectileDeathReason.Duration || reason == ProjectileDeathReason.OutOfBounds)) EnergyOrbMissed?.Invoke(p.Id);
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
        }
        internal void Spawn(int source, int depth, FPVector2 position, FP64 angle, FP64 speed, CompiledProjectile definition, ProjectileFaction faction = ProjectileFaction.Enemy, int ownerId = 2, int explosionTick = -1)
        {
            if (Fault != null) return;
            if (bullets.Count >= maxBullets)
            {
                Fault = $"Tick {Tick}：片段 {source + 1} 超过活跃弹上限 {maxBullets}。请调整发射点密度／寿命／分裂数量。";
                return;
            }
            var def = LWBodyDef.Dynamic(LWShape.Circle(definition.Radius), position, FP64.One);
            def.Layer = faction == ProjectileFaction.Enemy ? EnemyBulletLayer : FriendlyBulletLayer; def.Mask = faction == ProjectileFaction.Enemy ? PlayerLayer : (ushort)0; def.Flags = LWBodyFlags.Trigger;
            def.Velocity = DirectionVector(angle) * speed;
            int id = world.AddBody(def);
            var p = recycled.Count > 0 ? recycled.Pop() : new ProjectileState();
            p.Id = id; p.Faction = faction; p.OwnerId = ownerId; p.PreviousPosition = position; p.SourceClip = source; p.BirthTick = p.FireTick = Tick; p.Depth = depth;
            p.Position = position;
            p.Direction = angle; p.Settings = definition.Settings; p.Definition = definition; p.Radius = definition.Radius;
            p.PreviousRadius = p.Radius; p.VisualSize = definition.VisualSize; p.GrowthProgress = FP64.Zero;
            p.IsEnergyOrb = faction == ProjectileFaction.Enemy && depth == 0 && definition.Child != null &&
                definition.Settings.Interaction == ProjectileInteraction.EnergyOrb;
            p.Health = p.IsEnergyOrb ? FP64.FromInt(EnergyOrbSettings.MaxHealth) : FP64.Zero;
            p.ExplosionTick = explosionTick;
            bullets.Add(p); byId.Add(id, p); TotalSpawned++;
            PeakBullets = Math.Max(PeakBullets, bullets.Count);
        }

        private void RemoveAt(int index, ProjectileState p)
        {
            world.RemoveBody(p.Id); byId.Remove(p.Id);
            int last = bullets.Count - 1;
            bullets[index] = bullets[last]; bullets.RemoveAt(last);
            // Stable body IDs never reuse the pairs retained until next tick.
            recycled.Push(p);
        }

        internal void ReportFault(string message) { if (Fault == null) Fault = message; }
        private void UpdateGrowth(ProjectileState p, int tick)
        {
            if (!p.IsEnergyOrb || p.ExplosionTick <= p.BirthTick) return;
            FP64 progress = FPMath.Clamp(FP64.FromFraction(tick - p.BirthTick, p.ExplosionTick - p.BirthTick), FP64.Zero, FP64.One);
            p.GrowthProgress = progress;
            if (p.Settings.EnergyOrb.GrowthCurve == EnergyOrbGrowthCurve.SmoothStep)
                progress = progress * progress * (FP64.FromInt(3) - FP64.FromInt(2) * progress);
            p.Radius = p.Definition.Radius + (p.Definition.FinalRadius - p.Definition.Radius) * progress;
            p.VisualSize = p.Definition.VisualSize + (p.Definition.FinalVisualSize - p.Definition.VisualSize) * progress;
            var body = world.GetBody(p.Id); body.Shape = LWShape.Circle(p.Radius); world.SetBody(p.Id, body);
        }

        internal void SplitMothers(int sourceClip)
        {
            splits.Clear();
            // Capture values before pooling parents; children may reuse the same state objects.
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var parent = bullets[i];
                if (parent.SourceClip != sourceClip || parent.Faction != ProjectileFaction.Enemy || parent.Depth != 0 ||
                    parent.Definition.Child == null || deaths.ContainsKey(parent.Id)) continue;
                if (parent.IsEnergyOrb && parent.Health <= FP64.Zero) continue;
                if (Tick - parent.BirthTick >= Math.Max(1, parent.Settings.DurationTicks) || OutsideField(parent)) continue;
                splits.Add(new SplitSpawn
                {
                    Faction = parent.Faction, OwnerId = parent.OwnerId, SourceClip = parent.SourceClip, Position = parent.Position,
                    Count = Math.Max(1, Math.Min(64, parent.Settings.SplitCount)),
                    Direction = parent.Settings.InheritDirection ? parent.Direction : FP64.Zero,
                    Speed = parent.Definition.ChildSpeed, Definition = parent.Definition.Child
                });
                // An intact mother that reaches its explosion deadline was not destroyed in time.
                if (parent.IsEnergyOrb) EnergyOrbMissed?.Invoke(parent.Id);
                RemoveAt(i, parent);
            }
            foreach (var p in splits)
                for (int i = 0; i < p.Count && Fault == null; i++)
                    Spawn(p.SourceClip, 1, p.Position, p.Direction + FPMath.PI_TIMES_2 * FP64.FromFraction(i, p.Count),
                        p.Speed, p.Definition, p.Faction, p.OwnerId);
            splits.Clear();
        }

        internal static CompiledProjectile CompileProjectile(ProjectileSettings source, bool allowSplit = false)
        {
            var settings = source.Copy();
            var definition = new CompiledProjectile
            {
                Settings = settings, Radius = FP64.FromFloat(Math.Max(0.001f, settings.HitRadius)),
                ChildSpeed = FP64.FromFloat(Math.Max(0, settings.SplitSpeed))
            };
            definition.VisualSize = FP64.FromFloat(Math.Max(0.01f, settings.VisualSize));
            definition.FinalVisualSize = FP64.FromFloat(Math.Max(0.01f, settings.EnergyOrb.FinalVisualSize));
            definition.FinalRadius = FP64.FromFloat(Math.Max(0.001f, settings.EnergyOrb.FinalHitRadius));
            if (allowSplit)
            {
                if (settings.SplitChild) definition.Child = CompileProjectile(settings.SplitChild.Settings);
                else
                {
                    var child = settings.Copy(); child.SplitChild = null;
                    child.VisualSize *= 0.65f;
                    definition.Child = CompileProjectile(child);
                }
            }
            return definition;
        }

        private static FPVector2 DirectionVector(FP64 angle) => new(FPMath.Cos(angle), FPMath.Sin(angle));
    }
}
