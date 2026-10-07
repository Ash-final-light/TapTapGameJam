using UnityEngine;
namespace TapTap.Danmaku
{
    public sealed class RuntimeMusicBackend : IMusicPlaybackBackend
    {
        private readonly AudioSource source;
        private double startDsp;
        private bool launched, paused;
        private int savedSample;
        public RuntimeMusicBackend(AudioSource source)
        { this.source = source; source.playOnAwake = false; source.loop = false; source.spatialBlend = 0; source.pitch = 1; }
        public bool HasStarted => launched && !paused && AudioSettings.dspTime >= startDsp;
        public bool IsPlaying => launched && !paused && (AudioSettings.dspTime < startDsp || source.isPlaying);
        public int SamplePosition => source.timeSamples;
        public void Play(AudioClip clip, int startSample)
        {
            if (!clip || !clip.LoadAudioData()) throw new System.InvalidOperationException("音乐音频数据加载失败。");
            source.Stop(); source.clip = clip; source.timeSamples = startSample;
            startDsp = AudioSettings.dspTime + 0.1; source.PlayScheduled(startDsp); launched = true; paused = false;
        }
        public void Pause()
        { savedSample = source.timeSamples; source.Stop(); paused = true; }
        public void Resume() { if (source.clip) Play(source.clip, savedSample); }
        public void Seek(int sample) { if (source.clip) Play(source.clip, sample); }
        public void Stop() { source.Stop(); launched = paused = false; }
        public void Dispose() => Stop();
    }
}
