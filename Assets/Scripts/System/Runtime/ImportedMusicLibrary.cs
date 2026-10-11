using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TapTap.Systems.Music
{
    [Serializable]
    public sealed class ImportedSong
    {
        public string Id, FileName, Title;
        public double Duration, Bpm, FirstBeat;
        public int Frequency, Channels, Samples;
        public bool Aligned;
        public ImportedSong Copy() => (ImportedSong)MemberwiseClone();
    }

    [Serializable]
    public sealed class ImportedSongCatalog
    {
        public int Version;
        public List<ImportedSong> Songs;
    }

    public sealed class ImportedMusicLibrary
    {
        public const string CatalogFile = "library.json";
        public const long MaxFileBytes = 128L * 1024 * 1024;
        public const double MaxDuration = 900;
        private readonly JsonFileStore store;
        private ImportedSongCatalog catalog = new() { Version = 1, Songs = new List<ImportedSong>() };
        public string Error { get; private set; }
        public string DirectoryPath => store.DirectoryPath;
        public ImportedSong[] Songs => catalog.Songs.Select(s => s.Copy()).ToArray();
        public ImportedMusicLibrary(string directory)
        {
            store = new JsonFileStore(directory);
            if (!store.Exists(CatalogFile)) return;
            try
            {
                var loaded = store.Read<ImportedSongCatalog>(CatalogFile);
                if (loaded.Version != 1 || loaded.Songs == null || loaded.Songs.Any(s => !ValidSong(s)) ||
                    loaded.Songs.Select(s => s.Id).Distinct().Count() != loaded.Songs.Count)
                    throw new InvalidDataException("歌曲库内容无效或版本不支持。");
                catalog = loaded;
            }
            catch (Exception e) { Error = "歌曲库读取失败，原文件已保留：" + e.Message; }
        }
        private static bool ValidSong(ImportedSong s)
        {
            return s != null && s.Id != null && s.Id.Length == 64 && s.Id.All(Uri.IsHexDigit) &&
                (s.FileName == s.Id + ".mp3" || s.FileName == s.Id + ".wav") &&
                !string.IsNullOrWhiteSpace(s.Title) && Finite(s.Duration) && s.Duration > 0 && s.Duration <= MaxDuration &&
                s.Frequency > 0 && s.Channels > 0 && s.Channels <= 2 && s.Samples > 0 &&
                (!s.Aligned || ValidAlignment(s.Bpm, s.FirstBeat, s.Duration));
        }
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool ValidAlignment(double bpm, double firstBeat, double duration) =>
            Finite(bpm) && bpm >= 30 && bpm <= 300 && Finite(firstBeat) && firstBeat >= 0 && firstBeat < duration;
        public string PathFor(ImportedSong song)
        {
            if (!ValidSong(song)) throw new InvalidDataException("歌曲记录无效。");
            return Path.Combine(DirectoryPath, song.FileName);
        }
        public ImportedSong Find(string id) => catalog.Songs.Find(s => s.Id == id)?.Copy();
        public OperationResult Add(ImportedSong song)
        {
            if (!ValidSong(song)) return OperationResult.Fail("音频数据无效。");
            if (Find(song.Id) != null) return OperationResult.Ok();
            var next = CopyCatalog(); next.Songs.Add(song.Copy()); return Commit(next);
        }
        public OperationResult SaveAlignment(string id, double bpm, double firstBeat)
        {
            var next = CopyCatalog(); var song = next.Songs.Find(s => s.Id == id);
            if (song == null || !ValidAlignment(bpm, firstBeat, song.Duration))
                return OperationResult.Fail("BPM 需在 30–300 之间，首拍需在音频范围内。");
            song.Bpm = bpm; song.FirstBeat = firstBeat; song.Aligned = true; return Commit(next);
        }
        public OperationResult Rename(string id, string title)
        {
            title = title?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length > 80) return OperationResult.Fail("名称需为 1–80 个字符。");
            var next = CopyCatalog(); var song = next.Songs.Find(s => s.Id == id);
            if (song == null) return OperationResult.Fail("歌曲不存在。");
            song.Title = title; return Commit(next);
        }
        public OperationResult Remove(string id)
        {
            var next = CopyCatalog(); var song = next.Songs.Find(s => s.Id == id);
            if (song == null) return OperationResult.Fail("歌曲不存在。");
            next.Songs.Remove(song); var result = Commit(next);
            // Only delete our content-addressed copy after the catalog is committed.
            if (result.Success) try { File.Delete(PathFor(song)); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }
        private ImportedSongCatalog CopyCatalog() => new() { Version = 1, Songs = catalog.Songs.Select(s => s.Copy()).ToList() };
        private OperationResult Commit(ImportedSongCatalog next)
        {
            if (Error != null) return OperationResult.Fail(Error);
            try { store.Write(CatalogFile, next); catalog = next; return OperationResult.Ok(); }
            catch (Exception e) { return OperationResult.Fail("歌曲库保存失败：" + e.Message); }
        }

        public sealed class StagedFile
        {
            public string Path, Id, Extension, Title;
        }
        public Task<StagedFile> StageAsync(string source) => Task.Run(() =>
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension != ".wav" && extension != ".mp3") throw new InvalidDataException("目前支持 MP3 和 WAV。");
            Directory.CreateDirectory(DirectoryPath);
            string staging = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".importing");
            try
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length < 12 || input.Length > MaxFileBytes) throw new InvalidDataException("音频文件需小于 128 MB，且不能是空文件。");
                    var header = new byte[12]; input.Read(header, 0, header.Length); input.Position = 0;
                    bool wav = header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                        header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E';
                    bool mp3 = (header[0] == 'I' && header[1] == 'D' && header[2] == '3') ||
                        (header[0] == 0xff && (header[1] & 0xe0) == 0xe0);
                    if (!(extension == ".wav" ? wav : mp3)) throw new InvalidDataException("文件内容与音频格式不符，可能已损坏。");
                    using (var output = File.Create(staging)) input.CopyTo(output);
                }
                string id;
                using (var sha = SHA256.Create()) using (var input = File.OpenRead(staging))
                    id = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                string title = Path.GetFileNameWithoutExtension(source).Trim();
                if (title.Length > 80) title = title.Substring(0, 80);
                if (title.Length == 0) title = "未命名歌曲";
                return new StagedFile { Path = staging, Id = id, Extension = extension, Title = title };
            }
            catch { File.Delete(staging); throw; }
        });

        public OperationResult CompleteImport(StagedFile file, AudioClip clip, out ImportedSong song)
        {
            song = null;
            try
            {
                var existing = Find(file.Id);
                if (existing != null)
                {
                    // Reimport also repairs a missing library copy without resetting alignment.
                    string existingPath = PathFor(existing);
                    if (!File.Exists(existingPath)) File.Move(file.Path, existingPath);
                    else File.Delete(file.Path);
                    song = existing; return OperationResult.Ok();
                }
                song = new ImportedSong { Id = file.Id, FileName = file.Id + file.Extension, Title = file.Title,
                    Duration = (double)clip.samples / clip.frequency, Samples = clip.samples, Frequency = clip.frequency, Channels = clip.channels };
                string destination = PathFor(song);
                if (File.Exists(destination)) File.Delete(file.Path); else File.Move(file.Path, destination);
                var result = Add(song);
                if (!result.Success) { File.Delete(destination); song = null; }
                return result;
            }
            catch (Exception e) { song = null; return OperationResult.Fail("导入保存失败：" + e.Message); }
        }
    }

    public static class ImportedAudioLoader
    {
        public static IEnumerator Load(string path, Action<AudioClip, string> completed)
        {
            string uri;
            try { uri = new Uri(Path.GetFullPath(path)).AbsoluteUri; }
            catch (Exception e) { completed(null, e.Message); yield break; }
            using (var request = UnityWebRequestMultimedia.GetAudioClip(uri,
                Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG : AudioType.WAV))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { completed(null, "无法读取音频：" + request.error); yield break; }
                AudioClip clip = null;
                try { clip = DownloadHandlerAudioClip.GetContent(request); }
                catch (Exception e) { completed(null, "音频解码失败：" + e.Message); yield break; }
                // Inspect metadata before requesting PCM data, especially for long
                // compressed files whose decoded size is much larger than the file.
                if (clip && clip.frequency > 0 && (clip.frequency > 96000 || clip.channels > 2 ||
                    (double)clip.samples / clip.frequency > ImportedMusicLibrary.MaxDuration))
                {
                    UnityEngine.Object.Destroy(clip);
                    completed(null, "音频超出限制：最长 15 分钟、单声道/双声道、采样率不高于 96 kHz。"); yield break;
                }
                if (clip && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                float deadline = Time.realtimeSinceStartup + 30;
                while (clip && clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                if (!clip || clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0 || clip.frequency <= 0 ||
                    clip.frequency > 96000 || clip.channels <= 0 || clip.channels > 2 ||
                    (double)clip.samples / clip.frequency > ImportedMusicLibrary.MaxDuration)
                {
                    if (clip) UnityEngine.Object.Destroy(clip);
                    completed(null, "音频无效或超出限制：最长 15 分钟、单声道/双声道、采样率不高于 96 kHz。"); yield break;
                }
                completed(clip, null);
            }
        }
    }
}
