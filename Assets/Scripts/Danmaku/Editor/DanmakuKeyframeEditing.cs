using System;
using System.Linq;
namespace TapTap.Danmaku.Editor
{
    internal static class DanmakuKeyframeEditing
    {
        internal static bool CanAdd(DanmakuClip clip, DanmakuKeyframeType type)
            => type == DanmakuKeyframeType.Emit
                ? !clip.IsSplit || !clip.Keyframes.Any(k => k.Type == type)
                : clip.IsSplit && !clip.Keyframes.Any(k => k.Type == type);

        internal static DanmakuKeyframe Add(DanmakuClip clip, DanmakuKeyframeType type, int localTick)
        {
            if (!CanAdd(clip, type)) throw new InvalidOperationException("该片段不能再添加此类关键帧。");
            var key = new DanmakuKeyframe { Type = type, LocalTick = Math.Max(0, Math.Min(clip.DurationTicks - 1, localTick)) };
            clip.Keyframes.Add(key); return key;
        }

        internal static void Generate(DanmakuClip clip, int start, int interval, int count)
        {
            if (clip.IsSplit) throw new InvalidOperationException("子母弹不能批量发射。");
            if (start < 0 || interval < 1 || count < 1 || (long)start + (long)(count - 1) * interval >= clip.DurationTicks)
                throw new ArgumentException("生成范围必须位于片段内，间隔和数量至少为 1。");
            for (int i = 0; i < count; i++)
            {
                int tick = start + i * interval;
                if (!clip.Keyframes.Any(k => k.Type == DanmakuKeyframeType.Emit && k.LocalTick == tick))
                    Add(clip, DanmakuKeyframeType.Emit, tick);
            }
        }
    }
}
