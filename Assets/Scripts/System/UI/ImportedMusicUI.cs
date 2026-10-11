using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    // Build these pages alongside the existing views, so older UI prefabs gain the
    // entry point without regenerating the menu, settings or gameplay hierarchy.
    public static class ImportedMusicUI
    {
        private static readonly Color Ink = new(.13f, .16f, .2f), Blue = new(.04f, .55f, .73f);
        public static void Install(SystemUIController ui, SystemRoot root)
        {
            TMP_FontAsset font = ui.Menu.Title.font;
            var entry = Button("MyMusic", ui.Menu.transform, "我的音乐", new Vector2(220, 64), Vector2.zero, font);
            var entryRect = (RectTransform)entry.transform; entryRect.anchorMin = entryRect.anchorMax = new Vector2(1, 1); entryRect.anchoredPosition = new Vector2(-155, -65);
            ui.Menu.MusicButton = entry; entry.onClick.AddListener(ui.OpenImportedMusic);
            Transform parent = ui.Menu.transform.parent;
            ui.ImportedMusic = BuildLibrary(parent, font);
            ui.Alignment = BuildAlignment(parent, font);
            ui.ImportedMusic.transform.SetSiblingIndex(ui.Menu.transform.GetSiblingIndex() + 1);
            ui.Alignment.transform.SetSiblingIndex(ui.Menu.transform.GetSiblingIndex() + 2);
            ui.ImportedMusic.Bind(root); ui.Alignment.Bind(root);
        }
        private static ImportedMusicView BuildLibrary(Transform parent, TMP_FontAsset font)
        {
            var panel = Panel("ImportedMusic", parent); panel.gameObject.SetActive(false);
            var view = panel.gameObject.AddComponent<ImportedMusicView>();
            view.Back = Button("Back", panel, "返回主菜单", new Vector2(220, 65), new Vector2(-745, 430), font);
            Text("Heading", panel, "我的音乐", 52, new Vector2(540, 90), new Vector2(0, 430), font);
            view.Import = Button("Import", panel, "导入歌曲", new Vector2(220, 65), new Vector2(745, 430), font);
            Text("SupportedFormats", panel, "MP3 / WAV · 单首最长 15 分钟 · 导入后可稍后对齐", 25, new Vector2(1500, 60), new Vector2(0, 350), font);
            var scrollRoot = Image("SongList", panel, Color.white, new Vector2(790, 620), new Vector2(-465, -5));
            var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            var viewport = Image("Viewport", scrollRoot.transform, Color.white, Vector2.zero, Vector2.zero); Stretch(viewport.rectTransform);
            viewport.raycastTarget = true; viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport.transform, Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.spacing = 10; layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content; scroll.viewport = viewport.rectTransform; view.Content = content;
            view.RowTemplate = Button("RowTemplate", content, "歌曲", new Vector2(760, 105), Vector2.zero, font);
            view.RowTemplate.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 105;
            var rowText = view.RowTemplate.GetComponentInChildren<TMP_Text>(); rowText.fontSize = 27; rowText.alignment = TextAlignmentOptions.MidlineLeft; rowText.overflowMode = TextOverflowModes.Ellipsis;
            rowText.rectTransform.offsetMin = new Vector2(24, 8); rowText.rectTransform.offsetMax = new Vector2(-24, -8);
            view.RowTemplate.gameObject.SetActive(false);
            Image("DetailsCard", panel, Color.white, new Vector2(830, 620), new Vector2(440, -5));
            view.NameInput = Input("SongName", panel, new Vector2(660, 66), new Vector2(440, 240), font, 80);
            view.RenameButton = Button("Rename", panel, "保存名称", new Vector2(220, 60), new Vector2(440, 155), font);
            view.Details = Text("Details", panel, "", 28, new Vector2(740, 300), new Vector2(440, -45), font);
            view.PreviewButton = Button("Preview", panel, "试听", new Vector2(220, 65), new Vector2(200, -240), font);
            view.Align = Button("Align", panel, "设置对齐", new Vector2(240, 65), new Vector2(455, -240), font);
            view.Remove = Button("Remove", panel, "移除", new Vector2(150, 65), new Vector2(695, -240), font);
            view.Status = Text("Status", panel, "", 26, new Vector2(1600, 90), new Vector2(0, -400), font);
            var preview = new GameObject("MusicPreview"); preview.transform.SetParent(panel, false); view.Preview = preview.AddComponent<MusicPreview>();
            return view;
        }
        private static AudioAlignmentView BuildAlignment(Transform parent, TMP_FontAsset font)
        {
            var panel = Panel("AudioAlignment", parent); panel.gameObject.SetActive(false);
            var view = panel.gameObject.AddComponent<AudioAlignmentView>();
            view.Back = Button("Back", panel, "稍后对齐 / 返回", new Vector2(280, 65), new Vector2(-720, 435), font);
            view.Heading = Text("Heading", panel, "歌曲对齐", 43, new Vector2(1170, 95), new Vector2(155, 435), font);
            view.Heading.overflowMode = TextOverflowModes.Ellipsis;
            Text("Instructions", panel, "手动输入 BPM，点击波形标记首拍，开启节拍器检查是否跟随歌曲。", 27, new Vector2(1650, 65), new Vector2(0, 340), font);
            var waveBack = Image("WaveformBackground", panel, new Color(.1f, .15f, .2f), new Vector2(1600, 300), new Vector2(0, 140));
            var waveform = Rect("Waveform", waveBack.transform, new Vector2(1560, 275), Vector2.zero);
            view.Waveform = waveform.gameObject.AddComponent<AudioWaveformGraphic>(); view.Waveform.color = new Color(.1f, .74f, .9f); view.Waveform.raycastTarget = true;
            view.WindowLabel = Text("Window", panel, "", 25, new Vector2(1550, 50), new Vector2(0, -40), font);
            view.Seek = Slider("Seek", panel, new Vector2(1570, 30), new Vector2(0, -90));
            Text("BpmLabel", panel, "BPM", 29, new Vector2(120, 65), new Vector2(-730, -175), font);
            view.BpmInput = Input("BPM", panel, new Vector2(250, 66), new Vector2(-530, -175), font, 16);
            Text("OffsetLabel", panel, "首拍（秒）", 29, new Vector2(200, 65), new Vector2(-185, -175), font);
            view.OffsetInput = Input("FirstBeat", panel, new Vector2(290, 66), new Vector2(80, -175), font, 20);
            var toggleBox = Image("Metronome", panel, Color.white, new Vector2(45, 45), new Vector2(430, -175)); toggleBox.raycastTarget = true;
            view.Metronome = toggleBox.gameObject.AddComponent<UnityEngine.UI.Toggle>(); view.Metronome.targetGraphic = toggleBox;
            var check = Image("Check", toggleBox.transform, Blue, new Vector2(29, 29), Vector2.zero); view.Metronome.graphic = check;
            Text("MetronomeLabel", panel, "节拍器试听", 29, new Vector2(280, 65), new Vector2(615, -175), font);
            view.PlayButton = Button("Play", panel, "播放", new Vector2(200, 70), new Vector2(-700, -290), font);
            view.Mark = Button("Mark", panel, "当前位置设为首拍", new Vector2(310, 70), new Vector2(-400, -290), font);
            view.Zoom = Button("Zoom", panel, "查看全曲", new Vector2(230, 70), new Vector2(-90, -290), font);
            view.Save = Button("Save", panel, "保存对齐", new Vector2(250, 75), new Vector2(675, -290), font);
            view.Status = Text("Status", panel, "", 27, new Vector2(1620, 110), new Vector2(0, -410), font);
            var preview = new GameObject("MusicPreview"); preview.transform.SetParent(panel, false); view.Preview = preview.AddComponent<MusicPreview>();
            return view;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static RectTransform Panel(string name, Transform parent)
        { var image = Image(name, parent, new Color(.93f, .95f, .97f), Vector2.zero, Vector2.zero); Stretch(image.rectTransform); image.raycastTarget = true; return image.rectTransform; }
        private static UnityEngine.UI.Image Image(string name, Transform parent, Color tint, Vector2 size, Vector2 position)
        { var rect = Rect(name, parent, size, position); var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = tint; image.raycastTarget = false; return image; }
        private static TMP_Text Text(string name, Transform parent, string value, int size, Vector2 dimensions, Vector2 position, TMP_FontAsset font)
        {
            var rect = Rect(name, parent, dimensions, position); var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font;
            text.text = value; text.fontSize = size; text.color = Ink; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.richText = false; return text;
        }
        private static UnityEngine.UI.Button Button(string name, Transform parent, string label, Vector2 size, Vector2 position, TMP_FontAsset font)
        {
            var image = Image(name, parent, Blue, size, position); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var text = Text("Label", image.transform, label, 28, size, Vector2.zero, font); text.color = Color.white; Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(12, 6); text.rectTransform.offsetMax = new Vector2(-12, -6); return button;
        }
        private static TMP_InputField Input(string name, Transform parent, Vector2 size, Vector2 position, TMP_FontAsset font, int limit)
        {
            var image = Image(name, parent, new Color(.84f, .9f, .94f), size, position); image.raycastTarget = true;
            var input = image.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image; input.characterLimit = limit;
            var viewport = Rect("TextArea", image.transform, Vector2.zero, Vector2.zero); Stretch(viewport); viewport.offsetMin = new Vector2(15, 8); viewport.offsetMax = new Vector2(-15, -8);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var label = Text("Text", viewport, "", 29, Vector2.zero, Vector2.zero, font); Stretch(label.rectTransform); label.alignment = TextAlignmentOptions.MidlineLeft;
            input.textViewport = viewport; input.textComponent = (TextMeshProUGUI)label; input.fontAsset = font; input.lineType = TMP_InputField.LineType.SingleLine; input.richText = false;
            return input;
        }
        private static UnityEngine.UI.Slider Slider(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var background = Image(name, parent, new Color(.7f, .78f, .83f), size, position); background.raycastTarget = true;
            var slider = background.gameObject.AddComponent<UnityEngine.UI.Slider>();
            var fill = Image("Fill", background.transform, Blue, Vector2.zero, Vector2.zero); Stretch(fill.rectTransform); slider.fillRect = fill.rectTransform;
            var handle = Image("Handle", background.transform, Color.white, new Vector2(28, 40), Vector2.zero); handle.raycastTarget = true;
            slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight; return slider;
        }
    }
}
