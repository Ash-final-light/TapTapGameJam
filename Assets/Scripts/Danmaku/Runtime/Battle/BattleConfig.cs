using UnityEngine;
namespace TapTap.Danmaku
{
    [CreateAssetMenu(menuName = "Danmaku/Battle Config")]
    public sealed class BattleConfig : ScriptableObject
    {
        public DanmakuSequence Sequence;
        [Min(0.001f)] public float BossHealth = 1000;
        public Vector2 BossPosition = new(0, 3);
        [Min(0.001f)] public float BossHitRadius = 0.4f;
        public bool ConsumeBulletWhenInvulnerable = true;
        [Min(1)] public int MaxActiveProjectiles = 2048;
        public string Validate()
        {
            if (!Sequence) return "需要指定 Sequence。";
            if (!Positive(BossHealth) || !Positive(BossHitRadius)) return "Boss 生命和半径必须是有限正数。";
            if (MaxActiveProjectiles < 1) return "弹幕容量必须大于零。";
            if (!Positive(Sequence.FieldSize.x) || !Positive(Sequence.FieldSize.y)) return "场地尺寸必须是有限正数。";
            foreach (var clip in Sequence.Clips)
            {
                if (clip == null) return "Sequence 含空片段。";
                if (!clip.Muted && clip.ValidateKeyframes() is string error) return $"片段 {clip.Name}：{error}";
            }
            return null;
        }
        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
