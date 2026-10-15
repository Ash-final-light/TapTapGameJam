using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TapTap.Danmaku;
using TapTap.Systems.Music;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Systems
{
    public sealed class CustomSongSession : IDisposable
    {
        public ImportedSong Imported;
        public AudioClip Audio;
        public MusicAnalysisAsset Analysis;
        public DanmakuSequence Sequence;
        public BattleConfig Battle;
        public SongDefinition Song;
        public BattleStartData Setup;
        public string RecordId;
        public bool Disposed { get; private set; }
        public void Dispose()
        {
            if (Disposed) return; Disposed = true;
            foreach (var obj in new Object[] { Song, Battle, Sequence, Analysis, Audio })
                if (obj) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
        }
    }

    /// <summary>Owns loaded custom music through preparation, scene changes, results and retries.</summary>
    public sealed class CustomSongChallengeService : IDisposable
    {
        public const string EntryId = "custom-song";
        public const string ScenePath = "Assets/Scenes/CustomSongBattle.unity";
        private readonly SystemRoot root;
        private readonly JsonFileStore cache;
        private int generation;
        public bool Preparing { get; private set; }
        public CustomSongSession Prepared { get; private set; }
        [Serializable] private sealed class CachedAnalysis
        {
            public int Version, Frequency, Channels, Samples;
            public MusicMarker[] Markers;
        }
        public CustomSongChallengeService(SystemRoot owner)
        { root = owner; cache = new JsonFileStore(Path.Combine(root.Music.DirectoryPath, "AnalysisCache")); }

        public string ValidateSong(string songId)
        {
            var song = root.Music.Find(songId);
            if (song == null) return "歌曲不存在。";
            if (!File.Exists(root.Music.PathFor(song))) return "音频副本缺失，请重新导入这首歌曲。";
            if (!song.Aligned || !ImportedMusicLibrary.ValidAlignment(song.Bpm, song.FirstBeat, song.Duration)) return "请先保存 BPM 和首拍对齐。";
            if (!root.Config.CustomSongTemplate || root.Config.CustomSongTemplate.Validate() != null ||
                root.Config.Levels.Find(EntryId)?.ScenePath != ScenePath) return "自定义战斗场景尚未接入。";
            if (!root.Config.Weapons.Any(w => w && w.Unlocked && w.Mode == PlayerWeaponMode.Normal && w.Projectile && w.FireRate > 0 && w.FireRate <= 60))
                return "缺少可用的普通武器配置。";
            return null;
        }
        public IEnumerator Prepare(string songId, Action<float, string> progress, Action<OperationResult> completed)
        {
            Cancel(); ReleasePrepared();
            int request = generation; Preparing = true;
            var task = PrepareCore(songId, request, progress);
            string failure = null;
            try
            {
                while (true)
                {
                    bool more;
                    try { more = task.MoveNext(); }
                    catch (Exception error) { failure = "歌曲准备失败：" + error.Message; break; }
                    if (!more) break;
                    yield return task.Current;
                }
            }
            finally { (task as IDisposable)?.Dispose(); if (request == generation) Preparing = false; }
            if (request == generation) completed?.Invoke(failure == null && Prepared != null ? OperationResult.Ok() : OperationResult.Fail(failure ?? "歌曲准备已取消。"));
        }
        private IEnumerator PrepareCore(string songId, int request, Action<float, string> progress)
        {
            string invalid = ValidateSong(songId); if (invalid != null) throw new InvalidOperationException(invalid);
            var song = root.Music.Find(songId); AudioClip loaded = null; string error = null;
            CustomSongSession session = null;
            try
            {
                progress?.Invoke(.02f, "读取歌曲……");
                yield return ImportedAudioLoader.Load(root.Music.PathFor(song), (clip, failure) => { loaded = clip; error = failure; });
                if (request != generation) yield break;
                if (error != null || !loaded) throw new InvalidOperationException(error ?? "无法解码音频。");
                session = new CustomSongSession { Imported = song, Audio = loaded }; loaded = null;
                var audio = session.Audio;
                session.Analysis = ScriptableObject.CreateInstance<MusicAnalysisAsset>();
                var analysis = session.Analysis;
                analysis.Source = audio; analysis.SampleRate = audio.frequency; analysis.Channels = audio.channels;
                analysis.SampleFrameCount = audio.samples; analysis.AlgorithmVersion = MusicAnalysisAsset.CurrentAlgorithmVersion;
                string cacheName = song.Id + "-fft-v1.json";
                var markers = ReadCache(cacheName, audio);
                if (markers == null)
                {
                    var analyzer = new MusicSpectrumAnalyzer(audio.frequency, audio.channels, audio.samples, new MusicAnalysisSettings());
                    const int block = 32768;
                    var buffer = new float[Math.Min(block, audio.samples) * audio.channels];
                    for (int frame = 0; frame < audio.samples; frame += block)
                    {
                        if (request != generation) yield break;
                        int frames = Math.Min(block, audio.samples - frame);
                        if (buffer.Length != frames * audio.channels) buffer = new float[frames * audio.channels];
                        if (!audio.GetData(buffer, frame)) throw new InvalidOperationException("无法读取音频采样。");
                        // Unity audio access stays on the main thread; FFT runs on a worker with a bounded buffer.
                        var workBuffer = buffer;
                        var work = Task.Run(() => analyzer.Push(workBuffer, frames));
                        while (!work.IsCompleted) yield return null;
                        if (work.IsFaulted) throw work.Exception.GetBaseException();
                        progress?.Invoke(.08f + .75f * (frame + frames) / audio.samples, "分析高中低频…… " + Math.Round(100.0 * (frame + frames) / audio.samples) + "%");
                        yield return null;
                    }
                    analyzer.Finish(); markers = analyzer.GetMarkers();
                    if (request != generation) yield break;
                    try { cache.Write(cacheName, new CachedAnalysis { Version = 1, Frequency = audio.frequency,
                        Channels = audio.channels, Samples = audio.samples, Markers = markers }); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { } // Cache is optional; records are not.
                }
                if (request != generation) yield break;
                analysis.Markers = markers;
                progress?.Invoke(.88f, "编排弹幕与预警……"); yield return null;
                if (request != generation) yield break;
                session.Sequence = CustomSongSequenceGenerator.Generate(song.Id, audio, song.Bpm, song.FirstBeat, analysis, root.Config.CustomSongTemplate);
                session.Battle = Object.Instantiate(root.Config.CustomSongTemplate); session.Battle.Sequence = session.Sequence;
                session.RecordId = RecordKey(song);
                session.Song = ScriptableObject.CreateInstance<SongDefinition>();
                session.Song.Id = session.RecordId; session.Song.Title = song.Title; session.Song.Subtitle = "共鸣舞台 · 自定义歌曲";
                var difficulty = new SongDifficultyConfig { Difficulty = SongDifficulty.Normal, LevelEntryId = EntryId, Battle = session.Battle };
                session.Song.Difficulties = new[] { difficulty };
                var weapon = root.Config.Weapons.First(w => w && w.Unlocked && w.Mode == PlayerWeaponMode.Normal && w.Projectile && w.FireRate > 0 && w.FireRate <= 60);
                session.Setup = new BattleStartData(session.Song, difficulty, weapon, null, session);
                string validation = session.Setup.Validate(root.Config, root.Saves);
                if (validation != null) throw new InvalidOperationException(validation);
                Prepared = session; session = null;
                progress?.Invoke(1, "舞台已准备完成");
            }
            finally
            {
                session?.Dispose();
                if (loaded) Object.Destroy(loaded);
            }
        }
        private MusicMarker[] ReadCache(string name, AudioClip audio)
        {
            try
            {
                if (!cache.Exists(name)) return null;
                var data = cache.Read<CachedAnalysis>(name);
                if (data.Version != 1 || data.Frequency != audio.frequency || data.Channels != audio.channels || data.Samples != audio.samples ||
                    data.Markers == null || data.Markers.Length > 200000) return null;
                if (data.Markers.Any(m => m.SampleFrame < 0 || m.SampleFrame >= audio.samples || m.Band < 0 || m.Band > 2 ||
                    m.Strength <= 0 || float.IsNaN(m.Strength) || float.IsInfinity(m.Strength))) return null;
                return data.Markers;
            }
            catch (Exception) { return null; }
        }
        public static string RecordKey(ImportedSong song) => "custom:v" + CustomSongSequenceGenerator.Version + ":" + song.Id + ":" +
            song.Bpm.ToString("R", CultureInfo.InvariantCulture) + ":" + song.FirstBeat.ToString("R", CultureInfo.InvariantCulture);
        public void Cancel() { generation++; Preparing = false; }
        public void ReleasePrepared()
        {
            if (Prepared == null) return;
            if (root.ActiveRun == Prepared.Setup) root.ActiveRun = null;
            Prepared.Dispose(); Prepared = null;
        }
        public void Dispose() { Cancel(); ReleasePrepared(); }
    }
}
