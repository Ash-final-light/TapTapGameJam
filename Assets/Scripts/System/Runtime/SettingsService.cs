using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TapTap.Systems
{
    public interface IDisplaySettings
    {
        bool Supports(int width, int height, DisplayMode mode);
        void Apply(SettingsData data);
        void CaptureActual(SettingsData data);
    }
    public sealed class UnityDisplaySettings : IDisplaySettings
    {
        public bool Supports(int width, int height, DisplayMode mode)
        {
            if (width < 320 || height < 180) return false;
            if (Application.isEditor || mode == DisplayMode.Windowed) return true;
            if (mode == DisplayMode.Borderless) return true; // Borderless uses the desktop resolution.
            foreach (var r in Screen.resolutions) if (r.width == width && r.height == height) return true;
            return false;
        }
        public void Apply(SettingsData data)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = data.RenderFps;
            if (Application.isEditor) return;
            var mode = data.DisplayMode == DisplayMode.Windowed ? FullScreenMode.Windowed :
                data.DisplayMode == DisplayMode.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.ExclusiveFullScreen;
            Screen.SetResolution(data.Width, data.Height, mode);
        }
        public void CaptureActual(SettingsData data)
        {
            // Keep the requested listed resolution for later mode switches.
            // A borderless window follows the desktop's physical resolution.
        }
    }
    public sealed class SettingsEditSession : IDisposable
    {
        public SettingsData Draft;
        public InputActionAsset Controls;
        internal bool Applying, Closed;
        public void Dispose()
        {
            Closed = true;
            if (Controls) { Controls.Disable(); UnityEngine.Object.Destroy(Controls); Controls = null; }
        }
    }
    public sealed class SettingsService
    {
        private readonly JsonFileStore store;
        private readonly SystemDefaults defaults;
        private readonly BindingService binding;
        private readonly AudioVolumeService audio;
        private readonly IDisplaySettings display;
        public SettingsData Current { get; private set; }
        public string StartupWarning { get; private set; }
        public event Action Applied;
        public SettingsService(JsonFileStore store, SystemDefaults defaults, BindingService binding, AudioVolumeService audio, IDisplaySettings display)
        {
            this.store = store; this.defaults = defaults; this.binding = binding; this.audio = audio; this.display = display;
            Current = defaults.CreateDefault();
            if (store.Exists("settings.json")) try
            {
                var loaded = store.Read<SettingsData>("settings.json");
                var validation = Validate(loaded);
                if (!validation.Success) StartupWarning = "设置数据无效，已恢复默认。";
                else Current = loaded;
            }
            catch (Exception e) { Debug.LogWarning(e.Message); StartupWarning = "设置文件无法读取，已恢复默认。"; }
            var bindings = binding.Load(binding.Runtime, Current.BindingOverridesJson);
            if (!bindings.Success) { StartupWarning = bindings.Error; Current.BindingOverridesJson = ""; }
            display.Apply(Current); audio.Apply(Current);
        }
        public SettingsEditSession BeginEdit()
        {
            var controls = UnityEngine.Object.Instantiate(binding.Runtime);
            controls.Disable();
            // InputBinding overrides are non-serialized; Instantiate copies the
            // binding definitions but drops the already applied runtime rebinds.
            controls.LoadBindingOverridesFromJson(binding.Runtime.SaveBindingOverridesAsJson());
            return new SettingsEditSession { Draft = Current.Copy(), Controls = controls };
        }
        public void Preview(SettingsEditSession session) { if (!session.Closed) audio.Apply(session.Draft); }
        public void Reset(SettingsEditSession session)
        { session.Draft = defaults.CreateDefault(); session.Controls.RemoveAllBindingOverrides(); Preview(session); }
        public void Cancel(SettingsEditSession session) { if (session.Applying) return; audio.Apply(Current); session.Dispose(); }
        public OperationResult Validate(SettingsData data)
        {
            if (data == null || data.Version != 1 || !Enum.IsDefined(typeof(DisplayMode), data.DisplayMode)) return OperationResult.Fail("设置版本或显示模式无效。");
            if (!display.Supports(data.Width, data.Height, data.DisplayMode)) return OperationResult.Fail("当前显示模式不支持该分辨率。");
            if(!Array.Exists(defaults.Resolutions,size=>size.x==data.Width && size.y==data.Height)) return OperationResult.Fail("分辨率不在可选列表中。");
            if (Array.IndexOf(defaults.FrameRates, data.RenderFps) < 0) return OperationResult.Fail("帧率选项无效。");
            if (data.MasterVolume < 0 || data.MasterVolume > 100 || data.MusicVolume < 0 || data.MusicVolume > 100 || data.EffectsVolume < 0 || data.EffectsVolume > 100 ||
                float.IsNaN(data.VolumeScale) || float.IsInfinity(data.VolumeScale) || data.VolumeScale < 0 || data.VolumeScale > 1)
                return OperationResult.Fail("音量范围无效。");
            return OperationResult.Ok();
        }
        public IEnumerator Apply(SettingsEditSession session, Action<OperationResult> complete)
        {
            if (session.Closed || session.Applying) { complete(OperationResult.Fail("设置正在应用或已关闭。")); yield break; }
            var check = Validate(session.Draft);
            if (check.Success) check = binding.Validate(session.Controls);
            if (!check.Success) { complete(check); yield break; }
            session.Applying = true;
            var previous = Current.Copy(); var next = session.Draft.Copy();
            next.BindingOverridesJson = session.Controls.SaveBindingOverridesAsJson();
            var start = ApplyRuntime(next);
            if (!start.Success) { ApplyRuntime(previous); session.Applying = false; complete(start); yield break; }
            yield return null; yield return null;
            OperationResult result;
            try
            {
                display.CaptureActual(next);
                store.Write("settings.json", next);
                Current = next; session.Draft = next.Copy(); result = OperationResult.Ok();
            }
            catch (Exception e) { Debug.LogException(e); ApplyRuntime(previous); result = OperationResult.Fail("设置未保存，请检查存储空间及权限。"); }
            session.Applying = false;
            if (result.Success) Applied?.Invoke();
            complete(result);
        }
        private OperationResult ApplyRuntime(SettingsData data)
        {
            try { display.Apply(data); audio.Apply(data); return binding.Load(binding.Runtime, data.BindingOverridesJson); }
            catch (Exception e) { Debug.LogException(e); return OperationResult.Fail("无法应用当前设置。"); }
        }
    }
}
