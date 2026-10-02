// BruteForceBroadphase.cs
//
// N^2 broadphase. Skips both-static and both-sleeping pairs to keep the
// dispatch list small in typical resting scenes.

using System.Collections.Generic;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// O(N²) 暴力 broadphase。提前剔除"双静态/双休眠/Layer 互不可见"的对，
    /// 在常见的静止场景里 pair 数会显著少于 N²。
    /// </summary>
    public sealed class BruteForceBroadphase : IBroadphase
    {
        /// <summary>本次 Build 产生的候选对。</summary>
        private readonly List<LWPair> mPairs = new();

        /// <inheritdoc/>
        public List<LWPair> Pairs => mPairs;

        /// <inheritdoc/>
        public void Build(LWBody[] bodies, int capacity)
        {
            mPairs.Clear();
            for (int i = 0; i < capacity; i++)
            {
                LWBodyFlags fa = bodies[i].Flags;
                if ((fa & LWBodyFlags.Alive) == 0) continue;
                for (int j = i + 1; j < capacity; j++)
                {
                    LWBodyFlags fb = bodies[j].Flags;
                    if ((fb & LWBodyFlags.Alive) == 0) continue;

                    if ((fa & LWBodyFlags.Static) != 0 && (fb & LWBodyFlags.Static) != 0)
                        continue;
                    if ((fa & LWBodyFlags.Sleeping) != 0 && (fb & LWBodyFlags.Sleeping) != 0)
                        continue;
                    if (!LWLayerMask.ShouldCollide(bodies[i].Layer, bodies[i].Mask,
                                                    bodies[j].Layer, bodies[j].Mask))
                        continue;

                    mPairs.Add(new LWPair(i, j));
                }
            }
        }
    }
}
