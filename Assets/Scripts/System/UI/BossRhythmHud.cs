using System;
using TapTap.Danmaku;
using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    [DisallowMultipleComponent, DefaultExecutionOrder(650)]
    public sealed class BossRhythmHud : MonoBehaviour
    {
        public BattleManager Battle;
        public Canvas Canvas;
        public RectTransform SafeRoot, BeatPanel, TopAccent, BeatHint, BeatTrack, ComboNumber;
        public TMP_Text BpmLabel, Score, Combo, Status, Judgement, Award;
        public UnityEngine.UI.Image Center, TimeoutFill;
        public UnityEngine.UI.Image[] Markers = Array.Empty<UnityEngine.UI.Image>();
        [Min(1)] public int PreviewBeats = 4;
        public Color BeatColor = new(0.25f, 0.9f, 1);
        public Color PerfectColor = new(1, 0.8f, 0.24f);
        public Color BreakColor = new(1, 0.36f, 0.4f);
        private ScoreManager boundScores;
        private int previousScore = -1, previousCombo = -1, feedbackRevision = -1, lastPerfectTick = -1000;
        private Rect previousSafeArea;
        private Vector2Int previousScreen;

        private void LateUpdate()
        {
            var root = SystemRoot.Instance;
            bool visible = Battle && Battle.Simulation != null && Battle.BossScoring.Enabled &&
                (!root || !root.UI || (root.UI.Context == UIContext.Gameplay && !root.UI.IsPauseOpen &&
                    !root.UI.Settings.gameObject.activeSelf));
            if (Canvas) Canvas.enabled = visible;
            if (!visible) return;
            UpdateSafeArea();
            double songSeconds = Battle.Config.Sequence.Music ? Battle.MusicSongSeconds
                : Battle.Scores.Beats.SongSecondsAtTick(Battle.Simulation.Tick);
            Render(Battle.Scores, songSeconds, Battle.Simulation.Tick);
        }

        private void UpdateSafeArea()
        {
            if (!SafeRoot || Screen.width <= 0 || Screen.height <= 0) return;
            var screen = new Vector2Int(Screen.width, Screen.height); var safe = Screen.safeArea;
            if (screen == previousScreen && safe == previousSafeArea) return;
            previousScreen = screen; previousSafeArea = safe;
            SafeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            SafeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            SafeRoot.offsetMin = SafeRoot.offsetMax = Vector2.zero;
        }

        public void Render(ScoreManager scores, double songSeconds, int tick)
        {
            if (scores == null) return;
            // Reserve the existing left health card and right pause button on narrower displays.
            float headerWidth = Mathf.Clamp(SafeRoot.rect.width - 1380, 260, 560);
            BeatPanel.sizeDelta = new Vector2(headerWidth, BeatPanel.sizeDelta.y);
            TopAccent.sizeDelta = new Vector2(headerWidth, TopAccent.sizeDelta.y);
            BeatTrack.sizeDelta = new Vector2(headerWidth - 60, BeatTrack.sizeDelta.y);
            BeatHint.sizeDelta = new Vector2(headerWidth - 30, BeatHint.sizeDelta.y);
            if (!ReferenceEquals(boundScores, scores))
            {
                boundScores = scores; previousScore = previousCombo = feedbackRevision = -1; lastPerfectTick = -1000;
                BpmLabel.text = scores.Beats.Valid ? $"{scores.Beats.Bpm:0.#} BPM" : "节拍未配置";
            }
            int value = scores.CurrentScore, combo = scores.Stats.CurrentCombo;
            if (value != previousScore) { Score.text = value.ToString("D6"); previousScore = value; }
            if (combo != previousCombo) { Combo.text = combo.ToString(); previousCombo = combo; }
            bool active = combo > 0;
            Status.text = active ? "连击中" : "等待命中";
            Status.color = active ? BeatColor : new Color(0.65f, 0.7f, 0.8f);
            TimeoutFill.rectTransform.localScale = new Vector3(scores.ComboRemaining, 1, 1);
            TimeoutFill.color = scores.ComboRemaining < 0.25f ? BreakColor : BeatColor;

            if (feedbackRevision != scores.FeedbackRevision)
            {
                feedbackRevision = scores.FeedbackRevision;
                if (scores.LastFeedback == ScoreFeedback.Perfect) lastPerfectTick = scores.LastFeedbackTick;
                Judgement.text = scores.LastFeedback switch
                {
                    ScoreFeedback.Perfect => "PERFECT", ScoreFeedback.Hit => "普通命中",
                    ScoreFeedback.ComboBroken => "连击中断", _ => "跟着节拍开火"
                };
                Judgement.color = scores.LastFeedback == ScoreFeedback.Perfect ? PerfectColor
                    : scores.LastFeedback == ScoreFeedback.ComboBroken ? BreakColor : Color.white;
                Award.text = scores.LastAward > 0 ? "+" + scores.LastAward : "";
                Award.color = Judgement.color;
            }
            float age = scores.LastFeedbackTick < 0 ? float.MaxValue : (tick - scores.LastFeedbackTick) / 60f;
            float feedbackAlpha = scores.LastFeedback == ScoreFeedback.None ? 0.55f : Mathf.Clamp01((0.95f - age) / 0.25f);
            Judgement.alpha = Award.alpha = feedbackAlpha;
            if (ComboNumber) ComboNumber.localScale = Vector3.one * (1 + (active ? 0.16f * Mathf.Clamp01(1 - age / 0.18f) : 0));
            RenderBeats(scores.Beats, songSeconds, tick);
        }

        private void RenderBeats(BattleBeatClock beats, double songSeconds, int tick)
        {
            if (!beats.Valid) { foreach (var marker in Markers) marker.enabled = false; return; }
            double position = beats.BeatPosition(songSeconds);
            long next = (long)Math.Floor(position) + 1;
            float halfWidth = BeatTrack.rect.width * 0.5f - 20;
            int pairs = Markers.Length / 2;
            for (int i = 0; i < pairs; i++)
            {
                long index = next + i;
                double distance = index - position;
                bool visible = beats.IsBeatInSong(index) && distance >= 0 && distance <= PreviewBeats;
                float x = (float)(distance / PreviewBeats) * halfWidth;
                float alpha = Mathf.Clamp01((float)(PreviewBeats - distance)) * 0.85f;
                for (int side = 0; side < 2; side++)
                {
                    var marker = Markers[i * 2 + side]; marker.enabled = visible;
                    marker.rectTransform.anchoredPosition = new Vector2(side == 0 ? -x : x, 0);
                    marker.color = new Color(BeatColor.r, BeatColor.g, BeatColor.b, alpha);
                }
            }
            long previous = (long)Math.Floor(position);
            double sinceBeat = songSeconds - beats.SongSecondsAtBeat(previous);
            float pulse = beats.IsBeatInSong(previous) ? Mathf.Clamp01(1 - (float)sinceBeat / 0.15f) : 0;
            float perfect = Mathf.Clamp01(1 - (tick - lastPerfectTick) / 12f);
            Center.color = Color.Lerp(BeatColor * new Color(1, 1, 1, 0.45f), perfect > 0 ? PerfectColor : BeatColor, Mathf.Max(pulse, perfect));
            Center.rectTransform.localScale = Vector3.one * (1 + pulse * 0.25f + perfect * 0.18f);
        }
    }
}
