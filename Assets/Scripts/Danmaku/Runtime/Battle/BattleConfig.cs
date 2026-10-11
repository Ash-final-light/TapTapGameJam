using UnityEngine;
namespace TapTap.Danmaku
{
    [CreateAssetMenu(menuName = "弹幕/战斗配置")]
    public sealed class BattleConfig : ScriptableObject
    {
        public DanmakuSequence Sequence;
        public Vector2 BossPosition = new(0, 3);
        public bool ConsumeBulletWhenInvulnerable = true;
        [Min(1)] public int MaxActiveProjectiles = 2048;
        public string Validate()
        {
            if (!Sequence) return "需要指定 Sequence。";
            if (!Finite(BossPosition.x) || !Finite(BossPosition.y)) return "Boss 位置必须是有限数值。";
            if (!Finite(Sequence.MusicStartSeconds)) return "音乐开始偏移必须是有限数值。";
            if (MaxActiveProjectiles < 1) return "弹幕容量必须大于零。";
            if (!Positive(Sequence.FieldSize.x) || !Positive(Sequence.FieldSize.y)) return "场地尺寸必须是有限正数。";
            foreach (var clip in Sequence.Clips)
            {
                if (clip == null) return "Sequence 含空片段。";
                if (!clip.Muted && clip.ValidateKeyframes() is string error) return $"片段 {clip.Name}：{error}";
            }
            return null;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => value > 0 && Finite(value);
    }
}
