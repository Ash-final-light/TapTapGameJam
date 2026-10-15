using System;
using System.Collections.Generic;
using UnityEngine;
namespace TapTap.Systems
{
    [Serializable] public sealed class SongRecord { public string SongId, Grade; public SongDifficulty Difficulty; public int Score; }
    [Serializable] public sealed class ItemAcquisition { public string ItemId; public int Quantity; }
    [Serializable] public sealed class GameSaveData
    {
        public int Version;
        public List<SongRecord> Records;
        public List<ItemAcquisition> Items;
        public string LastSettledRunId;
    }
    public sealed class GameSaveService
    {
        public const string FileName = "game.json";
        private readonly JsonFileStore store;
        public GameSaveData Current { get; private set; }
        public SaveStatus Status { get; private set; }
        public GameSaveService(JsonFileStore store) { this.store = store; Inspect(); }
        public void Inspect()
        {
            Current = null;
            if (!store.Exists(FileName)) { Status = SaveStatus.Missing; return; }
            try
            {
                var data = store.Read<GameSaveData>(FileName);
                if (data.Version == 0) throw new InvalidOperationException("存档版本缺失。");
                if (data.Version != 1) { Status = SaveStatus.UnsupportedVersion; return; }
                if (data.Records == null || data.Items == null) throw new InvalidOperationException("存档数据缺失。");
                var keys = new HashSet<string>();
                foreach (var r in data.Records) if (r == null || string.IsNullOrWhiteSpace(r.SongId) || r.Score < 0 || !Enum.IsDefined(typeof(SongDifficulty), r.Difficulty) || !keys.Add(r.SongId + ":" + r.Difficulty)) throw new InvalidOperationException("歌曲成绩不合法。");
                keys.Clear(); foreach (var i in data.Items) if (i == null || string.IsNullOrWhiteSpace(i.ItemId) || i.Quantity < 0 || !keys.Add(i.ItemId)) throw new InvalidOperationException("道具数量不合法。");
                Current = data; Status = SaveStatus.Valid;
            }
            catch (Exception e) { Debug.LogWarning("[Spotlight] 游戏存档读取失败: " + e.Message); Status = SaveStatus.Corrupt; }
        }
        public int Quantity(string id) => Current?.Items.Find(i => i.ItemId == id)?.Quantity ?? 0;
        public SongRecord Record(string songId, SongDifficulty difficulty) => Current?.Records.Find(r => r.SongId == songId && r.Difficulty == difficulty);
        private static GameSaveData Empty() => new() { Version = 1, Records = new List<SongRecord>(), Items = new List<ItemAcquisition>() };
        public OperationResult NewGame() => Write(Empty());
        public OperationResult Settle(RunResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.RunId) || string.IsNullOrWhiteSpace(result.SongId) || result.Score < 0 ||
                (!result.IsCustom && (string.IsNullOrWhiteSpace(result.DropItemId) || result.DropQuantity <= 0)) ||
                (result.IsCustom && (!result.SongId.StartsWith("custom:v", StringComparison.Ordinal) || result.DropQuantity != 0 || !string.IsNullOrEmpty(result.DropItemId)))) return OperationResult.Fail("结算数据不完整。");
            if (Current?.LastSettledRunId == result.RunId) return OperationResult.Ok();
            var draft = Current == null ? Empty() : JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(Current));
            var record = draft.Records.Find(r => r.SongId == result.SongId && r.Difficulty == result.Difficulty);
            if (record == null) { record = new SongRecord { SongId = result.SongId, Difficulty = result.Difficulty }; draft.Records.Add(record); record.Score = result.Score; record.Grade = result.Grade; }
            else if (result.Score > record.Score) { record.Score = result.Score; record.Grade = result.Grade; }
            if (!result.IsCustom) AddItem(draft, result.DropItemId, result.DropQuantity);
            draft.LastSettledRunId = result.RunId;
            return Write(draft);
        }
        public OperationResult GrantItem(string id, int amount)
        {
            if (string.IsNullOrWhiteSpace(id) || amount <= 0) return OperationResult.Fail("道具 ID 和数量不合法。");
            var draft = Current == null ? Empty() : JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(Current));
            AddItem(draft, id, amount); return Write(draft);
        }
        private static void AddItem(GameSaveData draft, string id, int amount)
        {
            var item = draft.Items.Find(i => i.ItemId == id);
            if (item == null) { item = new ItemAcquisition { ItemId = id }; draft.Items.Add(item); }
            item.Quantity = (int)Math.Min(int.MaxValue, (long)item.Quantity + amount);
        }
        private OperationResult Write(GameSaveData draft)
        {
            try { store.Write(FileName, draft); Current = draft; Status = SaveStatus.Valid; return OperationResult.Ok(); }
            catch (Exception e) { Debug.LogWarning("[Spotlight] 游戏存档写入失败: " + e.Message); return OperationResult.Fail("成绩与道具未保存，请检查存储空间并重试保存。"); }
        }
    }
}
