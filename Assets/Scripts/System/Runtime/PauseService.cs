using System;
using System.Collections.Generic;

namespace TapTap.Systems
{
    public sealed class PauseService
    {
        private readonly Dictionary<IPauseTarget, bool> targets = new();
        private int count;
        public bool IsPaused => count > 0;
        public event Action<bool> Changed;
        public void Register(IPauseTarget target)
        {
            if (targets.ContainsKey(target)) return;
            bool running = target.IsRunning; targets[target] = running;
            if (IsPaused && running) target.Pause();
        }
        public void Unregister(IPauseTarget target) => targets.Remove(target);
        public IDisposable Acquire()
        {
            if (count++ == 0)
            {
                var keys = new List<IPauseTarget>(targets.Keys);
                foreach (var target in keys) { targets[target] = target.IsRunning; if (target.IsRunning) target.Pause(); }
                Changed?.Invoke(true);
            }
            return new Lease(this);
        }
        private void Release()
        {
            if (count <= 0 || --count > 0) return;
            foreach (var entry in targets) if (entry.Value) entry.Key.Resume();
            Changed?.Invoke(false);
        }
        private sealed class Lease : IDisposable
        {
            private PauseService owner;
            public Lease(PauseService owner) { this.owner = owner; }
            public void Dispose() { var value = owner; owner = null; value?.Release(); }
        }
    }
}
