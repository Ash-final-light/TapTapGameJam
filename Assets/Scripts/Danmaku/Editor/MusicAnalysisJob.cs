using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
namespace TapTap.Danmaku.Editor
{
    public sealed class MusicAnalysisJob : IDisposable
    {
        private static MusicAnalysisJob active;
        private readonly DanmakuSequence sequence;
        private readonly Action completed;
        private readonly Action<Exception> failed;
        private readonly MusicAnalysisSettings settings;
        private readonly string temporaryPath, sourceHash;
        private AudioClip readable;
        private MusicSpectrumAnalyzer analyzer;
        private float[] buffer;
        private int frame;
        private readonly double started;
        public static bool Running => active != null;
        public static bool IsCurrent(DanmakuSequence s) => s.Music && s.MusicAnalysis && s.MusicAnalysis.Source == s.Music &&
            s.MusicAnalysis.AlgorithmVersion == MusicAnalysisAsset.CurrentAlgorithmVersion &&
            s.MusicAnalysis.SettingsFingerprint == JsonUtility.ToJson(s.MusicAnalysisSettings) &&
            s.MusicAnalysis.SourceFingerprint == AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(s.Music)).ToString();
        public static void Start(DanmakuSequence sequence, Action completed, Action<Exception> failed = null)
        {
            if (Running) throw new InvalidOperationException("已有音频正在解析。");
            string error = sequence.MusicAnalysisSettings.Validate();
            if (error != null) throw new ArgumentException(error);
            if (!sequence.Music || !AssetDatabase.Contains(sequence)) throw new ArgumentException("请先保存 Sequence 资产并选择 AudioClip。");
            try { active = new MusicAnalysisJob(sequence, completed, failed); EditorApplication.update += active.Update;
                AssemblyReloadEvents.beforeAssemblyReload += Cancel; }
            catch { active?.Dispose(); throw; }
        }
        private MusicAnalysisJob(DanmakuSequence sequence, Action completed, Action<Exception> failed)
        {
            this.sequence = sequence; this.completed = completed; this.failed = failed; started = EditorApplication.timeSinceStartup;
            settings = JsonUtility.FromJson<MusicAnalysisSettings>(JsonUtility.ToJson(sequence.MusicAnalysisSettings));
            string path = AssetDatabase.GetAssetPath(sequence.Music);
            sourceHash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            string extension = System.IO.Path.GetExtension(path);
            if (!(AssetImporter.GetAtPath(path) is AudioImporter)) throw new NotSupportedException("请选择通过 AudioImporter 导入的音频文件。");
            const string folder = "Assets/__DanmakuAudioAnalysis";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__DanmakuAudioAnalysis");
            temporaryPath = folder + "/" + Guid.NewGuid().ToString("N") + extension;
            try
            {
                if (!AssetDatabase.CopyAsset(path, temporaryPath)) throw new InvalidOperationException("无法创建音频解析副本。");
                var importer = (AudioImporter)AssetImporter.GetAtPath(temporaryPath);
                var options = importer.defaultSampleSettings; options.loadType = AudioClipLoadType.DecompressOnLoad;
                options.compressionFormat = AudioCompressionFormat.PCM; options.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                options.preloadAudioData = true; importer.defaultSampleSettings = options;
                // Remove active platform overrides on the temporary copy only.
                foreach (var platform in new[] { "Standalone", "Android", "iOS", "WebGL" })
                    if (importer.ContainsSampleSettingsOverride(platform)) importer.ClearSampleSettingOverride(platform);
                importer.loadInBackground = false; importer.SaveAndReimport();
                readable = AssetDatabase.LoadAssetAtPath<AudioClip>(temporaryPath);
                if (!readable || !readable.LoadAudioData()) throw new InvalidOperationException("音频副本加载失败。");
            }
            catch { AssetDatabase.DeleteAsset(temporaryPath); throw; }
        }
        private void Update()
        {
            try
            {
                if (!sequence || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.DisplayCancelableProgressBar("解析音乐", sequence.Music.name, readable ? (float)frame / readable.samples : 0)) { Dispose(); return; }
                if (readable.loadState != AudioDataLoadState.Loaded)
                { if (EditorApplication.timeSinceStartup - started > 30) throw new TimeoutException("音频副本未在 30 秒内加载。"); return; }
                analyzer ??= new MusicSpectrumAnalyzer(readable.frequency, readable.channels, readable.samples, settings);
                var watch = Stopwatch.StartNew();
                while (frame < readable.samples && watch.ElapsedMilliseconds < 8)
                {
                    int count = Math.Min(4096, readable.samples - frame);
                    if (buffer == null || buffer.Length != count * readable.channels) buffer = new float[count * readable.channels];
                    if (!readable.GetData(buffer, frame)) throw new InvalidOperationException("音频 PCM 读取失败，未覆盖已有解析结果。");
                    analyzer.Push(buffer, count); frame += count;
                }
                if (frame < readable.samples) return;
                analyzer.Finish();
                if (sourceHash != AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(sequence.Music)).ToString()) throw new InvalidOperationException("解析期间源音频发生变化，请重新解析。");
                var asset = ScriptableObject.CreateInstance<MusicAnalysisAsset>();
                asset.Source = sequence.Music; asset.SampleRate = readable.frequency; asset.Channels = readable.channels; asset.SampleFrameCount = readable.samples;
                asset.AlgorithmVersion = MusicAnalysisAsset.CurrentAlgorithmVersion; asset.SourceFingerprint = sourceHash; asset.SettingsFingerprint = JsonUtility.ToJson(settings);
                asset.Markers = analyzer.GetMarkers(); asset.WaveformMinMax = analyzer.GetWaveform();
                string output = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(AssetDatabase.GetAssetPath(sequence), null) + "_MusicAnalysis.asset");
                AssetDatabase.CreateAsset(asset, output); Undo.RecordObject(sequence, "关联音乐解析结果");
                sequence.MusicAnalysis = asset; EditorUtility.SetDirty(sequence); AssetDatabase.SaveAssets();
                Debug.Log($"音乐解析完成：{asset.Markers.Length} 个分频候选点，{output}", asset);
                Dispose(); completed?.Invoke();
            }
            catch (Exception e) { Debug.LogException(e); Dispose(); failed?.Invoke(e); }
        }
        public static void Cancel() => active?.Dispose();
        public void Dispose()
        {
            EditorApplication.update -= Update; EditorUtility.ClearProgressBar();
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            if (!string.IsNullOrEmpty(temporaryPath)) AssetDatabase.DeleteAsset(temporaryPath);
            const string folder = "Assets/__DanmakuAudioAnalysis";
            if (System.IO.Directory.Exists(folder) && System.IO.Directory.GetFileSystemEntries(folder).Length == 0) AssetDatabase.DeleteAsset(folder);
            if (ReferenceEquals(active, this)) active = null;
        }
    }
}
