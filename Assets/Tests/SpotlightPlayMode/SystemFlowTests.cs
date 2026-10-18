#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

namespace Spotlight.Tests
{
    // Predefined Assembly-CSharp cannot be referenced by an asmdef. Reflection
    // keeps the existing project's assembly structure intact while exercising it.
    public sealed class SystemFlowTests
    {
        private UnityEngine.Object config;
        private object root, ui;
        private string previousFolder;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private static Type Runtime(string name) => Type.GetType("TapTap.Systems." + name + ", Assembly-CSharp", true);
        private static object Get(object owner, string name)
        {
            var type = owner.GetType();
            return type.GetProperty(name, BindingFlags.Public|BindingFlags.Instance)?.GetValue(owner)
                ?? type.GetField(name, BindingFlags.Public|BindingFlags.Instance)?.GetValue(owner);
        }
        private static void Set(object owner, string name, object value)
        {
            var property = owner.GetType().GetProperty(name);
            if (property != null) property.SetValue(owner, value); else owner.GetType().GetField(name).SetValue(owner, value);
        }
        private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name).Invoke(owner,args);
        private static GameObject GO(object component) => ((Component)component).gameObject;
        private static void Click(object view, string field) => ((Button)Get(view,field)).onClick.Invoke();
        private IEnumerator Frames(int count=3) { for(int i=0;i<count;i++) yield return null; }
        private IEnumerator Keys(params Key[] pressed)
        { InputSystem.QueueStateEvent(keyboard,new KeyboardState(pressed)); yield return Frames(); }
        private IEnumerator WaitForContext(string expected)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (Get(ui,"Context").ToString()!=expected && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(expected,Get(ui,"Context").ToString());
            yield return Frames();
        }
        private IEnumerator Capture(string name)
        {
            Directory.CreateDirectory("Logs/SpotlightScreenshots");
            // Batch PlayMode does not present a Game view for ScreenCapture.
            // Render the live uGUI hierarchy through Unity's native camera instead.
            var canvas=GO(ui).GetComponent<Canvas>();var scaler=GO(ui).GetComponent<CanvasScaler>();
            var cameraGo=new GameObject("UICaptureCamera");var camera=cameraGo.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.7f,.73f,.76f);camera.orthographic=true;
            var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;
            var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;float oldScale=scaler.scaleFactor;
            var oldScaling=scaler.uiScaleMode;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=1;
            yield return Frames(); Canvas.ForceUpdateCanvases(); camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();RenderTexture.active=previous;
            File.WriteAllBytes("Logs/SpotlightScreenshots/"+name+".png",texture.EncodeToPNG());
            canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;scaler.uiScaleMode=oldScaling;scaler.scaleFactor=oldScale;
            UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(cameraGo);yield return Frames();
        }
        [UnitySetUp]
        public IEnumerator Setup()
        {
            config=AssetDatabase.LoadMainAssetAtPath("Assets/Configs/Spotlight/SystemConfig.asset");
            Assert.IsNotNull(config);
            previousFolder=(string)Get(config,"SaveFolder");
            Set(config,"SaveFolder","SpotlightTests/"+Guid.NewGuid().ToString("N"));
            previousBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            previousEditorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard=InputSystem.AddDevice<Keyboard>();
            mouse=InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity"); yield return Frames();
            root=Runtime("SystemRoot").GetProperty("Instance").GetValue(null); Assert.IsNotNull(root);
            ui=Get(root,"UI"); Assert.IsTrue((bool)Get(root,"Initialized"));
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if(mouse!=null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode=previousEditorInput;
            if(root is Component component && component) UnityEngine.Object.Destroy(component.gameObject);
            if(testedDifficulty!=null) Set(testedDifficulty,"Battle",previousBattle);
            if(testBattle) UnityEngine.Object.Destroy(testBattle); if(testSequence) UnityEngine.Object.Destroy(testSequence);
            testedDifficulty=null; testBattle=testSequence=previousBattle=null;
            if(config) Set(config,"SaveFolder",previousFolder);
            Application.targetFrameRate=60;
            yield return Frames();
        }
        [UnityTest]
        public IEnumerator AppliedBindingsSurviveReopenApplyAndRestart()
        {
            var menu=Get(ui,"Menu");var settings=Get(ui,"Settings");var settingsService=Get(root,"Settings");
            Click(menu,"SettingsButton");((Button[])Get(settings,"Tabs"))[2].onClick.Invoke();yield return Frames();
            var rows=(Array)Get(settings,"KeyRows");Click(rows.GetValue(0),"Button");yield return Frames();
            yield return Keys(Key.H);yield return Keys();
            Assert.IsFalse((bool)Get(settings,"WaitingForKey"));
            Click(settings,"ApplyButton");yield return Frames(8);Click(settings,"CancelButton");yield return Frames();
            string savedJson=(string)Get(Get(settingsService,"Current"),"BindingOverridesJson");
            Assert.That(savedJson,Does.Contain("<Keyboard>/h"),"首次应用必须保存改键");
            Click(menu,"SettingsButton");yield return Frames();
            var draft=(InputActionAsset)Get(Get(settings,"Session"),"Controls");
            Assert.AreEqual("<Keyboard>/h",draft.FindAction("Gameplay/Left").bindings[0].effectivePath,"重新打开设置必须继承已应用改键");
            Assert.That(((TMP_Text)Get(rows.GetValue(0),"BindingText")).text.ToLowerInvariant(),Does.Contain("h"));
            // Applying a different option must not discard the binding inherited by the draft.
            ((Slider[])Get(settings,"Volumes"))[0].value=67;Click(settings,"ApplyButton");yield return Frames(8);
            Click(settings,"CancelButton");yield return Frames();
            var live=(InputActionAsset)Get(Get(root,"Input"),"Controls");
            Assert.AreEqual("<Keyboard>/h",live.FindAction("Gameplay/Left").bindings[0].effectivePath);
            string savePath=Path.Combine(Application.persistentDataPath,(string)Get(config,"SaveFolder"),"settings.json");
            Assert.That(File.ReadAllText(savePath),Does.Contain("<Keyboard>/h"),"再次应用其他设置不得清空文件中的改键");
            // Destroy the persistent composition root and create a fresh one from the scene.
            UnityEngine.Object.Destroy(((Component)root).gameObject);yield return Frames();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity");yield return Frames();
            root=Runtime("SystemRoot").GetProperty("Instance").GetValue(null);ui=Get(root,"UI");
            live=(InputActionAsset)Get(Get(root,"Input"),"Controls");
            Assert.AreEqual("<Keyboard>/h",live.FindAction("Gameplay/Left").bindings[0].effectivePath,"新实例必须从磁盘恢复改键");
            menu=Get(ui,"Menu");settings=Get(ui,"Settings");Click(menu,"SettingsButton");yield return Frames();
            draft=(InputActionAsset)Get(Get(settings,"Session"),"Controls");
            Assert.AreEqual("<Keyboard>/h",draft.FindAction("Gameplay/Left").bindings[0].effectivePath,"重启后设置页必须显示恢复后的键位");
            Assert.AreEqual(67,Get(Get(Get(root,"Settings"),"Current"),"MasterVolume"));
            // Editing, cancelling capture, cancelling reset, and cancelling the page keep the applied baseline.
            rows=(Array)Get(settings,"KeyRows");((Button[])Get(settings,"Tabs"))[2].onClick.Invoke();yield return Frames();
            Click(rows.GetValue(0),"Button");yield return Frames();yield return Keys(Key.Escape);yield return Keys();
            Assert.IsFalse((bool)Get(settings,"WaitingForKey"));Assert.IsTrue(GO(settings).activeSelf);
            Click(settings,"ResetButton");yield return Frames();Click(settings,"CancelButton");yield return Frames();
            Assert.AreEqual("<Keyboard>/h",live.FindAction("Gameplay/Left").bindings[0].effectivePath);
            Click(menu,"SettingsButton");yield return Frames();
            draft=(InputActionAsset)Get(Get(settings,"Session"),"Controls");
            Assert.AreEqual("<Keyboard>/h",draft.FindAction("Gameplay/Left").bindings[0].effectivePath,"取消重置不能修改已保存键位");
            Click(settings,"ResetButton");Click(settings,"ApplyButton");yield return Frames(8);Click(settings,"CancelButton");yield return Frames();
            UnityEngine.Object.Destroy(((Component)root).gameObject);yield return Frames();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity");yield return Frames();
            root=Runtime("SystemRoot").GetProperty("Instance").GetValue(null);ui=Get(root,"UI");
            live=(InputActionAsset)Get(Get(root,"Input"),"Controls");
            Assert.AreEqual("<Keyboard>/a",live.FindAction("Gameplay/Left").bindings[0].effectivePath,"重置并应用必须持久恢复默认键位");
        }
        [UnityTest]
        public IEnumerator ExitGameplayPreservesPermanentSaveAndReturnsToTitle()
        {
            Click(Get(ui,"Menu"),"StartButton"); yield return WaitForContext("SongSelect");
            var saves=Get(root,"Saves"); Assert.IsTrue((bool)Get(Call(saves,"GrantItem","item-1",2),"Success"));
            string directory=Path.Combine(Application.persistentDataPath,(string)Get(config,"SaveFolder"));
            string original=File.ReadAllText(Path.Combine(directory,"game.json"));
            yield return PrepareAndStartBattle();
            Call(ui,"OpenPause"); yield return Frames();
            Assert.IsNull(GO(Get(ui,"Pause")).transform.Find("Buttons/Save"));
            Click(Get(ui,"Pause"),"ExitButton"); yield return WaitForContext("Title");
            Assert.IsTrue(((Button)Get(Get(ui,"Menu"),"ContinueButton")).interactable);
            Assert.AreEqual(original,File.ReadAllText(Path.Combine(directory,"game.json")),"放弃本局不能清空歌曲与道具");
            Click(Get(ui,"Menu"),"ContinueButton"); yield return WaitForContext("SongSelect");
            Assert.AreEqual(2,Call(saves,"Quantity","item-1"));
        }
        [UnityTest]
        public IEnumerator LongRenderFrameKeepsSimulationTimeAndPauseState()
        {
            var sequence=ScriptableObject.CreateInstance(Type.GetType("TapTap.Danmaku.DanmakuSequence, Assembly-CSharp",true));
            Set(sequence,"DurationTicks",2000); Set(sequence,"CollisionEnabled",false);
            var actor=new GameObject("LowRenderRateCheck");
            float previousMaximum=Time.maximumDeltaTime;
            try
            {
                var player=actor.AddComponent(Type.GetType("TapTap.Danmaku.DanmakuSequencePlayer, Assembly-CSharp",true));
                Set(player,"PlayOnEnable",false); Set(player,"Sequence",sequence); Call(player,"Play");
                actor.AddComponent(Runtime("DanmakuPauseAdapter")); yield return Frames();
                var simulation=Get(player,"Simulation"); int before=(int)Get(simulation,"Tick");
                Time.maximumDeltaTime=.05f;
                System.Threading.Thread.Sleep(1100); yield return Frames();
                Assert.GreaterOrEqual((int)Get(simulation,"Tick")-before,60,"长渲染帧应补齐固定Tick，不能被deltaTime截断");
                var lease=(IDisposable)Call(Get(root,"Pause"),"Acquire");
                int paused=(int)Get(simulation,"Tick"); yield return Frames(5);
                Assert.AreEqual(paused,Get(simulation,"Tick")); lease.Dispose(); yield return Frames();
                Assert.IsTrue((bool)Get(player,"Playing")); Assert.Less((int)Get(simulation,"Tick")-paused,20,"恢复不补跑暂停时间");
            }
            finally { Time.maximumDeltaTime=previousMaximum; UnityEngine.Object.Destroy(actor); UnityEngine.Object.Destroy(sequence); }
        }
        private UnityEngine.Object testBattle, testSequence;
        private object testedDifficulty;
        private UnityEngine.Object previousBattle;
        [UnityTest]
        public IEnumerator AuthoredDrumsSongPlaysToMusicEndSettlesAndRetries()
        {
            Click(Get(ui,"Menu"),"StartButton"); yield return WaitForContext("SongSelect");
            Assert.AreEqual("drums155-90s-v1",Get(Get(Get(ui,"Songs"),"SelectedSong"),"Id"));
            Assert.That(((TMP_Text)Get(Get(ui,"Songs"),"Title")).text,Does.Contain("155 BPM"));
            Click(Get(ui,"Songs"),"Confirm"); yield return WaitForContext("Loadout");
            var weapons=((RectTransform)Get(Get(ui,"Loadout"),"WeaponsRoot")).GetComponentsInChildren<Button>();
            weapons[0].onClick.Invoke();weapons[0].onClick.Invoke();yield return Frames();
            Click(Get(ui,"Loadout"),"StartButton");yield return WaitForContext("Gameplay");
            var manager=UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp",true));
            var simulation=Get(manager,"Simulation");var setup=Get(root,"ActiveRun");
            var sequence=Get(Get(manager,"Config"),"Sequence");
            Assert.AreEqual(35,((IList)Get(sequence,"Clips")).Count);
            Assert.AreEqual(5403,Get(sequence,"DurationTicks"));
            Assert.AreEqual(5f,Scalar(Get(Get(simulation,"Player"),"MaxHealth")));
            Call(ui,"OpenPause");int paused=(int)Get(simulation,"Tick");yield return Frames(8);
            Assert.AreEqual(paused,Get(simulation,"Tick"));Call(ui,"Resume");
            float deadline=Time.realtimeSinceStartup+110;bool heardAudio=false;int nextLog=0;
            var camera=(Camera)Get(manager,"AimCamera");
            while(Get(ui,"Context").ToString()=="Gameplay" && Time.realtimeSinceStartup<deadline)
            {
                int tick=(int)Get(simulation,"Tick");
                Vector2 player=Vector(Get(Get(Get(simulation,"Player"),"Pose"),"Position"));
                float target=Mathf.Sin(tick/95f)*6;
                var buttons=new System.Collections.Generic.List<Key>{target>=player.x?Key.D:Key.A};
                bool threat=false;Vector2 aim=Vector(Get(simulation,"BossPosition"));bool orbSelected=false;
                foreach(var projectile in (IEnumerable)Get(Get(simulation,"World"),"Bullets"))
                {
                    bool orb=(bool)Get(projectile,"IsEnergyOrb");
                    Vector2 point=Vector(Get(projectile,"Position"));
                    if(orb && !orbSelected){aim=point;orbSelected=true;}
                    if(Get(projectile,"Faction").ToString()!="Enemy" || orb)continue;
                    Vector2 soon=point+(point-Vector(Get(projectile,"PreviousPosition")))*8;
                    if(Vector2.Distance(soon,player)<.8f)threat=true;
                }
                if(threat && tick%3==0)buttons.Add(Key.LeftShift);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(buttons.ToArray()));
                Vector3 world=((Component)manager).transform.TransformPoint(new Vector3(aim.x,aim.y,0));
                Vector3 screen=camera.WorldToScreenPoint(world);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y)}.WithButton(MouseButton.Left));
                var source=(AudioSource)Get(manager,"MusicSource");heardAudio|=source && source.isPlaying;
                if(tick>=nextLog){Debug.Log("Authored155 full-song playback tick="+tick);nextLog+=720;}
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.QueueStateEvent(mouse,new MouseState());
            yield return WaitForContext("Result");
            var result=Get(root,"LastResult");
            Assert.IsTrue(heardAudio,"Actual authored music AudioSource never started.");
            Assert.IsTrue((bool)Get(Get(result,"Stats"),"Completed"),"Normal 5 HP input route did not reach the actual music end.");
            Assert.AreEqual(5403,Get(simulation,"Tick"));Assert.AreEqual("drums155-90s-v1",Get(result,"SongId"));
            yield return Capture("Authored155Completed");
            var saves=Get(root,"Saves");string item=(string)Get(result,"DropItemId");int quantity=(int)Call(saves,"Quantity",item);
            Call(root,"CompleteRun",result);Assert.AreEqual(quantity,Call(saves,"Quantity",item));
            Assert.IsNull(Call(saves,"Record","remembrance",Get(result,"Difficulty")),"New chart overwrote the previous song record.");
            Click(Get(ui,"Result"),"Retry");yield return WaitForContext("Gameplay");
            Assert.AreSame(setup,Get(root,"ActiveRun"));
            manager=UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp",true));
            Assert.AreSame(sequence,Get(Get(manager,"Config"),"Sequence"));
            var damage=Type.GetType("Framework.FixedPoint.FP64, Assembly-CSharp",true).GetMethod("FromInt").Invoke(null,new object[]{999});
            Call(Get(Get(manager,"Simulation"),"PlayerSystem"),"ReceiveDamage",damage);
            yield return WaitForContext("Result");
            Assert.IsFalse((bool)Get(Get(Get(root,"LastResult"),"Stats"),"Completed"));
            Click(Get(ui,"Result"),"Next");yield return WaitForContext("Title");

            float Scalar(object value)=>(float)Call(value,"ToFloat");
            Vector2 Vector(object value)=>new(Scalar(Get(value,"X")),Scalar(Get(value,"Y")));
        }
        private IEnumerator PrepareAndStartBattle(bool equipFirstItem=false)
        {
            if(testBattle==null)
            {
                var songs=(Array)Get(Get(config,"Songs"),"Songs");
                testedDifficulty=((Array)Get(songs.GetValue(0),"Difficulties")).GetValue(0);
                previousBattle=(UnityEngine.Object)Get(testedDifficulty,"Battle");
                testBattle=UnityEngine.Object.Instantiate(previousBattle);
                testSequence=UnityEngine.Object.Instantiate((UnityEngine.Object)Get(testBattle,"Sequence"));
                Set(testSequence,"Music",null); Set(testSequence,"DurationTicks",2400);
                ((IList)Get(testSequence,"Clips")).Clear(); ((IList)Get(testSequence,"Events")).Clear();
                Set(testBattle,"Sequence",testSequence); Set(testedDifficulty,"Battle",testBattle);
            }
            if(Get(ui,"Context").ToString()=="Title") { Click(Get(ui,"Menu"),"ContinueButton"); yield return WaitForContext("SongSelect"); }
            Click(Get(ui,"Songs"),"Confirm"); yield return WaitForContext("Loadout");
            var weaponButtons=((RectTransform)Get(Get(ui,"Loadout"),"WeaponsRoot")).GetComponentsInChildren<Button>();
            weaponButtons[0].onClick.Invoke(); weaponButtons[0].onClick.Invoke(); yield return Frames();
            if(equipFirstItem)
            {
                var buttons=((RectTransform)Get(Get(ui,"Loadout"),"ItemsRoot")).GetComponentsInChildren<Button>();
                Assert.IsTrue(buttons[1].interactable); buttons[1].onClick.Invoke(); buttons[1].onClick.Invoke(); yield return Frames();
            }
            Click(Get(ui,"Loadout"),"StartButton"); yield return WaitForContext("Gameplay");
        }
        [UnityTest]
        public IEnumerator LoadoutPreviewDoubleClickSlotsAndStartGate()
        {
            Click(Get(ui,"Menu"),"StartButton"); yield return WaitForContext("SongSelect");
            Click(Get(ui,"Songs"),"Confirm"); yield return WaitForContext("Loadout");
            var loadout=Get(ui,"Loadout"); var start=(Button)Get(loadout,"StartButton");
            var description=(TMP_Text)Get(loadout,"Description");
            Assert.IsFalse(start.interactable); Assert.IsNull(Get(loadout,"SelectedWeapon")); Assert.AreEqual("",description.text);
            yield return Capture("LoadoutEmpty");
            var options=((RectTransform)Get(loadout,"ItemsRoot")).GetComponentsInChildren<Button>();
            options[0].onClick.Invoke();
            Assert.IsTrue(description.text.Contains("恢复2点"),"未解锁道具应能查看说明");
            options[0].onClick.Invoke(); Assert.AreEqual(0,((IList)Get(loadout,"Equipment")).Count,"未解锁道具不能装备");
            yield return Capture("LoadoutLockedPreview");
            var weaponButtons=((RectTransform)Get(loadout,"WeaponsRoot")).GetComponentsInChildren<Button>();
            weaponButtons[0].onClick.Invoke(); Assert.IsFalse(start.interactable,"单击弹幕仅查看说明");
            weaponButtons[0].onClick.Invoke(); yield return Frames(); Assert.IsTrue(start.interactable);
            var equippedWeapon=(Button)Get(loadout,"EquippedWeapon"); equippedWeapon.onClick.Invoke();
            Assert.IsTrue(start.interactable,"单击已装备弹幕不应卸下"); equippedWeapon.onClick.Invoke();
            yield return Frames(); Assert.IsFalse(start.interactable,"卸下弹幕后按钮应禁用");
            weaponButtons=((RectTransform)Get(loadout,"WeaponsRoot")).GetComponentsInChildren<Button>();
            weaponButtons[1].onClick.Invoke(); weaponButtons[1].onClick.Invoke(); yield return Frames(); Assert.IsTrue(start.interactable);
            var saves=Get(root,"Saves"); for(int i=1;i<=4;i++) Call(saves,"GrantItem","item-"+i,3);
            Call(loadout,"Open"); yield return Frames();
            options=((RectTransform)Get(loadout,"ItemsRoot")).GetComponentsInChildren<Button>();
            options[0].onClick.Invoke(); Assert.AreEqual(0,((IList)Get(loadout,"Equipment")).Count,"单击道具不应装备");
            options[0].onClick.Invoke(); yield return Frames();
            for(int i=1;i<3;i++)
            {
                options=((RectTransform)Get(loadout,"ItemsRoot")).GetComponentsInChildren<Button>();
                options[i].onClick.Invoke(); options[i].onClick.Invoke(); yield return Frames();
            }
            options=((RectTransform)Get(loadout,"ItemsRoot")).GetComponentsInChildren<Button>();
            options[3].onClick.Invoke(); options[3].onClick.Invoke(); yield return Frames();
            Assert.AreEqual(3,((IList)Get(loadout,"Equipment")).Count,"不能超过3个装备槽");
            options[0].onClick.Invoke(); options[0].onClick.Invoke(); yield return Frames();
            Assert.AreEqual(3,((IList)Get(loadout,"Equipment")).Count,"重复双击右侧道具不应卸下或重复装备");
            var slots=((RectTransform)Get(loadout,"EquippedRoot")).GetComponentsInChildren<Button>();
            slots[0].onClick.Invoke(); Assert.AreEqual(3,((IList)Get(loadout,"Equipment")).Count);
            Assert.IsTrue(slots[0].GetComponent<Outline>().enabled); Assert.Greater(slots[0].GetComponent<Outline>().effectDistance.x,2);
            yield return new WaitForSecondsRealtime(4.1f);
            yield return Capture("LoadoutEquippedSelected");
            yield return new WaitForSecondsRealtime(.35f);
            slots[0].onClick.Invoke(); slots[0].onClick.Invoke(); yield return Frames();
            Assert.AreEqual(2,((IList)Get(loadout,"Equipment")).Count);
            var scroll=((RectTransform)Get(loadout,"ItemsRoot")).GetComponentInParent<ScrollRect>();
            Assert.IsTrue(scroll.vertical); Assert.IsFalse(scroll.horizontal);
            var oldItems=(Array)Get(config,"Items"); var extended=Array.CreateInstance(oldItems.GetType().GetElementType(),12);
            for(int i=0;i<12;i++) extended.SetValue(oldItems.GetValue(i%oldItems.Length),i);
            try
            {
                Set(config,"Items",extended); Call(loadout,"Open"); yield return Frames(); Canvas.ForceUpdateCanvases();
                Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height,"扩展道具列表未增长");
                scroll.verticalNormalizedPosition=0; yield return Frames(); yield return Capture("LoadoutScrolled");
            }
            finally { Set(config,"Items",oldItems); }
            Click(loadout,"Back"); yield return WaitForContext("SongSelect");
        }
        [UnityTest]
        public IEnumerator EquippedItemUsesAcceptedIntentAndRestoresOnRetry()
        {
            Click(Get(ui,"Menu"),"StartButton"); yield return WaitForContext("SongSelect");
            var saves=Get(root,"Saves"); Call(saves,"GrantItem","item-2",3);
            yield return PrepareAndStartBattle(true);
            var manager=UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp",true));
            var items=(Array)Get(Get(manager,"Current"),"Items"); Assert.AreEqual(3,Get(items.GetValue(0),"Count"));
            yield return Keys(Key.R); yield return Keys();
            items=(Array)Get(Get(manager,"Current"),"Items"); Assert.AreEqual(2,Get(items.GetValue(0),"Count"),"同一次使用请求不能被多 Tick 重复执行");
            Assert.AreEqual(3,Call(saves,"Quantity","item-2"),"局内使用不能扣永久获取数量");
            for(int i=0;i<12 && Get(ui,"Context").ToString()!="Result";i++) Call(manager,"AdvanceFrame",10d);
            yield return WaitForContext("Result"); Click(Get(ui,"Result"),"Retry"); yield return WaitForContext("Gameplay");
            manager=UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp",true));
            items=(Array)Get(Get(manager,"Current"),"Items"); Assert.AreEqual(3,Get(items.GetValue(0),"Count"),"重试必须恢复道具使用次数");
        }
        [UnityTest]
        public IEnumerator SongLoadoutBattleResultDropRetryContinueAndNewGame()
        {
            var menu=Get(ui,"Menu"); Assert.IsFalse(((Button)Get(menu,"ContinueButton")).interactable);
            Click(menu,"StartButton"); yield return WaitForContext("SongSelect");
            var songs=Get(ui,"Songs"); var difficulties=(Button[])Get(songs,"DifficultyButtons");
            Assert.IsTrue(difficulties[0].interactable); Assert.IsFalse(difficulties[1].interactable); Assert.IsFalse(difficulties[2].interactable);
            yield return Keys(Key.RightArrow); yield return Keys();
            Assert.AreSame(difficulties[0].gameObject,UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject,"歌曲列表必须能用方向键进入难度区");
            yield return Capture("SongSelect");
            Click(songs,"Confirm"); yield return WaitForContext("Loadout"); yield return Capture("LoadoutLocked");
            Call(ui,"OpenSongSelection"); yield return PrepareAndStartBattle();
            Assert.AreEqual(1,UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>().Length);
            var manager=UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp",true));
            Call(ui,"OpenPause"); int paused=(int)Get(Get(manager,"Simulation"),"Tick"); yield return Frames(5);
            Assert.AreEqual(paused,Get(Get(manager,"Simulation"),"Tick")); Click(Get(ui,"Pause"),"ResumeButton"); yield return Frames();
            for(int i=0;i<12 && Get(ui,"Context").ToString()!="Result";i++) Call(manager,"AdvanceFrame",10d);
            yield return WaitForContext("Result"); yield return Capture("ResultCompleted");
            var result=Get(root,"LastResult"); Assert.IsTrue((bool)Get(Get(result,"Stats"),"Completed"));
            var saves=Get(root,"Saves"); string itemId=(string)Get(result,"DropItemId");
            Assert.AreEqual(1,Call(saves,"Quantity",itemId)); Call(root,"CompleteRun",result); Assert.AreEqual(1,Call(saves,"Quantity",itemId),"重复结算重复掉落");
            Assert.IsTrue((bool)Get(Call(root,"RetryResultSave"),"Success")); Assert.AreEqual(1,Call(saves,"Quantity",itemId));
            var firstSetup=Get(root,"ActiveRun"); Click(Get(ui,"Result"),"Retry"); yield return WaitForContext("Gameplay");
            Assert.AreSame(firstSetup,Get(root,"ActiveRun")); manager=UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp",true));
            var simulation=Get(manager,"Simulation"); var fp=Type.GetType("Framework.FixedPoint.FP64, Assembly-CSharp",true);
            var damage=fp.GetMethod("FromInt").Invoke(null,new object[]{999}); Call(Get(simulation,"PlayerSystem"),"ReceiveDamage",damage);
            Call(manager,"AdvanceFrame",.2d); yield return WaitForContext("Result"); yield return Capture("ResultFailed");
            result=Get(root,"LastResult"); Assert.IsFalse((bool)Get(Get(result,"Stats"),"Completed"));
            var acquired=(IList)Get(Get(saves,"Current"),"Items"); int quantity=0; foreach(var item in acquired) quantity+=(int)Get(item,"Quantity"); Assert.AreEqual(2,quantity);
            Click(Get(ui,"Result"),"Next"); yield return WaitForContext("Title");
            Click(menu,"ContinueButton"); yield return WaitForContext("SongSelect");
            Click(Get(ui,"Songs"),"Confirm"); yield return WaitForContext("Loadout"); yield return Capture("LoadoutUnlocked");
            var loadout=Get(ui,"Loadout"); var choices=((RectTransform)Get(loadout,"ItemsRoot")).GetComponentsInChildren<Button>();
            Assert.IsTrue(Array.Exists(choices,b=>b.interactable));
            Call(ui,"OpenSongSelection"); Click(Get(ui,"Songs"),"Back"); yield return WaitForContext("Title");
            Click(menu,"StartButton"); yield return Frames(); Assert.IsTrue(GO(Get(ui,"Dialog")).activeSelf);
            Call(Get(ui,"Dialog"),"Cancel"); Assert.AreEqual(2,quantity);
            Click(menu,"StartButton"); Click(Get(ui,"Dialog"),"ConfirmButton"); yield return WaitForContext("SongSelect");
            Assert.AreEqual(0,((IList)Get(Get(saves,"Current"),"Items")).Count); Assert.AreEqual(0,((IList)Get(Get(saves,"Current"),"Records")).Count);
        }
    }
}
#endif
