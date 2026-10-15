using System;
using System.Collections;
using System.Globalization;
using TMPro;
using TapTap.Systems.Music;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class AudioAlignmentView : MonoBehaviour
    {
        public TMP_Text Heading, Status, WindowLabel, MetronomeFeedback;
        public TMP_InputField BpmInput, OffsetInput;
        public UnityEngine.UI.Button Save, Back, PlayButton, Mark, Zoom;
        public UnityEngine.UI.Toggle Metronome;
        public UnityEngine.UI.Slider Seek;
        public AudioWaveformGraphic Waveform;
        public MusicPreview Preview;
        private SystemRoot root;
        private ImportedSong song;
        private int generation;
        private bool loaded, dirty, fullWave;
        private string originalBpm, originalOffset;
        public bool HasUnsavedChanges => dirty;
        public void Bind(SystemRoot owner)
        {
            root = owner; Preview.Bind(root.Audio);
            BpmInput.onValueChanged.AddListener(_ => Changed()); OffsetInput.onValueChanged.AddListener(_ => Changed());
            Metronome.onValueChanged.AddListener(enabled =>
            {
                ConfigurePreview();
                if (enabled && Preview.Metronome && !Preview.Playing) Preview.Play();
            });
            PlayButton.onClick.AddListener(() => { if (Preview.Playing) Preview.Pause(); else Preview.Play(); });
            Seek.onValueChanged.AddListener(value => Preview.Seek(value));
            Mark.onClick.AddListener(() => SetFirstBeat(Preview.Position));
            Waveform.Clicked = SetFirstBeat;
            Zoom.onClick.AddListener(() => { fullWave = !fullWave; Zoom.GetComponentInChildren<TMP_Text>().text = fullWave ? "查看 15 秒" : "查看全曲"; });
            Save.onClick.AddListener(SaveDraft); Back.onClick.AddListener(RequestClose);
        }
        public void Open(ImportedSong item)
        {
            song = item.Copy(); loaded = dirty = fullWave = false; Preview.Clear(); Waveform.Peaks = null;
            Waveform.Duration = song.Duration; Waveform.WindowStart = 0; Waveform.WindowEnd = Math.Min(15, song.Duration); Waveform.Refresh();
            originalBpm = (song.Aligned ? song.Bpm : 120).ToString("0.######", CultureInfo.InvariantCulture);
            originalOffset = (song.Aligned ? song.FirstBeat : 0).ToString("0.######", CultureInfo.InvariantCulture);
            BpmInput.SetTextWithoutNotify(originalBpm); OffsetInput.SetTextWithoutNotify(originalOffset);
            Heading.text = "歌曲对齐 · " + song.Title; Status.text = "正在加载音频和波形……";
            Metronome.SetIsOnWithoutNotify(false); Zoom.GetComponentInChildren<TMP_Text>().text = "查看全曲";
            Seek.minValue = 0; Seek.maxValue = (float)song.Duration; Seek.SetValueWithoutNotify(0);
            Save.interactable = PlayButton.interactable = Mark.interactable = Seek.interactable = false;
            int current = ++generation;
            root.StartCoroutine(ImportedAudioLoader.Load(root.Music.PathFor(song), (clip, error) =>
            {
                if (!this || !gameObject.activeInHierarchy || current != generation) { if (clip) Destroy(clip); return; }
                if (error != null) { Status.text = error; return; }
                Preview.SetClip(clip); root.StartCoroutine(BuildWaveform(clip, current));
            }));
        }
        private IEnumerator BuildWaveform(AudioClip clip, int current)
        {
            var peaks = new float[4096]; const int frames = 4096; var buffer = new float[frames * clip.channels];
            for (int start = 0, batch = 0; start < clip.samples; start += frames)
            {
                if (!this || current != generation || !clip) yield break;
                if (!clip.GetData(buffer, start)) { Status.text = "无法读取音频波形，请尝试转换为 PCM WAV 后重新导入。"; yield break; }
                int count = Math.Min(frames, clip.samples - start);
                for (int i = 0; i < count; i++)
                {
                    int bucket = (int)((long)(start + i) * peaks.Length / clip.samples);
                    for (int channel = 0; channel < clip.channels; channel++) peaks[bucket] = Mathf.Max(peaks[bucket], Mathf.Abs(buffer[i * clip.channels + channel]));
                }
                if (++batch % 16 == 0) yield return null;
            }
            Waveform.Peaks = peaks; Waveform.Refresh(); loaded = true; PlayButton.interactable = Mark.interactable = Seek.interactable = true;
            ConfigurePreview();
        }
        private bool Parameters(out double bpm, out double offset) =>
            double.TryParse(BpmInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm) &
            double.TryParse(OffsetInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
        private void Changed()
        {
            dirty = BpmInput.text != originalBpm || OffsetInput.text != originalOffset;
            ConfigurePreview();
        }
        private void ConfigurePreview()
        {
            bool valid = Parameters(out double bpm, out double offset) && ImportedMusicLibrary.ValidAlignment(bpm, offset, song?.Duration ?? 0);
            Save.interactable = loaded && valid;
            Preview.Configure(valid ? bpm : 120, valid ? offset : 0, loaded && valid && Metronome.isOn);
            Waveform.Bpm = valid ? bpm : 0; Waveform.FirstBeat = valid ? offset : 0;
            if (loaded) Status.text = valid ? "点击波形标首拍 · 金线为首拍，白线为播放位置 · " + (dirty ? "有未保存的修改" : "试听后点击保存") : "BPM 需在 30–300 之间，首拍需在音频范围内。";
            if (loaded && valid && Metronome.isOn)
                Status.text = root.Settings.Current.MasterVolume == 0 || root.Settings.Current.EffectsVolume == 0
                    ? "节拍器已开启，但总音量或音效音量为 0。请在设置中调高后试听。"
                    : "节拍器已开启：歌曲试听音量降低，橙色文字随每拍闪动。" + (dirty ? "有未保存的修改。" : "");
        }
        private void SetFirstBeat(double value)
        {
            if (!loaded) return;
            OffsetInput.text = value.ToString("0.000000", CultureInfo.InvariantCulture);
        }
        private void SaveDraft()
        {
            if (!loaded || !Parameters(out double bpm, out double offset)) return;
            var result = root.Music.SaveAlignment(song.Id, bpm, offset);
            if (!result.Success) { Status.text = result.Error; return; }
            dirty = false; root.UI.Toast("BPM 和首拍已保存。"); root.UI.OpenImportedMusic();
        }
        public void RequestClose()
        {
            Preview.Pause();
            if (dirty) root.UI.Dialog.Show("放弃本次未保存的 BPM 和首拍设置？已导入的歌曲会保留。", () => root.UI.OpenImportedMusic());
            else root.UI.OpenImportedMusic();
        }
        private void Update()
        {
            if (!loaded) return;
            double position = Preview.Position; Seek.SetValueWithoutNotify((float)position); Waveform.Position = position;
            double span = fullWave ? song.Duration : Math.Min(15, song.Duration);
            Waveform.WindowStart = Math.Max(0, Math.Min(song.Duration - span, position - span / 2)); Waveform.WindowEnd = Waveform.WindowStart + span; Waveform.Refresh();
            WindowLabel.text = Waveform.WindowStart.ToString("0.00") + " 秒 — " + Waveform.WindowEnd.ToString("0.00") + " 秒    ·    播放 " + position.ToString("0.000") + " 秒 / " + ImportedMusicView.FormatTime(song.Duration);
            PlayButton.GetComponentInChildren<TMP_Text>().text = Preview.Playing ? "暂停" : "播放";
            if (MetronomeFeedback)
            {
                double beats = Preview.Metronome && Preview.Playing && position >= Waveform.FirstBeat
                    ? (position - Waveform.FirstBeat) * Waveform.Bpm / 60 : -1;
                bool pulse = beats >= 0 && (beats - Math.Floor(beats)) * 60 / Waveform.Bpm < .12;
                MetronomeFeedback.text = !Metronome.isOn ? "节拍器试听" : !Preview.Metronome ? "参数无效"
                    : !Preview.Playing ? "已开启 · 已暂停" : beats < 0 ? "等待首拍" : "● 第 " + ((long)Math.Floor(beats) + 1) + " 拍";
                MetronomeFeedback.color = pulse ? new Color(.9f, .3f, .04f) : new Color(.13f, .16f, .2f);
            }
        }
        private void OnDisable() { generation++; loaded = false; if (Preview) Preview.Clear(); }
    }
}
