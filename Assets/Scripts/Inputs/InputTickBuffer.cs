using UnityEngine;

namespace Inputs
{
    /// <summary>Holds continuous axes and button edges until a logic tick consumes them. No per-tick allocation.</summary>
    public sealed class InputTickBuffer
    {
        private short moveX, moveY;
        private bool focused, fireHeld;
        private int bombs, pauses;

        public void Capture(Vector2 move, bool focus, bool fire)
        {
            moveX = Quantize(move.x); moveY = Quantize(move.y);
            focused = focus; fireHeld = fire;
        }

        private static short Quantize(float value) => float.IsNaN(value) ? (short)0 :
            (short)Mathf.RoundToInt(Mathf.Clamp(value, -1, 1) * short.MaxValue);

        public void QueueBomb() { if (bombs < 1024) bombs++; }
        public void QueuePause() { if (pauses < 1024) pauses++; }

        public InputSnapshot ConsumeTick()
        {
            bool bomb = bombs > 0, pause = pauses > 0;
            if (bomb) bombs--;
            if (pause) pauses--;
            return new InputSnapshot(moveX, moveY, focused, fireHeld, bomb, pause);
        }

        public void DiscardButtons() => bombs = pauses = 0;
        public void Clear() { moveX = moveY = 0; focused = fireHeld = false; DiscardButtons(); }
    }
}
