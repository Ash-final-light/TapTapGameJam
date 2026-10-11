using System;
using UnityEngine;
namespace TapTap.Danmaku
{
    public interface IMusicPlaybackBackend : IDisposable
    {
        bool IsPlaying { get; }
        bool HasStarted { get; }
        int SamplePosition { get; }
        void Play(AudioClip clip, int startSample);
        void Pause();
        void Resume();
        void Seek(int sample);
        void Stop();
    }
    /// <summary>One logical transport shared by editor and runtime; audio is authoritative while it plays.</summary>
    public sealed class MusicTimelineTransport : IDisposable
    {
        private readonly IMusicPlaybackBackend backend;
        private readonly AudioClip clip;
        private readonly double offset;
        private bool audioLaunched, audioEnded;
        private double seconds, remainingDelay;
        public bool Playing { get; private set; }
        public double Seconds => seconds;
        public double SongSeconds => seconds - offset;
        public bool AudioPlaying => Playing && audioLaunched && !audioEnded && backend.HasStarted && backend.IsPlaying;
        public bool WaitingForAudio => Playing && clip && !audioEnded && (!audioLaunched || !backend.HasStarted);
        public bool AudioEnded => audioEnded;
        public int TargetTick => audioEnded ? MusicTime.EndTick(seconds) : MusicTime.CompletedTick(seconds);
        public MusicTimelineTransport(AudioClip clip, double offset, IMusicPlaybackBackend backend)
        { this.clip = clip; this.offset = offset; this.backend = backend; }
        public void Play() { Seek(0); Resume(); }
        public void Advance(double elapsed)
        {
            if (!Playing || double.IsNaN(elapsed) || double.IsInfinity(elapsed)) return;
            elapsed = Math.Max(0, elapsed);
            if (!clip || audioEnded) { seconds += elapsed; return; }
            if (!audioLaunched)
            {
                seconds += elapsed; remainingDelay = Math.Max(0, offset - seconds);
                if (remainingDelay <= 0) StartAudio();
                return;
            }
            if (!backend.HasStarted) return;
            if (backend.IsPlaying)
                seconds = Math.Max(seconds, offset + (double)backend.SamplePosition / clip.frequency);
            else { audioEnded = true; seconds = Math.Max(seconds, offset + (double)clip.samples / clip.frequency); }
        }
        private void StartAudio()
        {
            backend.Play(clip, MusicTime.SampleAt(seconds, offset, clip.frequency, clip.samples));
            audioLaunched = true;
        }
        public void Pause() { if (!Playing) return; Playing = false; if (audioLaunched && !audioEnded) backend.Pause(); }
        public void Resume()
        {
            if (Playing) return; Playing = true;
            if (!clip || audioEnded) return;
            if (audioLaunched) backend.Resume();
            else if (remainingDelay <= 0) StartAudio();
        }
        public void Seek(double battleSeconds)
        {
            Playing = false; backend.Stop(); seconds = Math.Max(0, battleSeconds);
            audioLaunched = false; remainingDelay = Math.Max(0, offset - seconds);
            audioEnded = clip && seconds - offset >= (double)clip.samples / clip.frequency;
        }
        public void Dispose() { Playing = false; backend.Dispose(); }
    }
}
