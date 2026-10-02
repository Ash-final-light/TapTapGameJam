// LWLayerMask.cs
//
// Lightweight 16-bit layer/mask filter.

namespace Framework.Physics.LightweightPhysics
{
    /// <summary>
    /// 16 位的 Layer / Mask 过滤工具。每个体属于至多 16 个层中的某一个，
    /// 同时配套一个 Mask 表示愿意与哪些层发生碰撞；两体互相对应才视作可碰撞。
    /// </summary>
    public static class LWLayerMask
    {
        /// <summary>全部 16 层都开。</summary>
        public const ushort AllLayers = 0xFFFF;

        /// <summary>所有层都关。</summary>
        public const ushort NoLayer = 0x0000;

        /// <summary>层总数（16 位）。</summary>
        public const int LayerCount = 16;

        /// <summary>取第 <paramref name="layer"/> 位（合法范围 0..15，越界返回 0）。</summary>
        public static ushort LayerBit(int layer)
        {
            if (layer < 0 || layer > 15) return 0;
            return (ushort)(1 << layer);
        }

        /// <summary>单 bit 的 Layer → 序号（0..15）；非单 bit 取最低位，0 返回 0。</summary>
        public static int LayerIndex(ushort layerBit)
        {
            for (int i = 0; i < LayerCount; i++)
                if ((layerBit & (1 << i)) != 0) return i;
            return 0;
        }

        /// <summary>
        /// 两体是否互相可见：
        /// 当且仅当 <c>(layerA &amp; maskB) != 0</c> 且 <c>(layerB &amp; maskA) != 0</c> 时返回 true。
        /// </summary>
        public static bool ShouldCollide(ushort layerA, ushort maskA, ushort layerB, ushort maskB)
        {
            return (layerA & maskB) != 0 && (layerB & maskA) != 0;
        }
    }
}
