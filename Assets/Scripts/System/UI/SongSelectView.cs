using System;
using TMPro;
using UnityEngine;
namespace TapTap.Systems.UI
{
    public sealed class SongSelectView : MonoBehaviour
    {
        public LoopSongList List;
        public UnityEngine.UI.Image Cover;
        public TMP_Text Title, Subtitle, Artist, Best, Empty;
        public UnityEngine.UI.Button Back, Confirm;
        public UnityEngine.UI.Button[] DifficultyButtons;
        public UnityEngine.UI.Image[] DifficultySelectionArt;
        public SongDefinition SelectedSong { get; private set; }
        public SongDifficulty SelectedDifficulty { get; private set; }
        private SystemRoot root;
        public void Bind(SystemRoot owner)
        {
            root = owner; List.SelectionChanged += SelectSong;
            List.Bind(root.Config.Songs ? root.Config.Songs.Songs : Array.Empty<SongDefinition>());
            for (int i = 0; i < DifficultyButtons.Length; i++) { var difficulty = (SongDifficulty)i; DifficultyButtons[i].onClick.AddListener(() => { SelectedDifficulty = difficulty; Refresh(); }); }
            Confirm.onClick.AddListener(() => root.UI.OpenLoadout()); Back.onClick.AddListener(() => root.UI.ShowContext(UIContext.Title));
        }
        public void Open() { SelectSong(List.SelectedIndex); }
        private void SelectSong(int index)
        {
            SelectedSong = root.Config.Songs && index >= 0 && index < root.Config.Songs.Songs.Length ? root.Config.Songs.Songs[index] : null;
            var selected = SelectedSong ? SelectedSong.Find(SelectedDifficulty) : null;
            if (selected == null || !selected.Enabled)
                foreach (SongDifficulty difficulty in Enum.GetValues(typeof(SongDifficulty)))
                    if (SelectedSong && SelectedSong.Find(difficulty)?.Enabled == true) { SelectedDifficulty = difficulty; break; }
            Refresh();
        }
        public void Refresh()
        {
            Title.text = SelectedSong ? SelectedSong.Title : "暂无歌曲";
            Subtitle.text = SelectedSong ? SelectedSong.Subtitle : ""; Artist.text = SelectedSong ? SelectedSong.Artist : "";
            Cover.sprite = SelectedSong ? SelectedSong.Cover : null; Cover.enabled = Cover.sprite;
            for (int i = 0; i < DifficultyButtons.Length; i++)
            {
                var d = SelectedSong ? SelectedSong.Find((SongDifficulty)i) : null;
                DifficultyButtons[i].interactable = d != null && d.Enabled;
                bool hasArt = DifficultySelectionArt != null && i < DifficultySelectionArt.Length && DifficultySelectionArt[i];
                if (hasArt)
                {
                    DifficultySelectionArt[i].gameObject.SetActive((int)SelectedDifficulty == i);
                    DifficultyButtons[i].GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                    var group = DifficultyButtons[i].GetComponent<CanvasGroup>();
                    if (group) group.alpha = DifficultyButtons[i].interactable ? 1 : .35f;
                }
                else DifficultyButtons[i].GetComponent<UnityEngine.UI.Image>().color = (int)SelectedDifficulty == i ? new Color(1, .64f, .14f) : new Color(.18f, .26f, .34f);
            }
            var record = SelectedSong ? root.Saves.Record(SelectedSong.Id, SelectedDifficulty) : null;
            var firstDifficulty = Array.Find(DifficultyButtons, b => b.interactable);
            foreach (var row in List.Rows)
            {
                var navigation = row.Button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                navigation.selectOnRight = firstDifficulty ? firstDifficulty : Back; navigation.selectOnLeft = Back;
                row.Button.navigation = navigation;
            }
            Best.text = record == null ? "最高成绩\n暂无记录" : $"最高成绩\n{record.Grade}   {record.Score:D6}";
            Confirm.interactable = SelectedSong && SelectedSong.Find(SelectedDifficulty)?.Enabled == true;
            Empty.text = SelectedSong ? "" : "请配置歌曲目录";
        }
    }
}
