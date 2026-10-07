using System;
using Framework.FixedPoint;
using Inputs;
using UnityEngine;

namespace TapTap.Danmaku
{
    /// <summary>Only owner of player movement. No Transform reads and no Unity physics integration.</summary>
    public sealed class DanmakuPlayerMotor
    {
        private static readonly FP64 Dt = FP64.FromFraction(1, DanmakuSequence.TickRate);
        private readonly FP64 normalSpeed, focusSpeed, hitRadius;
        private readonly FPVector2 limit;
        private FPVector2 position, previous;
        private bool focused, collisionEnabled = true;
        public DanmakuPlayerState State => new(position, previous, hitRadius, collisionEnabled, focused);

        public DanmakuPlayerMotor(DanmakuPlayerConfig config, Vector2 fieldSize, Vector2 spawnPosition)
            : this(config, fieldSize, DanmakuSimulation.ToFP(spawnPosition)) { }

        public DanmakuPlayerMotor(DanmakuPlayerConfig config, Vector2 fieldSize, FPVector2 spawnPosition)
        {
            if (!config) throw new ArgumentNullException(nameof(config));
            normalSpeed = FP64.FromFloat(Mathf.Max(0, config.NormalMoveSpeed));
            focusSpeed = FP64.FromFloat(Mathf.Max(0, config.FocusMoveSpeed));
            hitRadius = FP64.FromFloat(Mathf.Max(0.001f, config.HitRadius));
            var half = DanmakuSimulation.ToFP(fieldSize) * FP64.Half;
            limit = new FPVector2(FPMath.Max(FP64.Zero, half.X - hitRadius), FPMath.Max(FP64.Zero, half.Y - hitRadius));
            Reset(spawnPosition);
        }

        public void Step(in InputSnapshot input)
        {
            previous = position;
            focused = input.Focused;
            FPVector2 move = input.Move;
            // Preserve stick strength below one; diagonal keyboard movement never exceeds full speed.
            if (move.SqrMagnitude > FP64.One) move = move.Normalized;
            position = Clamp(position + move * ((focused ? focusSpeed : normalSpeed) * Dt));
        }

        public void Reset(FPVector2 spawnPosition)
        { position = previous = Clamp(spawnPosition); focused = false; collisionEnabled = true; }

        public void SetCollisionEnabled(bool enabled) => collisionEnabled = enabled;

        private FPVector2 Clamp(FPVector2 value) => new(
            FPMath.Clamp(value.X, -limit.X, limit.X), FPMath.Clamp(value.Y, -limit.Y, limit.Y));
    }
}
