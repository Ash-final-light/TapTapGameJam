using Framework.FixedPoint;
namespace TapTap.Danmaku
{
    public sealed class PlayerWeaponController
    {
        private readonly CompiledProjectile projectile;
        private readonly FP64 baseFireRate, speed, arc;
        private FP64 interval;
        public TapTap.Systems.BattleStatModifiers Modifiers { get; set; }
        private readonly FPVector2 muzzle;
        private readonly int count;
        private FP64 remaining, direction = FPMath.PI_OVER_2;
        private bool held;
        public int TotalRounds { get; private set; }
        public PlayerWeaponController(PlayerWeaponDefinition weapon)
        {
            projectile = ProjectileWorld.CompileProjectile(weapon.Projectile.Settings);
            baseFireRate = FP64.FromFloat(weapon.FireRate);
            interval = FP64.FromInt(DanmakuSequence.TickRate) / baseFireRate;
            Modifiers = new TapTap.Systems.BuffManager().Current;
            speed = FP64.FromFloat(weapon.Emission.Speed); arc = FP64.FromFloat(weapon.Emission.ArcDegrees) * FPMath.Deg2Rad;
            count = weapon.Mode == PlayerWeaponMode.Spread ? System.Math.Max(1, System.Math.Min(64, weapon.Emission.Count)) : 1;
            muzzle = DanmakuSimulation.ToFP(weapon.MuzzleOffset);
        }
        public void Step(in BattleInput input, FPVector2 position, ProjectileWorld world)
        {
            var rate = FPMath.Clamp((baseFireRate + Modifiers.FireFlat) * Modifiers.FireMultiplier, FP64.FromFloat(0.001f), FP64.FromInt(60));
            interval = FP64.FromInt(DanmakuSequence.TickRate) / rate;
            FPVector2 origin = position + muzzle;
            var delta = input.AimPosition - origin;
            if (input.AimValid && delta.SqrMagnitude > FP64.FromFloat(0.000001f)) direction = FPMath.Atan2(delta.Y, delta.X);
            if (!input.Movement.FireHeld) { held = false; remaining = FP64.Zero; return; }
            if (!held) remaining = FP64.Zero;
            held = true;
            while (remaining <= FP64.Zero && world.Fault == null)
            {
                for (int i = 0; i < count; i++)
                {
                    var angle = direction + (count > 1 ? arc * (FP64.FromFraction(i, count - 1) - FP64.Half) : FP64.Zero);
                    world.SpawnFriendly(projectile, origin, angle, FPMath.Max(FP64.Zero, (speed + Modifiers.SpeedFlat) * Modifiers.SpeedMultiplier), Modifiers.DamageFlat, Modifiers.DamageMultiplier);
                }
                TotalRounds++; remaining += interval;
            }
            remaining -= FP64.One;
        }
    }
}
