// LWSweepHit.cs

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>形状扫掠（CircleCast / BoxCast）的命中信息。</summary>
    public struct LWSweepHit
    {
        /// <summary>被命中的 body ID。</summary>
        public int BodyId;

        /// <summary>扫掠形状从起点移动到接触位置所经过的距离。</summary>
        public FP64 Distance;

        /// <summary>首次接触点（世界坐标）。</summary>
        public FPVector2 Point;

        /// <summary>
        /// 命中点的表面外向法线（指向被命中体的外侧，与 swept 入射方向相反）。
        /// <para>注意：与 <see cref="LWContact.Normal"/>（A→B 求解器约定）方向相反，
        /// 这里的方向适配 <see cref="FPVector2.Reflect"/>。</para>
        /// </summary>
        public FPVector2 Normal;
    }
}
