using System.Collections.Generic;
using Framework.FixedPoint;
using TapTap.Player;
using UnityEngine;
using TapTap.Systems;
namespace TapTap.Danmaku
{
    public sealed class BattleSimulation
    {
        public readonly ProjectileWorld World;
        public readonly BossSequenceRunner BossTimeline;
        public readonly PlayerRuntime PlayerSystem;
        public PlayerMotor PlayerMotor => PlayerSystem.Motor;
        public readonly PlayerWeaponController Weapon;
        public readonly ActorState Player, Boss;
        public readonly List<BattleEvent> Events = new();
        public readonly CombatComboState Combo = new();
        public readonly BuffManager Buffs = new();
        private readonly CombatResolver resolver;
        private readonly Dictionary<string, CompiledProjectile> actionProjectiles = new();
        public int Tick => World.Tick;
        public bool Finished => !Player.Alive || BossTimeline.Finished;
        public BattleSimulation(BattleConfig config, PlayerDefinition playerDefinition, Vector2 playerSpawn, IDamageConfigResolver damageConfigs = null, PlayerWeaponDefinition selectedWeapon = null)
        {
            string error = config.Validate(); if (error != null) throw new System.ArgumentException(error);
            if (!playerDefinition) throw new System.ArgumentException("请指定独立 PlayerDefinition。");
            string playerError = playerDefinition.Validate(); if (playerError != null) throw new System.ArgumentException(playerError);
            if (damageConfigs == null)
            {
                RequireExplicitDamage((selectedWeapon ? selectedWeapon : playerDefinition.Weapon).Projectile.Settings);
                foreach (var binding in playerDefinition.Actions.Bindings)
                    foreach (var node in binding.Clip.Nodes) if (node.Projectile) RequireExplicitDamage(node.Projectile.Settings);
                foreach (var clip in config.Sequence.Clips)
                    if (!clip.Muted)
                    {
                        RequireExplicitDamage(clip.EffectiveProjectileSettings);
                        if (clip.IsSplit && clip.EffectiveProjectileSettings.SplitChild)
                            RequireExplicitDamage(clip.EffectiveProjectileSettings.SplitChild.Settings);
                    }
            }
            World = new ProjectileWorld(config.Sequence, config.MaxActiveProjectiles);
            BossTimeline = new BossSequenceRunner(config.Sequence, World);
            PlayerSystem = new PlayerRuntime(playerDefinition, config.Sequence.FieldSize, playerSpawn);
            Player = PlayerSystem.Actor;
            var pos = DanmakuSimulation.ToFP(config.BossPosition);
            Boss = new ActorState(2, FP64.FromFloat(config.BossHealth), new DanmakuPlayerState(pos, pos, FP64.FromFloat(config.BossHitRadius)));
            World.SetPlayerState(Player.Pose); World.SetBossState(Boss.Pose);
            Weapon = new PlayerWeaponController(selectedWeapon ? selectedWeapon : playerDefinition.Weapon); resolver = new CombatResolver(config, damageConfigs);
            World.EnergyOrbMissed += _ => Combo.Reset();
            // Compile point-event projectile templates with the same lifetime as this battle.
            foreach (var binding in playerDefinition.Actions.Bindings)
                foreach (var node in binding.Clip.Nodes) if (node.Projectile) actionProjectiles[binding.Clip.GetInstanceID() + ":" + node.Id] = ProjectileWorld.CompileProjectile(node.Projectile.Settings);
        }
        public void Step(in BattleInput input)
            => Step(input, new PlayerTickInput(input.Movement.MoveX, fireHeld: input.Movement.FireHeld));
        public void Step(in BattleInput input, in PlayerTickInput playerInput)
        {
            Events.Clear(); if (Finished || World.Fault != null) return;
            Buffs.Advance(Tick);
            var modifiers = Buffs.Current;
            PlayerMotor.MoveFlat = modifiers.MoveFlat; PlayerMotor.MoveMultiplier = modifiers.MoveMultiplier;
            Weapon.Modifiers = modifiers;
            PlayerSystem.BeginTick(playerInput);
            World.SetPlayerState(Player.Pose); World.SetBossState(Boss.Pose);
            World.BeginTick(); BossTimeline.Step();
            var gated = new BattleInput(new Inputs.InputSnapshot(playerInput.Horizontal, 0, fireHeld: playerInput.FireHeld && PlayerSystem.FireAllowed), input.AimPosition, input.AimValid);
            Weapon.Step(gated, Player.Pose.Position, World);
            foreach (var e in PlayerSystem.Events)
                if (e.Kind == PlayerRuntimeEventKind.PointEvent && e.Node.Event != PlayerPointEventKind.JumpImpulse && e.Node.Projectile)
                {
                    var delta = input.AimPosition - Player.Pose.Position;
                    var angle = input.AimValid && delta.SqrMagnitude > FP64.Zero ? FPMath.Atan2(delta.Y, delta.X) : FPMath.PI_OVER_2;
                    if (actionProjectiles.TryGetValue(e.Clip.GetInstanceID() + ":" + e.Node.Id, out var projectile))
                        World.SpawnFriendly(projectile, Player.Pose.Position, angle, FPMath.Max(FP64.Zero, (FP64.FromFloat(e.Node.ProjectileSpeed) + modifiers.SpeedFlat) * modifiers.SpeedMultiplier), modifiers.DamageFlat, modifiers.DamageMultiplier);
                }
            World.Advance(); resolver.Resolve(World, PlayerSystem, Boss, Events, Combo);
            World.CommitLifecycle(); BossTimeline.AfterStep();
            PlayerSystem.EndTick();
        }
        private static void RequireExplicitDamage(ProjectileSettings settings)
        {
            if (settings.DamageSource == ProjectileDamageSource.ConfigId)
                throw new System.ArgumentException("单弹选择了 DamageConfigId，需要明确提供 IDamageConfigResolver。");
        }
    }
}
