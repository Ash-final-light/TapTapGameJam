using System;
using UnityEngine;

namespace TapTap.MusicPresentation
{
    /// <summary>Build topology once, then morph and sample a closed XY curve without frame allocations.</summary>
    public sealed class MusicCurveGeometry
    {
        public const int MaxControlPoints = 512, MaxRenderPoints = 4096;
        private readonly Vector3[] baseline, target, morphed;
        private readonly int[] bands;
        public int ControlPointCount => baseline.Length;

        public MusicCurveGeometry(int sides, float radius, int generations, AnimationCurve generator, bool outwards)
        {
            if (sides < 3 || sides > 64 || radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentException("初始边数必须为 3–64，半径必须为有限正数。");
            if (generations < 0 || generations > 3) throw new ArgumentException("分形代数必须为 0–3。");
            var keys = generator?.keys;
            if (generations > 0)
            {
                if (keys == null || keys.Length < 3 || Math.Abs(keys[0].time) > 0.00001 || Math.Abs(keys[keys.Length - 1].time - 1) > 0.00001)
                    throw new ArgumentException("生成曲线至少需要 3 个关键点，首尾时间为 0 和 1。");
                if (Math.Abs(keys[0].value) > 0.00001 || Math.Abs(keys[keys.Length - 1].value) > 0.00001)
                    throw new ArgumentException("生成曲线首尾值必须为 0，以保持相邻边连接。");
                for (int i = 0; i < keys.Length; i++)
                    if (float.IsNaN(keys[i].value) || float.IsInfinity(keys[i].value) || float.IsNaN(keys[i].time)
                        || float.IsInfinity(keys[i].time) || (i > 0 && keys[i].time <= keys[i - 1].time))
                        throw new ArgumentException("关键点必须有限，时间必须严格递增。");
            }
            int count = sides;
            for (int i = 0; i < generations; i++)
            {
                if (keys.Length - 1 > MaxControlPoints / count)
                    throw new ArgumentException("分形超过 512 个控制点，请减少边数、关键点或代数。");
                count *= keys.Length - 1;
            }
            var current = new Vector3[sides];
            var currentBands = new int[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                current[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
                currentBands[i] = i % MusicSpectrumEnvelope.BandCount;
            }
            Vector3[] finalBase = current;
            for (int generation = 0; generation < generations; generation++)
            {
                int nextCount = current.Length * (keys.Length - 1);
                var next = new Vector3[nextCount]; finalBase = new Vector3[nextCount];
                var nextBands = new int[nextCount];
                int index = 0;
                for (int edge = 0; edge < current.Length; edge++)
                {
                    Vector3 start = current[edge], end = current[(edge + 1) % current.Length];
                    Vector3 difference = end - start;
                    Vector3 normal = new Vector3(difference.y, -difference.x, 0) * (outwards ? 1 : -1);
                    for (int key = 0; key < keys.Length - 1; key++, index++)
                    {
                        finalBase[index] = Vector3.LerpUnclamped(start, end, keys[key].time);
                        next[index] = finalBase[index] + normal * keys[key].value;
                        nextBands[index] = currentBands[edge];
                    }
                }
                current = next; currentBands = nextBands;
            }
            baseline = finalBase; target = current; bands = currentBands;
            morphed = new Vector3[baseline.Length];
        }

        public int RenderPointCount(int samplesPerSegment)
        {
            if (samplesPerSegment < 1 || samplesPerSegment > MaxRenderPoints / ControlPointCount)
                throw new ArgumentException("曲线采样数必须为正，最终顶点不能超过 4096。");
            return ControlPointCount * samplesPerSegment;
        }

        public void Fill(Vector3[] output, int samplesPerSegment, MusicSpectrumEnvelope envelope,
            float deformation, float radialScale, float rotationDegrees)
        {
            if (output == null || output.Length != RenderPointCount(samplesPerSegment))
                throw new ArgumentException("输出缓存长度与采样数不匹配。");
            Quaternion rotation = Quaternion.AngleAxis(rotationDegrees, Vector3.forward);
            for (int i = 0; i < morphed.Length; i++)
                morphed[i] = rotation * (Vector3.LerpUnclamped(baseline[i], target[i],
                    Mathf.Clamp01((envelope == null ? 0 : envelope[bands[i]]) * deformation)) * radialScale);
            int index = 0, count = morphed.Length;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = morphed[(i + count - 1) % count], b = morphed[i];
                Vector3 c = morphed[(i + 1) % count], d = morphed[(i + 2) % count];
                for (int sample = 0; sample < samplesPerSegment; sample++)
                {
                    float t = (float)sample / samplesPerSegment;
                    output[index++] = 0.5f * ((2 * b) + (c - a) * t +
                        (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
                }
            }
        }
    }
}
