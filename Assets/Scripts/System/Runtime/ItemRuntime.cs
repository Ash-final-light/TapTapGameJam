using System;
using System.Collections.Generic;
namespace TapTap.Systems
{
    public sealed class ItemRuntime : IGameplayRequestSink
    {
        private sealed class Slot { public ItemDefinition Definition; public int Remaining, ReadyAt; }
        private readonly List<Slot> slots = new();
        private readonly BuffManager buffs;
        private readonly Func<int> tick;
        private readonly TapTap.Danmaku.ActorState player;
        public string SelectedId { get; private set; }
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
        public ItemViewData[] Snapshot() => slots.ConvertAll(s => new ItemViewData { Id = s.Definition.Id, Name = s.Definition.DisplayName,
            Icon = s.Definition.Icon, Count = s.Remaining, Usable = s.Remaining > 0 && tick() >= s.ReadyAt }).ToArray();
        public void RequestSelectItem(string itemId) { if (slots.Exists(s => s.Definition.Id == itemId && s.Remaining > 0)) SelectedId = itemId; }
        public void RequestUseItem(string itemId)
        {
            var slot = slots.Find(s => s.Definition.Id == itemId); int now = tick();
            if (slot == null || slot.Remaining <= 0 || now < slot.ReadyAt || !slot.Definition.Effect) return;
            if (!slot.Definition.Effect.Apply(new ItemEffectContext(buffs, now, itemId, player))) return;
            slot.Remaining--; slot.ReadyAt = (int)Math.Min(int.MaxValue, (long)now + Math.Max(0, slot.Definition.CooldownTicks));
            if (slot.Remaining == 0) SelectedId = slots.Find(s => s.Remaining > 0)?.Definition.Id;
        }
        public void RequestSkill(int index) { /* Skills remain owned by PlayerRuntime's accepted intents. */ }
    }
}
