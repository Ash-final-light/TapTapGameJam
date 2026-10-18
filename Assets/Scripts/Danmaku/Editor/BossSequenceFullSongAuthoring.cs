using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using Inputs;
using TapTap.Player;
using TapTap.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Danmaku.Editor
{
    /// <summary>One-time authoring of editable assets, not a runtime chart generator.</summary>
    public static class BossSequenceFullSongAuthoring
    {
        public const string SequencePath = "Assets/DanmakuExamples/BossSequence.asset";
        public const string Folder = "Assets/DanmakuExamples/Authored155";
        public const string SongPath = "Assets/Configs/Spotlight/Run/Drums155.asset";
        public const string SongId = "drums155-90s-v1";
        private static readonly int[] StageBeats = { 0, 32, 64, 96, 112, 160, 208, 224 };
        private static readonly string[] StageNames = { "主题：环形与追射", "横向走位：左右交替", "能量球：瞄准击破", "恢复：调整站位与体力", "变奏：移动发射器", "综合：追射与能量球", "高潮：最后四小节", "收束：保持到曲终" };
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        public static int BeatTick(int beat) => (int)Math.Round((0.19 + beat * 60.0 / 155) * 60);

        public static void BuildBatch()
        {
            try { Build(); Validate(); CapturePreviews(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void ValidateBatch()
        {
            try
            {
                Validate(); BossSequenceBoundsValidation.Run();
                EditorSceneManager.OpenScene(TapTap.Systems.Editor.BossRhythmSceneSetup.ScenePath);
                TapTap.Systems.Editor.BossScoringValidation.Run();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Build()
        {
            var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(SequencePath);
            Require(sequence && sequence.Music && sequence.Clips.Count == 4,
                "仅用于四条原始轨道的首次扩编；已有完整谱面请直接编辑 SO，不能重复生成覆盖设计。 ");
            Require(Math.Abs(sequence.ManualMusicBpm - 155) < .001 && Math.Abs(sequence.ManualMusicBeatOffsetSeconds - .19) < .001,
                "当前 BPM 或首拍已变化，请保留人工修改并重新规划。 ");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/DanmakuExamples", "Authored155");
            string[] opening = sequence.Clips.Select(JsonUtility.ToJson).ToArray();
            var ring = SavePreset("155BPM_环形_四拍", sequence.Clips[0]);
            var aimed = SavePreset("155BPM_自机狙_四拍", sequence.Clips[1]);
            var child = NewAsset<ProjectileDefinition>("能量球_分裂子弹");
            child.Settings = ring.Projectile.Settings.Copy();
            child.Settings.Color = new Color(1, .75f, .3f); child.Settings.DurationTicks = 150;
            child.Settings.Interaction = ProjectileInteraction.Ordinary; child.Settings.SplitChild = null;
            var orb = NewAsset<EmissionPreset>("155BPM_能量球_四拍击破");
            orb.Pattern = BuiltinPattern.Split; orb.DurationTicks = BeatTick(4) - BeatTick(0) + 1;
            orb.Settings = new EmissionSettings { Shape = EmissionShape.Point, Count = 1, Speed = 0 };
            orb.Keyframes = new() { new() { LocalTick = 0 }, new() { Type = DanmakuKeyframeType.Split, LocalTick = orb.DurationTicks - 1 } };
            var mother = ScriptableObject.CreateInstance<ProjectileDefinition>(); mother.name = "能量球母弹";
            mother.Settings = new ProjectileSettings
            {
                Color = new Color(1, .8f, .25f), VisualSize = .55f, HitRadius = .16f, DurationTicks = 180,
                Interaction = ProjectileInteraction.EnergyOrb, SplitChild = child, SplitCount = 8, SplitSpeed = 8,
                InheritDirection = false,
                EnergyOrb = new EnergyOrbSettings { ContactDamage = false, FinalVisualSize = 1.1f, FinalHitRadius = .4f, GrowthCurve = EnergyOrbGrowthCurve.SmoothStep }
            };
            AssetDatabase.AddObjectToAsset(mother, orb); orb.Projectile = mother;

            sequence.DurationTicks = MusicTime.EndTick(sequence.MusicStartSeconds + (double)sequence.Music.samples / sequence.Music.frequency);
            sequence.FieldSize = new Vector2(21.12f, 11.88f); // 16:9 authoring reference, runtime remains camera driven.
            sequence.UseCameraBounds = sequence.ClearAtEnd = true;

            // B: use the established two voices, alternating left/right origins.
            for (int beat = 32; beat < 64; beat += 16)
            {
                float x = beat == 32 ? -4 : 4;
                AddPhrase(sequence, aimed, beat, "B 横向追射", new Vector2(x, 3), 0, 4, 8, 12);
                AddPhrase(sequence, ring, beat, "B 环形回应", new Vector2(-x * .5f, 3), 3, 7, 11, 15);
            }
            // C: each orb replaces a ring accent; no third simultaneous firing voice.
            for (int beat = 64; beat < 96; beat += 16)
            {
                var aim = AddPhrase(sequence, aimed, beat, "C 稀疏追射", new Vector2(0, 3), 0, 8);
                aim.Emission.Count = 3;
                AddOrb(sequence, orb, beat + 3, 4, new Vector2(-3, .6f), false);
                AddOrb(sequence, orb, beat + 11, 4, new Vector2(3, .6f), false);
            }
            var rest = AddPhrase(sequence, ring, 96, "D 恢复 · 稀疏环形", new Vector2(0, 3), 3, 11);
            rest.Emission.Count = 8;
            for (int beat = 112; beat < 160; beat += 16)
            {
                int index = (beat - 112) / 16;
                var aim = AddPhrase(sequence, aimed, beat, "E 主题追射", new Vector2(index % 2 == 0 ? -4 : 4, 3), 0, 4, 8, 12);
                var reply = AddPhrase(sequence, ring, beat, "E 环形变奏", new Vector2(0, 3), 3, 7, 11, 15);
                if (index == 1) Move(reply, new Vector2(-4, 3), new Vector2(4, 3));
                if (index == 2) Move(aim, new Vector2(4, 3), new Vector2(-4, 3));
            }
            for (int beat = 160; beat < 208; beat += 16)
            {
                int index = (beat - 160) / 16; float side = index % 2 == 0 ? -1 : 1;
                var aim = AddPhrase(sequence, aimed, beat, "F 综合追射", new Vector2(side * 4, 3), 0, 4, 8, 12);
                aim.Emission.ArcDegrees = 50;
                var reply = AddPhrase(sequence, ring, beat, "F 环形回应", new Vector2(-side * 3, 3), 7, 15);
                if (index == 1) Move(reply, new Vector2(-3, 3), new Vector2(3, 3));
                AddOrb(sequence, orb, beat + 3, 3, new Vector2(side * 3, .8f), true);
                AddOrb(sequence, orb, beat + 11, 3, new Vector2(-side * 3, .8f), true);
            }
            var finaleAim = AddPhrase(sequence, aimed, 208, "G 高潮追射", new Vector2(-3, 3), 0, 4, 8, 12);
            Move(finaleAim, new Vector2(-3, 3), new Vector2(3, 3));
            var finaleRing = AddPhrase(sequence, ring, 208, "G 高潮环形", new Vector2(0, 3), 3, 7, 11, 15);
            finaleRing.Emission.Count = 14;

            for (int i = 0; i < StageBeats.Length; i++)
            {
                int tick = i == 0 ? 0 : BeatTick(StageBeats[i]);
                int end = i + 1 < StageBeats.Length ? BeatTick(StageBeats[i + 1]) : sequence.DurationTicks;
                sequence.Events.Add(new DanmakuTimelineEvent { Tick = tick, DurationTicks = end - tick, Type = DanmakuEventType.Warning, Label = StageNames[i] });
            }
            sequence.Events.Add(new DanmakuTimelineEvent { Tick = sequence.DurationTicks - 12, Type = DanmakuEventType.ClearBullets, Label = "尾奏清弹", DurationTicks = 1 });
            sequence.Events = sequence.Events.OrderBy(e => e.Tick).ThenBy(e => e.ClipIndex).ToList();
            for (int i = 0; i < opening.Length; i++) Require(opening[i] == JsonUtility.ToJson(sequence.Clips[i]), "原始片段被意外修改。");
            RebuildAnalysis(sequence);
            ConfigureSong(sequence);
            foreach (var value in new Object[] { sequence, ring, aimed, orb, mother, child }) EditorUtility.SetDirty(value);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/BossSequenceOpeningSnapshot.jsonl", opening);
        }

        private static T NewAsset<T>(string name) where T : ScriptableObject
        {
            string path = Folder + "/" + name + ".asset";
            Require(!File.Exists(path), "资源已存在，避免覆盖：" + path);
            var asset = ScriptableObject.CreateInstance<T>(); asset.name = name; AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static EmissionPreset SavePreset(string name, DanmakuClip source)
        {
            var preset = NewAsset<EmissionPreset>(name);
            preset.Pattern = source.Pattern; preset.DurationTicks = source.DurationTicks;
            preset.Settings = source.EffectiveEmission.Copy(); preset.Keyframes = source.Keyframes.ConvertAll(k => k.Copy());
            var projectile = ScriptableObject.CreateInstance<ProjectileDefinition>(); projectile.name = "子弹模板";
            projectile.Settings = source.EffectiveProjectileSettings.Copy();
            AssetDatabase.AddObjectToAsset(projectile, preset); preset.Projectile = projectile;
            EditorUtility.SetDirty(projectile); return preset;
        }
        private static DanmakuClip AddPhrase(DanmakuSequence sequence, EmissionPreset preset, int beat, string name, Vector2 origin, params int[] offsets)
        {
            var clip = new DanmakuClip
            {
                Id = "drums155-" + beat + "-" + preset.Pattern, Name = name + " · " + beat + "拍",
                StartTick = BeatTick(beat), DurationTicks = BeatTick(beat + 16) - BeatTick(beat), Pattern = preset.Pattern,
                Preset = preset, OverrideEmission = true, Emission = preset.Settings.Copy(),
                Projectile = preset.Projectile, OverrideProjectile = false,
                EmitterPosition = origin, EmitterEndPosition = origin
            };
            foreach (int offset in offsets) clip.Keyframes.Add(new DanmakuKeyframe { Id = clip.Id + "-" + offset, LocalTick = BeatTick(beat + offset) - clip.StartTick });
            sequence.Clips.Add(clip); return clip;
        }
        private static void Move(DanmakuClip clip, Vector2 start, Vector2 end)
        { clip.MoveEmitter = true; clip.EmitterPosition = start; clip.EmitterEndPosition = end; }
        private static void AddOrb(DanmakuSequence sequence, EmissionPreset preset, int beat, int delayBeats, Vector2 origin, bool advanced)
        {
            int start = BeatTick(beat), split = BeatTick(beat + delayBeats);
            var clip = new DanmakuClip
            {
                Id = "drums155-orb-" + beat, Name = (advanced ? "F 综合能量球" : "C 教学能量球") + " · " + beat + "拍",
                StartTick = start, DurationTicks = split - start + 1,
                Pattern = BuiltinPattern.Split, Preset = preset, OverrideEmission = false,
                EmitterPosition = origin, EmitterEndPosition = origin, Projectile = preset.Projectile,
                Keyframes = new() { new() { Id = "orb-emit-" + beat, LocalTick = 0 }, new() { Id = "orb-split-" + beat, LocalTick = split - start, Type = DanmakuKeyframeType.Split } }
            };
            sequence.Clips.Add(clip);
            sequence.Events.Add(new DanmakuTimelineEvent
            {
                Tick = BeatTick(beat - 1), DurationTicks = split - BeatTick(beat - 1), ClipIndex = sequence.Clips.Count - 1,
                Label = advanced ? "能量球 · 击破或躲开分裂" : "金色能量球 · 瞄准击破", Type = DanmakuEventType.Warning
            });
        }

        private static void RebuildAnalysis(DanmakuSequence sequence)
        {
            const string temporary = Folder + "/AnalysisReadback.mp3";
            string source = AssetDatabase.GetAssetPath(sequence.Music);
            Require(AssetDatabase.CopyAsset(source, temporary), "无法创建 PCM 解析副本。");
            try
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(temporary);
                var options = importer.defaultSampleSettings;
                options.loadType = AudioClipLoadType.DecompressOnLoad; options.compressionFormat = AudioCompressionFormat.PCM;
                options.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate; options.preloadAudioData = true;
                importer.defaultSampleSettings = options; importer.loadInBackground = false;
                foreach (var platform in new[] { "Standalone", "Android", "iOS", "WebGL" })
                    if (importer.ContainsSampleSettingsOverride(platform)) importer.ClearSampleSettingOverride(platform);
                importer.SaveAndReimport();
                var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(temporary);
                Require(audio && audio.LoadAudioData() && audio.loadState == AudioDataLoadState.Loaded, "PCM 读取副本尚未加载。");
                var settings = sequence.MusicAnalysisSettings;
                var analyzer = new MusicSpectrumAnalyzer(audio.frequency, audio.channels, audio.samples, settings);
                for (int frame = 0; frame < audio.samples;)
                {
                    int count = Math.Min(4096, audio.samples - frame);
                    var buffer = new float[count * audio.channels];
                    Require(audio.GetData(buffer, frame), "PCM 读取失败。"); analyzer.Push(buffer, count); frame += count;
                }
                analyzer.Finish();
                var analysis = NewAsset<MusicAnalysisAsset>("Drums155_音乐分析");
                analysis.Source = sequence.Music; analysis.SampleRate = audio.frequency; analysis.Channels = audio.channels; analysis.SampleFrameCount = audio.samples;
                analysis.AlgorithmVersion = MusicAnalysisAsset.CurrentAlgorithmVersion;
                analysis.SourceFingerprint = AssetDatabase.GetAssetDependencyHash(source).ToString();
                analysis.SettingsFingerprint = JsonUtility.ToJson(settings);
                analysis.Markers = analyzer.GetMarkers(); analysis.WaveformMinMax = analyzer.GetWaveform();
                sequence.MusicAnalysis = analysis; EditorUtility.SetDirty(analysis);
            }
            finally { AssetDatabase.DeleteAsset(temporary); }
        }
        private static void ConfigureSong(DanmakuSequence sequence)
        {
            const string previous = "Assets/Configs/Spotlight/Run/Remembrance.asset";
            var song = AssetDatabase.LoadAssetAtPath<SongDefinition>(previous);
            Require(song, "原有歌曲入口不存在，不能猜测目录配置。");
            Require(song.Find(SongDifficulty.Normal)?.Battle?.Sequence == sequence, "歌曲入口不再指向当前 BossSequence。");
            Require(string.IsNullOrEmpty(AssetDatabase.MoveAsset(previous, SongPath)), "无法重命名当前歌曲入口。");
            song.Id = SongId; song.Title = "鼓点舞台 · 155 BPM";
            song.Subtitle = "90 秒 · 环形、追射与能量球"; song.Artist = "155 BPM / 4拍节奏";
            EditorUtility.SetDirty(song);
        }

        [MenuItem("工具/弹幕/验证 155 BPM 整曲关卡")]
        public static void Validate()
        {
            var report = new List<string>();
            try
            {
                var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(SequencePath);
                Require(sequence && sequence.DurationTicks == MusicTime.EndTick((double)sequence.Music.samples / sequence.Music.frequency), "总时长未覆盖音频。");
                Require(sequence.Clips.Count == 35 && sequence.Events.Count(e => e.Type == DanmakuEventType.Warning && e.ClipIndex < 0) == 8, "阶段或片段不完整。");
                Require(MusicAnalysisJob.IsCurrent(sequence), "分析与当前鼓曲不匹配。");
                var ids = new HashSet<string>();
                foreach (var clip in sequence.Clips)
                {
                    Require(ids.Add(clip.Id), "片段 ID 重复。"); Require(clip.ValidateKeyframes() == null, clip.Name + ": " + clip.ValidateKeyframes());
                    Require(clip.EndTick <= sequence.DurationTicks, "片段越过曲终。");
                }
                foreach (var clip in sequence.Clips.Skip(4))
                    foreach (var key in clip.Keyframes)
                    {
                        int tick = clip.StartTick + key.LocalTick;
                        int nearest = (int)Math.Round((tick / 60.0 - .19) * 155 / 60);
                        Require(tick == BeatTick(nearest), "新关键帧偏离全局 BPM 网格。");
                    }
                Require(sequence.Clips.Where(c => c.IsSplit).Count() == 10, "能量球教学／综合段未完成。");
                Require(sequence.Clips.SelectMany(c => c.Keyframes.Where(k => k.Type == DanmakuKeyframeType.Emit).Select(k => c.StartTick + k.LocalTick)).Max() < BeatTick(224), "收束段仍在发射。");
                Require(sequence.Events.Any(e => e.Type == DanmakuEventType.ClearBullets && e.Tick > BeatTick(224)), "尾奏缺少清弹。");
                Require(TapTap.Systems.UI.BossRhythmHud.TimelineWarningAt(sequence, 0)?.Label == StageNames[0], "开场阶段提示缺失。");
                Require(TapTap.Systems.UI.BossRhythmHud.TimelineWarningAt(sequence, BeatTick(67))?.ClipIndex >= 0, "能量球提示未覆盖阶段提示。");
                Require(TapTap.Systems.UI.BossRhythmHud.TimelineWarningAt(sequence, BeatTick(224))?.Label == StageNames[7], "收束提示缺失。");
                Require(TapTap.Systems.UI.BossRhythmHud.TimelineWarningAt(sequence, sequence.DurationTicks) == null, "提示未在曲终退出。");
                var song = AssetDatabase.LoadAssetAtPath<SongDefinition>(SongPath);
                Require(song && song.Id == SongId && song.Find(SongDifficulty.Normal).Battle.Sequence == sequence, "歌曲入口或成绩 ID 失配。");
                var system = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
                Require(system.Songs.Find(SongId) == song, "选曲目录未引用新歌曲。");
                report.Add($"PASS assets: {sequence.DurationTicks} ticks; {sequence.Clips.Count} clips; 10 orbs; 8 stages; manual BPM grid; current FFT analysis; song={song.Id}");
                CheckOpening(sequence); report.Add("PASS opening: original 4 clips and all settings preserved");
                foreach (float aspect in new[] { 16f / 9, 4f / 3 })
                {
                    var preview = new DanmakuSimulation(sequence);
                    preview.World.SetBounds(new Rect(-5.94f * aspect, -5.94f, 11.88f * aspect, 11.88f));
                    preview.SetPlayerState(new DanmakuPlayerState(FPVector2.FromInt(0, -4), FPVector2.FromInt(0, -4), FP64.FromFloat(.08f), false));
                    int exploded = 0; preview.World.EnergyOrbMissed += _ => exploded++;
                    while (!preview.Finished && preview.Fault == null) preview.Step();
                    Require(preview.Fault == null && preview.Tick == sequence.DurationTicks, preview.Fault ?? "整曲模拟未到末尾。");
                    Require(exploded == 10 && preview.Bullets.Count == 0, "母球提前离屏或尾奏仍有敌弹。");
                    Require(preview.PeakBullets < 128, "密度超过首版 Normal 预算。");
                    report.Add($"PASS unbroken-orb preview aspect={aspect:0.###}: spawned={preview.TotalSpawned}; peak={preview.PeakBullets}; exploded={exploded}; tail=clear");
                }
                RunPilots(sequence, report);
            }
            catch (Exception error) { report.Add("FAIL " + error); throw; }
            finally { Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/BossSequenceFullSongValidation.txt", report); }
            Debug.Log(string.Join("\n", report));
        }
        private static void CheckOpening(DanmakuSequence sequence)
        {
            string path = "Logs/BossSequenceOpeningSnapshot.jsonl";
            if (File.Exists(path))
            {
                string[] expected = File.ReadAllLines(path);
                for (int i = 0; i < 4; i++) Require(expected[i] == JsonUtility.ToJson(sequence.Clips[i]), "开场原片段发生改变。");
            }
            int[][] ticks = { new[] {81,174,267,360}, new[] {11,104,197,290} };
            for (int i = 0; i < 4; i++)
                Require(sequence.Clips[i].StartTick == (i < 2 ? 0 : 372) && sequence.Clips[i].Keyframes.Select(k => k.LocalTick).SequenceEqual(ticks[i % 2]), "开场原节拍发生改变。");
        }

        private static void RunPilots(DanmakuSequence sequence, List<string> report)
        {
            var config = AssetDatabase.LoadAssetAtPath<BattleConfig>("Assets/DanmakuExamples/PhaseOne/Battle.asset");
            var definition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>("Assets/Player/MaidGhost.asset");
            int completed = 0;
            foreach (float aspect in new[] {16f / 9, 4f / 3})
            {
                bool survived = false;
                for (int route = 0; route < 3 && !survived; route++)
                {
                    var battle = new BattleSimulation(config, definition, new Vector2(0, -4), bossScoring: new BossScoringSettings { Enabled = true },
                        bounds: new Rect(-5.94f * aspect, -5.94f, 11.88f * aspect, 11.88f));
                    var scores = new ScoreManager(null, new BossScoringSettings { Enabled = true }, sequence);
                    int orbs = 0, damage = 0; float extent = 5.94f * aspect - .5f;
                    while (!battle.Finished && battle.World.Fault == null)
                    {
                        var state = battle.Player.Pose; Vector2 player = DanmakuSimulation.ToVector(state.Position);
                        float target = Mathf.Sin(battle.Tick / (95f + route * 25) + route) * Mathf.Min(6, extent);
                        short axis = (short)(Math.Sign(target - player.x) * short.MaxValue);
                        bool threat = false;
                        foreach (var p in battle.World.Bullets)
                        {
                            if (p.Faction != ProjectileFaction.Enemy || p.IsEnergyOrb) continue;
                            Vector2 point = DanmakuSimulation.ToVector(p.Position), previous = DanmakuSimulation.ToVector(p.PreviousPosition);
                            Vector2 soon = point + (point - previous) * 8;
                            if (Vector2.Distance(soon, player) < .8f) threat = true;
                        }
                        var edges = threat && battle.Tick % 3 == 0 ? PlayerButtonEdges.Dash : PlayerButtonEdges.None;
                        FPVector2 aim = battle.BossPosition;
                        var orb = battle.World.Bullets.FirstOrDefault(p => p.IsEnergyOrb);
                        if (orb != null) aim = orb.Position;
                        var input = new BattleInput(new InputSnapshot(axis, 0, fireHeld: true), aim, true);
                        battle.Step(input, new PlayerTickInput(axis, fireHeld: true, edges: edges));
                        scores.Advance(battle.Tick);
                        foreach (var e in battle.Events)
                        {
                            scores.Consume(e);
                            if (e.Type == BattleEventType.EnergyOrbDestroyed) orbs++;
                            if (e.Type == BattleEventType.Damage) damage++;
                        }
                    }
                    Require(battle.World.Fault == null, battle.World.Fault);
                    survived = battle.EndReason == RunEndReason.Completed;
                    report.Add($"PILOT aspect={aspect:0.###} route={route}: tick={battle.Tick}; outcome={battle.EndReason}; hp={battle.Player.Health}; damage={damage}; orbsDestroyed={orbs}; score={scores.CurrentScore}");
                    if (survived) completed++;
                }
                Require(survived, "标准 5 HP 走位／冲刺路线未能完成当前宽高比，请继续调整编排。");
            }
            Require(completed == 2, "未完成两种窗口比例的正常角色整曲测试。");
        }

        private static void CapturePreviews()
        {
            var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(SequencePath);
            int[] samples = { BeatTick(39) + 8, BeatTick(70) - 8, BeatTick(107) + 8, BeatTick(139) + 8, BeatTick(182) + 8, BeatTick(219) + 8 };
            var render = new PreviewRenderUtility();
            var root = new GameObject("Full song preview") { hideFlags = HideFlags.HideAndDontSave };
            render.AddSingleGO(root);
            try
            {
                using var pool = new DanmakuSpritePool(root.transform);
                render.camera.orthographic = true; render.camera.orthographicSize = 5.94f;
                render.camera.transform.position = new Vector3(0, 0, -10); render.camera.transform.rotation = Quaternion.identity;
                render.camera.clearFlags = CameraClearFlags.SolidColor; render.camera.backgroundColor = new Color(.025f, .04f, .075f);
                foreach (int tick in samples)
                {
                    var sim = new DanmakuSimulation(sequence); sim.Seek(tick); pool.Sync(sim, sequence);
                    render.BeginStaticPreview(new Rect(0, 0, 960, 540)); render.Render(true);
                    var image = render.EndStaticPreview();
                    try { File.WriteAllBytes("Logs/Boss155-" + tick + ".png", image.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(image); }
                }
            }
            finally { render.Cleanup(); }
        }
    }
}
