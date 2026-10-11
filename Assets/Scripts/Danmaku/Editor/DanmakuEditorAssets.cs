using System.IO;
using UnityEditor;
using UnityEngine;

namespace TapTap.Danmaku.Editor
{
    public static class DanmakuEditorAssets
    {
        private const string Root = "Assets/DanmakuExamples";

        [MenuItem("工具/弹幕/创建八类示例资产")]
        public static void CreateExamplesMenu() => CreateExamples();

        public static DanmakuSequence CreateExamples()
        {
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var bullet = GetOrCreate<ProjectileDefinition>("SmallBullet");
            var mother = GetOrCreate<ProjectileDefinition>("SplitMother");
            // Only initialize newly created assets; never overwrite designers' edits.
            if (bullet.Settings.Sprite == null)
            {
                string spritePath = Root + "/BulletCircle.png";
                if (!File.Exists(spritePath))
                {
                    var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                    var colors = new Color[1024];
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                            colors[y * 32 + x] = new Color(1, 1, 1,
                                Mathf.Clamp01(15 - new Vector2(x - 15.5f, y - 15.5f).magnitude));
                    texture.SetPixels(colors); texture.Apply();
                    File.WriteAllBytes(spritePath, texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                    AssetDatabase.ImportAsset(spritePath);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(spritePath);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spritePixelsPerUnit = 32; importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true; importer.SaveAndReimport();
                }
                bullet.Settings.Sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                EditorUtility.SetDirty(bullet);
            }
            var playerConfig = CreatePlayerConfig();
            var presets = new EmissionPreset[8];
            for (int i = 0; i < presets.Length; i++)
            {
                string name = ((BuiltinPattern)i).ToString();
                string path = Root + "/" + name + ".asset";
                bool exists = AssetDatabase.LoadAssetAtPath<EmissionPreset>(path);
                presets[i] = GetOrCreate<EmissionPreset>(name);
                if (!exists)
                {
                    presets[i].Settings = DanmakuPresetCatalog.Create((BuiltinPattern)i);
                    presets[i].Pattern = (BuiltinPattern)i;
                    presets[i].Projectile = i == (int)BuiltinPattern.Split ? mother : bullet;
                    EditorUtility.SetDirty(presets[i]);
                }
            }
            if (!mother.Settings.SplitChild)
            {
                mother.Settings = bullet.Settings.Copy();
                mother.Settings.SplitChild = bullet;
                mother.Settings.VisualSize = 0.3f; mother.Settings.Color = new Color(0.8f, 0.5f, 1);
                EditorUtility.SetDirty(mother);
            }
            string sequencePath = Root + "/BossExample.asset";
            var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(sequencePath);
            if (!sequence)
            {
                sequence = DanmakuSequence.CreateExample(); sequence.name = "BossExample";
                sequence.PreviewPlayerConfig = playerConfig; sequence.BossSprite = bullet.Settings.Sprite;
                int[] kinds = { (int)BuiltinPattern.Ring, (int)BuiltinPattern.AimedFan, (int)BuiltinPattern.Split };
                for (int i = 0; i < sequence.Clips.Count; i++)
                {
                    var clip = sequence.Clips[i];
                    clip.Preset = presets[kinds[i]]; clip.OverrideEmission = false;
                    clip.ProjectileOverride.Sprite = bullet.Settings.Sprite;
                    clip.OverrideProjectile = true;
                    if (i == 2) clip.ProjectileOverride.SplitChild = bullet;
                }
                AssetDatabase.CreateAsset(sequence, sequencePath);
            }
            AssetDatabase.SaveAssets(); Selection.activeObject = sequence;
            return sequence;
        }

        [MenuItem("工具/弹幕/重建关键帧示例资产")]
        public static void RebuildKeyframeExamples()
        {
            CreateExamples();
            for (int i = 0; i < 8; i++)
            {
                var preset = GetOrCreate<EmissionPreset>(((BuiltinPattern)i).ToString());
                preset.Pattern = (BuiltinPattern)i; preset.Settings = DanmakuPresetCatalog.Create((BuiltinPattern)i);
                preset.DurationTicks = 180; preset.Keyframes.Clear(); EditorUtility.SetDirty(preset);
            }
            var example = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(Root + "/BossExample.asset");
            RebuildExampleClips(example);
            var savedExample = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(Root + "/BossSequence.asset");
            if (savedExample) RebuildExampleClips(savedExample);
            var battle = AssetDatabase.LoadAssetAtPath<BattleConfig>(BattleEditorSetup.ConfigPath);
            if (battle && battle.Sequence) RebuildExampleClips(battle.Sequence);
            AssetDatabase.SaveAssets();
            // Reserialize owned templates to remove deleted interval/age fields from disk.
            var paths = new System.Collections.Generic.List<string>();
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset is ProjectileDefinition || asset is PlayerWeaponDefinition) paths.Add(path);
            }
            AssetDatabase.ForceReserializeAssets(paths);
            foreach (var window in Resources.FindObjectsOfTypeAll<DanmakuEditorWindow>()) window.RefreshSequence();
        }

        private static void RebuildExampleClips(DanmakuSequence sequence)
        {
            var template = DanmakuSequence.CreateExample();
            var bullet = GetOrCreate<ProjectileDefinition>("SmallBullet");
            try
            {
                // Replace only example scheduling; retain music/analysis and battle configuration.
                sequence.Clips = template.Clips; sequence.Events = template.Events;
                foreach (var clip in sequence.Clips)
                {
                    clip.Preset = GetOrCreate<EmissionPreset>(clip.Pattern.ToString()); clip.OverrideEmission = false;
                    clip.OverrideProjectile = true; clip.ProjectileOverride.Sprite = bullet.Settings.Sprite;
                    if (clip.IsSplit) clip.ProjectileOverride.SplitChild = bullet;
                }
                EditorUtility.SetDirty(sequence);
            }
            finally { Object.DestroyImmediate(template); }
        }

        private static T GetOrCreate<T>(string name) where T : ScriptableObject
        {
            string path = Root + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>(); asset.name = name;
            AssetDatabase.CreateAsset(asset, path); return asset;
        }

        [MenuItem("工具/弹幕/创建玩家配置示例")]
        public static void CreatePlayerConfigMenu() => Selection.activeObject = CreatePlayerConfig();

        public static DanmakuPlayerConfig CreatePlayerConfig()
        {
            const string path = "Assets/Inputs/DefaultPlayer.asset";
            Directory.CreateDirectory("Assets/Inputs"); AssetDatabase.Refresh();
            var config = AssetDatabase.LoadAssetAtPath<DanmakuPlayerConfig>(path);
            if (config) return config;
            config = ScriptableObject.CreateInstance<DanmakuPlayerConfig>(); config.name = "DefaultPlayer";
            config.Sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/BulletCircle.png");
            AssetDatabase.CreateAsset(config, path); AssetDatabase.SaveAssets();
            return config;
        }

        [MenuItem("GameObject/弹幕/攻击播放器", false, 10)]
        private static void CreatePlayer()
        {
            var obj = new GameObject("Danmaku Sequence Player");
            Undo.RegisterCreatedObjectUndo(obj, "创建弹幕播放器");
            var player = obj.AddComponent<DanmakuSequencePlayer>();
            player.Sequence = Selection.activeObject as DanmakuSequence;
            if (!player.Sequence) player.Sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(Root + "/BossExample.asset");
            Selection.activeGameObject = obj;
        }
    }
}
