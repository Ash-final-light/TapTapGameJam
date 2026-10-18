#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using TapTap.Systems.UI;

namespace TapTap.Systems.Editor
{
    public static class SpotlightAssetBuilder
    {
        private const string UIPath="Assets/UI/Spotlight", ConfigPath="Assets/Configs/Spotlight";
        private static TMP_FontAsset font;
        private static Sprite avatar, icon, heart, emptyHeart, solid;
        private static readonly Color Blue=new(0.04f,0.62f,0.82f), Gold=new(1,0.72f,0.26f), Ink=new(0.13f,0.16f,0.2f);
        [MenuItem("Tools/Spotlight/搭建系统UI与占位场景")]
        public static void Generate()
        {
            if(Application.isPlaying) throw new InvalidOperationException("请退出 Play 模式后搭建。");
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolders();
            if(!AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            {
                if(Application.isBatchMode) throw new InvalidOperationException("请先导入 TMP Essential Resources，再运行批处理搭建。");
                AssetDatabase.importPackageCompleted += OnResourcesImported;
                TMP_PackageResourceImporter.ImportResources(true,false,false); return;
            }
            PrepareFont(); PrepareSprites();
            solid=SpriteFile("Solid", (x,y)=>Color.white);
            var defaults=GetOrCreate<SystemDefaults>(ConfigPath+"/SystemDefaults.asset");
            var levels=GetOrCreate<LevelEntryCatalog>(ConfigPath+"/LevelEntries.asset");
            if(levels.Entries.Length==0) levels.Entries=new[] { new LevelEntry { Id="tutorial",DisplayName="新手关卡（占位）",ScenePath="Assets/Scenes/GameplayPlaceholder.unity" } };
            var tips=GetOrCreate<LoadingTipConfig>(ConfigPath+"/LoadingTips.asset");
            if(tips.Tips.Length==0) tips.Tips=new[] { new LoadingTip { Id="items",Text="按住 Tab 选择道具，松开后按 R 使用。" },
                new LoadingTip { Id="skills",Text="技能 1：Q　技能 2：E。键位可在设置中修改。" } };
            string csvPath=ConfigPath+"/LoadingTips.csv";
            if(!File.Exists(csvPath)) File.WriteAllText(csvPath,"Id,Text,Weight,Enabled\nitems,按住 Tab 选择道具，松开后按 R 使用。,1,true\nskills,技能 1：Q　技能 2：E。键位可在设置中修改。,1,true\n",new System.Text.UTF8Encoding(true));
            var controls=CreateControls();
            var ui=CreateUI();
            var config=GetOrCreate<SystemConfig>(ConfigPath+"/SystemConfig.asset");
            config.Defaults=defaults;config.Levels=levels;config.LoadingTips=tips;config.Controls=controls;config.UIPrefab=ui;
            foreach(var asset in new UnityEngine.Object[] {defaults,levels,tips,config}) EditorUtility.SetDirty(asset);
            var root=new GameObject("SystemRoot"); var component=root.AddComponent<SystemRoot>();
            var serialized=new SerializedObject(component); serialized.FindProperty("config").objectReferenceValue=config; serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,UIPath+"/Prefabs/SystemRoot.prefab"); UnityEngine.Object.DestroyImmediate(root);
            CreateScene("Assets/Scenes/Title.unity",prefab,false);
            CreateScene("Assets/Scenes/Loading.unity",prefab,false);
            CreateScene("Assets/Scenes/GameplayPlaceholder.unity",prefab,true);
            var buildScenes=new List<EditorBuildSettingsScene> {new(config.TitleScene,true),new(config.LoadingScene,true),new("Assets/Scenes/GameplayPlaceholder.unity",true)};
            foreach(var scene in EditorBuildSettings.scenes) if(!buildScenes.Exists(s=>s.path==scene.path)) buildScenes.Add(scene);
            EditorBuildSettings.scenes=buildScenes.ToArray();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SpotlightRunAssetBuilder.Install();
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/SpotlightAssetBuild.txt","PASS: UI Prefabs, SystemRoot, three scenes, input actions, settings and loading configuration generated through Unity Editor API.\n");
            Debug.Log("[Spotlight] 系统 UI 与占位场景已生成。打开 Title 场景开始。");
            if(!Application.isBatchMode) EditorSceneManager.OpenScene(config.TitleScene);
        }
        private static void OnResourcesImported(string package)
        { AssetDatabase.importPackageCompleted -= OnResourcesImported; EditorApplication.delayCall += Generate; }
        private static void EnsureFolders()
        {
            foreach(string path in new[]{UIPath,UIPath+"/Prefabs",UIPath+"/Placeholders",UIPath+"/Fonts",ConfigPath,"Assets/Inputs","Assets/Scenes"}) Directory.CreateDirectory(path);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        private static T GetOrCreate<T>(string path) where T:ScriptableObject
        { var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset)return asset;asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);return asset; }
        private static void PrepareFont()
        {
            string path=UIPath+"/Fonts/SpotlightChinese.asset";
            font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path); if(font)return;
            var source=AssetDatabase.LoadAssetAtPath<Font>(UIPath+"/Fonts/NotoSansSC-Regular.otf");
            if(!source)throw new InvalidOperationException("缺少占位中文字体 NotoSansSC-Regular.otf。");
            font=TMP_FontAsset.CreateFontAsset(source,64,8,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
            font.name="SpotlightChinese";font.isMultiAtlasTexturesEnabled=true;
            AssetDatabase.CreateAsset(font,path); AssetDatabase.AddObjectToAsset(font.material,font);
            foreach(var texture in font.atlasTextures) if(texture)AssetDatabase.AddObjectToAsset(texture,font);
            font.TryAddCharacters("聚光灯开始游戏继续设置退出保存进度取消重置应用画质声音操作键位分辨率帧上限显示模式总音量背景音乐效比例输入输出默认跟随系统后续接关闭未授权技能道具选择使用暂停载中准备舞台新手占位调试暂无可选数量血体力方向左右移动跳跃飞行长按闪避官方推荐0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz /：，。！？（）—×%+",out _);
            EditorUtility.SetDirty(font);
        }
        private static void PrepareSprites()
        {
            avatar=SpriteFile("Avatar",(x,y)=> { float dx=x-64,dy=y-64; return dx*dx+dy*dy<3300 ? new Color(0.95f,0.6f,0.68f) : Color.clear; });
            icon=SpriteFile("Item",(x,y)=> Mathf.Abs(x-64)+Mathf.Abs(y-64)<52 ? new Color(0.65f,0.8f,1) : Color.clear);
            heart=SpriteFile("Heart",(x,y)=> HeartInside(x,y) ? new Color(1,0.38f,0.56f) : Color.clear);
            emptyHeart=SpriteFile("HeartEmpty",(x,y)=> HeartInside(x,y) ? new Color(0.36f,0.39f,0.43f) : Color.clear);
        }
        private static bool HeartInside(int x,int y)
        { float a=(x-64)/48f,b=(y-64)/48f; float c=a*a+b*b-0.7f;return c*c*c-a*a*b*b*b<0; }
        private static Sprite SpriteFile(string name,Func<int,int,Color> pixel)
        {
            string path=UIPath+"/Placeholders/"+name+".png";
            if(!File.Exists(path)) {var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);for(int y=0;y<128;y++)for(int x=0;x<128;x++)texture.SetPixel(x,y,pixel(x,y));
                texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);}
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static InputActionAsset CreateControls()
        {
            const string path="Assets/Inputs/SpotlightControls.inputactions";
            var existing=AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);if(existing)return existing;
            var asset=ScriptableObject.CreateInstance<InputActionAsset>();asset.name="SpotlightControls";
            var game=new InputActionMap("Gameplay");asset.AddActionMap(game);
            for(int i=0;i<SpotlightActions.Names.Length;i++)game.AddAction(SpotlightActions.Names[i],InputActionType.Button,SpotlightActions.DefaultPaths[i],expectedControlLayout:"Button");
            game.AddAction("Pause",InputActionType.Button,"<Keyboard>/escape");
            game.AddAction("Fire",InputActionType.Button,"<Mouse>/leftButton",expectedControlLayout:"Button");
            game.AddAction("Aim",InputActionType.Value,"<Mouse>/position",expectedControlLayout:"Vector2");
            for(int i=0;i<10;i++)game.AddAction("Slot"+i,InputActionType.Button,"<Keyboard>/"+(i==9?"0":(i+1).ToString()));
            var ui=new InputActionMap("UI");asset.AddActionMap(ui);
            ui.AddAction("Navigate",InputActionType.Value,expectedControlLayout:"Vector2").AddCompositeBinding("2DVector")
                .With("Up","<Keyboard>/upArrow").With("Down","<Keyboard>/downArrow").With("Left","<Keyboard>/leftArrow").With("Right","<Keyboard>/rightArrow");
            ui.AddAction("Submit",InputActionType.Button,"<Keyboard>/enter");ui.AddAction("Cancel",InputActionType.Button,"<Keyboard>/escape");
            ui.AddAction("Point",InputActionType.PassThrough,"<Mouse>/position",expectedControlLayout:"Vector2");
            ui.AddAction("Click",InputActionType.PassThrough,"<Mouse>/leftButton",expectedControlLayout:"Button");
            ui.AddAction("ScrollWheel",InputActionType.PassThrough,"<Mouse>/scroll",expectedControlLayout:"Vector2");
            File.WriteAllText(path,asset.ToJson());UnityEngine.Object.DestroyImmediate(asset);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }
        private static SystemUIController CreateUI()
        {
            var go=new GameObject("SpotlightUI",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=0.5f;
            var controller=go.AddComponent<SystemUIController>();
            var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(go.transform);
            controller.EventSystem=events.GetComponent<EventSystem>();controller.InputModule=events.GetComponent<InputSystemUIInputModule>();
            var safe=Rect("SafeAreaRoot",go.transform);Stretch(safe);safe.gameObject.AddComponent<AshFramework.UI.ScreenAdaptation.Components.SafeArea>();
            controller.Menu=BuildMenu(safe);controller.Loading=BuildLoading(safe);controller.Hud=BuildHud(safe);
            controller.Pause=BuildPause(safe);controller.Settings=BuildSettings(safe);controller.Dialog=BuildDialog(safe);
            controller.ToastLabel=Text("Toast",safe,"",28,Color.white);SetRect(controller.ToastLabel.rectTransform,new Vector2(.5f,.95f),new Vector2(1300,65));
            var toastBackground=controller.ToastLabel.gameObject.AddComponent<UnityEngine.UI.Shadow>();toastBackground.effectColor=Color.black;
            foreach(var view in new Component[]{controller.Menu,controller.Loading,controller.Hud,controller.Pause,controller.Settings,controller.Dialog})
                SaveChildPrefab(view.gameObject,UIPath+"/Prefabs/"+view.gameObject.name+".prefab");
            var saved=PrefabUtility.SaveAsPrefabAsset(go,UIPath+"/Prefabs/SpotlightUI.prefab").GetComponent<SystemUIController>();
            UnityEngine.Object.DestroyImmediate(go);return saved;
        }
        private static void SaveChildPrefab(GameObject child,string path)
        { var clone=UnityEngine.Object.Instantiate(child);clone.name=child.name;PrefabUtility.SaveAsPrefabAsset(clone,path);UnityEngine.Object.DestroyImmediate(clone); }
        private static MenuView BuildMenu(Transform parent)
        {
            var panel=Panel("MainMenu",parent,new Color(.72f,.77f,.82f));var view=panel.gameObject.AddComponent<MenuView>();
            var background=Image("BackgroundPlaceholder",panel,new Color(.55f,.63f,.72f));Stretch(background.rectTransform);
            var stripe=Image("BackgroundAccent",panel,new Color(.7f,.78f,.85f));SetRect(stripe.rectTransform,new Vector2(.74f,.5f),new Vector2(850,1300));stripe.transform.localRotation=Quaternion.Euler(0,0,-18);
            view.Title=Text("GameTitle",panel,"聚光灯",100,Ink);SetRect(view.Title.rectTransform,new Vector2(.26f,.74f),new Vector2(700,170));
            var note=Text("PlaceholderNote",panel,"主菜单背景 / 标题占位",25,new Color(.25f,.3f,.38f));SetRect(note.rectTransform,new Vector2(.27f,.61f),new Vector2(650,70));
            var buttons=Rect("Buttons",panel);SetRect(buttons,new Vector2(.5f,.14f),new Vector2(1360,90));Horizontal(buttons,36);
            view.StartButton=Button("StartGame",buttons,"开始游戏",260,80);view.ContinueButton=Button("Continue",buttons,"继续游戏",260,80);
            view.SettingsButton=Button("Settings",buttons,"设置",260,80);view.ExitButton=Button("Exit",buttons,"退出游戏",260,80);
            return view;
        }
        private static LoadingView BuildLoading(Transform parent)
        {
            var panel=Panel("Loading",parent,new Color(.045f,.055f,.075f));var view=panel.gameObject.AddComponent<LoadingView>();
            var character=Image("RunningCharacterPlaceholder",panel,Color.white,avatar);SetRect(character.rectTransform,new Vector2(.5f,.5f),new Vector2(150,180),new Vector2(0,80));view.Character=character.rectTransform;
            view.Tip=Text("Tip",panel,"加载提示",34,Color.white);SetRect(view.Tip.rectTransform,new Vector2(.5f,.4f),new Vector2(1300,100));
            view.Status=Text("LoadingStatus",panel,"载入中……",28,Color.white);SetRect(view.Status.rectTransform,new Vector2(.82f,.08f),new Vector2(560,70));
            view.Progress=Slider("Progress",panel,450);SetRect((RectTransform)view.Progress.transform,new Vector2(.82f,.14f),new Vector2(450,30));view.Progress.minValue=0;view.Progress.maxValue=1;view.Progress.wholeNumbers=false;view.Progress.interactable=false;
            view.Retry=Button("Retry",panel,"重试",220,70);SetRect((RectTransform)view.Retry.transform,new Vector2(.42f,.16f),new Vector2(220,70));
            view.Back=Button("Back",panel,"返回主菜单",260,70);SetRect((RectTransform)view.Back.transform,new Vector2(.58f,.16f),new Vector2(260,70));return view;
        }
        private static PauseView BuildPause(Transform parent)
        {
            var panel=Panel("Pause",parent,new Color(0,0,0,.65f));var view=panel.gameObject.AddComponent<PauseView>();
            var title=Text("PauseTitle",panel,"游戏暂停",92,Color.white);SetRect(title.rectTransform,new Vector2(.5f,.72f),new Vector2(750,145));
            var buttons=Rect("Buttons",panel);SetRect(buttons,new Vector2(.5f,.4f),new Vector2(300,450));Vertical(buttons,30);
            view.ResumeButton=Button("Resume",buttons,"继续游戏",300,80);
            view.SettingsButton=Button("Settings",buttons,"设置",300,80);view.ExitButton=Button("Exit",buttons,"退出游戏",300,80);return view;
        }
        private static DialogView BuildDialog(Transform parent)
        {
            var shade=Panel("Dialog",parent,new Color(0,0,0,.7f));var view=shade.gameObject.AddComponent<DialogView>();
            var card=Rect("Card",shade);SetRect(card,new Vector2(.5f,.5f),new Vector2(960,380));Image("Background",card,new Color(.16f,.2f,.25f)).rectTransform.Stretch();
            view.Message=Text("Message",card,"确认",34,Color.white);SetRect(view.Message.rectTransform,new Vector2(.5f,.65f),new Vector2(860,180));
            view.ConfirmButton=Button("Confirm",card,"确认",260,75);SetRect((RectTransform)view.ConfirmButton.transform,new Vector2(.32f,.2f),new Vector2(260,75));
            view.CancelButton=Button("Cancel",card,"取消",260,75);SetRect((RectTransform)view.CancelButton.transform,new Vector2(.68f,.2f),new Vector2(260,75));return view;
        }
        private static SettingsView BuildSettings(Transform parent)
        {
            var shade=Panel("Settings",parent,new Color(0,0,0,.55f));var view=shade.gameObject.AddComponent<SettingsView>();
            var card=Rect("Card",shade);card.anchorMin=new Vector2(.08f,.05f);card.anchorMax=new Vector2(.92f,.95f);card.offsetMin=card.offsetMax=Vector2.zero;
            var cardBackground=Image("CardBackground",card,new Color(.74f,.78f,.81f,.88f));Stretch(cardBackground.rectTransform);
            var tabs=Rect("Tabs",card);tabs.anchorMin=new Vector2(0,1);tabs.anchorMax=new Vector2(1,1);tabs.pivot=new Vector2(.5f,1);tabs.sizeDelta=new Vector2(0,86);tabs.anchoredPosition=Vector2.zero;Horizontal(tabs,35,32);
            view.Tabs=new UnityEngine.UI.Button[3];view.TabBorders=new UnityEngine.UI.Image[3];
            string[] tabLabels={"画质","声音","操作键位"};
            for(int i=0;i<3;i++){view.Tabs[i]=Button("Tab"+i,tabs,tabLabels[i],420,64,Color.white,Ink);view.TabBorders[i]=Image("SelectedBorder",view.Tabs[i].transform,Color.white);
                Stretch(view.TabBorders[i].rectTransform);view.TabBorders[i].transform.SetAsFirstSibling();var inner=view.Tabs[i].GetComponent<UnityEngine.UI.Image>();inner.color=Color.white;
                var outline=view.TabBorders[i].gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=Gold;outline.effectDistance=new Vector2(4,-4);}
            var content=Rect("Pages",card);content.anchorMin=new Vector2(0,0);content.anchorMax=new Vector2(1,1);content.offsetMin=new Vector2(50,105);content.offsetMax=new Vector2(-50,-110);
            view.Pages=new GameObject[3];
            var graphics=Scroll("Graphics",content);view.Pages[0]=graphics.parent.parent.gameObject;
            view.Resolution=SettingDropdown(graphics,"分辨率","Resolution");view.Resolution.gameObject.AddComponent<DropdownAvailability>();
            view.FrameRate=SettingDropdown(graphics,"帧率上限","FrameRate");view.Mode=SettingDropdown(graphics,"显示模式","DisplayMode");
            view.Mode.AddOptions(new List<string>{"窗口化显示","全屏显示","无框窗口化显示"});
            var graphicsNote=Text("DisplayNote",graphics,"分辨率与显示模式将在应用后生效。",24,Ink);Layout(graphicsNote.gameObject,1100,70);
            var sound=Scroll("Sound",content);view.Pages[1]=sound.parent.parent.gameObject;view.Volumes=new UnityEngine.UI.Slider[4];view.VolumeLabels=new TMP_Text[4];
            string[] soundLabels={"总音量","背景音乐","音效","音量比例"};
            for(int i=0;i<4;i++){var row=SettingRow(sound,"Volume"+i,soundLabels[i]);view.Volumes[i]=Slider("Slider",row,680);view.VolumeLabels[i]=Text("Value",row,"100",28,Ink);Layout(view.VolumeLabels[i].gameObject,100,64);}
            foreach(string label in new[]{"音频输入","音频输出"}){var row=SettingRow(sound,label,label);var device=Dropdown("Device",row,700);device.AddOptions(new List<string>{"跟随系统"});device.interactable=false;}
            var keys=Scroll("Keys",content);view.Pages[2]=keys.parent.parent.gameObject;
            var header=Rect("Header",keys);Layout(header.gameObject,1300,54);Horizontal(header,30);TextCell(header,"键位名称",420);TextCell(header,"当前键位",420);TextCell(header,"官方推荐键位",420);
            string[] actions={"Left","Right","JumpFly","JumpFly","Dodge","Skill1","Skill2","ItemSelect","ItemUse"};
            string[] labels={"左移动","右移动","跳跃","飞行（长按）","闪避","技能 1","技能 2","道具选择栏","道具使用栏"};
            string[] recommended={"←","→","K","L",";","—","—","—","—"};view.KeyRows=new KeybindRowView[actions.Length];
            for(int i=0;i<actions.Length;i++){var row=Rect("BindingRow"+i,keys);Layout(row.gameObject,1300,64);Horizontal(row,30);
                var item=row.gameObject.AddComponent<KeybindRowView>();item.ActionName=actions[i];item.Label=TextCell(row,labels[i],420);
                item.Button=Button("Binding",row,"待绑定",420,64,Color.white,Ink);item.BindingText=item.Button.GetComponentInChildren<TMP_Text>();TextCell(row,recommended[i],420);view.KeyRows[i]=item;}
            TextCell(keys,"Esc 固定暂停；跳跃与长按飞行共用绑定。",1200);
            var footer=Rect("Footer",card);footer.anchorMin=new Vector2(0,0);footer.anchorMax=new Vector2(1,0);footer.pivot=new Vector2(.5f,0);footer.sizeDelta=new Vector2(0,88);Horizontal(footer,24,28);footer.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment=TextAnchor.MiddleRight;
            view.ResetButton=Button("Reset",footer,"重置",210,64);view.ApplyButton=Button("Apply",footer,"应用",210,64);view.CancelButton=Button("Cancel",footer,"取消",210,64);
            var wait=Panel("RebindOverlay",shade,new Color(0,0,0,.85f));view.RebindOverlay=wait.gameObject;view.RebindPrompt=Text("Prompt",wait,"按下新键",46,Color.white);SetRect(view.RebindPrompt.rectTransform,new Vector2(.5f,.5f),new Vector2(1500,280));wait.gameObject.SetActive(false);
            return view;
        }
        private static HudView BuildHud(Transform parent)
        {
            var panel=Rect("HUD",parent);Stretch(panel);var view=panel.gameObject.AddComponent<HudView>();
            view.Portrait=Image("Portrait",panel,Color.white,avatar);SetRect(view.Portrait.rectTransform,new Vector2(0,1),new Vector2(150,150),new Vector2(100,-100));
            var heartCard=Image("HealthCard",panel,Color.white);SetRect(heartCard.rectTransform,new Vector2(0,1),new Vector2(470,68),new Vector2(425,-64));
            view.HeartsRoot=Rect("Hearts",heartCard.transform);Stretch(view.HeartsRoot);Horizontal(view.HeartsRoot,6,12);
            view.HeartsRoot.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment=TextAnchor.MiddleLeft;
            var element=view.HeartsRoot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();element.ignoreLayout=true;
            view.HeartTemplate=Image("HeartTemplate",panel,Color.white,heart);Layout(view.HeartTemplate.gameObject,50,50);view.HeartTemplate.gameObject.SetActive(false);view.FullHeart=heart;view.EmptyHeart=emptyHeart;
            var equipped=Image("EquippedCard",panel,Color.white);SetRect(equipped.rectTransform,new Vector2(0,1),new Vector2(470,68),new Vector2(425,-142));
            view.EquippedIcon=Image("EquippedIcon",equipped.transform,Color.white,icon);SetRect(view.EquippedIcon.rectTransform,new Vector2(.12f,.5f),new Vector2(60,60));
            view.EquippedCount=Text("Count",equipped.transform,"0",28,Ink);SetRect(view.EquippedCount.rectTransform,new Vector2(.29f,.5f),new Vector2(100,50));
            var staminaBack=Image("StaminaBackground",panel,Color.white);SetRect(staminaBack.rectTransform,new Vector2(0,0),new Vector2(32,345),new Vector2(34,220));
            view.Stamina=Image("Stamina",staminaBack.transform,Blue,solid);Stretch(view.Stamina.rectTransform);view.Stamina.type=UnityEngine.UI.Image.Type.Filled;view.Stamina.fillMethod=UnityEngine.UI.Image.FillMethod.Vertical;view.Stamina.fillOrigin=0;
            view.StaminaText=Text("StaminaValue",panel,"100/100",22,Ink);SetRect(view.StaminaText.rectTransform,new Vector2(0,0),new Vector2(160,44),new Vector2(85,28));
            view.Skill1=Image("Skill1",panel,Color.white,icon);SetRect(view.Skill1.rectTransform,new Vector2(0,0),new Vector2(100,100),new Vector2(110,355));
            view.Microphone=Image("Microphone",panel,Color.gray,avatar);SetRect(view.Microphone.rectTransform,new Vector2(0,0),new Vector2(100,100),new Vector2(110,225));
            view.Skill2=Image("Skill2",panel,Color.white,icon);SetRect(view.Skill2.rectTransform,new Vector2(0,0),new Vector2(100,100),new Vector2(110,95));
            view.Skill1Key=Text("KeyHint",view.Skill1.transform,"Q",24,Ink);SetRect(view.Skill1Key.rectTransform,new Vector2(.5f,.05f),new Vector2(70,30));
            view.Skill2Key=Text("KeyHint",view.Skill2.transform,"E",24,Ink);SetRect(view.Skill2Key.rectTransform,new Vector2(.5f,.05f),new Vector2(70,30));
            view.MicrophoneLabel=Text("MicrophoneState",panel,"关闭",22,Ink);SetRect(view.MicrophoneLabel.rectTransform,new Vector2(0,0),new Vector2(130,40),new Vector2(110,163));
            var use=Image("ItemUse",panel,Color.white);SetRect(use.rectTransform,new Vector2(0,0),new Vector2(125,90),new Vector2(250,225));
            view.UseIcon=Image("Icon",use.transform,Color.white,icon);SetRect(view.UseIcon.rectTransform,new Vector2(.35f,.55f),new Vector2(70,70));
            view.UseCount=Text("Count",use.transform,"0",23,Ink);SetRect(view.UseCount.rectTransform,new Vector2(.8f,.5f),new Vector2(40,40));
            view.UseKey=Text("KeyHint",use.transform,"R",24,Ink);SetRect(view.UseKey.rectTransform,new Vector2(.5f,0),new Vector2(100,32),new Vector2(0,-20));
            view.SelectorKey=Text("SelectorKey",panel,"按住 Tab 选择道具",22,Ink);SetRect(view.SelectorKey.rectTransform,new Vector2(0,0),new Vector2(370,40),new Vector2(365,145));
            var selector=Image("ItemSelector",panel,new Color(.97f,.98f,1,.96f));SetRect(selector.rectTransform,new Vector2(0,0),new Vector2(920,115),new Vector2(630,330));view.Selector=selector.gameObject;
            var slotParent=Rect("Slots",selector.transform);Stretch(slotParent);Horizontal(slotParent,8,12);slotParent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment=TextAnchor.MiddleLeft;view.Slots=new ItemSlotView[10];
            for(int i=0;i<10;i++){var btn=Button("Slot"+i,slotParent,"",78,94,Color.white,Ink);var slot=btn.gameObject.AddComponent<ItemSlotView>();slot.Button=btn;
                slot.Border=Image("Border",btn.transform,Color.white);Stretch(slot.Border.rectTransform);slot.Border.transform.SetAsFirstSibling();
                var outline=slot.Border.gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=Gold;outline.effectDistance=new Vector2(3,-3);
                slot.Icon=Image("Icon",btn.transform,Color.white,icon);SetRect(slot.Icon.rectTransform,new Vector2(.5f,.6f),new Vector2(46,46));
                slot.Number=Text("Number",btn.transform,(i+1).ToString(),19,Ink);SetRect(slot.Number.rectTransform,new Vector2(.18f,.17f),new Vector2(22,24));
                slot.Count=Text("Count",btn.transform,"0",19,Ink);SetRect(slot.Count.rectTransform,new Vector2(.76f,.17f),new Vector2(35,24));view.Slots[i]=slot;}
            view.SelectionHint=Text("SelectionHint",selector.transform,"数字键选择",24,Ink);SetRect(view.SelectionHint.rectTransform,new Vector2(.5f,1),new Vector2(750,36),new Vector2(0,26));
            view.PauseButton=Button("Pause",panel,"≡",70,70,new Color(.08f,.1f,.12f),Color.white);SetRect((RectTransform)view.PauseButton.transform,new Vector2(1,1),new Vector2(70,70),new Vector2(-60,-60));
            view.Warning=Text("Warning",panel,"",36,Gold);SetRect(view.Warning.rectTransform,new Vector2(.5f,.62f),new Vector2(900,100));return view;
        }
        private static void CreateScene(string path,GameObject system,bool gameplay)
        {
            Scene scene;
            if(File.Exists(path))scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);else scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(!UnityEngine.Object.FindObjectOfType<Camera>()){var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";camera.transform.position=new Vector3(0,0,-10);camera.GetComponent<Camera>().orthographic=true;}
            var current=UnityEngine.Object.FindObjectOfType<SystemRoot>();if(!current)PrefabUtility.InstantiatePrefab(system,scene);
            if(gameplay && !GameObject.Find("StagePlaceholder"))
            {
                var stage=new GameObject("StagePlaceholder");var sprite=stage.AddComponent<SpriteRenderer>();sprite.sprite=icon;sprite.color=new Color(.35f,.4f,.45f,.35f);stage.transform.localScale=new Vector3(6,4,1);
                var camera=UnityEngine.Object.FindObjectOfType<Camera>();camera.backgroundColor=new Color(.7f,.73f,.76f);camera.clearFlags=CameraClearFlags.SolidColor;
            }
            EditorSceneManager.SaveScene(scene,path);
        }
        private static RectTransform Rect(string name,Transform parent)
        {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>();}
        private static RectTransform Panel(string name,Transform parent,Color color)
        {var image=Image(name,parent,color);Stretch(image.rectTransform);image.raycastTarget=true;return image.rectTransform;}
        private static UnityEngine.UI.Image Image(string name,Transform parent,Color color,Sprite sprite=null)
        {var rect=Rect(name,parent);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.sprite=sprite;image.raycastTarget=false;return image;}
        private static void Stretch(this RectTransform rect)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        private static void SetRect(RectTransform rect,Vector2 anchor,Vector2 size,Vector2 offset=default)
        {rect.anchorMin=rect.anchorMax=anchor;rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=offset;}
        private static TMP_Text Text(string name,Transform parent,string value,int size,Color color)
        {var rect=Rect(name,parent);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.text=value;text.fontSize=size;text.color=color;
            text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;text.enableWordWrapping=true;return text;}
        private static TMP_Text TextCell(Transform parent,string value,float width)
        {var text=Text("Label",parent,value,28,Ink);Layout(text.gameObject,width,64);return text;}
        private static void Layout(GameObject go,float width,float height)
        {var layout=go.GetComponent<UnityEngine.UI.LayoutElement>()??go.AddComponent<UnityEngine.UI.LayoutElement>();layout.preferredWidth=width;layout.preferredHeight=height;layout.minHeight=height;}
        private static void Horizontal(RectTransform rect,float spacing,int padding=0)
        {var group=rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();group.spacing=spacing;group.padding=new RectOffset(padding,padding,4,4);group.childAlignment=TextAnchor.MiddleCenter;
            group.childControlWidth=true;group.childControlHeight=true;group.childForceExpandWidth=false;group.childForceExpandHeight=false;}
        private static void Vertical(RectTransform rect,float spacing)
        {var group=rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();group.spacing=spacing;group.childAlignment=TextAnchor.UpperCenter;group.childControlWidth=true;group.childControlHeight=true;group.childForceExpandWidth=false;group.childForceExpandHeight=false;}
        private static UnityEngine.UI.Button Button(string name,Transform parent,string label,float width,float height,Color? background=null,Color? foreground=null)
        {
            var image=Image(name,parent,background??Blue);image.raycastTarget=true;Layout(image.gameObject,width,height);
            var button=image.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;var colors=button.colors;colors.highlightedColor=new Color(.85f,.94f,1);colors.selectedColor=new Color(.82f,.93f,1);colors.disabledColor=new Color(.35f,.38f,.42f,.65f);button.colors=colors;
            var text=Text("Label",image.transform,label,30,foreground??Color.white);Stretch(text.rectTransform);text.rectTransform.offsetMin=new Vector2(10,6);text.rectTransform.offsetMax=new Vector2(-10,-6);return button;
        }
        private static UnityEngine.UI.Slider Slider(string name,Transform parent,float width)
        {
            var rect=Rect(name,parent);Layout(rect.gameObject,width,64);var hit=rect.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var slider=rect.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.minValue=0;slider.maxValue=100;slider.wholeNumbers=true;
            var back=Image("Background",rect,new Color(.5f,.55f,.6f));back.rectTransform.anchorMin=new Vector2(0,.42f);back.rectTransform.anchorMax=new Vector2(1,.58f);back.rectTransform.offsetMin=back.rectTransform.offsetMax=Vector2.zero;
            var fill=Image("Fill",back.transform,Blue);Stretch(fill.rectTransform);slider.fillRect=fill.rectTransform;
            var handleArea=Rect("HandleArea",rect);handleArea.anchorMin=new Vector2(0,.5f);handleArea.anchorMax=new Vector2(1,.5f);handleArea.offsetMin=new Vector2(12,0);handleArea.offsetMax=new Vector2(-12,0);
            var handle=Image("Handle",handleArea,Color.white,avatar);SetRect(handle.rectTransform,new Vector2(.5f,.5f),new Vector2(30,30));handle.raycastTarget=true;slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;return slider;
        }
        private static RectTransform Scroll(string name,Transform parent)
        {
            var root=Rect(name,parent);Stretch(root);var scroll=root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=32;
            var viewport=Image("Viewport",root,new Color(1,1,1,.015f));Stretch(viewport.rectTransform);viewport.raycastTarget=true;viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            var content=Rect("Content",viewport.transform);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;content.anchoredPosition=Vector2.zero;
            Vertical(content,20);var fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.content=content;scroll.viewport=viewport.rectTransform;
            viewport.rectTransform.offsetMax=new Vector2(-22,0);
            var bar=Image("Scrollbar",root,new Color(.5f,.55f,.6f));bar.raycastTarget=true;
            bar.rectTransform.anchorMin=new Vector2(1,0);bar.rectTransform.anchorMax=Vector2.one;bar.rectTransform.pivot=new Vector2(1,.5f);bar.rectTransform.sizeDelta=new Vector2(14,0);bar.rectTransform.anchoredPosition=Vector2.zero;
            var area=Rect("SlidingArea",bar.transform);Stretch(area);var handle=Image("Handle",area,Color.white);Stretch(handle.rectTransform);handle.raycastTarget=true;
            var scrollbar=bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();scrollbar.direction=UnityEngine.UI.Scrollbar.Direction.BottomToTop;scrollbar.handleRect=handle.rectTransform;scrollbar.targetGraphic=handle;
            scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;return content;
        }
        private static RectTransform SettingRow(Transform parent,string name,string label)
        {var row=Rect(name,parent);Layout(row.gameObject,1300,70);Horizontal(row,40);TextCell(row,label,420);return row;}
        private static TMP_Dropdown SettingDropdown(Transform parent,string label,string name)
        {return Dropdown(name,SettingRow(parent,name+"Row",label),780);}
        private static TMP_Dropdown Dropdown(string name,Transform parent,float width)
        {
            var image=Image(name,parent,Color.white);image.raycastTarget=true;Layout(image.gameObject,width,64);
            var dropdown=image.gameObject.AddComponent<TMP_Dropdown>();dropdown.targetGraphic=image;
            var caption=Text("Caption",image.transform,"",28,Ink);Stretch(caption.rectTransform);caption.rectTransform.offsetMin=new Vector2(15,2);caption.rectTransform.offsetMax=new Vector2(-55,-2);dropdown.captionText=(TextMeshProUGUI)caption;
            var arrow=Text("Arrow",image.transform,"▼",24,Ink);SetRect(arrow.rectTransform,new Vector2(1,.5f),new Vector2(40,45),new Vector2(-25,0));
            var template=Image("Template",image.transform,new Color(.96f,.97f,1));template.raycastTarget=true;template.rectTransform.anchorMin=Vector2.zero;template.rectTransform.anchorMax=new Vector2(1,0);template.rectTransform.pivot=new Vector2(.5f,1);template.rectTransform.sizeDelta=new Vector2(0,350);
            var scroll=template.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();scroll.horizontal=false;scroll.scrollSensitivity=35;
            var viewport=Image("Viewport",template.transform,Color.white);Stretch(viewport.rectTransform);viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            var content=Rect("Content",viewport.transform);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,56);
            var item=Image("Item",content,new Color(.93f,.95f,.98f));item.raycastTarget=true;item.rectTransform.anchorMin=Vector2.zero;item.rectTransform.anchorMax=Vector2.one;item.rectTransform.offsetMin=item.rectTransform.offsetMax=Vector2.zero;
            var toggle=item.gameObject.AddComponent<UnityEngine.UI.Toggle>();toggle.targetGraphic=item;
            var check=Image("Check",item.transform,Blue);SetRect(check.rectTransform,new Vector2(0,.5f),new Vector2(20,20),new Vector2(20,0));toggle.graphic=check;
            var itemLabel=Text("Label",item.transform,"Option",26,Ink);Stretch(itemLabel.rectTransform);itemLabel.rectTransform.offsetMin=new Vector2(48,0);itemLabel.rectTransform.offsetMax=new Vector2(-12,0);
            dropdown.template=template.rectTransform;dropdown.itemText=(TextMeshProUGUI)itemLabel;scroll.content=content;scroll.viewport=viewport.rectTransform;template.gameObject.SetActive(false);return dropdown;
        }
    }
}
#endif
