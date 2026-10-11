#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TapTap.Systems.Editor
{
    public static class LoadoutTextImporter
    {
        public const string TablePath = "Assets/Configs/Spotlight/LoadoutTexts.csv";
        [MenuItem("Tools/Spotlight/从CSV导入装备文案")]
        public static void Import()
        {
            var config = AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            if (!config) throw new InvalidOperationException("缺少 SystemConfig。");
            Apply(config, File.ReadAllText(TablePath, Encoding.UTF8));
            AssetDatabase.SaveAssets();
            Debug.Log("[Spotlight] 装备文案表已导入。");
        }
        public static void Apply(SystemConfig config, string csv)
        {
            var rows = LoadingTipImporter.Parse(csv.TrimStart('\uFEFF'));
            if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "Kind", "Id", "DisplayName", "Description" }))
                throw new FormatException("装备文案 CSV 表头必须为 Kind,Id,DisplayName,Description。");
            var changes = new List<Action>(); var keys = new HashSet<string>();
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i]; if (row.Length == 1 && string.IsNullOrWhiteSpace(row[0])) continue;
                if (row.Length != 4 || string.IsNullOrWhiteSpace(row[2]) || !keys.Add(row[0] + ":" + row[1]))
                    throw new FormatException("装备文案 CSV 第 " + (i+1) + " 行无效或重复。");
                if (row[0] == "Item")
                {
                    var item = Array.Find(config.Items, value => value && value.Id == row[1]);
                    if (!item) throw new FormatException("未知道具 Id：" + row[1]);
                    changes.Add(() => { Undo.RecordObject(item,"Import Loadout Texts"); item.DisplayName = row[2]; item.Description = row[3]; EditorUtility.SetDirty(item); });
                }
                else if (row[0] == "Weapon")
                {
                    var weapon = Array.Find(config.Weapons, value => value && value.Mode.ToString() == row[1]);
                    if (!weapon) throw new FormatException("未知弹幕 Id：" + row[1]);
                    changes.Add(() => { Undo.RecordObject(weapon,"Import Loadout Texts"); weapon.DisplayName = row[2]; weapon.Description = row[3]; EditorUtility.SetDirty(weapon); });
                }
                else throw new FormatException("未知文案 Kind：" + row[0]);
            }
            // Validate the entire table before changing any asset.
            foreach (var change in changes) change();
        }
    }
}
#endif
