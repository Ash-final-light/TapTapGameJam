#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using Inputs;
using TapTap.Danmaku;
using TapTap.Player;
using UnityEditor;
using UnityEngine;
namespace TapTap.Systems.Editor
{
    public static class SpotlightRunValidation
    {
        private static readonly List<string> report = new();
        private static readonly List<UnityEngine.Object> owned = new();
        private static T Make<T>() where T : ScriptableObject { var obj = ScriptableObject.CreateInstance<T>(); owned.Add(obj); return obj; }
        private static void Require(bool value,string message) { if (!value) throw new Exception(message); }
        private static void Test(string name,Action action) { try { action(); report.Add("PASS " + name); } catch (Exception e) { report.Add("FAIL " + name + ": " + e); } }
        public static void InstallAndRun() { SpotlightRunAssetBuilder.Install(); Run(); }
        public static void RunRegression() { Run(); TapTap.Danmaku.Editor.BattleValidation.Run(); }
        [MenuItem("Tools/Spotlight/验证歌曲装备与结算")]
        public static void Run()
        {
            report.Clear();
            try
            {
                Test("评分组合、断连、评级、结算只生成一次",Scoring);
                Test("统一存档、失败局最高分、重复结算、重启和重置",Persistence);
                Test("保存失败不更新内存且可重试",SaveFailure);
                Test("Buff 叠层、移除、Tick 到期与资源隔离",Buffs);
                Test("主动道具冷却、数量、被动效果与重试恢复",Items);
                Test("掉落权重、必掉一个、坏配置拒绝",Drops);
                Test("时间轴通关、Boss 死亡不结束、玩家死亡失败",EndConditions);
                Test("Buff 实际影响移速、射速、子弹速度和伤害",RuntimeStats);
                Test("能量球到期、分裂遗漏、击破及清理区分",OrbLifecycle);
                Test("配置与 Prefab 完整、暂停保存按钮已删除",Assets);
            }
            finally { foreach (var obj in owned) if (obj) UnityEngine.Object.DestroyImmediate(obj); owned.Clear(); }
            Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/SpotlightRunValidation.txt",report);
            if (report.Any(r => r.StartsWith("FAIL"))) throw new Exception(string.Join("\n",report));
            Debug.Log(string.Join("\n",report));
        }
        private static BattleStartData Setup()
        {
            var song = Make<SongDefinition>(); song.Id = "test-song";
            var difficulty = new SongDifficultyConfig { Difficulty = SongDifficulty.Normal, Rating = Make<ThresholdRatingFormula>() }; song.Difficulties = new[] { difficulty };
            return new BattleStartData(song,difficulty,null,null);
        }
        private static void Scoring()
        {
            var formulas = new ScoreFormula[] { Make<OrbScoreFormula>(), Make<ComboScoreFormula>(), Make<MissScoreFormula>(), Make<CompletionScoreFormula>() };
            var manager = new ScoreManager(formulas);
            var orb = new ProjectileState(); var shot = new ProjectileState { Settings = new ProjectileSettings() };
            manager.Consume(new BattleEvent(BattleEventType.EnergyOrbDestroyed,0,orb,shot,FP64.One,1));
            manager.Consume(new BattleEvent(BattleEventType.EnergyOrbDestroyed,1,orb,shot,FP64.One,2)); manager.MissOrb();
            manager.Consume(new BattleEvent(BattleEventType.EnergyOrbDestroyed,2,orb,shot,FP64.One,1));
            var result = manager.Finish("r",Setup(),60,RunEndReason.Completed);
            Require(result.Score == 1270 && result.Stats.MaxCombo == 2 && result.Stats.Misses == 1 && result.Stats.DestroyedOrbs == 3,"统计或组合分数错误");
            manager.MissOrb(); Require(ReferenceEquals(result,manager.Finish("other",Setup(),99,RunEndReason.PlayerDied)) && result.Stats.Misses == 1,"结算没有锁定");
            var failed = new ScoreManager(formulas); failed.MissOrb(); Require(failed.Finish("f",Setup(),5,RunEndReason.PlayerDied).Score == 0,"失败奖励或最低分错误");
            var rating = Make<ThresholdRatingFormula>(); Array.Reverse(rating.Thresholds); Require(rating.Evaluate(10000,result.Stats).Name == "S","评级依赖列表排序");
        }
        private static RunResult Result(string id,int score) => new() { RunId = id, SongId = "song", Difficulty = SongDifficulty.Hard, Score = score, Grade = "A", DropItemId = "item", DropQuantity = 1 };
        private static void Persistence()
        {
            var dir = Path.Combine(Path.GetTempPath(),"SpotlightRunChecks",Guid.NewGuid().ToString("N"));
            try
            {
                var service = new GameSaveService(new JsonFileStore(dir)); Require(service.Status == SaveStatus.Missing,"无档状态错误");
                Require(service.NewGame().Success,"新档失败"); var r = Result("one",5000); Require(service.Settle(r).Success,"结算保存失败");
                Require(service.Settle(r).Success && service.Quantity("item") == 1,"重复发奖");
                Require(service.Settle(Result("two",2000)).Success && service.Quantity("item") == 2 && service.Record("song",SongDifficulty.Hard).Score == 5000,"低分覆盖最高分");
                Require(service.Settle(Result("three",7000)).Success && service.Record("song",SongDifficulty.Hard).Score == 7000,"失败局无法更新最高分");
                service = new GameSaveService(new JsonFileStore(dir)); Require(service.Quantity("item") == 3 && service.Record("song",SongDifficulty.Hard).Score == 7000,"重启数据丢失");
                Require(service.NewGame().Success && service.Quantity("item") == 0 && service.Current.Records.Count == 0,"新游戏未清空");
                Require(service.Settle(Result("zero",0)).Success && service.Record("song",SongDifficulty.Hard).Grade == "A","零分首次记录评级丢失");
                File.WriteAllText(Path.Combine(dir,GameSaveService.FileName),"{}"); service.Inspect(); Require(service.Status == SaveStatus.Corrupt,"空对象存档被接受");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); }
        }
        private static void SaveFailure()
        {
            var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); File.WriteAllText(path,"block");
            try { var service = new GameSaveService(new JsonFileStore(path)); Require(!service.Settle(Result("one",1)).Success && service.Current == null,"保存失败污染内存"); }
            finally { File.Delete(path); }
        }
        private static void Buffs()
        {
            var manager = new BuffManager(); var modifier = new BuffModifier { Stat = BuffStat.MoveSpeed, Flat = 2, Multiplier = 1.5f };
            manager.Add("speed",new[] { modifier },0,60,2); modifier.Flat = 99;
            Require(manager.Current.MoveFlat == FP64.FromInt(2),"Buff 引用了外部可变数据");
            manager.Add("speed",new[] { new BuffModifier { Stat = BuffStat.MoveSpeed, Flat = 2, Multiplier = 1.5f } },0,60,2);
            Require(manager.Current.MoveFlat == FP64.FromInt(4) && manager.Current.MoveMultiplier == FP64.FromFloat(2.25f),"叠层错误");
            manager.Advance(59); Require(manager.Current.MoveFlat == FP64.FromInt(4),"提前到期"); manager.Advance(60); Require(manager.Current.MoveFlat == FP64.Zero,"到期未移除");
            manager.Add("permanent",new[] { modifier },0,0); manager.Advance(6000); Require(manager.Current.MoveFlat == FP64.FromInt(99),"永久效果到期"); manager.Remove("permanent"); Require(manager.Current.MoveMultiplier == FP64.One,"移除未恢复");
        }
        private static void Items()
        {
            var item = Make<ItemDefinition>(); item.Id = "i"; item.UsesPerRun = 2; item.CooldownTicks = 30;
            var effect = Make<BuffItemEffect>(); effect.Modifiers = new[] { new BuffModifier { Stat = BuffStat.MoveSpeed, Multiplier = 2 } }; item.Effect = effect;
            var buffs = new BuffManager(); int tick = 0; var runtime = new ItemRuntime(new[] { item },buffs,()=>tick);
            runtime.RequestUseItem("i"); runtime.RequestUseItem("i"); Require(runtime.Snapshot()[0].Count == 1 && buffs.Current.MoveMultiplier == FP64.FromInt(2),"使用去重失败");
            tick = 30; runtime.RequestUseItem("i"); Require(runtime.Snapshot()[0].Count == 0,"冷却后未使用");
            Require(new ItemRuntime(new[] { item },new BuffManager(),()=>0).Snapshot()[0].Count == 2,"重试次数未恢复");
            item.Passive = true; buffs = new BuffManager(); Require(new ItemRuntime(new[] { item },buffs,()=>0).Snapshot().Length == 0 && buffs.Current.MoveMultiplier == FP64.FromInt(2),"被动接入失败");
        }
        private static void Drops()
        {
            var table = Make<ItemDropTable>(); Require(table.Validate() != null,"空表未拒绝"); var item = Make<ItemDefinition>(); item.Id = "i";
            table.Entries = new[] { new ItemDropEntry { Item = item, Weight = 1 } }; var random = new System.Random(12);
            for (int i = 0; i < 50; i++) Require(table.Pick(random).Item == item,"必掉一个失败"); table.Entries[0].Weight = 0; Require(table.Validate() != null,"零权重未拒绝");
        }
        private static BattleSimulation Battle(int duration = 600)
        {
            var definition = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlayerDefinition>("Assets/Player/MaidGhost.asset")); owned.Add(definition);
            definition.Motor.MoveSpeed = 2; definition.Motor.GroundHeight = 0;
            var weapon = Make<PlayerWeaponDefinition>(); weapon.Projectile = Make<ProjectileDefinition>(); weapon.FireRate = 5; weapon.Emission.Speed = 1; definition.Weapon = weapon;
            var config = Make<BattleConfig>(); config.Sequence = Make<DanmakuSequence>(); config.Sequence.Clips.Clear(); config.Sequence.Events.Clear(); config.Sequence.DurationTicks = duration; config.Sequence.ClearAtEnd = false; config.BossHealth = 1;
            return new BattleSimulation(config,definition,Vector2.zero);
        }
        private static void EndConditions()
        {
            var battle = Battle(3); battle.World.SpawnFriendly(new ProjectileSettings { Damage = 1 },DanmakuSimulation.ToFP(new Vector2(0,3)),FP64.Zero,FP64.Zero); battle.Step(default);
            Require(!battle.Boss.Alive && !battle.Finished,"Boss 死亡提前结束"); battle.Step(default); battle.Step(default); Require(battle.Finished && battle.Player.Alive,"时间轴未结束");
            battle = Battle(); battle.PlayerSystem.ReceiveDamage(FP64.FromInt(999)); Require(battle.Finished && !battle.Player.Alive,"玩家死亡未结束");
        }
        private static void RuntimeStats()
        {
            var basic = Battle(); var enhanced = Battle();
            enhanced.Buffs.Add("stats",new[] { new BuffModifier { Stat = BuffStat.MoveSpeed, Multiplier = 2 }, new BuffModifier { Stat = BuffStat.FireRate, Multiplier = 2 }, new BuffModifier { Stat = BuffStat.ProjectileSpeed, Multiplier = 2 }, new BuffModifier { Stat = BuffStat.Damage, Multiplier = 3 } },0,0);
            var fire = new BattleInput(new InputSnapshot(short.MaxValue,0,fireHeld:true),DanmakuSimulation.ToFP(new Vector2(0,3)),true);
            var input = new PlayerTickInput(short.MaxValue,fireHeld:true);
            basic.Step(fire,input); enhanced.Step(fire,input);
            Require(enhanced.Player.Pose.Position.X == basic.Player.Pose.Position.X * FP64.FromInt(2),"移速未生效");
            Require(enhanced.World.Bullets[0].DamageMultiplier == FP64.FromInt(3) && enhanced.World.Bullets[0].DamageFlat == FP64.Zero,"子弹伤害快照缺失");
            for (int i = 1; i < 60; i++) { basic.Step(default,new PlayerTickInput(0,fireHeld:true)); enhanced.Step(default,new PlayerTickInput(0,fireHeld:true)); }
            Require(basic.Weapon.TotalRounds == 5 && enhanced.Weapon.TotalRounds == 10,"射速未生效");
            Require(enhanced.PlayerSystem.Definition.Motor.MoveSpeed == 2,"基础资源被污染");
        }
        private static void OrbLifecycle()
        {
            var sequence = Make<DanmakuSequence>(); var child = Make<ProjectileDefinition>();
            var settings = new ProjectileSettings { Interaction = ProjectileInteraction.EnergyOrb, SplitChild = child, DurationTicks = 1 };
            var world = new ProjectileWorld(sequence); int misses = 0; world.EnergyOrbMissed += _ => misses++;
            world.Spawn(0,0,FPVector2.Zero,FP64.Zero,FP64.Zero,ProjectileWorld.CompileProjectile(settings,true)); world.BeginTick(); world.Advance(); world.CommitLifecycle(); Require(misses == 1,"到期遗漏缺失");
            settings.DurationTicks = 60; world.Spawn(0,0,FPVector2.Zero,FP64.Zero,FP64.Zero,ProjectileWorld.CompileProjectile(settings,true)); world.ClearEnemy(); Require(misses == 1,"清理误记 MISS");
            world.Spawn(0,0,FPVector2.Zero,FP64.Zero,FP64.Zero,ProjectileWorld.CompileProjectile(settings,true)); world.SplitMothers(0); Require(misses == 2,"分裂遗漏缺失");
            world.Clear(); world.BeginTick(); world.Spawn(0,0,FPVector2.Zero,FP64.Zero,FP64.Zero,ProjectileWorld.CompileProjectile(settings,true)); world.DamageEnergyOrb(world.Bullets[0],FP64.One); world.CommitLifecycle(); Require(misses == 2,"击破误记 MISS");
        }
        private static void Assets()
        {
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            Require(config && config.Songs && config.Songs.Songs.Length > 0 && config.Drops && config.Drops.Validate() == null && config.ScoreFormulas.Length == 4,"系统配置缺失");
            var ui = config.UIPrefab; Require(ui.Songs && ui.Loadout && ui.Result && ui.Songs.List.Rows.Length == 7,"页面引用缺失");
            Require(!ui.Pause.transform.Find("Buttons/Save"),"保存按钮仍存在");
            Require(config.Songs.Songs[0].Find(SongDifficulty.Hard).Enabled == false,"未配置难度错误开放");
            var saves = new GameSaveService(new JsonFileStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))));
            var song = config.Songs.Songs[0]; var setup = new BattleStartData(song,song.Find(SongDifficulty.Normal),config.Weapons[0],null);
            Require(setup.Validate(config,saves) == null,"有效开局被拒绝");
            Require(new BattleStartData(song,song.Find(SongDifficulty.Hard),config.Weapons[0],null).Validate(config,saves) != null,"关闭难度未阻止启动");
            Require(new BattleStartData(song,song.Find(SongDifficulty.Normal),config.Weapons[0],new[] { config.Items[0] }).Validate(config,saves) != null,"未获得道具允许装备");
        }
    }
}
#endif
