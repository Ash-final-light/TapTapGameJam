using System.Linq;
using TapTap.Danmaku;
using TapTap.Systems.Music;
using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class CustomSongPrepareView : MonoBehaviour
    {
        public UnityEngine.UI.Button Back, Generate, StartBattle;
        public UnityEngine.UI.Slider Progress;
        public TMP_Text SongDetails, Status;
        private SystemRoot root;
        private string songId;
        public void Bind(SystemRoot owner)
        {
            root = owner; Back.onClick.AddListener(Close); Generate.onClick.AddListener(Prepare);
            StartBattle.onClick.AddListener(() =>
            {
                var setup = root.CustomSongs.Prepared?.Setup;
                var result = root.Scenes.StartRun(setup);
                if (!result.Success) { Status.text = result.Error; root.UI.Toast(result.Error); }
            });
        }
        public void Open(ImportedSong song)
        {
            root.CustomSongs.Cancel(); root.CustomSongs.ReleasePrepared(); songId = song.Id;
            SongDetails.text = song.Title + "\n" + ImportedMusicView.FormatTime(song.Duration) + "    " + song.Bpm.ToString("0.###") + " BPM    首拍 " + song.FirstBeat.ToString("0.000") + " 秒";
            Progress.value = 0; StartBattle.interactable = false;
            var error = root.CustomSongs.ValidateSong(songId); Generate.interactable = error == null;
            var best = root.Saves.Record(CustomSongChallengeService.RecordKey(song), SongDifficulty.Normal);
            Status.text = error ?? (best == null ? "准备好后，分析音乐并生成专属弹幕。" : "本曲最高分：" + best.Score.ToString("D6") + "\n重新挑战相同谱面，争取更高成绩。");
        }
        private void Prepare()
        {
            StartBattle.interactable = Generate.interactable = false;
            root.StartCoroutine(root.CustomSongs.Prepare(songId, (value, text) =>
            { if (this && gameObject.activeInHierarchy) { Progress.value = value; Status.text = text; } }, result =>
            {
                if (!this || !gameObject.activeInHierarchy) return;
                Generate.interactable = true; StartBattle.interactable = result.Success;
                if (!result.Success) { Status.text = result.Error; return; }
                var session = root.CustomSongs.Prepared;
                int low = session.Sequence.Clips.Count(c => c.Name == "重音扩散"), mid = session.Sequence.Clips.Count(c => c.Name == "旋律追射");
                int high = session.Sequence.Clips.Count - low - mid;
                Status.text = "舞台已准备完成\n低频 " + low + " 轮 · 中频 " + mid + " 轮 · 高频 " + high + " 轮\n" +
                    (session.Sequence.Clips.Count == 0 ? "本曲有效候选点较少，本次为轻量节拍挑战。" : "攻击已编排，重试将使用相同基础谱面。");
            }));
        }
        public void Close()
        { if (root.Scenes.Busy) return; root.CustomSongs.Cancel(); root.CustomSongs.ReleasePrepared(); root.UI.OpenImportedMusic(); }
        private void OnDisable() { if (root != null) root.CustomSongs.Cancel(); }
    }
}
