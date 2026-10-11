using System;
using TapTap.Systems.Music;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class MusicPreview : MonoBehaviour
    {
        private AudioSource music;
        private readonly AudioSource[] clicks = new AudioSource[2];
        private AudioClip clickClip;
        private AudioVolumeService volumes;
        private double startDsp, origin, pausedPosition, bpm = 120, firstBeat;
        private long nextBeat;
        private int clickSlot;
        public AudioClip Clip => music ? music.clip : null;
        public bool Playing { get; private set; }
        public bool Metronome { get; private set; }
        public double Duration => Clip ? (double)Clip.samples / Clip.frequency : 0;
        public double Position => Playing ? Math.Min(Duration, origin + Math.Max(0, AudioSettings.dspTime - startDsp)) : pausedPosition;
        public void Bind(AudioVolumeService service)
        {
            volumes = service; music = MakeSource("ImportedSongPreview", AudioChannel.Music, 1);
            for (int i = 0; i < clicks.Length; i++) clicks[i] = MakeSource("AlignmentClick" + i, AudioChannel.Effects, .45f);
            const int rate = 48000; var samples = new float[rate / 50];
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(Math.Sin(i * 2 * Math.PI * 1600 / rate) * Math.Exp(-i / (rate * .003)) * .7);
            clickClip = AudioClip.Create("Alignment metronome", samples.Length, 1, rate, false); clickClip.SetData(samples, 0);
            foreach (var source in clicks) source.clip = clickClip;
        }
        private AudioSource MakeSource(string label, AudioChannel channel, float volume)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = false;
            source.spatialBlend = 0; source.volume = volume; volumes.Register(source, channel); return source;
        }
        public void SetClip(AudioClip clip) { Clear(); music.clip = clip; }
        public void Configure(double value, double offset, bool enabled)
        {
            bpm = value; firstBeat = offset; Metronome = enabled;
            foreach (var source in clicks) source.Stop();
            if (Playing) ResetNextBeat(Position);
        }
        public void Play()
        {
            if (!Clip) return;
            if (pausedPosition >= Duration - .001) pausedPosition = 0;
            origin = pausedPosition; startDsp = AudioSettings.dspTime + .1;
            music.timeSamples = Math.Min(Clip.samples - 1, (int)Math.Round(origin * Clip.frequency));
            music.PlayScheduled(startDsp); Playing = true; ResetNextBeat(origin);
            ScheduleClicks();
        }
        public void Pause()
        {
            pausedPosition = Position; Playing = false;
            if (music) music.Stop(); foreach (var source in clicks) if (source) source.Stop();
        }
        public void Seek(double seconds)
        {
            bool resume = Playing; Pause(); pausedPosition = Math.Max(0, Math.Min(Duration, seconds)); if (resume) Play();
        }
        private void ResetNextBeat(double position) => nextBeat = Math.Max(0, (long)Math.Ceiling((position - firstBeat) * bpm / 60 - 1e-8));
        private void ScheduleClicks()
        {
            if (!Playing || !Metronome || !ImportedMusicLibrary.ValidAlignment(bpm, firstBeat, Duration)) return;
            double now = AudioSettings.dspTime;
            for (int count = 0; count < 8; count++)
            {
                double songTime = firstBeat + nextBeat * 60 / bpm;
                double scheduled = startDsp + songTime - origin;
                if (songTime >= Duration || scheduled > now + .2) break;
                nextBeat++;
                // If a frame stalls, skip an expired click instead of emitting a burst.
                if (scheduled < now + .015) continue;
                clicks[clickSlot++ % clicks.Length].PlayScheduled(scheduled);
            }
        }
        private void Update()
        {
            if (!Playing) return;
            if (Position >= Duration) { Pause(); pausedPosition = Duration; }
            else ScheduleClicks();
        }
        public void Clear()
        {
            Pause(); pausedPosition = 0;
            if (music && music.clip) { var clip = music.clip; music.clip = null; Destroy(clip); }
        }
        private void OnApplicationFocus(bool focused) { if (!focused) Pause(); }
        private void OnDisable() { if (music) Clear(); }
        private void OnDestroy()
        {
            Clear(); if (volumes != null) { if (music) volumes.Unregister(music); foreach (var source in clicks) if (source) volumes.Unregister(source); }
            if (clickClip) Destroy(clickClip);
        }
    }
}
