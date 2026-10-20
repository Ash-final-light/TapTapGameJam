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
        public readonly ActorState Player;
        public readonly FPVector2 BossPosition;
        public readonly int EndTick;
        public readonly List<BattleEvent> Events = new();
        public readonly CombatComboState Combo = new();
        public readonly BuffManager Buffs = new();
        public ItemRuntime Items { get; private set; }
        private readonly CombatResolver resolver;
        private readonly Dictionary<string, CompiledProjectile> actionProjectiles = new();
        public int Tick => World.Tick;
        public bool Finished => !Player.Alive || Tick >= EndTick;
        public RunEndReason EndReason => Player.Alive ? RunEndReason.Completed : RunEndReason.PlayerDied;
        public BattleSimulation(BattleConfig config, PlayerDefinition playerDefinition, Vector2 playerSpawn, PlayerWeaponDefinition selectedWeapon = null,
            BossScoringSettings bossScoring = null, Rect? bounds = null)
        {
            string error = config.Validate(); if (error != null) throw new System.ArgumentException(error);
            if (!playerDefinition) throw new System.ArgumentException("请指定独立 PlayerDefinition。");
            string playerError = playerDefinition.Validate(); if (playerError != null) throw new System.ArgumentException(playerError);
            // Music defines the run length; without music use the authored timeline.
            EndTick = config.Sequence.Music
                ? MusicTime.EndTick(config.Sequence.MusicStartSeconds + (double)config.Sequence.Music.samples / config.Sequence.Music.frequency)
                : System.Math.Max(1, config.Sequence.DurationTicks);
            World = new ProjectileWorld(config.Sequence, config.MaxActiveProjectiles, bounds);
            BossTimeline = new BossSequenceRunner(config.Sequence, World);
            PlayerSystem = new PlayerRuntime(playerDefinition, config.Sequence.FieldSize, playerSpawn, bounds: bounds);
            Player = PlayerSystem.Actor;
            BossPosition = DanmakuSimulation.ToFP(config.BossPosition);
            if (bossScoring?.Enabled == true)
            {
                string scoreError = bossScoring.Validate();
                if (scoreError != null) throw new System.ArgumentException(scoreError);
                World.SetBossTarget(BossPosition, FP64.FromFloat(bossScoring.HitRadius));
            }
            World.SetPlayerState(Player.Pose);
            Weapon = new PlayerWeaponController(selectedWeapon ? selectedWeapon : playerDefinition.Weapon); resolver = new CombatResolver(config);
            World.EnergyOrbMissed += _ => Combo.Reset();
            // Compile point-event projectile templates with the same lifetime as this battle.
            foreach (var binding in playerDefinition.Actions.Bindings)
                foreach (var node in binding.Clip.Nodes) if (node.Projectile) actionProjectiles[binding.Clip.GetInstanceID() + ":" + node.Id] = ProjectileWorld.CompileProjectile(node.Projectile.Settings);
        }
        public void SetBounds(Rect bounds)
        {
            PlayerMotor.SetBounds(bounds); Player.Pose = PlayerMotor.State;
            World.SetBounds(bounds); World.SetPlayerState(Player.Pose);
        }
        public void ConfigureItems(ItemDefinition[] equipment, System.Func<string, int> quantity = null)
            => Items = new ItemRuntime(equipment, Buffs, () => Tick, Player, quantity);
        public void Step(in BattleInput input)
            => Step(input, new PlayerTickInput(input.Movement.MoveX, fireHeld: input.Movement.FireHeld));
        public void Step(in BattleInput input, in PlayerTickInput playerInput)
        {
            Events.Clear(); if (Finished || World.Fault != null) return;
            // Healing precedes this tick's collisions; buffs affect this tick's movement and shots.
            if ((playerInput.Edges & PlayerButtonEdges.UseItem) != 0) Items?.RequestUseItem(playerInput.ItemId);
            Buffs.Advance(Tick);
            var modifiers = Buffs.Current;
            PlayerMotor.MoveFlat = modifiers.MoveFlat; PlayerMotor.MoveMultiplier = modifiers.MoveMultiplier;
            Weapon.Modifiers = modifiers;
            PlayerSystem.BeginTick(playerInput);
            World.SetPlayerState(Player.Pose);
            World.BeginTick(); BossTimeline.Step();
            var gated = new BattleInput(new Inputs.InputSnapshot(playerInput.Horizontal, 0, fireHeld: playerInput.FireHeld && PlayerSystem.FireAllowed), input.AimPosition, input.AimValid);
            Weapon.Step(gated, Player.Pose.Position, World);
            foreach (var e in PlayerSystem.Events)
                if (e.Kind == PlayerRuntimeEventKind.PointEvent && e.Node.Event != PlayerPointEventKind.JumpImpulse && e.Node.Projectile)
                {
                    var delta = input.AimPosition - Player.Pose.Position;
                    var angle = input.AimValid && delta.SqrMagnitude > FP64.Zero ? FPMath.Atan2(delta.Y, delta.X) : FPMath.PI_OVER_2;
                    if (actionProjectiles.TryGetValue(e.Clip.GetInstanceID() + ":" + e.Node.Id, out var projectile))
                        World.SpawnFriendly(projectile, Player.Pose.Position, angle, FPMath.Max(FP64.Zero, (FP64.FromFloat(e.Node.ProjectileSpeed) + modifiers.SpeedFlat) * modifiers.SpeedMultiplier));
                }
            World.Advance(); resolver.Resolve(World, PlayerSystem, Events, Combo);
            World.CommitLifecycle(); BossTimeline.AfterStep();
            PlayerSystem.EndTick();
        }
    }
}
