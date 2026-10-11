using System;
using UnityEngine;

namespace TapTap.MusicPresentation
{
    public enum MusicUIResponse { Auto, Spectrum, Beat }
    public enum MusicUIBand { Low, Mid, High, Amplitude }

    /// <summary>Music-driven UI motion. Put this on the visual child of a stable layout/button slot.</summary>
    [AddComponentMenu("TapTap/Music Presentation/Music Reactive UI")]
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform)), DefaultExecutionOrder(650)]
    public sealed class MusicReactiveUI : MonoBehaviour
    {
        public BattleMusicPresentationSource Source;
        [Tooltip("留空时动画作用于本物体。建议指定布局槽内部的图标，保持外层布局与点击范围稳定。")]
        public RectTransform Target;
        [Tooltip("Auto：有 BPM 时按拍跳动，否则跟随频谱。Spectrum：始终使用频谱。Beat：需要有效 BPM。")]
        public MusicUIResponse Response = MusicUIResponse.Auto;
        public MusicUIBand Band = MusicUIBand.Low;
        [Header("节拍配置")]
        [Tooltip("0 表示读取当前 Sequence 的 ManualMusicBpm；正数覆盖它。")]
        [Min(0)] public float BpmOverride;
        [Tooltip("BpmOverride 为正时使用的首拍歌曲时间；否则读取 Sequence 的手工首拍偏移。")]
        [Min(0)] public float FirstBeatSeconds;
        [Range(0.02f, 1)] public float BeatDecay = 0.2f;
        [Tooltip("无 BPM 的频谱模式下，每秒完成的左右摆动周期数。")]
        [Min(0)] public float SpectrumSwayFrequency = 1;
        [Header("动画幅度")]
        [Range(0, 0.5f)] public float ScaleAmount = 0.1f;
        [Range(0, 30)] public float RotationDegrees = 6;
        [Tooltip("X 为左右位移幅度，Y 为向上跳动幅度，单位为 RectTransform 的 UI 单位。")]
        public Vector2 PositionAmount = new(4, 5);
        [Min(0.001f)] public float AttackSeconds = 0.015f, ReleaseSeconds = 0.12f;
        private RectTransform boundTarget;
        private BattleMusicPresentationSource boundSource;
        private Vector2 basePosition;
        private Quaternion baseRotation;
        private Vector3 baseScale;
        private float pulse, sway;
        private int sourceRevision = -1;

        private void OnEnable()
        {
            if (!Source) Source = GetComponentInParent<BattleMusicPresentationSource>();
            if (!Target) Target = GetComponent<RectTransform>();
            CaptureTarget(); boundSource = Source; sourceRevision = -1;
        }

        /// <summary>Use this after a HUD prefab is instantiated to bind the current battle's shared source.</summary>
        public void Bind(BattleMusicPresentationSource source)
        {
            Source = source; boundSource = source; sourceRevision = -1;
            pulse = sway = 0; RestorePose();
        }

        public void SetTarget(RectTransform target)
        {
            RestorePose(); Target = target ? target : GetComponent<RectTransform>(); CaptureTarget();
        }

        private void LateUpdate()
        {
            if (!Target) Target = GetComponent<RectTransform>();
            if (boundTarget != Target)
            { RestorePose(); CaptureTarget(); }
            if (!boundTarget) return;
            bool available = Source && Source.isActiveAndEnabled;
            if (boundSource != Source || (available && sourceRevision != Source.Revision))
            {
                boundSource = Source; sourceRevision = available ? Source.Revision : -1;
                pulse = sway = 0;
            }
            if (available && (Source.IsPaused || (Source.Battle && Source.Battle.MusicWaitingForAudio))) return;
            float targetPulse = 0, targetSway = 0;
            if (available && Source.IsPlaying)
            {
                var sequence = Source.Battle && Source.Battle.Config ? Source.Battle.Config.Sequence : null;
                float bpm = BpmOverride > 0 ? BpmOverride : sequence ? sequence.ManualMusicBpm : 0;
                float firstBeat = BpmOverride > 0 ? FirstBeatSeconds : sequence ? sequence.ManualMusicBeatOffsetSeconds : 0;
                bool useBeat = Response == MusicUIResponse.Beat || (Response == MusicUIResponse.Auto && bpm > 0);
                if (useBeat)
                    EvaluateBeat(Source.SongSeconds, bpm, firstBeat, BeatDecay, out targetPulse, out targetSway);
                else
                {
                    targetPulse = Band == MusicUIBand.Amplitude ? Source.Envelope.Amplitude : Source.Envelope[(int)Band];
                    targetSway = (float)Math.Sin(Source.SongSeconds * Math.PI * 2 * SpectrumSwayFrequency) * targetPulse;
                }
            }
            float dt = Time.unscaledDeltaTime;
            pulse = MusicSpectrumEnvelope.Smooth(pulse, targetPulse, dt, targetPulse > pulse ? AttackSeconds : ReleaseSeconds);
            float tau = Mathf.Max(0.001f, Mathf.Abs(targetSway) > Mathf.Abs(sway) ? AttackSeconds : ReleaseSeconds);
            sway = Mathf.Lerp(sway, targetSway, 1 - Mathf.Exp(-dt / tau));
            ApplyPose(pulse, sway);
        }

        public static void EvaluateBeat(double songSeconds, float bpm, float firstBeatSeconds, float decay,
            out float beatPulse, out float beatSway)
        {
            beatPulse = beatSway = 0;
            if (double.IsNaN(songSeconds) || double.IsInfinity(songSeconds) || !Finite(bpm) || bpm <= 0 ||
                !Finite(firstBeatSeconds) || !Finite(decay) || songSeconds < firstBeatSeconds) return;
            double beat = (songSeconds - firstBeatSeconds) * bpm / 60;
            double phase = beat - Math.Floor(beat);
            beatPulse = (float)Math.Exp(-phase / Math.Max(0.001, decay));
            beatSway = (float)-Math.Cos(Math.PI * (beat % 2));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void CaptureTarget()
        {
            boundTarget = Target; pulse = sway = 0;
            if (!boundTarget) return;
            basePosition = boundTarget.anchoredPosition;
            baseRotation = boundTarget.localRotation;
            baseScale = boundTarget.localScale;
        }

        private void ApplyPose(float strength, float swing)
        {
            boundTarget.anchoredPosition = basePosition + new Vector2(PositionAmount.x * swing, PositionAmount.y * strength);
            boundTarget.localRotation = baseRotation * Quaternion.Euler(0, 0, RotationDegrees * swing);
            boundTarget.localScale = baseScale * (1 + ScaleAmount * strength);
        }

        private void RestorePose()
        {
            if (!boundTarget) return;
            boundTarget.anchoredPosition = basePosition;
            boundTarget.localRotation = baseRotation;
            boundTarget.localScale = baseScale;
        }

        private void OnDisable() { RestorePose(); pulse = sway = 0; }
    }
}
