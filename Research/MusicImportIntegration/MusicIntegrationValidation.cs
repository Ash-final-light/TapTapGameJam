using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TapTap.Systems;
using TapTap.Systems.Music;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MusicIntegrationValidation : MonoBehaviour
{
    private string output, inputs;
    private readonly List<string> report = new();
    private SystemRoot root;
    private void Start()
    {
        output = Arg("-resultRoot"); inputs = Arg("-inputRoot"); Directory.CreateDirectory(output);
        StartCoroutine(Guard(Test()));
    }
    private string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    private void Check(bool passed, string name)
    {
        if (!passed) throw new Exception(name);
        report.Add("PASS " + name); File.WriteAllLines(Path.Combine(output, "validation.txt"), report); Debug.Log("PASS " + name);
    }
    private IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(test);
        while (stack.Count > 0)
        {
            var item = stack.Peek(); bool next; object yielded = null;
            try { next = item.MoveNext(); if (next) yielded = item.Current; }
            catch (Exception e) { report.Add("FAIL " + e); File.WriteAllLines(Path.Combine(output, "validation.txt"), report); Debug.LogException(e); Application.Quit(1); yield break; }
            if (!next) { stack.Pop(); (item as IDisposable)?.Dispose(); continue; }
            if (yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
        }
        report.Add("ALL PASS"); File.WriteAllLines(Path.Combine(output, "validation.txt"), report); Application.Quit(0);
    }
    private IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.2f);
        Debug.developerConsoleVisible = false;
        yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null;
    }
    private IEnumerator WaitReady()
    {
        float until = Time.realtimeSinceStartup + 30;
        while (!root.UI.Alignment.PlayButton.interactable && Time.realtimeSinceStartup < until) yield return null;
        Check(root.UI.Alignment.PlayButton.interactable, "audio decoded and waveform available");
    }
    private IEnumerator Test()
    {
        yield return null; root = SystemRoot.Instance;
        Check(root && root.Initialized && root.UI.Menu.MusicButton, "main menu integration initialized");
        Screen.SetResolution(1280, 720, false); yield return new WaitForSecondsRealtime(.3f);
        if (Arg("-restart") != null)
        {
            Check(root.Music.Songs.Length == 2, "separate player restart restores two songs");
            var restored = root.Music.Songs.Single(s => s.Title == "我的校准曲");
            Check(restored.Aligned && restored.Bpm == 137.5 && Math.Abs(restored.FirstBeat - .123) < .000001, "restart retains decimal BPM and first beat");
            root.UI.OpenAudioAlignment(restored); yield return WaitReady();
            Check(root.UI.Alignment.BpmInput.text == "137.5", "restored alignment appears in input field");
            yield return Capture("restart-alignment"); yield break;
        }
        Check(root.Music.Songs.Length == 0, "isolated profile begins empty");
        yield return Capture("main-menu");
        root.UI.Menu.MusicButton.onClick.Invoke();
        Check(root.UI.Context == UIContext.ImportedMusic && !root.UI.Menu.gameObject.activeSelf, "music entry opens independent list");
        yield return Capture("empty-library");
        string originalDirectory = Directory.GetCurrentDirectory();
        var pickerCancel = WindowsMusicPicker.Pick(); AutoDialog(null);
        yield return WaitPicker(pickerCancel);
        Check(pickerCancel.Result.Error == null && pickerCancel.Result.Path == null && root.Music.Songs.Length == 0, "native picker cancellation keeps library unchanged");
        string wav = Directory.GetFiles(inputs, "*137.5bpm 48000.wav").Single();
        var picker = WindowsMusicPicker.Pick(); AutoDialog(wav);
        yield return WaitPicker(picker);
        Check(picker.Result.Error == null && string.Equals(Path.GetFullPath(picker.Result.Path ?? ""), Path.GetFullPath(wav), StringComparison.OrdinalIgnoreCase), "native Windows picker returns Unicode and special-character path");
        Check(Directory.GetCurrentDirectory() == originalDirectory, "picker preserves working directory");
        yield return root.UI.ImportedMusic.ImportPath(picker.Result.Path);
        Check(root.Music.Songs.Length == 1 && root.UI.Context == UIContext.AudioAlignment, "WAV import saves song then opens alignment");
        yield return WaitReady(); yield return Capture("alignment-loaded");
        var song = root.Music.Songs.Single();
        root.UI.Alignment.Back.onClick.Invoke();
        Check(root.UI.Context == UIContext.ImportedMusic && !root.Music.Find(song.Id).Aligned, "align later retains pending song");
        Check(root.UI.Alignment.Preview.Clip == null, "leaving alignment releases decoded clip");
        yield return Capture("pending-library");
        root.UI.OpenAudioAlignment(song); yield return WaitReady();
        var alignment = root.UI.Alignment;
        yield return new WaitForSecondsRealtime(.2f);
        Check(alignment.Waveform.Peaks != null && alignment.Waveform.Peaks.Any(p => p > .1f), "waveform contains decoded sample peaks");
        var mesh = alignment.Waveform.canvasRenderer.GetMesh();
        Check(mesh && mesh.vertexCount > 0 && !string.IsNullOrEmpty(alignment.WindowLabel.text), "waveform vertices and time window render");
        var rect = alignment.Waveform.rectTransform;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMin + rect.rect.width / 3, 0)));
        ExecuteEvents.Execute(alignment.Waveform.gameObject, new PointerEventData(root.UI.EventSystem) { position = point }, ExecuteEvents.pointerClickHandler);
        Check(double.TryParse(alignment.OffsetInput.text, out double marked) && Math.Abs(marked - 5) < .001, "pointer click on waveform marks first beat");
        alignment.Zoom.onClick.Invoke(); yield return null;
        Check(Math.Abs(alignment.Waveform.WindowEnd - alignment.Waveform.WindowStart - 20) < .001, "full-song waveform zoom updates window");
        alignment.Zoom.onClick.Invoke();
        alignment.BpmInput.text = "137.5"; alignment.OffsetInput.text = "0.123";
        alignment.Metronome.isOn = true; alignment.PlayButton.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.4f);
        Check(alignment.Preview.Playing && alignment.Preview.Position > .1 && alignment.Preview.Metronome, "scheduled music and metronome preview advances");
        alignment.Seek.value = 5; yield return new WaitForSecondsRealtime(.15f);
        Check(alignment.Preview.Position >= 5 && alignment.Preview.Position < 5.3, "seek establishes new playback origin");
        alignment.PlayButton.onClick.Invoke(); double pause = alignment.Preview.Position;
        yield return new WaitForSecondsRealtime(.1f);
        Check(!alignment.Preview.Playing && Math.Abs(alignment.Preview.Position - pause) < .001, "pause holds playback position");
        alignment.BpmInput.text = "NaN"; Check(!alignment.Save.interactable, "invalid BPM cannot be saved");
        alignment.BpmInput.text = "137.5"; alignment.OffsetInput.text = "20"; Check(!alignment.Save.interactable, "first beat at duration cannot be saved");
        alignment.OffsetInput.text = "0.123";
        alignment.Save.onClick.Invoke(); song = root.Music.Find(song.Id);
        Check(song.Aligned && song.Bpm == 137.5 && Math.Abs(song.FirstBeat - .123) < .000001, "save commits decimal BPM and first beat");
        root.UI.OpenAudioAlignment(song); yield return WaitReady();
        alignment.BpmInput.text = "180"; alignment.Back.onClick.Invoke();
        Check(root.UI.Dialog.gameObject.activeSelf, "dirty alignment requests discard confirmation");
        yield return Capture("discard-confirmation");
        root.UI.Dialog.Cancel(); Check(root.UI.Context == UIContext.AudioAlignment && alignment.BpmInput.text == "180", "cancel discard keeps editable draft");
        alignment.Back.onClick.Invoke(); root.UI.Dialog.ConfirmButton.onClick.Invoke();
        Check(root.UI.Context == UIContext.ImportedMusic && root.Music.Find(song.Id).Bpm == 137.5, "discard leaves saved alignment intact");
        root.UI.ImportedMusic.NameInput.text = "我的校准曲"; root.UI.ImportedMusic.RenameButton.onClick.Invoke();
        Check(root.Music.Find(song.Id).Title == "我的校准曲", "rename persists song title");
        var duplicate = Path.Combine(output, "改名后的同一曲.wav"); File.Copy(wav, duplicate, true);
        yield return root.UI.ImportedMusic.ImportPath(duplicate); yield return WaitReady(); root.UI.Alignment.Back.onClick.Invoke();
        Check(root.Music.Songs.Length == 1 && root.Music.Find(song.Id).Title == "我的校准曲" && root.Music.Find(song.Id).Aligned, "content duplicate preserves name and alignment");
        int count = root.Music.Songs.Length;
        yield return root.UI.ImportedMusic.ImportPath(Path.Combine(inputs, "损坏 文件.wav"));
        Check(root.Music.Songs.Length == count && root.UI.Context == UIContext.ImportedMusic, "damaged WAV rejected without partial entry");
        var validHeader = Path.Combine(output, "假音频.wav"); File.WriteAllBytes(validHeader, Encoding.ASCII.GetBytes("RIFFxxxxWAVEgarbage data"));
        yield return root.UI.ImportedMusic.ImportPath(validHeader);
        Check(root.Music.Songs.Length == count, "header-only WAV rejected by decode validation");
        string mp3 = Directory.GetFiles(inputs, "*90s*.mp3").Single();
        yield return root.UI.ImportedMusic.ImportPath(mp3); yield return WaitReady(); root.UI.Alignment.Back.onClick.Invoke();
        Check(root.Music.Songs.Length == 2, "MP3 import and waveform supported");
        Check(new ImportedMusicLibrary(root.Music.DirectoryPath).Songs.Length == 2, "catalog reload reads saved songs");
        Check(root.Saves.NewGame().Success && new ImportedMusicLibrary(root.Music.DirectoryPath).Songs.Length == 2, "new game keeps imported library independent of progress");
        string copy = root.Music.PathFor(song); File.Delete(copy);
        yield return root.UI.ImportedMusic.ImportPath(wav); yield return WaitReady(); root.UI.Alignment.Back.onClick.Invoke();
        Check(File.Exists(copy) && root.Music.Find(song.Id).Aligned, "reimport repairs missing copy and retains settings");
        var scrollSongs = new List<string>();
        for (int i = 0; i < 9; i++)
        {
            var bytes = File.ReadAllBytes(wav); bytes[bytes.Length - 1] = (byte)(i + 1);
            var path = Path.Combine(output, "列表测试 " + i + ".wav"); File.WriteAllBytes(path, bytes);
            yield return root.UI.ImportedMusic.ImportPath(path); yield return WaitReady(); root.UI.Alignment.Back.onClick.Invoke();
            scrollSongs.Add(root.Music.Songs.Last().Id);
        }
        Canvas.ForceUpdateCanvases(); yield return null;
        var scroll = root.UI.ImportedMusic.Content.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        Check(scroll.content.rect.height > scroll.viewport.rect.height, "library scroll content exceeds viewport");
        scroll.verticalNormalizedPosition = 0; yield return Capture("scrolling-library");
        foreach (string id in scrollSongs) Check(root.Music.Remove(id).Success, "remove test song " + id.Substring(0, 8));
        root.UI.ImportedMusic.Refresh();
        root.UI.ImportedMusic.PreviewButton.onClick.Invoke(); yield return new WaitForSecondsRealtime(.6f);
        Check(root.UI.ImportedMusic.Preview.Playing, "library preview plays selected song");
        root.UI.ImportedMusic.Back.onClick.Invoke();
        Check(!root.UI.ImportedMusic.Preview.Playing && !root.UI.ImportedMusic.Preview.Clip, "leaving library stops and releases preview");
        Check(root.UI.Context == UIContext.Title && root.UI.Menu.StartButton.gameObject.activeInHierarchy, "returns to existing main menu");
        root.UI.OpenSongSelection(); Check(root.UI.Songs.gameObject.activeSelf && !root.UI.ImportedMusic.gameObject.activeSelf, "existing song selection still opens");
        root.UI.ShowContext(UIContext.Title); root.UI.OpenImportedMusic(); yield return Capture("saved-library");
        Check(!Directory.GetFiles(root.Music.DirectoryPath, "*.importing*").Any(), "no staging files remain after import outcomes");
        string blocked = Path.Combine(output, "blocked-library"); Directory.CreateDirectory(blocked);
        string badCatalog = "{\"Version\":99,\"Songs\":[]}"; File.WriteAllText(Path.Combine(blocked, ImportedMusicLibrary.CatalogFile), badCatalog);
        var badLibrary = new ImportedMusicLibrary(blocked);
        Check(badLibrary.Error != null && !badLibrary.Add(song).Success && File.ReadAllText(Path.Combine(blocked, ImportedMusicLibrary.CatalogFile)) == badCatalog, "invalid catalog is preserved and cannot be overwritten");
        File.WriteAllText(Path.Combine(blocked, ImportedMusicLibrary.CatalogFile), "{}");
        Check(new ImportedMusicLibrary(blocked).Error != null, "empty JSON object is rejected as a corrupt catalog");
        Check(!root.Music.SaveAlignment(song.Id, double.PositiveInfinity, 0).Success && !root.Music.SaveAlignment(song.Id, 120, -1).Success, "library rejects non-finite BPM and negative offset");
    }
    private IEnumerator WaitPicker(Task<WindowsMusicPicker.Result> task)
    {
        float deadline = Time.realtimeSinceStartup + 20;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Check(task.IsCompleted, "native picker completes");
    }
    private static void AutoDialog(string path)
    {
        uint processId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                IntPtr dialog = IntPtr.Zero;
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out uint pid); var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                    if (pid == processId && title.ToString() == "导入歌曲") dialog = window; return true;
                }, IntPtr.Zero);
                if (dialog != IntPtr.Zero)
                {
                    System.Threading.Thread.Sleep(400);
                    if (path != null)
                    {
                        // On this Windows version the filename is an Edit child of
                        // ComboBoxEx32 (cmb13), rather than the legacy edt1 control.
                        IntPtr edit = IntPtr.Zero;
                        EnumChildWindows(dialog, (child, _) =>
                        {
                            var kind = new StringBuilder(128); GetClassName(child, kind, kind.Capacity);
                            if (GetDlgCtrlID(child) == 1148 && kind.ToString() == "Edit") edit = child;
                            return true;
                        }, IntPtr.Zero);
                        if (edit != IntPtr.Zero) SendMessage(edit, 0x000c, IntPtr.Zero, path);
                    }
                    PostMessage(dialog, 0x111, new IntPtr(path == null ? 2 : 1), IntPtr.Zero); return;
                }
                System.Threading.Thread.Sleep(100);
            }
        });
    }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, string lparam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
