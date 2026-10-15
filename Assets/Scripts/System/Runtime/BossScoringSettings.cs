using System;
using UnityEngine;

namespace TapTap.Systems
{
    [Serializable]
    public sealed class BossScoringSettings
    {
        public bool Enabled;
        [Min(0.001f)] public float HitRadius = 0.4f;
        [Min(0)] public int PointsPerHit = 100;
        [Min(0)] public int OnBeatBonus = 50;
        [Min(0)] public float BeatWindowSeconds = 0.1f;
        [Min(0.01f)] public float ComboTimeoutBeats = 2;

        public BossScoringSettings Copy() => (BossScoringSettings)MemberwiseClone();
        public string Validate()
        {
            if (!Enabled) return null;
            if (!Finite(HitRadius) || HitRadius <= 0 || PointsPerHit < 0 || OnBeatBonus < 0 ||
                !Finite(BeatWindowSeconds) || BeatWindowSeconds < 0 ||
                !Finite(ComboTimeoutBeats) || ComboTimeoutBeats <= 0)
                return "Boss 计分半径、分值或时间窗口无效。";
            return null;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
