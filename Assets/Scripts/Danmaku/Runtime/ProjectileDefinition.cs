using System;
using UnityEngine;

namespace TapTap.Danmaku
{
    public enum ProjectileDamageSource { Explicit, ConfigId }
    [Serializable]
    public sealed class ProjectileSettings
    {
        public Sprite Sprite;
        public Color Color = Color.white;
        [Min(0.01f)] public float VisualSize = 0.18f;
        [Min(0.001f)] public float HitRadius = 0.06f;
        [Min(1)] public int DurationTicks = 240;
        [Min(1)] public int HitCount = 1;
        public int DamageConfigId;
        public ProjectileDamageSource DamageSource;
        [Min(0)] public float Damage = 1;
        public int[] TargetBuffIds = Array.Empty<int>();
        [Tooltip("0 禁用；以子弹出生年龄触发。清弹和越界不触发分裂。")]
        [Min(0)] public int SplitAgeTicks;
        public ProjectileDefinition SplitChild;
        [Range(1, 64)] public int SplitCount = 8;
        [Min(0)] public float SplitSpeed = 2.5f;
        public bool InheritDirection = true;

        public ProjectileSettings Copy()
        {
            var copy = (ProjectileSettings)MemberwiseClone();
            copy.TargetBuffIds = TargetBuffIds == null ? Array.Empty<int>() : (int[])TargetBuffIds.Clone();
            return copy;
        }
    }

    [CreateAssetMenu(menuName = "Danmaku/Projectile Definition", fileName = "Projectile")]
    public sealed class ProjectileDefinition : ScriptableObject
    {
        public ProjectileSettings Settings = new();
    }
}
