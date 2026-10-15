using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using TapTap.Danmaku;
using TapTap.Systems;
using TapTap.Systems.Music;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Validation-only observer and separate, explicitly selected automated player checks.
// Manual mode only observes callbacks and gameplay: it never invokes UI actions.
public sealed class CustomSongPlayerProbe : MonoBehaviour
{
    private string output, input;
    private bool automatic, fire;
    private Mouse mouse;
    private readonly HashSet<int> hooked = new();
    private readonly List<string> events = new(), errors = new(), checks = new();
    private float nextSnapshot, maxSyncError, maxFrame;
    private string context;
    private int peakBullets;
    private readonly float[] audio = new float[512];
    private float audioPeak;
    private SystemRoot root;
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        output = Arg("-resultRoot"); input = Arg("-inputRoot"); automatic = Environment.GetCommandLineArgs().Contains("-auto");
        Directory.CreateDirectory(output); Application.logMessageReceived += Log;
        Write();
    }
    private static string Arg(string name)
    { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
    private void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        errors.Add(type + ": " + message + "\n" + stack); Write();
    }
    private void Record(string text) { events.Add(Time.realtimeSinceStartup.ToString("0.000") + " " + text); Write(); }
    private void Require(bool value, string description)
    { checks.Add((value ? "PASS " : "FAIL ") + description); Write(); if (!value) throw new InvalidOperationException(description); }
    private void Write()
    {
        File.WriteAllLines(Path.Combine(output, "events.txt"), events);
        File.WriteAllLines(Path.Combine(output, "errors.txt"), errors);
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
    }
    private IEnumerator Start()
    {
        while (!SystemRoot.Instance || !SystemRoot.Instance.Initialized) yield return null;
        root = SystemRoot.Instance;
        if (!automatic) yield break;
        mouse = InputSystem.AddDevice<Mouse>();
        var run = Automated();
        while (true)
        {
            bool more;
            try { more = run.MoveNext(); }
            catch (Exception error) { Log(error.Message, error.StackTrace, LogType.Exception); Application.Quit(1); yield break; }
            if (!more) break; yield return run.Current;
        }
        Require(errors.Count == 0, "no player errors"); Write(); Application.Quit(0);
    }
    private IEnumerator Wait(Func<bool> predicate, float timeout = 45)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!predicate()) throw new InvalidOperationException("Timed out in " + root.UI.Context);
        yield return null;
    }
    private IEnumerator Automated()
    {
        // Three isolated frequency inputs plus a real compressed music file.
        root.UI.OpenImportedMusic();
        foreach (var path in Directory.GetFiles(input).Where(p => p.EndsWith(".wav") || p.EndsWith(".mp3")).OrderBy(p => p))
        {
            yield return root.UI.ImportedMusic.ImportPath(path);
            string contentId;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                contentId = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            var song = root.Music.Find(contentId);
            Require(root.Music.SaveAlignment(song.Id, 120, .1).Success, "alignment " + song.Title);
            root.UI.OpenImportedMusic();
            bool done = false; OperationResult status = default;
            yield return root.CustomSongs.Prepare(song.Id, null, r => { done = true; status = r; });
            Require(done && status.Success, "runtime analysis " + song.Title + " " + status.Error);
            var session = root.CustomSongs.Prepared;
            Require(session.Sequence.Clips.All(c => c.ValidateKeyframes() == null), "valid timeline " + song.Title);
            if (song.Title == "high") Require(session.Sequence.Clips.Count > 0 && session.Sequence.Clips.All(c => c.Name.StartsWith("晶音")), "high input drives high attacks");
            if (song.Title == "low") Require(session.Sequence.Clips.Count > 0 && session.Sequence.Clips.All(c => c.Name == "重音扩散"), "low input drives low attacks");
            if (song.Title == "mid") Require(session.Sequence.Clips.Count > 0 && session.Sequence.Clips.All(c => c.Name == "旋律追射"), "mid input drives mid attacks");
            if (song.Title == "mixed_short") Require(session.Sequence.Clips.Any(c => c.Name == "重音扩散") && session.Sequence.Clips.Any(c => c.Name == "旋律追射") && session.Sequence.Clips.Any(c => c.Name.StartsWith("晶音")), "mixed input retains three attack families");
            Record("GENERATED " + song.Title + " low=" + session.Sequence.Clips.Count(c => c.Name == "重音扩散") +
                " mid=" + session.Sequence.Clips.Count(c => c.Name == "旋律追射") + " high=" + session.Sequence.Clips.Count(c => c.Name.StartsWith("晶音")));
            root.CustomSongs.ReleasePrepared();
        }
        var selected = root.Music.Songs.First(s => s.Title.Contains("mixed"));
        bool prepared = false;
        yield return root.CustomSongs.Prepare(selected.Id, null, r => prepared = r.Success);
        Require(prepared, "prepare battle");
        string recordId = root.CustomSongs.Prepared.RecordId;
        string timeline = Timeline(root.CustomSongs.Prepared.Sequence);
        Require(root.Scenes.StartRun(root.CustomSongs.Prepared.Setup).Success, "start custom scene");
        yield return Wait(() => FindObjectOfType<BattleManager>()?.Simulation != null);
        var manager = FindObjectOfType<BattleManager>(); fire = true;
        yield return new WaitForSecondsRealtime(1);
        root.UI.OpenPause(); int tick = manager.Simulation.Tick; double music = manager.MusicSongSeconds;
        yield return new WaitForSecondsRealtime(.6f);
        Require(manager.Simulation.Tick == tick && Math.Abs(manager.MusicSongSeconds - music) < .02, "pause freezes music and simulation");
        root.UI.Resume();
        yield return Wait(() => root.UI.Context == UIContext.Result, 30); fire = false;
        var result = root.LastResult;
        Require(result.IsCustom && result.Stats.Completed, "complete imported song");
        Require(result.Stats.BossHits > 0 && result.Stats.OnBeatHits > 0, "ordinary and on-beat hit scoring");
        Require(result.DropQuantity == 0 && root.Saves.Current.Items.Count == 0 && root.Saves.Record(recordId, SongDifficulty.Normal) != null, "record without item drops");
        Require(audioPeak > .001f && maxSyncError < .1f, "nonzero playback and sample/tick sync error=" + maxSyncError);
        root.UI.Result.Retry.onClick.Invoke();
        yield return Wait(() => root.UI.Context == UIContext.Gameplay && !root.Scenes.Busy);
        Require(timeline == Timeline(root.CustomSongs.Prepared.Sequence), "retry preserves timeline");
        yield return Wait(() => FindObjectOfType<BattleManager>()?.Simulation != null);
        manager = FindObjectOfType<BattleManager>();
        // Explicit failure-path check, independent of the survival/aiming run above.
        while (manager.Simulation.Player.Alive)
        { manager.Simulation.PlayerSystem.ReceiveDamage(FP64.One); yield return new WaitForSecondsRealtime(.55f); }
        yield return Wait(() => root.UI.Context == UIContext.Result);
        Require(!root.LastResult.Stats.Completed && root.LastResult.IsCustom, "death settles custom failure");
        root.UI.Result.Next.onClick.Invoke();
        yield return Wait(() => root.UI.Context == UIContext.ImportedMusic);
        Require(root.CustomSongs.Prepared == null, "return releases song resources");
        File.WriteAllText(Path.Combine(output, "metrics.json"), JsonUtility.ToJson(new Metrics { PeakBullets = peakBullets,
            MaxSyncErrorSeconds = maxSyncError, AudioPeak = audioPeak, MaxFrameSeconds = maxFrame, BossHits = result.Stats.BossHits, OnBeatHits = result.Stats.OnBeatHits }, true));
    }
    private static string Timeline(DanmakuSequence sequence) => string.Join("\n", sequence.Clips.Select(JsonUtility.ToJson));
    private void Update()
    {
        root = SystemRoot.Instance;
        if (!root || !root.Initialized) return;
        if (context != root.UI.Context.ToString()) { context = root.UI.Context.ToString(); Record("CONTEXT " + context); }
        var manager = FindObjectOfType<BattleManager>();
        if (automatic && fire && mouse != null && manager && manager.Simulation != null)
        {
            Vector3 point = manager.AimCamera.WorldToScreenPoint(manager.transform.TransformPoint((Vector3)manager.Config.BossPosition));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(point.x, point.y), buttons = 1 });
        }
        if (manager && manager.Simulation != null && manager.IsRunning && manager.MusicPlaying)
        {
            maxSyncError = Mathf.Max(maxSyncError, Mathf.Abs(manager.Simulation.Tick / 60f - (float)manager.MusicSongSeconds));
            peakBullets = Math.Max(peakBullets, manager.Simulation.World.Bullets.Count); maxFrame = Mathf.Max(maxFrame, Time.unscaledDeltaTime);
            manager.MusicSource.GetOutputData(audio, 0); foreach (var sample in audio) audioPeak = Mathf.Max(audioPeak, Mathf.Abs(sample));
        }
        if (Time.realtimeSinceStartup < nextSnapshot) return; nextSnapshot = Time.realtimeSinceStartup + .2f;
        foreach (var button in root.UI.GetComponentsInChildren<Button>(true))
        {
            if (!hooked.Add(button.GetInstanceID())) continue;
            var target = button; target.onClick.AddListener(() => Record("BUTTON " + target.name));
        }
        foreach (var field in root.UI.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (!hooked.Add(field.GetInstanceID())) continue;
            var target = field; target.onEndEdit.AddListener(value => Record("INPUT " + target.name + "=" + value));
        }
        File.WriteAllText(Path.Combine(output, "state.json"), JsonUtility.ToJson(new State {
            Context = context, ErrorCount = errors.Count, SongCount = root.Music.Songs.Length, Songs = root.Music.Songs,
            SaveDirectory = root.Music.DirectoryPath, Preparing = root.CustomSongs.Preparing, Ready = root.CustomSongs.Prepared != null,
            Status = root.UI.CustomSong.Status.text, Tick = manager?.Simulation?.Tick ?? 0, MusicSeconds = manager ? manager.MusicSongSeconds : 0,
            Playing = manager && manager.IsRunning, Paused = root.UI.IsPauseOpen, Health = manager?.Simulation?.Player.Health.ToFloat() ?? 0,
            Flying = manager?.Simulation?.PlayerSystem.Motor.Flying ?? false,
            Stamina = manager?.Simulation?.PlayerSystem.Stamina.ToFloat() ?? 0,
            PlayerX = manager?.Simulation?.Player.Pose.Position.X.ToFloat() ?? 0,
            PlayerY = manager?.Simulation?.Player.Pose.Position.Y.ToFloat() ?? 0,
            BossHits = manager?.Scores?.Stats.BossHits ?? 0, OnBeatHits = manager?.Scores?.Stats.OnBeatHits ?? 0,
            MaxSyncError = maxSyncError, AudioPeak = audioPeak, ResultScore = root.LastResult?.Score ?? 0,
            Completed = root.LastResult?.Stats.Completed ?? false, DropQuantity = root.LastResult?.DropQuantity ?? 0 }, true));
    }
    [Serializable] private sealed class Metrics { public int PeakBullets, BossHits, OnBeatHits; public float MaxSyncErrorSeconds, AudioPeak, MaxFrameSeconds; }
    [Serializable] private sealed class State
    {
        public string Context, Status, SaveDirectory; public ImportedSong[] Songs;
        public int ErrorCount, SongCount, Tick, BossHits, OnBeatHits, ResultScore, DropQuantity;
        public bool Preparing, Ready, Playing, Paused, Completed, Flying; public double MusicSeconds; public float Health, Stamina, PlayerX, PlayerY, MaxSyncError, AudioPeak;
    }
    private void OnDestroy()
    { Application.logMessageReceived -= Log; if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); }
}
