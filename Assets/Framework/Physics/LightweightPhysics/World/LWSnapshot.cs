// LWSnapshot.cs
//
// Opaque, deep-copyable snapshot of an LWPhysicsWorld. Used for replay /
// prediction rollback.

using System;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// <see cref="LWPhysicsWorld"/> 的不透明快照，用于回放与预测回滚。
    /// 各数组的 buffer 长度可以 &gt;= 逻辑长度，<see cref="Capacity"/> /
    /// <see cref="FreeCount"/> / <see cref="PrevTriggerCount"/> 给出"真正有效"的元素个数，
    /// 这样 <see cref="LWPhysicsWorld.Snapshot(LWSnapshot)"/> 可以原地复用 buffer。
    /// </summary>
    public sealed class LWSnapshot
    {
        /// <summary>刚体数组（前 <see cref="Capacity"/> 个有效，其余未定义）。</summary>
        public LWBody[] Bodies;

        /// <summary><see cref="Bodies"/> 中的有效元素数（&lt;= buffer 长度）。</summary>
        public int Capacity;

        /// <summary>下一次 AddBody 将分配的 ID。</summary>
        public int NextId;

        /// <summary>空闲槽位索引数组。</summary>
        public int[] FreeIndices;

        /// <summary><see cref="FreeIndices"/> 的有效长度。</summary>
        public int FreeCount;

        /// <summary>
        /// 上一帧的 Trigger 配对集合，按 <c>((long)lo &lt;&lt; 32 | (uint)hi)</c> 打包并升序排列。
        /// Restore 后的下一次 Step 用它做 Enter/Stay/Exit diff。
        /// </summary>
        public long[] PrevTriggerPairs;

        /// <summary><see cref="PrevTriggerPairs"/> 的有效长度。</summary>
        public int PrevTriggerCount;

        /// <summary>默认构造一个空快照，所有数组为零长。</summary>
        public LWSnapshot() : this(
            Array.Empty<LWBody>(), 0, 1, Array.Empty<int>(), Array.Empty<long>())
        { }

        /// <summary>
        /// 由原始字段构造快照（兼容测试代码手工拼装的旧用法）。
        /// 逻辑长度被设置为各数组的 Length。
        /// </summary>
        public LWSnapshot(LWBody[] bodies, int capacity, int nextId,
                          int[] freeIndices, long[] prevTriggerPairs)
        {
            Bodies = bodies ?? Array.Empty<LWBody>();
            Capacity = capacity;
            NextId = nextId;
            FreeIndices = freeIndices ?? Array.Empty<int>();
            FreeCount = FreeIndices.Length;
            PrevTriggerPairs = prevTriggerPairs ?? Array.Empty<long>();
            PrevTriggerCount = PrevTriggerPairs.Length;
        }
    }
}
