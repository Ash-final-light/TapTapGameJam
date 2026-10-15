#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Spotlight.Tests
{
    public sealed class CustomSongFlowTests
    {
        private UnityEngine.Object config;
        private object root, ui;
        private string previousFolder, wavPath;
        private static object Get(object owner, string name)
        {
            var type = owner.GetType();
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner) ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
        }
        private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name).Invoke(owner, args);
        private static void Click(object view, string field) => ((Button)Get(view, field)).onClick.Invoke();
        private static IEnumerator Frames(int count = 3) { for (int i = 0; i < count; i++) yield return null; }
        private IEnumerator Wait(Func<bool> predicate, float timeout = 40)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(predicate(), "等待流程超时；当前界面=" + Get(ui, "Context"));
            yield return Frames();
        }
        [UnitySetUp]
        public IEnumerator Setup()
        {
            config = AssetDatabase.LoadMainAssetAtPath("Assets/Configs/Spotlight/SystemConfig.asset");
            previousFolder = (string)Get(config, "SaveFolder");
            config.GetType().GetField("SaveFolder").SetValue(config, "SpotlightTests/Custom-" + Guid.NewGuid().ToString("N"));
            wavPath = Path.Combine(Path.GetTempPath(), "CustomSong-" + Guid.NewGuid().ToString("N") + ".wav");
            WriteWav(wavPath);
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Title.unity"); yield return Frames();
            root = Type.GetType("TapTap.Systems.SystemRoot, Assembly-CSharp", true).GetProperty("Instance").GetValue(null);
            Assert.IsNotNull(root); ui = Get(root, "UI"); Call(ui, "OpenImportedMusic"); yield return Frames();
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (root is Component component && component) UnityEngine.Object.Destroy(component.gameObject);
            if (config) config.GetType().GetField("SaveFolder").SetValue(config, previousFolder);
            if (File.Exists(wavPath)) File.Delete(wavPath);
            yield return Frames();
        }
        private IEnumerator ImportAligned()
        {
            yield return (IEnumerator)Call(Get(ui, "ImportedMusic"), "ImportPath", wavPath);
            var library = Get(root, "Music"); var songs = (Array)Get(library, "Songs"); Assert.AreEqual(1, songs.Length);
            string id = (string)Get(songs.GetValue(0), "Id");
            var result = Call(library, "SaveAlignment", id, 120.0, 0.1);
            Assert.IsTrue((bool)Get(result, "Success"));
            Call(ui, "OpenImportedMusic"); yield return Frames();
            Assert.IsTrue(((Button)Get(Get(ui, "ImportedMusic"), "Challenge")).interactable);
            Click(Get(ui, "ImportedMusic"), "Challenge"); yield return Frames();
            Assert.AreEqual("CustomSongPrepare", Get(ui, "Context").ToString());
        }
        [UnityTest]
        public IEnumerator ImportedSongGeneratesPlaysSettlesRetriesAndReturnsWithoutDrops()
        {
            yield return ImportAligned(); var preparation = Get(ui, "CustomSong");
            Click(preparation, "Generate");
            yield return Wait(() => ((Button)Get(preparation, "StartBattle")).interactable);
            var service = Get(root, "CustomSongs"); var session = Get(service, "Prepared"); Assert.IsNotNull(session);
            var sequence = Get(session, "Sequence"); string generated = JsonUtility.ToJson(sequence);
            Assert.AreEqual(120f, Get(sequence, "ManualMusicBpm"));
            Assert.That(((TMP_Text)Get(preparation, "Status")).text, Does.Contain("低频"));
            CaptureUI("custom-song-prepare");
            Click(preparation, "StartBattle");
            yield return Wait(() => SceneManager.GetActiveScene().path == "Assets/Scenes/CustomSongBattle.unity");
            Component manager = null;
            yield return Wait(() =>
            {
                manager = UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp", true)) as Component;
                return manager && Get(manager, "Simulation") != null;
            });
            Assert.IsTrue((bool)Get(manager, "IsRunning"));
            Call(ui, "OpenPause"); yield return Frames();
            int paused = (int)Get(Get(manager, "Simulation"), "Tick"); yield return Frames(12);
            Assert.AreEqual(paused, Get(Get(manager, "Simulation"), "Tick"));
            Call(ui, "Resume");
            yield return Wait(() => Get(ui, "Context").ToString() == "Result", 25);
            var result = Get(root, "LastResult"); Assert.IsTrue((bool)Get(result, "IsCustom"));
            Assert.IsTrue((bool)Get(Get(result, "Stats"), "Completed"));
            Assert.AreEqual(0, Get(result, "DropQuantity"));
            Assert.AreEqual(0, ((IList)Get(Get(Get(root, "Saves"), "Current"), "Items")).Count);
            Assert.That(((TMP_Text)Get(Get(ui, "Result"), "Title")).text, Does.Contain("CustomSong-"));
            CaptureUI("custom-song-result");
            Click(Get(ui, "Result"), "Retry");
            yield return Wait(() => Get(root, "LastResult") == null && Get(ui, "Context").ToString() == "Gameplay");
            Assert.AreSame(session, Get(service, "Prepared"));
            Assert.AreEqual(generated, JsonUtility.ToJson(Get(Get(service, "Prepared"), "Sequence")));
            var exit = Call(Get(root, "Scenes"), "AbandonToMusic"); Assert.IsTrue((bool)Get(exit, "Success"));
            yield return Wait(() => Get(ui, "Context").ToString() == "ImportedMusic");
            Assert.IsNull(Get(service, "Prepared")); Assert.IsTrue((bool)Get(session, "Disposed"));
            Assert.IsTrue(((Button)Get(Get(ui, "ImportedMusic"), "Challenge")).interactable);
        }
        [UnityTest]
        public IEnumerator CancelPreparationAndRegenerateFromCacheWithoutStaleSession()
        {
            yield return ImportAligned(); var view = Get(ui, "CustomSong"); var service = Get(root, "CustomSongs");
            Click(view, "Generate"); Call(view, "Close"); yield return Frames(15);
            Assert.IsNull(Get(service, "Prepared")); Assert.IsFalse((bool)Get(service, "Preparing"));
            Click(Get(ui, "ImportedMusic"), "Challenge"); Click(view, "Generate");
            yield return Wait(() => ((Button)Get(view, "StartBattle")).interactable);
            var first = Get(service, "Prepared"); string spectrum = JsonUtility.ToJson(Get(first, "Sequence"));
            // Only compare the timeline itself; a reload owns a new Unity AudioClip and analysis object.
            var firstClips = JsonUtility.ToJson(new ClipSnapshot { Json = Timeline(Get(first, "Sequence")) });
            Click(view, "Generate"); yield return Wait(() => ((Button)Get(view, "StartBattle")).interactable);
            Assert.IsTrue((bool)Get(first, "Disposed"));
            Assert.AreEqual(firstClips, JsonUtility.ToJson(new ClipSnapshot { Json = Timeline(Get(Get(service, "Prepared"), "Sequence")) }));
        }
        private static string Timeline(object sequence)
        {
            string result = "";
            foreach (var clip in (IEnumerable)Get(sequence, "Clips")) result += JsonUtility.ToJson(clip);
            return result;
        }
        [UnityTest]
        public IEnumerator FailureGradeFitsAndRetryCountdownClearsPreviousDeath()
        {
            yield return ImportAligned(); var view = Get(ui, "CustomSong");
            Click(view, "Generate"); yield return Wait(() => ((Button)Get(view, "StartBattle")).interactable);
            Click(view, "StartBattle"); Component manager = null;
            yield return Wait(() =>
            {
                manager = UnityEngine.Object.FindObjectOfType(Type.GetType("TapTap.Danmaku.BattleManager, Assembly-CSharp", true)) as Component;
                return manager && Get(manager, "Simulation") != null;
            });
            var actor = Get(Get(manager, "Simulation"), "Player"); var health = Get(actor, "Health");
            var zero = health.GetType().GetField("Zero", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            actor.GetType().GetProperty("Health").SetValue(actor, zero);
            yield return Wait(() => Get(ui, "Context").ToString() == "Result");
            var grade = (TMP_Text)Get(Get(ui, "Result"), "Grade"); grade.ForceMeshUpdate();
            Assert.AreEqual("未完成", grade.text); Assert.AreEqual(1, grade.textInfo.lineCount);
            CaptureUI("custom-song-failure");
            Click(Get(ui, "Result"), "Retry");
            yield return Wait(() => Get(root, "LastResult") == null && Get(ui, "Context").ToString() == "Gameplay");
            var hud = Get(ui, "Hud"); Assert.AreEqual("", ((TMP_Text)Get(hud, "Warning")).text);
            Assert.AreEqual(5, hud.GetType().GetField("previousHealth", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud));
            CaptureUI("custom-song-retry-countdown");
            Assert.IsTrue((bool)Get(Call(Get(root, "Scenes"), "AbandonToMusic"), "Success"));
            yield return Wait(() => Get(ui, "Context").ToString() == "ImportedMusic");
        }
        [Serializable] private sealed class ClipSnapshot { public string Json; }
        private void CaptureUI(string name)
        {
            // A render of the actual live uGUI, independent of Game view focus.
            var canvas = ((Component)ui).GetComponent<Canvas>();
            var go = new GameObject("CustomSongUICapture"); var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .15f, .2f); camera.orthographic = true;
            var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
            var mode = canvas.renderMode; var priorCamera = canvas.worldCamera; var distance = canvas.planeDistance;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply(); RenderTexture.active = old;
            Directory.CreateDirectory("Logs/SpotlightScreenshots"); File.WriteAllBytes("Logs/SpotlightScreenshots/" + name + ".png", texture.EncodeToPNG());
            canvas.renderMode = mode; canvas.worldCamera = priorCamera; canvas.planeDistance = distance;
            UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(go);
        }
        private static void WriteWav(string path)
        {
            const int rate = 22050, seconds = 8, frames = rate * seconds;
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
            for (int i = 0; i < frames; i++)
            {
                double t = (double)i / rate, phase = t % .5, envelope = Math.Exp(-phase * 18);
                double frequency = new[] { 100.0, 800.0, 4500.0 }[(int)(t * 2) % 3];
                writer.Write((short)(Math.Sin(2 * Math.PI * frequency * t) * envelope * 12000));
            }
        }
    }
}
#endif
