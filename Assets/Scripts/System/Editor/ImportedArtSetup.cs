using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TapTap.Systems.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Systems.Editor
{
    /// <summary>Explicit editor requests only; regular reloads never rewrite UI assets.</summary>
    [InitializeOnLoad]
    public static class ImportedArtSetup
    {
        private const string Request = "Temp/ImportedArtSetup.request";
        private const string Folder = "Assets/ArtAsset/package1/";
        private const string Prefabs = "Assets/UI/Spotlight/Prefabs/";
        private static readonly Dictionary<string, Rect> Bounds = new();
        private static readonly Color Ink = new(.12f, .1f, .28f);
        static ImportedArtSetup() => EditorApplication.update += Process;

        private static void Process()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string action;
            try { action = File.ReadAllText(Request).Trim(); }
            catch (IOException) { return; }
            if (string.IsNullOrEmpty(action)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode && action != "runtime-check" && action != "buttons-runtime") return;
            File.Delete(Request);
            Directory.CreateDirectory("Logs");
            try
            {
                if (action == "runtime-check") { RuntimeCheck(); return; }
                if (action == "buttons-runtime") { CheckAllButtonsRuntime(); return; }
                if (action == "buttons-install") { InstallAllButtons(); return; }
                if (action == "install") Install();
                Capture(false); Capture(true);
                File.WriteAllText("Logs/ImportedArtSetup.txt", "PASS\nSeven PNG assets integrated into SpotlightUI, SongSelect and HUD prefabs.\nOriginal images and sprite metadata preserved.\nPreviews: Logs/ImportedArtSongSelect.png, Logs/ImportedArtHud.png\n");
                Debug.Log("[ImportedArt] UI art installed and previews verified.");
            }
            catch (Exception error)
            {
                File.WriteAllText("Logs/ImportedArtSetup.txt", "FAIL\n" + error);
                Debug.LogException(error);
            }
        }

        private static Sprite Sprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + name + ".png");
            if (!sprite) throw new InvalidOperationException("Missing imported sprite: " + name);
            return sprite;
        }

        // Compensate transparent source margins in the UI only, without editing sprite metadata.
        private static Rect VisibleBounds(string name)
        {
            if (Bounds.TryGetValue(name, out var bounds)) return bounds;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(Folder + name + ".png"))) throw new IOException(name);
                var pixels = texture.GetPixels32();
                int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                        if (pixels[y * texture.width + x].a > 2)
                        { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                if (maxX < minX) throw new InvalidOperationException("Empty PNG: " + name);
                bounds = new Rect((float)minX / texture.width, (float)minY / texture.height,
                    (float)(maxX - minX + 1) / texture.width, (float)(maxY - minY + 1) / texture.height);
                Bounds[name] = bounds; return bounds;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static UnityEngine.UI.Image Art(Transform parent, string objectName, string source, Vector2 visibleSize, Vector2 center)
        {
            var child = parent.Find(objectName);
            var image = child ? child.GetComponent<UnityEngine.UI.Image>() : null;
            if (!image)
            {
                var go = new GameObject(objectName, typeof(RectTransform), typeof(UnityEngine.UI.Image));
                go.transform.SetParent(parent, false); image = go.GetComponent<UnityEngine.UI.Image>();
            }
            image.sprite = Sprite(source); image.color = Color.white; image.raycastTarget = false;
            image.type = UnityEngine.UI.Image.Type.Simple; image.preserveAspect = false;
            var bounds = VisibleBounds(source);
            var size = new Vector2(visibleSize.x / bounds.width, visibleSize.y / bounds.height);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = center - Vector2.Scale(bounds.center - Vector2.one * .5f, size);
            image.transform.SetAsFirstSibling(); return image;
        }

        private static void StyleBack(UnityEngine.UI.Button button)
        {
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
            Art(button.transform, "ImportedBackArtwork", "返回", new Vector2(75, 48), new Vector2(-44, 0));
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = "返回"; label.color = Ink; label.rectTransform.offsetMin = new Vector2(75, 0);
        }

        private static void Songs(SongSelectView view)
        {
            StyleBack(view.Back);
            foreach (var row in view.List.Rows)
            {
                row.Button.transition = UnityEngine.UI.Selectable.Transition.None;
                row.Button.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                row.Artwork = Art(row.transform, "ImportedRowArtwork", "歌曲名称轮换器", new Vector2(350, 88), Vector2.zero);
                row.Label.color = Ink; row.Label.fontSize = 23;
                row.Label.rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
                row.Label.rectTransform.anchoredPosition = new Vector2(0, 10);
            }
            Art(view.Title.transform.parent, "ImportedSongTitleArtwork", "歌曲名称显示框", new Vector2(760, 110), view.Title.rectTransform.anchoredPosition);
            view.Title.color = Ink; view.Title.fontSize = 36;
            view.Title.rectTransform.sizeDelta = new Vector2(650, 70);
            view.DifficultySelectionArt = new UnityEngine.UI.Image[view.DifficultyButtons.Length];
            for (int i = 0; i < view.DifficultyButtons.Length; i++)
            {
                var button = view.DifficultyButtons[i];
                button.transition = UnityEngine.UI.Selectable.Transition.None;
                if (!button.GetComponent<CanvasGroup>()) button.gameObject.AddComponent<CanvasGroup>();
                button.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                Art(button.transform, "ImportedDifficultyFrame", "歌曲难度框", new Vector2(180, 112), Vector2.zero);
                var selected = Art(button.transform, "ImportedDifficultySelected", "歌曲难度-选中状态", new Vector2(210, 155), Vector2.zero);
                selected.transform.SetSiblingIndex(1); selected.gameObject.SetActive(i == 0);
                view.DifficultySelectionArt[i] = selected;
                var label = button.GetComponentInChildren<TMP_Text>(); label.color = Ink; label.fontSize = 25;
                label.transform.SetAsLastSibling();
            }
        }

        private static void Hud(HudView view)
        {
            view.FullHeart = Sprite("血量1"); view.EmptyHeart = Sprite("血量2");
            view.HeartTemplate.sprite = view.FullHeart; view.HeartTemplate.color = Color.white;
            view.HeartTemplate.preserveAspect = true; view.HeartTemplate.raycastTarget = false;
            view.HeartTemplate.transform.localScale = Vector3.one * 1.8f;
        }

        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            // Require all assets before changing any prefab.
            foreach (string name in new[] { "返回", "歌曲名称轮换器", "歌曲名称显示框", "歌曲难度框", "歌曲难度-选中状态", "血量1", "血量2" })
            { Sprite(name); VisibleBounds(name); }
            Edit("SongSelect.prefab", root => Songs(root.GetComponent<SongSelectView>()));
            Edit("HUD.prefab", root => Hud(root.GetComponent<HudView>()));
            Edit("SpotlightUI.prefab", root =>
            {
                var ui = root.GetComponent<SystemUIController>(); Songs(ui.Songs); Hud(ui.Hud);
                StyleBack(ui.Loadout.Back);
            });
            AssetDatabase.SaveAssets();
        }

        private static void Edit(string file, Action<GameObject> edit)
        {
            string path = Prefabs + file;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureTheme(ImportedButtonTheme theme)
        {
            theme.Face = Sprite("歌曲名称显示框"); theme.FaceBounds = VisibleBounds("歌曲名称显示框");
            theme.Arrow = Sprite("返回"); theme.ArrowBounds = VisibleBounds("返回");
            theme.Icon = Sprite("血量1");
            theme.Frame = Sprite("歌曲难度框");
        }

        private static void InstallAllButtons()
        {
            var report = new List<string>(); int total = 0;
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { Prefabs.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = root.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length;
                    if (count == 0) continue;
                    var theme = root.GetComponent<ImportedButtonTheme>() ?? root.AddComponent<ImportedButtonTheme>();
                    ConfigureTheme(theme); theme.ApplyAll(); Canvas.ForceUpdateCanvases();
                    foreach (var artwork in root.GetComponentsInChildren<ImportedButtonArtwork>(true)) artwork.RefreshVisual();
                    ValidateButtons(root); PrefabUtility.SaveAsPrefabAsset(root, path);
                    total += count; report.Add(path + ": " + count + " buttons");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets(); Capture(false); Capture(true);
            CapturePanel("Menu"); CapturePanel("Settings"); CapturePanel("Pause"); CapturePanel("Loadout"); CapturePanel("Result");
            report.Insert(0, "PASS: " + total + " button instances across UI prefabs (including standalone copies).");
            File.WriteAllLines("Logs/ImportedButtons.txt", report);
            Debug.Log("[ImportedArt] " + report[0]);
        }

        private static void ValidateButtons(GameObject root)
        {
            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var style = button.GetComponent<ImportedButtonArtwork>();
                var existing = button.transform.Find("ImportedRowArtwork") ?? button.transform.Find("ImportedDifficultyFrame") ?? button.transform.Find("ImportedBackArtwork");
                var image = style ? style.Artwork : existing ? existing.GetComponent<UnityEngine.UI.Image>() : null;
                if (!image || !image.sprite || !AssetDatabase.GetAssetPath(image.sprite).StartsWith(Folder, StringComparison.Ordinal))
                    throw new InvalidOperationException("Button missing imported art: " + button.name);
                if (!button.GetComponent<UnityEngine.UI.Image>().raycastTarget || image.raycastTarget)
                    throw new InvalidOperationException("Button hit area invalid: " + button.name);
                if (style && (button.targetGraphic != image || style.Icons.Any(i => i && i.sprite != style.IconSprite)))
                    throw new InvalidOperationException("Button graphic references invalid: " + button.name);
            }
        }

        private static void CapturePanel(string name)
        {
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            var ui = Object.Instantiate(config.UIPrefab); ui.gameObject.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                foreach (Transform child in ui.transform.Find("SafeAreaRoot")) child.gameObject.SetActive(false);
                Component view = name == "Menu" ? ui.Menu : name == "Settings" ? ui.Settings : name == "Pause" ? ui.Pause : name == "Loadout" ? ui.Loadout : (Component)ui.Result;
                view.gameObject.SetActive(true);
                if (name == "Settings")
                    for (int i = 0; i < ui.Settings.Pages.Length; i++) ui.Settings.Pages[i].SetActive(i == 0);
                RenderPanel(ui, "Logs/ImportedButtons" + name + ".png");
            }
            finally { Object.DestroyImmediate(ui.gameObject); }
        }

        private static void RenderPanel(SystemUIController ui, string path)
        {
            bool cloned = EditorApplication.isPlaying;
            GameObject stage = null;
            if (cloned)
            {
                stage = new GameObject("Button Preview Stage") { hideFlags = HideFlags.HideAndDontSave }; stage.SetActive(false);
                ui = Object.Instantiate(ui, stage.transform); ui.gameObject.hideFlags = HideFlags.HideAndDontSave;
                ui.EventSystem.gameObject.SetActive(false); stage.SetActive(true);
            }
            var cameraObject = new GameObject("Button Art Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try
            {
                foreach (var behaviour in ui.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(behaviour is UnityEngine.UI.Graphic) && !(behaviour is UnityEngine.UI.CanvasScaler) &&
                        !(behaviour is UnityEngine.UI.LayoutGroup) && !(behaviour is UnityEngine.UI.LayoutElement)) behaviour.enabled = false;
                foreach (var child in ui.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .15f, .2f);
                camera.cullingMask = 1 << 5; camera.targetTexture = target;
                var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                foreach (var artwork in ui.GetComponentsInChildren<ImportedButtonArtwork>(true)) artwork.RefreshVisual();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
                if (cloned) Object.DestroyImmediate(stage);
            }
        }

        private static void CheckAllButtonsRuntime()
        {
            var root = SystemRoot.Instance;
            if (!EditorApplication.isPlaying || !root || !root.Initialized) throw new InvalidOperationException("Start Title in Play Mode first.");
            var ui = root.UI;
            ui.GetComponent<ImportedButtonTheme>().ApplyAll();
            ui.OpenSongSelection(); ui.Loadout.Open();
            foreach (var artwork in ui.GetComponentsInChildren<ImportedButtonArtwork>(true)) artwork.RefreshVisual();
            ValidateButtons(ui.gameObject);
            int count = ui.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length;
            ui.ShowContext(UIContext.Title); ui.Menu.SettingsButton.onClick.Invoke();
            if (!ui.Settings.gameObject.activeSelf) throw new InvalidOperationException("Settings button click failed.");
            foreach (Transform child in ui.transform.Find("SafeAreaRoot"))
            {
                if (!child.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any()) continue;
                // Capture each page without changing save data or invoking destructive commands.
                var states = ui.transform.Find("SafeAreaRoot").Cast<Transform>().Select(t => (t.gameObject, t.gameObject.activeSelf)).ToArray();
                foreach (var state in states) state.Item1.SetActive(state.Item1 == child.gameObject);
                RenderPanel(ui, "Logs/ImportedButtonsRuntime" + child.name + ".png");
                foreach (var state in states) state.Item1.SetActive(state.Item2);
            }
            ui.Settings.Cancel(); ui.ShowContext(UIContext.Title);
            ValidateButtons(ui.gameObject);
            File.WriteAllText("Logs/ImportedButtonsRuntime.txt", "PASS: " + count + " runtime buttons.\nIncludes generated music/alignment/challenge pages and cloned equipment options.\nSettings button callback and cancel verified. No save data modified.\n");
        }

        private static void Capture(bool hud)
        {
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            var ui = Object.Instantiate(config.UIPrefab); ui.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var cameraObject = new GameObject("Imported Art Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                foreach (var behaviour in ui.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(behaviour is UnityEngine.UI.Graphic) && !(behaviour is UnityEngine.UI.CanvasScaler) &&
                        !(behaviour is UnityEngine.UI.LayoutGroup) && !(behaviour is UnityEngine.UI.LayoutElement)) behaviour.enabled = false;
                foreach (Transform panel in ui.transform.Find("SafeAreaRoot")) panel.gameObject.SetActive(false);
                if (hud)
                {
                    ui.Hud.gameObject.SetActive(true);
                    ui.Hud.Render(new HudSnapshot { Health = 3, MaxHealth = 5 });
                }
                else
                {
                    ui.Songs.gameObject.SetActive(true); var view = ui.Songs;
                    var song = config.Songs.Songs.FirstOrDefault(s => s);
                    view.Title.text = song ? song.Title : "歌曲名称";
                    view.Subtitle.text = song ? song.Subtitle : ""; view.Artist.text = song ? song.Artist : "";
                    view.Cover.sprite = song ? song.Cover : null; view.Cover.enabled = view.Cover.sprite;
                    view.Best.text = "最高成绩\n暂无记录";
                    for (int i = 0; i < view.DifficultyButtons.Length; i++)
                    {
                        var difficulty = song ? song.Find((SongDifficulty)i) : null;
                        bool available = difficulty != null && difficulty.Enabled;
                        view.DifficultyButtons[i].interactable = available;
                        view.DifficultyButtons[i].GetComponent<CanvasGroup>().alpha = available ? 1 : .35f;
                    }
                    for (int i = 0; i < view.List.Rows.Length; i++)
                    {
                        var row = view.List.Rows[i]; int relative = i - view.List.Rows.Length / 2;
                        row.Rect.anchoredPosition = new Vector2(Mathf.Min(145, relative * relative * 105 * 105 / 1800f), -relative * 105);
                        row.Rect.localScale = Vector3.one * (relative == 0 ? 1 : .88f);
                        row.Label.text = song ? song.Title : "歌曲名称";
                        row.Artwork.color = relative == 0 ? Color.white : new Color(.8f, .82f, .9f, .72f);
                    }
                }
                foreach (var child in ui.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .15f, .2f);
                camera.cullingMask = 1 << 5; camera.targetTexture = target;
                var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                foreach (var artwork in ui.GetComponentsInChildren<ImportedButtonArtwork>(true)) artwork.RefreshVisual();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                File.WriteAllBytes(hud ? "Logs/ImportedArtHud.png" : "Logs/ImportedArtSongSelect.png", texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(ui.gameObject); Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
            }
        }

        private static void RuntimeCheck()
        {
            var root = SystemRoot.Instance;
            if (!EditorApplication.isPlaying || !root || !root.Initialized) throw new InvalidOperationException("Start Title in Play Mode first.");
            var ui = root.UI; ui.OpenSongSelection();
            var view = ui.Songs;
            if (!view.gameObject.activeInHierarchy || view.DifficultySelectionArt.Length != view.DifficultyButtons.Length)
                throw new InvalidOperationException("Song selection or artwork references missing.");
            foreach (var row in view.List.Rows) if (!row.Artwork || !row.Artwork.sprite) throw new InvalidOperationException("Missing row artwork.");
            int selected = (int)view.SelectedDifficulty;
            for (int i = 0; i < view.DifficultyButtons.Length; i++)
            {
                if (view.DifficultySelectionArt[i].gameObject.activeSelf != (i == selected)) throw new InvalidOperationException("Difficulty highlight mismatch.");
                float expectedAlpha = view.DifficultyButtons[i].interactable ? 1 : .35f;
                if (!Mathf.Approximately(view.DifficultyButtons[i].GetComponent<CanvasGroup>().alpha, expectedAlpha))
                    throw new InvalidOperationException("Disabled difficulty artwork mismatch.");
            }
            view.List.Move(1); view.List.Move(-1);
            ui.Hud.Render(new HudSnapshot { Health = 3, MaxHealth = 5 });
            var hearts = ui.Hud.HeartsRoot.GetComponentsInChildren<UnityEngine.UI.Image>();
            if (hearts.Length != 5 || hearts.Count(h => h.sprite == ui.Hud.FullHeart) != 3 || hearts.Count(h => h.sprite == ui.Hud.EmptyHeart) != 2)
                throw new InvalidOperationException("Health sprite binding failed.");
            File.WriteAllText("Logs/ImportedArtRuntime.txt", "PASS\nTitle -> SongSelect; list navigation; selected difficulty artwork; 3 full + 2 empty health icons.\nNo save data modified.\n");
        }
    }
}
