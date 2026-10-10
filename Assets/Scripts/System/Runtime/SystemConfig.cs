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
        public SongCatalog Songs;
        public TapTap.Danmaku.PlayerWeaponDefinition[] Weapons = System.Array.Empty<TapTap.Danmaku.PlayerWeaponDefinition>();
        public ItemDefinition[] Items = System.Array.Empty<ItemDefinition>();
        [Range(1, 10)] public int ItemSlotCount = 3;
        public ItemDropTable Drops;
        public ScoreFormula[] ScoreFormulas = System.Array.Empty<ScoreFormula>();
        public string SaveFolder = "Spotlight";
        public string TitleScene = "Assets/Scenes/Title.unity", LoadingScene = "Assets/Scenes/Loading.unity";
    }
}
