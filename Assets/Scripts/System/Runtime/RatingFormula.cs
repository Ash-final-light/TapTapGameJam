using System;
using UnityEngine;
namespace TapTap.Systems
{
    [Serializable] public sealed class RatingValue { public string Name = "C"; public int MinimumScore; public Color Color = Color.white; }
    public abstract class RatingFormula : ScriptableObject { public abstract RatingValue Evaluate(int score, BattleStats stats); }
}
