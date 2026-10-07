using Framework.FixedPoint;

namespace Inputs
{
    /// <summary>One game tick of input. Signed 16-bit axes make the input record independent of floating point movement.</summary>
    public readonly struct InputSnapshot
    {
        public readonly short MoveX, MoveY;
        public readonly bool Focused, FireHeld, BombPressed, PausePressed;

        public InputSnapshot(short moveX, short moveY, bool focused = false, bool fireHeld = false,
            bool bombPressed = false, bool pausePressed = false)
        {
            MoveX = moveX; MoveY = moveY; Focused = focused; FireHeld = fireHeld;
            BombPressed = bombPressed; PausePressed = pausePressed;
        }

        public FPVector2 Move => new(FP64.FromFraction(MoveX, short.MaxValue), FP64.FromFraction(MoveY, short.MaxValue));
    }
}
