using UnityEngine;
namespace TapTap.Systems
{
    public interface IItemEffect { bool Apply(ItemEffectContext context); }
    public sealed class ItemEffectContext
    {
        public readonly BuffManager Buffs;
        public readonly int Tick;
        public readonly string ItemId;
        public ItemEffectContext(BuffManager buffs, int tick, string itemId) { Buffs = buffs; Tick = tick; ItemId = itemId; }
    }
    public abstract class ItemEffect : ScriptableObject, IItemEffect { public abstract bool Apply(ItemEffectContext context); }
}
