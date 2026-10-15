using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Systems
{
    public enum AudioChannel { Music, Effects }
    public sealed class AudioVolumeService
    {
        private readonly Dictionary<AudioSource, (AudioChannel channel, float baseVolume, float gain)> sources = new();
        private SettingsData current = new();
        public float VolumeScale => current.VolumeScale;
        public event Action<float> ScaleChanged;
        public void Register(AudioSource source, AudioChannel channel)
        { if (!source || sources.ContainsKey(source)) return; sources[source] = (channel, source.volume, 1); Refresh(); }
        public void SetGain(AudioSource source, float gain)
        {
            if (!source || !sources.TryGetValue(source, out var entry)) return;
            entry.gain = Mathf.Clamp01(gain); sources[source] = entry; Refresh();
        }
        public void Unregister(AudioSource source)
        { if(sources.TryGetValue(source,out var entry) && source) source.volume=entry.baseVolume; sources.Remove(source); }
        public void Apply(SettingsData data)
        {
            bool changed = Math.Abs(data.VolumeScale - current.VolumeScale) > 0.00001f;
            current = data.Copy(); Refresh(); if (changed) ScaleChanged?.Invoke(current.VolumeScale);
        }
        private void Refresh()
        {
            foreach (var entry in sources) if (entry.Key)
                entry.Key.volume = entry.Value.baseVolume * entry.Value.gain * current.MasterVolume / 100f *
                    (entry.Value.channel == AudioChannel.Music ? current.MusicVolume : current.EffectsVolume) / 100f;
        }
    }
}
