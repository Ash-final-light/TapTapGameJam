using System;
using System.Collections.Generic;
using Framework.FixedPoint;
using Framework.Physics.LightweightPhysics;

namespace TapTap.Danmaku
{
    /// <summary>
    /// Specialized enemy-bullet world: pairs bullets only with the small set of targets.
    /// Cost O(body slots + bullets * targets), never enemy bullet vs enemy bullet.
    /// Intended for the fixed-player prototype; a multi-enemy world needs its own index.
    /// </summary>
    public sealed class BulletTargetBroadphase : IBroadphase
    {
        private readonly List<int> targets = new(8);
        private readonly List<LWPair> pairs = new(256);
        private readonly ushort bulletLayer, targetLayer;
        private static readonly Comparison<LWPair> PairComparison = ComparePairs;
        public List<LWPair> Pairs => pairs;
        public int CandidateChecks { get; private set; }

        public BulletTargetBroadphase(ushort bulletLayer = 1, ushort targetLayer = 2)
        { this.bulletLayer = bulletLayer; this.targetLayer = targetLayer; }

        public void Build(LWBody[] bodies, int capacity)
        {
            targets.Clear(); pairs.Clear(); CandidateChecks = 0;
            for (int i = 0; i < capacity; i++)
                if ((bodies[i].Flags & LWBodyFlags.Alive) != 0 && (bodies[i].Layer & targetLayer) != 0)
                    targets.Add(i);
            for (int i = 0; i < capacity; i++)
            {
                ref LWBody a = ref bodies[i];
                if ((a.Flags & LWBodyFlags.Alive) == 0 || (a.Layer & bulletLayer) == 0) continue;
                for (int t = 0; t < targets.Count; t++)
                {
                    int j = targets[t]; ref LWBody b = ref bodies[j];
                    if (!LWLayerMask.ShouldCollide(a.Layer, a.Mask, b.Layer, b.Mask)) continue;
                    CandidateChecks++;
                    FPVector2 extentA = a.Shape.Type == LWShapeType.Circle
                        ? new FPVector2(a.Shape.Radius, a.Shape.Radius) : a.Shape.HalfExtents;
                    FPVector2 extentB = b.Shape.Type == LWShapeType.Circle
                        ? new FPVector2(b.Shape.Radius, b.Shape.Radius) : b.Shape.HalfExtents;
                    if (FPMath.Abs(a.Position.X - b.Position.X) > extentA.X + extentB.X ||
                        FPMath.Abs(a.Position.Y - b.Position.Y) > extentA.Y + extentB.Y) continue;
                    pairs.Add(i < j ? new LWPair(i, j) : new LWPair(j, i));
                }
            }
            // Stable order even with recycled slots or multiple targets.
            pairs.Sort(PairComparison);
        }

        private static int ComparePairs(LWPair a, LWPair b)
        {
            int c = a.IndexA.CompareTo(b.IndexA);
            return c != 0 ? c : a.IndexB.CompareTo(b.IndexB);
        }
    }
}
