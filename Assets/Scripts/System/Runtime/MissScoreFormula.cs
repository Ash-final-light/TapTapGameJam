using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Scoring/Miss Penalty")]
    public sealed class MissScoreFormula : ScoreFormula
    { [Min(0)] public int PenaltyPerMiss = 50; public override int Evaluate(BattleStats stats) => -ScoreManager.Multiply(stats.Misses, Mathf.Max(0, PenaltyPerMiss)); }
}
