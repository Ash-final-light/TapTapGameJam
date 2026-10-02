// UniformGridBroadphase.cs
//
// Hashed uniform grid broadphase. Each step the alive bodies are inserted
// into the cells their AABB covers; pairs are emitted by walking each cell's
// occupants and deduplicating across multi-cell bodies. The pair list is
// sorted ascending by (IndexA, IndexB) so output matches BruteForceBroadphase
// for any equivalent scene -- key to keeping bit-exact determinism across
// broadphase choices.
//
// Also implements ISpatialIndex so Raycast / Sweep / Trajectory can collect
// candidates via a cell walk instead of a linear body scan.

using System;
using System.Collections.Generic;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 散列均匀网格 broadphase：每步把活跃体按 AABB 覆盖的格子插入桶，
    /// 然后按桶内成对生成 pair（跨桶去重）。pair 列表按 (IndexA, IndexB) 升序，
    /// 与 <see cref="BruteForceBroadphase"/> 输出顺序一致，保证跨 broadphase 切换的逐比特确定性。
    /// <para>同时实现 <see cref="ISpatialIndex"/>，让 Raycast / Sweep / Trajectory
    /// 走"遍历相关格子"而不是 O(N) 线性扫描。</para>
    /// </summary>
    public sealed class UniformGridBroadphase : IBroadphase, ISpatialIndex
    {
        /// <summary>单格边长（世界单位）。</summary>
        private readonly FP64 mCellSize;

        /// <summary>活跃体数 ≤ 该阈值时直接走 O(N²) 暴力路径。</summary>
        private readonly int mBruteThreshold;

        /// <summary>cellKey -&gt; 该格内的 body index 列表。</summary>
        private readonly Dictionary<long, List<int>> mCells = new();

        /// <summary>桶 List&lt;int&gt; 的对象池，避免每帧 new。</summary>
        private readonly Stack<List<int>> mBucketPool = new();

        /// <summary>本次 Build 产生的 pair 列表。</summary>
        private readonly List<LWPair> mPairs = new();

        /// <summary>跨桶去重用的 pair 集合，存 ((long)i &lt;&lt; 32) | (uint)j。</summary>
        private readonly HashSet<long> mPairSet = new();

        /// <summary>
        /// QueryAABB 去重用的 epoch-tag 数组：自增 <see cref="mGen"/> 即可让上次的 tag 失效，
        /// 避免每次查询都清空 tag 数组。
        /// </summary>
        private int[] mGenTags = Array.Empty<int>();

        /// <summary>当前 tag 代数（每次 Refresh / QueryAABB 自增）。</summary>
        private int mGen;

        /// <summary>缓存最近一次 Refresh 时传入的 bodies 引用。</summary>
        private LWBody[] mBodiesRef;

        /// <summary>缓存最近一次 Refresh 时传入的 capacity。</summary>
        private int mCapRef;

        /// <summary>使用默认 Config 构造。</summary>
        public UniformGridBroadphase() : this(LWPhysicsConfig.Default) { }

        /// <summary>使用指定 Config 构造；非法 cellSize 会回退为 1。</summary>
        public UniformGridBroadphase(LWPhysicsConfig config)
        {
            mCellSize = config.GridCellSize.Raw > 0 ? config.GridCellSize : FP64.One;
            mBruteThreshold = config.BruteForceBodyThreshold < 0 ? 0 : config.BruteForceBodyThreshold;
        }

        /// <inheritdoc/>
        public List<LWPair> Pairs => mPairs;

        // IBroadphase ------------------------------------------------------

        /// <inheritdoc/>
        /// <remarks>
        /// 当活跃体数 ≤ <c>BruteForceBodyThreshold</c> 时跳过 cell hash 的构建，
        /// 直接用 O(N²) 暴力枚举生成 pair；网格不重建可省下成百次 dictionary 写入。
        /// 副作用：在该分支下 <see cref="QueryAABB"/> 会返回空，调用方在查询前必须先
        /// <see cref="Refresh"/>。<see cref="LWPhysicsWorld"/> 通过
        /// <c>mSpatialDirty</c> + <c>PrepareSpatialIndex</c> 已经做了懒刷新，
        /// 单独使用本类的调用方需自行 Refresh。
        /// </remarks>
        public void Build(LWBody[] bodies, int capacity)
        {
            mPairs.Clear();
            mPairSet.Clear();

            int alive = CountAlive(bodies, capacity);
            if (alive <= mBruteThreshold)
            {
                // ClearCells 保证之前一次 grid 路径留下的桶不会泄漏到本帧的
                // QueryAABB；mBodiesRef/mCapRef 也置零，避免任何遗留状态被读到。
                ClearCells();
                mBodiesRef = null;
                mCapRef = 0;
                BuildPairsBrute(bodies, capacity);
                // BuildPairsBrute 天然按 (i, j) 字典序追加，已经满足
                // SortPairs 期望的升序，故无需再排一次。
            }
            else
            {
                Refresh(bodies, capacity);
                BuildPairsGrid(bodies, capacity);
                // 网格按 dict 迭代顺序生成 pair，必须显式排序保证确定性。
                SortPairs();
            }
        }

        // ISpatialIndex ----------------------------------------------------

        /// <inheritdoc/>
        public void Refresh(LWBody[] bodies, int capacity)
        {
            ClearCells();
            mBodiesRef = bodies;
            mCapRef = capacity;
            EnsureGenTags(capacity);
            for (int i = 0; i < capacity; i++)
            {
                if ((bodies[i].Flags & LWBodyFlags.Alive) == 0) continue;
                InsertBody(i, in bodies[i]);
            }
            // Bump tag epoch so the next QueryAABB ignores stale tags.
            unchecked { mGen++; }
            if (mGen == int.MaxValue)
            {
                Array.Clear(mGenTags, 0, mGenTags.Length);
                mGen = 1;
            }
        }

        /// <inheritdoc/>
        public void QueryAABB(in FPVector2 min, in FPVector2 max, List<int> outIndices)
        {
            if (mCells.Count == 0) return;

            int mnX = CellCoord(min.X);
            int mnY = CellCoord(min.Y);
            int mxX = CellCoord(max.X);
            int mxY = CellCoord(max.Y);

            unchecked { mGen++; }
            if (mGen == int.MaxValue)
            {
                Array.Clear(mGenTags, 0, mGenTags.Length);
                mGen = 1;
            }
            int gen = mGen;
            EnsureGenTags(mCapRef);
            int[] tags = mGenTags;

            for (int cx = mnX; cx <= mxX; cx++)
            {
                for (int cy = mnY; cy <= mxY; cy++)
                {
                    if (!mCells.TryGetValue(MakeCellKey(cx, cy), out List<int> bucket))
                        continue;
                    for (int k = 0; k < bucket.Count; k++)
                    {
                        int idx = bucket[k];
                        if (tags[idx] == gen) continue;
                        tags[idx] = gen;
                        outIndices.Add(idx);
                    }
                }
            }
            // Deterministic ascending order regardless of cell iteration order.
            outIndices.Sort();
        }

        /// <inheritdoc/>
        public void QuerySegmentAABB(
            in FPVector2 origin, in FPVector2 dir, FP64 maxDist,
            in FPVector2 inflation,
            List<int> outIndices)
        {
            FPVector2 endPos = origin + dir * maxDist;
            FP64 mnX = origin.X < endPos.X ? origin.X : endPos.X;
            FP64 mxX = origin.X < endPos.X ? endPos.X : origin.X;
            FP64 mnY = origin.Y < endPos.Y ? origin.Y : endPos.Y;
            FP64 mxY = origin.Y < endPos.Y ? endPos.Y : origin.Y;
            FPVector2 minA = new(mnX - inflation.X, mnY - inflation.Y);
            FPVector2 maxA = new(mxX + inflation.X, mxY + inflation.Y);
            QueryAABB(in minA, in maxA, outIndices);
        }

        // Helpers ----------------------------------------------------------

        /// <summary>计算 <c>bodies[0..capacity)</c> 中 Alive 位的数量。</summary>
        private static int CountAlive(LWBody[] bodies, int capacity)
        {
            int n = 0;
            for (int i = 0; i < capacity; i++)
                if ((bodies[i].Flags & LWBodyFlags.Alive) != 0) n++;
            return n;
        }

        /// <summary>把 body 的 AABB 覆盖到的所有格子都插入 index <paramref name="i"/>。</summary>
        private void InsertBody(int i, in LWBody body)
        {
            FPVector2 half;
            if (body.Shape.Type == LWShapeType.Circle)
                half = new FPVector2(body.Shape.Radius, body.Shape.Radius);
            else
                half = body.Shape.HalfExtents;

            FPVector2 minA = body.Position - half;
            FPVector2 maxA = body.Position + half;

            int mnX = CellCoord(minA.X);
            int mnY = CellCoord(minA.Y);
            int mxX = CellCoord(maxA.X);
            int mxY = CellCoord(maxA.Y);

            for (int cx = mnX; cx <= mxX; cx++)
            {
                for (int cy = mnY; cy <= mxY; cy++)
                {
                    long key = MakeCellKey(cx, cy);
                    if (!mCells.TryGetValue(key, out List<int> bucket))
                    {
                        bucket = mBucketPool.Count > 0 ? mBucketPool.Pop() : new List<int>(4);
                        mCells[key] = bucket;
                    }
                    bucket.Add(i);
                }
            }
        }

        /// <summary>小规模场景的 O(N²) 暴力 pair 生成。</summary>
        private void BuildPairsBrute(LWBody[] bodies, int capacity)
        {
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

        /// <summary>按桶内成对生成 pair，跨桶用 <see cref="mPairSet"/> 去重。</summary>
        private void BuildPairsGrid(LWBody[] bodies, int capacity)
        {
            foreach (KeyValuePair<long, List<int>> kvp in mCells)
            {
                List<int> bucket = kvp.Value;
                int bn = bucket.Count;
                for (int a = 0; a < bn; a++)
                {
                    int i = bucket[a];
                    LWBodyFlags fi = bodies[i].Flags;
                    for (int b = a + 1; b < bn; b++)
                    {
                        int j = bucket[b];
                        // Bucket entries were appended in body-index ascending
                        // order, so i < j here; no need to swap.
                        LWBodyFlags fj = bodies[j].Flags;
                        if ((fi & LWBodyFlags.Static) != 0 && (fj & LWBodyFlags.Static) != 0)
                            continue;
                        if ((fi & LWBodyFlags.Sleeping) != 0 && (fj & LWBodyFlags.Sleeping) != 0)
                            continue;
                        if (!LWLayerMask.ShouldCollide(bodies[i].Layer, bodies[i].Mask,
                                                       bodies[j].Layer, bodies[j].Mask))
                            continue;
                        long pairKey = ((long)i << 32) | (uint)j;
                        if (mPairSet.Add(pairKey))
                        {
                            mPairs.Add(new LWPair(i, j));
                        }
                    }
                }
            }
        }

        /// <summary>pair 排序比较器，按 (IndexA, IndexB) 升序。</summary>
        private static readonly Comparison<LWPair> sPairCompare = (a, b) =>
        {
            int c = a.IndexA.CompareTo(b.IndexA);
            return c != 0 ? c : a.IndexB.CompareTo(b.IndexB);
        };

        /// <summary>对 pair 列表做确定性排序。</summary>
        private void SortPairs() => mPairs.Sort(sPairCompare);

        /// <summary>清空所有格子并把桶 List 归还对象池。</summary>
        private void ClearCells()
        {
            foreach (KeyValuePair<long, List<int>> kvp in mCells)
            {
                kvp.Value.Clear();
                mBucketPool.Push(kvp.Value);
            }
            mCells.Clear();
        }

        /// <summary>把世界坐标 <paramref name="v"/> 折算成格子坐标（向 -∞ 取整）。</summary>
        private int CellCoord(FP64 v)
        {
            return (v / mCellSize).ToIntFloor();
        }

        /// <summary>把 (cx, cy) 打包为 64 位 cell key。</summary>
        private static long MakeCellKey(int cx, int cy)
        {
            return ((long)cx << 32) | (uint)cy;
        }

        /// <summary>必要时扩容 <see cref="mGenTags"/>，使其至少能容纳 <paramref name="required"/> 个元素。</summary>
        private void EnsureGenTags(int required)
        {
            if (required <= 0) return;
            if (mGenTags.Length >= required) return;
            int sz = Math.Max(mGenTags.Length, 16);
            while (sz < required) sz *= 2;
            Array.Resize(ref mGenTags, sz);
        }
    }
}
