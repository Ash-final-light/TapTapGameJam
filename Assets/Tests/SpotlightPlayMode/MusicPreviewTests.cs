#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Spotlight.Tests
{
    public sealed class MusicPreviewTests
    {
        private GameObject owner, other;
        private Component preview;
        private object audio;
        private static Type Runtime(string name) => Type.GetType("TapTap.Systems." + name + ", Assembly-CSharp", true);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        private static object Settings(int master, int music, int effects)
        {
            var data = Activator.CreateInstance(Runtime("SettingsData"));
            data.GetType().GetField("MasterVolume").SetValue(data, master);
            data.GetType().GetField("MusicVolume").SetValue(data, music);
            data.GetType().GetField("EffectsVolume").SetValue(data, effects);
            return data;
        }
        [SetUp]
        public void Setup()
        {
            owner = new GameObject("MetronomePreviewTest");
            preview = owner.AddComponent(Runtime("UI.MusicPreview"));
            audio = Activator.CreateInstance(Runtime("AudioVolumeService"));
            Call(preview, "Bind", audio);
            Call(preview, "SetClip", AudioClip.Create("Preview test silence", 48000 * 3, 1, 48000, false));
        }
        [TearDown]
        public void Teardown()
        {
            if (owner) UnityEngine.Object.DestroyImmediate(owner);
            if (other) UnityEngine.Object.DestroyImmediate(other);
        }
        [Test]
        public void MetronomeMixRestoresPreviewAndRespectsGlobalSettings()
        {
            var sources = owner.GetComponentsInChildren<AudioSource>();
            var music = sources.Single(source => source.name == "ImportedSongPreview");
            var click = sources.First(source => source.name.StartsWith("AlignmentClick"));
            other = new GameObject("Unrelated music"); var background = other.AddComponent<AudioSource>();
            background.playOnAwake = false; background.volume = .8f;
            Call(audio, "Register", background, Enum.Parse(Runtime("AudioChannel"), "Music"));
            Call(audio, "Apply", Settings(80, 50, 60));
            Call(preview, "Configure", 120d, 0d, true);
            Assert.AreEqual(.4f, music.volume, .0001f, "Paused preview must preserve the normal music volume.");
            Call(preview, "Play");
            Assert.AreEqual(.12f, music.volume, .0001f);
            Assert.AreEqual(.32f, background.volume, .0001f, "Other music sources must remain unchanged.");
            Assert.Greater(click.volume, .35f);
            Call(audio, "Apply", Settings(50, 40, 0));
            Assert.AreEqual(.06f, music.volume, .0001f, "Global volume updates must preserve the preview gain.");
            Assert.AreEqual(0, click.volume, "The metronome must obey Effects mute.");
            Call(preview, "Configure", 120d, 0d, false);
            Assert.AreEqual(.2f, music.volume, .0001f, "Disabling the metronome restores the current channel volume.");
            Call(preview, "Configure", 120d, 0d, true); Call(preview, "Pause");
            Assert.AreEqual(.2f, music.volume, .0001f);
            Call(preview, "Play"); owner.SetActive(false);
            Assert.AreEqual(.2f, music.volume, .0001f, "Leaving the page restores the gain and stops playback.");
            Assert.IsFalse((bool)preview.GetType().GetProperty("Playing").GetValue(preview));
            Assert.IsFalse((bool)preview.GetType().GetProperty("Metronome").GetValue(preview));
            Assert.AreEqual(.16f, background.volume, .0001f);
        }
        [Test]
        public void ClickHasAudibleBodyAndSmoothEdges()
        {
            var clip = (AudioClip)preview.GetType().GetField("clickClip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview);
            var samples = new float[clip.samples]; Assert.IsTrue(clip.GetData(samples, 0));
            Assert.GreaterOrEqual(clip.length, .04f);
            Assert.Greater(Math.Sqrt(samples.Average(sample => (double)sample * sample)), .12);
            Assert.LessOrEqual(samples.Max(sample => Math.Abs(sample)), .8f);
            Assert.AreEqual(0, samples[0], .0001f);
            Assert.Less(Math.Abs(samples[samples.Length - 1]), .02f);
        }
    }
}
#endif
