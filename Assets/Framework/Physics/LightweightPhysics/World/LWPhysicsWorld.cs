// LWPhysicsWorld.cs
//
// Owns body storage, drives the per-step pipeline (integrate -> broadphase ->
// narrowphase -> solve -> sleep) and exposes Snapshot / Restore for replay
// and prediction rollback.

using System;
using System.Collections.Generic;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 物理世界：拥有刚体存储、驱动每步管线（积分 → broadphase → narrowphase → solver → sleep），
    /// 并提供 Snapshot / Restore 给回放与预测回滚使用。
    /// <para>
    /// API 约定：
    /// <list type="bullet">
    /// <item><see cref="GetBody"/> 返回 VALUE（防御性拷贝）；要写回请编辑拷贝后调用 <see cref="SetBody"/>。
    /// 旧的 <c>ref</c> 返回 API 已被移除，因为 <see cref="AddBody"/>/<see cref="Restore"/> 引发的内部
    /// 数组扩容会让 <c>ref</c> 静默失效。</item>
    /// <item><c>Config</c> 在构造时确定且不可变；要换 Config 请新建一个 World。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class LWPhysicsWorld
    {
        /// <summary>稠密刚体数组（容量按 2 倍扩，长度可能大于 <see cref="mCapacity"/>）。</summary>
        private LWBody[] mBodies;

        /// <summary>已分配过的最高 slot index + 1（不是活跃体数量）。</summary>
        private int mCapacity;

        /// <summary>已被回收的空闲 slot 索引，下一次 AddBody 优先复用。</summary>
        private readonly List<int> mFreeIndices = new();

        /// <summary>稳定 ID 到当前数组槽位；避免逐颗弹读写时反复扫描世界。</summary>
        private readonly Dictionary<int, int> mIdToIndex = new();

        /// <summary>下一个分配的 body ID（严格递增，不复用）。</summary>
        private int mNextId = 1;

        /// <summary>当前活跃（Alive 位为 1）的刚体数量。</summary>
        private int mAliveCount;

        /// <summary>构造时锁定的物理参数，运行期不可变。</summary>
        private readonly LWPhysicsConfig mConfig;

        /// <summary>注入的 broadphase（默认 <see cref="UniformGridBroadphase"/>）。</summary>
        private readonly IBroadphase mBroadphase;

        /// <summary>当前 Step 产生的碰撞列表（不含 Trigger）。</summary>
        private readonly List<LWContact> mContacts = new();

        /// <summary>上一帧的 Trigger 配对集合（升序、用于 diff）。</summary>
        private List<long> mPrevTriggerPairs = new();

        /// <summary>当前帧的 Trigger 配对集合。</summary>
        private List<long> mCurrTriggerPairs = new();

        /// <summary>当前 Step 产生的 Trigger 事件（Enter / Stay / Exit）。</summary>
        private readonly List<LWTriggerEvent> mTriggerEvents = new();

        /// <summary>
        /// 标记 broadphase 的空间索引是否需要重建。
        /// 在 <see cref="PrepareSpatialIndex"/> 中按需刷新。
        /// </summary>
        private bool mSpatialDirty = true;

        /// <summary>使用默认配置构造。</summary>
        public LWPhysicsWorld() : this(LWPhysicsConfig.Default) { }

        /// <summary>使用指定配置构造，broadphase 默认采用 <see cref="UniformGridBroadphase"/>。</summary>
        public LWPhysicsWorld(LWPhysicsConfig config) : this(config, new UniformGridBroadphase(config)) { }

        /// <summary>
        /// 使用指定配置 + 自定义 broadphase 构造。
        /// 传 null 时降级为 <see cref="BruteForceBroadphase"/>。
        /// </summary>
        public LWPhysicsWorld(LWPhysicsConfig config, IBroadphase broadphase)
        {
            mConfig = config;
            mBroadphase = broadphase ?? new BruteForceBroadphase();
            int initial = config.InitialBodyCapacity > 0 ? config.InitialBodyCapacity : 16;
            mBodies = new LWBody[initial];
            mCapacity = 0;
        }

        /// <summary>构造时锁定的 Config（只读）。</summary>
        public LWPhysicsConfig Config => mConfig;

        /// <summary>当前活跃刚体数。</summary>
        public int BodyCount => mAliveCount;

        /// <summary>当前 Step 产生的实体碰撞列表。</summary>
        public IReadOnlyList<LWContact> StepContacts => mContacts;

        /// <summary>当前 Step 产生的 Trigger 事件列表。</summary>
        public IReadOnlyList<LWTriggerEvent> TriggerEvents => mTriggerEvents;

        /// <summary>内部访问：给同程序集的查询代码（M2/M3）线性扫描使用。</summary>
        internal LWBody[] BodiesArray => mBodies;

        /// <summary>内部访问：当前已分配 slot 数。</summary>
        internal int BodyCapacity => mCapacity;

        /// <summary>
        /// 若 broadphase 实现了 <see cref="ISpatialIndex"/>，按需刷新后返回其门面；
        /// 否则返回 null（调用方将退化为线性扫描）。
        /// </summary>
        internal ISpatialIndex PrepareSpatialIndex()
        {
            if (mBroadphase is not ISpatialIndex idx) return null;
            if (mSpatialDirty)
            {
                idx.Refresh(mBodies, mCapacity);
                mSpatialDirty = false;
            }
            return idx;
        }

        /// <summary>
        /// 外部接口：当调用方通过其它途径改写了刚体位置/形状（不走 <see cref="SetBody"/>）时，
        /// 必须调用本方法将空间索引标记为脏。
        /// </summary>
        public void MarkSpatialIndexDirty() => mSpatialDirty = true;

        /// <summary>
        /// 添加一个刚体，返回稳定的 body ID。
        /// 若 ID 空间耗尽（达到 <c>int.MaxValue</c>）将抛 <see cref="InvalidOperationException"/>。
        /// </summary>
        public int AddBody(in LWBodyDef def)
        {
            if (mNextId == int.MaxValue)
                throw new InvalidOperationException(
                    "LWPhysicsWorld: body id space exhausted (int.MaxValue reached).");

            int idx;
            if (mFreeIndices.Count > 0)
            {
                int tail = mFreeIndices.Count - 1;
                idx = mFreeIndices[tail];
                mFreeIndices.RemoveAt(tail);
            }
            else
            {
                if (mCapacity == mBodies.Length)
                {
                    Array.Resize(ref mBodies, mBodies.Length * 2);
                }
                idx = mCapacity;
                mCapacity++;
            }

            LWBodyFlags flags = (def.Flags | LWBodyFlags.Alive) & ~LWBodyFlags.Sleeping;
            bool isStatic = def.Mass.Raw <= 0;
            if (isStatic) flags |= LWBodyFlags.Static;

            int id = mNextId++;
            mBodies[idx] = new LWBody
            {
                Id = id,
                Flags = flags,
                Layer = def.Layer == 0 ? LWLayerMask.LayerBit(0) : def.Layer,
                Mask = def.Mask == 0 ? LWLayerMask.AllLayers : def.Mask,
                Shape = def.Shape,
                Position = def.Position,
                Velocity = def.Velocity,
                InvMass = isStatic ? FP64.Zero : (FP64.One / def.Mass),
                Restitution = def.Restitution,
                LinearDamping = def.LinearDamping,
                UserData = def.UserData,
                SleepFrames = 0,
            };
            mAliveCount++;
            mIdToIndex.Add(id, idx);
            mSpatialDirty = true;
            return id;
        }

        /// <summary>移除指定 ID 的刚体；不存在时返回 false。</summary>
        public bool RemoveBody(int id)
        {
            int idx = IndexFromId(id);
            if (idx < 0) return false;
            mIdToIndex.Remove(id);
            mBodies[idx] = default;
            mFreeIndices.Add(idx);
            mAliveCount--;
            mSpatialDirty = true;
            return true;
        }

        /// <summary>判断指定 ID 是否存在并活跃。</summary>
        public bool HasBody(int id) => IndexFromId(id) >= 0;

        /// <summary>
        /// 返回 ID 对应刚体的状态拷贝。找不到时抛 <see cref="ArgumentException"/>。
        /// </summary>
        public LWBody GetBody(int id)
        {
            int idx = IndexFromId(id);
            if (idx < 0) throw new ArgumentException($"LWPhysicsWorld: body id {id} not found", nameof(id));
            return mBodies[idx];
        }

        /// <summary>
        /// 非抛异常版本：找到则返回 true 并把状态拷贝写入 <paramref name="body"/>。
        /// </summary>
        public bool TryGetBody(int id, out LWBody body)
        {
            int idx = IndexFromId(id);
            if (idx < 0) { body = default; return false; }
            body = mBodies[idx];
            return true;
        }

        public IEnumerable<LWBody> GetBodies()
        {
            for (int i = 0; i < mCapacity; i++)
            {
                if ((mBodies[i].Flags & LWBodyFlags.Alive) == 0)
                    continue;

                yield return mBodies[i];
            }
        }

        /// <summary>
        /// 用 <paramref name="body"/> 覆盖指定 ID 的刚体；
        /// <c>Id</c> 与 <c>Alive / Static</c> 等生命周期字段由 World 强制保留，
        /// 调用方在拷贝上误改这些字段不会破坏内部一致性。
        /// </summary>
        public void SetBody(int id, in LWBody body)
        {
            int idx = IndexFromId(id);
            if (idx < 0) throw new ArgumentException($"LWPhysicsWorld: body id {id} not found", nameof(id));
            LWBodyFlags lifecycle = mBodies[idx].Flags & (LWBodyFlags.Alive | LWBodyFlags.Static);
            LWBodyFlags userFlags = body.Flags & ~(LWBodyFlags.Alive | LWBodyFlags.Static);
            LWBody updated = body;
            updated.Id = mBodies[idx].Id;
            updated.Flags = lifecycle | userFlags;
            mBodies[idx] = updated;
            mSpatialDirty = true;
        }

        /// <summary>按稳定 ID 查询当前槽位；扩容、移除与恢复快照时保持映射同步。</summary>
        private int IndexFromId(int id)
        {
            return id > 0 && mIdToIndex.TryGetValue(id, out int index) ? index : -1;
        }

        /// <summary>使用 <see cref="LWPhysicsConfig.FixedDt"/> 步进一帧。</summary>
        public void Step() => Step(mConfig.FixedDt);

        /// <summary>
        /// 步进一帧：积分 → broadphase → narrowphase → solver/触发器 diff → sleep 计数。
        /// </summary>
        public void Step(FP64 dt)
        {
            // 1. Integrate.
            for (int i = 0; i < mCapacity; i++)
            {
                ref LWBody body = ref mBodies[i];
                if ((body.Flags & LWBodyFlags.Alive) == 0) continue;
                if ((body.Flags & LWBodyFlags.Static) != 0) continue;
                if ((body.Flags & LWBodyFlags.Sleeping) != 0) continue;

                if (body.LinearDamping.Raw > 0)
                {
                    FP64 factor = FP64.One - body.LinearDamping * dt;
                    if (factor.Raw < 0) factor = FP64.Zero;
                    body.Velocity = body.Velocity * factor;
                }
                body.Position += body.Velocity * dt;
            }

            // 2. Broadphase + narrowphase + solver.
            mContacts.Clear();
            mCurrTriggerPairs.Clear();
            mBroadphase.Build(mBodies, mCapacity);
            List<LWPair> pairs = mBroadphase.Pairs;
            for (int p = 0; p < pairs.Count; p++)
            {
                LWPair pair = pairs[p];
                ref LWBody a = ref mBodies[pair.IndexA];
                ref LWBody b = ref mBodies[pair.IndexB];
                if (!Narrowphase.Test(in a, in b, out LWContact c))
                    continue;

                bool isTrigger = (a.Flags & LWBodyFlags.Trigger) != 0
                              || (b.Flags & LWBodyFlags.Trigger) != 0;
                if (isTrigger)
                {
                    int lo = a.Id < b.Id ? a.Id : b.Id;
                    int hi = a.Id < b.Id ? b.Id : a.Id;
                    mCurrTriggerPairs.Add(((long)lo << 32) | (uint)hi);
                    continue;
                }

                c.BodyA = a.Id;
                c.BodyB = b.Id;
                mContacts.Add(c);
                ContactSolver.Resolve(ref a, ref b, c, mConfig);
            }

            // 2b. Trigger Enter/Stay/Exit diff against last step.
            mCurrTriggerPairs.Sort();
            BuildTriggerEvents();
            // Swap buffers so prev now holds the pairs we just computed.
            (mPrevTriggerPairs, mCurrTriggerPairs) = (mCurrTriggerPairs, mPrevTriggerPairs);

            // 3. Sleep accounting.
            for (int i = 0; i < mCapacity; i++)
            {
                ref LWBody body = ref mBodies[i];
                if ((body.Flags & LWBodyFlags.Alive) == 0) continue;
                if ((body.Flags & LWBodyFlags.Static) != 0) continue;

                FP64 spdSq = body.Velocity.SqrMagnitude;
                if (spdSq < mConfig.SleepLinearSpeedSqr)
                {
                    body.SleepFrames++;
                    if (body.SleepFrames >= mConfig.SleepFrames)
                    {
                        body.Flags |= LWBodyFlags.Sleeping;
                        body.Velocity = FPVector2.Zero;
                    }
                }
                else
                {
                    body.SleepFrames = 0;
                }
            }

            // Position correction during ContactSolver and integrate above
            // moved bodies relative to the grid the broadphase built at the
            // top of this Step. Mark the spatial index dirty so the next
            // query refreshes it against post-Step positions.
            mSpatialDirty = true;
        }

        // Snapshot / Restore -----------------------------------------------

        /// <summary>分配一个新的快照并写入当前世界状态。</summary>
        public LWSnapshot Snapshot() => Snapshot(null);

        /// <summary>
        /// 复用版本：当 <paramref name="reuse"/> 非空时复用其 buffer（必要时按 2 倍扩容），
        /// 元素逻辑长度通过 <c>Capacity / FreeCount / PrevTriggerCount</c> 表达，
        /// 因此调用方可以在稳态下做到零分配。
        /// </summary>
        public LWSnapshot Snapshot(LWSnapshot reuse)
        {
            LWSnapshot s = reuse ?? new LWSnapshot();

            // Bodies: grow buffer if needed, copy up to mCapacity.
            if (s.Bodies == null || s.Bodies.Length < mCapacity)
            {
                int newLen = s.Bodies == null || s.Bodies.Length == 0 ? 16 : s.Bodies.Length;
                while (newLen < mCapacity) newLen *= 2;
                s.Bodies = new LWBody[newLen];
            }
            else if (s.Capacity > mCapacity)
            {
                // Clear stale tail entries so a later inspector cannot read
                // bodies from a longer previous snapshot.
                Array.Clear(s.Bodies, mCapacity, s.Capacity - mCapacity);
            }
            if (mCapacity > 0) Array.Copy(mBodies, s.Bodies, mCapacity);
            s.Capacity = mCapacity;
            s.NextId = mNextId;

            // FreeIndices.
            int freeCount = mFreeIndices.Count;
            if (s.FreeIndices == null || s.FreeIndices.Length < freeCount)
            {
                int newLen = (s.FreeIndices == null || s.FreeIndices.Length == 0) ? 4 : s.FreeIndices.Length;
                while (newLen < freeCount) newLen *= 2;
                s.FreeIndices = new int[newLen];
            }
            for (int i = 0; i < freeCount; i++) s.FreeIndices[i] = mFreeIndices[i];
            s.FreeCount = freeCount;

            // PrevTriggerPairs.
            int prevCount = mPrevTriggerPairs.Count;
            if (s.PrevTriggerPairs == null || s.PrevTriggerPairs.Length < prevCount)
            {
                int newLen = (s.PrevTriggerPairs == null || s.PrevTriggerPairs.Length == 0) ? 4 : s.PrevTriggerPairs.Length;
                while (newLen < prevCount) newLen *= 2;
                s.PrevTriggerPairs = new long[newLen];
            }
            for (int i = 0; i < prevCount; i++) s.PrevTriggerPairs[i] = mPrevTriggerPairs[i];
            s.PrevTriggerCount = prevCount;

            return s;
        }

        /// <summary>
        /// 将世界状态恢复到快照对应的时刻：刚体数组、空闲列表、ID、上一帧 Trigger 配对
        /// 全部覆盖；当前 Step 的临时数据（contacts/events）被清空。
        /// </summary>
        public void Restore(LWSnapshot snap)
        {
            if (snap == null) throw new ArgumentNullException(nameof(snap));
            if (snap.Capacity > mBodies.Length)
            {
                int newCap = mBodies.Length;
                if (newCap == 0) newCap = 1;
                while (newCap < snap.Capacity) newCap *= 2;
                mBodies = new LWBody[newCap];
            }
            else
            {
                Array.Clear(mBodies, 0, mCapacity);
            }

            if (snap.Capacity > 0)
            {
                Array.Copy(snap.Bodies, mBodies, snap.Capacity);
            }
            mCapacity = snap.Capacity;
            mNextId = snap.NextId;

            mFreeIndices.Clear();
            for (int i = 0; i < snap.FreeCount; i++)
            {
                mFreeIndices.Add(snap.FreeIndices[i]);
            }

            int alive = 0;
            mIdToIndex.Clear();
            for (int i = 0; i < mCapacity; i++)
            {
                if ((mBodies[i].Flags & LWBodyFlags.Alive) == 0) continue;
                alive++;
                mIdToIndex.Add(mBodies[i].Id, i);
            }
            mAliveCount = alive;
            mContacts.Clear();
            mTriggerEvents.Clear();
            mPrevTriggerPairs.Clear();
            for (int i = 0; i < snap.PrevTriggerCount; i++)
            {
                mPrevTriggerPairs.Add(snap.PrevTriggerPairs[i]);
            }
            mCurrTriggerPairs.Clear();
            mSpatialDirty = true;
        }

        // Helpers ----------------------------------------------------------

        /// <summary>
        /// 在两份有序 Trigger 配对列表上做双指针归并，
        /// 同时出现 → Stay，仅出现在当前 → Enter，仅出现在上一帧 → Exit。
        /// </summary>
        private void BuildTriggerEvents()
        {
            mTriggerEvents.Clear();
            int p = 0, q = 0;
            int cn = mCurrTriggerPairs.Count;
            int pn = mPrevTriggerPairs.Count;
            while (p < cn && q < pn)
            {
                long cur = mCurrTriggerPairs[p];
                long prev = mPrevTriggerPairs[q];
                if (cur == prev)
                {
                    EmitTrigger(LWTriggerPhase.Stay, cur);
                    p++; q++;
                }
                else if (cur < prev)
                {
                    EmitTrigger(LWTriggerPhase.Enter, cur);
                    p++;
                }
                else
                {
                    EmitTrigger(LWTriggerPhase.Exit, prev);
                    q++;
                }
            }
            while (p < cn) EmitTrigger(LWTriggerPhase.Enter, mCurrTriggerPairs[p++]);
            while (q < pn) EmitTrigger(LWTriggerPhase.Exit, mPrevTriggerPairs[q++]);
        }

        /// <summary>把打包后的 (lo, hi) 配对解开并写入触发器事件列表。</summary>
        private void EmitTrigger(LWTriggerPhase phase, long packed)
        {
            mTriggerEvents.Add(new LWTriggerEvent
            {
                Phase = phase,
                BodyA = (int)(packed >> 32),
                BodyB = (int)(packed & 0xFFFFFFFFL),
            });
        }
    }
}
