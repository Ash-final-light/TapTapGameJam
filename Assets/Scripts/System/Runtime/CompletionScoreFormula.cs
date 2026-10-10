using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Scoring/Completion Bonus")]
    public sealed class CompletionScoreFormula : ScoreFormula
    { [Min(0)] public int Bonus = 1000; public override int Evaluate(BattleStats stats) => stats.Completed ? Bonus : 0; }
}
