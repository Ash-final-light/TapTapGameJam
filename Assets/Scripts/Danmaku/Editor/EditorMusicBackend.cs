using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    public sealed class EditorMusicBackend : IMusicPlaybackBackend
    {
        private static readonly Type AudioUtil = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
        private static MethodInfo Method(string name) => AudioUtil?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo PlayMethod = Method("PlayPreviewClip"), StopMethod = Method("StopAllPreviewClips"),
            PauseMethod = Method("PausePreviewClip"), ResumeMethod = Method("ResumePreviewClip"),
            PlayingMethod = Method("IsPreviewClipPlaying"), PositionMethod = Method("GetPreviewClipSamplePosition");
        public static bool Available => PlayMethod != null && StopMethod != null && PauseMethod != null && ResumeMethod != null && PlayingMethod != null && PositionMethod != null;
        private AudioClip clip;
        private bool launched, paused;
        public bool HasStarted => launched && !paused;
        public bool IsPlaying => launched && !paused && (bool)PlayingMethod.Invoke(null, null);
        public int SamplePosition => launched ? Math.Max(0, (int)PositionMethod.Invoke(null, null)) : 0;
        public void Play(AudioClip value, int sample)
        {
            if (!Available) throw new NotSupportedException("当前 Unity 的音频试听 API 不完整；请在 PlayMode 验证音乐播放。编辑器同步试听不能启用。");
            if (!value || !value.LoadAudioData()) throw new InvalidOperationException("音乐音频数据加载失败，不能开始试听。");
            Stop(); clip = value; PlayMethod.Invoke(null, new object[] { value, sample, false }); launched = true; paused = false;
        }
        public void Pause() { if (launched && !paused) { PauseMethod.Invoke(null, null); paused = true; } }
        public void Resume() { if (launched && paused) { ResumeMethod.Invoke(null, null); paused = false; } }
        public void Seek(int sample) { if (clip) Play(clip, sample); }
        public void Stop() { if (launched) StopMethod?.Invoke(null, null); launched = paused = false; }
        public void Dispose() => Stop();
    }
}
