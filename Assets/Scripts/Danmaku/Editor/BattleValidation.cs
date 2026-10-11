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
        [MenuItem("工具/弹幕/第一阶段/运行战斗验证")]
        public static void Run()
        {
            var owned = new List<UnityEngine.Object>();
            T Make<T>() where T : ScriptableObject { var result = ScriptableObject.CreateInstance<T>(); owned.Add(result); return result; }
            try
            {
                var sequence = Make<DanmakuSequence>(); sequence.FieldSize = new Vector2(100, 100); sequence.DurationTicks = 600;
                var config = Make<BattleConfig>(); config.Sequence = sequence; var player = Make<PlayerDefinition>(); player.Actions = Make<PlayerActionLibrary>(); player.Motor.Gravity = 0; player.Motor.GroundHeight = 0;
                var weapon = Make<PlayerWeaponDefinition>(); weapon.Projectile = Make<ProjectileDefinition>(); player.Weapon = weapon;
                config.BossPosition = new Vector2(0, 3);
                weapon.Emission.Speed = 30;
                var fire = new BattleInput(new InputSnapshot(0, 0, fireHeld: true), DanmakuSimulation.ToFP(config.BossPosition), true);
                foreach (float rate in new[] { 5f, 2.5f, 8f })
                {
                    weapon.FireRate = rate; var battle = new BattleSimulation(config, player, Vector2.zero);
                    for (int i = 0; i < 600; i++) battle.Step(fire);
                    Assert(battle.Weapon.TotalRounds == (int)(rate * 10), "射击调度频率错误：" + rate);
                    Assert(battle.Events.Count == 0 && battle.Player.Health == battle.Player.MaxHealth, "友方子弹穿过 Boss 时错误造成伤害。");
                    Assert(battle.Tick == 600 && battle.Finished, "战斗未推进至时间轴结尾。");
                }
                weapon.FireRate = 5; weapon.Mode = PlayerWeaponMode.Spread; weapon.Emission.Count = 5;
                var spread = new BattleSimulation(config, player, Vector2.zero); spread.Step(fire);
                Assert(spread.World.TotalSpawned == 5, "散射数量错误。");
                weapon.Mode = PlayerWeaponMode.Normal;
                var fast = new BattleSimulation(config, player, Vector2.zero);
                fast.World.SpawnFriendly(weapon.Projectile.Settings, new FPVector2(FP64.Zero, FP64.FromInt(-3)), FPMath.PI_OVER_2, FP64.FromInt(600));
                fast.Step(default);
                Assert(fast.World.Bullets.Count == 1 && fast.Events.Count == 0 && fast.World.Contacts.Count == 0, "高速玩家子弹被 Boss 阻挡。");
                player.HurtInvulnerableTicks = 0;
                sequence.Clips.Add(new DanmakuClip { DurationTicks = 1, EmitterPosition = Vector2.zero, Keyframes = new() { new DanmakuKeyframe() } });
                sequence.Clips[0].Emission.Speed = 0;
                // Legacy damage and piercing settings must have no effect after import.
                JsonUtility.FromJsonOverwrite("{\"Damage\":99,\"DamageSource\":1,\"DamageConfigId\":123,\"HitCount\":9}", sequence.Clips[0].ProjectileOverride);
                var enemy = new BattleSimulation(config, player, Vector2.zero); enemy.Step(default);
                Assert(enemy.Player.Health == enemy.Player.MaxHealth - FP64.One && enemy.World.Bullets.Count == 0 && enemy.Events[0].Damage == FP64.One, "固定1伤害或一次命中回收失败。");
                sequence.Clips[0].Emission.Count = 2;
                var simultaneous = new BattleSimulation(config, player, Vector2.zero); simultaneous.Step(default);
                Assert(simultaneous.Player.Health == simultaneous.Player.MaxHealth - FP64.FromInt(2), "零无敌时间仍屏蔽同 Tick 伤害。");
                player.HurtInvulnerableTicks = 30;
                var immune = new BattleSimulation(config, player, Vector2.zero); immune.Step(default);
                Assert(immune.Player.Health == immune.Player.MaxHealth - FP64.One && immune.World.Bullets.Count == 0, "无敌窗口或免伤耗弹策略失败。");
                config.ConsumeBulletWhenInvulnerable = false;
                immune = new BattleSimulation(config, player, Vector2.zero); immune.Step(default); immune.Step(default);
                Assert(immune.Player.Health == immune.Player.MaxHealth - FP64.One && immune.World.Bullets.Count == 1, "关闭免伤耗弹时错误回收或重复受伤。");
                config.ConsumeBulletWhenInvulnerable = true; player.HurtInvulnerableTicks = 0; player.MaxHealth = 1;
                var death = new BattleSimulation(config, player, Vector2.zero);
                death.Step(default); int deaths = 0;
                foreach (var e in death.Events) if (e.Type == BattleEventType.ActorDied) deaths++;
                Assert(deaths == 1 && death.Finished && death.EndReason == TapTap.Systems.RunEndReason.PlayerDied, "玩家倒下事件未去重或没有结束歌曲。");
                sequence.Clips.Clear();
                var clear = new BattleSimulation(config, player, Vector2.zero);
                clear.World.SpawnFriendly(weapon.Projectile.Settings, FPVector2.Zero, FP64.Zero, FP64.Zero);
                clear.World.ClearEnemy(); Assert(clear.World.Bullets.Count == 1, "敌方清弹错误删除友方弹幕。");
                var music = AudioClip.Create("Song end validation", 101, 1, 6000, false); owned.Add(music);
                sequence.Music = music; sequence.DurationTicks = 600;
                var shortSong = new BattleSimulation(config, player, Vector2.zero); shortSong.Step(default);
                Assert(!shortSong.Finished, "不足整帧的歌曲被提前结束。"); shortSong.Step(default);
                Assert(shortSong.Finished && shortSong.EndReason == TapTap.Systems.RunEndReason.Completed, "短歌曲结束未结算。");
                sequence.DurationTicks = 1;
                var longSong = new BattleSimulation(config, player, Vector2.zero); longSong.Step(default);
                Assert(longSong.BossTimeline.Finished && !longSong.Finished, "短时间轴导致长歌曲提前结算。"); longSong.Step(default); Assert(longSong.Finished, "歌曲结束未结算。");
                sequence.MusicStartSeconds = 1;
                Assert(new BattleSimulation(config, player, Vector2.zero).EndTick == 62, "歌曲开始偏移未计入结算时间。");
                sequence.MusicStartSeconds = -1f / 60;
                Assert(new BattleSimulation(config, player, Vector2.zero).EndTick == 1, "从歌曲中段播放的结算时间错误。");
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/BattleValidation.txt", "PASS: 5/2.5/8 Hz; fixed one damage; first-hit consumption; ignored legacy damage/piercing fields; spread; continuous world; player shots pass Boss; invulnerability; player death deduplication; faction clear; song end before/after timeline; fractional final tick; music start offsets.\n");
                Debug.Log("Battle validation passed.");
            }
            finally { foreach (var asset in owned) UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
