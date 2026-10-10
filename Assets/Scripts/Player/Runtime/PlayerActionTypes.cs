using System;
using UnityEngine;
using Spine.Unity;
using TapTap.Danmaku;

namespace TapTap.Player
{
    public enum PlayerLocomotionState { Grounded = 0, Rising = 10, Falling = 20, Flying = 30 }
    public enum PlayerActionKind { None = 0, Jump = 10, Dash = 20, Skill = 30, Land = 40, Hurt = 50, Dead = 60 }
    public enum PlayerIntentKind { Skill = 10, Dash = 20, Jump = 30, Fly = 31, Fire = 40, UseItem = 50 }
    public enum PlayerClipSlot { Idle = 0, Move = 10, Jump = 20, Fall = 30, FlyStart = 40, FlyLoop = 41, FlyEnd = 42, Land = 50, Dash = 60, Skill1 = 70, Skill2 = 71, Hurt = 80, Dead = 90 }
    public enum PlayerActionPhase { Startup = 0, Active = 10, Recovery = 20 }
    public enum PlayerClipEndMode { Complete = 0, Loop = 10, HoldLast = 20 }
    public enum PlayerMotionMode { Normal = 0, Dash = 10, Flight = 20, Disabled = 30 }
    public enum PlayerPointEventKind { JumpImpulse = 10, EmitProjectile = 20, SkillPulse = 30 }
    public enum PlayerActionNodeKind { Spine = 0, Motion = 10, Invulnerability = 20, Transition = 30, FirePermission = 40, PointEvent = 50, Audio = 60, Effect = 70 }
    public enum PlayerTransitionCondition { Airborne = 0, Grounded = 10, JumpHeld = 20, HoldThreshold = 30, HasFlightStamina = 40 }
    public enum PlayerRuntimeEventKind { ActionStarted, ActionStopped, Audio, EffectStarted, EffectStopped, PointEvent, IntentExecuted }
    [Flags] public enum PlayerIntentMask { None = 0, Skill = 1, Dash = 2, Jump = 4, Fly = 8, Fire = 16, UseItem = 32, All = 63 }
    [Flags] public enum PlayerButtonEdges { None = 0, JumpPressed = 1, JumpReleased = 2, Dash = 4, Skill1 = 8, Skill2 = 16, UseItem = 32 }

    public readonly struct PlayerTickInput
    {
        public readonly short Horizontal;
        public readonly bool JumpHeld, FireHeld;
        public readonly PlayerButtonEdges Edges;
        public PlayerTickInput(short horizontal, bool jumpHeld = false, bool fireHeld = false, PlayerButtonEdges edges = 0)
        { Horizontal = horizontal; JumpHeld = jumpHeld; FireHeld = fireHeld; Edges = edges; }
    }

    [Serializable] public sealed class PlayerMotorConfig
    {
        [Min(0)] public float MoveSpeed = 4, Gravity = 20, JumpSpeed = 8, DashSpeed = 12, FlightRiseSpeed = 2;
        public float GroundHeight = -4;
        [Min(0)] public float FlightStaminaPerSecond = 12;
    }
    [Serializable] public sealed class PlayerInputPolicy
    {
        [Min(1)] public int BufferTicks = 8, FlyHoldTicks = 10;
        public int SkillPriority = 300, DashPriority = 200, JumpPriority = 100;
        public int Priority(PlayerIntentKind kind) => kind switch
        { PlayerIntentKind.Skill => SkillPriority, PlayerIntentKind.Dash => DashPriority, PlayerIntentKind.Jump => JumpPriority, _ => 0 };
    }
    [Serializable] public sealed class PlayerSpineSegment
    {
        public AnimationReferenceAsset Animation;
        [Tooltip("未提供专用动画时的明确占位绑定。编辑器会提示。")]
        public string FallbackAnimation;
        [Min(0)] public int TrackIndex;
        [Min(0)] public float SourceOffset;
        [Min(0.001f)] public float Speed = 1;
        [Min(0)] public float MixSeconds = 0.08f;
        public bool Loop;
        public string AnimationName => Animation && Animation.Animation != null ? Animation.Animation.Name : FallbackAnimation;
    }
    [Serializable] public sealed class PlayerActionNode
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "节点";
        public PlayerActionNodeKind Kind;
        [Min(0)] public int StartTick;
        [Min(1)] public int EndTick = 1;
        public int Order;
        public PlayerSpineSegment Spine = new();
        public PlayerMotionMode Motion;
        [Min(0)] public float SpeedScale = 1;
        public PlayerIntentMask AllowedIntents;
        public PlayerTransitionCondition[] Conditions = Array.Empty<PlayerTransitionCondition>();
        public bool AllowFire;
        public PlayerPointEventKind Event;
        public ProjectileDefinition Projectile;
        [Min(0)] public float ProjectileSpeed = 8;
        public AudioClip Audio;
        [Range(0, 1)] public float Volume = 1;
        public GameObject Effect;
        public Vector3 LocalOffset;
        public bool FollowPlayer = true;
        public bool IsPoint => Kind == PlayerActionNodeKind.PointEvent || Kind == PlayerActionNodeKind.Audio;
        public bool Contains(int tick) => !IsPoint && tick >= StartTick && tick < EndTick;
        public PlayerActionNode Copy()
        { var copy = JsonUtility.FromJson<PlayerActionNode>(JsonUtility.ToJson(this)); copy.Id = Guid.NewGuid().ToString("N"); return copy; }
    }
    [Serializable] public sealed class PlayerClipBinding
    { public PlayerClipSlot Slot; public PlayerActionClip Clip; }
    public readonly struct PlayerRuntimeEvent
    {
        public readonly PlayerRuntimeEventKind Kind;
        public readonly PlayerActionClip Clip;
        public readonly PlayerActionNode Node;
        public readonly int InstanceId, SkillSlot;
        public readonly PlayerIntentKind Intent;
        public PlayerRuntimeEvent(PlayerRuntimeEventKind kind, PlayerActionClip clip, int instance, PlayerActionNode node = null,
            PlayerIntentKind intent = default, int skillSlot = 0)
        { Kind = kind; Clip = clip; InstanceId = instance; Node = node; Intent = intent; SkillSlot = skillSlot; }
    }
}
