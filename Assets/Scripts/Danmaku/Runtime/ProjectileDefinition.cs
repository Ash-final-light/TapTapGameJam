using System;
using UnityEngine;

namespace TapTap.Danmaku
{
    public enum ProjectileDamageSource { Explicit, ConfigId }
    public enum ProjectileInteraction { Ordinary, EnergyOrb }
    public enum EnergyOrbGrowthCurve { Linear, SmoothStep }
    [Serializable]
    public sealed class EnergyOrbSettings
    {
        public const int MaxHealth = 1;
        [Tooltip("母球是否造成原有敌弹接触伤害；有效命中后母球回收，不能再爆炸。")]
        public bool ContactDamage;
        [Min(0.01f)] public float FinalVisualSize = 0.18f;
        [Min(0.001f)] public float FinalHitRadius = 0.06f;
        public EnergyOrbGrowthCurve GrowthCurve;
        public EnergyOrbSettings Copy() => (EnergyOrbSettings)MemberwiseClone();
    }
    [Serializable]
    public sealed class ProjectileSettings
    {
        public Sprite Sprite;
        public Color Color = Color.white;
        [Min(0.01f)] public float VisualSize = 0.18f;
        [Min(0.001f)] public float HitRadius = 0.06f;
        public ProjectileInteraction Interaction;
        public EnergyOrbSettings EnergyOrb = new();
        [Min(1)] public int DurationTicks = 240;
        [Min(1)] public int HitCount = 1;
        public int DamageConfigId;
        public ProjectileDamageSource DamageSource;
        [Min(0)] public float Damage = 1;
        public int[] TargetBuffIds = Array.Empty<int>();
        [Tooltip("子母弹分裂点使用的子弹模板；为空时使用缩小的同色子弹。子弹不继续分裂。")]
        public ProjectileDefinition SplitChild;
        [Range(1, 64)] public int SplitCount = 8;
        [Min(0)] public float SplitSpeed = 2.5f;
        public bool InheritDirection = true;

        public ProjectileSettings Copy()
        {
            var copy = (ProjectileSettings)MemberwiseClone();
            copy.EnergyOrb = EnergyOrb?.Copy() ?? new EnergyOrbSettings();
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
