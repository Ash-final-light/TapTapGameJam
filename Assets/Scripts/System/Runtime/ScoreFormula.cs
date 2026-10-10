using UnityEngine;
namespace TapTap.Systems
{
    public abstract class ScoreFormula : ScriptableObject { public abstract int Evaluate(BattleStats stats); }
}
