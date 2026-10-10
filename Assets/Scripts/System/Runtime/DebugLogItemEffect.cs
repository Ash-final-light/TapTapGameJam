using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Items/Debug Log Effect")]
    public sealed class DebugLogItemEffect : ItemEffect
    { public string Message = "占位道具效果"; public override bool Apply(ItemEffectContext context) { Debug.Log($"[Spotlight Item] {context.ItemId}: {Message} (Tick {context.Tick})"); return true; } }
}
