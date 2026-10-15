using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TapTap.Systems;
using TapTap.Systems.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Validation player only: observe real Windows input, never invoke a UI callback.
[DefaultExecutionOrder(-1100)]
public sealed class MusicUIInteractionProbe : MonoBehaviour
{
    private string output;
    private float nextSnapshot;
    private readonly List<string> events = new();
    private readonly List<string> errors = new();
    private readonly HashSet<int> hooked = new();
    private void Awake()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("-manualUI")) { enabled = false; return; }
        int index = Array.IndexOf(args, "-resultRoot");
        output = args[index + 1]; Directory.CreateDirectory(output);
        Application.logMessageReceived += Log;
        events.Add("START real Windows UI input; no callbacks invoked by probe");
        Write();
    }
    private void Log(string message, string trace, LogType kind)
    {
        if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert) return;
        errors.Add(kind + ": " + message + "\n" + trace); Write();
    }
    private void Record(string action)
    {
        events.Add(Time.realtimeSinceStartup.ToString("0.000") + " " + action); Write();
    }
    private void Update()
    {
        var root = SystemRoot.Instance;
        if (!root || !root.Initialized || Time.realtimeSinceStartup < nextSnapshot) return;
        nextSnapshot = Time.realtimeSinceStartup + .2f;
        foreach (var button in root.UI.GetComponentsInChildren<Button>(true))
        {
            if (!hooked.Add(button.GetInstanceID())) continue;
            var target = button;
            button.onClick.AddListener(() => Record("BUTTON " + target.name));
        }
        foreach (var toggle in root.UI.GetComponentsInChildren<Toggle>(true))
        {
            if (!hooked.Add(toggle.GetInstanceID())) continue;
            var target = toggle;
            toggle.onValueChanged.AddListener(value => Record("TOGGLE " + target.name + "=" + value));
        }
        foreach (var slider in root.UI.GetComponentsInChildren<Slider>(true))
        {
            if (!hooked.Add(slider.GetInstanceID())) continue;
            var target = slider;
            slider.onValueChanged.AddListener(value => Record("SLIDER " + target.name + "=" + value));
        }
        foreach (var input in root.UI.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (!hooked.Add(input.GetInstanceID())) continue;
            var target = input;
            input.onEndEdit.AddListener(value => Record("INPUT " + target.name + "=" + value));
        }
        var alignment = root.UI.Alignment;
        var missing = root.UI.GetComponentsInChildren<Graphic>(true)
            .Where(graphic => !graphic.GetComponent<CanvasRenderer>()).Select(graphic => graphic.name).ToArray();
        var snapshot = new
        {
            context = root.UI.Context.ToString(), errorCount = errors.Count,
            songs = root.Music.Songs.Select(song => new { song.Title, song.Bpm, song.FirstBeat, song.Aligned }),
            status = root.UI.Context == UIContext.AudioAlignment ? alignment.Status.text : root.UI.ImportedMusic.Status.text,
            dialog = root.UI.Dialog.gameObject.activeSelf,
            waveformRenderer = alignment.Waveform.GetComponent<CanvasRenderer>() != null,
            missingRenderers = missing, alignmentPlaying = alignment.Preview.Playing,
            libraryPlaying = root.UI.ImportedMusic.Preview.Playing,
            bpm = alignment.BpmInput.text, firstBeat = alignment.OffsetInput.text,
            metronome = alignment.Metronome.isOn, position = alignment.Preview.Position,
            buttons = root.UI.GetComponentsInChildren<Button>().Where(button => button.IsInteractable()).Select(button => button.name)
        };
        File.WriteAllText(Path.Combine(output, "state.json"), JsonUtility.ToJson(new State
        {
            Context = snapshot.context, ErrorCount = snapshot.errorCount, SongCount = root.Music.Songs.Length,
            Status = snapshot.status, Dialog = snapshot.dialog, WaveformRenderer = snapshot.waveformRenderer,
            MissingRenderers = missing, AlignmentPlaying = snapshot.alignmentPlaying, LibraryPlaying = snapshot.libraryPlaying,
            Bpm = snapshot.bpm, FirstBeat = snapshot.firstBeat, Metronome = snapshot.metronome, Position = snapshot.position,
            Buttons = snapshot.buttons.ToArray(), Songs = root.Music.Songs,
            MetronomeFeedback = alignment.MetronomeFeedback ? alignment.MetronomeFeedback.text : "",
            PreviewMusicVolume = alignment.Preview.GetComponentsInChildren<AudioSource>().FirstOrDefault(source => source.name == "ImportedSongPreview")?.volume ?? 0,
            ClickVolume = alignment.Preview.GetComponentsInChildren<AudioSource>().FirstOrDefault(source => source.name == "AlignmentClick0")?.volume ?? 0
        }, true));
    }
    [Serializable] private sealed class State
    {
        public string Context, Status, Bpm, FirstBeat, MetronomeFeedback;
        public float PreviewMusicVolume, ClickVolume;
        public int ErrorCount, SongCount;
        public bool Dialog, WaveformRenderer, AlignmentPlaying, LibraryPlaying, Metronome;
        public double Position;
        public string[] MissingRenderers, Buttons;
        public TapTap.Systems.Music.ImportedSong[] Songs;
    }
    private void Write()
    {
        File.WriteAllLines(Path.Combine(output, "interaction-events.txt"), events);
        File.WriteAllLines(Path.Combine(output, "interaction-errors.txt"), errors);
    }
    private void OnDestroy()
    {
        if (output == null) return;
        Application.logMessageReceived -= Log; Record("END");
    }
}
