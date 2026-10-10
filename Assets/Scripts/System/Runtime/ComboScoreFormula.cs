using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Scoring/Max Combo Bonus")]
    public sealed class ComboScoreFormula : ScoreFormula
    { [Min(0)] public int PointsPerCombo = 10; public override int Evaluate(BattleStats stats) => ScoreManager.Multiply(stats.MaxCombo, PointsPerCombo); }
}
