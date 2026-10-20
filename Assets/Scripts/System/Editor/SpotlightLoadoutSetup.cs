#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using TapTap.Systems.UI;

namespace TapTap.Systems.Editor
{
    public static class SpotlightLoadoutSetup
    {
        private const string Folder = "Assets/Configs/Spotlight/Run/";
        [MenuItem("Tools/Spotlight/更新开始前准备界面")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 模式后更新。");
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            if (!config || !config.UIPrefab || !config.UIPrefab.Loadout) throw new InvalidOperationException("缺少现有准备界面。");
            SpotlightItemSetup.ConfigureDefaults(config);
            var healItem = Array.Find(config.Items, item => item && item.Id == "item-1");
            var burstItem = Array.Find(config.Items, item => item && item.Id == "item-2");
            if (!healItem || !burstItem) throw new InvalidOperationException("缺少首批道具 item-1 / item-2。");
            var heal = Asset<HealItemEffect>("HealEffect"); heal.Health = 2; EditorUtility.SetDirty(heal);
            var burst = Asset<BuffItemEffect>("BurstEffect"); burst.BuffId = "item-burst";
            burst.DurationTicks = 5 * TapTap.Danmaku.DanmakuSequence.TickRate; burst.MaxStacks = 1;
            burst.Modifiers = new[] { new BuffModifier { Stat = BuffStat.FireRate, Multiplier = 2 }, new BuffModifier { Stat = BuffStat.ProjectileCount, Multiplier = 3 } };
            EditorUtility.SetDirty(burst);
            healItem.Passive = burstItem.Passive = false; healItem.Effect = heal; burstItem.Effect = burst;
            healItem.MaxHeldPerRun = burstItem.MaxHeldPerRun = 3;
            healItem.UsesPerRun = burstItem.UsesPerRun = 3; healItem.CooldownTicks = 60; burstItem.CooldownTicks = 300;
            EditorUtility.SetDirty(healItem); EditorUtility.SetDirty(burstItem);
            config.ItemSlotCount = 3; EditorUtility.SetDirty(config);
            LoadoutTextImporter.Apply(config, File.ReadAllText(LoadoutTextImporter.TablePath));
            string path = AssetDatabase.GetAssetPath(config.UIPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view = root.GetComponent<SystemUIController>().Loadout; view.EnsureLayout();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            const string loadoutPath = "Assets/UI/Spotlight/Prefabs/Loadout.prefab";
            var loadout = PrefabUtility.LoadPrefabContents(loadoutPath);
            try { loadout.GetComponent<LoadoutView>().EnsureLayout(); PrefabUtility.SaveAsPrefabAsset(loadout,loadoutPath); }
            finally { PrefabUtility.UnloadPrefabContents(loadout); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Spotlight] 准备界面、装备文案、回血与5秒弹幕爆发已配置。");
        }
        private static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = Folder + name + ".asset"; var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset,path); return asset;
        }
    }
}
#endif
