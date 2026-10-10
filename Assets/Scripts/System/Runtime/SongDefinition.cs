using System;
using TapTap.Danmaku;
using UnityEngine;

namespace TapTap.Systems
{
    public enum SongDifficulty { Normal, Hard, Expert }
    [Serializable] public sealed class SongDifficultyConfig
    {
        public SongDifficulty Difficulty;
        public bool Enabled = true;
        public string LevelEntryId = "tutorial";
        public BattleConfig Battle;
        public RatingFormula Rating;
    }
    [CreateAssetMenu(menuName = "Spotlight/Song")]
    public sealed class SongDefinition : ScriptableObject
    {
        public string Id, Title, Subtitle, Artist;
        public Sprite Cover;
        public SongDifficultyConfig[] Difficulties = Array.Empty<SongDifficultyConfig>();
        public SongDifficultyConfig Find(SongDifficulty difficulty) => Array.Find(Difficulties, d => d != null && d.Difficulty == difficulty);
    }
}
