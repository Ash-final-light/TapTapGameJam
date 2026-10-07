using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Framework.FixedPoint;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Danmaku.Editor
{
    public static class EnergyOrbValidation
    {
        private sealed class Fixture : IDisposable
        {
            public readonly DanmakuSequence Sequence = ScriptableObject.CreateInstance<DanmakuSequence>();
            public readonly BattleConfig Config = ScriptableObject.CreateInstance<BattleConfig>();
            public readonly DanmakuPlayerConfig Player = ScriptableObject.CreateInstance<DanmakuPlayerConfig>();
            public readonly PlayerWeaponDefinition Weapon = ScriptableObject.CreateInstance<PlayerWeaponDefinition>();
            public readonly ProjectileDefinition Shot = ScriptableObject.CreateInstance<ProjectileDefinition>();
            public DanmakuClip Clip => Sequence.Clips[0];
            public Fixture()
            {
                Sequence.FieldSize = new Vector2(100, 100); Sequence.ClearAtEnd = false;
                Sequence.DurationTicks = 120; Sequence.CollisionEnabled = false; Sequence.MaxActiveBullets = 1024;
                Sequence.Clips.Add(new DanmakuClip
                {
                    Id = "energy-fixture", Pattern = BuiltinPattern.Split, DurationTicks = 120, EmitterPosition = Vector2.zero,
                    Keyframes = new() { new DanmakuKeyframe(), new DanmakuKeyframe { Type = DanmakuKeyframeType.Split, LocalTick = 10 } },
                    Emission = new EmissionSettings { Speed = 0 }, OverrideProjectile = true,
                    ProjectileOverride = new ProjectileSettings
                    {
                        Interaction = ProjectileInteraction.EnergyOrb, VisualSize = 0.2f, HitRadius = 0.1f,
                        EnergyOrb = new EnergyOrbSettings { FinalVisualSize = 1, FinalHitRadius = 0.5f },
                        DurationTicks = 120, SplitCount = 8, SplitSpeed = 0
                    }
                });
                Config.Sequence = Sequence; Config.Player = Player; Config.Weapon = Weapon;
                Config.PlayerSpawn = new Vector2(-20, -20); Config.BossPosition = new Vector2(0, 4);
                Config.BossHealth = 1000; Config.PlayerInvulnerableTicks = 0;
                Weapon.Projectile = Shot; Shot.Settings.Damage = 10; Shot.Settings.DurationTicks = 120;
            }
            public BattleSimulation Battle() => new(Config);
            public void Fire(BattleSimulation battle, Vector2 point, float speed = 0, int hits = 1, float angle = 0)
            {
                var s = Shot.Settings.Copy(); s.HitCount = hits;
                battle.World.SpawnFriendly(s, DanmakuSimulation.ToFP(point), FP64.FromFloat(angle), FP64.FromFloat(speed));
            }
            public void Dispose()
            { foreach (var o in new Object[] { Sequence, Config, Player, Weapon, Shot }) Object.DestroyImmediate(o); }
        }
        private static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static int Count(BattleSimulation b, BattleEventType type) => b.Events.Count(e => e.Type == type);
        private static void Step(BattleSimulation b, int ticks) { for (int i = 0; i < ticks; i++) b.Step(default); }
        [MenuItem("Tools/Danmaku/第二阶段/运行能量球与连击验证")]
        public static void Run()
        {
            var report = new List<string>();
            void Check(string name, Action test) { test(); report.Add("PASS " + name); }
            try
            {
                Check("高速穿越／Boss 与能量球目标分流／命中耗弹", TestSweep);
                Check("双方高速相对移动／网格覆盖母球路径", TestMovingTarget);
                Check("同 Tick 多弹去重／事件值快照／累计连击", TestDedup);
                Check("贯穿多球与 Boss／接触按时间排序", TestPiercing);
                Check("分裂 Tick 开始前击碎／同 Tick 爆炸优先", TestBoundary);
                Check("清弹不分裂／寿命与越界过滤／接触耗尽过滤", TestLifecycle);
                Check("母球接触伤害开关／普通敌弹不可击碎", TestInteractions);
                Check("两种膨胀曲线／动态半径／编译配置快照", TestGrowth);
                Check("膨胀导致的接触／持续重叠不重复耗弹", TestGrowthContact);
                Check("多母球池复用／不同片段隔离／子球不可再次击碎", TestReuse);
                Check("随机位置／保护距离／片段随机流／Seek 一致", TestRandomSeek);
                Check("容量保护／空间索引候选数／配置校验", TestCapacity);
            }
            catch (Exception e) { report.Add("FAIL " + e); throw; }
            finally
            {
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/EnergyOrbValidation.txt", string.Join("\n", report));
            }
            Debug.Log("Energy orb validation passed:\n" + string.Join("\n", report));
        }
        public static void RunBatch()
        {
            try { Run(); DanmakuValidation.Run(); BattleValidation.Run(); DanmakuPlayerValidation.Run(); MusicAnalysisValidation.Run(); VerifySceneBindings(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        private static void VerifySceneBindings()
        {
            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/GameplayPlaceholder.unity");
                var managers = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<BattleManager>(true)).ToArray();
                Assert(managers.Length == 1, "GameplayPlaceholder 必须有且只有一个 BattleManager。");
                var manager = managers[0]; Assert(manager.Config && manager.AimCamera && manager.Config.Validate() == null, "实战配置／瞄准相机引用失效。");
                Assert(AssetDatabase.GetAssetPath(manager.Config) == "Assets/DanmakuExamples/PhaseOne/Battle.asset", "实战配置发生替换。");
                Assert(AssetDatabase.GetAssetPath(manager.Config.Sequence.Music) == "Assets/AudioAsset/VISUAL ARTS , Key - Remembrance.mp3" && manager.Config.Sequence.MusicAnalysis, "音乐或离线分析引用失效。");
                Assert(scene.GetRootGameObjects().Any(o => o.GetComponentInChildren<TapTap.Systems.SystemRoot>(true)), "实战缺少现有 SystemRoot。");
                File.WriteAllText("Logs/EnergyOrbSceneBindings.txt", "PASS: one BattleManager; existing Battle config; AimCamera; SystemRoot; Remembrance; analysis reference. Read-only scene inspection.\n");
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive)) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
                else UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            }
        }
        private static void TestSweep()
        {
            using var f = new Fixture(); var b = f.Battle();
            f.Fire(b, new Vector2(-3, 0), 600); b.Step(default);
            Assert(b.Combo.Count == 1 && b.World.Bullets.Count == 0 && b.Boss.Health == b.Boss.MaxHealth, "高速击碎遗漏／击碎伤害误写 Boss／未耗弹。");
            Assert(b.World.Contacts.Any(c => c.TargetKind == CombatTargetKind.EnergyOrb && c.TargetId != b.Boss.Id), "接触没有记录球实体与种类。");
            Step(b, 15); Assert(b.World.TotalSpawned == 2, "击碎后仍爆炸。");
            var boss = f.Battle(); f.Fire(boss, new Vector2(0, 1), 600, angle: Mathf.PI / 2); boss.Step(default);
            Assert(boss.Combo.Count == 0 && boss.Boss.Health == boss.Boss.MaxHealth - FP64.FromInt(10), "Boss 接触误写连击。");
        }
        private static void TestMovingTarget()
        {
            using var f = new Fixture(); f.Clip.EmitterPosition = new Vector2(3, 0);
            f.Clip.Emission.Speed = 360; f.Clip.Emission.DirectionDegrees = 180;
            var b = f.Battle(); f.Fire(b, new Vector2(-3, 0), 360); b.Step(default);
            Assert(b.Combo.Count == 1, "只覆盖球的末位置，遗漏相向交叉。");
        }
        private static void TestDedup()
        {
            using var f = new Fixture(); var b = f.Battle();
            f.Fire(b, Vector2.zero); f.Fire(b, Vector2.zero); b.Step(default);
            Assert(b.Combo.Count == 1 && Count(b, BattleEventType.EnergyOrbDestroyed) == 1 && Count(b, BattleEventType.ComboChanged) == 1, "重复击碎或计数。");
            Assert(b.World.Bullets.Count == 1 && b.World.Bullets[0].RemainingHits == 1, "被先前子弹击碎的目标仍消耗后续子弹。");
            var snapshot = b.Events[0]; int id = snapshot.TargetId;
            Step(b, 11); Assert(snapshot.TargetId == id && snapshot.ComboCount == 1 && snapshot.RemainingHealth == FP64.Zero, "事件引用了池对象。");
            var next = f.Clip.Copy(); next.Id = "next"; next.StartTick = 15; f.Sequence.Clips.Add(next);
            b = f.Battle(); f.Fire(b, Vector2.zero); b.Step(default); Step(b, 14); f.Fire(b, Vector2.zero); b.Step(default);
            Assert(b.Combo.Count == 2 && b.Events.Last().ComboCount == 2, "连击未累计。");
        }
        private static void TestPiercing()
        {
            using var f = new Fixture(); f.Clip.EmitterPosition = new Vector2(-1, 0); f.Config.BossPosition = new Vector2(3, 0);
            var second = f.Clip.Copy(); second.EmitterPosition = new Vector2(1, 0); f.Sequence.Clips.Add(second);
            var b = f.Battle(); f.Fire(b, new Vector2(-3, 0), 600, 3); b.Step(default);
            Assert(b.Combo.Count == 2 && b.Boss.Health == b.Boss.MaxHealth - FP64.FromInt(10) && b.World.Bullets.Count == 0, "多目标命中耗弹／顺序错误。");
            b = f.Battle(); f.Fire(b, new Vector2(-3, 0), 600); b.Step(default);
            Assert(b.Combo.Count == 1 && b.Boss.Health == b.Boss.MaxHealth && b.World.Bullets.Count == 1, "单次命中弹穿透了先命中的球。");
        }
        private static void TestBoundary()
        {
            using var f = new Fixture(); var early = f.Battle(); Step(early, 9); f.Fire(early, Vector2.zero); early.Step(default); early.Step(default);
            Assert(early.Combo.Count == 1 && early.World.TotalSpawned == 2, "分裂 Tick 开始前命中未阻止爆炸。");
            var late = f.Battle(); Step(late, 10); f.Fire(late, Vector2.zero); late.Step(default);
            Assert(late.Combo.Count == 0 && late.World.TotalSpawned == 10 && late.World.Bullets.Count(p => p.Depth == 1) == 8, "同 Tick 时序变成了击碎优先。");
        }
        private static void TestLifecycle()
        {
            using var f = new Fixture(); var b = f.Battle(); b.Step(default); b.World.ClearEnemy(); Step(b, 12);
            Assert(b.World.TotalSpawned == 1 && b.Combo.Count == 0, "清弹产生后继或连击。");
            f.Clip.ProjectileOverride.DurationTicks = 10; b = f.Battle(); Step(b, 12); Assert(b.World.TotalSpawned == 1, "过期球爆炸。");
            f.Clip.ProjectileOverride.DurationTicks = 120; f.Clip.EmitterPosition = new Vector2(100, 0);
            b = f.Battle(); Step(b, 12); Assert(b.World.TotalSpawned == 1, "越界球爆炸。");
            f.Clip.EmitterPosition = f.Config.PlayerSpawn; f.Clip.ProjectileOverride.EnergyOrb.ContactDamage = true;
            f.Clip.ProjectileOverride.HitCount = 5;
            b = f.Battle(); Step(b, 12); Assert(b.World.TotalSpawned == 1 && b.Combo.Count == 0, "已接触消耗的母球爆炸或增加连击。");
        }
        private static void TestInteractions()
        {
            using var f = new Fixture(); f.Clip.EmitterPosition = f.Config.PlayerSpawn;
            var b = f.Battle(); b.Step(default); Assert(b.Player.Health == b.Player.MaxHealth, "关闭接触伤害仍伤害玩家。");
            f.Clip.ProjectileOverride.EnergyOrb.ContactDamage = true; b = f.Battle(); b.Step(default);
            Assert(b.Player.Health == b.Player.MaxHealth - FP64.One, "母球接触伤害未走统一结算。");
            f.Clip.EmitterPosition = Vector2.zero; f.Clip.ProjectileOverride.Interaction = ProjectileInteraction.Ordinary;
            b = f.Battle(); f.Fire(b, Vector2.zero); b.Step(default);
            Assert(b.Combo.Count == 0 && b.World.Bullets.Count == 2, "普通敌弹被玩家射击消除。");
        }
        private static void TestGrowth()
        {
            using var f = new Fixture(); var b = f.Battle(); Step(b, 5); var orb = b.World.Bullets.Single();
            Assert(Mathf.Abs(orb.HitRadius.ToFloat() - 0.3f) < 0.0001f && Mathf.Abs(orb.VisualSize.ToFloat() - 0.6f) < 0.0001f, "线性膨胀尺寸错误。");
            f.Clip.ProjectileOverride.EnergyOrb.FinalHitRadius = 10; b.Step(default);
            Assert(orb.HitRadius < FP64.One, "模板修改影响已编译的战斗快照。");
            f.Clip.ProjectileOverride.EnergyOrb.FinalHitRadius = 0.5f; f.Clip.ProjectileOverride.EnergyOrb.GrowthCurve = EnergyOrbGrowthCurve.SmoothStep;
            b = f.Battle(); Step(b, 2); orb = b.World.Bullets.Single();
            Assert(Mathf.Abs(orb.HitRadius.ToFloat() - 0.1416f) < 0.0001f, "SmoothStep 膨胀错误。");
            Step(b, 8); Assert(orb.GrowthProgress == FP64.One && Mathf.Abs(orb.HitRadius.ToFloat() - 0.5f) < 0.0001f, "爆炸前尺寸未达到配置终值。");
        }
        private static void TestGrowthContact()
        {
            using var f = new Fixture(); var b = f.Battle(); f.Fire(b, new Vector2(0.4f, 0)); Step(b, 10);
            Assert(b.Combo.Count == 1, "半径增长接触遗漏。");
            f.Shot.Settings.Damage = 0.25f; b = f.Battle(); f.Fire(b, Vector2.zero, hits: 10); Step(b, 5);
            Assert(b.Combo.Count == 0 && b.World.Bullets.First(p => p.IsEnergyOrb).Health == FP64.FromFloat(0.75f) &&
                b.World.Bullets.First(p => p.Faction == ProjectileFaction.Player).RemainingHits == 9, "持续重叠重复耗弹／伤害。");
        }
        private static void TestReuse()
        {
            using var f = new Fixture(); f.Clip.Emission.Count = 3; f.Clip.ProjectileOverride.HitCount = 5;
            var second = f.Clip.Copy(); second.Id = "second"; second.EmitterPosition = new Vector2(10, 0); second.Keyframes[1].LocalTick = 20; f.Sequence.Clips.Add(second);
            var b = f.Battle(); Step(b, 11);
            Assert(b.World.Bullets.Count(p => p.IsEnergyOrb && p.SourceClip == 1) == 3 && b.World.Bullets.Count(p => p.Depth == 1 && p.SourceClip == 0) == 24, "片段隔离或池复用丢失母球位置。");
            Assert(b.World.Bullets.Where(p => p.Depth == 1).All(p => !p.IsEnergyOrb && p.Health == FP64.Zero && p.ExplosionTick == -1 && p.GrowthProgress == FP64.Zero), "子弹保留池对象的母球状态。");
            f.Fire(b, Vector2.zero); b.Step(default); Assert(b.Combo.Count == 0, "子弹仍可击碎。");
        }
        private static string State(DanmakuSimulation sim)
        {
            var s = new StringBuilder().Append(sim.Tick).Append('|').Append(sim.TotalSpawned);
            foreach (var p in sim.Bullets) s.Append('|').Append(p.Id).Append(',').Append(p.SourceClip).Append(',')
                .Append(p.Position.X.Raw).Append(',').Append(p.Position.Y.Raw).Append(',').Append(p.HitRadius.Raw).Append(',')
                .Append(p.VisualSize.Raw).Append(',').Append(p.GrowthProgress.Raw).Append(',').Append(p.Health.Raw).Append(',').Append(p.Depth);
            return s.ToString();
        }
        private static void TestRandomSeek()
        {
            using var f = new Fixture(); f.Sequence.PreviewPlayerPosition = Vector2.zero; f.Clip.Emission.Count = 32;
            var s = f.Clip.Emission; s.SpawnPosition = SpawnPositionPolicy.RandomArea;
            s.RandomSpawnMin = new Vector2(-4, -4); s.RandomSpawnMax = new Vector2(4, 4); s.PlayerProtectionDistance = 2;
            var sim = new DanmakuSimulation(f.Sequence); sim.Step();
            Assert(sim.Bullets.All(p => p.Position.SqrMagnitude >= FP64.FromInt(4) && FPMath.Abs(p.Position.X) <= FP64.FromInt(4) && FPMath.Abs(p.Position.Y) <= FP64.FromInt(4)), "随机区域／保护距离越界。");
            string spawned = State(sim); var other = new DanmakuSimulation(f.Sequence); other.Step(); Assert(State(other) == spawned, "固定随机流不能重演。");
            for (int tick = 1; tick <= 25; tick++)
            {
                while (sim.Tick < tick) sim.Step(); other.Seek(tick); Assert(State(sim) == State(other), "Seek 在膨胀或分裂边界不同：" + tick);
            }
            var extra = f.Clip.Copy(); extra.Id = "independent"; f.Sequence.Clips.Add(extra);
            other = new DanmakuSimulation(f.Sequence); other.Step();
            sim.Seek(1); Assert(other.Bullets.Take(32).Select(p => p.Position).SequenceEqual(sim.Bullets.Take(32).Select(p => p.Position)), "新片段改变旧片段随机流。");
            s.RandomSpawnMin = s.RandomSpawnMax = Vector2.zero;
            other = new DanmakuSimulation(f.Sequence); other.Step(); Assert(other.Fault != null && other.TotalSpawned == 0, "不可满足保护距离仍出生。");
        }
        private static void TestCapacity()
        {
            using var f = new Fixture(); f.Config.MaxActiveProjectiles = 3; f.Clip.Emission.Count = 4;
            var b = f.Battle(); b.Step(default); Assert(b.World.Fault != null && b.World.Bullets.Count == 3, "母球容量保护失效。");
            f.Config.MaxActiveProjectiles = 3; f.Clip.Emission.Count = 1; b = f.Battle(); Step(b, 11);
            Assert(b.World.Fault != null && b.World.Bullets.Count <= 3, "爆炸环超过容量。");
            f.Config.MaxActiveProjectiles = 2048; f.Sequence.Clips.Clear();
            for (int i = 0; i < 64; i++)
            {
                var c = new DanmakuClip
                {
                    Pattern = BuiltinPattern.Split, DurationTicks = 120, EmitterPosition = new Vector2(20 + i % 8 * 3, 20 + i / 8 * 3),
                    Keyframes = new() { new DanmakuKeyframe(), new DanmakuKeyframe { Type = DanmakuKeyframeType.Split, LocalTick = 100 } },
                    Emission = new EmissionSettings { Speed = 0 }, OverrideProjectile = true,
                    ProjectileOverride = new ProjectileSettings { Interaction = ProjectileInteraction.EnergyOrb }
                }; f.Sequence.Clips.Add(c);
            }
            b = f.Battle(); for (int i = 0; i < 64; i++) f.Fire(b, Vector2.zero); b.Step(default);
            Assert(b.World.EnergyOrbCandidateChecks == 0, "远处母球仍生成全体两两候选。");
            f.Sequence.Clips[0].Pattern = BuiltinPattern.Single; Assert(f.Config.Validate() != null, "非子母弹能量球配置未拒绝。");
        }
    }
}
