#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Spotlight.Tests
{
    public sealed class AudioWaveformTests
    {
        private GameObject canvasObject, eventObject;
        private UnityEngine.UI.MaskableGraphic waveform;
        private EventSystem events;

        [SetUp]
        public void Setup()
        {
            // Match the importer: construct a custom Graphic under an inactive UI.
            canvasObject = new GameObject("WaveformTestCanvas", typeof(RectTransform));
            canvasObject.SetActive(false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var waveObject = new GameObject("Waveform", typeof(RectTransform));
            var rect = (RectTransform)waveObject.transform;
            rect.SetParent(canvasObject.transform, false);
            rect.sizeDelta = new Vector2(600, 250);
            var type = Type.GetType("TapTap.Systems.UI.AudioWaveformGraphic, Assembly-CSharp", true);
            waveform = (UnityEngine.UI.MaskableGraphic)waveObject.AddComponent(type);
            waveform.raycastTarget = true;
            eventObject = new GameObject("WaveformTestEvents", typeof(EventSystem));
            events = eventObject.GetComponent<EventSystem>();
        }

        [TearDown]
        public void Teardown()
        {
            UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(eventObject);
        }

        [Test]
        public void InactiveCreationIncludesRendererBeforeAnyGraphicGetter()
        {
            // Do not access Graphic.canvasRenderer: its lazy getter can hide this bug in a player.
            Assert.IsNotNull(waveform.GetComponent<CanvasRenderer>(),
                "Adding AudioWaveformGraphic must create CanvasRenderer while the UI is still inactive.");
        }

        [UnityTest]
        public IEnumerator RaycastWorksDuringLoadingAfterPopulationAndReopen()
        {
            canvasObject.SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Raycast(); // Empty waveform while the imported audio is loading must also be safe.

            Set("Peaks", new[] { .25f, .8f, .5f, 1f });
            Set("Duration", 20d);
            Set("WindowStart", 0d);
            Set("WindowEnd", 20d);
            waveform.SetVerticesDirty();
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertHitsAndClicks();

            canvasObject.SetActive(false);
            yield return null;
            canvasObject.SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertHitsAndClicks();
            LogAssert.NoUnexpectedReceived();
        }

        private void Set(string field, object value) => waveform.GetType().GetField(field).SetValue(waveform, value);

        private PointerEventData Pointer() => new PointerEventData(events)
        {
            position = RectTransformUtility.WorldToScreenPoint(null,
                waveform.rectTransform.TransformPoint(waveform.rectTransform.rect.center))
        };

        private List<RaycastResult> Raycast()
        {
            var results = new List<RaycastResult>();
            events.RaycastAll(Pointer(), results);
            return results;
        }

        private void AssertHitsAndClicks()
        {
            var results = Raycast();
            Assert.IsNotEmpty(results);
            Assert.AreSame(waveform.gameObject, results[0].gameObject,
                "The real uGUI raycaster must resolve the waveform as the pointer target.");
            double selected = -1;
            Set("Clicked", new Action<double>(time => selected = time));
            var pointer = Pointer();
            pointer.pointerPressRaycast = results[0];
            ExecuteEvents.Execute(results[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(selected, Is.EqualTo(10d).Within(.001d));
        }
    }
}
#endif
