using System;
using Framework.FixedPoint;
using Inputs;
namespace TapTap.Danmaku
{
    public enum ProjectileFaction { Enemy, Player }
    public enum BattleEventType { Damage, ActorDied, EnergyOrbDestroyed, ComboChanged }
    public enum CombatTargetKind { Player, Boss, EnergyOrb }
    public readonly struct CombatContact
    {
        public readonly int ProjectileId, TargetId;
        public readonly CombatTargetKind TargetKind;
        public readonly FP64 Time;
        public CombatContact(int projectileId, int targetId, CombatTargetKind targetKind, FP64 time)
        { ProjectileId = projectileId; TargetId = targetId; TargetKind = targetKind; Time = time; }
        public static readonly Comparison<CombatContact> Compare = (a, b) =>
        {
            int c = a.Time.CompareTo(b.Time); if (c != 0) return c;
            c = a.ProjectileId.CompareTo(b.ProjectileId); if (c != 0) return c;
            c = a.TargetKind.CompareTo(b.TargetKind); return c != 0 ? c : a.TargetId.CompareTo(b.TargetId);
        };
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
        public readonly CombatTargetKind TargetKind;
        public readonly int ComboCount;
        public readonly FPVector2 Position;
        public BattleEvent(BattleEventType type, int tick, ActorState target, ProjectileState p, FP64 damage, CombatTargetKind targetKind)
        { Type = type; Tick = tick; TargetId = target.Id; ProjectileId = p.Id; OwnerId = p.OwnerId;
            Damage = damage; RemainingHealth = target.Health; DamageConfigId = p.Settings.DamageConfigId;
            TargetKind = targetKind;
            ComboCount = 0; Position = target.Pose.Position; }
        public BattleEvent(BattleEventType type, int tick, ProjectileState target, ProjectileState source, FP64 damage, int comboCount)
        {
            Type = type; Tick = tick; TargetId = target.Id; ProjectileId = source.Id; OwnerId = source.OwnerId;
            Damage = damage; RemainingHealth = target.Health; DamageConfigId = source.Settings.DamageConfigId;
            TargetKind = CombatTargetKind.EnergyOrb; ComboCount = comboCount; Position = target.Position;
        }
    }
    /// <summary>本阶段仅累计；无超时、受伤清零或奖励规则。新战斗创建新状态。</summary>
    public sealed class CombatComboState
    {
        public int Count { get; private set; }
        internal int Increment() { if (Count < int.MaxValue) Count++; return Count; }
    }
    public interface IDamageConfigResolver { FP64 Resolve(int damageConfigId); }
}
