// ISpatialIndex.cs
//
// Query-side spatial acceleration. Optional: a broadphase implementation may
// also implement this interface to expose its acceleration structure to the
// Raycast / Sweep / Trajectory query layer. When the world's broadphase does
// not implement ISpatialIndex queries fall back to a linear scan.

using System.Collections.Generic;
using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 查询侧的空间加速接口。Broadphase 实现可选地暴露该接口，使
    /// Raycast / Sweep / Trajectory 等查询可以走 O(候选) 而非 O(N) 路径；
    /// 未实现该接口时，<see cref="LWPhysicsWorld"/> 会让查询退化为线性扫描。
    /// </summary>
    public interface ISpatialIndex
    {
        /// <summary>
        /// 按 <paramref name="bodies"/> 的前 <paramref name="capacity"/> 个 slot 重建索引。
        /// 相对 <see cref="IBroadphase.Build"/> 更轻，因为不生成 pair 列表。
        /// World 在查询前根据脏标记按需调用本方法。
        /// </summary>
        void Refresh(LWBody[] bodies, int capacity);

        /// <summary>
        /// 收集 AABB 与 [<paramref name="min"/>, <paramref name="max"/>] 相交的所有刚体索引；
        /// 调用方需先清空 <paramref name="outIndices"/>，实现保证去重并按升序追加。
        /// </summary>
        void QueryAABB(in FPVector2 min, in FPVector2 max, List<int> outIndices);

        /// <summary>
        /// 便捷查询：把线段 <c>[origin, origin + dir * maxDist]</c> 形成的 AABB
        /// 四向再外扩 <paramref name="inflation"/>，然后调用 <see cref="QueryAABB"/>。
        /// </summary>
        void QuerySegmentAABB(
            in FPVector2 origin, in FPVector2 dir, FP64 maxDist,
            in FPVector2 inflation,
            List<int> outIndices);
    }
}
