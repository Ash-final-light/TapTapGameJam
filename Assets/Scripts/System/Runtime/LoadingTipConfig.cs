using System;
using UnityEngine;

namespace TapTap.Systems
{
    [Serializable] public sealed class LoadingTip { public string Id, Text; public int Weight = 1; public bool Enabled = true; }
    [CreateAssetMenu(menuName = "Spotlight/Loading Tips")]
    public sealed class LoadingTipConfig : ScriptableObject
    {
        public LoadingTip[] Tips = Array.Empty<LoadingTip>();
        public float MinimumSeconds = 1, TimeoutSeconds = 30;
        public string Pick(System.Random random)
        {
            int sum = 0; foreach (var tip in Tips) if (tip.Enabled && tip.Weight > 0) sum += Math.Min(10000, tip.Weight);
            if (sum == 0) return "正在准备舞台……";
            int choice = random.Next(sum);
            foreach (var tip in Tips) if (tip.Enabled && tip.Weight > 0)
            { choice -= Math.Min(10000, tip.Weight); if (choice < 0) return tip.Text; }
            return "正在准备舞台……";
        }
    }
}
