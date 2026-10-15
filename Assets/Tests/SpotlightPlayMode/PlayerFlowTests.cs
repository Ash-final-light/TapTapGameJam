#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Spotlight.Tests
{
    public sealed class PlayerFlowTests
    {
        private object manager, root;
        private Keyboard keyboard;
        private ScriptableObject battle, sequence;
        private ScriptableObject systemConfig;
        private string previousSaveFolder;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
        private static object Get(object owner, string name) => owner.GetType().GetProperty(name)?.GetValue(owner) ?? owner.GetType().GetField(name)?.GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name).SetValue(owner, value);
        private static object Call(object owner, string name, params object[] args)
        {
            foreach (var method in owner.GetType().GetMethods()) if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(owner, args);
            throw new MissingMethodException(name);
        }
        private object Player => Get(Get(manager, "Simulation"), "PlayerSystem");
        private void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        private void Tick(int count = 1)
        { for (int i = 0; i < count; i++) Call(manager, "AdvanceFrame", 1d / 60); }
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            oldBackground = InputSystem.settings.backgroundBehavior; oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            systemConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Configs/Spotlight/SystemConfig.asset");
            previousSaveFolder = (string)Get(systemConfig, "SaveFolder");
            Set(systemConfig, "SaveFolder", "SpotlightTests/Player-" + Guid.NewGuid().ToString("N"));
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/GameplayPlaceholder.unity"); yield return null;
            foreach (var component in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (component.GetType().FullName == "TapTap.Danmaku.BattleManager") manager = component;
                if (component.GetType().FullName == "TapTap.Systems.SystemRoot") root = component;
            }
            Assert.IsNotNull(manager); Assert.IsNotNull(root); Call(manager, "Stop");
            battle = UnityEngine.Object.Instantiate((ScriptableObject)Get(manager, "Config"));
            sequence = ScriptableObject.CreateInstance(Type.GetType("TapTap.Danmaku.DanmakuSequence, Assembly-CSharp", true));
            Set(sequence, "FieldSize", new Vector2(20, 20)); Set(battle, "Sequence", sequence); Set(manager, "Config", battle);
            // Direct scene startup registers its authored song. This fixture deliberately
            // uses a silent simulation, so remove that context before playing the clone.
            root.GetType().GetProperty("ActiveRun").SetValue(root, null);
            Keys(); Call(manager, "Play"); Call(manager, "Pause");
            Assert.IsNotNull(Get(manager, "Simulation"));
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (manager != null) Call(manager, "Stop");
            if (root is Component component) UnityEngine.Object.Destroy(component.gameObject);
            yield return null;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            if (battle) UnityEngine.Object.Destroy(battle); if (sequence) UnityEngine.Object.Destroy(sequence);
            if (systemConfig) Set(systemConfig, "SaveFolder", previousSaveFolder);
            manager = root = null;
        }
        [UnityTest]
        public IEnumerator SameTickUsesSkillPriorityAndKeepsLowerRequests()
        {
            Keys(Key.Q, Key.LeftShift, Key.Space); Call(manager, "Resume"); Tick();
            Assert.AreEqual("Skill", Get(Get(Get(Player, "Action"), "Clip"), "Kind").ToString());
            Assert.AreEqual(1, Get(Get(Player, "Action"), "SkillSlot"));
            Assert.AreEqual(Get(Player, "MaxStamina"), Get(Player, "Stamina"));
            var pending = (ICollection)Get(Get(Player, "Inputs"), "Pending"); Assert.AreEqual(2, pending.Count);
            Call(manager, "Pause"); yield return null;
        }
        [UnityTest]
        public IEnumerator SkillTwoDoesNotConsumeStamina()
        {
            Keys(Key.E); Call(manager, "Resume"); Tick(7);
            Assert.AreEqual("Skill", Get(Get(Get(Player, "Action"), "Clip"), "Kind").ToString());
            Assert.AreEqual(2, Get(Get(Player, "Action"), "SkillSlot"));
            Assert.AreEqual(Get(Player, "MaxStamina"), Get(Player, "Stamina"));
            Call(manager, "Pause"); yield return null;
        }
        [UnityTest]
        public IEnumerator HoldingJumpEntersFlightAndReleaseExits()
        {
            Keys(Key.Space); Call(manager, "Resume"); Tick(12);
            Assert.IsTrue((bool)Get(Get(Player, "Motor"), "Flying"));
            Assert.Less((float)Call(Get(Player, "Stamina"), "ToFloat"), (float)Call(Get(Player, "MaxStamina"), "ToFloat"));
            Keys(); Tick(); Assert.IsFalse((bool)Get(Get(Player, "Motor"), "Flying"));
            Call(manager, "Pause"); yield return null;
        }
        [UnityTest]
        public IEnumerator PauseFreezesTickAndClearsBufferedInput()
        {
            Keys(Key.LeftShift); Call(manager, "Resume"); Tick(4);
            Assert.IsTrue((bool)Get(Player, "ActionInvulnerable"));
            int tick = (int)Get(Player, "Tick"); Call(manager, "Pause"); Tick(60);
            Assert.AreEqual(tick, Get(Player, "Tick"));
            Assert.AreEqual(0, ((ICollection)Get(Get(Player, "Inputs"), "Pending")).Count);
            Keys(); Call(manager, "Resume"); Tick(12); Assert.IsFalse((bool)Get(Player, "ActionInvulnerable"));
            Call(manager, "Pause"); yield return null;
        }
        [UnityTest]
        public IEnumerator SpineMeshAndHudFollowPlayerState()
        {
            Keys(Key.LeftShift); Call(manager, "Resume"); Tick(4);
            var playerVisual = ((Component)manager).transform.Find("Battle Views/Player Visual"); Assert.IsNotNull(playerVisual);
            var mesh = playerVisual.GetComponentInChildren<MeshFilter>(); Assert.IsNotNull(mesh); Assert.Greater(mesh.sharedMesh.vertexCount, 0);
            var hud = Get(manager, "Current"); Assert.AreEqual(100f, (float)Get(hud, "Stamina"));
            var hitRange = playerVisual.Find("Player Hit Range"); Assert.IsNotNull(hitRange); Assert.IsTrue(hitRange.gameObject.activeInHierarchy);
            var ring = hitRange.Find("Boundary").GetComponent<LineRenderer>();
            var actor = Get(Player, "Actor"); var pose = Get(actor, "Pose");
            float radius = (float)Call(Get(pose, "HitRadius"), "ToFloat");
            Assert.AreEqual(radius, Vector2.Distance(ring.GetPosition(0), playerVisual.position), 0.0001f);
            var expectedColor = (Color)Get(Get(manager, "PlayerDefinition"), "InvulnerableHitRangeColor");
            Assert.AreEqual(expectedColor.g, ring.startColor.g, 0.0001f);
            Assert.Greater(ring.sortingOrder, mesh.GetComponent<Renderer>().sortingOrder);
            foreach (var component in playerVisual.GetComponentsInChildren<MonoBehaviour>())
                if (component.GetType().FullName == "Spine.Unity.SkeletonAnimation")
                    Assert.Less((float)Get(Get(component, "Skeleton"), "A"), 1f);
            Call(manager, "Pause"); yield return null;
        }
    }
}
#endif
