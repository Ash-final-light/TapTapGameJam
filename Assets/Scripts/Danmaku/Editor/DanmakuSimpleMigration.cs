using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TapTap.Danmaku.Editor
{
    [InitializeOnLoad]
    internal static class DanmakuSimpleMigration
    {
        private static string VersionKey => "TapTap.Danmaku.FixedDamageMigration.2." + Application.dataPath;
        static DanmakuSimpleMigration() => EditorApplication.update += MigrateOnce;
        private static void MigrateOnce()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= MigrateOnce;
            if (EditorPrefs.GetBool(VersionKey)) return;
            try { Migrate(); EditorPrefs.SetBool(VersionKey, true); }
            catch (Exception error) { Debug.LogException(error); }
        }
        [MenuItem("工具/弹幕/清理旧版高级与伤害配置数据")]
        public static void Migrate()
        {
            var paths = new HashSet<string>();
            foreach (string type in new[] { "DanmakuSequence", "EmissionPreset", "PlayerWeaponDefinition", "ProjectileDefinition", "BattleConfig" })
                foreach (string guid in AssetDatabase.FindAssets("t:" + type, new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                    string text = File.ReadAllText(path);
                    if (text.Contains("SpeedLayers:") || text.Contains("SpeedLayerDelta:") || text.Contains("RandomAngleDegrees:") ||
                        text.Contains("HitCount:") || text.Contains("DamageSource:") || text.Contains("DamageConfigId:") ||
                        text.Contains("TargetBuffIds:") || text.Contains("BossHealth:") || text.Contains("BossHitRadius:")) paths.Add(path);
                }
            // Unity serializes the imported objects, preserving GUIDs, references and all supported fields.
            if (paths.Count > 0) AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/DanmakuSimpleMigration.txt", "已清理旧版字段的资产数：" + paths.Count + "\n" + string.Join("\n", paths));
        }
    }

    public static class DanmakuSimpleValidation
    {
        [MenuItem("工具/弹幕/验证简化版本")]
        public static void Run()
        {
            DanmakuSimpleMigration.Migrate();
            DanmakuValidation.Run();
            EnergyOrbValidation.Run();
            MusicAnalysisValidation.Run();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/DanmakuSimpleValidation.txt", "PASS：弹幕模拟与编辑操作、八类默认发射点、单一弹速、发射时间替换／撤销、子母弹末尾边界、能量球、音乐解析与传输。\n");
        }
        public static void RunBatch()
        {
            try { Run(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
