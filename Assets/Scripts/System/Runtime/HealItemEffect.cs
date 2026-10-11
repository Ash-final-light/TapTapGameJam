using Framework.FixedPoint;
using UnityEngine;

namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Items/Heal Effect")]
    public sealed class HealItemEffect : ItemEffect
    {
        [Min(1)] public int Health = 2;
        public override bool Apply(ItemEffectContext context)
        {
            var player = context.Player;
            if (player == null || !player.Alive || player.Health >= player.MaxHealth || Health <= 0) return false;
            player.Health = FPMath.Min(player.MaxHealth, player.Health + FP64.FromInt(Health));
            return true;
        }
    }
}
