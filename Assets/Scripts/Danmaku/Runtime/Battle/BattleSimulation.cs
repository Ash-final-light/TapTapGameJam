using System.Collections.Generic;
using Framework.FixedPoint;
namespace TapTap.Danmaku
{
    public sealed class BattleSimulation
    {
        public readonly ProjectileWorld World;
        public readonly BossSequenceRunner BossTimeline;
        public readonly DanmakuPlayerMotor PlayerMotor;
        public readonly PlayerWeaponController Weapon;
        public readonly ActorState Player, Boss;
        public readonly List<BattleEvent> Events = new();
        private readonly CombatResolver resolver;
        public int Tick => World.Tick;
        public bool Finished => !Player.Alive || !Boss.Alive;
        public BattleSimulation(BattleConfig config, IDamageConfigResolver damageConfigs = null)
        {
            string error = config.Validate(); if (error != null) throw new System.ArgumentException(error);
            if (damageConfigs == null)
            {
                RequireExplicitDamage(config.Weapon.Projectile.Settings, 0);
                foreach (var clip in config.Sequence.Clips) if (!clip.Muted) RequireExplicitDamage(clip.EffectiveProjectileSettings, 0);
            }
            World = new ProjectileWorld(config.Sequence, config.MaxActiveProjectiles);
            BossTimeline = new BossSequenceRunner(config.Sequence, World);
            PlayerMotor = new DanmakuPlayerMotor(config.Player, config.Sequence.FieldSize, config.PlayerSpawn);
            Player = new ActorState(1, FP64.FromFloat(config.PlayerHealth), PlayerMotor.State);
            var pos = DanmakuSimulation.ToFP(config.BossPosition);
            Boss = new ActorState(2, FP64.FromFloat(config.BossHealth), new DanmakuPlayerState(pos, pos, FP64.FromFloat(config.BossHitRadius)));
            World.SetPlayerState(Player.Pose); World.SetBossState(Boss.Pose);
            Weapon = new PlayerWeaponController(config.Weapon); resolver = new CombatResolver(config, damageConfigs);
        }
        public void Step(in BattleInput input)
        {
            Events.Clear(); if (Finished || World.Fault != null) return;
            PlayerMotor.Step(input.Movement); Player.Pose = PlayerMotor.State;
            World.SetPlayerState(Player.Pose); World.SetBossState(Boss.Pose);
            World.BeginTick(); BossTimeline.Step(); Weapon.Step(input, Player.Pose.Position, World);
            World.Advance(); resolver.Resolve(World, Player, Boss, Events);
            World.CommitLifecycle(); BossTimeline.AfterStep();
        }
        private static void RequireExplicitDamage(ProjectileSettings settings, int depth)
        {
            if (settings.DamageSource == ProjectileDamageSource.ConfigId)
                throw new System.ArgumentException("单弹选择了 DamageConfigId，需要明确提供 IDamageConfigResolver。");
            if (settings.SplitAgeTicks > 0 && settings.SplitChild && depth < 4)
                RequireExplicitDamage(settings.SplitChild.Settings, depth + 1);
        }
    }
}
