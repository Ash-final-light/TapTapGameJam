using TMPro;
using UnityEngine;
namespace TapTap.Systems.UI
{
    public sealed class ResultView : MonoBehaviour
    {
        public UnityEngine.UI.Image Cover, DropIcon;
        public TMP_Text Title, Difficulty, Score, Grade, Statistics, Drop, SaveStatusLabel;
        public UnityEngine.UI.Button Retry, Next, RetrySave;
        private SystemRoot root;
        private bool saved;
        public void Bind(SystemRoot owner)
        {
            root = owner;
            Retry.onClick.AddListener(() => { if (!EnsureSaved()) return; var result = root.Scenes.RetryRun(); if (!result.Success) root.UI.Toast(result.Error); });
            Next.onClick.AddListener(() => { if (!EnsureSaved()) return; var result = root.Scenes.AbandonToTitle(); if (!result.Success) root.UI.Toast(result.Error); });
            RetrySave.onClick.AddListener(() => EnsureSaved());
        }
        private bool EnsureSaved()
        {
            if (saved) return true;
            var result = root.RetryResultSave(); SetSaveStatus(result); if (!result.Success) root.UI.Toast(result.Error); return result.Success;
        }
        public void Show(RunResult result, OperationResult save)
        {
            var song = root.Config.Songs.Find(result.SongId);
            Title.text = song ? song.Title + "\n" + song.Subtitle : result.SongId;
            Cover.sprite = song ? song.Cover : null; Cover.enabled = Cover.sprite;
            Difficulty.text = result.Difficulty.ToString(); Score.text = result.Score.ToString("D6"); Grade.text = result.Grade;
            Grade.color = result.GradeColor == Color.white ? new Color(.12f,.17f,.23f) : result.GradeColor;
            Statistics.text = $"{(result.Stats.Completed ? "歌曲完成" : "角色倒下，歌曲结束")}\n最高连击：{result.Stats.MaxCombo}\nMISS：{result.Stats.Misses}\n击破：{result.Stats.DestroyedOrbs}    受伤：{result.Stats.DamageTakenCount}";
            if (result.Stats.BossScoringEnabled)
                Statistics.text = $"{(result.Stats.Completed ? "歌曲完成" : "角色倒下，歌曲结束")}\n最高连击：{result.Stats.MaxCombo}    Boss 命中：{result.Stats.BossHits}\n节拍命中：{result.Stats.OnBeatHits}    MISS：{result.Stats.Misses}\n击破：{result.Stats.DestroyedOrbs}    受伤：{result.Stats.DamageTakenCount}";
            var item = System.Array.Find(root.Config.Items, i => i && i.Id == result.DropItemId);
            Drop.text = $"获得道具：{(item ? item.DisplayName : result.DropItemId)} ×{result.DropQuantity}";
            DropIcon.sprite = item ? item.Icon : null; DropIcon.enabled = DropIcon.sprite;
            SetSaveStatus(save);
        }
        private void SetSaveStatus(OperationResult result)
        { saved = result.Success; SaveStatusLabel.text = saved ? "成绩与道具已保存" : result.Error; RetrySave.gameObject.SetActive(!saved); }
    }
}
