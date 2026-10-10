using System;
using System.Collections.Generic;
using TapTap.Danmaku;
namespace TapTap.Systems
{
    public sealed class BattleStats
    {
        public int DestroyedOrbs { get; private set; }
        public int Misses { get; private set; }
        public int CurrentCombo { get; private set; }
        public int MaxCombo { get; private set; }
        public int DamageTakenCount { get; private set; }
        public float DamageTaken { get; private set; }
        public int ElapsedTicks { get; private set; }
        public RunEndReason EndReason { get; private set; }
        public bool Completed => EndReason == RunEndReason.Completed;
        internal void OrbDestroyed() { DestroyedOrbs++; CurrentCombo++; MaxCombo = Math.Max(MaxCombo, CurrentCombo); }
        internal void Miss() { Misses++; CurrentCombo = 0; }
        internal void Damage(float amount) { if (amount > 0) { DamageTakenCount++; DamageTaken += amount; } }
        internal BattleStats Finish(int ticks, RunEndReason reason) { var result = (BattleStats)MemberwiseClone(); result.ElapsedTicks = ticks; result.EndReason = reason; return result; }
    }
    public sealed class ScoreBreakdown
    {
        public readonly string Name;
        public readonly int Score;
        public ScoreBreakdown(string name, int score) { Name = name; Score = score; }
    }
    public sealed class ScoreManager
    {
        private readonly ScoreFormula[] formulas;
        private readonly BattleStats stats = new();
        private RunResult result;
        public ScoreManager(ScoreFormula[] formulas) { this.formulas = formulas == null ? Array.Empty<ScoreFormula>() : (ScoreFormula[])formulas.Clone(); }
        public void Consume(BattleEvent e)
        {
            if (result != null) return;
            if (e.Type == BattleEventType.EnergyOrbDestroyed) stats.OrbDestroyed();
            if (e.Type == BattleEventType.Damage && e.TargetKind == CombatTargetKind.Player) stats.Damage(e.Damage.ToFloat());
        }
        public void MissOrb() { if (result == null) stats.Miss(); }
        public RunResult Finish(string runId, BattleStartData setup, int ticks, RunEndReason reason)
        {
            if (result != null) return result;
            var snapshot = stats.Finish(ticks, reason);
            var breakdown = new List<ScoreBreakdown>(); long total = 0;
            foreach (var formula in formulas)
                if (formula) { int score = formula.Evaluate(snapshot); total += score; breakdown.Add(new ScoreBreakdown(formula.name, score)); }
            int finalScore = (int)Math.Max(0, Math.Min(int.MaxValue, total));
            var rating = setup.Difficulty.Rating.Evaluate(finalScore, snapshot);
            result = new RunResult { RunId = runId, SongId = setup.Song.Id, Difficulty = setup.Difficulty.Difficulty,
                Score = finalScore, Grade = rating.Name, GradeColor = rating.Color, Stats = snapshot, Breakdown = breakdown.ToArray() };
            return result;
        }
        public static int Multiply(int value, int coefficient) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value * coefficient));
    }
}
