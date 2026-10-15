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
        private float gradeFontSize, gradeMinSize, gradeMaxSize;
        private bool gradeAutoSize, gradeWrap;
        public void Bind(SystemRoot owner)
        {
            root = owner;
            gradeFontSize = Grade.fontSize; gradeMinSize = Grade.fontSizeMin; gradeMaxSize = Grade.fontSizeMax;
            gradeAutoSize = Grade.enableAutoSizing; gradeWrap = Grade.enableWordWrapping;
            Retry.onClick.AddListener(() => { if (!EnsureSaved()) return; var result = root.Scenes.RetryRun(); if (!result.Success) root.UI.Toast(result.Error); });
            Next.onClick.AddListener(() => { if (!EnsureSaved()) return; var result = root.LastResult?.IsCustom == true ? root.Scenes.AbandonToMusic() : root.Scenes.AbandonToTitle(); if (!result.Success) root.UI.Toast(result.Error); });
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
            Title.text = result.IsCustom ? result.SongTitle + "\n共鸣舞台" : song ? song.Title + "\n" + song.Subtitle : result.SongId;
            Cover.sprite = song ? song.Cover : null; Cover.enabled = Cover.sprite;
            Difficulty.text = result.Difficulty.ToString(); Score.text = result.Score.ToString("D6"); Grade.text = result.Grade;
            Grade.enableAutoSizing = result.IsCustom || gradeAutoSize;
            Grade.enableWordWrapping = result.IsCustom ? false : gradeWrap;
            Grade.fontSizeMin = result.IsCustom ? 32 : gradeMinSize;
            Grade.fontSizeMax = result.IsCustom ? gradeFontSize : gradeMaxSize;
            if (!result.IsCustom && !gradeAutoSize) Grade.fontSize = gradeFontSize;
            Grade.color = result.GradeColor == Color.white ? new Color(.12f,.17f,.23f) : result.GradeColor;
            Statistics.text = $"{(result.Stats.Completed ? "歌曲完成" : "角色倒下，歌曲结束")}\n最高连击：{result.Stats.MaxCombo}\nMISS：{result.Stats.Misses}\n击破：{result.Stats.DestroyedOrbs}    受伤：{result.Stats.DamageTakenCount}";
            if (result.Stats.BossScoringEnabled)
                Statistics.text = $"{(result.Stats.Completed ? "歌曲完成" : "角色倒下，歌曲结束")}\n最高连击：{result.Stats.MaxCombo}    Boss 命中：{result.Stats.BossHits}\n节拍命中：{result.Stats.OnBeatHits}    MISS：{result.Stats.Misses}\n击破：{result.Stats.DestroyedOrbs}    受伤：{result.Stats.DamageTakenCount}";
            var item = System.Array.Find(root.Config.Items, i => i && i.Id == result.DropItemId);
            Drop.text = result.IsCustom ? "本曲最高分：" + (root.Saves.Record(result.SongId, result.Difficulty)?.Score ?? result.Score).ToString("D6") +
                "\n节拍奖励命中占比：" + (result.Stats.BossHits > 0 ? (100.0 * result.Stats.OnBeatHits / result.Stats.BossHits).ToString("0.0") + "%" : "暂无")
                : $"获得道具：{(item ? item.DisplayName : result.DropItemId)} ×{result.DropQuantity}";
            DropIcon.sprite = item ? item.Icon : null; DropIcon.enabled = DropIcon.sprite;
            Next.GetComponentInChildren<TMP_Text>().text = result.IsCustom ? "返回我的音乐" : "返回主菜单";
            SetSaveStatus(save);
        }
        private void SetSaveStatus(OperationResult result)
        { saved = result.Success; SaveStatusLabel.text = saved ? (root.LastResult?.IsCustom == true ? "歌曲成绩已保存" : "成绩与道具已保存") : result.Error; RetrySave.gameObject.SetActive(!saved); }
    }
}
