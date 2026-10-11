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
        [Tooltip("单局最多携带数量，实际数量还受库存和每局使用次数限制。")]
        [Min(1)] public int MaxHeldPerRun = 3;
        [Min(1)] public int UsesPerRun = 3;
        [Min(0)] public int CooldownTicks = 60;
        public ItemEffect Effect;
    }
}
