// LWBodyFlags.cs

using System;

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>刚体状态位（按位组合）。</summary>
    [Flags]
    public enum LWBodyFlags : byte
    {
        /// <summary>无任何标记。</summary>
        None = 0,

        /// <summary>该 slot 已被分配并代表一个有效刚体。</summary>
        Alive = 1 << 0,

        /// <summary>静态体：不积分、不被位置修正推动。</summary>
        Static = 1 << 1,

        /// <summary>休眠中：跳过积分与碰撞，碰撞会唤醒。</summary>
        Sleeping = 1 << 2,

        /// <summary>触发器：参与检测但不进行碰撞解算，会产生 Enter/Stay/Exit 事件。</summary>
        Trigger = 1 << 3,
    }
}
