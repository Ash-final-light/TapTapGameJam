using System.Collections.Generic;

namespace TapTap.Player
{
    public readonly struct PlayerIntent
    {
        public readonly PlayerIntentKind Kind;
        public readonly int Slot, PressedTick, ExpireTick, Sequence;
        public readonly short Direction;
        public PlayerIntent(PlayerIntentKind kind, int slot, int tick, int expiry, int sequence, short direction)
        { Kind = kind; Slot = slot; PressedTick = tick; ExpireTick = expiry; Sequence = sequence; Direction = direction; }
    }
    public readonly struct PlayerIntentDecision
    {
        public readonly PlayerIntent Intent;
        public readonly string Reason;
        public readonly bool Executed;
        public PlayerIntentDecision(PlayerIntent intent, string reason, bool executed = false)
        { Intent = intent; Reason = reason; Executed = executed; }
    }
    public sealed class PlayerIntentBuffer
    {
        private readonly List<PlayerIntent> pending = new(32);
        private readonly int duration, skillPriority, dashPriority, jumpPriority;
        private int sequence;
        public IReadOnlyList<PlayerIntent> Pending => pending;
        public int HeldTicks { get; private set; }
        public bool JumpHeld { get; private set; }
        public PlayerIntentBuffer(PlayerInputPolicy policy)
        { duration = policy.BufferTicks; skillPriority = policy.SkillPriority; dashPriority = policy.DashPriority; jumpPriority = policy.JumpPriority; }
        public int Priority(PlayerIntentKind kind) => kind == PlayerIntentKind.Skill ? skillPriority : kind == PlayerIntentKind.Dash ? dashPriority : jumpPriority;
        public void Capture(in PlayerTickInput input, int tick, List<PlayerIntentDecision> decisions)
        {
            JumpHeld = input.JumpHeld && (input.Edges & PlayerButtonEdges.JumpReleased) == 0;
            HeldTicks = JumpHeld ? HeldTicks < int.MaxValue ? HeldTicks + 1 : HeldTicks : 0;
            for (int i = pending.Count - 1; i >= 0; i--)
                if (tick > pending[i].ExpireTick) { decisions.Add(new(pending[i], "缓冲已过期")); pending.RemoveAt(i); }
            AddEdge(PlayerButtonEdges.Skill1, PlayerIntentKind.Skill, 1, input, tick);
            AddEdge(PlayerButtonEdges.Skill2, PlayerIntentKind.Skill, 2, input, tick);
            AddEdge(PlayerButtonEdges.Dash, PlayerIntentKind.Dash, 0, input, tick);
            AddEdge(PlayerButtonEdges.JumpPressed, PlayerIntentKind.Jump, 0, input, tick);
            // Items resolve at the battle tick boundary, independently of animation transitions.
            pending.Sort((a, b) =>
            {
                int c = Priority(b.Kind).CompareTo(Priority(a.Kind)); if (c != 0) return c;
                c = a.PressedTick.CompareTo(b.PressedTick); if (c != 0) return c;
                c = a.Slot.CompareTo(b.Slot); return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
            });
        }
        public void Queue(PlayerIntentKind kind, int slot, int tick, short direction = 0)
        {
            for (int i = pending.Count - 1; i >= 0; i--) if (pending[i].Kind == kind && pending[i].Slot == slot) pending.RemoveAt(i);
            if (pending.Count < 32) pending.Add(new(kind, slot, tick, tick + duration - 1, sequence++, direction));
        }
        private void AddEdge(PlayerButtonEdges edge, PlayerIntentKind kind, int slot, in PlayerTickInput input, int tick)
        { if ((input.Edges & edge) != 0) Queue(kind, slot, tick, input.Horizontal); }
        internal void Consume(int index) => pending.RemoveAt(index);
        public void Clear() { pending.Clear(); HeldTicks = 0; JumpHeld = false; }
    }
}
