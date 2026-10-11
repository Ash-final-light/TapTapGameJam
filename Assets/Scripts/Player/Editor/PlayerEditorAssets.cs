using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Spine.Unity;
using TapTap.Danmaku;
using TapTap.Danmaku.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Player.Editor
{
    public static class PlayerEditorAssets
    {
        public const string Root = "Assets/Player";
        public const string DefinitionPath = Root + "/MaidGhost.asset";
        private const string LegacyBattle = "Assets/DanmakuExamples/PhaseOne/Battle.asset";
        [MenuItem("Tools/角色/创建默认角色与动作库")]
        public static void CreateMenu() { Selection.activeObject = EnsureAssets(); PlayerActionEditorWindow.Open(); }
        [MenuItem("Tools/角色/体力仅用于飞行")]
        public static void ApplyFlightOnlyStamina()
        {
            var definition = EnsureAssets();
            foreach (var slot in new[] { PlayerClipSlot.Dash, PlayerClipSlot.Skill1, PlayerClipSlot.Skill2 })
            {
                var clip = definition.Actions.Get(slot);
                if (!clip) throw new InvalidOperationException($"缺少 {slot} 动作绑定。");
                Undo.RecordObject(clip, "Use stamina for flight only"); clip.StaminaCost = 0; EditorUtility.SetDirty(clip);
            }
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }
        public static PlayerDefinition EnsureAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DefinitionPath);
            if (existing) return existing;
            // Read old serialized fields before saving the now smaller BattleConfig.
            string legacy = File.Exists(LegacyBattle) ? File.ReadAllText(LegacyBattle) : "";
            Directory.CreateDirectory(Root + "/Actions"); Directory.CreateDirectory(Root + "/Audio"); AssetDatabase.Refresh();
            var definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            definition.MaxHealth = ReadNumber(legacy, "PlayerHealth", 5); definition.HurtInvulnerableTicks = (int)ReadNumber(legacy, "PlayerInvulnerableTicks", 30);
            definition.Weapon = ReadReference<PlayerWeaponDefinition>(legacy, "Weapon") ?? AssetDatabase.LoadAssetAtPath<PlayerWeaponDefinition>("Assets/DanmakuExamples/PhaseOne/NormalWeapon.asset");
            var old = ReadReference<DanmakuPlayerConfig>(legacy, "Player");
            if (old) { definition.Motor.MoveSpeed = old.NormalMoveSpeed; definition.HitRadius = old.HitRadius; definition.FallbackSprite = definition.Portrait = old.Sprite; definition.FallbackVisualSize = old.VisualSize; }
            definition.VisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArtAsset/Spine/MaidGhost/MaidGhost.prefab");
            definition.SkeletonData = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>("Assets/ArtAsset/Spine/MaidGhost/char_3_maid_SkeletonData.asset");
            var library = ScriptableObject.CreateInstance<PlayerActionLibrary>(); AssetDatabase.CreateAsset(library, Root + "/Actions.asset");
            definition.Actions = library;
            var jumpSound = MakeSound("Jump", 660); var dashSound = MakeSound("Dash", 220); var skillSound = MakeSound("Skill", 880); var hurtSound = MakeSound("Hurt", 140);
            var trail = MakeTrail();
            foreach (PlayerClipSlot slot in Enum.GetValues(typeof(PlayerClipSlot)))
            {
                string path = Root + "/Actions/" + slot + ".asset";
                var clip = CreatePreset(slot);
                if (slot == PlayerClipSlot.Jump) AddAudio(clip, jumpSound);
                if (slot == PlayerClipSlot.Dash)
                {
                    AddAudio(clip, dashSound);
                    clip.Nodes.Add(new PlayerActionNode { Kind = PlayerActionNodeKind.Effect, Name = "冲刺残影", StartTick = 3, EndTick = 15, Effect = trail });
                }
                if (slot == PlayerClipSlot.FlyStart) AddAudio(clip, jumpSound);
                if (slot == PlayerClipSlot.Land) AddAudio(clip, dashSound);
                if (slot == PlayerClipSlot.Skill1 || slot == PlayerClipSlot.Skill2)
                {
                    AddAudio(clip, skillSound);
                    clip.Nodes.First(n => n.Kind == PlayerActionNodeKind.PointEvent).Projectile = definition.Weapon ? definition.Weapon.Projectile : null;
                }
                if (slot == PlayerClipSlot.Hurt || slot == PlayerClipSlot.Dead) AddAudio(clip, hurtSound);
                AssetDatabase.CreateAsset(clip, path); library.Bindings.Add(new() { Slot = slot, Clip = clip });
            }
            AssetDatabase.CreateAsset(definition, DefinitionPath); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            return definition;
        }
        public static PlayerActionClip CreatePreset(PlayerClipSlot slot)
        {
            var c = ScriptableObject.CreateInstance<PlayerActionClip>(); c.name = c.DisplayName = slot.ToString();
            c.DurationTicks = slot switch { PlayerClipSlot.Dash => 24, PlayerClipSlot.Jump => 28, PlayerClipSlot.Skill1 or PlayerClipSlot.Skill2 => 30,
                PlayerClipSlot.Hurt => 32, PlayerClipSlot.Dead => 150, PlayerClipSlot.FlyStart or PlayerClipSlot.FlyEnd or PlayerClipSlot.Land => 6, _ => 60 };
            c.ActionKind = slot switch { PlayerClipSlot.Dash => PlayerActionKind.Dash, PlayerClipSlot.Jump => PlayerActionKind.Jump,
                PlayerClipSlot.Skill1 or PlayerClipSlot.Skill2 => PlayerActionKind.Skill, PlayerClipSlot.Hurt => PlayerActionKind.Hurt,
                PlayerClipSlot.Dead => PlayerActionKind.Dead, PlayerClipSlot.Land => PlayerActionKind.Land, _ => PlayerActionKind.None };
            c.EndMode = slot == PlayerClipSlot.Dead ? PlayerClipEndMode.HoldLast :
                slot is PlayerClipSlot.Idle or PlayerClipSlot.Move or PlayerClipSlot.Fall or PlayerClipSlot.FlyLoop ? PlayerClipEndMode.Loop : PlayerClipEndMode.Complete;
            string animation = slot switch { PlayerClipSlot.Idle => "Standby", PlayerClipSlot.Move => "Run", PlayerClipSlot.Hurt => "Hurt", PlayerClipSlot.Dead => "Die",
                PlayerClipSlot.Skill1 => "RoadHitGreat1", PlayerClipSlot.Skill2 => "AirHitGreat1", _ => "Jump" };
            c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Spine, Name = "Spine " + animation, EndTick = c.DurationTicks,
                Spine = new PlayerSpineSegment { FallbackAnimation = animation, Loop = c.EndMode == PlayerClipEndMode.Loop } });
            if (slot == PlayerClipSlot.Jump)
            {
                c.Nodes.Add(new() { Kind = PlayerActionNodeKind.PointEvent, Name = "起跳冲量", Event = PlayerPointEventKind.JumpImpulse });
                c.DefaultTransitions = PlayerIntentMask.Skill | PlayerIntentMask.Dash | PlayerIntentMask.Fly;
            }
            if (slot == PlayerClipSlot.Dash)
            {
                c.DefaultAllowFire = false; c.StaminaCost = 0; c.CooldownTicks = 24; c.ActiveStartTick = 3; c.RecoveryStartTick = 15;
                c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Motion, Name = "锁定方向冲刺", StartTick = 3, EndTick = 15, Motion = PlayerMotionMode.Dash });
                c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Invulnerability, Name = "冲刺无敌", StartTick = 3, EndTick = 15 });
                c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Transition, Name = "允许接飞行", StartTick = 12, EndTick = 24, AllowedIntents = PlayerIntentMask.Fly,
                    Conditions = new[] { PlayerTransitionCondition.Airborne, PlayerTransitionCondition.JumpHeld, PlayerTransitionCondition.HoldThreshold, PlayerTransitionCondition.HasFlightStamina } });
            }
            if (slot == PlayerClipSlot.Skill1 || slot == PlayerClipSlot.Skill2)
            {
                c.DefaultAllowFire = false; c.StaminaCost = 0; c.CooldownTicks = 60; c.ActiveStartTick = 6; c.RecoveryStartTick = 18;
                c.Nodes.Add(new() { Kind = PlayerActionNodeKind.PointEvent, Name = "技能发射", StartTick = 6, Event = PlayerPointEventKind.SkillPulse, ProjectileSpeed = 12 });
                c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Transition, Name = "后摇可冲刺", StartTick = 18, EndTick = 30, AllowedIntents = PlayerIntentMask.Dash });
            }
            if (slot == PlayerClipSlot.Dead)
            { c.DefaultAllowFire = false; c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Motion, Name = "禁止移动", EndTick = c.DurationTicks, Motion = PlayerMotionMode.Disabled }); }
            return c;
        }
        private static void AddAudio(PlayerActionClip c, AudioClip sound) => c.Nodes.Add(new() { Kind = PlayerActionNodeKind.Audio, Name = "提示音", Audio = sound, Order = 100 });
        private static AudioClip MakeSound(string name, double frequency)
        {
            string path = Root + "/Audio/" + name + ".wav";
            if (!File.Exists(path))
            {
                const int rate = 22050, samples = 3307;
                using var writer = new BinaryWriter(File.Create(path));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * frequency * Math.PI * 2 / rate) * Math.Sin(Math.PI * i / samples) * 5000));
            }
            AssetDatabase.ImportAsset(path); return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        private static GameObject MakeTrail()
        {
            string path = Root + "/DashTrail.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var material = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(material, Root + "/Trail.mat");
            var root = new GameObject("DashTrail");
            try
            {
                var trail = root.AddComponent<TrailRenderer>(); trail.sharedMaterial = material; trail.time = 0.12f; trail.startWidth = 0.25f; trail.endWidth = 0;
                trail.startColor = new Color(0.5f, 0.8f, 1, 0.5f); trail.endColor = new Color(0.5f, 0.8f, 1, 0); trail.sortingOrder = 19;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [MenuItem("Tools/角色/迁移现有战斗角色并接入场景")]
        public static void Migrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("迁移需要退出 PlayMode。");
            string legacy = File.Exists(LegacyBattle) ? File.ReadAllText(LegacyBattle) : "";
            var definition = EnsureAssets();
            var scene = SceneManager.GetSceneByPath(BattleEditorSetup.ScenePath); bool opened = !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("场景存在未保存修改，迁移不会覆盖它。");
            if (opened) scene = EditorSceneManager.OpenScene(BattleEditorSetup.ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var manager in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattleManager>(true)))
                {
                    Undo.RecordObject(manager, "Migrate independent player"); manager.PlayerDefinition = definition;
                    var match = Regex.Match(legacy, @"PlayerSpawn: \{x: ([^,]+), y: ([^}]+)\}");
                    if (match.Success) manager.PlayerSpawn = new Vector2(float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
                    EditorUtility.SetDirty(manager);
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.ForceReserializeAssets(new[] { LegacyBattle }); AssetDatabase.SaveAssets();
            Debug.Log("玩家系统已迁移：BattleConfig 仅保留战斗规则，场景入口引用独立 PlayerDefinition。", definition);
        }
        private static float ReadNumber(string yaml, string name, float fallback)
        {
            var match = Regex.Match(yaml, @"(?m)^  " + name + @": ([^\r\n]+)");
            return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }
        private static T ReadReference<T>(string yaml, string name) where T : UnityEngine.Object
        {
            var match = Regex.Match(yaml, @"(?m)^  " + name + @": .*guid: ([a-f0-9]{32})");
            return match.Success ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)) : null;
        }
    }
}
