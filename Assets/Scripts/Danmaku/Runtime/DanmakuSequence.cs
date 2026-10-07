using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Danmaku
{
    public enum DanmakuKeyframeType { Emit, Split }

    [Serializable]
    public sealed class DanmakuKeyframe
    {
        [HideInInspector] public string Id = Guid.NewGuid().ToString("N");
        [Min(0)] public int LocalTick;
        public DanmakuKeyframeType Type;
        public DanmakuKeyframe Copy() => new() { LocalTick = LocalTick, Type = Type };
    }

    [Serializable]
    public sealed class DanmakuClip
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "发射片段";
        [Min(0)] public int StartTick;
        [Min(1)] public int DurationTicks = 180;
        public bool Muted;
        public BuiltinPattern Pattern;
        public List<DanmakuKeyframe> Keyframes = new();
        public bool IsSplit => Preset && !OverrideEmission ? Preset.Pattern == BuiltinPattern.Split : Pattern == BuiltinPattern.Split;
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
            copy.Keyframes = Keyframes?.ConvertAll(k => k?.Copy()) ?? new();
            return copy;
        }

        public string ValidateKeyframes()
        {
            if (Keyframes == null) return "关键帧列表为空引用。";
            int emit = 0, split = 0, emitTick = -1, splitTick = -1;
            var ids = new HashSet<string>();
            foreach (var key in Keyframes)
            {
                if (key == null || string.IsNullOrEmpty(key.Id) || !ids.Add(key.Id)) return "关键帧 ID 缺失或重复。";
                if (key.LocalTick < 0 || key.LocalTick >= DurationTicks) return "关键帧超出片段范围，请调整时间或片段长度。";
                if (key.Type == DanmakuKeyframeType.Emit) { emit++; emitTick = key.LocalTick; }
                else if (key.Type == DanmakuKeyframeType.Split) { split++; splitTick = key.LocalTick; }
                else return "未知关键帧类型。";
            }
            if (!IsSplit && split > 0) return "只有子母弹片段可添加分裂点。";
            if (IsSplit && (emit > 1 || split > 1)) return "子母弹只能有一个发射点和一个分裂点。";
            if (split > 0 && (emit == 0 || splitTick <= emitTick)) return "分裂点必须晚于唯一发射点。";
            var p = EffectiveProjectileSettings;
            if (p.Interaction == ProjectileInteraction.EnergyOrb)
            {
                if (!IsSplit) return "能量球必须使用子母弹片段。";
                if (emit > 0 && split != 1) return "能量球发射需要一个更晚的分裂点。";
                if (p.EnergyOrb == null || !FinitePositive(p.VisualSize) || !FinitePositive(p.HitRadius) ||
                    !FinitePositive(p.EnergyOrb.FinalVisualSize) || !FinitePositive(p.EnergyOrb.FinalHitRadius)) return "能量球视觉尺寸和判定半径必须是有限正数。";
                if (p.EnergyOrb.FinalVisualSize < p.VisualSize || p.EnergyOrb.FinalHitRadius < p.HitRadius) return "能量球膨胀结束尺寸／半径不能小于初始值。";
                if (!Enum.IsDefined(typeof(EnergyOrbGrowthCurve), p.EnergyOrb.GrowthCurve)) return "未知膨胀曲线。";
            }
            var emission = EffectiveEmission;
            if (emission.SpawnPosition == SpawnPositionPolicy.RandomArea &&
                (!Finite(emission.RandomSpawnMin.x) || !Finite(emission.RandomSpawnMin.y) || !Finite(emission.RandomSpawnMax.x) || !Finite(emission.RandomSpawnMax.y) ||
                 emission.RandomSpawnMin.x > emission.RandomSpawnMax.x || emission.RandomSpawnMin.y > emission.RandomSpawnMax.y ||
                 !Finite(emission.PlayerProtectionDistance) || emission.PlayerProtectionDistance < 0)) return "随机出生矩形／保护距离无效。";
            return null;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool FinitePositive(float value) => Finite(value) && value > 0;
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
                Name = "环形扩散", Pattern = BuiltinPattern.Ring, DurationTicks = 240, Emission = DanmakuPresetCatalog.Create(BuiltinPattern.Ring),
                ProjectileOverride = new ProjectileSettings { Color = new Color(0.35f, 0.7f, 1), DurationTicks = 180 }
            });
            sequence.Clips.Add(new DanmakuClip
            {
                Name = "自机狙", Pattern = BuiltinPattern.AimedFan, StartTick = 120, DurationTicks = 240,
                Emission = DanmakuPresetCatalog.Create(BuiltinPattern.AimedFan),
                ProjectileOverride = new ProjectileSettings { Color = new Color(1, 0.45f, 0.4f), DurationTicks = 180 }
            });
            sequence.Clips.Add(new DanmakuClip
            {
                Name = "子母弹", Pattern = BuiltinPattern.Split, StartTick = 300, DurationTicks = 120,
                Emission = DanmakuPresetCatalog.Create(BuiltinPattern.Split),
                ProjectileOverride = new ProjectileSettings
                { Color = new Color(0.75f, 0.5f, 1), VisualSize = 0.28f, DurationTicks = 180 }
            });
            for (int tick = 0; tick < 240; tick += 45)
                sequence.Clips[0].Keyframes.Add(new DanmakuKeyframe { LocalTick = tick });
            for (int tick = 0; tick < 240; tick += 36)
                sequence.Clips[1].Keyframes.Add(new DanmakuKeyframe { LocalTick = tick });
            sequence.Clips[2].Keyframes.Add(new DanmakuKeyframe());
            sequence.Clips[2].Keyframes.Add(new DanmakuKeyframe { LocalTick = 60, Type = DanmakuKeyframeType.Split });
            sequence.Events.Add(new DanmakuTimelineEvent { Tick = 270, DurationTicks = 30, Label = "子母弹预警", ClipIndex = 2 });
            return sequence;
        }
    }
}
