// LWRaycastHit.cs

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>射线查询命中信息。</summary>
    public struct LWRaycastHit
    {
        /// <summary>被命中的 body ID。</summary>
        public int BodyId;

        /// <summary>命中点距离射线起点的有向距离（沿方向向量）。</summary>
        public FP64 Distance;

        /// <summary>命中点（世界坐标）。</summary>
        public FPVector2 Point;

        /// <summary>
        /// 命中点的表面外向法线。
        /// <para>注意：与 <see cref="LWContact.Normal"/>（A→B 求解器约定）方向相反，
        /// 这里的方向适配 <see cref="FPVector2.Reflect"/>。</para>
        /// </summary>
        public FPVector2 Normal;
    }
}
