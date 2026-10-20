#if UNITY_EDITOR
using System;
using System.Linq;
using TapTap.Systems.UI;
using UnityEditor;
using UnityEngine;

namespace TapTap.Systems.Editor
{
    public static class SpotlightItemSetup
    {
        private const string Folder = "Assets/Configs/Spotlight/Run/";
        private static bool Retired(ItemDefinition item) => item &&
            (item.Id == "item-3" || item.Id == "item-4" || item.Id == "item-5" || item.Id == "item-6");

        // Shared by the older installation tools, so rebuilding cannot restore retired items.
        public static void ConfigureDefaults(SystemConfig config)
        {
            var heal = Asset<HealItemEffect>("HealEffect");
            var burst = AssetDatabase.LoadAssetAtPath<BuffItemEffect>(Folder + "BurstEffect.asset");
            if (!burst)
            {
                burst = Asset<BuffItemEffect>("BurstEffect"); burst.BuffId = "item-burst";
                burst.DurationTicks = 300; burst.MaxStacks = 1;
                burst.Modifiers = new[] { new BuffModifier { Stat = BuffStat.FireRate, Multiplier = 2 },
                    new BuffModifier { Stat = BuffStat.ProjectileCount, Multiplier = 3 } };
                EditorUtility.SetDirty(burst);
            }
            var items = (config.Items ?? Array.Empty<ItemDefinition>()).Where(i => i && !Retired(i)).ToList();
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Spotlight/Placeholders/Item.png");
            if (!icon) icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/DanmakuExamples/BulletCircle.png");
            foreach (var id in new[] { "item-1", "item-2" })
            {
                var item = items.Find(i => i.Id == id);
                if (!item)
                {
                    item = Asset<ItemDefinition>(id == "item-1" ? "Item1" : "Item2"); item.Id = id;
                    item.DisplayName = id == "item-1" ? "血量回复道具" : "短时弹幕爆发道具";
                    item.Description = id == "item-1" ? "恢复2点生命值；满血时不消耗次数。" : "5秒内射速×2，每轮弹幕数量×3。";
                    item.Icon = icon; item.Passive = false; item.MaxHeldPerRun = item.UsesPerRun = 3;
                    item.CooldownTicks = id == "item-1" ? 60 : 300;
                    item.Effect = id == "item-1" ? (ItemEffect)heal : burst;
                    items.Add(item); EditorUtility.SetDirty(item);
                }
                if (item.IconColor == Color.white)
                {
                    item.IconColor = id == "item-1" ? new Color(.08f,.6f,.35f) : new Color(.95f,.55f,.08f);
                    EditorUtility.SetDirty(item);
                }
            }
            config.Items = items.ToArray();
            if (!config.Drops) config.Drops = Asset<ItemDropTable>("Drops");
            config.Drops.Entries = (config.Drops.Entries ?? Array.Empty<ItemDropEntry>())
                .Where(e => e != null && e.Item && !Retired(e.Item) && items.Contains(e.Item)).ToArray();
            if (config.Drops.Entries.Length == 0)
                config.Drops.Entries = items.Select(item => new ItemDropEntry { Item = item }).ToArray();
            EditorUtility.SetDirty(config.Drops); EditorUtility.SetDirty(config);
        }

        [MenuItem("Tools/Spotlight/更新道具目录与使用反馈")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 模式后更新。");
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            if (!config || !config.UIPrefab) throw new InvalidOperationException("缺少 SystemConfig 或现有 UI。");
            ConfigureDefaults(config);
            string uiPath = AssetDatabase.GetAssetPath(config.UIPrefab);
            var root = PrefabUtility.LoadPrefabContents(uiPath);
            try { root.GetComponent<SystemUIController>().Hud.EnsureItemFeedback(); PrefabUtility.SaveAsPrefabAsset(root, uiPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            const string hudPath = "Assets/UI/Spotlight/Prefabs/HUD.prefab";
            var hud = PrefabUtility.LoadPrefabContents(hudPath);
            try { hud.GetComponent<HudView>().EnsureItemFeedback(); PrefabUtility.SaveAsPrefabAsset(hud, hudPath); }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
            for (int i = 3; i <= 6; i++) AssetDatabase.DeleteAsset(Folder + "Item" + i + ".asset");
            AssetDatabase.DeleteAsset(Folder + "DebugItemEffect.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[Spotlight] 已移除四个占位道具，并更新道具冷却与状态反馈。");
        }

        private static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = Folder + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
    }
}
#endif
