using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Danmaku
{
    [Serializable]
    public sealed class DanmakuClip
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "发射片段";
        [Min(0)] public int StartTick;
        [Min(1)] public int DurationTicks = 180;
        public bool Muted;
        public Vector2 EmitterPosition = new(0, 3);
        public Vector2 EmitterEndPosition = new(0, 3);
        public bool MoveEmitter;
        public EmissionPreset Preset;
        public bool OverrideEmission = true;
        public EmissionSettings Emission = new();
        public ProjectileDefinition Projectile;
        public bool OverrideProjectile;
        public ProjectileSettings ProjectileOverride = new();
        public int EndTick => StartTick + DurationTicks;
        public EmissionSettings EffectiveEmission => !OverrideEmission && Preset ? Preset.Settings : Emission;
        public ProjectileDefinition EffectiveProjectile => Projectile ? Projectile : Preset ? Preset.Projectile : null;
        public ProjectileSettings EffectiveProjectileSettings => OverrideProjectile ? ProjectileOverride :
            EffectiveProjectile ? EffectiveProjectile.Settings : ProjectileOverride;

        public DanmakuClip Copy()
        {
            var copy = (DanmakuClip)MemberwiseClone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Emission = Emission?.Copy() ?? new EmissionSettings();
            copy.ProjectileOverride = ProjectileOverride?.Copy() ?? new ProjectileSettings();
            return copy;
        }
    }

    public enum DanmakuEventType { Warning, ClearBullets }

    [Serializable]
    public sealed class DanmakuTimelineEvent
    {
        [Min(0)] public int Tick;
        public DanmakuEventType Type;
        public string Label = "预警";
        [Min(1)] public int DurationTicks = 60;
        public int ClipIndex = -1;
    }

    [CreateAssetMenu(menuName = "Danmaku/Boss Sequence", fileName = "BossSequence")]
    public sealed class DanmakuSequence : ScriptableObject
    {
        public const int TickRate = 60;
        [Min(1)] public int DurationTicks = 480;
        [Min(1)] public int MaxActiveBullets = 256;
        [Range(0, 4)] public int MaxSplitDepth = 2;
        public int RandomSeed = 12345;
        [Header("音乐与离线节拍提示")]
        public AudioClip Music;
        public MusicAnalysisAsset MusicAnalysis;
        public MusicAnalysisSettings MusicAnalysisSettings = new();
        [Tooltip("正值表示音乐晚于战斗开始；负值表示从歌曲中段开始。")]
        public float MusicStartSeconds;
        public Vector2 FieldSize = new(9, 11);
        public Vector2 PreviewPlayerPosition = new(0, -4);
        public DanmakuPlayerConfig PreviewPlayerConfig;
        public float PreviewHitRadius => PreviewPlayerConfig ? PreviewPlayerConfig.HitRadius : 0.08f;
        public Sprite PreviewSprite => PreviewPlayerConfig ? PreviewPlayerConfig.Sprite : null;
        public Sprite BossSprite;
        public bool CollisionEnabled = true;
        public bool ClearAtEnd = true;
        public List<DanmakuClip> Clips = new();
        public List<DanmakuTimelineEvent> Events = new();

        public static DanmakuSequence CreateExample()
        {
            var sequence = CreateInstance<DanmakuSequence>();
            sequence.name = "Boss 弹幕示例";
            sequence.Clips.Add(new DanmakuClip
            {
                Name = "环形扩散", DurationTicks = 240, Emission = DanmakuPresetCatalog.Create(BuiltinPattern.Ring),
                ProjectileOverride = new ProjectileSettings { Color = new Color(0.35f, 0.7f, 1), DurationTicks = 180 }
            });
            sequence.Clips.Add(new DanmakuClip
            {
                Name = "自机狙", StartTick = 120, DurationTicks = 240,
                Emission = DanmakuPresetCatalog.Create(BuiltinPattern.AimedFan),
                ProjectileOverride = new ProjectileSettings { Color = new Color(1, 0.45f, 0.4f), DurationTicks = 180 }
            });
            sequence.Clips.Add(new DanmakuClip
            {
                Name = "子母弹", StartTick = 300, DurationTicks = 120,
                Emission = DanmakuPresetCatalog.Create(BuiltinPattern.Split),
                ProjectileOverride = new ProjectileSettings
                { Color = new Color(0.75f, 0.5f, 1), VisualSize = 0.28f, DurationTicks = 180, SplitAgeTicks = 60 }
            });
            sequence.Events.Add(new DanmakuTimelineEvent { Tick = 270, DurationTicks = 30, Label = "子母弹预警", ClipIndex = 2 });
            return sequence;
        }
    }
}
