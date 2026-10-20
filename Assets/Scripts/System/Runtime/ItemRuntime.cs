using System;
using System.Collections.Generic;
namespace TapTap.Systems
{
    public readonly struct ItemUseResult
    {
        public readonly string ItemId, Message;
        public readonly bool Success;
        public ItemUseResult(string itemId, bool success, string message)
        { ItemId = itemId; Success = success; Message = message; }
    }
    public sealed class ItemRuntime : IGameplayRequestSink
    {
        private sealed class Slot { public ItemDefinition Definition; public int Remaining, ReadyAt; }
        private readonly List<Slot> slots = new();
        private readonly BuffManager buffs;
        private readonly Func<int> tick;
        private readonly TapTap.Danmaku.ActorState player;
        public string SelectedId { get; private set; }
        public event Action<ItemUseResult> UseResolved;
        public ItemRuntime(ItemDefinition[] equipment, BuffManager buffs, Func<int> tick,
            TapTap.Danmaku.ActorState player = null, Func<string, int> quantity = null)
        {
            this.buffs = buffs; this.tick = tick; this.player = player;
            foreach (var item in equipment ?? Array.Empty<ItemDefinition>())
                if (item && item.Effect)
                {
                    if (item.Passive) item.Effect.Apply(new ItemEffectContext(buffs, 0, item.Id, player));
                    else
                    {
                        int available = Math.Min(Math.Max(0, item.UsesPerRun), Math.Max(0, item.MaxHeldPerRun));
                        if (quantity != null) available = Math.Min(available, Math.Max(0, quantity(item.Id)));
                        slots.Add(new Slot { Definition = item, Remaining = available });
                        if (available > 0) SelectedId ??= item.Id;
                    }
                }
        }
        public ItemViewData[] Snapshot()
        {
            int now = tick();
            return slots.ConvertAll(s => {
                string reason = UnavailableReason(s, now);
                var context = new ItemEffectContext(buffs, now, s.Definition.Id, player);
                return new ItemViewData { Id = s.Definition.Id, Name = s.Definition.DisplayName,
                    Icon = s.Definition.Icon, IconColor = s.Definition.IconColor, Count = s.Remaining, Usable = reason == null, UnavailableReason = reason,
                    CooldownTicks = Math.Max(0, s.Definition.CooldownTicks), CooldownRemainingTicks = Math.Max(0, s.ReadyAt - now),
                    EffectRemainingTicks = s.Definition.Effect ? s.Definition.Effect.RemainingTicks(context) : 0 };
            }).ToArray();
        }
        public void RequestSelectItem(string itemId) { if (slots.Exists(s => s.Definition.Id == itemId && s.Remaining > 0)) SelectedId = itemId; }
        private string UnavailableReason(Slot slot, int now)
        {
            if (slot == null) return "未装备可用道具";
            if (player != null && !player.Alive) return "角色已倒下，无法使用道具";
            if (slot.Remaining <= 0) return "本局次数已用完";
            if (now < slot.ReadyAt) return $"冷却中 {Seconds(slot.ReadyAt - now)}秒";
            if (!slot.Definition.Effect) return "道具未配置效果";
            return slot.Definition.Effect.UnavailableReason(new ItemEffectContext(buffs, now, slot.Definition.Id, player));
        }
        public static string Seconds(int ticks) => (Math.Ceiling(Math.Max(0, ticks) * 10d / TapTap.Danmaku.DanmakuSequence.TickRate) / 10d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        public void RequestUseItem(string itemId) => TryUseItem(itemId);
        public ItemUseResult TryUseItem(string itemId)
        {
            var slot = slots.Find(s => s.Definition.Id == itemId); int now = tick();
            string reason = UnavailableReason(slot, now);
            if (reason != null) return Report(new ItemUseResult(itemId, false, reason));
            if (!slot.Definition.Effect.Apply(new ItemEffectContext(buffs, now, itemId, player)))
                return Report(new ItemUseResult(itemId, false, "当前无法生效，未消耗次数"));
            slot.Remaining--; slot.ReadyAt = (int)Math.Min(int.MaxValue, (long)now + Math.Max(0, slot.Definition.CooldownTicks));
            if (slot.Remaining == 0) SelectedId = slots.Find(s => s.Remaining > 0)?.Definition.Id;
            return Report(new ItemUseResult(itemId, true, $"已使用{slot.Definition.DisplayName}，本局剩余 {slot.Remaining} 次"));
        }
        private ItemUseResult Report(ItemUseResult result) { UseResolved?.Invoke(result); return result; }
        public void RequestSkill(int index) { /* Skills remain owned by PlayerRuntime's accepted intents. */ }
    }
}
