using System;
using TapTap.Danmaku;
using UnityEngine;

namespace TapTap.MusicPresentation
{
    [AddComponentMenu("TapTap/Music Presentation/Battle Music Presentation Source")]
    [DisallowMultipleComponent, DefaultExecutionOrder(500)]
    public sealed class BattleMusicPresentationSource : MonoBehaviour
    {
        [Tooltip("明确绑定战斗宿主；同一物体上的 BattleManager 可以自动绑定。")]
        public BattleManager Battle;
        [Range(64, 8192)] public int SpectrumSize = 1024;
        [Min(0)] public float Sensitivity = 1;
        [Min(0.000001f)] public float MinimumPeak = 0.001f;
        [Min(0.001f)] public float PeakReleaseSeconds = 3;
        [Min(0.001f)] public float AttackSeconds = 0.035f, ReleaseSeconds = 0.18f;
        public MusicSpectrumEnvelope Envelope { get; } = new();
        public double SongSeconds { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public int Revision { get; private set; }
        private AudioSource boundSource;
        private AudioClip boundClip;
        private float[] left, right;

        private void Awake()
        {
            if (!Battle) Battle = GetComponent<BattleManager>();
            if (!Battle) Debug.LogWarning("音乐表现数据源需要绑定 BattleManager。", this);
        }

        private void OnValidate()
        {
            SpectrumSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(SpectrumSize), 64, 8192);
        }

        private void LateUpdate()
        {
            var source = Battle ? Battle.MusicSource : null;
            var clip = source ? source.clip : null;
            if (boundSource != source || boundClip != clip) ResetBinding(source, clip);
            IsPlaying = Battle && source && clip && Battle.MusicPlaying;
            IsPaused = Battle && source && clip && Battle.MusicPaused;
            if (IsPaused || (Battle && source && clip && Battle.MusicWaitingForAudio)) return;
            if (!IsPlaying)
            {
                Envelope.Release(Time.unscaledDeltaTime, ReleaseSeconds);
                return;
            }
            double time = Math.Max(0, Battle.MusicSongSeconds);
            if (time + 0.001 < SongSeconds) ResetBinding(source, clip);
            SongSeconds = time;
            int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(SpectrumSize), 64, 8192);
            if (left == null || left.Length != size)
            {
                left = new float[size]; right = new float[size]; Envelope.Reset();
            }
            source.GetSpectrumData(left, 0, FFTWindow.Blackman);
            bool stereo = clip.channels > 1 && AudioSettings.speakerMode != AudioSpeakerMode.Mono;
            if (stereo) source.GetSpectrumData(right, 1, FFTWindow.Blackman);
            Envelope.Sample(left, stereo ? right : null, AudioSettings.outputSampleRate, Time.unscaledDeltaTime,
                Sensitivity, MinimumPeak, PeakReleaseSeconds, AttackSeconds, ReleaseSeconds);
        }

        private void ResetBinding(AudioSource source, AudioClip clip)
        {
            boundSource = source; boundClip = clip; SongSeconds = 0;
            Envelope.Reset(); Revision++;
            if (left != null) Array.Clear(left, 0, left.Length);
            if (right != null) Array.Clear(right, 0, right.Length);
        }

        private void OnDisable()
        {
            ResetBinding(null, null); IsPlaying = IsPaused = false;
        }
    }
}
