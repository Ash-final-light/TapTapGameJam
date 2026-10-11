#if UNITY_EDITOR
using System;
using System.IO;
using Framework.FixedPoint;
using Inputs;
using TapTap.Danmaku;
using UnityEditor;
using UnityEngine;

namespace TapTap.Systems.Editor
{
    public static class SpotlightLoadoutValidation
    {
        private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
        public static void InstallAndRun() { SpotlightLoadoutSetup.Install(); Run(); SpotlightRunValidation.Run(); }
        [MenuItem("Tools/Spotlight/验证准备界面与普通道具")]
        public static void Run()
        {
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            var actor = new ActorState(1,FP64.FromInt(5),default);
            var heal = Array.Find(config.Items, item => item.Id == "item-1");
            int tick = 0; var buffs = new BuffManager();
            var items = new ItemRuntime(new[] { heal },buffs,() => tick,actor,_ => 2);
            Check(items.Snapshot()[0].Count == 2,"携带数量未限制到库存");
            items.RequestUseItem(heal.Id); Check(items.Snapshot()[0].Count == 2,"满血错误消耗回血次数");
            actor.Health = FP64.One; items.RequestUseItem(heal.Id);
            Check(actor.Health == FP64.FromInt(3) && items.Snapshot()[0].Count == 1,"未恢复2点生命值");
            items.RequestUseItem(heal.Id); Check(actor.Health == FP64.FromInt(3),"冷却未限制使用");
            tick = heal.CooldownTicks; actor.Health = FP64.FromInt(4); items.RequestUseItem(heal.Id);
            Check(actor.Health == actor.MaxHealth && items.Snapshot()[0].Count == 0,"回血越过上限或次数错误");
            actor.Health = FP64.Zero;
            Check(!heal.Effect.Apply(new ItemEffectContext(buffs,tick,heal.Id,actor)),"回血错误复活角色");
            var burst = Array.Find(config.Items, item => item.Id == "item-2");
            var limited = ScriptableObject.CreateInstance<ItemDefinition>(); limited.Id = "limited"; limited.Effect = burst.Effect;
            limited.MaxHeldPerRun = 2; limited.UsesPerRun = 1;
            try { Check(new ItemRuntime(new[] { limited },buffs,()=>0,actor,_=>99).Snapshot()[0].Count == 1,"携带上限或使用次数上限无效"); }
            finally { UnityEngine.Object.DestroyImmediate(limited); }
            foreach (var weapon in config.Weapons)
            {
                var basic = new PlayerWeaponController(weapon); var enhanced = new PlayerWeaponController(weapon);
                var sequence = ScriptableObject.CreateInstance<DanmakuSequence>();
                try
                {
                    sequence.Clips.Clear(); sequence.Events.Clear();
                    var ordinaryWorld = new ProjectileWorld(sequence); var burstWorld = new ProjectileWorld(sequence);
                    var effectBuffs = new BuffManager(); burst.Effect.Apply(new ItemEffectContext(effectBuffs,10,burst.Id));
                    enhanced.Modifiers = effectBuffs.Current;
                    var input = new BattleInput(new InputSnapshot(0,0,fireHeld:true),default,false);
                    basic.Step(input,FPVector2.Zero,ordinaryWorld); enhanced.Step(input,FPVector2.Zero,burstWorld);
                    Check(burstWorld.Bullets.Count == ordinaryWorld.Bullets.Count * 3,"爆发没有增加每轮弹幕数量");
                    for (int i=1;i<120;i++) { basic.Step(input,FPVector2.Zero,ordinaryWorld); enhanced.Step(input,FPVector2.Zero,burstWorld); }
                    Check(enhanced.TotalRounds == basic.TotalRounds * 2,"爆发射速没有提升");
                    effectBuffs.Advance(309); Check(effectBuffs.Current.FireMultiplier == FP64.FromInt(2),"爆发提前结束");
                    effectBuffs.Advance(310); Check(effectBuffs.Current.FireMultiplier == FP64.One && effectBuffs.Current.CountMultiplier == FP64.One,"5秒后效果未恢复");
                    enhanced.Modifiers = effectBuffs.Current; burstWorld.Clear();
                    enhanced.Step(default,FPVector2.Zero,burstWorld); enhanced.Step(input,FPVector2.Zero,burstWorld);
                    Check(burstWorld.Bullets.Count == ordinaryWorld.Bullets.Count / basic.TotalRounds,"到期后每轮弹幕数量未恢复");
                }
                finally { UnityEngine.Object.DestroyImmediate(sequence); }
            }
            string oldName = heal.DisplayName;
            try { LoadoutTextImporter.Apply(config,"Kind,Id,DisplayName,Description\nItem,item-1,不应保存,内容\nItem,missing,错误,内容"); throw new Exception("无效表格未拒绝"); }
            catch (FormatException) { Check(heal.DisplayName == oldName,"无效表格造成部分写入"); }
            var view = config.UIPrefab.Loadout;
            Check(view.EquippedWeapon && view.ItemsRoot.GetComponentInParent<UnityEngine.UI.ScrollRect>(true),"弹幕装备槽或滚动列表未接入");
            Check(view.ItemsRoot.GetComponent<UnityEngine.UI.ContentSizeFitter>(),"列表无法随道具增加扩展");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/SpotlightLoadoutValidation.txt","PASS 回血2点、上限、死亡、满血不消耗、冷却\nPASS 库存/持有上限/使用上限\nPASS 普通与散射弹幕射速翻倍、数量三倍、300Tick到期恢复\nPASS CSV整表校验与无效表格无部分写入\nPASS 已装备弹幕槽、ScrollRect与自动内容高度\n");
            Debug.Log("[Spotlight] 准备界面与道具验证通过。");
        }
    }
}
#endif
