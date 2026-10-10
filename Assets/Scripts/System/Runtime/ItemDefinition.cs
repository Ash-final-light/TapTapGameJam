using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public string Id, DisplayName;
        [TextArea] public string Description;
        public Sprite Icon;
        public bool Passive;
        [Min(1)] public int UsesPerRun = 3;
        [Min(0)] public int CooldownTicks = 60;
        public ItemEffect Effect;
    }
}
