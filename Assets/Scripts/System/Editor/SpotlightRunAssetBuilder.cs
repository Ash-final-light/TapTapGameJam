#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TapTap.Danmaku;
using TapTap.Systems.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
namespace TapTap.Systems.Editor
{
    public static class SpotlightRunAssetBuilder
    {
        private const string Folder = "Assets/Configs/Spotlight/Run";
        private const string Prefabs = "Assets/UI/Spotlight/Prefabs";
        private static TMP_FontAsset font;
        private static readonly Color Ink = new(.12f,.17f,.23f), Blue = new(.04f,.62f,.82f);
        [MenuItem("Tools/Spotlight/接入歌曲装备与结算")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 模式后接入。");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Spotlight/Fonts/SpotlightChinese.asset");
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            if (!config || !config.UIPrefab || !font) throw new InvalidOperationException("现有 SystemConfig、UI Prefab 或中文字体缺失。");
            Configure(config);
            string path = AssetDatabase.GetAssetPath(config.UIPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var ui = root.GetComponent<SystemUIController>(); var parent = root.transform.Find("SafeAreaRoot");
                if (!parent) throw new InvalidOperationException("现有 UI 缺少 SafeAreaRoot。");
                if (!ui.Songs) ui.Songs = BuildSongs(parent);
                if (!ui.Loadout) ui.Loadout = BuildLoadout(parent);
                if (!ui.Result) ui.Result = BuildResult(parent);
                var retryText = ui.Result.Retry.GetComponentInChildren<TMP_Text>();
                if (retryText.text.Contains("↻")) retryText.text = retryText.text.Replace("↻ ","");
                var oldSave = ui.Pause.transform.Find("Buttons/Save"); if (oldSave) UnityEngine.Object.DestroyImmediate(oldSave.gameObject);
                foreach (var panel in new Component[] { ui.Songs, ui.Loadout, ui.Result })
                {
                    panel.transform.SetSiblingIndex(ui.Settings.transform.GetSiblingIndex()); panel.gameObject.SetActive(false);
                    SaveChild(panel.gameObject, Prefabs + "/" + panel.name + ".prefab");
                }
                SaveChild(ui.Pause.gameObject, Prefabs + "/Pause.prefab");
                ui.Dialog.transform.SetAsLastSibling(); ui.ToastLabel.transform.SetAsLastSibling();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
            Debug.Log("[Spotlight] 已接入歌曲选择、装备与结算，已有场景及战斗资源保留。");
        }
        private static void Configure(SystemConfig config)
        {
            SpotlightItemSetup.ConfigureDefaults(config);
            if (config.ScoreFormulas.Length == 0) config.ScoreFormulas = new ScoreFormula[] { Asset<OrbScoreFormula>("OrbScore"), Asset<ComboScoreFormula>("ComboBonus"), Asset<MissScoreFormula>("MissPenalty"), Asset<CompletionScoreFormula>("CompletionBonus") };
            if (config.Weapons.Length == 0) config.Weapons = new[] { AssetDatabase.LoadAssetAtPath<PlayerWeaponDefinition>("Assets/DanmakuExamples/PhaseOne/NormalWeapon.asset"), AssetDatabase.LoadAssetAtPath<PlayerWeaponDefinition>("Assets/DanmakuExamples/PhaseOne/SpreadWeapon.asset") }.Where(w => w).ToArray();
            if (!config.Songs)
            {
                var battle = AssetDatabase.LoadAssetAtPath<BattleConfig>("Assets/DanmakuExamples/PhaseOne/Battle.asset");
                if (!battle) throw new InvalidOperationException("请先配置正式 BattleConfig。");
                var rating = Asset<ThresholdRatingFormula>("Rating"); var song = Asset<SongDefinition>("Remembrance");
                song.Id = "remembrance"; song.Title = battle.Sequence.Music ? battle.Sequence.Music.name : "Remembrance";
                song.Subtitle = "聚光灯 · 演出曲目"; song.Artist = "VISUAL ARTS / Key";
                song.Cover = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Spotlight/Placeholders/Avatar.png") ?? AssetDatabase.LoadAssetAtPath<TapTap.Player.PlayerDefinition>(TapTap.Player.Editor.PlayerEditorAssets.DefinitionPath)?.Portrait;
                song.Difficulties = new[] { new SongDifficultyConfig { Difficulty = SongDifficulty.Normal, Enabled = true, Battle = battle, Rating = rating, LevelEntryId = config.Levels.FirstEntryId },
                    new SongDifficultyConfig { Difficulty = SongDifficulty.Hard, Enabled = false, Rating = rating, LevelEntryId = config.Levels.FirstEntryId },
                    new SongDifficultyConfig { Difficulty = SongDifficulty.Expert, Enabled = false, Rating = rating, LevelEntryId = config.Levels.FirstEntryId } };
                EditorUtility.SetDirty(song); config.Songs = Asset<SongCatalog>("Songs"); config.Songs.Songs = new[] { song }; EditorUtility.SetDirty(config.Songs);
            }
        }
        private static T Asset<T>(string name) where T : ScriptableObject
        { string path = Folder + "/" + name + ".asset"; var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset) return asset; asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset,path); return asset; }
        private static void SaveChild(GameObject child, string path)
        { var clone = UnityEngine.Object.Instantiate(child); clone.name = child.name; PrefabUtility.SaveAsPrefabAsset(clone,path); UnityEngine.Object.DestroyImmediate(clone); }
        private static SongSelectView BuildSongs(Transform parent)
        {
            var panel = Panel("SongSelect",parent); var view = panel.gameObject.AddComponent<SongSelectView>();
            view.Back = Button("Back",panel,"◀ 返回",new Vector2(170,64),new Vector2(-795,435));
            Text("Heading",panel,"歌曲选择",56,Ink,new Vector2(420,90),new Vector2(-485,435));
            var listRect = Rect("LoopSongList",panel,new Vector2(590,720),new Vector2(-580,0));
            var hit = listRect.gameObject.AddComponent<UnityEngine.UI.Image>(); hit.color = new Color(.9f,.93f,.96f); hit.raycastTarget = true;
            listRect.gameObject.AddComponent<UnityEngine.UI.RectMask2D>(); var list = listRect.gameObject.AddComponent<LoopSongList>(); view.List = list;
            list.Rows = new SongListItemView[7];
            for (int i = 0; i < list.Rows.Length; i++)
            {
                var button = Button("SongRow"+i,listRect,"歌曲",new Vector2(360,85),Vector2.zero); var row = button.gameObject.AddComponent<SongListItemView>();
                row.Button = button; row.Label = button.GetComponentInChildren<TMP_Text>(); row.Rect = (RectTransform)button.transform; row.Owner = list; list.Rows[i] = row;
                var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
            }
            list.Previous = Button("Previous",panel,"▲",new Vector2(100,55),new Vector2(-570,375)); list.Next = Button("Next",panel,"▼",new Vector2(100,55),new Vector2(-570,-395));
            var back = Image("CoverBackground",panel,new Color(.8f,.85f,.9f),new Vector2(400,400),new Vector2(170,160));
            view.Cover = Image("Cover",back.transform,Color.white,new Vector2(390,390),Vector2.zero); view.Cover.preserveAspect = true;
            view.Title = Text("Title",panel,"歌曲",42,Ink,new Vector2(720,80),new Vector2(210,-90));
            view.Subtitle = Text("Subtitle",panel,"",27,Ink,new Vector2(720,65),new Vector2(210,-155));
            view.Artist = Text("Artist",panel,"",24,Ink,new Vector2(720,55),new Vector2(210,-210));
            view.Best = Text("Best",panel,"最高成绩",34,Ink,new Vector2(310,190),new Vector2(690,195));
            view.DifficultyButtons = new UnityEngine.UI.Button[3];
            for (int i = 0; i < 3; i++) view.DifficultyButtons[i] = Button("Difficulty"+i,panel,((SongDifficulty)i).ToString(),new Vector2(180,75),new Vector2(-10+i*205,-315));
            view.Confirm = Button("Confirm",panel,"确认选择 ✓",new Vector2(270,85),new Vector2(715,-420));
            view.Empty = Text("Empty",panel,"",28,Ink,new Vector2(780,65),new Vector2(220,-420)); return view;
        }
        private static LoadoutView BuildLoadout(Transform parent)
        {
            var panel = Panel("Loadout",parent); var view = panel.gameObject.AddComponent<LoadoutView>();
            view.Back = Button("Back",panel,"◀ 返回",new Vector2(170,64),new Vector2(-795,435));
            Text("Heading",panel,"开始前准备",52,Ink,new Vector2(550,90),new Vector2(-420,435));
            var left = Image("EquippedCard",panel,Color.white,new Vector2(760,690),new Vector2(-460,20));
            var right = Image("ChoicesCard",panel,Color.white,new Vector2(790,690),new Vector2(390,20));
            view.WeaponLabel = Text("EquippedWeapon",left.transform,"已装备弹幕",32,Ink,new Vector2(710,140),new Vector2(0,235));
            Text("EquippedItemsHeading",left.transform,"已装备道具",32,Ink,new Vector2(650,65),new Vector2(0,105));
            view.EquippedRoot = Rect("EquippedItems",left.transform,new Vector2(700,165),new Vector2(0,-20));
            Grid(view.EquippedRoot,new Vector2(205,150),3);
            view.Description = Text("Description",left.transform,"选择弹幕或已获得道具查看说明。",27,Ink,new Vector2(690,185),new Vector2(0,-225));
            Text("WeaponHeading",right.transform,"玩家弹幕类型",32,Ink,new Vector2(740,65),new Vector2(0,295));
            view.WeaponsRoot = Rect("Weapons",right.transform,new Vector2(710,150),new Vector2(0,185)); Grid(view.WeaponsRoot,new Vector2(215,135),3);
            Text("ItemsHeading",right.transform,"道具",32,Ink,new Vector2(740,65),new Vector2(0,60));
            view.ItemsRoot = Rect("Items",right.transform,new Vector2(710,310),new Vector2(0,-140)); Grid(view.ItemsRoot,new Vector2(215,135),3);
            view.OptionTemplate = Button("OptionTemplate",panel,"选项",new Vector2(215,135),Vector2.zero);
            var label = view.OptionTemplate.GetComponentInChildren<TMP_Text>(); label.fontSize = 24; label.rectTransform.offsetMax = new Vector2(-8,-50);
            var optionIcon = Image("Icon",view.OptionTemplate.transform,Color.white,new Vector2(50,45),new Vector2(0,37)); optionIcon.preserveAspect = true;
            view.SlotTemplate = UnityEngine.Object.Instantiate(view.OptionTemplate,panel); view.SlotTemplate.name = "SlotTemplate";
            view.OptionTemplate.gameObject.SetActive(false); view.SlotTemplate.gameObject.SetActive(false);
            view.StartButton = Button("Start",panel,"Link start !",new Vector2(290,85),new Vector2(690,-420)); return view;
        }
        private static ResultView BuildResult(Transform parent)
        {
            var panel = Panel("Result",parent); var view = panel.gameObject.AddComponent<ResultView>();
            var coverBack = Image("CoverBackground",panel,new Color(.8f,.85f,.9f),new Vector2(570,640),new Vector2(-560,50));
            view.Cover = Image("Cover",coverBack.transform,Color.white,new Vector2(550,620),Vector2.zero); view.Cover.preserveAspect = true;
            view.Title = Text("Title",panel,"歌曲",43,Ink,new Vector2(930,130),new Vector2(260,370));
            view.Difficulty = Text("Difficulty",panel,"Normal",28,new Color(.85f,.45f,.05f),new Vector2(220,65),new Vector2(730,420));
            var scoreBack = Image("ScoreCard",panel,new Color(.85f,.88f,.91f),new Vector2(1000,325),new Vector2(310,125));
            view.Score = Text("Score",scoreBack.transform,"000000",100,Ink,new Vector2(610,190),new Vector2(-145,0));
            view.Grade = Text("Grade",scoreBack.transform,"A",130,Blue,new Vector2(260,240),new Vector2(345,0));
            view.Statistics = Text("Statistics",panel,"最高连击：0\nMISS：0",32,Ink,new Vector2(550,230),new Vector2(85,-180));
            view.DropIcon = Image("DropIcon",panel,Color.white,new Vector2(100,100),new Vector2(520,-180)); view.DropIcon.preserveAspect = true;
            view.Drop = Text("Drop",panel,"获得道具",26,Ink,new Vector2(410,130),new Vector2(650,-280));
            view.SaveStatusLabel = Text("SaveStatus",panel,"",24,Ink,new Vector2(850,80),new Vector2(-340,-415));
            view.Retry = Button("Retry",panel,"重试",new Vector2(225,80),new Vector2(410,-420)); view.Next = Button("Next",panel,"下一步 ▶",new Vector2(225,80),new Vector2(670,-420));
            view.RetrySave = Button("RetrySave",panel,"重试保存",new Vector2(210,65),new Vector2(85,-345)); return view;
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        { var go = new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false); var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f); r.sizeDelta = size; r.anchoredPosition = position; return r; }
        private static UnityEngine.UI.Image Image(string name,Transform parent,Color color,Vector2 size,Vector2 position)
        { var r = Rect(name,parent,size,position); var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image; }
        private static RectTransform Panel(string name,Transform parent)
        { var image = Image(name,parent,new Color(.93f,.95f,.97f),Vector2.zero,Vector2.zero); var r = image.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; image.raycastTarget = true; return r; }
        private static TMP_Text Text(string name,Transform parent,string value,int size,Color color,Vector2 dimensions,Vector2 position)
        { var r = Rect(name,parent,dimensions,position); var text = r.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.text = value; text.fontSize = size; text.color = color; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; return text; }
        private static UnityEngine.UI.Button Button(string name,Transform parent,string label,Vector2 size,Vector2 position)
        {
            var image = Image(name,parent,Blue,size,position); image.raycastTarget = true; var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.disabledColor = new Color(.32f,.35f,.4f,.5f); button.colors = colors;
            var text = Text("Label",image.transform,label,29,Color.white,size,Vector2.zero); text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(8,6); text.rectTransform.offsetMax = new Vector2(-8,-6); return button;
        }
        private static void Grid(RectTransform root,Vector2 cell,int columns)
        { var grid = root.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>(); grid.cellSize = cell; grid.spacing = new Vector2(20,20); grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = columns; grid.childAlignment = TextAnchor.MiddleCenter; }
    }
}
#endif
