using System;
using System.Collections.Generic;
using System.IO;
using Framework.FixedPoint;
using Inputs;
using TapTap.Player;
using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    public static class BattleValidation
    {
        private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        [MenuItem("Tools/Danmaku/第一阶段/运行战斗验证")]
        public static void Run()
        {
            var owned = new List<UnityEngine.Object>();
            T Make<T>() where T : ScriptableObject { var result = ScriptableObject.CreateInstance<T>(); owned.Add(result); return result; }
            try
            {
                var sequence = Make<DanmakuSequence>(); sequence.FieldSize = new Vector2(100, 100); sequence.DurationTicks = 600;
                var config = Make<BattleConfig>(); config.Sequence = sequence; var player = Make<PlayerDefinition>(); player.Actions = Make<PlayerActionLibrary>(); player.Motor.Gravity = 0; player.Motor.GroundHeight = 0;
                var weapon = Make<PlayerWeaponDefinition>(); weapon.Projectile = Make<ProjectileDefinition>(); player.Weapon = weapon;
                config.BossPosition = new Vector2(0, 3); config.BossHealth = 10000;
                weapon.Emission.Speed = 30; weapon.Projectile.Settings.Damage = 10;
                var fire = new BattleInput(new InputSnapshot(0, 0, fireHeld: true), DanmakuSimulation.ToFP(config.BossPosition), true);
                foreach (float rate in new[] { 5f, 2.5f, 8f })
                {
                    weapon.FireRate = rate; var battle = new BattleSimulation(config, player, Vector2.zero);
                    for (int i = 0; i < 600; i++) battle.Step(fire);
                    Assert(battle.Weapon.TotalRounds == (int)(rate * 10), "射击调度频率错误：" + rate);
                    Assert(battle.Boss.Health < battle.Boss.MaxHealth && battle.Player.Health == battle.Player.MaxHealth, "阵营伤害或持续推进失败。");
                    Assert(battle.Tick == 600 && battle.Finished, "战斗未推进至时间轴结尾。");
                }
                weapon.FireRate = 5; weapon.Mode = PlayerWeaponMode.Spread; weapon.Emission.Count = 5;
                var spread = new BattleSimulation(config, player, Vector2.zero); spread.Step(fire);
                Assert(spread.World.TotalSpawned == 5, "散射数量错误。");
                weapon.Mode = PlayerWeaponMode.Normal;
                var fast = new BattleSimulation(config, player, Vector2.zero);
                fast.World.SpawnFriendly(weapon.Projectile.Settings, new FPVector2(FP64.Zero, FP64.FromInt(-3)), FPMath.PI_OVER_2, FP64.FromInt(600));
                fast.Step(default);
                Assert(fast.Boss.Health == fast.Boss.MaxHealth - FP64.FromInt(10), "高速弹扫掠遗漏或重复结算。");
                player.HurtInvulnerableTicks = 0;
                sequence.Clips.Add(new DanmakuClip { DurationTicks = 1, EmitterPosition = Vector2.zero, Keyframes = new() { new DanmakuKeyframe() } });
                sequence.Clips[0].Emission.Speed = 0; sequence.Clips[0].ProjectileOverride.Damage = 1.5f;
                var enemy = new BattleSimulation(config, player, Vector2.zero); enemy.Step(default);
                Assert(enemy.Player.Health == enemy.Player.MaxHealth - FP64.FromFloat(1.5f), "敌弹小数伤害失败。");
                sequence.Clips[0].Emission.Count = 2;
                var simultaneous = new BattleSimulation(config, player, Vector2.zero); simultaneous.Step(default);
                Assert(simultaneous.Player.Health == simultaneous.Player.MaxHealth - FP64.FromInt(3), "零无敌时间仍屏蔽同 Tick 伤害。");
                player.HurtInvulnerableTicks = 30;
                var immune = new BattleSimulation(config, player, Vector2.zero); immune.Step(default);
                Assert(immune.Player.Health == immune.Player.MaxHealth - FP64.FromFloat(1.5f) && immune.World.Bullets.Count == 0, "无敌窗口或免伤耗弹策略失败。");
                config.BossHealth = 10; sequence.Clips.Clear();
                var death = new BattleSimulation(config, player, Vector2.zero);
                death.World.SpawnFriendly(weapon.Projectile.Settings, DanmakuSimulation.ToFP(config.BossPosition), FP64.Zero, FP64.Zero);
                death.World.SpawnFriendly(weapon.Projectile.Settings, DanmakuSimulation.ToFP(config.BossPosition), FP64.Zero, FP64.Zero);
                death.Step(default); int deaths = 0;
                foreach (var e in death.Events) if (e.Type == BattleEventType.ActorDied) deaths++;
                Assert(deaths == 1 && !death.Finished, "Boss 死亡事件未去重或错误结束了歌曲。");
                var clear = new BattleSimulation(config, player, Vector2.zero);
                clear.World.SpawnFriendly(weapon.Projectile.Settings, FPVector2.Zero, FP64.Zero, FP64.Zero);
                clear.World.ClearEnemy(); Assert(clear.World.Bullets.Count == 1, "敌方清弹错误删除友方弹幕。");
                weapon.Projectile.Settings.DamageSource = ProjectileDamageSource.ConfigId;
                bool rejected = false;
                try { _ = new BattleSimulation(config, player, Vector2.zero); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "配置伤害缺少解析器时未在开战前阻止。");
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/BattleValidation.txt", "PASS: 5/2.5/8 Hz; faction damage; spread; continuous world; sweep; fractional damage; invulnerability; death deduplication; faction clear; missing damage resolver.\n");
                Debug.Log("Battle validation passed.");
            }
            finally { foreach (var asset in owned) UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
