#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;

namespace Spotlight.Tests
{
    public sealed class WindowedDisplayTests
    {
        [Test]
        public void SavedLargeWindowRemainsVisibleAfterMovingToSmallerDesktop()
        {
            var method = Type.GetType("TapTap.Systems.UnityDisplaySettings, Assembly-CSharp", true)
                .GetMethod("FitWindowedResolution");
            Vector2Int Fit(int width, int height, int desktopWidth, int desktopHeight) =>
                (Vector2Int)method.Invoke(null, new object[] { width, height, desktopWidth, desktopHeight });
            var size = Fit(2560, 1440, 1920, 1080);
            Assert.LessOrEqual(size.x, 1920 - 32); Assert.LessOrEqual(size.y, 1080 - 80);
            Assert.Greater(size.x, 1280); Assert.Greater(size.y, 720);
            Assert.That((float)size.x / size.y, Is.EqualTo(2560f / 1440).Within(.002f));
            Assert.AreEqual(new Vector2Int(1280, 720), Fit(1280, 720, 1920, 1080), "Already visible windows should keep their size.");
            Assert.AreEqual(new Vector2Int(2560, 1440), Fit(2560, 1440, 0, 0), "Unavailable display metrics must not collapse the window.");
        }
    }
}
#endif
