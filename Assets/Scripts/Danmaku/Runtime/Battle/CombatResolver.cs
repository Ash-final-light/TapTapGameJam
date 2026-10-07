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
        public void Resolve(ProjectileWorld world, ActorState player, ActorState boss, List<BattleEvent> events)
        {
            foreach (var contact in world.Contacts)
            {
                if (!world.TryGet(contact.ProjectileId, out var p)) continue;
                var target = contact.TargetId == player.Id ? player : boss;
                if (!target.Alive || !target.Pose.CollisionEnabled) continue;
                bool immune = world.Tick <= target.InvulnerableUntilTick;
                if (immune)
                { if (config.ConsumeBulletWhenInvulnerable) world.ConsumeHit(p); continue; }
                FP64 damage;
                if (p.Settings.DamageSource == ProjectileDamageSource.ConfigId)
                {
                    if (damageConfigs == null) throw new InvalidOperationException("单弹选择了 DamageConfigId，但战斗没有指定 IDamageConfigResolver。");
                    damage = damageConfigs.Resolve(p.Settings.DamageConfigId);
                }
                else damage = FP64.FromFloat(p.Settings.Damage);
                damage = FPMath.Max(FP64.Zero, damage);
                target.Health = FPMath.Max(FP64.Zero, target.Health - damage);
                if (target.Id == player.Id) target.InvulnerableUntilTick = config.PlayerInvulnerableTicks > 0 ? world.Tick + config.PlayerInvulnerableTicks : 0;
                world.ConsumeHit(p);
                events.Add(new BattleEvent(BattleEventType.Damage, world.Tick, target, p, damage));
                if (!target.Alive) events.Add(new BattleEvent(BattleEventType.ActorDied, world.Tick, target, p, damage));
            }
        }
    }
}
