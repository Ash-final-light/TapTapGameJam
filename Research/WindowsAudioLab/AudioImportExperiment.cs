using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

public sealed class AudioImportExperiment : MonoBehaviour
{
    [Serializable] public sealed class Entry {
        public string id, title, file; public int frequency, channels, samples;
        public double bpm, firstBeat; public bool configured;
    }
    [Serializable] public sealed class Catalog { public List<Entry> songs = new List<Entry>(); }
    [Serializable] public sealed class CheckResult { public string name, detail; public bool pass; }
    [Serializable] public sealed class LoadResult {
        public string name, result, error, loadState; public bool clipPresent, pcmReadable;
        public int rate, channels, samples; public double loadMs, readyMs, length;
    }
    [Serializable] public sealed class Timing {
        public string name; public int requestedFps, observations;
        public double observedFps, meanSignedMs, p95AbsoluteMs, maxAbsoluteMs, maxFrameMs;
        public double realtimeDspOffsetRangeMs, realtimeDspElapsedP95Ms;
    }
    [Serializable] public sealed class Report {
        public string unity, platform, utc, status = "RUNNING", limitation;
        public int requestedBuffer, outputRate, bufferFrames, buffers, failures;
        public List<CheckResult> checks = new List<CheckResult>();
        public List<LoadResult> loads = new List<LoadResult>();
        public List<Timing> timings = new List<Timing>();
    }
    private Report report = new Report();
    private Catalog catalog = new Catalog();
    private string labRoot, library, reportPath, catalogPath;
    private AudioSource source;
    private AudioClip playback;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => new GameObject("Independent Audio Experiment").AddComponent<AudioImportExperiment>();
    private void Start()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) {
            if (args[i] == "--lab-root") labRoot = args[i + 1];
            if (args[i] == "--buffer-frames") report.requestedBuffer = int.Parse(args[i + 1]);
        }
        if (string.IsNullOrEmpty(labRoot)) { UnityEngine.Debug.LogError("Missing --lab-root"); Application.Quit(2); return; }
        library = Path.Combine(labRoot, "results", "library"); Directory.CreateDirectory(library);
        reportPath = Path.Combine(labRoot, "results", report.requestedBuffer == 0 ? "report.json" : "report-buffer-" + report.requestedBuffer + ".json"); catalogPath = Path.Combine(library, "catalog.json");
        report.unity = Application.unityVersion; report.platform = Application.platform.ToString();
        report.utc = DateTime.UtcNow.ToString("o");
        report.limitation = "Scripted file paths; no system-picker interaction, physical input, acoustic loopback or audible calibration. Timing measures DSP versus sample cursor, not end-to-end latency. Mono x64 only.";
        if (report.requestedBuffer != 0) {
            var configuration = AudioSettings.GetConfiguration(); configuration.dspBufferSize = report.requestedBuffer;
            Check("Requested audio buffer accepted", AudioSettings.Reset(configuration), "Requested frames=" + report.requestedBuffer);
        }
        report.outputRate = AudioSettings.outputSampleRate;
        AudioSettings.GetDSPBufferSize(out report.bufferFrames, out report.buffers);
        QualitySettings.vSyncCount = 0; Application.runInBackground = true;
        source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0;
        source.volume = 0; source.pitch = 1;
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        StartCoroutine(Guard(Run()));
    }
    private IEnumerator Guard(IEnumerator run)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(run);
        while (stack.Count > 0)
        {
            object current = null; bool next = false; Exception failure = null;
            try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { Check("Unexpected exception", false, failure.ToString()); break; }
            if (!next) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        source.Stop(); if (playback) Destroy(playback);
        report.status = report.failures == 0 ? "PASS" : "FAIL";
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        UnityEngine.Debug.Log("AUDIO LAB " + report.status + " checks=" + report.checks.Count);
        Application.Quit(report.failures == 0 ? 0 : 1);
    }
    private IEnumerator Run()
    {
        var inputs = Directory.GetFiles(Path.Combine(labRoot, "inputs", "外部 音频 #100%"));
        foreach (var path in inputs.OrderBy(p => p, StringComparer.Ordinal))
        {
            bool bad = Path.GetFileName(path).StartsWith("损坏", StringComparison.Ordinal);
            AudioType type = Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG : AudioType.WAV;
            var timer = Stopwatch.StartNew();
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(path)).AbsoluteUri, type))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                var load = new LoadResult { name = Path.GetFileName(path), result = request.result.ToString(), error = request.error, loadMs = timer.Elapsed.TotalMilliseconds };
                report.loads.Add(load);
                if (bad)
                {
                    AudioClip rejected = null;
                    try { if (request.result == UnityWebRequest.Result.Success) rejected = DownloadHandlerAudioClip.GetContent(request); }
                    catch (Exception error) { load.error = error.Message; }
                    load.clipPresent = rejected;
                    if (rejected) { load.rate = rejected.frequency; load.channels = rejected.channels; load.samples = rejected.samples; load.loadState = rejected.loadState.ToString(); }
                    bool blocked = false;
                    try { Import(path, rejected); } catch (InvalidDataException) { blocked = true; }
                    Check("Reject corrupt WAV at header check", blocked, $"request={request.result}, clipPresent={load.clipPresent}, samples={load.samples}");
                    if (rejected) Destroy(rejected);
                    continue;
                }
                Check("Decode " + load.name, request.result == UnityWebRequest.Result.Success, request.error);
                if (request.result != UnityWebRequest.Result.Success) continue;
                var clip = DownloadHandlerAudioClip.GetContent(request);
                load.clipPresent = clip;
                if (!clip) { Check("Decoded clip exists " + load.name, false, "Request success without a clip"); continue; }
                clip.LoadAudioData(); double readyDeadline = Time.realtimeSinceStartupAsDouble + 30;
                while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartupAsDouble < readyDeadline) yield return null;
                load.readyMs = timer.Elapsed.TotalMilliseconds; load.loadState = clip.loadState.ToString();
                load.rate = clip.frequency; load.channels = clip.channels; load.samples = clip.samples; load.length = (double)clip.samples / clip.frequency;
                var probe = new float[Math.Min(clip.samples, clip.frequency) * clip.channels];
                load.pcmReadable = clip.GetData(probe, 0);
                Check("Valid decoded PCM " + load.name, clip.samples > 0 && clip.frequency > 0 && clip.channels > 0 && clip.loadState == AudioDataLoadState.Loaded && load.pcmReadable, $"{clip.frequency} Hz / {clip.channels}ch / {load.length:F3}s / {clip.loadState}");
                var entry = Import(path, clip);
                Check("Imported bytes " + load.name, Hash(path) == Hash(Path.Combine(library, entry.file)), entry.id);
                if (load.name.StartsWith("校准", StringComparison.Ordinal)) CheckWave(clip, load.name.Contains("137.5") ? 137.5 : 120, load.name.Contains("137.5") ? .123 : .25);
                if (load.name == "校准 137.5bpm 48000.wav") playback = clip; else Destroy(clip);
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            }
        }
        Check("Invalid file excluded from catalog", catalog.songs.Count == report.loads.Count - 1, catalog.songs.Count.ToString());
        Check("Pending alignment persists", JsonUtility.FromJson<Catalog>(File.ReadAllText(catalogPath)).songs.All(e => !e.configured && e.bpm == 0), "Imported before BPM is entered");
        if (!playback) { Check("Playback fixture present", false, "48 kHz fixture missing"); yield break; }
        var fixturePath = inputs.First(p => Path.GetFileName(p) == "校准 137.5bpm 48000.wav");
        int count = catalog.songs.Count;
        var selected = Import(fixturePath, playback);
        Check("Duplicate content reuses entry", catalog.songs.Count == count, selected.id);
        var draft = JsonUtility.FromJson<Entry>(JsonUtility.ToJson(selected)); draft.bpm = 137.5; draft.firstBeat = .123;
        Check("Cancel alignment preserves saved entry", selected.bpm == 0 && !selected.configured, "Draft is independent");
        Check("Reject invalid alignment", !Valid(double.NaN, .123, playback.length) && !Valid(0, .123, playback.length) && !Valid(137.5, playback.length, playback.length), "NaN / zero BPM / first beat at end");
        selected.bpm = draft.bpm; selected.firstBeat = draft.firstBeat; selected.configured = true; SaveCatalog();
        var restored = JsonUtility.FromJson<Catalog>(File.ReadAllText(catalogPath)).songs.Find(e => e.id == selected.id);
        Check("Alignment survives JSON reload", restored.configured && Math.Abs(restored.bpm - 137.5) < 1e-9 && Math.Abs(restored.firstBeat - .123) < 1e-9, "137.5 BPM / 0.123 s");
        var renamed = fixturePath + ".moved"; File.Move(fixturePath, renamed);
        try { Check("Source moved, copy remains", File.Exists(Path.Combine(library, selected.file)), selected.file); }
        finally { File.Move(renamed, fixturePath); }
        // Independent timestamp data represents events delivered later; physical key latency is not measured.
        foreach (int fps in new[] { 30, 60, 120 })
        {
            double max = 0;
            for (int n = 1; n < 30; n++)
            {
                double eventTime = .123 + n * 60 / 137.5 + .027;
                double delivery = Math.Ceiling((eventTime + .05) * fps) / fps;
                double songAtEvent = delivery - (delivery - eventTime);
                max = Math.Max(max, Math.Abs(songAtEvent - (.123 + n * 60 / 137.5) - .027));
            }
            Check("Synthetic late delivery " + fps + " FPS", max < 1e-9, "Timestamp error seconds=" + max.ToString("G6"));
        }
        source.clip = playback;
        foreach (int fps in new[] { 30, 60, 120 })
        {
            Application.targetFrameRate = fps;
            source.Stop(); source.timeSamples = 0;
            double start = AudioSettings.dspTime + .1; source.PlayScheduled(start);
            yield return Observe("Continuous", fps, start, 0, 2.5);
        }
        source.Stop(); source.timeSamples = playback.frequency;
        double seekStart = AudioSettings.dspTime + .1; source.PlayScheduled(seekStart);
        yield return Observe("Seek 1s", 120, seekStart, 1, .6);
        source.Pause(); int paused = source.timeSamples;
        double pauseUntil = Time.realtimeSinceStartupAsDouble + .25;
        while (Time.realtimeSinceStartupAsDouble < pauseUntil) yield return null;
        Check("Paused sample position frozen", source.timeSamples == paused, paused.ToString());
        source.Stop(); source.timeSamples = paused;
        double resumeStart = AudioSettings.dspTime + .1; source.PlayScheduled(resumeStart);
        yield return Observe("Resume by rescheduling", 120, resumeStart, (double)paused / playback.frequency, .6);
    }
    private IEnumerator Observe(string name, int fps, double start, double origin, double seconds)
    {
        var errors = new List<double>(); var gaps = new List<double>(); var offsets = new List<double>(); var elapsedErrors = new List<double>();
        var raw = new List<string> { "realtime,dsp,samples,frequency,startDsp,origin,valid" };
        double firstRealtime = 0, firstDsp = 0;
        double previous = Time.realtimeSinceStartupAsDouble, deadline = previous + .1 + seconds;
        int firstSample = source.timeSamples, lastSample = firstSample;
        while (Time.realtimeSinceStartupAsDouble < deadline)
        {
            yield return null;
            double now = Time.realtimeSinceStartupAsDouble; gaps.Add((now - previous) * 1000); previous = now;
            double dsp = AudioSettings.dspTime; lastSample = source.timeSamples;
            raw.Add(string.Join(",", new[] { now.ToString("R", System.Globalization.CultureInfo.InvariantCulture), dsp.ToString("R", System.Globalization.CultureInfo.InvariantCulture), lastSample.ToString(), playback.frequency.ToString(), start.ToString("R", System.Globalization.CultureInfo.InvariantCulture), origin.ToString("R", System.Globalization.CultureInfo.InvariantCulture), dsp >= start + .1 && source.isPlaying ? "1" : "0" }));
            if (dsp >= start + .1 && source.isPlaying)
            {
                if (errors.Count == 0) { firstRealtime = now; firstDsp = dsp; }
                errors.Add((origin + dsp - start - (double)lastSample / playback.frequency) * 1000);
                offsets.Add((now - dsp) * 1000);
                elapsedErrors.Add(Math.Abs((now - firstRealtime - (dsp - firstDsp)) * 1000));
            }
        }
        Check(name + " audio advances " + fps, lastSample > firstSample && errors.Count > 0, $"{firstSample}->{lastSample}");
        File.WriteAllLines(Path.Combine(labRoot, "results", "clock-" + name.Replace(' ', '-') + "-" + fps + "-buffer-" + report.bufferFrames + ".csv"), raw);
        if (errors.Count == 0) yield break;
        var absolute = errors.Select(Math.Abs).OrderBy(x => x).ToArray();
        elapsedErrors.Sort();
        report.timings.Add(new Timing { name = name, requestedFps = fps, observations = errors.Count,
            observedFps = 1000 / gaps.Average(), maxFrameMs = gaps.Max(), meanSignedMs = errors.Average(),
            p95AbsoluteMs = absolute[(int)Math.Floor((absolute.Length - 1) * .95)], maxAbsoluteMs = absolute.Last(),
            realtimeDspOffsetRangeMs = offsets.Max() - offsets.Min(), realtimeDspElapsedP95Ms = elapsedErrors[(int)Math.Floor((elapsedErrors.Count - 1) * .95)] });
    }
    private Entry Import(string path, AudioClip clip)
    {
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) && !WavHeader(path)) throw new InvalidDataException("Invalid WAV header");
        if (!clip || clip.frequency <= 0 || clip.channels <= 0 || clip.samples <= 0 || clip.loadState != AudioDataLoadState.Loaded) throw new InvalidDataException("Invalid decoded audio");
        string id = Hash(path); var old = catalog.songs.Find(e => e.id == id); if (old != null) return old;
        string relative = id + Path.GetExtension(path).ToLowerInvariant(); File.Copy(path, Path.Combine(library, relative), true);
        var entry = new Entry { id = id, title = Path.GetFileNameWithoutExtension(path), file = relative, frequency = clip.frequency, channels = clip.channels, samples = clip.samples };
        catalog.songs.Add(entry); SaveCatalog(); return entry;
    }
    private void SaveCatalog() => File.WriteAllText(catalogPath, JsonUtility.ToJson(catalog, true));
    private static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    private static bool Valid(double bpm, double first, double length) => !double.IsNaN(bpm) && !double.IsInfinity(bpm) && bpm > 0 && !double.IsNaN(first) && !double.IsInfinity(first) && first >= 0 && first < length;
    private static bool WavHeader(string path)
    {
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            if (reader.BaseStream.Length < 12) return false;
            string riff = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)); reader.ReadUInt32();
            string wave = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            return riff == "RIFF" && wave == "WAVE";
        }
    }
    private void CheckWave(AudioClip clip, double bpm, double offset)
    {
        var pcm = new float[clip.samples * clip.channels]; bool read = clip.GetData(pcm, 0);
        var onsets = new List<int>();
        for (int i = 0; i < clip.samples; i++) if (pcm[i * clip.channels] > .5f && (i == 0 || pcm[(i - 1) * clip.channels] <= .5f)) onsets.Add(i);
        int expected = (int)Math.Ceiling(((double)clip.samples / clip.frequency - offset) * bpm / 60);
        double max = 0;
        for (int n = 0; n < onsets.Count; n++) max = Math.Max(max, Math.Abs((double)onsets[n] / clip.frequency - (offset + n * 60 / bpm)));
        Check("Decoded beat grid " + bpm + " / " + clip.frequency, read && onsets.Count == expected && max <= .51 / clip.frequency, $"onsets={onsets.Count}, maxErrorMs={max * 1000:G6}");
    }
    private void Check(string name, bool pass, string detail)
    {
        report.checks.Add(new CheckResult { name = name, pass = pass, detail = detail }); if (!pass) report.failures++;
        UnityEngine.Debug.Log((pass ? "PASS " : "FAIL ") + name + " " + detail);
    }
    private void OnGUI() { GUILayout.Label("Windows audio import research: " + report.status); GUILayout.Label("Checks: " + report.checks.Count + " / failures: " + report.failures); }
}
