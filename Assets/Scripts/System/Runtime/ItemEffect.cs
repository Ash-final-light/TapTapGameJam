using UnityEngine;
namespace TapTap.Systems
{
    public interface IItemEffect { bool Apply(ItemEffectContext context); }
    public sealed class ItemEffectContext
    {
        public readonly BuffManager Buffs;
        public readonly int Tick;
        public readonly string ItemId;
        public readonly TapTap.Danmaku.ActorState Player;
        public ItemEffectContext(BuffManager buffs, int tick, string itemId, TapTap.Danmaku.ActorState player = null)
        { Buffs = buffs; Tick = tick; ItemId = itemId; Player = player; }
    }
    public abstract class ItemEffect : ScriptableObject, IItemEffect { public abstract bool Apply(ItemEffectContext context); }
}
