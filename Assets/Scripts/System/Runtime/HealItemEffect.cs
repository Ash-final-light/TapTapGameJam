using Framework.FixedPoint;
using UnityEngine;

namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Items/Heal Effect")]
    public sealed class HealItemEffect : ItemEffect
    {
        [Min(1)] public int Health = 2;
        public override string Validate() => Health <= 0 ? "回血数值必须大于零" : null;
        public override string UnavailableReason(ItemEffectContext context)
        {
            if (Validate() is string error) return error;
            if (context.Player == null || !context.Player.Alive) return "角色已倒下，无法回血";
            return context.Player.Health >= context.Player.MaxHealth ? "生命已满" : null;
        }
        public override bool Apply(ItemEffectContext context)
        {
            var player = context.Player;
            if (UnavailableReason(context) != null) return false;
            player.Health = FPMath.Min(player.MaxHealth, player.Health + FP64.FromInt(Health));
            return true;
        }
    }
}
