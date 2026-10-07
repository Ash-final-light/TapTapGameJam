using System;
using Framework.FixedPoint;
using Inputs;
namespace TapTap.Danmaku
{
    public enum ProjectileFaction { Enemy, Player }
    public enum BattleEventType { Damage, ActorDied }
    public readonly struct CombatContact
    {
        public readonly int ProjectileId, TargetId;
        public readonly FP64 Time;
        public CombatContact(int projectileId, int targetId, FP64 time)
        { ProjectileId = projectileId; TargetId = targetId; Time = time; }
        public static readonly Comparison<CombatContact> Compare = (a, b) =>
        { int c = a.Time.CompareTo(b.Time); return c != 0 ? c : a.ProjectileId.CompareTo(b.ProjectileId); };
    }
    public readonly struct BattleInput
    {
        public readonly InputSnapshot Movement;
        public readonly FPVector2 AimPosition;
        public readonly bool AimValid;
        public BattleInput(InputSnapshot movement, FPVector2 aimPosition, bool aimValid)
        { Movement = movement; AimPosition = aimPosition; AimValid = aimValid; }
    }
    public sealed class ActorState
    {
        public readonly int Id;
        public readonly FP64 MaxHealth;
        public FP64 Health { get; internal set; }
        public DanmakuPlayerState Pose { get; internal set; }
        public int InvulnerableUntilTick { get; internal set; }
        public bool Alive => Health > FP64.Zero;
        public ActorState(int id, FP64 health, DanmakuPlayerState pose)
        { Id = id; MaxHealth = Health = health; Pose = pose; }
    }
    public readonly struct BattleEvent
    {
        public readonly BattleEventType Type;
        public readonly int Tick, TargetId, ProjectileId, OwnerId;
        public readonly FP64 Damage, RemainingHealth;
        public readonly int DamageConfigId;
        public BattleEvent(BattleEventType type, int tick, ActorState target, ProjectileState p, FP64 damage)
        { Type = type; Tick = tick; TargetId = target.Id; ProjectileId = p.Id; OwnerId = p.OwnerId;
            Damage = damage; RemainingHealth = target.Health; DamageConfigId = p.Settings.DamageConfigId; }
    }
    public interface IDamageConfigResolver { FP64 Resolve(int damageConfigId); }
}
