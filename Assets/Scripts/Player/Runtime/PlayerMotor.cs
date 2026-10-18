using Framework.FixedPoint;
using TapTap.Danmaku;
using UnityEngine;

namespace TapTap.Player
{
    /// <summary>Flat arena motor. Owns position; Spine/Transform never feed the simulation.</summary>
    public sealed class PlayerMotor
    {
        private static readonly FP64 Dt = FP64.FromFraction(1, PlayerActionClip.TickRate);
        private readonly FP64 moveSpeed, gravity, jumpSpeed, dashSpeed, riseSpeed, hitRadius, groundHeight;
        private FP64 floor;
        private FPVector2 minimum, maximum;
        private FPVector2 position, previous;
        private FP64 verticalSpeed;
        public FP64 MoveFlat { get; set; }
        public FP64 MoveMultiplier { get; set; } = FP64.One;
        public int Facing { get; private set; } = 1;
        public bool Flying { get; private set; }
        public bool Grounded => !Flying && position.Y <= floor && verticalSpeed <= FP64.Zero;
        public PlayerLocomotionState Locomotion => Flying ? PlayerLocomotionState.Flying : Grounded ? PlayerLocomotionState.Grounded :
            verticalSpeed > FP64.Zero ? PlayerLocomotionState.Rising : PlayerLocomotionState.Falling;
        public DanmakuPlayerState State => new(position, previous, hitRadius);
        public FP64 VerticalSpeed => verticalSpeed;
        public PlayerMotor(PlayerDefinition definition, Vector2 fieldSize, Vector2 spawn, Rect? bounds = null)
        {
            var c = definition.Motor;
            moveSpeed = FP64.FromFloat(c.MoveSpeed); gravity = FP64.FromFloat(c.Gravity);
            jumpSpeed = FP64.FromFloat(c.JumpSpeed); dashSpeed = FP64.FromFloat(c.DashSpeed); riseSpeed = FP64.FromFloat(c.FlightRiseSpeed);
            hitRadius = FP64.FromFloat(definition.HitRadius);
            groundHeight = FP64.FromFloat(c.GroundHeight);
            SetBounds(bounds ?? new Rect(-fieldSize * 0.5f, fieldSize));
            position = previous = Clamp(DanmakuSimulation.ToFP(spawn));
        }
        public void SetBounds(Rect bounds)
        {
            var center = DanmakuSimulation.ToFP(bounds.center);
            var half = DanmakuSimulation.ToFP(bounds.size) * FP64.Half;
            var inset = new FPVector2(FPMath.Max(FP64.Zero, half.X - hitRadius), FPMath.Max(FP64.Zero, half.Y - hitRadius));
            minimum = center - inset; maximum = center + inset;
            floor = FPMath.Clamp(groundHeight, minimum.Y, maximum.Y);
            // A resize is not swept movement and must not create a hit across the arena.
            position = previous = Clamp(position);
            if ((position.Y >= maximum.Y && verticalSpeed > FP64.Zero) || (position.Y <= floor && verticalSpeed < FP64.Zero))
                verticalSpeed = FP64.Zero;
        }
        public void Jump() { if (Grounded) { verticalSpeed = jumpSpeed; Flying = false; } }
        public void SetFlight(bool enabled) { Flying = enabled; if (enabled) verticalSpeed = FP64.Zero; }
        public bool Step(short horizontal, PlayerMotionMode mode, FP64 speedScale, int dashFacing)
        {
            previous = position;
            bool wasGrounded = Grounded;
            var axis = FP64.FromFraction(horizontal, short.MaxValue);
            if (mode == PlayerMotionMode.Dash) Facing = dashFacing;
            else if (horizontal != 0) Facing = horizontal < 0 ? -1 : 1;
            if (mode == PlayerMotionMode.Disabled) return false;
            FP64 vx = mode == PlayerMotionMode.Dash ? dashSpeed * FP64.FromInt(dashFacing) * speedScale : axis * FPMath.Max(FP64.Zero, (moveSpeed + MoveFlat) * MoveMultiplier) * speedScale;
            if (mode == PlayerMotionMode.Dash) verticalSpeed = FP64.Zero;
            else if (Flying || mode == PlayerMotionMode.Flight) verticalSpeed = riseSpeed;
            else if (!Grounded) verticalSpeed -= gravity * Dt;
            position = Clamp(position + new FPVector2(vx, verticalSpeed) * Dt);
            if (position.Y <= floor) { verticalSpeed = FP64.Zero; Flying = false; }
            else if (position.Y >= maximum.Y && verticalSpeed > FP64.Zero) verticalSpeed = FP64.Zero;
            return !wasGrounded && Grounded;
        }
        private FPVector2 Clamp(FPVector2 p) => new(FPMath.Clamp(p.X, minimum.X, maximum.X), FPMath.Clamp(p.Y, floor, maximum.Y));
    }
}
