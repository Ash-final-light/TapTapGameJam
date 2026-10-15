using System;
using System.Collections;
using System.IO;
using TMPro;
using TapTap.Systems.Music;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class ImportedMusicView : MonoBehaviour
    {
        public UnityEngine.UI.Button Import, Back, PreviewButton, Align, RenameButton, Remove, Challenge;
        public UnityEngine.UI.Button RowTemplate;
        public RectTransform Content;
        public TMP_Text Status, Details;
        public TMP_InputField NameInput;
        public MusicPreview Preview;
        private SystemRoot root;
        private string selectedId;
        private bool busy;
        private int loadGeneration;
        public void Bind(SystemRoot owner)
        {
            root = owner; Preview.Bind(root.Audio);
            Challenge.onClick.AddListener(() => { if (root.Music.Find(selectedId) is ImportedSong song) root.UI.OpenCustomSong(song); });
            Import.onClick.AddListener(() => { if (!busy) root.StartCoroutine(PickAndImport()); });
            Back.onClick.AddListener(() => root.UI.ShowContext(UIContext.Title));
            Align.onClick.AddListener(() => { var song = root.Music.Find(selectedId); if (song != null) root.UI.OpenAudioAlignment(song); });
            PreviewButton.onClick.AddListener(PreviewSelected);
            RenameButton.onClick.AddListener(() => { var result = root.Music.Rename(selectedId, NameInput.text); root.UI.Toast(result.Success ? "歌曲名称已保存。" : result.Error); Refresh(); });
            Remove.onClick.AddListener(() =>
            {
                var song = root.Music.Find(selectedId); if (song == null) return;
                root.UI.Dialog.Show("从歌曲库移除“" + song.Title + "”？原始音频文件将保留。", () =>
                {
                    loadGeneration++; Preview.Clear(); var result = root.Music.Remove(song.Id);
                    if (!result.Success) root.UI.Toast(result.Error); else selectedId = null;
                    Refresh();
                });
            });
        }
        public void Refresh()
        {
            foreach (Transform child in Content) if (child.gameObject != RowTemplate.gameObject) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var songs = root.Music.Songs;
            if (root.Music.Find(selectedId) == null) selectedId = songs.Length > 0 ? songs[0].Id : null;
            foreach (var song in songs)
            {
                var row = Instantiate(RowTemplate, Content); row.name = "ImportedSong_" + song.Id.Substring(0, 8);
                row.GetComponentInChildren<TMP_Text>().text = song.Title + "\n" + (song.Aligned ? song.Bpm.ToString("0.##") + " BPM · 已设置对齐" : "待对齐") + " · " + FormatTime(song.Duration);
                row.GetComponent<UnityEngine.UI.Image>().color = song.Id == selectedId ? new Color(.05f, .46f, .63f) : new Color(.18f, .25f, .32f);
                string id = song.Id; row.onClick.AddListener(() => SelectSong(id)); row.gameObject.SetActive(true);
            }
            Import.interactable = !busy && root.Music.Error == null;
            Status.text = root.Music.Error ?? (busy ? "正在导入，请稍候……" : songs.Length == 0 ? "还没有导入歌曲。点击右上方导入第一首。" : "共 " + songs.Length + " 首 · 音频已保存到本地歌曲库");
            RenderSelection();
        }
        private void SelectSong(string id) { loadGeneration++; Preview.Clear(); selectedId = id; Refresh(); }
        private void RenderSelection()
        {
            var song = root.Music.Find(selectedId); bool valid = song != null;
            bool exists = valid && File.Exists(root.Music.PathFor(song));
            Align.interactable = PreviewButton.interactable = exists && !busy;
            Challenge.interactable = exists && valid && song.Aligned && !busy;
            RenameButton.interactable = Remove.interactable = NameInput.interactable = valid && !busy;
            NameInput.SetTextWithoutNotify(song?.Title ?? "");
            Details.text = song == null ? "选择一首歌曲，开始试听或对齐。" : song.Title + "\n\n" +
                FormatTime(song.Duration) + " · " + song.Frequency + " Hz · " + song.Channels + " 声道\n\n" +
                (song.Aligned ? "BPM  " + song.Bpm.ToString("0.###") + "\n首拍  " + song.FirstBeat.ToString("0.000") + " 秒\n已对齐，可进入挑战" : "尚未设置 BPM 和首拍。\n保存对齐后即可挑战。") +
                (exists ? "" : "\n\n音频副本缺失，请重新导入同一文件以修复。");
            PreviewButton.GetComponentInChildren<TMP_Text>().text = Preview.Playing ? "暂停试听" : "试听";
        }
        private IEnumerator PickAndImport()
        {
            busy = true; Refresh(); loadGeneration++; Preview.Clear();
            var picking = WindowsMusicPicker.Pick();
            while (!picking.IsCompleted) yield return null;
            var chosen = picking.Result;
            if (chosen.Error != null || string.IsNullOrEmpty(chosen.Path))
            { busy = false; if (chosen.Error != null) root.UI.Toast(chosen.Error); if (gameObject.activeInHierarchy) Refresh(); yield break; }
            yield return ImportPath(chosen.Path);
        }
        // A path entry point also lets the Windows player validation exercise the real import flow.
        public IEnumerator ImportPath(string path)
        {
            busy = true; if (gameObject.activeInHierarchy) Refresh();
            var staging = root.Music.StageAsync(path);
            while (!staging.IsCompleted) yield return null;
            if (staging.IsFaulted || staging.IsCanceled)
            { FinishImport(null, staging.Exception?.GetBaseException().Message ?? "导入已取消。"); yield break; }
            var file = staging.Result; AudioClip clip = null; string error = null; ImportedSong song = null;
            try
            {
                // Keep the extension for Unity's decoder while retaining a private staging filename.
                string decodingPath = file.Path + file.Extension;
                try { File.Move(file.Path, decodingPath); file.Path = decodingPath; }
                catch (Exception e) { error = e.Message; }
                if (error == null) yield return ImportedAudioLoader.Load(file.Path, (loaded, failure) => { clip = loaded; error = failure; });
                if (error == null)
                {
                    var result = root.Music.CompleteImport(file, clip, out song); if (!result.Success) error = result.Error;
                }
            }
            finally
            {
                if (clip) Destroy(clip);
                try { File.Delete(file.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            FinishImport(song, error);
        }
        private void FinishImport(ImportedSong song, string error)
        {
            busy = false;
            if (error != null) root.UI.Toast(error);
            else { selectedId = song.Id; root.UI.Toast("歌曲已导入，可现在对齐，也可稍后设置。"); }
            if (!gameObject.activeInHierarchy) return;
            Refresh(); if (song != null) root.UI.OpenAudioAlignment(song);
        }
        private void PreviewSelected()
        {
            if (Preview.Playing) { Preview.Pause(); RenderSelection(); return; }
            if (Preview.Clip) { Preview.Play(); RenderSelection(); return; }
            var song = root.Music.Find(selectedId); if (song == null) return;
            int generation = ++loadGeneration; PreviewButton.interactable = false;
            root.StartCoroutine(ImportedAudioLoader.Load(root.Music.PathFor(song), (clip, error) =>
            {
                if (!this || !gameObject.activeInHierarchy || generation != loadGeneration) { if (clip) Destroy(clip); return; }
                if (error != null) root.UI.Toast(error);
                else { Preview.SetClip(clip); Preview.Play(); }
                RenderSelection();
            }));
        }
        private void Update()
        {
            if (Preview && PreviewButton) PreviewButton.GetComponentInChildren<TMP_Text>().text = Preview.Playing ? "暂停试听" : "试听";
        }
        private void OnDisable() { loadGeneration++; if (Preview) Preview.Clear(); }
        public static string FormatTime(double value) => ((int)value / 60).ToString("00") + ":" + ((int)value % 60).ToString("00");
    }
}
