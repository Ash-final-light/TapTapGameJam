using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using TapTap.Player;
namespace TapTap.Danmaku
{
    public sealed class CombatResolver
    {
        private readonly BattleConfig config;
        private readonly IDamageConfigResolver damageConfigs;
        public CombatResolver(BattleConfig config, IDamageConfigResolver damageConfigs = null)
        { this.config = config; this.damageConfigs = damageConfigs; }
        public void Resolve(ProjectileWorld world, PlayerRuntime playerSystem, ActorState boss, List<BattleEvent> events, CombatComboState combo)
        {
            var player = playerSystem.Actor;
            foreach (var contact in world.Contacts)
            {
                if (!world.TryGet(contact.ProjectileId, out var p)) continue;
                if (contact.TargetKind == CombatTargetKind.EnergyOrb)
                {
                    if (p.Faction != ProjectileFaction.Player || !world.TryGet(contact.TargetId, out var orb) || !orb.IsEnergyOrb) continue;
                    FP64 orbDamage = Damage(p);
                    bool destroyed = world.DamageEnergyOrb(orb, orbDamage);
                    world.ConsumeHit(p);
                    if (destroyed)
                    {
                        int count = combo.Increment();
                        events.Add(new BattleEvent(BattleEventType.EnergyOrbDestroyed, world.Tick, orb, p, orbDamage, count));
                        events.Add(new BattleEvent(BattleEventType.ComboChanged, world.Tick, orb, p, orbDamage, count));
                    }
                    continue;
                }
                ActorState target;
                if (contact.TargetKind == CombatTargetKind.Player && contact.TargetId == player.Id) target = player;
                else if (contact.TargetKind == CombatTargetKind.Boss && contact.TargetId == boss.Id) target = boss;
                else continue;
                if (!target.Alive || !target.Pose.CollisionEnabled) continue;
                bool immune = target.Id == player.Id ? playerSystem.Invulnerable : world.Tick <= target.InvulnerableUntilTick;
                if (immune)
                { if (config.ConsumeBulletWhenInvulnerable) world.ConsumeHit(p); continue; }
                FP64 damage = Damage(p);
                if (target.Id == player.Id) damage = playerSystem.ReceiveDamage(damage);
                else target.Health = FPMath.Max(FP64.Zero, target.Health - damage);
                world.ConsumeHit(p);
                events.Add(new BattleEvent(BattleEventType.Damage, world.Tick, target, p, damage, contact.TargetKind));
                if (!target.Alive) events.Add(new BattleEvent(BattleEventType.ActorDied, world.Tick, target, p, damage, contact.TargetKind));
            }
        }
        private FP64 Damage(ProjectileState p)
        {
            FP64 baseDamage;
            if (p.Settings.DamageSource != ProjectileDamageSource.ConfigId) baseDamage = FP64.FromFloat(p.Settings.Damage);
            else
            {
                if (damageConfigs == null) throw new InvalidOperationException("单弹选择了 DamageConfigId，但战斗没有指定 IDamageConfigResolver。");
                baseDamage = damageConfigs.Resolve(p.Settings.DamageConfigId);
            }
            return FPMath.Max(FP64.Zero, (baseDamage + p.DamageFlat) * p.DamageMultiplier);
        }
    }
}
