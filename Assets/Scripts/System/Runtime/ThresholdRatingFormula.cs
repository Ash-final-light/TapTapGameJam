using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Scoring/Threshold Rating")]
    public sealed class ThresholdRatingFormula : RatingFormula
    {
        public RatingValue[] Thresholds = { new() { Name = "C", MinimumScore = 0 }, new() { Name = "B", MinimumScore = 2000 },
            new() { Name = "A", MinimumScore = 5000, Color = Color.cyan }, new() { Name = "S", MinimumScore = 10000, Color = Color.yellow }, new() { Name = "SS", MinimumScore = 20000, Color = Color.magenta } };
        public override RatingValue Evaluate(int score, BattleStats stats)
        {
            RatingValue best = null;
            foreach (var threshold in Thresholds) if (threshold != null && score >= threshold.MinimumScore && (best == null || threshold.MinimumScore > best.MinimumScore)) best = threshold;
            return best ?? new RatingValue();
        }
    }
}
