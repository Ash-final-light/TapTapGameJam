using System;
using System.IO;
using System.Linq;
using Spine.Unity;
using TapTap.Danmaku;
using TapTap.Spine.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Player.Editor
{
    /// <summary>Creates an independent Hero loadout without rewriting the imported art or legacy player.</summary>
    public static class HeroPlayerSetup
    {
        public const string ActionsPath = "Assets/Player/HeroActions";
        public const string PortraitPath = "Assets/Player/HeroPortrait.png";
        private static readonly string[] BattleScenes =
        { "Assets/Scenes/GameplayPlaceholder.unity", "Assets/Scenes/CustomSongBattle.unity" };

        [MenuItem("Tools/角色/接入 Hero 到现有战斗")]
        public static void ApplyToBattles()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("接入 Hero 需要退出 PlayMode。");
            foreach (string path in BattleScenes)
            {
                var loaded = SceneManager.GetSceneByPath(path);
                if (loaded.isLoaded && loaded.isDirty)
                    throw new InvalidOperationException("场景存在未保存修改，不会覆盖：" + path);
            }
            var definition = EnsureAssets();
            foreach (string path in BattleScenes)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleManager>(true)).ToArray();
                    if (managers.Length == 0) throw new InvalidOperationException("场景没有 BattleManager：" + path);
                    foreach (var manager in managers)
                    {
                        Undo.RecordObject(manager, "Use Hero player");
                        manager.PlayerDefinition = definition;
                        EditorUtility.SetDirty(manager);
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            Debug.Log("Hero 已接入两个战斗入口；技能复用反击动作和绿/黄特效，不添加弹反判定。", definition);
        }

        public static PlayerDefinition EnsureAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerEditorAssets.DefinitionPath);
            if (existing) return existing;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeroSpineImport.PrefabPath);
            var skeleton = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(HeroSpineImport.Folder + "/char_hero_SkeletonData.asset");
            if (!prefab || !skeleton || skeleton.GetSkeletonData(false) == null)
                throw new InvalidOperationException("请先导入 Hero 的独立 Spine 资源。");

            var legacy = PlayerEditorAssets.EnsureLegacyAssets();
            var definition = Object.Instantiate(legacy);
            definition.name = "Hero";
            definition.VisualPrefab = prefab;
            definition.SkeletonData = skeleton;
            definition.Skill1Color = new Color(0.05f, 1, 0.55f);
            definition.Skill2Color = new Color(1, 0.88f, 0.08f);
            definition.TintDuringSkills = false;
            definition.Portrait = definition.FallbackSprite = EnsurePortrait(prefab);
            definition.FallbackVisualSize = 2.2f;

            Directory.CreateDirectory(ActionsPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var library = ScriptableObject.CreateInstance<PlayerActionLibrary>();
            AssetDatabase.CreateAsset(library, ActionsPath + ".asset");
            definition.Actions = library;
            foreach (PlayerClipSlot slot in Enum.GetValues(typeof(PlayerClipSlot)))
            {
                var source = legacy.Actions.Get(slot);
                var clip = source ? Object.Instantiate(source) : PlayerEditorAssets.CreatePreset(slot);
                clip.name = slot.ToString();
                clip.StableId = Guid.NewGuid().ToString("N");
                ConfigureAnimation(clip, slot);
                if (slot is PlayerClipSlot.Skill1 or PlayerClipSlot.Skill2)
                {
                    bool second = slot == PlayerClipSlot.Skill2;
                    var effect = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Parry/" + (second ? "黄色弹反" : "绿色弹反") + ".prefab");
                    if (!effect) throw new InvalidOperationException("缺少技能序列帧特效。");
                    clip.Nodes.Add(new PlayerActionNode
                    {
                        Name = second ? "黄色技能释放" : "绿色技能释放", Kind = PlayerActionNodeKind.Effect,
                        StartTick = clip.ActiveStartTick, EndTick = clip.DurationTicks, Effect = effect,
                        LocalOffset = new Vector3(0.9f, 1, -0.02f), FollowPlayer = true, MirrorWithFacing = true
                    });
                }
                string error = clip.Validate();
                if (error != null) throw new InvalidOperationException(slot + "：" + error);
                AssetDatabase.CreateAsset(clip, ActionsPath + "/" + slot + ".asset");
                library.Bindings.Add(new PlayerClipBinding { Slot = slot, Clip = clip });
            }
            string validation = definition.Validate();
            if (validation != null) throw new InvalidOperationException(validation);
            AssetDatabase.CreateAsset(definition, PlayerEditorAssets.DefinitionPath);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static void ConfigureAnimation(PlayerActionClip clip, PlayerClipSlot slot)
        {
            string name = slot switch
            {
                PlayerClipSlot.Idle or PlayerClipSlot.FlyLoop or PlayerClipSlot.Land => "player_idle",
                PlayerClipSlot.Move => "player_run",
                PlayerClipSlot.Jump or PlayerClipSlot.FlyStart => "player_jump_up",
                PlayerClipSlot.Fall or PlayerClipSlot.FlyEnd => "player_jump_fall",
                PlayerClipSlot.Dash => "player_dash",
                PlayerClipSlot.Hurt => "player_hurt_flash",
                PlayerClipSlot.Dead => "player_dead",
                _ => "palyer_counter"
            };
            var animation = AssetDatabase.LoadAssetAtPath<AnimationReferenceAsset>(HeroSpineImport.Folder + "/Animations/" + name + ".asset");
            if (!animation || animation.Animation == null) throw new InvalidOperationException("缺少 Hero 动画：" + name);
            bool loop = slot is PlayerClipSlot.Idle or PlayerClipSlot.Move or PlayerClipSlot.Fall or PlayerClipSlot.FlyLoop;
            if (loop || slot == PlayerClipSlot.Dead)
                clip.DurationTicks = Mathf.RoundToInt(animation.Animation.Duration * PlayerActionClip.TickRate);
            clip.EndMode = loop ? PlayerClipEndMode.Loop : slot == PlayerClipSlot.Dead ? PlayerClipEndMode.HoldLast : PlayerClipEndMode.Complete;
            clip.Nodes.RemoveAll(node => node.Kind == PlayerActionNodeKind.Spine);
            foreach (var node in clip.Nodes)
                if (!node.IsPoint) node.EndTick = Mathf.Min(node.EndTick, clip.DurationTicks);
            bool shortTransition = slot is PlayerClipSlot.FlyStart or PlayerClipSlot.FlyEnd or PlayerClipSlot.Land;
            clip.Nodes.Insert(0, new PlayerActionNode
            {
                Name = "Spine " + name, Kind = PlayerActionNodeKind.Spine, EndTick = clip.DurationTicks,
                Spine = new PlayerSpineSegment
                {
                    Animation = animation, Loop = loop, MixSeconds = shortTransition ? 0.05f : 0.08f,
                    Speed = loop || shortTransition ? 1 : animation.Animation.Duration * PlayerActionClip.TickRate / clip.DurationTicks
                }
            });
        }

        private static Sprite EnsurePortrait(GameObject prefab)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPath);
            if (existing) return existing;
            var scene = EditorSceneManager.NewPreviewScene();
            var root = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Hero Portrait Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                var animation = root.GetComponent<SkeletonAnimation>();
                animation.Initialize(false); animation.enabled = false;
                animation.AnimationState.SetAnimation(0, "player_idle", true);
                animation.Update(0.2f); animation.LateUpdate();
                var bounds = root.GetComponent<Renderer>().bounds;
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false; camera.scene = scene; camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.nearClipPlane = 0.1f; camera.farClipPlane = 30;
                camera.transform.position = bounds.center + Vector3.back * 10;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.08f;
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
                File.WriteAllBytes(PortraitPath, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            AssetDatabase.ImportAsset(PortraitPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PortraitPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPath);
        }
    }
}
