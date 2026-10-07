using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Danmaku
{
    public enum EmissionShape { Point, Arc, Ring, Segment }
    public enum BuiltinPattern { Single, AimedFan, Fan, Ring, Spiral, Wall, Petals, Split }
    public enum SpawnPositionPolicy { Emitter, RandomArea }

    [Serializable]
    public sealed class EmissionSettings
    {
        public EmissionShape Shape;
        [Range(1, 64)] public int Count = 1;
        public float DirectionDegrees = -90;
        [Range(0, 360)] public float ArcDegrees = 60;
        [Min(0)] public float SpawnRadius;
        public bool AimAtPlayer;
        public float RotationDegreesPerSecond;
        public float AlternatePhaseDegrees;
        [Min(0)] public float Speed = 2;
        [Range(1, 8)] public int SpeedLayers = 1;
        public float SpeedLayerDelta = 0.4f;
        [Range(0, 180)] public float RandomAngleDegrees;
        public Vector2 SegmentStart = new(-3, 0);
        public Vector2 SegmentEnd = new(3, 0);
        [Tooltip("RandomArea 使用战斗局部坐标中的绝对矩形；独立采样每颗弹，覆盖形状的位置偏移。")]
        public SpawnPositionPolicy SpawnPosition;
        public Vector2 RandomSpawnMin, RandomSpawnMax;
        [Min(0)] public float PlayerProtectionDistance;
        public EmissionSettings Copy() => (EmissionSettings)MemberwiseClone();
    }

    [CreateAssetMenu(menuName = "Danmaku/Emission Preset", fileName = "Emission")]
    public sealed class EmissionPreset : ScriptableObject
    {
        public BuiltinPattern Pattern;
        [Min(1)] public int DurationTicks = 180;
        public List<DanmakuKeyframe> Keyframes = new();
        public EmissionSettings Settings = new();
        public ProjectileDefinition Projectile;
    }

    public static class DanmakuPresetCatalog
    {
        public static readonly string[] Names =
        {
            "定向单发／连射", "自机狙扇形", "固定扇形", "环形／多层环",
            "旋转扫射／螺旋", "线阵／墙弹", "花瓣／交错环", "子母弹（单次发射／分裂）"
        };

        public static EmissionSettings Create(BuiltinPattern pattern)
        {
            var s = new EmissionSettings();
            switch (pattern)
            {
                case BuiltinPattern.Single: break;
                case BuiltinPattern.AimedFan:
                    s.Shape = EmissionShape.Arc; s.Count = 5; s.AimAtPlayer = true;
                    s.DirectionDegrees = 0; break;
                case BuiltinPattern.Fan:
                    s.Shape = EmissionShape.Arc; s.Count = 7; s.ArcDegrees = 100; break;
                case BuiltinPattern.Ring:
                    s.Shape = EmissionShape.Ring; s.Count = 12; break;
                case BuiltinPattern.Spiral:
                    s.Shape = EmissionShape.Ring; s.Count = 3;
                    s.RotationDegreesPerSecond = 75; break;
                case BuiltinPattern.Wall:
                    s.Shape = EmissionShape.Segment; s.Count = 9; break;
                case BuiltinPattern.Petals:
                    s.Shape = EmissionShape.Ring; s.Count = 8; s.SpeedLayers = 2;
                    s.AlternatePhaseDegrees = 22.5f; break;
                case BuiltinPattern.Split:
                    s.Shape = EmissionShape.Arc; s.Count = 3; s.ArcDegrees = 50;
                    s.Speed = 1.5f; break;
            }
            return s;
        }
    }
}
