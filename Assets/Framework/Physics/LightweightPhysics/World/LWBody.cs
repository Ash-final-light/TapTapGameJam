// LWBody.cs
//
// Physics body record. Stored densely in LWPhysicsWorld; access through
// the world's GetBody(id) accessor.

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 物理体记录，由 <see cref="LWPhysicsWorld"/> 紧凑数组存储。
    /// 外部读写应通过 <see cref="LWPhysicsWorld.GetBody"/> / <see cref="LWPhysicsWorld.SetBody"/>。
    /// </summary>
    public struct LWBody
    {
        /// <summary>1 起的稳定 ID（0 保留为 "invalid"）。便于在遍历时回查自己。</summary>
        public int Id;

        /// <summary>状态位（Alive / Static / Sleeping / Trigger）。</summary>
        public LWBodyFlags Flags;

        /// <summary>所在层（16 bit 中至多 1 位为 1）。</summary>
        public ushort Layer;

        /// <summary>掩码（决定愿意和哪些 Layer 发生碰撞）。</summary>
        public ushort Mask;

        /// <summary>形状（圆或 AABB）。</summary>
        public LWShape Shape;

        /// <summary>世界坐标位置。</summary>
        public FPVector2 Position;

        /// <summary>线速度。</summary>
        public FPVector2 Velocity;

        /// <summary>质量倒数；静态体为 0，动态体应 &gt; 0。</summary>
        public FP64 InvMass;

        /// <summary>恢复系数（弹性，0 完全非弹性，1 完全弹性）。</summary>
        public FP64 Restitution;

        /// <summary>线性阻尼系数（每秒比例衰减）。</summary>
        public FP64 LinearDamping;

        /// <summary>用户自定义 32 位数据（常用于挂接外层实体 ID）。</summary>
        public int UserData;

        /// <summary>
        /// 内部休眠计数：每帧速度低于阈值时自增，
        /// 任何碰撞或外部速度改写都会重置。
        /// </summary>
        public int SleepFrames;
    }

    /// <summary>
    /// 创建 <see cref="LWBody"/> 的输入参数。通过工厂方法
    /// <see cref="Dynamic"/> / <see cref="Static"/> 构造，或自行填写各字段。
    /// </summary>
    public struct LWBodyDef
    {
        /// <summary>形状。</summary>
        public LWShape Shape;

        /// <summary>初始位置。</summary>
        public FPVector2 Position;

        /// <summary>初始速度。</summary>
        public FPVector2 Velocity;

        /// <summary>质量；0（或负数）表示静态体。</summary>
        public FP64 Mass;

        /// <summary>恢复系数。</summary>
        public FP64 Restitution;

        /// <summary>线性阻尼。</summary>
        public FP64 LinearDamping;

        /// <summary>所在层；0 会被 World 视为默认 LayerBit(0)。</summary>
        public ushort Layer;

        /// <summary>掩码；0 会被 World 视为 AllLayers。</summary>
        public ushort Mask;

        /// <summary>初始 flags（不会包含 Alive；由 World 写入）。</summary>
        public LWBodyFlags Flags;

        /// <summary>用户自定义数据。</summary>
        public int UserData;

        /// <summary>
        /// 构造动态体的常见默认值：位置/速度按入参，质量按入参，
        /// Layer = bit(0)，Mask = AllLayers。
        /// </summary>
        public static LWBodyDef Dynamic(LWShape shape, FPVector2 position, FP64 mass)
        {
            return new LWBodyDef
            {
                Shape = shape,
                Position = position,
                Velocity = FPVector2.Zero,
                Mass = mass,
                Restitution = FP64.Zero,
                LinearDamping = FP64.Zero,
                // Default body sits on the "Default" layer (bit 0) and collides
                // with every other layer. Set Layer / Mask explicitly to opt
                // into selective collisions.
                Layer = LWLayerMask.LayerBit(0),
                Mask = LWLayerMask.AllLayers,
                Flags = LWBodyFlags.None,
                UserData = 0,
            };
        }

        /// <summary>
        /// 构造静态体（质量为 0、Flags 含 Static）的常见默认值。
        /// </summary>
        public static LWBodyDef Static(LWShape shape, FPVector2 position)
        {
            return new LWBodyDef
            {
                Shape = shape,
                Position = position,
                Velocity = FPVector2.Zero,
                Mass = FP64.Zero,
                Restitution = FP64.Zero,
                LinearDamping = FP64.Zero,
                Layer = LWLayerMask.LayerBit(0),
                Mask = LWLayerMask.AllLayers,
                Flags = LWBodyFlags.Static,
                UserData = 0,
            };
        }
    }
}
