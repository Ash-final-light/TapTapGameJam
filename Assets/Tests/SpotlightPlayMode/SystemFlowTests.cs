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
        public IEnumerator ExitGameplayDiscardsProgressAndReturnsToTitle()
        {
            var menu=Get(ui,"Menu");var settings=Get(ui,"Settings");
            Click(menu,"SettingsButton");yield return Frames();
            ((Slider[])Get(settings,"Volumes"))[0].value=72;
            Click(settings,"ApplyButton");yield return Frames(8);Click(settings,"CancelButton");yield return Frames();
            string directory=Path.Combine(Application.persistentDataPath,(string)Get(config,"SaveFolder"));
            string settingsJson=File.ReadAllText(Path.Combine(directory,"settings.json"));
            string progressPath=Path.Combine(directory,"progress.json");
            var originalRoot=root;
            Click(menu,"StartButton");yield return WaitForContext("Gameplay");
            Assert.AreEqual("Missing",Get(Get(root,"Saves"),"Status").ToString());
            Assert.IsFalse(File.Exists(progressPath),"开始新游戏不能生成进度文件");
            var data=UnityEngine.Object.FindObjectOfType(Runtime("HudDebugDataSource"));Call(data,"AddGrowth");
            var pause=Get(ui,"Pause");Call(ui,"OpenPause");Click(pause,"ExitButton");
            yield return WaitForContext("Title");
            Assert.AreEqual("Assets/Scenes/Title.unity",SceneManager.GetActiveScene().path);
            Assert.AreSame(originalRoot,root,"退出本局应返回标题，不能关闭系统或程序");
            Assert.IsFalse(((Button)Get(menu,"ContinueButton")).interactable);
            Assert.IsNull(Get(Get(root,"Saves"),"Current"));Assert.IsFalse(File.Exists(progressPath));
            Assert.IsFalse((bool)Get(ui,"IsPauseOpen"));Assert.IsFalse((bool)Get(Get(root,"Pause"),"IsPaused"));
            Assert.IsFalse((bool)Get(Get(root,"Input"),"GameplayEnabled"));
            Assert.AreEqual(settingsJson,File.ReadAllText(Path.Combine(directory,"settings.json")),"放弃本局必须保留个人设置");
            Click(menu,"StartButton");yield return WaitForContext("Gameplay");
            data=UnityEngine.Object.FindObjectOfType(Runtime("HudDebugDataSource"));
            Assert.AreEqual(0,Get(data,"DebugGrowth"),"再次开始不能恢复已放弃的本局结果");
            Call(data,"AddGrowth");Call(ui,"OpenPause");Click(pause,"SaveButton");yield return Frames();
            Call(data,"AddGrowth");Click(pause,"SaveButton");yield return Frames();
            Assert.AreEqual("Valid",Get(Get(root,"Saves"),"Status").ToString());
            Assert.IsTrue(File.Exists(progressPath+".bak"),"第二次保存应产生测试备份");
            Click(pause,"ExitButton");yield return WaitForContext("Title");
            Assert.IsFalse(((Button)Get(menu,"ContinueButton")).interactable);
            foreach(string suffix in new[]{"",".bak",".tmp"}) Assert.IsFalse(File.Exists(progressPath+suffix),"放弃本局必须清理进度及附属文件");
            UnityEngine.Object.Destroy(((Component)root).gameObject);yield return Frames();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity");yield return Frames();
            root=Runtime("SystemRoot").GetProperty("Instance").GetValue(null);ui=Get(root,"UI");
            Assert.IsFalse(((Button)Get(Get(ui,"Menu"),"ContinueButton")).interactable,"重新启动后也不能继续已放弃的本局");
            Assert.AreEqual(72,Get(Get(Get(root,"Settings"),"Current"),"MasterVolume"));
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
        [UnityTest]
        public IEnumerator MenuSettingsLoadHudPauseAndSave()
        {
            var menu=Get(ui,"Menu"); var settings=Get(ui,"Settings"); var hud=Get(ui,"Hud");
            Assert.IsFalse(((Button)Get(menu,"ContinueButton")).interactable,"无档继续应禁用");
            yield return Capture("01-Title");
            Click(menu,"SettingsButton"); yield return Frames();
            Assert.IsTrue(GO(settings).activeSelf);
            Assert.AreEqual(11,((TMP_Dropdown)Get(settings,"Resolution")).options.Count);
            yield return Capture("02-SettingsGraphics");
            var tabs=(Button[])Get(settings,"Tabs"); tabs[1].onClick.Invoke(); yield return Frames();
            var volumeSlider=((Slider[])Get(settings,"Volumes"))[0];var volumeRect=(RectTransform)volumeSlider.transform;
            var pointer=RectTransformUtility.WorldToScreenPoint(null,volumeRect.TransformPoint(new Vector3(volumeRect.rect.width*.25f,0,0)));
            InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer,buttons=1});yield return Frames();
            InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});yield return Frames();
            Assert.That(volumeSlider.value,Is.InRange(70f,80f),"点击滑条轨道应改变音量");
            volumeSlider.value=23; yield return Frames();
            Assert.AreEqual(23,Get(Get(Get(settings,"Session"),"Draft"),"MasterVolume"));
            yield return Capture("03-SettingsAudio");
            Click(settings,"CancelButton"); yield return Frames();
            Assert.AreEqual(100,Get(Get(Get(root,"Settings"),"Current"),"MasterVolume"));
            Click(menu,"SettingsButton"); tabs[2].onClick.Invoke(); yield return Frames();
            var rows=(Array)Get(settings,"KeyRows"); Assert.AreEqual(9,rows.Length);
            // Exercise actual interactive capture and the linked jump/fly rows.
            Click(rows.GetValue(5),"Button"); yield return Frames();
            Assert.IsTrue((bool)Get(settings,"WaitingForKey"));
            yield return Keys(Key.J); yield return Keys();
            Assert.IsFalse((bool)Get(settings,"WaitingForKey"));
            var draftControls=(InputActionAsset)Get(Get(settings,"Session"),"Controls");
            Assert.AreEqual("<Keyboard>/j",draftControls.FindAction("Gameplay/Skill1").bindings[0].effectivePath);
            Click(settings,"ApplyButton"); yield return Frames(8);
            Assert.IsFalse((bool)Get(settings,"Applying"));
            Assert.That(((TMP_Text)Get(hud,"Skill1Key")).text.ToLowerInvariant(),Does.Contain("j"));
            yield return Capture("04-SettingsBindings");
            Click(settings,"CancelButton");
            float started=Time.realtimeSinceStartup; Click(menu,"StartButton");
            yield return Frames(5); yield return Capture("05-Loading");
            float deadline=Time.realtimeSinceStartup+20;
            while(Get(ui,"Context").ToString()!="Gameplay" && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual("Gameplay",Get(ui,"Context").ToString());
            Assert.GreaterOrEqual(Time.realtimeSinceStartup-started,1f);
            var data=UnityEngine.Object.FindObjectOfType(Runtime("HudDebugDataSource")); Assert.IsNotNull(data);
            Assert.AreEqual("Missing",Get(Get(root,"Saves"),"Status").ToString(),"开始新游戏不能自动保存进度");
            var items=(Array)Get(data,"Items"); Assert.AreEqual(3,Get(items.GetValue(0),"Count"));
            yield return Frames(); Assert.IsTrue((bool)Get(Get(root,"Input"),"GameplayEnabled"),"进入HUD后应启用游戏输入");
            yield return Keys(Key.Tab);
            var liveControls=(InputActionAsset)Get(Get(root,"Input"),"Controls");
            Assert.IsTrue(liveControls.FindAction("Gameplay/ItemSelect").IsPressed(),"Tab action没有收到按下: "+liveControls.FindAction("Gameplay/ItemSelect").bindings[0].effectivePath+" | keyboard="+keyboard.tabKey.isPressed);
            Assert.IsTrue(((GameObject)Get(hud,"Selector")).activeSelf,"Tab动作收到后应显示道具栏");
            yield return Keys(Key.Tab,Key.Digit2); Assert.AreEqual("potion",Get(data,"SelectedItemId"));
            yield return Capture("06-HudSelector");
            yield return Keys(); Assert.IsFalse(((GameObject)Get(hud,"Selector")).activeSelf);
            Assert.AreEqual("potion",Get(data,"SelectedItemId")); Assert.AreEqual(2,Get(items.GetValue(1),"Count"));
            yield return Keys(Key.R); Assert.AreEqual(1,Get(items.GetValue(1),"Count"),"使用应消费一次");
            yield return Frames(8); Assert.AreEqual(1,Get(items.GetValue(1),"Count"),"按住R不重复使用");
            yield return Keys();yield return Keys(Key.R);yield return Keys();
            Assert.AreEqual(0,Get(items.GetValue(1),"Count"));Assert.AreEqual("crystal",Get(data,"SelectedItemId"));
            yield return Keys(Key.Tab);Assert.AreEqual(1,((System.Collections.ICollection)Get(hud,"VisibleItems")).Count,"零数量道具应从选择栏移除");yield return Keys();
            yield return Keys(Key.J); Assert.That((string)Get(data,"Warning"),Does.Contain("技能 1"));
            yield return Keys(); yield return Keys(Key.E); Assert.That((string)Get(data,"Warning"),Does.Contain("技能 2"));
            yield return Keys(); Call(data,"SpendStamina"); Call(data,"LoseHealth"); yield return Frames();
            Assert.AreEqual(.75f,((Image)Get(hud,"Stamina")).fillAmount,.001f);
            yield return Capture("07-Hud");
            yield return Keys(Key.Escape); Assert.IsTrue((bool)Get(ui,"IsPauseOpen"));
            yield return Keys(); var pause=Get(ui,"Pause"); yield return Capture("08-Pause");
            Click(pause,"SettingsButton"); yield return Frames(); Click(settings,"CancelButton"); yield return Frames();
            Assert.IsTrue((bool)Get(ui,"IsPauseOpen"));
            Call(data,"AddGrowth"); Click(pause,"SaveButton"); yield return Frames();
            var saved=(Array)Get(Get(Get(root,"Saves"),"Current"),"CharacterValues"); Assert.AreEqual(1,Get(saved.GetValue(0),"Value"));
            Click(pause,"ResumeButton"); yield return Frames(); Assert.IsFalse((bool)Get(ui,"IsPauseOpen"));
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity"); yield return Frames();
            Assert.IsTrue(((Button)Get(menu,"ContinueButton")).interactable);
            Click(menu,"StartButton"); yield return Frames(); Assert.IsTrue(GO(Get(ui,"Dialog")).activeSelf);
            Call(Get(ui,"Dialog"),"Cancel"); Click(menu,"ContinueButton");
            deadline=Time.realtimeSinceStartup+20;
            while(Get(ui,"Context").ToString()!="Gameplay" && Time.realtimeSinceStartup<deadline) yield return null;
            yield return Frames(); data=UnityEngine.Object.FindObjectOfType(Runtime("HudDebugDataSource"));
            Assert.AreEqual(1,Get(data,"DebugGrowth"),"继续恢复局外养成");
            items=(Array)Get(data,"Items"); Assert.AreEqual(2,Get(items.GetValue(1),"Count"),"继续从关卡入口重建局内状态");
            Assert.AreEqual(1,UnityEngine.Object.FindObjectsOfType(Runtime("SystemRoot")).Length);
            Assert.AreEqual(1,UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>().Length);
            // Starting over replaces an existing save only after confirmation, without auto-saving the new run.
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity");yield return Frames();
            Click(menu,"StartButton");yield return Frames();Call(Get(ui,"Dialog"),"Cancel");
            Assert.AreEqual("Valid",Get(Get(root,"Saves"),"Status").ToString(),"取消开始新游戏必须保留原存档");
            Click(menu,"StartButton");yield return Frames();Click(Get(ui,"Dialog"),"ConfirmButton");
            yield return WaitForContext("Gameplay");
            Assert.AreEqual("Missing",Get(Get(root,"Saves"),"Status").ToString(),"确认新游戏必须清理原进度且不自动保存新局");
            data=UnityEngine.Object.FindObjectOfType(Runtime("HudDebugDataSource"));Assert.AreEqual(0,Get(data,"DebugGrowth"));
            Call(ui,"OpenPause");Click(Get(ui,"Pause"),"ExitButton");yield return WaitForContext("Title");
            Assert.IsFalse(((Button)Get(menu,"ContinueButton")).interactable);
        }
    }
}
#endif
