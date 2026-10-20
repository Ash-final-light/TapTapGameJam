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
        public override string Validate()
        {
            if (string.IsNullOrWhiteSpace(BuffId) || Modifiers == null || Modifiers.Length == 0 || DurationTicks < 0 || MaxStacks < 1)
                return "增益效果配置无效";
            bool changed = false;
            foreach (var modifier in Modifiers)
            {
                if (modifier == null || !Enum.IsDefined(typeof(BuffStat), modifier.Stat) ||
                    float.IsNaN(modifier.Flat) || float.IsInfinity(modifier.Flat) ||
                    float.IsNaN(modifier.Multiplier) || float.IsInfinity(modifier.Multiplier) || modifier.Multiplier < 0)
                    return "增益效果配置无效";
                changed |= modifier.Flat != 0 || modifier.Multiplier != 1;
            }
            return changed ? null : "增益效果未配置数值变化";
        }
        public override string UnavailableReason(ItemEffectContext context)
            => Validate() ?? (context.Buffs == null ? "增益系统不可用" : null);
        public override int RemainingTicks(ItemEffectContext context) => context.Buffs?.RemainingTicks(BuffId, context.Tick) ?? 0;
        public override bool Apply(ItemEffectContext context)
        {
            if (UnavailableReason(context) != null) return false;
            context.Buffs.Add(BuffId, Modifiers, context.Tick, DurationTicks, MaxStacks); return true;
        }
    }
}
