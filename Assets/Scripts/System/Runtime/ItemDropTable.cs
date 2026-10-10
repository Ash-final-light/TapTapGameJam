using System;
using UnityEngine;
namespace TapTap.Systems
{
    [Serializable] public sealed class ItemDropEntry { public ItemDefinition Item; [Min(1)] public int Weight = 1, Quantity = 1; }
    [CreateAssetMenu(menuName = "Spotlight/Item Drop Table")]
    public sealed class ItemDropTable : ScriptableObject
    {
        public ItemDropEntry[] Entries = Array.Empty<ItemDropEntry>();
        public string Validate()
        {
            if (Entries == null || Entries.Length == 0) return "掉落表不能为空，每次结算必须掉落一个道具。";
            foreach (var e in Entries) if (e == null || !e.Item || string.IsNullOrWhiteSpace(e.Item.Id) || e.Weight <= 0 || e.Quantity <= 0) return "掉落道具、权重或数量不合法。";
            return null;
        }
        public ItemDropEntry Pick(System.Random random)
        {
            string error = Validate(); if (error != null) throw new InvalidOperationException(error);
            long total = 0; foreach (var e in Entries) total += e.Weight;
            double value = random.NextDouble() * total;
            foreach (var e in Entries) { value -= e.Weight; if (value < 0) return e; }
            return Entries[Entries.Length - 1];
        }
    }
}
