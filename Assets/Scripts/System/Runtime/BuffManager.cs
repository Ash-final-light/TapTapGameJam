using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using UnityEngine;
namespace TapTap.Systems
{
    public enum BuffStat { MoveSpeed, FireRate, ProjectileSpeed, ProjectileCount }
    [Serializable] public sealed class BuffModifier
    {
        public BuffStat Stat;
        public float Flat;
        [Min(0)] public float Multiplier = 1;
    }
    public readonly struct BattleStatModifiers
    {
        public readonly FP64 MoveFlat, MoveMultiplier, FireFlat, FireMultiplier, SpeedFlat, SpeedMultiplier;
        public readonly FP64 CountFlat, CountMultiplier;
        public BattleStatModifiers(FP64[] flat, FP64[] multiplier)
        { MoveFlat = flat[0]; MoveMultiplier = multiplier[0]; FireFlat = flat[1]; FireMultiplier = multiplier[1]; SpeedFlat = flat[2]; SpeedMultiplier = multiplier[2]; CountFlat = flat[3]; CountMultiplier = multiplier[3]; }
    }
    public sealed class BuffManager
    {
        private sealed class Entry { public string Id; public BuffModifier[] Modifiers; public int UntilTick; }
        private readonly List<Entry> entries = new();
        public BattleStatModifiers Current { get; private set; }
        public BuffManager() { Rebuild(); }
        public void Add(string id, BuffModifier[] modifiers, int tick, int durationTicks, int maxStacks = 1)
        {
            if (string.IsNullOrEmpty(id) || modifiers == null) return;
            var copy = new BuffModifier[modifiers.Length];
            for (int i = 0; i < modifiers.Length; i++)
            {
                var m = modifiers[i];
                if (m == null || !Enum.IsDefined(typeof(BuffStat), m.Stat) || float.IsNaN(m.Flat) || float.IsInfinity(m.Flat) || float.IsNaN(m.Multiplier) || float.IsInfinity(m.Multiplier) || m.Multiplier < 0) throw new ArgumentException("Buff 参数无效。");
                copy[i] = new BuffModifier { Stat = m.Stat, Flat = m.Flat, Multiplier = m.Multiplier };
            }
            int count = entries.FindAll(e => e.Id == id).Count;
            if (count >= Math.Max(1, maxStacks)) entries.Remove(entries.Find(e => e.Id == id));
            entries.Add(new Entry { Id = id, Modifiers = copy, UntilTick = durationTicks <= 0 ? int.MaxValue : (int)Math.Min(int.MaxValue, (long)tick + durationTicks) }); Rebuild();
        }
        public void Remove(string id) { if (entries.RemoveAll(e => e.Id == id) > 0) Rebuild(); }
        public int RemainingTicks(string id, int tick)
        {
            int remaining = 0;
            foreach (var entry in entries) if (entry.Id == id) remaining = Math.Max(remaining, Math.Max(0, entry.UntilTick - tick));
            return remaining;
        }
        public void Advance(int tick) { if (entries.RemoveAll(e => tick >= e.UntilTick) > 0) Rebuild(); }
        private void Rebuild()
        {
            var flat = new FP64[4]; var multiplier = new[] { FP64.One, FP64.One, FP64.One, FP64.One };
            foreach (var entry in entries) foreach (var m in entry.Modifiers) { flat[(int)m.Stat] += FP64.FromFloat(m.Flat); multiplier[(int)m.Stat] *= FP64.FromFloat(m.Multiplier); }
            Current = new BattleStatModifiers(flat, multiplier);
        }
    }
}
