using UnityEngine;
namespace TapTap.Danmaku
{
    [CreateAssetMenu(menuName = "Danmaku/Battle Config")]
    public sealed class BattleConfig : ScriptableObject
    {
        public DanmakuSequence Sequence;
        public DanmakuPlayerConfig Player;
        public PlayerWeaponDefinition Weapon;
        [Min(0.001f)] public float PlayerHealth = 5;
        [Min(0.001f)] public float BossHealth = 1000;
        public Vector2 PlayerSpawn = new(0, -4), BossPosition = new(0, 3);
        [Min(0.001f)] public float BossHitRadius = 0.4f;
        [Min(0)] public int PlayerInvulnerableTicks = 30;
        public bool ConsumeBulletWhenInvulnerable = true;
        [Min(1)] public int MaxActiveProjectiles = 2048;
        public string Validate()
        {
            if (!Sequence || !Player || !Weapon || !Weapon.Projectile) return "需要指定 Sequence、Player、Weapon 和单弹模板。";
            if (Weapon.Mode == PlayerWeaponMode.Homing) return "跟踪武器属于后续阶段，当前战斗尚未实现跟踪。";
            if (!Positive(PlayerHealth) || !Positive(BossHealth) || !Positive(BossHitRadius) || !Positive(Weapon.FireRate)) return "生命、Boss 半径和射速必须是有限正数。";
            if (Weapon.FireRate > DanmakuSequence.TickRate) return "第一阶段射速不能超过每秒 60 次。";
            if (PlayerInvulnerableTicks < 0 || MaxActiveProjectiles < 1) return "无敌 Tick 不能为负，弹幕容量必须大于零。";
            if (!Positive(Sequence.FieldSize.x) || !Positive(Sequence.FieldSize.y) || !Positive(Player.HitRadius)) return "场地尺寸和玩家判定半径必须是有限正数。";
            return null;
        }
        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
