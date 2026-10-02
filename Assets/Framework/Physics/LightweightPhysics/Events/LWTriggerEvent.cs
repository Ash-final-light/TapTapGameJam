// LWTriggerEvent.cs

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>触发器事件的阶段：进入 / 停留 / 离开。</summary>
    public enum LWTriggerPhase : byte
    {
        /// <summary>本帧首次产生重叠。</summary>
        Enter = 0,

        /// <summary>上一帧也重叠，本帧仍重叠。</summary>
        Stay = 1,

        /// <summary>上一帧重叠，本帧已不重叠。</summary>
        Exit = 2,
    }

    /// <summary>
    /// 触发器事件。<see cref="BodyA"/> 始终是两个 body ID 中较小者，
    /// 这样配对能以规范形式存储，事件列表跨运行可重现。
    /// </summary>
    public struct LWTriggerEvent
    {
        /// <summary>事件阶段。</summary>
        public LWTriggerPhase Phase;

        /// <summary>较小的那个 body ID。</summary>
        public int BodyA;

        /// <summary>较大的那个 body ID。</summary>
        public int BodyB;
    }
}
