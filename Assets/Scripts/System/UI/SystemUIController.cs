using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class SystemUIController : MonoBehaviour
    {
        public MenuView Menu;
        public LoadingView Loading;
        public SettingsView Settings;
        public PauseView Pause;
        public HudView Hud;
        public DialogView Dialog;
        public TMP_Text ToastLabel;
        public EventSystem EventSystem;
        public InputSystemUIInputModule InputModule;
        public UIContext Context { get; private set; }
        public bool IsPauseOpen => Pause.gameObject.activeSelf;
        private SystemRoot root;
        private IDisposable pauseLease;
        private IHudDataSource data;
        private IGameplayRequestSink requests;
        private float toastUntil;
        private int escapeFrame=-1;
        private bool settingsFromPause;
        private InputActionReference[] references;
        public void Bind(SystemRoot owner)
        {
            root=owner;
            Menu.StartButton.onClick.AddListener(NewGame); Menu.ContinueButton.onClick.AddListener(Continue);
            Menu.SettingsButton.onClick.AddListener(OpenSettings); Menu.ExitButton.onClick.AddListener(root.ExitGame);
            Pause.ResumeButton.onClick.AddListener(Resume); Pause.SettingsButton.onClick.AddListener(OpenSettings);
            Pause.SaveButton.onClick.AddListener(Save); Pause.ExitButton.onClick.AddListener(root.AbandonGame);
            Hud.PauseButton.onClick.AddListener(OpenPause); Hud.SlotClicked=SelectSlot;
            Loading.Retry.onClick.AddListener(Retry); Loading.Back.onClick.AddListener(BackToTitle);
            Settings.Bind(root); Settings.CloseRequested=SettingsClosed;
            root.Input.PauseRequested+=OnEscape; root.Input.CancelRequested+=OnEscape;
            root.Input.SelectionHeldChanged+=ShowSelector; root.Input.SlotRequested+=SelectSlot;
            root.Scenes.Changed+=RefreshLoading;
            root.Settings.Applied+=OnSettingsApplied;
            references = new InputActionReference[6];
            string[] names={"Navigate","Submit","Cancel","Point","Click","ScrollWheel"};
            for(int i=0;i<names.Length;i++) references[i]=InputActionReference.Create(root.Input.Controls.FindAction("UI/"+names[i],true));
            InputModule.actionsAsset=root.Input.Controls;
            InputModule.move=references[0]; InputModule.submit=references[1]; InputModule.cancel=null; // Esc is exclusively routed by this controller.
            InputModule.point=references[3]; InputModule.leftClick=references[4]; InputModule.scrollWheel=references[5];
            InputModule.rightClick=null; InputModule.middleClick=null;
            Settings.gameObject.SetActive(false); Pause.gameObject.SetActive(false); Dialog.gameObject.SetActive(false);
            Hud.Selector.SetActive(false); ToastLabel.gameObject.SetActive(false);
            Hud.RefreshBindings(root.Binding, root.Input.Controls);
        }
        public void ShowContext(UIContext context)
        {
            Context=context;
            Menu.gameObject.SetActive(context==UIContext.Title); Loading.gameObject.SetActive(context==UIContext.Loading); Hud.gameObject.SetActive(context==UIContext.Gameplay);
            if(context!=UIContext.Gameplay) { pauseLease?.Dispose(); pauseLease=null; Pause.gameObject.SetActive(false); }
            Hud.Warning.gameObject.SetActive(!IsPauseOpen);
            root.Input.SetGameplay(context==UIContext.Gameplay && !IsPauseOpen && !Settings.gameObject.activeSelf);
            RefreshContinue();
            if(context==UIContext.Title) Select(Menu.StartButton.gameObject);
            if(context==UIContext.Loading) { Loading.Tip.text=root.Config.LoadingTips.Pick(new System.Random()); RefreshLoading(); }
            if(context==UIContext.Gameplay) EventSystem.SetSelectedGameObject(null);
        }
        private void Select(GameObject target) { EventSystem.SetSelectedGameObject(null); EventSystem.SetSelectedGameObject(target); }
        public void RefreshContinue() { Menu.ContinueButton.interactable=root.Saves.Status==SaveStatus.Valid; }
        private void NewGame()
        {
            if(root.Saves.Status==SaveStatus.Valid) Dialog.Show("开始新游戏会重置养成与关卡进度，个人设置将保留。是否继续？",()=>Begin(root.Config.Levels.FirstEntryId,true));
            else Begin(root.Config.Levels.FirstEntryId,true);
        }
        private void Continue() { if(root.Saves.Current!=null) Begin(root.Saves.Current.LevelEntryId,false); }
        private void Begin(string id,bool fresh)
        {
            var result=root.Scenes.Begin(id,fresh);
            if(!result.Success) { ShowContext(UIContext.Loading); Toast(result.Error); RefreshLoading(); }
        }
        private void Retry() { var result=root.Scenes.Retry(); if(!result.Success) Toast(result.Error); }
        private void BackToTitle() { if(root.Scenes.Busy) return; SceneManager.LoadScene(root.Config.TitleScene); ShowContext(UIContext.Title); }
        private void RefreshLoading()
        {
            Loading.Progress.value=root.Scenes.Progress;
            Loading.Status.text=root.Scenes.Error ?? (root.Scenes.Busy ? "载入中…… "+Mathf.RoundToInt(root.Scenes.Progress*100)+"%" : "准备完成");
            Loading.Retry.gameObject.SetActive(root.Scenes.Failed); Loading.Back.gameObject.SetActive(root.Scenes.Failed);
            Loading.Retry.interactable=Loading.Back.interactable=!root.Scenes.Busy;
        }
        public void OpenPause()
        {
            if(Context!=UIContext.Gameplay || IsPauseOpen || Settings.gameObject.activeSelf) return;
            pauseLease=root.Pause.Acquire(); Pause.gameObject.SetActive(true); Hud.Selector.SetActive(false); Hud.Warning.gameObject.SetActive(false); root.Input.SetGameplay(false); Select(Pause.ResumeButton.gameObject);
        }
        public void Resume()
        {
            if(Settings.gameObject.activeSelf || Dialog.gameObject.activeSelf) return;
            Pause.gameObject.SetActive(false); pauseLease?.Dispose(); pauseLease=null;
            Hud.Warning.gameObject.SetActive(true);
            root.Input.SetGameplay(Context==UIContext.Gameplay); EventSystem.SetSelectedGameObject(null);
        }
        public void OpenSettings()
        {
            if(Settings.gameObject.activeSelf) return;
            settingsFromPause=IsPauseOpen; root.Input.SetGameplay(false); Settings.Open(); Select(Settings.Tabs[0].gameObject);
        }
        private void SettingsClosed()
        { if(settingsFromPause) Select(Pause.ResumeButton.gameObject); else if(Context==UIContext.Title) Select(Menu.SettingsButton.gameObject); }
        private void OnSettingsApplied() { root.Input.Clear(); Hud.RefreshBindings(root.Binding, root.Input.Controls); }
        private void Save() { var result=root.SaveProgress(); Toast(result.Success ? "局外进度已保存。" : result.Error); }
        private void OnEscape()
        {
            if(escapeFrame==Time.frameCount) return; escapeFrame=Time.frameCount;
            if(Settings.WaitingForKey) { Settings.Cancel(); return; }
            if(Dialog.gameObject.activeSelf) { Dialog.Cancel(); return; }
            if(Settings.gameObject.activeSelf) { Settings.Cancel(); return; }
            if(IsPauseOpen) Resume(); else if(Context==UIContext.Gameplay) OpenPause();
        }
        public void BindHud(IHudDataSource source,IGameplayRequestSink sink)
        { if(data!=null) data.Changed-=Render; data=source; requests=sink; if(data!=null) { data.Changed+=Render; Render(data.Current); } else Hud.Render(null); }
        private void Render(HudSnapshot snapshot) => Hud.Render(snapshot);
        private void ShowSelector(bool visible) { Hud.Selector.SetActive(visible && Context==UIContext.Gameplay && !IsPauseOpen); }
        private void SelectSlot(int index)
        { if(!Hud.Selector.activeSelf || index<0 || index>=Hud.VisibleItems.Count || index>=10) return; requests?.RequestSelectItem(Hud.VisibleItems[index].Id); }
        public void DispatchCommand(GameplayCommand command)
        {
            if(command==GameplayCommand.Skill1) requests?.RequestSkill(1);
            else if(command==GameplayCommand.Skill2) requests?.RequestSkill(2);
            else if(command==GameplayCommand.UseItem && data!=null)
            {
                var selected=Array.Find(data.Current.Items,i=>i!=null && i.Id==data.Current.SelectedItemId && i.Count>0 && i.Usable);
                if(selected!=null) requests?.RequestUseItem(selected.Id);
            }
        }
        public void Toast(string message)
        { ToastLabel.text=message; ToastLabel.gameObject.SetActive(true); toastUntil=Time.unscaledTime+4; }
        private void Update() { if(ToastLabel && ToastLabel.gameObject.activeSelf && Time.unscaledTime>=toastUntil) ToastLabel.gameObject.SetActive(false); }
        public void Unbind()
        {
            BindHud(null,null); Settings.Shutdown(); pauseLease?.Dispose(); pauseLease=null;
            if(root==null) return;
            root.Input.PauseRequested-=OnEscape; root.Input.CancelRequested-=OnEscape; root.Input.SelectionHeldChanged-=ShowSelector;
            root.Input.SlotRequested-=SelectSlot; root.Scenes.Changed-=RefreshLoading; root.Settings.Applied-=OnSettingsApplied;
            if(references!=null) foreach(var reference in references) if(reference) Destroy(reference);
        }
    }
}
