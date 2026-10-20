#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using TapTap.Danmaku;
using TapTap.Player;
using TapTap.Systems.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Systems.Editor
{
    public static class ItemUsageValidation
    {
        private static readonly List<string> results = new();
        private static readonly List<Object> temporary = new();
        private static SystemConfig config;
        private static PlayerDefinition hero;
        private static ItemDefinition heal, burst;
        private static int assertions;
        public const string ReportPath = "Logs/ItemUsageValidation.txt";

        [MenuItem("Tools/Spotlight/验证道具使用流程")]
        public static void Run()
        {
            results.Clear(); temporary.Clear(); assertions = 0;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 模式后验证。");
                config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
                hero = AssetDatabase.LoadAssetAtPath<PlayerDefinition>("Assets/Player/Hero.asset");
                heal = config.Items.Single(i => i.Id == "item-1"); burst = config.Items.Single(i => i.Id == "item-2");
                Catalog(); Healing(); BuffsAndRetry(); InputIdentity(); DuringActions(); CollisionOrder(); Feedback();
                Directory.CreateDirectory("Logs");
                File.WriteAllText(ReportPath, "PASS\nAssertions: " + assertions + "\n" + string.Join("\n", results));
                Debug.Log("[Spotlight] 道具验证通过：" + assertions + " 项断言。报告：" + ReportPath);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, "FAIL\n" + string.Join("\n", results) + "\n" + error);
                throw;
            }
            finally { foreach (var obj in temporary) if (obj) Object.DestroyImmediate(obj); }
        }
        private static T New<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); temporary.Add(value); return value; }
        private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        private static void Pass(string message) => results.Add("PASS " + message);
        private static BattleSimulation Battle(PlayerWeaponDefinition weapon = null, bool collide = false)
        {
            var sequence = New<DanmakuSequence>(); sequence.DurationTicks = 1000; sequence.FieldSize = new Vector2(30,30);
            if (collide) sequence.Clips.Add(new DanmakuClip { Pattern = BuiltinPattern.Single, DurationTicks = 1,
                EmitterPosition = new Vector2(0,hero.Motor.GroundHeight), OverrideEmission = true,
                Emission = new EmissionSettings { Count = 1, Speed = 0 }, OverrideProjectile = true,
                ProjectileOverride = new ProjectileSettings { HitRadius = .2f },
                Keyframes = new List<DanmakuKeyframe> { new() { Type = DanmakuKeyframeType.Emit, LocalTick = 0 } } });
            var battle = New<BattleConfig>(); battle.Sequence = sequence;
            var simulation = new BattleSimulation(battle, hero, new Vector2(0,hero.Motor.GroundHeight), weapon);
            simulation.ConfigureItems(new[] { heal, burst }, _ => 3); return simulation;
        }
        private static void Use(BattleSimulation battle, string id, PlayerButtonEdges extra = 0)
            => battle.Step(default, new PlayerTickInput(0, edges: PlayerButtonEdges.UseItem | extra, itemId: id));
        private static ItemViewData Item(ItemRuntime runtime, string id) => runtime.Snapshot().Single(i => i.Id == id);
        private static void Catalog()
        {
            Check(config.Items.Length == 2 && config.Items.All(i => i && i.Effect && i.Effect.Validate() == null), "Catalog must contain two real effects");
            Check(heal.IconColor != burst.IconColor && heal.IconColor != Color.white && burst.IconColor != Color.white,"Item icons need distinct visible colors");
            Check(config.Drops.Validate() == null && config.Drops.Entries.All(e => config.Items.Contains(e.Item)), "Drop table contains retired items");
            for (int i=3;i<=6;i++) Check(!AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Configs/Spotlight/Run/Item"+i+".asset"), "Retired item asset remains");
            var random = new System.Random(11);
            for (int i=0;i<100;i++) Check(config.Items.Contains(config.Drops.Pick(random).Item), "Retired item dropped");
            Pass("Only heal and burst remain in catalog, assets and 100 sampled drops");
        }
        private static void Healing()
        {
            var battle = Battle(); var runtime = battle.Items;
            Check(!runtime.TryUseItem(heal.Id).Success && Item(runtime,heal.Id).Count == 3 && Item(runtime,heal.Id).CooldownRemainingTicks == 0, "Full health charged uses/cooldown");
            battle.Player.Health = battle.Player.MaxHealth - FP64.One;
            Check(runtime.TryUseItem(heal.Id).Success && battle.Player.Health == battle.Player.MaxHealth && Item(runtime,heal.Id).Count == 2, "Healing must clamp to max");
            Check(!runtime.TryUseItem(heal.Id).Success && Item(runtime,heal.Id).Count == 2, "Cooldown charged a use");
            for (int i=0;i<60;i++) battle.Step(default, default);
            Check(Item(runtime,heal.Id).CooldownRemainingTicks == 0, "Cooldown did not expire at 60 ticks");
            battle.Player.Health = FP64.Zero;
            Check(!runtime.TryUseItem(heal.Id).Success && !runtime.TryUseItem(burst.Id).Success, "Dead actors used an item");
            Check(Item(runtime,heal.Id).Count == 2 && Item(runtime,burst.Id).Count == 3, "Death consumed an item");
            Check(!runtime.TryUseItem(null).Success && !runtime.TryUseItem("unknown").Success, "Invalid IDs accepted");
            var invalid = New<BuffItemEffect>(); var item = New<ItemDefinition>(); item.Id="invalid"; item.Effect=invalid;
            var invalidRuntime = new ItemRuntime(new[]{item}, new BuffManager(), ()=>0);
            Check(!invalidRuntime.TryUseItem(item.Id).Success && Item(invalidRuntime,item.Id).Count == 3, "Empty buff consumed a use");
            invalid.Modifiers = new[] {new BuffModifier {Multiplier = float.NaN}};
            Check(!invalidRuntime.TryUseItem(item.Id).Success, "Invalid buff threw or consumed");
            Pass("Full health, clamping, cooldown, death, missing IDs and invalid effects never consume incorrectly");
        }
        private static void BuffsAndRetry()
        {
            foreach (var weapon in config.Weapons)
            {
                var battle = Battle(weapon); int baseCount = weapon.Mode == PlayerWeaponMode.Spread ? weapon.Emission.Count : 1;
                battle.Step(default, new PlayerTickInput(0, fireHeld:true, edges:PlayerButtonEdges.UseItem, itemId:burst.Id));
                Check(battle.World.Bullets.Count(p=>p.Faction==ProjectileFaction.Player) == baseCount*3, "Burst did not affect same-tick shot count");
                Check(battle.Weapon.Modifiers.FireMultiplier == FP64.FromInt(2), "Burst rate multiplier missing");
                int remaining = Item(battle.Items,burst.Id).CooldownRemainingTicks;
                Check(remaining == 299 && Item(battle.Items,burst.Id).EffectRemainingTicks == 299, "Burst starts at wrong tick");
                for (int i=0;i<20;i++) Check(Item(battle.Items,burst.Id).CooldownRemainingTicks == remaining, "Wall-clock/UI refresh advanced a paused timer");
                while(battle.Tick < 300) battle.Step(default,default);
                Check(Item(battle.Items,burst.Id).CooldownRemainingTicks == 0 && Item(battle.Items,burst.Id).EffectRemainingTicks == 0, "Burst HUD expiry is wrong");
                battle.Step(default,default);
                Check(battle.Weapon.Modifiers.FireMultiplier == FP64.One && battle.Weapon.Modifiers.CountMultiplier == FP64.One, "Buff failed to restore baseline");
                Check(battle.Items.TryUseItem(burst.Id).Success && Item(battle.Items,burst.Id).Count==1, "Burst reuse at cooldown boundary failed");
                while(Item(battle.Items,burst.Id).CooldownRemainingTicks > 0) battle.Step(default,default);
                Check(battle.Items.TryUseItem(burst.Id).Success && Item(battle.Items,burst.Id).Count==0, "Final burst use failed");
                Check(Item(battle.Items,burst.Id).EffectRemainingTicks==300, "Final active effect disappeared at zero uses");
                Check(!battle.Items.TryUseItem(burst.Id).Success && Item(battle.Items,burst.Id).Count==0, "Empty count went negative");
            }
            var store = new JsonFileStore(Path.Combine("Logs","ItemUsageSave-"+Guid.NewGuid().ToString("N")));
            var saves = new GameSaveService(store); Check(saves.NewGame().Success && saves.GrantItem(heal.Id,2).Success, "Test save setup failed");
            var run = Battle(); run.ConfigureItems(new[]{heal},saves.Quantity); run.Player.Health=FP64.One;
            Check(Item(run.Items,heal.Id).Count==2 && run.Items.TryUseItem(heal.Id).Success && saves.Quantity(heal.Id)==2, "Permanent inventory was consumed");
            var retry = Battle(); retry.ConfigureItems(new[]{heal},saves.Quantity);
            Check(Item(retry.Items,heal.Id).Count==2 && Item(retry.Items,heal.Id).CooldownRemainingTicks==0, "Retry did not restore run uses");
            store.Delete(GameSaveService.FileName); Directory.Delete(store.DirectoryPath);
            Pass("Normal/spread burst, duration, paused snapshots, expiry, reuse, last use and inventory/retry semantics");
        }
        private static void InputIdentity()
        {
            using var input = new SystemInputRouter(config.Controls);
            string selected=heal.Id; input.SelectedItemId=()=>selected; input.SetGameplay(true);
            input.Commands.Queue(GameplayCommand.UseItem,input.SelectedItemId());
            Check(input.Commands.Count>0,"Item command did not enqueue");
            selected=burst.Id; var captured=input.CapturePlayerTick();
            Check(captured.ItemId==heal.Id && (captured.Edges&PlayerButtonEdges.UseItem)!=0,"R followed later selection");
            var battle=Battle(); battle.Player.Health=FP64.One; battle.Items.RequestSelectItem(burst.Id); battle.Step(default,captured);
            Check(battle.Player.Health==FP64.FromInt(3) && Item(battle.Items,heal.Id).Count==2 && Item(battle.Items,burst.Id).Count==3,"Wrong item used after switching");
            input.Commands.Queue(GameplayCommand.UseItem,heal.Id); input.SetGameplay(false); input.SetGameplay(true);
            Check((input.CapturePlayerTick().Edges&PlayerButtonEdges.UseItem)==0,"Pause replayed stale R");
            Pass("Queued item identity survives later selection; pause clears queued presses (native key events tested in player)");
        }
        private static void DuringActions()
        {
            foreach (var action in new[]{PlayerButtonEdges.Skill1,PlayerButtonEdges.Skill2,PlayerButtonEdges.Dash,PlayerButtonEdges.JumpPressed})
            {
                var battle=Battle(); battle.Player.Health=FP64.One;
                battle.Step(default,new PlayerTickInput(short.MaxValue,edges:action)); int id=battle.PlayerSystem.Action.Id;
                Use(battle,heal.Id);
                Check(battle.Player.Health==FP64.FromInt(3) && battle.PlayerSystem.Action?.Id==id,"Item delayed or interrupted "+action);
                Check(!battle.PlayerSystem.Inputs.Pending.Any(i=>i.Kind==PlayerIntentKind.UseItem),"Item left in animation buffer");
            }
            var hurt=Battle(); hurt.PlayerSystem.ReceiveDamage(FP64.One); int hurtId=hurt.PlayerSystem.Action.Id; Use(hurt,heal.Id);
            Check(hurt.Player.Health==hurt.Player.MaxHealth && hurt.PlayerSystem.Action?.Id==hurtId,"Item interrupted hurt recovery");
            var fly=Battle(); fly.PlayerMotor.SetFlight(true); fly.Player.Health=FP64.One;
            fly.Step(default,new PlayerTickInput(short.MaxValue,jumpHeld:true,edges:PlayerButtonEdges.UseItem,itemId:heal.Id));
            Check(fly.PlayerMotor.Flying && fly.Player.Health==FP64.FromInt(3),"Flight item use failed");
            var simultaneous=Battle(); simultaneous.Player.Health=FP64.One; Use(simultaneous,heal.Id,PlayerButtonEdges.Skill1);
            Check(simultaneous.Player.Health==FP64.FromInt(3) && simultaneous.PlayerSystem.Action?.Clip.Kind==PlayerActionKind.Skill,"Item stole simultaneous skill input");
            Pass("Both skills, dash, jump, hurt, flight and simultaneous skill/item preserve action continuity");
        }
        private static void CollisionOrder()
        {
            var control=Battle(collide:true); control.Player.Health=FP64.One; control.Step(default,default);
            Check(!control.Player.Alive && control.Events.Any(e=>e.Type==BattleEventType.Damage),"Collision control did not hit");
            var healed=Battle(collide:true); healed.Player.Health=FP64.One; Use(healed,heal.Id);
            Check(healed.Player.Alive && healed.Player.Health==FP64.FromInt(2) && Item(healed.Items,heal.Id).Count==2,"Healing did not precede same-tick lethal collision");
            Pass("Controlled lethal collision kills without item; same-tick healing resolves first and survives");
        }
        private static void Feedback()
        {
            var root=Object.Instantiate(config.UIPrefab.gameObject); temporary.Add(root); var hud=root.GetComponent<SystemUIController>().Hud;
            hud.EnsureItemFeedback(); int children=hud.GetComponentsInChildren<Transform>(true).Length; hud.EnsureItemFeedback();
            Check(hud.ItemStatus && hud.ItemCooldown && hud.ItemCooldownText && children==hud.GetComponentsInChildren<Transform>(true).Length,"HUD setup missing or non-idempotent");
            var battle=Battle(); battle.Player.Health=FP64.One; Use(battle,heal.Id);
            hud.Render(new HudSnapshot{Items=battle.Items.Snapshot(),SelectedItemId=heal.Id});
            Check(hud.ItemCooldown.enabled && hud.ItemCooldownText.text.Length>0 && hud.ItemStatus.text.Contains("冷却"),"Cooldown feedback invisible");
            for(int i=0;i<60;i++) battle.Step(default,default);
            battle.Player.Health=battle.Player.MaxHealth; hud.Render(new HudSnapshot{Items=battle.Items.Snapshot(),SelectedItemId=heal.Id});
            Check(hud.ItemStatus.text.Contains("生命已满"),"Full-health feedback missing");
            hud.ShowItemFeedback(new ItemUseResult(heal.Id,false,"生命已满"));
            Check(hud.ItemFeedback && hud.ItemFeedback.transform.parent.gameObject.activeSelf && hud.ItemFeedback.text=="生命已满","Dedicated item feedback missing");
            root.GetComponent<ImportedButtonTheme>()?.ApplyAll();
            foreach(var slot in hud.Slots)
            {
                var style=slot.GetComponent<ImportedButtonArtwork>();
                Check(!style || (style.Icons.Length==0 && !style.Labels.Contains(slot.CooldownText)),"Button theme overrides item icon or cooldown text");
            }
            var snapshots=battle.Items.Snapshot(); snapshots[0].Count=0; snapshots[0].Usable=false; hud.Render(new HudSnapshot{Items=snapshots,SelectedItemId=burst.Id});
            Check(hud.VisibleItems.Count==2 && hud.Slots[0].ItemId==heal.Id && hud.Slots[1].ItemId==burst.Id && !hud.Slots[0].Button.interactable,"Exhausted slot shifted shortcut indices");
            Check(hud.Slots[0].CooldownText.text=="已用完","Exhausted item must show depleted status instead of cooldown");
            Pass("HUD cooldown, full-health status, stable exhausted slots and idempotent UI installation");
        }
    }
}
#endif
