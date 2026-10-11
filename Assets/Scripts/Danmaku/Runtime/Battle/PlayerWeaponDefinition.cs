using UnityEngine;
namespace TapTap.Danmaku
{
    public enum PlayerWeaponMode { Normal, Spread, Homing }
    [CreateAssetMenu(menuName = "弹幕/玩家武器")]
    public sealed class PlayerWeaponDefinition : ScriptableObject
    {
        public string DisplayName;
        [TextArea] public string Description;
        public bool Unlocked = true;
        public string Label => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Mode switch { PlayerWeaponMode.Normal => "普通弹幕", PlayerWeaponMode.Spread => "散射弹幕", _ => "跟踪弹幕" };
        public PlayerWeaponMode Mode;
        [Min(0.001f)] public float FireRate = 5;
        public Vector2 MuzzleOffset = new(0, 0.2f);
        public EmissionSettings Emission = new();
        public ProjectileDefinition Projectile;
    }
}
