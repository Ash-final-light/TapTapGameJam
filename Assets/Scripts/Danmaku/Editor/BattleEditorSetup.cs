using System;
using System.IO;
using System.Linq;
using TapTap.Systems;
using TapTap.Player.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TapTap.Danmaku.Editor
{
    public static class BattleEditorSetup
    {
        public const string ScenePath = "Assets/Scenes/GameplayPlaceholder.unity";
        public const string MusicPath = "Assets/AudioAsset/VISUAL ARTS , Key - Remembrance.mp3";
        public const string ConfigPath = "Assets/DanmakuExamples/PhaseOne/Battle.asset";
        private const string Folder = "Assets/DanmakuExamples/PhaseOne";
        [MenuItem("Tools/Danmaku/第一阶段/接入 GameplayPlaceholder")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 PlayMode 后接入场景。");
            var scene = SceneManager.GetSceneByPath(ScenePath); bool opened = !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("GameplayPlaceholder 有未保存修改，请先手动保存，再执行接入。");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var cameras = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Where(c => c.enabled).ToArray();
                if (cameras.Length != 1) throw new InvalidOperationException("目标场景需要明确的一台启用相机；请在 BattleManager 中手动指定 AimCamera。");
                var config = CreateAssets();
                var managers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattleManager>(true)).ToArray();
                if (managers.Length > 1) throw new InvalidOperationException("目标场景有多个 BattleManager，请先确定唯一战斗入口。");
                var manager = managers.FirstOrDefault();
                if (!manager) { var root = new GameObject("BattleManager"); SceneManager.MoveGameObjectToScene(root, scene); manager = root.AddComponent<BattleManager>(); }
                manager.Config = config; manager.AimCamera = cameras[0];
                manager.PlayerDefinition = PlayerEditorAssets.EnsureAssets();
                cameras[0].orthographicSize = Mathf.Max(cameras[0].orthographicSize, config.Sequence.FieldSize.y * 0.54f);
                foreach (var debug in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HudDebugDataSource>(true))) debug.enabled = false;
                if (!scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<SystemRoot>(true)))
                {
                    const string path = "Assets/UI/Spotlight/Prefabs/SystemRoot.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!prefab) throw new InvalidOperationException("找不到已存在的 SystemRoot.prefab，不能猜测输入/UI 配置。");
                    PrefabUtility.InstantiatePrefab(prefab, scene);
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets(); Debug.Log("第一阶段战斗已接入 " + ScenePath, config);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        public static BattleConfig CreateAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BattleConfig>(ConfigPath);
            if (existing) return existing;
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            if (!music) throw new InvalidOperationException("指定音乐尚未导入或路径不存在：" + MusicPath);
            var player = GetOrCreate<DanmakuPlayerConfig>("Player");
            var normal = GetOrCreate<ProjectileDefinition>("NormalProjectile"); normal.Settings.Damage = 10;
            var spread = GetOrCreate<ProjectileDefinition>("SpreadProjectile"); spread.Settings.Damage = 5;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/DanmakuExamples/BulletCircle.png");
            normal.Settings.Sprite = spread.Settings.Sprite = player.Sprite = sprite;
            var weapon = GetOrCreate<PlayerWeaponDefinition>("NormalWeapon");
            weapon.Mode = PlayerWeaponMode.Normal; weapon.FireRate = 5; weapon.Projectile = normal; weapon.Emission.Speed = 8;
            var fan = GetOrCreate<PlayerWeaponDefinition>("SpreadWeapon");
            fan.Mode = PlayerWeaponMode.Spread; fan.FireRate = 2.5f; fan.Projectile = spread;
            fan.Emission.Shape = EmissionShape.Arc; fan.Emission.Count = 5; fan.Emission.ArcDegrees = 30; fan.Emission.Speed = 6;
            const string seqPath = Folder + "/Sequence.asset";
            var sequence = AssetDatabase.LoadAssetAtPath<DanmakuSequence>(seqPath);
            if (!sequence)
            {
                sequence = DanmakuSequence.CreateExample(); sequence.PreviewPlayerConfig = player;
                sequence.DurationTicks = Mathf.CeilToInt(music.length * 60); AssetDatabase.CreateAsset(sequence, seqPath);
            }
            sequence.Music = music;
            var config = GetOrCreate<BattleConfig>("Battle"); config.Sequence = sequence;
            foreach (var asset in new UnityEngine.Object[] { player, normal, spread, weapon, fan, sequence, config }) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets(); return config;
        }
        private static T GetOrCreate<T>(string name) where T : ScriptableObject
        {
            string path = Folder + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        [MenuItem("Tools/Danmaku/第一阶段/解析 Remembrance")]
        public static void AnalyzeMusic()
        {
            var config = AssetDatabase.LoadAssetAtPath<BattleConfig>(ConfigPath);
            if (!config) throw new InvalidOperationException("请先执行接入 GameplayPlaceholder。");
            MusicAnalysisJob.Start(config.Sequence, () => Debug.Log("Remembrance 音轨解析已保存。", config.Sequence));
        }
    }
}
