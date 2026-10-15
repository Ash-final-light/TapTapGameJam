using System;
using UnityEngine;

namespace TapTap.Systems
{
    public enum DisplayMode { Windowed, FullScreen, Borderless }
    public enum MicrophoneState { Off, On, Unavailable }
    public enum SaveStatus { Missing, Valid, Corrupt, UnsupportedVersion }
    public enum UIContext { Title, Gameplay, Loading, SongSelect, Loadout, Result, ImportedMusic, AudioAlignment, CustomSongPrepare }

    [Serializable] public sealed class SettingsData
    {
        public int Version = 1, Width = 1920, Height = 1080, RenderFps = 60;
        public DisplayMode DisplayMode = DisplayMode.Windowed;
        public int MasterVolume = 100, MusicVolume = 100, EffectsVolume = 100;
        public float VolumeScale = 1;
        public string BindingOverridesJson = "";
        public SettingsData Copy() => (SettingsData)MemberwiseClone();
    }
    [Serializable] public sealed class ProgressValue { public string Id; public int Value; }
    [Serializable] public sealed class ProgressSaveData
    {
        public int Version = 1;
        public string LevelEntryId;
        public ProgressValue[] CharacterValues = Array.Empty<ProgressValue>();
    }
    public readonly struct OperationResult
    {
        public readonly bool Success;
        public readonly string Error;
        private OperationResult(bool success, string error) { Success = success; Error = error; }
        public static OperationResult Ok() => new(true, null);
        public static OperationResult Fail(string error) => new(false, error);
    }
    [Serializable] public sealed class ItemViewData
    {
        public string Id, Name;
        public Sprite Icon;
        [Min(0)] public int Count;
        public bool Usable = true;
    }
    public sealed class HudSnapshot
    {
        public Sprite Portrait;
        public int Health = 5, MaxHealth = 5;
        public float Stamina = 100, MaxStamina = 100;
        public Color Skill1Color = new(0.35f, 0.75f, 1), Skill2Color = new(1, 0.75f, 0.3f);
        public ItemViewData[] Items = Array.Empty<ItemViewData>();
        public string SelectedItemId, Warning;
        public MicrophoneState Microphone;
        public Vector2 WarningViewport = new(0.5f, 0.62f);
    }
    public interface IHudDataSource
    {
        HudSnapshot Current { get; }
        event Action<HudSnapshot> Changed;
    }
    public interface IGameplayRequestSink
    {
        void RequestSelectItem(string itemId);
        void RequestUseItem(string itemId);
        void RequestSkill(int index);
    }
    public interface IProgressDataProvider
    {
        ProgressValue[] Export();
        void Restore(ProgressValue[] data);
        void ResetNewGame();
    }
    public interface IPauseTarget
    {
        bool IsRunning { get; }
        void Pause();
        void Resume();
    }
}
