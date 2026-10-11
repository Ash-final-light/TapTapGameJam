using System;
using TapTap.Danmaku;
namespace TapTap.Systems
{
    public sealed class BattleStartData
    {
        public readonly SongDefinition Song;
        public readonly SongDifficultyConfig Difficulty;
        public readonly PlayerWeaponDefinition Weapon;
        public readonly ItemDefinition[] Items;
        public BattleStartData(SongDefinition song, SongDifficultyConfig difficulty, PlayerWeaponDefinition weapon, ItemDefinition[] items)
        { Song = song; Difficulty = difficulty; Weapon = weapon; Items = items == null ? Array.Empty<ItemDefinition>() : (ItemDefinition[])items.Clone(); }
        public static BattleStartData FindForBattle(SystemConfig config, BattleConfig battle, PlayerWeaponDefinition weapon)
        {
            if (!config || !config.Songs) return null;
            foreach (var song in config.Songs.Songs)
                if (song)
                    foreach (var difficulty in song.Difficulties)
                        if (difficulty != null && difficulty.Enabled && difficulty.Battle == battle && difficulty.Rating)
                            return new BattleStartData(song, difficulty, weapon, null);
            return null;
        }
        public string Validate(SystemConfig config, GameSaveService saves)
        {
            if (!Song || !config.Songs || config.Songs.Find(Song.Id) != Song || Difficulty == null || Song.Find(Difficulty.Difficulty) != Difficulty || !Difficulty.Enabled)
                return "歌曲或难度未开放。";
            if (!Difficulty.Battle || Difficulty.Battle.Validate() != null || !Difficulty.Rating || config.Levels.Find(Difficulty.LevelEntryId) == null)
                return "歌曲关卡或评级配置不完整。";
            if (!Weapon || !Weapon.Unlocked || Array.IndexOf(config.Weapons, Weapon) < 0 || Weapon.Mode == PlayerWeaponMode.Homing || !Weapon.Projectile || Weapon.FireRate <= 0 || Weapon.FireRate > 60)
                return "请选择已实现且有效的玩家弹幕。";
            if (Items.Length > Math.Min(3, config.ItemSlotCount)) return "装备道具超过槽位上限。";
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var item in Items)
                if (item && (!ids.Add(item.Id) || Array.IndexOf(config.Items, item) < 0 || saves.Quantity(item.Id) <= 0 || !item.Effect)) return "道具未获得、重复装备或未配置效果。";
            if (!config.Drops) return "未配置结算掉落表。";
            string dropError = config.Drops.Validate(); if (dropError != null) return dropError;
            foreach (var drop in config.Drops.Entries) if (Array.IndexOf(config.Items, drop.Item) < 0) return "掉落道具未加入装备目录。";
            return null;
        }
    }
    public enum RunEndReason { Completed, PlayerDied }
    public sealed class RunResult
    {
        public string RunId, SongId, Grade, DropItemId;
        public SongDifficulty Difficulty;
        public int Score, DropQuantity;
        public UnityEngine.Color GradeColor;
        public BattleStats Stats;
        public ScoreBreakdown[] Breakdown;
    }
}
