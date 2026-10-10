using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Scoring/Orb Score")]
    public sealed class OrbScoreFormula : ScoreFormula
    { [Min(0)] public int PointsPerOrb = 100; public override int Evaluate(BattleStats stats) => ScoreManager.Multiply(stats.DestroyedOrbs, PointsPerOrb); }
}
