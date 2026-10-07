using UnityEngine;

namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Default Settings")]
    public sealed class SystemDefaults : ScriptableObject
    {
        public SettingsData Settings = new();
        public Vector2Int[] Resolutions = { new(512,288),new(768,432),new(1024,576),new(1280,720),
            new(1334,750),new(1366,768),new(1536,864),new(1600,900),new(1920,1080),new(2048,1152),new(2560,1440) };
        public int[] FrameRates = { 1,5,10,20,30,60,120,-1 };
        public SettingsData CreateDefault()
        {
            var result = Settings.Copy();
            var display = Screen.currentResolution;
            // Prefer native resolution when it is in the approved list; otherwise
            // use the largest listed size that fits the desktop.
            foreach(var size in Resolutions)
                if(size.x<=display.width && size.y<=display.height) { result.Width=size.x; result.Height=size.y; }
            result.DisplayMode = Application.isEditor || Debug.isDebugBuild ? DisplayMode.Windowed : DisplayMode.FullScreen;
            result.BindingOverridesJson = "";
            return result;
        }
    }
}
