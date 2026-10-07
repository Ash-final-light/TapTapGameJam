using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace TapTap.Systems
{
    public sealed class JsonFileStore
    {
        public string DirectoryPath { get; }
        public JsonFileStore(string directory) { DirectoryPath = directory; }
        public bool Exists(string name) => File.Exists(Path.Combine(DirectoryPath, name));
        public T Read<T>(string name) where T : class
        {
            string json = File.ReadAllText(Path.Combine(DirectoryPath, name), Encoding.UTF8).Trim();
            if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal))
                throw new InvalidDataException("存档内容不完整。");
            return JsonUtility.FromJson<T>(json) ?? throw new InvalidDataException("存档内容为空。");
        }
        public void Write<T>(string name, T data)
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, name), temporary = path + ".tmp", backup = path + ".bak";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else File.Move(temporary, path);
        }
        public void Delete(string name)
        {
            string path = Path.Combine(DirectoryPath, name);
            File.Delete(path + ".tmp");
            File.Delete(path + ".bak");
            File.Delete(path);
        }
    }
    public sealed class ProgressSaveService
    {
        private const string FileName = "progress.json";
        private readonly JsonFileStore store;
        private readonly LevelEntryCatalog levels;
        public ProgressSaveData Current { get; private set; }
        public SaveStatus Status { get; private set; }
        public ProgressSaveService(JsonFileStore store, LevelEntryCatalog levels) { this.store = store; this.levels = levels; Inspect(); }
        public void Inspect()
        {
            Current = null;
            if (!store.Exists(FileName)) { Status = SaveStatus.Missing; return; }
            try
            {
                var data = store.Read<ProgressSaveData>(FileName);
                if (data.Version != 1) { Status = SaveStatus.UnsupportedVersion; return; }
                if (string.IsNullOrEmpty(data.LevelEntryId) || levels.Find(data.LevelEntryId) == null || data.CharacterValues == null)
                { Status = SaveStatus.Corrupt; return; }
                Current = data; Status = SaveStatus.Valid;
            }
            catch (Exception e) { Debug.LogWarning("[Spotlight] 进度读取失败: " + e.Message); Status = SaveStatus.Corrupt; }
        }
        public OperationResult Save(string entryId, ProgressValue[] values)
        {
            if (levels.Find(entryId) == null) return OperationResult.Fail("关卡入口未配置。");
            var data = new ProgressSaveData { LevelEntryId = entryId, CharacterValues = values ?? Array.Empty<ProgressValue>() };
            try { store.Write(FileName, data); Current = data; Status = SaveStatus.Valid; return OperationResult.Ok(); }
            catch (Exception e) { Debug.LogException(e); return OperationResult.Fail("无法保存进度，请检查存储空间及文件权限。"); }
        }
        public OperationResult Clear()
        {
            try { store.Delete(FileName); Current = null; Status = SaveStatus.Missing; return OperationResult.Ok(); }
            catch (Exception e)
            {
                Inspect();
                Debug.LogWarning("[Spotlight] 进度清理失败: " + e.Message);
                return OperationResult.Fail("无法清理本局进度，请检查文件权限后重试。");
            }
        }
    }
}
