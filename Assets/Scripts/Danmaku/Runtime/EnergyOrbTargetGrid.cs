using System;
using System.Collections.Generic;
using Framework.FixedPoint;

namespace TapTap.Danmaku
{
    /// <summary>Only shootable mother orbs enter this index. Bounds cover both swept paths.</summary>
    internal sealed class EnergyOrbTargetGrid
    {
        private const int MaxCellsPerSweep = 4096;
        private readonly Dictionary<(int, int), List<int>> cells = new();
        private readonly Stack<List<int>> recycled = new();
        private readonly List<int> oversized = new(), all = new();
        private readonly struct Bounds
        {
            public readonly int MinX, MinY, MaxX, MaxY;
            public bool Oversized => ((long)MaxX - MinX + 1) * ((long)MaxY - MinY + 1) > MaxCellsPerSweep;
            public Bounds(ProjectileState p)
            {
                FP64 radius = FPMath.Max(p.PreviousRadius, p.HitRadius);
                MinX = Cell(FPMath.Min(p.PreviousPosition.X, p.Position.X) - radius);
                MaxX = Cell(FPMath.Max(p.PreviousPosition.X, p.Position.X) + radius);
                MinY = Cell(FPMath.Min(p.PreviousPosition.Y, p.Position.Y) - radius);
                MaxY = Cell(FPMath.Max(p.PreviousPosition.Y, p.Position.Y) + radius);
            }
        }
        // Arithmetic shift floors negative positions, using 2-world-unit cells.
        private static int Cell(FP64 value) => (int)(value.Raw >> 33);
        public void Rebuild(IReadOnlyList<ProjectileState> bullets)
        {
            foreach (var list in cells.Values) { list.Clear(); recycled.Push(list); }
            cells.Clear(); oversized.Clear(); all.Clear();
            for (int i = 0; i < bullets.Count; i++)
            {
                var p = bullets[i];
                if (!p.IsEnergyOrb || p.Health <= FP64.Zero) continue;
                all.Add(p.Id); var b = new Bounds(p);
                if (b.Oversized) { oversized.Add(p.Id); continue; }
                for (int x = b.MinX; x <= b.MaxX; x++)
                for (int y = b.MinY; y <= b.MaxY; y++)
                {
                    if (!cells.TryGetValue((x, y), out var list))
                    { list = recycled.Count > 0 ? recycled.Pop() : new List<int>(4); cells.Add((x, y), list); }
                    list.Add(p.Id);
                }
            }
        }
        public void Query(ProjectileState p, HashSet<int> candidates)
        {
            candidates.Clear(); if (all.Count == 0) return;
            var b = new Bounds(p);
            // Bound grid work even for extreme speeds; still only query orb targets.
            if (b.Oversized) { foreach (int id in all) candidates.Add(id); return; }
            foreach (int id in oversized) candidates.Add(id);
            for (int x = b.MinX; x <= b.MaxX; x++)
            for (int y = b.MinY; y <= b.MaxY; y++)
                if (cells.TryGetValue((x, y), out var list)) foreach (int id in list) candidates.Add(id);
        }
    }
}
