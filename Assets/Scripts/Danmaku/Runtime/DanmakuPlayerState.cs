using Framework.FixedPoint;

namespace TapTap.Danmaku
{
    public readonly struct DanmakuPlayerState
    {
        public readonly FPVector2 Position, PreviousPosition;
        public readonly FP64 HitRadius;
        public readonly bool CollisionEnabled, Focused;

        public DanmakuPlayerState(FPVector2 position, FPVector2 previousPosition, FP64 hitRadius,
            bool collisionEnabled = true, bool focused = false)
        {
            Position = position; PreviousPosition = previousPosition; HitRadius = hitRadius;
            CollisionEnabled = collisionEnabled; Focused = focused;
        }
    }
}
