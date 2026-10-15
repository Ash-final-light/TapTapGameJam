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
        public bool BossScoringEnabled { get; internal set; }
        public int BossHits { get; private set; }
        public int OnBeatHits { get; private set; }
        public int DamageTakenCount { get; private set; }
        public float DamageTaken { get; private set; }
        public int ElapsedTicks { get; private set; }
        public RunEndReason EndReason { get; private set; }
        public bool IsFinished { get; private set; }
        public bool Completed => IsFinished && EndReason == RunEndReason.Completed;
        internal void OrbDestroyed() { DestroyedOrbs++; if (!BossScoringEnabled) IncrementCombo(); }
        internal void BossHit(bool onBeat) { BossHits++; if (onBeat) OnBeatHits++; IncrementCombo(); }
        private void IncrementCombo() { CurrentCombo++; MaxCombo = Math.Max(MaxCombo, CurrentCombo); }
        internal void BreakCombo() => CurrentCombo = 0;
        internal void Miss() { Misses++; if (!BossScoringEnabled) BreakCombo(); }
        internal void Damage(float amount) { if (amount > 0) { DamageTakenCount++; DamageTaken += amount; } }
        internal BattleStats Finish(int ticks, RunEndReason reason) { var result = (BattleStats)MemberwiseClone(); result.ElapsedTicks = ticks; result.EndReason = reason; result.IsFinished = true; return result; }
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
        private readonly BossScoringSettings bossScoring;
        private readonly BattleBeatClock beats;
        private readonly int comboTimeoutTicks;
        private int tick, lastBossHitTick = -1;
        private RunResult result;
        public BattleStats Stats => result?.Stats ?? stats;
        public BattleBeatClock Beats => beats;
        public int CurrentScore => result?.Score ?? Evaluate(stats, null);
        public int FeedbackRevision { get; private set; }
        public int LastFeedbackTick { get; private set; } = -1;
        public int LastAward { get; private set; }
        public ScoreFeedback LastFeedback { get; private set; }
        public float ComboRemaining => stats.CurrentCombo > 0 && comboTimeoutTicks > 0
            ? (float)Math.Max(0, comboTimeoutTicks - (tick - lastBossHitTick)) / comboTimeoutTicks : 0;

        public ScoreManager(ScoreFormula[] formulas, BossScoringSettings bossScoring = null, DanmakuSequence sequence = null)
        {
            this.formulas = formulas == null ? Array.Empty<ScoreFormula>() : (ScoreFormula[])formulas.Clone();
            beats = new BattleBeatClock(sequence);
            if (bossScoring?.Enabled != true) return;
            string error = bossScoring.Validate();
            if (error != null) throw new ArgumentException(error);
            this.bossScoring = bossScoring.Copy(); stats.BossScoringEnabled = true;
            comboTimeoutTicks = Math.Max(1, (int)Math.Ceiling(beats.PeriodSeconds * bossScoring.ComboTimeoutBeats * DanmakuSequence.TickRate));
        }

        public void Advance(int currentTick)
        {
            if (result != null) return;
            tick = Math.Max(tick, currentTick);
            if (bossScoring != null && stats.CurrentCombo > 0 && tick - lastBossHitTick >= comboTimeoutTicks)
                BreakCombo();
        }

        public void Consume(BattleEvent e)
        {
            if (result != null) return;
            Advance(e.Tick);
            if (e.Type == BattleEventType.EnergyOrbDestroyed) stats.OrbDestroyed();
            if (bossScoring != null && e.Type == BattleEventType.BossHit && e.TargetKind == CombatTargetKind.Boss)
            {
                bool onBeat = beats.IsOnBeat(e.FireTick, bossScoring.BeatWindowSeconds);
                stats.BossHit(onBeat); lastBossHitTick = tick;
                Feedback(onBeat ? ScoreFeedback.Perfect : ScoreFeedback.Hit,
                    (int)Math.Min(int.MaxValue, (long)bossScoring.PointsPerHit + (onBeat ? bossScoring.OnBeatBonus : 0)));
            }
            if (e.Type == BattleEventType.Damage && e.TargetKind == CombatTargetKind.Player)
            {
                stats.Damage(e.Damage.ToFloat());
                if (bossScoring != null && e.Damage > Framework.FixedPoint.FP64.Zero && stats.CurrentCombo > 0) BreakCombo();
            }
        }
        public void MissOrb() { if (result == null) stats.Miss(); }
        private void BreakCombo() { stats.BreakCombo(); Feedback(ScoreFeedback.ComboBroken, 0); }
        private void Feedback(ScoreFeedback kind, int award)
        { LastFeedback = kind; LastAward = award; LastFeedbackTick = tick; FeedbackRevision++; }

        private int Evaluate(BattleStats snapshot, List<ScoreBreakdown> breakdown)
        {
            long total = 0;
            foreach (var formula in formulas)
                if (formula && !(snapshot.BossScoringEnabled && formula is ComboScoreFormula))
                {
                    int score = formula.Evaluate(snapshot); total += score;
                    breakdown?.Add(new ScoreBreakdown(formula.name, score));
                }
            if (bossScoring != null)
            {
                int hits = Multiply(snapshot.BossHits, bossScoring.PointsPerHit);
                int bonus = Multiply(snapshot.OnBeatHits, bossScoring.OnBeatBonus);
                total += (long)hits + bonus;
                breakdown?.Add(new ScoreBreakdown("Boss 命中", hits));
                breakdown?.Add(new ScoreBreakdown("节拍奖励", bonus));
            }
            return (int)Math.Max(0, Math.Min(int.MaxValue, total));
        }

        public RunResult Finish(string runId, BattleStartData setup, int ticks, RunEndReason reason)
        {
            if (result != null) return result;
            Advance(ticks);
            var snapshot = stats.Finish(ticks, reason);
            var breakdown = new List<ScoreBreakdown>();
            int finalScore = Evaluate(snapshot, breakdown);
            var rating = setup?.Difficulty?.Rating ? setup.Difficulty.Rating.Evaluate(finalScore, snapshot) : new RatingValue();
            result = new RunResult { RunId = runId, SongId = setup?.Song ? setup.Song.Id : "standalone",
                IsCustom = setup?.IsCustom == true, SongTitle = setup?.Song ? setup.Song.Title : null,
                Difficulty = setup?.Difficulty?.Difficulty ?? SongDifficulty.Normal,
                Score = finalScore, Grade = rating.Name, GradeColor = rating.Color, Stats = snapshot, Breakdown = breakdown.ToArray() };
            if (result.IsCustom) { result.Grade = snapshot.Completed ? "完成" : "未完成"; result.GradeColor = UnityEngine.Color.white; }
            return result;
        }
        public static int Multiply(int value, int coefficient) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value * coefficient));
    }
    public enum ScoreFeedback { None, Hit, Perfect, ComboBroken }
}
