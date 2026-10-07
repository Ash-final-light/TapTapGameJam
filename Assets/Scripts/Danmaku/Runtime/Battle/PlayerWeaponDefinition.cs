using UnityEngine;
namespace TapTap.Danmaku
{
    public enum PlayerWeaponMode { Normal, Spread, Homing }
    [CreateAssetMenu(menuName = "Danmaku/Player Weapon")]
    public sealed class PlayerWeaponDefinition : ScriptableObject
    {
        public PlayerWeaponMode Mode;
        [Min(0.001f)] public float FireRate = 5;
        public Vector2 MuzzleOffset = new(0, 0.2f);
        public EmissionSettings Emission = new();
        public ProjectileDefinition Projectile;
    }
}
