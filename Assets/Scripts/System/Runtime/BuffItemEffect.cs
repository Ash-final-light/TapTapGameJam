using System;
using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Items/Buff Effect")]
    public sealed class BuffItemEffect : ItemEffect
    {
        public string BuffId = "item-buff";
        [Tooltip("0 表示持续整局；持续时间使用战斗 Tick，暂停不消耗。")]
        [Min(0)] public int DurationTicks = 300;
        [Min(1)] public int MaxStacks = 1;
        public BuffModifier[] Modifiers = Array.Empty<BuffModifier>();
        public override bool Apply(ItemEffectContext context)
        { context.Buffs.Add(BuffId, Modifiers, context.Tick, DurationTicks, MaxStacks); return true; }
    }
}
