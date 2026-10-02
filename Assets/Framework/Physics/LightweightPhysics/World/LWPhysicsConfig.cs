// LWPhysicsConfig.cs

using Framework.FixedPoint;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 两体弹性合成策略。<see cref="Min"/> 表示"较硬的一方主导"，
    /// <see cref="Max"/> 与 Box2D 一致（更适合街机风格），
    /// <see cref="Multiply"/> 类似几何均值，<see cref="Average"/> 线性混合。
    /// </summary>
    public enum LWRestitutionCombine : byte
    {
        /// <summary>取两者中较小的恢复系数。</summary>
        Min = 0,

        /// <summary>取两者中较大的恢复系数（Box2D 风格）。</summary>
        Max = 1,

        /// <summary>两者相乘。</summary>
        Multiply = 2,

        /// <summary>两者算术平均。</summary>
        Average = 3,
    }

    /// <summary>
    /// 物理世界的全局参数集合，构造时一次性传入 <see cref="LWPhysicsWorld"/>。
    /// </summary>
    public struct LWPhysicsConfig
    {
        /// <summary>
        /// 固定步长。<see cref="LWPhysicsWorld.Step(FP64)"/> 允许显式传 dt，
        /// 但确定性场景下应统一使用本字段。
        /// </summary>
        public FP64 FixedDt;

        /// <summary>
        /// Baumgarte 风格的位置修正系数：每步把超过 <see cref="PositionSlop"/>
        /// 的穿透深度按此比例进行位置纠正。
        /// </summary>
        public FP64 PositionCorrectionPercent;

        /// <summary>位置修正的容忍带宽（小于该深度不修正）。</summary>
        public FP64 PositionSlop;

        /// <summary>
        /// 碰撞后沿法线的最小分离速度下限。&gt; 0 时能让完全非弹性的接触
        /// 也能稳定分离（适合预测可视化）。
        /// </summary>
        public FP64 MinSeparationSpeed;

        /// <summary>
        /// 最小分离速度仅在穿透深度超过此值时施加（区分"真重叠"与"掠过接触"）。
        /// 默认 0 = 任意接触都施加（旧行为）。
        /// </summary>
        public FP64 MinSeparationPenetration;

        /// <summary>
        /// 进入休眠的速度平方阈值。
        /// 非静态体连续 <see cref="SleepFrames"/> 帧 <c>|v|² &lt;</c> 该值则进入休眠。
        /// </summary>
        public FP64 SleepLinearSpeedSqr;

        /// <summary>进入休眠所需的连续帧数。</summary>
        public int SleepFrames;

        /// <summary>初始的 body 数组容量；溢出时按 2 倍扩容。</summary>
        public int InitialBodyCapacity;

        /// <summary>
        /// 均匀网格 broadphase 的格子边长（世界单位）。
        /// 通常取典型刚体的直径即可获得最佳对数；自定义 broadphase 时该字段被忽略。
        /// </summary>
        public FP64 GridCellSize;

        /// <summary>
        /// 活跃体数低于该阈值时，网格 broadphase 直接退化为 O(N²) 暴力构建，
        /// 避免小规模场景的网格构建开销。
        /// </summary>
        public int BruteForceBodyThreshold;

        /// <summary>ContactSolver 合成两体 Restitution 的策略。</summary>
        public LWRestitutionCombine RestitutionCombine;

        /// <summary>
        /// 按 Layer 配对的恢复系数表（扁平 16×16，行=LayerA 列=LayerB，需对称填充）。
        /// 非 null 时 ContactSolver 优先查表；null 时回退 <see cref="RestitutionCombine"/>。
        /// </summary>
        public FP64[] RestitutionMatrix;

        /// <summary>默认配置：60Hz、Baumgarte 0.8 / slop 0.005、Grid 1m、Min 弹性合成。</summary>
        /// <remarks>
        /// 该默认值面向典型 idle 玩法 (≤ 30 体) 做了启动 / 稳态零浪费调优：
        /// <list type="bullet">
        /// <item><see cref="InitialBodyCapacity"/> = 32：直接覆盖典型场景的 body 数量，
        /// 避免 <c>AddBody</c> 第 17 个起触发的一次 <c>Array.Resize</c>。</item>
        /// <item><see cref="BruteForceBodyThreshold"/> = 32：alive ≤ 32 时网格 broadphase
        /// 跳过 cell hash 构建与 pair 排序，直接走 O(N²) 暴力枚举（实测与
        /// <see cref="BruteForceBroadphase"/> 等价）。大规模场景显式调低。</item>
        /// <item><see cref="SleepFrames"/> = 8：约 0.13s 静止即休眠，让稳态后的物体尽早从
        /// broadphase / solver 中淘汰。需要"轻推也立刻响应"的玩法可调到 16–30。</item>
        /// </list>
        /// </remarks>
        public static LWPhysicsConfig Default
        {
            get
            {
                return new LWPhysicsConfig
                {
                    FixedDt = FP64.FromFraction(1, 60),
                    PositionCorrectionPercent = FP64.FromFloat(0.8f),
                    PositionSlop = FP64.FromFloat(0.005f),
                    MinSeparationSpeed = FP64.Zero,
                    SleepLinearSpeedSqr = FP64.FromFloat(0.0004f),
                    SleepFrames = 8,
                    InitialBodyCapacity = 32,
                    GridCellSize = FP64.One,
                    BruteForceBodyThreshold = 32,
                    RestitutionCombine = LWRestitutionCombine.Min,
                };
            }
        }
    }
}
