using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using TapTap.Danmaku;
using UnityEngine;

namespace TapTap.Player
{
    public sealed class CompiledPlayerClip
    {
        public readonly PlayerActionClip Source;
        public readonly int Duration, ActiveStart, RecoveryStart, Cooldown;
        public readonly PlayerActionKind Kind;
        public readonly PlayerClipEndMode EndMode;
        public readonly bool AllowFire;
        public readonly PlayerIntentMask Transitions;
        public readonly FP64 Cost;
        public readonly PlayerActionNode[] Nodes;
        public CompiledPlayerClip(PlayerActionClip source)
        {
            Source = source; Duration = source.DurationTicks; ActiveStart = source.ActiveStartTick; RecoveryStart = source.RecoveryStartTick;
            Cooldown = source.CooldownTicks; Kind = source.ActionKind; EndMode = source.EndMode;
            AllowFire = source.DefaultAllowFire; Transitions = source.DefaultTransitions; Cost = FP64.FromFloat(source.StaminaCost);
            Nodes = new PlayerActionNode[source.Nodes.Count];
            for (int i = 0; i < Nodes.Length; i++) { Nodes[i] = source.Nodes[i].Copy(); Nodes[i].Id = source.Nodes[i].Id; }
            Array.Sort(Nodes, (a, b) => { int c = a.StartTick.CompareTo(b.StartTick); return c != 0 ? c : a.Order.CompareTo(b.Order); });
        }
    }
    public sealed class PlayerActionInstance
    {
        public readonly CompiledPlayerClip Clip;
        public readonly int Id, SkillSlot, DashFacing;
        public int LocalTick { get; internal set; }
        internal int LastEmittedTick = -1;
        internal readonly HashSet<string> Effects = new();
        public PlayerActionInstance(CompiledPlayerClip clip, int id, int slot, int facing)
        { Clip = clip; Id = id; SkillSlot = slot; DashFacing = facing; }
        public PlayerActionPhase Phase => Clip.ActiveStart < 0 ? PlayerActionPhase.Active : LocalTick < Clip.ActiveStart ? PlayerActionPhase.Startup :
            Clip.RecoveryStart >= 0 && LocalTick >= Clip.RecoveryStart ? PlayerActionPhase.Recovery : PlayerActionPhase.Active;
    }
    /// <summary>Single owner of player resources, inputs and actions. Battle references Actor directly.</summary>
    public sealed class PlayerRuntime
    {
        private static readonly FP64 Dt = FP64.FromFraction(1, 60);
        private readonly Dictionary<PlayerClipSlot, CompiledPlayerClip> clips = new();
        private readonly Dictionary<PlayerActionClip, int> cooldowns = new();
        private readonly FP64 maxStamina, recovery, flightDrain;
        private readonly int recoveryDelay, hurtTicks, flyHoldTicks;
        private int instanceId, recoverAt, hurtUntil = -1;
        public readonly PlayerDefinition Definition;
        public readonly PlayerMotor Motor;
        public readonly PlayerIntentBuffer Inputs;
        public readonly ActorState Actor;
        public readonly List<PlayerRuntimeEvent> Events = new();
        public readonly List<PlayerIntentDecision> Decisions = new();
        public PlayerActionInstance Action { get; private set; }
        public PlayerActionInstance LocomotionAction { get; private set; }
        public FP64 Stamina { get; private set; }
        public FP64 MaxStamina => maxStamina;
        public int Tick { get; private set; }
        public bool ActionInvulnerable { get; private set; }
        public bool Invulnerable => ActionInvulnerable || Tick < hurtUntil;
        public bool FireAllowed { get; private set; } = true;
        public PlayerIntentMask AllowedTransitions { get; private set; }
        public PlayerMotionMode Motion { get; private set; }
        public FP64 MotionScale { get; private set; } = FP64.One;
        public PlayerActionInstance VisualAction => Action ?? LocomotionAction;
        public PlayerRuntime(PlayerDefinition definition, Vector2 fieldSize, Vector2 spawn, PlayerClipSlot? previewSlot = null, Rect? bounds = null)
        {
            if (!definition) throw new ArgumentNullException(nameof(definition));
            string error = definition.Validate(); if (error != null) throw new ArgumentException(error);
            Definition = definition; Motor = new(definition, fieldSize, spawn, bounds); Inputs = new(definition.Input);
            Actor = new(1, FP64.FromFloat(definition.MaxHealth), Motor.State);
            maxStamina = Stamina = FP64.FromFloat(definition.MaxStamina); recovery = FP64.FromFloat(definition.StaminaRecoveryPerSecond) * Dt;
            flightDrain = FP64.FromFloat(definition.Motor.FlightStaminaPerSecond) * Dt;
            recoveryDelay = definition.StaminaRecoveryDelayTicks; hurtTicks = definition.HurtInvulnerableTicks; flyHoldTicks = definition.Input.FlyHoldTicks;
            foreach (var binding in definition.Actions.Bindings) clips.Add(binding.Slot, new(binding.Clip));
            if (previewSlot.HasValue) StartSlot(previewSlot.Value, false, 0);
        }
        public void BeginTick(in PlayerTickInput input)
        {
            Events.Clear(); Decisions.Clear(); Inputs.Capture(input, Tick, Decisions);
            if (!Actor.Alive) { Inputs.Clear(); if (Action?.Clip.Kind != PlayerActionKind.Dead) { StopAction(); StartSlot(PlayerClipSlot.Dead, false, 0); } Evaluate(); return; }
            if ((input.Edges & PlayerButtonEdges.UseItem) != 0)
                Events.Add(new(PlayerRuntimeEventKind.IntentExecuted, null, 0, intent: PlayerIntentKind.UseItem));
            CompleteIfNeeded();
            if (Motor.Flying && (!Inputs.JumpHeld || Stamina < flightDrain))
            { Motor.SetFlight(false); if (Action == null) StartSlot(PlayerClipSlot.FlyEnd, false, 0); }
            Evaluate();
            bool transitioned = false;
            for (int i = 0; i < Inputs.Pending.Count; i++)
            {
                var intent = Inputs.Pending[i];
                string reason = CanExecute(intent);
                if (reason != null) { Decisions.Add(new(intent, reason)); continue; }
                if (intent.Kind == PlayerIntentKind.UseItem)
                { Events.Add(new(PlayerRuntimeEventKind.IntentExecuted, null, 0, intent: intent.Kind)); Inputs.Consume(i); transitioned = true; break; }
                var slot = intent.Kind == PlayerIntentKind.Skill ? intent.Slot == 1 ? PlayerClipSlot.Skill1 : PlayerClipSlot.Skill2 :
                    intent.Kind == PlayerIntentKind.Dash ? PlayerClipSlot.Dash : PlayerClipSlot.Jump;
                StopAction(); StartSlot(slot, true, intent.Slot, intent.Direction);
                Events.Add(new(PlayerRuntimeEventKind.IntentExecuted, Action.Clip.Source, Action.Id, intent: intent.Kind, skillSlot: intent.Slot));
                Decisions.Add(new(intent, "执行", true)); Inputs.Consume(i); transitioned = true; break;
            }
            if (!transitioned && !Motor.Grounded && !Motor.Flying && Inputs.JumpHeld && Inputs.HeldTicks >= flyHoldTicks && Stamina >= flightDrain &&
                (Action == null || (AllowedTransitions & PlayerIntentMask.Fly) != 0))
            { StopAction(); Motor.SetFlight(true); StartSlot(PlayerClipSlot.FlyStart, false, 0); }
            Evaluate(); Emit(Action);
            if (Motor.Flying) { Stamina = FPMath.Max(FP64.Zero, Stamina - flightDrain); recoverAt = Tick + recoveryDelay; }
            else if (Tick >= recoverAt) Stamina = FPMath.Min(maxStamina, Stamina + recovery);
            bool landed = Motor.Step(input.Horizontal, Motion, MotionScale, Action?.DashFacing ?? Motor.Facing);
            Actor.Pose = Motor.State;
            if (landed && Action?.Clip.Kind != PlayerActionKind.Dead) { StopAction(); StartSlot(PlayerClipSlot.Land, false, 0); Evaluate(); Emit(Action); }
            UpdateLocomotion(input.Horizontal); Emit(LocomotionAction, false);
        }
        public void EndTick()
        { Advance(Action); Advance(LocomotionAction); Tick++; }
        public void ClearInput() => Inputs.Clear();
        public void ForceInterrupt() { StopAction(); Evaluate(); }
        public bool CanReceiveDamage => Actor.Alive && !Invulnerable;
        public FP64 ReceiveDamage(FP64 damage)
        {
            if (!CanReceiveDamage || damage <= FP64.Zero) return FP64.Zero;
            var applied = FPMath.Min(Actor.Health, damage); Actor.Health -= applied;
            if (hurtTicks > 0) hurtUntil = Tick + hurtTicks;
            StopAction(); StartSlot(Actor.Alive ? PlayerClipSlot.Hurt : PlayerClipSlot.Dead, false, 0);
            if (!Actor.Alive) { Motor.SetFlight(false); Inputs.Clear(); }
            Evaluate(); Emit(Action); return applied;
        }
        private string CanExecute(PlayerIntent intent)
        {
            var mask = intent.Kind switch { PlayerIntentKind.Skill => PlayerIntentMask.Skill, PlayerIntentKind.Dash => PlayerIntentMask.Dash,
                PlayerIntentKind.Jump => PlayerIntentMask.Jump, PlayerIntentKind.UseItem => PlayerIntentMask.UseItem, _ => PlayerIntentMask.None };
            if (Action != null && (AllowedTransitions & mask) == 0) return "当前动作未开放接续窗口";
            if (intent.Kind == PlayerIntentKind.UseItem) return null;
            var slot = intent.Kind == PlayerIntentKind.Skill ? intent.Slot == 1 ? PlayerClipSlot.Skill1 : PlayerClipSlot.Skill2 :
                intent.Kind == PlayerIntentKind.Dash ? PlayerClipSlot.Dash : PlayerClipSlot.Jump;
            if (!clips.TryGetValue(slot, out var clip)) return "动作未绑定";
            if (intent.Kind == PlayerIntentKind.Jump && !Motor.Grounded) return "跳跃需要接地";
            if (Stamina < clip.Cost) return "体力不足";
            if (cooldowns.TryGetValue(clip.Source, out int until) && Tick < until) return "动作冷却中";
            return null;
        }
        private void StartSlot(PlayerClipSlot slot, bool pay, int skillSlot, short direction = 0)
        {
            if (Action != null || !clips.TryGetValue(slot, out var clip)) return;
            if (pay)
            {
                Stamina -= clip.Cost;
                if (clip.Cost > FP64.Zero) recoverAt = Tick + recoveryDelay;
                cooldowns[clip.Source] = Tick + clip.Cooldown;
            }
            if (clip.Kind == PlayerActionKind.Dash) Motor.SetFlight(false);
            Action = new(clip, ++instanceId, skillSlot, direction == 0 ? Motor.Facing : direction < 0 ? -1 : 1);
            Events.Add(new(PlayerRuntimeEventKind.ActionStarted, clip.Source, Action.Id));
        }
        private void StopAction() { Stop(Action); Action = null; }
        private void Stop(PlayerActionInstance action)
        {
            if (action == null) return;
            foreach (var n in action.Clip.Nodes) if (action.Effects.Contains(n.Id)) Events.Add(new(PlayerRuntimeEventKind.EffectStopped, action.Clip.Source, action.Id, n));
            Events.Add(new(PlayerRuntimeEventKind.ActionStopped, action.Clip.Source, action.Id));
        }
        private void CompleteIfNeeded()
        {
            if (Action == null || Action.LocalTick < Action.Clip.Duration) return;
            if (Action.Clip.EndMode == PlayerClipEndMode.Loop) { Action.LocalTick = 0; Action.LastEmittedTick = -1; }
            else if (Action.Clip.EndMode == PlayerClipEndMode.HoldLast) Action.LocalTick = Action.Clip.Duration - 1;
            else StopAction();
        }
        private void Evaluate()
        {
            ActionInvulnerable = false; FireAllowed = Actor.Alive; Motion = Actor.Alive ? PlayerMotionMode.Normal : PlayerMotionMode.Disabled; MotionScale = FP64.One;
            AllowedTransitions = Action == null ? PlayerIntentMask.All : Action.Clip.Transitions;
            if (Action == null) return;
            FireAllowed &= Action.Clip.AllowFire;
            foreach (var n in Action.Clip.Nodes)
            {
                if (!n.Contains(Action.LocalTick)) continue;
                switch (n.Kind)
                {
                    case PlayerActionNodeKind.Invulnerability: ActionInvulnerable = true; break;
                    case PlayerActionNodeKind.FirePermission: FireAllowed = Actor.Alive && n.AllowFire; break;
                    case PlayerActionNodeKind.Motion: Motion = n.Motion; MotionScale = FP64.FromFloat(n.SpeedScale); break;
                    case PlayerActionNodeKind.Transition:
                        if (ConditionsPass(n.Conditions)) AllowedTransitions |= n.AllowedIntents; break;
                }
            }
        }
        private bool ConditionsPass(PlayerTransitionCondition[] conditions)
        {
            foreach (var c in conditions)
                if (c == PlayerTransitionCondition.Airborne && Motor.Grounded || c == PlayerTransitionCondition.Grounded && !Motor.Grounded ||
                    c == PlayerTransitionCondition.JumpHeld && !Inputs.JumpHeld || c == PlayerTransitionCondition.HoldThreshold && Inputs.HeldTicks < flyHoldTicks ||
                    c == PlayerTransitionCondition.HasFlightStamina && Stamina < flightDrain) return false;
            return true;
        }
        private void Emit(PlayerActionInstance action, bool gameplay = true)
        {
            if (action == null || action.LastEmittedTick == action.LocalTick) return;
            action.LastEmittedTick = action.LocalTick;
            foreach (var n in action.Clip.Nodes)
            {
                if (n.Kind == PlayerActionNodeKind.Effect)
                {
                    bool active = n.Contains(action.LocalTick), held = action.Effects.Contains(n.Id);
                    if (active && !held) { action.Effects.Add(n.Id); Events.Add(new(PlayerRuntimeEventKind.EffectStarted, action.Clip.Source, action.Id, n)); }
                    else if (!active && held) { action.Effects.Remove(n.Id); Events.Add(new(PlayerRuntimeEventKind.EffectStopped, action.Clip.Source, action.Id, n)); }
                }
                if (!n.IsPoint || n.StartTick != action.LocalTick) continue;
                // Rising reuses the Jump clip for its pose; its launch sound belongs to the accepted jump action.
                if (n.Kind == PlayerActionNodeKind.Audio)
                { if (gameplay || action.Clip.Kind != PlayerActionKind.Jump) Events.Add(new(PlayerRuntimeEventKind.Audio, action.Clip.Source, action.Id, n)); }
                else if (gameplay) { if (n.Event == PlayerPointEventKind.JumpImpulse) Motor.Jump(); Events.Add(new(PlayerRuntimeEventKind.PointEvent, action.Clip.Source, action.Id, n)); }
            }
        }
        private void UpdateLocomotion(short horizontal)
        {
            if (Action != null)
            { Stop(LocomotionAction); LocomotionAction = null; return; }
            var slot = Motor.Locomotion switch
            {
                PlayerLocomotionState.Flying => PlayerClipSlot.FlyLoop,
                PlayerLocomotionState.Rising => PlayerClipSlot.Jump,
                PlayerLocomotionState.Falling => PlayerClipSlot.Fall,
                _ => horizontal == 0 ? PlayerClipSlot.Idle : PlayerClipSlot.Move,
            };
            clips.TryGetValue(slot, out var clip);
            if (LocomotionAction?.Clip != clip)
            {
                Stop(LocomotionAction); LocomotionAction = clip == null ? null : new(clip, ++instanceId, 0, Motor.Facing);
                if (LocomotionAction != null) Events.Add(new(PlayerRuntimeEventKind.ActionStarted, clip.Source, LocomotionAction.Id));
            }
            if (LocomotionAction != null && LocomotionAction.LocalTick >= LocomotionAction.Clip.Duration)
            { LocomotionAction.LocalTick = 0; LocomotionAction.LastEmittedTick = -1; }
        }
        private static void Advance(PlayerActionInstance action) { if (action != null && action.LocalTick < int.MaxValue) action.LocalTick++; }
    }
}
