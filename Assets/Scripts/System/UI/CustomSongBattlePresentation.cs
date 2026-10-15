using System;
using System.Collections;
using TapTap.Danmaku;
using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    [RequireComponent(typeof(BattleManager))]
    public sealed class CustomSongBattlePresentation : MonoBehaviour
    {
        public BattleManager Battle;
        private SystemRoot root;
        private Canvas canvas;
        private RectTransform safe;
        private TMP_Text song, warning, countdown;
        private UnityEngine.UI.Image progress;
        private LineRenderer[] bands;
        private Material lineMaterial;
        private int nextWarning;
        private readonly Color[] colors = { new(1, .5f, .35f), new(.3f, .8f, 1), new(.85f, .6f, 1) };
        private IEnumerator Start()
        {
            Battle ??= GetComponent<BattleManager>(); root = SystemRoot.Instance;
            if (!root || root.ActiveRun?.IsCustom != true)
            {
                if (root) { root.UI.Toast("请从我的音乐选择歌曲后进入共鸣舞台。"); root.Scenes.AbandonToMusic(); }
                yield break;
            }
            Battle.Config = root.ActiveRun.Difficulty.Battle;
            // The persistent HUD may still hold the previous run's death snapshot.
            // Present the new player's initial state while the countdown owns startup.
            var player = Battle.PlayerDefinition;
            root.UI.Hud.Render(new HudSnapshot { Portrait = player.Portrait,
                Health = Mathf.CeilToInt(player.MaxHealth), MaxHealth = Mathf.CeilToInt(player.MaxHealth),
                Stamina = player.MaxStamina, MaxStamina = player.MaxStamina,
                Skill1Color = player.Skill1Color, Skill2Color = player.Skill2Color });
            Build();
            while (root.Scenes.Busy) yield return null;
            double remaining = 3;
            while (remaining > 0)
            {
                countdown.text = Math.Ceiling(remaining).ToString();
                root.Input.SetGameplay(false);
                if (!root.UI.IsPauseOpen && !root.UI.Settings.gameObject.activeSelf) remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
            countdown.text = ""; root.Input.Clear();
            root.Input.SetGameplay(!root.UI.IsPauseOpen);
            Battle.Play();
        }
        private void Build()
        {
            var go = new GameObject("CustomSongHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            go.transform.SetParent(transform, false); canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>(); safe.SetParent(go.transform, false);
            safe.anchorMin = Vector2.zero; safe.anchorMax = Vector2.one; safe.offsetMin = safe.offsetMax = Vector2.zero;
            song = Text("SongProgress", new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(1100, 70), 25);
            warning = Text("AttackWarning", new Vector2(.5f, 1), new Vector2(0, -235), new Vector2(1000, 65), 30);
            countdown = Text("Countdown", new Vector2(.5f, .5f), Vector2.zero, new Vector2(600, 200), 100);
            var bar = new GameObject("SongProgressBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            var rect = (RectTransform)bar.transform; rect.SetParent(safe, false); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(0, .5f); rect.anchoredPosition = new Vector2(-450, 15); rect.sizeDelta = new Vector2(900, 5);
            progress = bar.GetComponent<UnityEngine.UI.Image>(); progress.color = new Color(.3f, .8f, 1); progress.raycastTarget = false;
            // Reuse the existing background's line shader when available.
            var curve = GetComponentInChildren<TapTap.MusicPresentation.BattleMusicBackgroundCurve>();
            var shared = curve ? curve.GetComponent<LineRenderer>()?.sharedMaterial : null;
            var shader = shared ? shared.shader : Shader.Find("Sprites/Default");
            lineMaterial = shared ? new Material(shared) : new Material(shader);
            if (lineMaterial.HasProperty("_Tint")) lineMaterial.SetColor("_Tint", Color.white);
            bands = new LineRenderer[3];
            for (int b = 0; b < 3; b++)
            {
                var marker = new GameObject(new[] { "LowRing", "MidFan", "HighDiamond" }[b]); marker.transform.SetParent(transform, false);
                marker.transform.localPosition = (Vector3)Battle.Config.BossPosition + new Vector3((b - 1) * .9f, b == 1 ? .75f : .15f, 0);
                var line = marker.AddComponent<LineRenderer>(); bands[b] = line;
                line.sharedMaterial = lineMaterial; line.useWorldSpace = false; line.loop = true;
                line.widthMultiplier = .035f; line.sortingOrder = 12; line.startColor = line.endColor = colors[b];
                int count = b == 0 ? 24 : b == 1 ? 3 : 4; line.positionCount = count;
                for (int i = 0; i < count; i++)
                { float angle = 2 * Mathf.PI * i / count + Mathf.PI / 2; line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .22f); }
            }
        }
        private TMP_Text Text(string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform; rect.SetParent(safe, false); rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = root.UI.Menu.Title.font; text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false; text.richText = false;
            return text;
        }
        private void LateUpdate()
        {
            if (!canvas || !root || !Battle.Config) return;
            bool gameplay = root.UI.Context == UIContext.Gameplay && !root.UI.IsPauseOpen && !root.UI.Settings.gameObject.activeSelf;
            canvas.enabled = gameplay;
            if (bands != null) foreach (var band in bands) band.enabled = root.UI.Context == UIContext.Gameplay;
            var area = Screen.safeArea;
            if (Screen.width > 0 && Screen.height > 0)
            { safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height); safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height); }
            var sequence = Battle.Config.Sequence;
            double seconds = Battle.MusicSongSeconds, duration = sequence.Music ? (double)sequence.Music.samples / sequence.Music.frequency : 0;
            song.text = root.ActiveRun?.Song.Title + "    " + ImportedMusicView.FormatTime(seconds) + " / " + ImportedMusicView.FormatTime(duration);
            progress.rectTransform.localScale = new Vector3(duration > 0 ? Mathf.Clamp01((float)(seconds / duration)) : 0, 1, 1);
            int activeBand = -1;
            if (Battle.Simulation != null && !Battle.Simulation.Finished)
            {
                int tick = Battle.Simulation.Tick;
                while (nextWarning < sequence.Events.Count && sequence.Events[nextWarning].Tick + sequence.Events[nextWarning].DurationTicks < tick) nextWarning++;
                if (nextWarning < sequence.Events.Count && sequence.Events[nextWarning].Tick <= tick)
                {
                    var e = sequence.Events[nextWarning]; activeBand = e.Label.StartsWith("LOW|", StringComparison.Ordinal) ? 0 : e.Label.StartsWith("MID|", StringComparison.Ordinal) ? 1 : 2;
                    warning.text = "即将发动 · " + e.Label.Substring(e.Label.IndexOf('|') + 1); warning.color = colors[activeBand];
                }
                else warning.text = "";
            }
            else warning.text = "";
            for (int b = 0; bands != null && b < bands.Length; b++)
            {
                float scale = activeBand == b ? 1.1f + .2f * Mathf.Sin((float)seconds * 10) : 1;
                bands[b].transform.localScale = Vector3.one * scale;
                bands[b].startColor = bands[b].endColor = colors[b] * new Color(1, 1, 1, activeBand == b ? 1 : .45f);
            }
        }
        private void OnDestroy() { if (lineMaterial) Destroy(lineMaterial); }
    }
}
