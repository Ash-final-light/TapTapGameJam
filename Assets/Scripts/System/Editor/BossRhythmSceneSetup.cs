using System;
using System.IO;
using System.Linq;
using TapTap.Danmaku;
using TapTap.Systems.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Systems.Editor
{
    [InitializeOnLoad]
    public static class BossRhythmSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/GameplayPlaceholder.unity";
        private const string RequestPath = "Temp/BossRhythmSceneSetup.request";
        private const string ReportPath = "Logs/BossRhythmSceneSetup.txt";
        private static readonly Color Ink = new(0.055f, 0.08f, 0.13f, 0.94f);
        private static readonly Color Cyan = new(0.25f, 0.9f, 1);
        private static readonly Color Gold = new(1, 0.8f, 0.24f);

        static BossRhythmSceneSetup() => EditorApplication.update += ProcessRequest;
        public static void RunBatch()
        {
            try { Install(); BossScoringValidation.Run(); CapturePreview(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void CaptureBatch()
        {
            try { Install(); CapturePreview(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static void ProcessRequest()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            string action = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
            try
            {
                if (action == "validate") BossScoringValidation.Run();
                else { Install(); BossScoringValidation.Run(); CapturePreview(); }
            }
            catch (Exception error)
            { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, "FAIL\n" + error); Debug.LogException(error); }
        }

        [MenuItem("工具/战斗计分/接入 Boss 节拍计分与 HUD")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 PlayMode 后接入战斗计分。");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var battle = Find<BattleManager>(scene).Single();
            var sequence = battle.Config ? battle.Config.Sequence : null;
            if (!sequence) throw new InvalidOperationException("BattleManager 需要配置 BattleConfig 和 Sequence。");
            var beats = new BattleBeatClock(sequence);
            Undo.RecordObject(battle, "开启 Boss 节拍计分");
            battle.BossScoring ??= new BossScoringSettings(); battle.BossScoring.Enabled = true;
            EditorUtility.SetDirty(battle);
            var views = Find<BossRhythmHud>(scene);
            if (views.Length > 1) throw new InvalidOperationException("战斗场景存在多个 BossRhythmHud。");
            var view = views.SingleOrDefault();
            if (!view) view = BuildHud(scene);
            Undo.RecordObject(view, "绑定 Boss 节拍 HUD"); view.Battle = battle; EditorUtility.SetDirty(view);
            ConfigureLayout(view);
            Undo.RecordObject(view.BpmLabel, "读取 BattleManager 的节拍配置");
            view.BpmLabel.text = $"{beats.Bpm:0.#} BPM";
            Canvas.ForceUpdateCanvases();
            ValidateHud(view);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("战斗场景保存失败。");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, $"PASS\nScene: {scene.path}\nSequence: {AssetDatabase.GetAssetPath(sequence)}\n" +
                $"BPM: {beats.Bpm}\nFirst beat: {beats.FirstBeatSeconds}s\n" +
                "Fire-time judgement; Boss hit 100; on-beat bonus 50; window ±100 ms; timeout 2 beats; damage breaks combo.\n");
            Debug.Log("Boss 节拍计分与 HUD 已接入：" + scene.path, view);
        }

        private static BossRhythmHud BuildHud(Scene scene)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Spotlight/Fonts/SpotlightChinese.asset");
            if (!font) throw new InvalidOperationException("缺少现有 HUD 中文字体。");
            var go = new GameObject("BossRhythmCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster), typeof(BossRhythmHud));
            SceneManager.MoveGameObjectToScene(go, scene); Undo.RegisterCreatedObjectUndo(go, "创建 Boss 节拍 HUD");
            var view = go.GetComponent<BossRhythmHud>(); view.Canvas = go.GetComponent<Canvas>();
            view.Canvas.renderMode = RenderMode.ScreenSpaceOverlay; view.Canvas.sortingOrder = 5;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            view.SafeRoot = Rect("SafeArea", go.transform, Vector2.zero, Vector2.zero);
            view.SafeRoot.anchorMin = Vector2.zero; view.SafeRoot.anchorMax = Vector2.one;
            view.SafeRoot.offsetMin = view.SafeRoot.offsetMax = Vector2.zero;

            var top = Image("BeatPanel", view.SafeRoot, Ink, new Vector2(540, 110), new Vector2(0, -22));
            Anchor(top.rectTransform, new Vector2(0.5f, 1));
            view.BeatPanel = top.rectTransform;
            view.TopAccent = Image("TopAccent", top.transform, Cyan, new Vector2(540, 3), new Vector2(0, 53)).rectTransform;
            view.BpmLabel = Text("Bpm", top.transform, font, "60 BPM", 20, new Vector2(250, 30), new Vector2(0, 31), Cyan);
            view.BeatTrack = Rect("BeatTrack", top.transform, new Vector2(480, 40), new Vector2(0, -6));
            var trackLine = Image("TrackLine", view.BeatTrack, new Color(0.4f, 0.7f, 0.8f, 0.22f), new Vector2(460, 2), Vector2.zero);
            trackLine.rectTransform.anchorMin = new Vector2(0, 0.5f); trackLine.rectTransform.anchorMax = new Vector2(1, 0.5f);
            trackLine.rectTransform.sizeDelta = new Vector2(-20, 2);
            Image("TimingWindow", view.BeatTrack, new Color(Cyan.r, Cyan.g, Cyan.b, 0.09f), new Vector2(42, 40), Vector2.zero);
            view.Markers = new UnityEngine.UI.Image[8];
            for (int i = 0; i < view.Markers.Length; i++)
            {
                var marker = Image("BeatMarker" + i, view.BeatTrack, Cyan, new Vector2(12, 12), new Vector2((i / 2 + 1) * 75 * (i % 2 == 0 ? -1 : 1), 0));
                marker.rectTransform.localRotation = Quaternion.Euler(0, 0, 45); view.Markers[i] = marker;
            }
            view.Center = Image("BeatCenter", view.BeatTrack, Cyan, new Vector2(17, 17), Vector2.zero);
            view.Center.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            view.BeatHint = Text("BeatHint", top.transform, font, "跟着节拍开火 · 命中 Boss 获得奖励", 14, new Vector2(510, 24), new Vector2(0, -39), new Color(0.7f, 0.8f, 0.9f)).rectTransform;

            var right = Image("ScorePanel", view.SafeRoot, Ink, new Vector2(280, 380), new Vector2(-32, -170));
            Anchor(right.rectTransform, Vector2.one);
            Image("SideAccent", right.transform, Cyan, new Vector2(3, 380), new Vector2(-138, 0));
            Text("ScoreLabel", right.transform, font, "SCORE", 19, new Vector2(230, 30), new Vector2(0, 159), new Color(0.65f, 0.8f, 0.9f));
            view.Score = Text("ScoreValue", right.transform, font, "000000", 43, new Vector2(250, 65), new Vector2(0, 116), Color.white);
            Image("Divider", right.transform, new Color(0.4f, 0.7f, 0.8f, 0.2f), new Vector2(220, 2), new Vector2(0, 68));
            view.Combo = Text("ComboValue", right.transform, font, "0", 76, new Vector2(240, 105), new Vector2(0, 8), Color.white);
            view.ComboNumber = view.Combo.rectTransform;
            Text("ComboLabel", right.transform, font, "COMBO", 20, new Vector2(230, 30), new Vector2(0, -52), Cyan);
            view.Status = Text("ComboStatus", right.transform, font, "等待命中", 20, new Vector2(230, 32), new Vector2(0, -87), Color.white);
            var timeoutBack = Image("TimeoutTrack", right.transform, new Color(0.3f, 0.4f, 0.5f, 0.4f), new Vector2(220, 5), new Vector2(0, -116));
            view.TimeoutFill = Image("TimeoutFill", timeoutBack.transform, Cyan, new Vector2(220, 5), Vector2.zero);
            view.TimeoutFill.rectTransform.pivot = new Vector2(0, 0.5f); view.TimeoutFill.rectTransform.anchoredPosition = new Vector2(-110, 0);
            view.TimeoutFill.rectTransform.localScale = new Vector3(0, 1, 1);
            view.Judgement = Text("Judgement", right.transform, font, "跟着节拍开火", 24, new Vector2(250, 40), new Vector2(0, -146), Gold);
            view.Award = Text("Award", right.transform, font, "", 20, new Vector2(230, 30), new Vector2(0, -176), Gold);
            return view;
        }

        private static void ConfigureLayout(BossRhythmHud view)
        {
            view.BeatPanel = (RectTransform)view.BeatTrack.parent;
            view.TopAccent = (RectTransform)view.BeatPanel.Find("TopAccent");
            view.BeatHint = (RectTransform)view.BeatPanel.Find("BeatHint");
            Undo.RecordObjects(new UnityEngine.Object[] { view.BeatPanel, view.TopAccent, view.BeatTrack, view.BeatHint }, "调整节拍条布局");
            view.BeatPanel.sizeDelta = new Vector2(540, 110); view.TopAccent.sizeDelta = new Vector2(540, 3);
            view.BeatTrack.sizeDelta = new Vector2(480, 40); view.BeatHint.sizeDelta = new Vector2(510, 24);
            view.BeatHint.GetComponent<TMP_Text>().fontSize = 14;
            var line = (RectTransform)view.BeatTrack.Find("TrackLine");
            line.anchorMin = new Vector2(0, 0.5f); line.anchorMax = new Vector2(1, 0.5f); line.sizeDelta = new Vector2(-20, 2);
        }

        public static void ValidateHud(BossRhythmHud view)
        {
            if (!view || !view.Canvas || !view.SafeRoot || !view.BeatPanel || !view.TopAccent || !view.BeatHint || !view.BeatTrack || !view.Center || !view.Score || !view.Combo ||
                !view.Status || !view.Judgement || !view.Award || !view.TimeoutFill || !view.BpmLabel || view.Markers.Length != 8)
                throw new InvalidOperationException("节拍 HUD 引用不完整。");
            foreach (var graphic in view.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (graphic.raycastTarget || graphic.rectTransform.rect.width <= 0 || graphic.rectTransform.rect.height <= 0)
                    throw new InvalidOperationException("HUD 图形应有有效尺寸且不阻挡输入：" + graphic.name);
        }

        private static T[] Find<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        private static void Anchor(RectTransform rect, Vector2 anchor)
        { var position = rect.anchoredPosition; rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; }
        private static UnityEngine.UI.Image Image(string name, Transform parent, Color color, Vector2 size, Vector2 position)
        { var image = Rect(name, parent, size, position).gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image; }
        private static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, string value, int size, Vector2 dimensions, Vector2 position, Color color)
        {
            var text = Rect(name, parent, dimensions, position).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; return text;
        }

        public static void CapturePreview()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var live = Find<BossRhythmHud>(scene).Single();
            var copy = UnityEngine.Object.Instantiate(live.gameObject); copy.hideFlags = HideFlags.HideAndDontSave;
            var view = copy.GetComponent<BossRhythmHud>(); view.enabled = false;
            var systemConfig = Find<SystemRoot>(scene).Single().Config;
            var existingHud = UnityEngine.Object.Instantiate(systemConfig.UIPrefab);
            existingHud.gameObject.hideFlags = HideFlags.HideAndDontSave;
            existingHud.enabled = false;
            foreach (var panel in new[] { existingHud.Menu.gameObject, existingHud.Loading.gameObject, existingHud.Settings.gameObject,
                existingHud.Pause.gameObject, existingHud.Dialog.gameObject, existingHud.Songs.gameObject, existingHud.Loadout.gameObject,
                existingHud.Result.gameObject, existingHud.ImportedMusic ? existingHud.ImportedMusic.gameObject : null,
                existingHud.Alignment ? existingHud.Alignment.gameObject : null, existingHud.ToastLabel.gameObject })
                if (panel) panel.SetActive(false);
            existingHud.Hud.gameObject.SetActive(true); existingHud.Hud.Selector.SetActive(false);
            existingHud.Hud.Render(new HudSnapshot { Portrait = live.Battle.PlayerDefinition.Portrait });
            var cameraObject = new GameObject("Boss HUD preview camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
            camera.cullingMask = 1 << 5;
            foreach (var child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            foreach (var child in existingHud.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            var target = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; view.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                view.Canvas.worldCamera = camera; view.Canvas.planeDistance = 1; view.Canvas.enabled = true;
                var hudCanvas = existingHud.GetComponent<Canvas>(); hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                hudCanvas.worldCamera = camera; hudCanvas.planeDistance = 1.1f;
                Canvas.ForceUpdateCanvases();
                var scores = new ScoreManager(null, live.Battle.BossScoring, live.Battle.Config.Sequence);
                int fireTick = (int)Math.Round((scores.Beats.MusicStartSeconds + scores.Beats.FirstBeatSeconds) * 60);
                for (int i = 0; i < 23; i++)
                    scores.Consume(new BattleEvent(fireTick + 30, new ProjectileState { FireTick = fireTick }, Framework.FixedPoint.FPVector2.Zero));
                view.Render(scores, scores.Beats.FirstBeatSeconds + scores.Beats.PeriodSeconds * 0.25, fireTick + 30);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes("Logs/BossRhythmHudPreview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(copy); UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(existingHud.gameObject);
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
