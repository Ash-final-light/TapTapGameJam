using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using UnityEngine;
namespace TapTap.Danmaku
{
    /// <summary>Fixed-player authoring preview over the same projectile core as live battles.</summary>
    public sealed class DanmakuSimulation
    {
        private DanmakuSequence sequence;
        private int solo;
        private bool publish = true;
        private readonly List<ProjectileHit> hits = new();
        public ProjectileWorld World { get; private set; }
        public BossSequenceRunner Timeline { get; private set; }
        public int Tick => World.Tick;
        public int TotalSpawned => World.TotalSpawned;
        public int PeakBullets => World.PeakBullets;
        public int HitCount { get; private set; }
        public string Fault => World.Fault;
        public bool Finished => Timeline.Finished;
        public DanmakuPlayerState PlayerState => World.PlayerState;
        public IReadOnlyList<ProjectileState> Bullets => World.Bullets;
        public IReadOnlyList<ProjectileHit> StepHits => hits;
        public event Action<ProjectileHit> OnHit;
        public DanmakuSimulation(DanmakuSequence sequence, int soloClip = -1) => Reset(sequence, soloClip);
        public void Reset(DanmakuSequence source, int solo = -1)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            sequence = source; this.solo = solo; HitCount = 0; hits.Clear();
            World = new ProjectileWorld(source); Timeline = new BossSequenceRunner(source, World, solo);
        }
        public void SetPlayerState(in DanmakuPlayerState state) => World.SetPlayerState(state);
        public void Seek(int tick)
        {
            tick = Math.Max(0, Math.Min(sequence.DurationTicks, tick)); Reset(sequence, solo); publish = false;
            try { while (Tick < tick && Fault == null) Step(); } finally { publish = true; }
        }
        public void Step()
        {
            if (Finished || Fault != null) return;
            hits.Clear(); World.BeginTick(); Timeline.Step(); World.Advance();
            foreach (var contact in World.Contacts)
                if (contact.TargetKind == CombatTargetKind.Player && World.TryGet(contact.ProjectileId, out var p))
                { hits.Add(new ProjectileHit(p, Tick, PlayerState)); HitCount++; World.ConsumeHit(p); }
            World.CommitLifecycle(); Timeline.AfterStep();
            if (publish) foreach (var hit in hits) OnHit?.Invoke(hit);
        }
        public void Clear(int clipIndex = -1) => World.Clear(clipIndex);
        public Vector2 EmitterPosition(int index) => Timeline.EmitterPosition(index);
        public static FPVector2 ToFP(Vector2 v) => FPVector2.FromFloat(v.x, v.y);
        public static Vector2 ToVector(FPVector2 v) => new(v.X.ToFloat(), v.Y.ToFloat());
    }
}
