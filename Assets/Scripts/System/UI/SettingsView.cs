using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class SettingsView : MonoBehaviour
    {
        public TMP_Dropdown Resolution, FrameRate, Mode;
        public UnityEngine.UI.Slider[] Volumes;
        public TMP_Text[] VolumeLabels;
        public GameObject[] Pages;
        public UnityEngine.UI.Button[] Tabs;
        public UnityEngine.UI.Image[] TabBorders;
        public UnityEngine.UI.Button ResetButton, ApplyButton, CancelButton;
        public KeybindRowView[] KeyRows;
        public GameObject RebindOverlay;
        public TMP_Text RebindPrompt;
        public Action CloseRequested;
        public bool WaitingForKey => rebind != null;
        public bool Applying => session?.Applying ?? false;
        public SettingsEditSession Session => session;
        private SystemRoot root;
        private SettingsEditSession session;
        private InputActionRebindingExtensions.RebindingOperation rebind;
        private readonly List<Vector2Int> resolutions = new();
        private bool refreshing;
        private int selectedPage;
        public void Bind(SystemRoot owner)
        {
            root = owner;
            Resolution.onValueChanged.AddListener(OnResolution); FrameRate.onValueChanged.AddListener(OnFps); Mode.onValueChanged.AddListener(OnMode);
            for (int i = 0; i < Volumes.Length; i++) { int index = i; Volumes[i].onValueChanged.AddListener(v => OnVolume(index,v)); }
            for (int i = 0; i < Tabs.Length; i++) { int index = i; Tabs[i].onClick.AddListener(() => SelectPage(index)); }
            foreach (var row in KeyRows) { var item = row; row.Button.onClick.AddListener(() => BeginRebind(item)); }
            ResetButton.onClick.AddListener(ResetDraft); ApplyButton.onClick.AddListener(Apply); CancelButton.onClick.AddListener(Cancel);
        }
        public void Open()
        {
            session = root.Settings.BeginEdit(); session.Controls.Disable();
            gameObject.SetActive(true); selectedPage = 0; Refresh(); SelectPage(0);
        }
        private void SelectPage(int index)
        {
            selectedPage = index;
            for (int i = 0; i < Pages.Length; i++) { Pages[i].SetActive(i == index); TabBorders[i].enabled = i == index; }
        }
        public void Refresh()
        {
            if (session == null) return;
            refreshing = true;
            var data = session.Draft;
            resolutions.Clear(); resolutions.AddRange(root.Config.Defaults.Resolutions);
            Resolution.ClearOptions(); var options = new List<string>();
            var display = new UnityDisplaySettings();
            foreach (var size in resolutions) options.Add(size.x + "×" + size.y + (display.Supports(size.x,size.y,data.DisplayMode) ? "" : "（不可用）"));
            Resolution.AddOptions(options); Resolution.SetValueWithoutNotify(Math.Max(0,resolutions.IndexOf(new Vector2Int(data.Width,data.Height))));
            FrameRate.ClearOptions(); options.Clear(); foreach (int fps in root.Config.Defaults.FrameRates) options.Add(fps < 0 ? "无限制" : fps + " FPS");
            FrameRate.AddOptions(options); FrameRate.SetValueWithoutNotify(Array.IndexOf(root.Config.Defaults.FrameRates,data.RenderFps));
            Mode.SetValueWithoutNotify((int)data.DisplayMode);
            int[] values = { data.MasterVolume,data.MusicVolume,data.EffectsVolume,Mathf.RoundToInt(data.VolumeScale*100) };
            for (int i = 0; i < values.Length; i++) { Volumes[i].SetValueWithoutNotify(values[i]); VolumeLabels[i].text = values[i].ToString(); }
            foreach (var row in KeyRows) row.BindingText.text = root.Binding.Display(session.Controls,row.ActionName);
            SetInteractable(!Applying); refreshing = false;
        }
        private void OnResolution(int index)
        { if (refreshing || session == null || index < 0 || index >= resolutions.Count) return; var size = resolutions[index]; session.Draft.Width = size.x; session.Draft.Height = size.y; }
        private void OnFps(int index) { if (!refreshing && session != null) session.Draft.RenderFps = root.Config.Defaults.FrameRates[index]; }
        private void OnMode(int index) { if (refreshing || session == null) return; session.Draft.DisplayMode = (DisplayMode)index; Refresh(); }
        private void OnVolume(int index, float value)
        {
            if (refreshing || session == null) return;
            int number = Mathf.RoundToInt(value); VolumeLabels[index].text = number.ToString();
            switch(index) { case 0: session.Draft.MasterVolume=number; break; case 1: session.Draft.MusicVolume=number; break;
                case 2: session.Draft.EffectsVolume=number; break; default: session.Draft.VolumeScale=number/100f; break; }
            root.Settings.Preview(session);
        }
        private void ResetDraft() { if (Applying) return; StopRebind(); root.Settings.Reset(session); Refresh(); }
        public void Apply()
        {
            if (Applying || WaitingForKey) return;
            SetInteractable(false);
            root.StartCoroutine(root.Settings.Apply(session, result => { Refresh(); root.UI.Toast(result.Success ? "设置已应用并保存。" : result.Error); }));
        }
        public void Cancel()
        {
            if (Applying) return;
            if (WaitingForKey) { StopRebind(); return; }
            if (session != null) { root.Settings.Cancel(session); session = null; }
            gameObject.SetActive(false); CloseRequested?.Invoke();
        }
        private void SetInteractable(bool enabled)
        {
            ApplyButton.interactable = ResetButton.interactable = CancelButton.interactable = enabled;
            Resolution.interactable = FrameRate.interactable = Mode.interactable = enabled;
            foreach (var volume in Volumes) volume.interactable = enabled;
            foreach (var row in KeyRows) row.Button.interactable = enabled;
        }
        private void BeginRebind(KeybindRowView row)
        {
            if (Applying || WaitingForKey) return;
            var action = session.Controls.FindAction("Gameplay/" + row.ActionName, true);
            string previous = action.bindings[0].overridePath;
            RebindPrompt.text = "按下新键：" + row.Label.text + "\nEsc 取消；数字键保留给道具栏";
            RebindOverlay.SetActive(true); SetInteractable(false);
            rebind = action.PerformInteractiveRebinding(0).WithControlsHavingToMatchPath("<Keyboard>/*")
                .WithControlsExcluding("<Mouse>").WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ => FinishRebind()).OnComplete(operation =>
                {
                    var result = root.Binding.Validate(session.Controls);
                    if (!result.Success) { if (previous == null) action.RemoveBindingOverride(0); else action.ApplyBindingOverride(0,previous); root.UI.Toast(result.Error); }
                    FinishRebind();
                }).Start();
        }
        private void FinishRebind() { var operation = rebind; rebind = null; operation?.Dispose(); RebindOverlay.SetActive(false); Refresh(); }
        private void StopRebind() { if (rebind != null) rebind.Cancel(); RebindOverlay.SetActive(false); }
        public void Shutdown()
        { StopRebind(); if (session != null && !Applying) root.Settings.Cancel(session); session = null; }
    }
}
