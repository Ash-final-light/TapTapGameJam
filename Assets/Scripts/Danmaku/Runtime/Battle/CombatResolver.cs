using System.Collections.Generic;
using Framework.FixedPoint;
using TapTap.Player;
namespace TapTap.Danmaku
{
    public sealed class CombatResolver
    {
        private readonly BattleConfig config;
        public CombatResolver(BattleConfig config) => this.config = config;
        public void Resolve(ProjectileWorld world, PlayerRuntime playerSystem, List<BattleEvent> events, CombatComboState combo)
        {
            var player = playerSystem.Actor;
            foreach (var contact in world.Contacts)
            {
                if (!world.TryGet(contact.ProjectileId, out var p)) continue;
                if (contact.TargetKind == CombatTargetKind.Boss)
                {
                    if (p.Faction != ProjectileFaction.Player) continue;
                    world.ConsumeHit(p);
                    events.Add(new BattleEvent(world.Tick, p, world.BossPosition));
                    continue;
                }
                if (contact.TargetKind == CombatTargetKind.EnergyOrb)
                {
                    if (p.Faction != ProjectileFaction.Player || !world.TryGet(contact.TargetId, out var orb) || !orb.IsEnergyOrb) continue;
                    FP64 orbDamage = FP64.One;
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
                if (contact.TargetKind != CombatTargetKind.Player || contact.TargetId != player.Id ||
                    p.Faction != ProjectileFaction.Enemy || !player.Alive || !player.Pose.CollisionEnabled) continue;
                if (playerSystem.Invulnerable)
                { if (config.ConsumeBulletWhenInvulnerable) world.ConsumeHit(p); continue; }
                FP64 damage = playerSystem.ReceiveDamage(FP64.One);
                world.ConsumeHit(p);
                events.Add(new BattleEvent(BattleEventType.Damage, world.Tick, player, p, damage, contact.TargetKind));
                if (!player.Alive) events.Add(new BattleEvent(BattleEventType.ActorDied, world.Tick, player, p, damage, contact.TargetKind));
            }
        }
    }
}
