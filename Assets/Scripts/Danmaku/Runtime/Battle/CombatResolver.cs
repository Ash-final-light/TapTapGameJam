using System;
using System.Collections.Generic;
using Framework.FixedPoint;
namespace TapTap.Danmaku
{
    public sealed class CombatResolver
    {
        private readonly BattleConfig config;
        private readonly IDamageConfigResolver damageConfigs;
        public CombatResolver(BattleConfig config, IDamageConfigResolver damageConfigs = null)
        { this.config = config; this.damageConfigs = damageConfigs; }
        public void Resolve(ProjectileWorld world, ActorState player, ActorState boss, List<BattleEvent> events, CombatComboState combo)
        {
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
                bool immune = world.Tick <= target.InvulnerableUntilTick;
                if (immune)
                { if (config.ConsumeBulletWhenInvulnerable) world.ConsumeHit(p); continue; }
                FP64 damage = Damage(p);
                target.Health = FPMath.Max(FP64.Zero, target.Health - damage);
                if (target.Id == player.Id) target.InvulnerableUntilTick = config.PlayerInvulnerableTicks > 0 ? world.Tick + config.PlayerInvulnerableTicks : 0;
                world.ConsumeHit(p);
                events.Add(new BattleEvent(BattleEventType.Damage, world.Tick, target, p, damage, contact.TargetKind));
                if (!target.Alive) events.Add(new BattleEvent(BattleEventType.ActorDied, world.Tick, target, p, damage, contact.TargetKind));
            }
        }
        private FP64 Damage(ProjectileState p)
        {
            if (p.Settings.DamageSource != ProjectileDamageSource.ConfigId) return FPMath.Max(FP64.Zero, FP64.FromFloat(p.Settings.Damage));
            if (damageConfigs == null) throw new InvalidOperationException("单弹选择了 DamageConfigId，但战斗没有指定 IDamageConfigResolver。");
            return FPMath.Max(FP64.Zero, damageConfigs.Resolve(p.Settings.DamageConfigId));
        }
    }
}
