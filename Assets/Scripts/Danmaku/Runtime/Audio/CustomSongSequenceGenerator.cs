using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TapTap.Danmaku
{
    /// <summary>Compiles bounded onset selections into the same timeline used by authored battles.</summary>
    public static class CustomSongSequenceGenerator
    {
        public const int Version = 2;
        public const int EnemyBudget = 120;
        public static DanmakuSequence Generate(string songId, AudioClip music, double bpm, double firstBeat,
            MusicAnalysisAsset analysis, BattleConfig template)
        {
            if (!music || !analysis || !template || !template.Sequence || bpm < 30 || bpm > 300 ||
                double.IsNaN(bpm) || double.IsInfinity(bpm) || firstBeat < 0 ||
                double.IsNaN(firstBeat) || double.IsInfinity(firstBeat) || firstBeat >= music.length)
                throw new ArgumentException("歌曲、对齐或战斗模板无效。");
            var sequence = ScriptableObject.CreateInstance<DanmakuSequence>();
            sequence.name = "CustomSong_v" + Version;
            sequence.Music = music; sequence.MusicAnalysis = analysis;
            sequence.ManualMusicBpm = (float)bpm; sequence.ManualMusicBeatOffsetSeconds = (float)firstBeat;
            sequence.DurationTicks = MusicTime.EndTick((double)music.samples / music.frequency);
            sequence.FieldSize = template.Sequence.FieldSize; sequence.BossSprite = template.Sequence.BossSprite;
            sequence.RandomSeed = StableSeed(songId); sequence.MaxActiveBullets = template.MaxActiveProjectiles;
            double duration = (double)music.samples / music.frequency, period = 60 / bpm;
            double lead = Math.Max(period, 0.5), interval = Math.Max(2 * period, 0.75);
            double tail = Math.Min(8 * period, duration * 0.15);
            double opening = Math.Max(lead, Math.Min(2 * period, duration * 0.1));
            var valid = analysis.Markers.Where(m => m.Band >= 0 && m.Band < 3 && m.SampleFrame >= 0 &&
                m.SampleFrame < analysis.SampleFrameCount && m.Strength > 0 && !float.IsInfinity(m.Strength) &&
                !float.IsNaN(m.Strength)).OrderBy(m => m.SampleFrame).ThenBy(m => m.Band).ToArray();
            var peaks = new float[3];
            foreach (var marker in valid) peaks[marker.Band] = Math.Max(peaks[marker.Band], marker.Strength);
            float globalPeak = peaks.Max();
            // Normalizing a nearly silent band to its own maximum amplifies FFT leakage.
            // Keep a relative significance floor, then normalize meaningful band activity.
            valid = valid.Where(m => m.Strength >= globalPeak * .03f).ToArray();
            float Weight(MusicMarker m) => m.Strength / Math.Max(peaks[m.Band], globalPeak * .25f);
            // One representative per short cluster. Raw onset times are preserved.
            var selected = new List<MusicMarker>();
            for (int i = 0; i < valid.Length;)
            {
                int end = i + 1; var best = valid[i];
                double until = (double)valid[i].SampleFrame / analysis.SampleRate + Math.Min(period * 0.5, 0.2);
                while (end < valid.Length && (double)valid[end].SampleFrame / analysis.SampleRate <= until)
                {
                    var candidate = valid[end++];
                    if (Weight(candidate) > Weight(best)) best = candidate;
                }
                selected.Add(best); i = end;
            }
            int nextAttackTick = 0, intervalTicks = (int)Math.Ceiling(interval * DanmakuSequence.TickRate - 1e-6);
            double orbFree = 0;
            var occupied = new List<(int endTick, int count)>();
            foreach (var marker in selected)
            {
                double seconds = (double)marker.SampleFrame / analysis.SampleRate;
                long block = Math.Max(0, (long)Math.Floor((seconds - firstBeat) / (4 * period)));
                int tick = (int)Math.Round(seconds * DanmakuSequence.TickRate);
                if (seconds < Math.Max(firstBeat, opening) || seconds >= duration - tail || tick < nextAttackTick || block % 6 == 5) continue;
                bool orb = marker.Band == 2 && marker.Strength / peaks[2] >= 0.55f && seconds >= orbFree;
                double fuse = Math.Max(4 * period, 1.5);
                if (orb && seconds + fuse + 1 >= duration) orb = false;
                var clip = MakeClip(sequence, template, marker.Band, tick, orb, fuse);
                occupied.RemoveAll(x => x.endTick <= tick);
                int cost = clip.Emission.Count + (orb ? clip.ProjectileOverride.SplitCount : 0);
                if (occupied.Sum(x => x.count) + cost > EnemyBudget) continue;
                int index = sequence.Clips.Count;
                clip.Id = songId + "-v" + Version + "-" + index;
                clip.Keyframes[0].Id = clip.Id + "-emit";
                if (orb) clip.Keyframes[1].Id = clip.Id + "-split";
                sequence.Clips.Add(clip);
                sequence.Events.Add(new DanmakuTimelineEvent { Tick = Math.Max(0, tick - (int)Math.Ceiling(lead * 60)),
                    DurationTicks = (int)Math.Ceiling(lead * 60), Type = DanmakuEventType.Warning, ClipIndex = index,
                    Label = (marker.Band == 0 ? "LOW|" : marker.Band == 1 ? "MID|" : "HIGH|") + clip.Name });
                occupied.Add((tick + clip.ProjectileOverride.DurationTicks + (orb ? 240 : 0), cost));
                if (orb) orbFree = seconds + fuse + Math.Max(period * 2, 1);
                nextAttackTick = tick + intervalTicks;
            }
            return sequence;
        }

        private static DanmakuClip MakeClip(DanmakuSequence sequence, BattleConfig template, int band, int tick, bool orb, double fuse)
        {
            var clip = new DanmakuClip { StartTick = tick, DurationTicks = 240, EmitterPosition = template.BossPosition,
                Pattern = band == 0 ? BuiltinPattern.Ring : band == 1 ? BuiltinPattern.AimedFan : BuiltinPattern.Fan,
                OverrideEmission = true, OverrideProjectile = true };
            clip.Name = band == 0 ? "重音扩散" : band == 1 ? "旋律追射" : "晶音弹群";
            clip.Emission = DanmakuPresetCatalog.Create(clip.Pattern);
            clip.Emission.Count = band == 0 ? 10 : band == 1 ? 3 : 5;
            clip.Emission.Speed = band == 0 ? 1.7f : band == 1 ? 2.3f : 2.1f;
            clip.Emission.ArcDegrees = band == 1 ? 45 : 65;
            clip.ProjectileOverride = new ProjectileSettings { Color = band == 0 ? new Color(1, .5f, .35f) :
                band == 1 ? new Color(.3f, .8f, 1) : new Color(.85f, .6f, 1),
                VisualSize = band == 0 ? .23f : .17f, HitRadius = band == 0 ? .075f : .055f, DurationTicks = 240 };
            clip.Keyframes.Add(new DanmakuKeyframe { Type = DanmakuKeyframeType.Emit, LocalTick = 0 });
            if (orb)
            {
                int split = Math.Max(1, (int)Math.Ceiling(fuse * 60));
                clip.Name = "晶音能量球"; clip.Pattern = BuiltinPattern.Split;
                clip.DurationTicks = split + 1;
                float side = ((sequence.Clips.Count + sequence.RandomSeed) & 1) == 0 ? -1 : 1;
                clip.EmitterPosition = new Vector2(side * Mathf.Min(1.2f, sequence.FieldSize.x * .2f),
                    Mathf.Min(template.BossPosition.y - 1, sequence.FieldSize.y * .25f));
                clip.Emission = new EmissionSettings { Count = 1, Speed = 0 };
                clip.ProjectileOverride.VisualSize = .38f; clip.ProjectileOverride.HitRadius = .19f;
                clip.ProjectileOverride.Interaction = ProjectileInteraction.EnergyOrb;
                clip.ProjectileOverride.EnergyOrb = new EnergyOrbSettings { ContactDamage = false,
                    FinalVisualSize = .7f, FinalHitRadius = .35f, GrowthCurve = EnergyOrbGrowthCurve.SmoothStep };
                clip.ProjectileOverride.DurationTicks = split + 1;
                clip.ProjectileOverride.SplitCount = 8; clip.ProjectileOverride.SplitSpeed = 1.8f;
                clip.Keyframes.Add(new DanmakuKeyframe { Type = DanmakuKeyframeType.Split, LocalTick = split });
            }
            return clip;
        }
        public static int StableSeed(string value)
        {
            unchecked { uint seed = 2166136261; foreach (char c in value ?? "") seed = (seed ^ c) * 16777619; return (int)(seed & 0x7fffffff); }
        }
    }
}
