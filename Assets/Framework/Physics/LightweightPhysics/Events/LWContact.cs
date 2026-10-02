// LWContact.cs

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 一次 Narrowphase 检测产生的实体碰撞信息。
    /// </summary>
    public struct LWContact
    {
        /// <summary>参与碰撞的 A 端 body ID。</summary>
        public int BodyA;

        /// <summary>参与碰撞的 B 端 body ID。</summary>
        public int BodyB;

        /// <summary>碰撞点（世界坐标）。</summary>
        public FPVector2 Point;

        /// <summary>
        /// 由 A 指向 B 的单位法线（即求解器把 B 推开的方向）。
        /// <para>
        /// 注意：这是 A→B 约定，与 <see cref="LWSweepHit.Normal"/> / <see cref="LWRaycastHit.Normal"/>
        /// （表面外向，配合 <see cref="FPVector2.Reflect"/> 用）相反。
        /// 同时消费两类 API 的调用方需自行处理这层不对称。
        /// </para>
        /// </summary>
        public FPVector2 Normal;

        /// <summary>穿透深度（重叠时为正）。</summary>
        public FP64 Penetration;
    }
}
