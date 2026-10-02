namespace Framework.FixedPoint
{
    public static class FPUtils
    {
        public static void Prewarm()
        {
            // Just call some methods to prewarm JIT and avoid first-call hiccups in hot code paths.
            _ = FPMath.Sin(FP64.Zero);
        }
    }
}
