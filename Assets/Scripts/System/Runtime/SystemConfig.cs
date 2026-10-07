using UnityEngine;
using UnityEngine.InputSystem;
using TapTap.Systems.UI;

namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/System Config")]
    public sealed class SystemConfig : ScriptableObject
    {
        public SystemDefaults Defaults;
        public LevelEntryCatalog Levels;
        public LoadingTipConfig LoadingTips;
        public InputActionAsset Controls;
        public SystemUIController UIPrefab;
        public string SaveFolder = "Spotlight";
        public string TitleScene = "Assets/Scenes/Title.unity", LoadingScene = "Assets/Scenes/Loading.unity";
    }
}
