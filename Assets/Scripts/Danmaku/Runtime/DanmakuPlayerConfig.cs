using UnityEngine;

namespace TapTap.Danmaku
{
    [CreateAssetMenu(menuName = "Danmaku/Player Config", fileName = "PlayerConfig")]
    public sealed class DanmakuPlayerConfig : ScriptableObject
    {
        [Min(0)] public float NormalMoveSpeed = 4;
        [Min(0)] public float FocusMoveSpeed = 1.8f;
        [Min(0.001f)] public float HitRadius = 0.08f;
        public Sprite Sprite;
        [Min(0.001f)] public float VisualSize = 0.22f;
    }
}
