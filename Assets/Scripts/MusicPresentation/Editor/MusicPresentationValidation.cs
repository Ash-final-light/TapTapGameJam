using System;
using System.IO;
using System.Reflection;
using TapTap.Danmaku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.MusicPresentation.Editor
{
    public static class MusicPresentationValidation
    {
        [MenuItem("Tools/Music Presentation/运行音乐背景验证")]
        public static void Run()
        {
            ValidateEnvelope(); ValidateGeometry(); ValidateTransport(); ValidateRenderer(); ValidateUI(); ValidateHudBinding();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MusicPresentationValidation.txt",
                "PASS: silence/non-finite input; stereo magnitudes; 44.1/48 kHz bands; frame-independent smoothing; " +
                "closed geometry; morph; point budget; scheduled playback/pause/offset/end; URP shader and LineRenderer; " +
                "UI beat timing/offset/silence; absolute pose/no scale drift; pause freezes pose; target replacement; disable restores pose; " +
                "inactive HUD binding; repeated bind preserves animation; battle handoff; detach restores pose.\n");
            Debug.Log("Music presentation validation passed.");
        }

        private static void ValidateEnvelope()
        {
            const int size = 1024;
            var left = new float[size]; var right = new float[size];
            var envelope = new MusicSpectrumEnvelope();
            envelope.Sample(left, right, 48000, 1, 1, 0.001f, 3, 0.035f, 0.18f);
            Assert(envelope.Amplitude == 0, "静音产生幅值。");
            foreach (int rate in new[] { 44100, 48000 })
            {
                int[] frequencies = { 100, 1000, 6000 };
                for (int band = 0; band < 3; band++)
                {
                    Array.Clear(left, 0, size); Array.Clear(right, 0, size); envelope.Reset();
                    int bin = Mathf.RoundToInt(frequencies[band] * 2f * size / rate);
                    // Only the right channel contains energy; magnitudes must be combined.
                    right[bin] = 1;
                    envelope.Sample(left, right, rate, 1, 1, 0.001f, 3, 0.035f, 0.18f);
                    for (int b = 0; b < 3; b++) Assert(b == band ? envelope[b] > 0.99f : envelope[b] == 0, "分频或右声道遗漏。");
                }
            }
            left[4] = float.NaN; right[4] = float.PositiveInfinity;
            envelope.Sample(left, right, 48000, 1, 1, 0.001f, 3, 0.035f, 0.18f);
            for (int b = 0; b < 3; b++) Assert(!float.IsNaN(envelope[b]) && envelope[b] >= 0 && envelope[b] <= 1, "包络出现无效值。");
            float a = 0, bValue = 0;
            for (int i = 0; i < 30; i++) a = MusicSpectrumEnvelope.Smooth(a, 1, 1f / 30, 0.2f);
            for (int i = 0; i < 120; i++) bValue = MusicSpectrumEnvelope.Smooth(bValue, 1, 1f / 120, 0.2f);
            Assert(Mathf.Abs(a - bValue) < 0.00001f, "平滑随帧率变化。");
            envelope.Release(10, 0.18f); Assert(envelope.Amplitude < 0.0001f, "结束后未回落。");
        }

        private static AnimationCurve Generator() => new(new Keyframe(0, 0), new Keyframe(1f / 3, 0),
            new Keyframe(0.5f, 0.288675f), new Keyframe(2f / 3, 0), new Keyframe(1, 0));

        private static void ValidateGeometry()
        {
            var polygon = new MusicCurveGeometry(7, 2, 0, null, true);
            var points = new Vector3[polygon.RenderPointCount(1)];
            polygon.Fill(points, 1, null, 1, 1, 0);
            float edge = Vector3.Distance(points[0], points[1]);
            for (int i = 0; i < points.Length; i++)
                Assert(Mathf.Abs(Vector3.Distance(points[i], points[(i + 1) % points.Length]) - edge) < 0.00001f, "七边形角步进或闭环有误。");
            var shape = new MusicCurveGeometry(6, 2, 2, Generator(), true);
            Assert(shape.ControlPointCount == 96, "分形拓扑数量有误。");
            points = new Vector3[shape.RenderPointCount(6)];
            shape.Fill(points, 6, null, 1, 1, 0);
            var baseline = (Vector3[])points.Clone();
            var spectrum = new float[1024]; for (int i = 0; i < spectrum.Length; i++) spectrum[i] = 1;
            var envelope = new MusicSpectrumEnvelope();
            envelope.Sample(spectrum, null, 48000, 1, 1, 0.001f, 3, 0.035f, 0.18f);
            shape.Fill(points, 6, envelope, 1, 1, 0);
            float moved = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Assert(!float.IsNaN(points[i].x) && !float.IsInfinity(points[i].x) && Mathf.Abs(points[i].z) < 0.00001f, "几何无效或离开 XY 平面。");
                moved += Vector3.Distance(points[i], baseline[i]);
            }
            Assert(moved > 1, "频谱未驱动曲线形变。");
            float seam = Vector3.Distance(points[points.Length - 1], points[0]);
            Assert(seam > 0 && seam < 0.5f, "闭环接缝缺失或重复点。");
            bool rejected = false;
            try { _ = new MusicCurveGeometry(64, 2, 3, Generator(), true); }
            catch (ArgumentException) { rejected = true; }
            Assert(rejected, "分形顶点上限未生效。");
            rejected = false;
            try { shape.RenderPointCount(100); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "渲染采样上限未生效。");
        }

        private sealed class Playback : IMusicPlaybackBackend
        {
            public bool IsPlaying { get; set; }
            public bool HasStarted { get; set; }
            public int SamplePosition { get; set; }
            public void Play(AudioClip clip, int startSample) { SamplePosition = startSample; IsPlaying = true; HasStarted = false; }
            public void Pause() { IsPlaying = HasStarted = false; }
            public void Resume() { IsPlaying = true; HasStarted = false; }
            public void Seek(int sample) { SamplePosition = sample; }
            public void Stop() { IsPlaying = HasStarted = false; }
            public void Dispose() => Stop();
        }

        private static void ValidateTransport()
        {
            var clip = AudioClip.Create("Presentation validation", 144000, 1, 48000, false);
            try
            {
                var backend = new Playback();
                using var transport = new MusicTimelineTransport(clip, -0.5, backend);
                transport.Play(); Assert(transport.WaitingForAudio && !transport.AudioPlaying, "DSP 预约被视为播放。");
                Assert(Math.Abs(transport.SongSeconds - 0.5) < 0.00001, "负偏移的歌曲时间错误。");
                backend.HasStarted = true; backend.SamplePosition = 48000; transport.Advance(0);
                Assert(transport.AudioPlaying && Math.Abs(transport.SongSeconds - 1) < 0.00001, "歌曲时间不跟随样本。");
                transport.Pause(); Assert(!transport.AudioPlaying && !transport.WaitingForAudio, "暂停状态错误。");
                transport.Advance(10); Assert(Math.Abs(transport.SongSeconds - 1) < 0.00001, "暂停时间漂移。");
                transport.Resume(); Assert(transport.WaitingForAudio, "恢复预约未等待。");
                backend.HasStarted = true; backend.IsPlaying = false; transport.Advance(0);
                Assert(!transport.AudioPlaying && !transport.WaitingForAudio, "歌曲结束仍输出播放状态。");
                using var delayed = new MusicTimelineTransport(clip, 1, new Playback());
                delayed.Play(); Assert(delayed.WaitingForAudio && delayed.SongSeconds == -1, "正偏移等待错误。");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }

        private static void ValidateRenderer()
        {
            var shader = Resources.Load<Shader>("BattleMusicLine");
            Assert(shader && !ShaderUtil.ShaderHasError(shader), "URP 曲线 Shader 未导入或编译失败。");
            // A preview scene prevents the check from dirtying the user's active scene.
            var scene = EditorSceneManager.NewPreviewScene();
            var obj = new GameObject("Music curve validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
            try
            {
                var curve = obj.AddComponent<BattleMusicBackgroundCurve>();
                InvokeLifecycle(curve, "Awake"); InvokeLifecycle(curve, "LateUpdate");
                var line = obj.GetComponent<LineRenderer>();
                Assert(line.enabled && line.loop && !line.useWorldSpace && line.positionCount == 144, "背景绘制组件未正确初始化。");
                Assert(line.sharedMaterial.shader == shader && line.sortingOrder == -100, "背景材质或排序错误。");
                var properties = new MaterialPropertyBlock(); line.GetPropertyBlock(properties);
                Assert(Mathf.Abs(properties.GetColor("_Tint").a - curve.Opacity) < 0.00001f, "背景透明度未传给 Shader。");
                curve.Generations = 3; curve.Sides = 64; curve.Rebuild();
                // Invalid settings are checked by the pure geometry test; no deliberate error log here.
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void ValidateUI()
        {
            MusicReactiveUI.EvaluateBeat(0.5, 120, 1, 0.2f, out float pulse, out float sway);
            Assert(pulse == 0 && sway == 0, "首拍之前 UI 提前跳动。");
            MusicReactiveUI.EvaluateBeat(1, 120, 1, 0.2f, out pulse, out sway);
            Assert(pulse == 1 && sway == -1, "首拍脉冲或向左摆动错误。");
            MusicReactiveUI.EvaluateBeat(1.5, 120, 1, 0.2f, out pulse, out sway);
            Assert(pulse == 1 && sway == 1, "相邻拍未切换向右。");
            MusicReactiveUI.EvaluateBeat(1.25, 120, 1, 0.2f, out pulse, out sway);
            Assert(pulse > 0 && pulse < 0.1f && Mathf.Abs(sway) < 0.00001f, "拍内回落有误。");
            MusicReactiveUI.EvaluateBeat(100, 0, 0, 0.2f, out pulse, out sway);
            Assert(pulse == 0 && sway == 0, "无有效 BPM 时生成拍点。");
            MusicReactiveUI.EvaluateBeat(double.NaN, 120, 0, 0.2f, out pulse, out sway);
            Assert(pulse == 0 && sway == 0, "异常时间产生无效姿态。");

            var scene = EditorSceneManager.NewPreviewScene();
            var obj = new GameObject("Reactive UI validation", typeof(RectTransform));
            var second = new GameObject("Replacement UI target", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(second, scene);
            try
            {
                var target = obj.GetComponent<RectTransform>();
                target.anchoredPosition = new Vector2(30, 40); target.localScale = new Vector3(2, 3, 1);
                var initialRotation = Quaternion.Euler(0, 0, 15); target.localRotation = initialRotation;
                var ui = obj.AddComponent<MusicReactiveUI>(); InvokeLifecycle(ui, "OnEnable");
                var apply = typeof(MusicReactiveUI).GetMethod("ApplyPose", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int i = 0; i < 100; i++) apply.Invoke(ui, new object[] { 1f, 1f });
                Assert(Vector3.Distance(target.localScale, new Vector3(2.2f, 3.3f, 1.1f)) < 0.00001f, "UI 缩放累积漂移。");
                Assert(Vector2.Distance(target.anchoredPosition, new Vector2(34, 45)) < 0.00001f, "UI 位移累积漂移。");
                Assert(Quaternion.Angle(target.localRotation, initialRotation * Quaternion.Euler(0, 0, 6)) < 0.001f, "UI 未保留基础旋转。");
                var source = obj.AddComponent<BattleMusicPresentationSource>();
                ui.Bind(source);
                typeof(BattleMusicPresentationSource).GetProperty("IsPaused").SetValue(source, true);
                apply.Invoke(ui, new object[] { 1f, 1f });
                var pausedPosition = target.anchoredPosition; var pausedScale = target.localScale;
                InvokeLifecycle(ui, "LateUpdate");
                Assert(target.anchoredPosition == pausedPosition && target.localScale == pausedScale, "暂停期间 UI 姿态变化。");
                ui.SetTarget(second.GetComponent<RectTransform>());
                Assert(Vector3.Distance(target.localScale, new Vector3(2, 3, 1)) < 0.00001f &&
                    Vector2.Distance(target.anchoredPosition, new Vector2(30, 40)) < 0.00001f, "更换目标未还原旧图标。");
                apply.Invoke(ui, new object[] { 1f, -1f }); InvokeLifecycle(ui, "OnDisable");
                Assert(second.transform.localScale == Vector3.one, "禁用未还原新图标。");
                Assert(second.GetComponent<RectTransform>().anchoredPosition == Vector2.zero, "禁用未还原位置。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(second);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void ValidateHudBinding()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var first = new GameObject("First battle music");
            var next = new GameObject("Next battle music");
            var hudObject = new GameObject("Hidden runtime HUD", typeof(RectTransform));
            foreach (var obj in new[] { first, next, hudObject })
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
            try
            {
                var hud = hudObject.AddComponent<TapTap.Systems.UI.HudView>();
                var iconObject = new GameObject("Portrait", typeof(RectTransform));
                iconObject.transform.SetParent(hudObject.transform, false);
                var icon = iconObject.AddComponent<MusicReactiveUI>(); InvokeLifecycle(icon, "OnEnable");
                hudObject.SetActive(false);
                var source = first.AddComponent<BattleMusicPresentationSource>();
                var binding = first.AddComponent<BattleMusicHudBinding>(); binding.Source = source;
                binding.BindHud(hud);
                Assert(icon.Source == source, "隐藏 HUD 的图标未绑定音乐源。");
                var apply = typeof(MusicReactiveUI).GetMethod("ApplyPose", BindingFlags.Instance | BindingFlags.NonPublic);
                apply.Invoke(icon, new object[] { 1f, 1f }); binding.BindHud(hud);
                Assert(Mathf.Abs(iconObject.transform.localScale.x - 1.1f) < 0.00001f, "重复绑定每帧复位动画。");
                var nextSource = next.AddComponent<BattleMusicPresentationSource>();
                var nextBinding = next.AddComponent<BattleMusicHudBinding>(); nextBinding.Source = nextSource;
                nextBinding.BindHud(hud); InvokeLifecycle(binding, "OnDisable");
                Assert(icon.Source == nextSource, "旧战斗解绑了新战斗音乐源。");
                apply.Invoke(icon, new object[] { 1f, 1f }); nextBinding.BindHud(null);
                Assert(!icon.Source && iconObject.transform.localScale == Vector3.one &&
                    iconObject.GetComponent<RectTransform>().anchoredPosition == Vector2.zero, "脱离战斗未还原图标。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(next);
                UnityEngine.Object.DestroyImmediate(hudObject); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void InvokeLifecycle(MonoBehaviour behaviour, string method)
            => behaviour.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);
    }
}
