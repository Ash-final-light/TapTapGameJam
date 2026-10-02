// IBroadphase.cs

using System.Collections.Generic;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 待进入 Narrowphase 检测的一对刚体（这里存储的是数组 INDEX，不是 body ID）。
    /// </summary>
    public readonly struct LWPair
    {
        /// <summary>A 端在 World.mBodies 数组中的索引。</summary>
        public readonly int IndexA;

        /// <summary>B 端在 World.mBodies 数组中的索引。</summary>
        public readonly int IndexB;

        /// <summary>构造一个配对（索引顺序由调用方约定）。</summary>
        public LWPair(int a, int b)
        {
            IndexA = a;
            IndexB = b;
        }
    }

    /// <summary>
    /// 物理广义阶段接口。负责从全部刚体中筛出可能发生碰撞的候选对。
    /// </summary>
    public interface IBroadphase
    {
        /// <summary>
        /// 针对 <paramref name="bodies"/> 中前 <paramref name="capacity"/> 个 slot 的活跃子集
        /// 构建候选对列表。返回的列表归 broadphase 所有，下次 <c>Build</c> 调用前保持有效。
        /// </summary>
        void Build(LWBody[] bodies, int capacity);

        /// <summary>最近一次 <see cref="Build"/> 产生的候选对列表（确定性排序）。</summary>
        List<LWPair> Pairs { get; }
    }
}
