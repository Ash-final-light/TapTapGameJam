using System;
using UnityEngine;

namespace TapTap.Danmaku
{
    [DisallowMultipleComponent]
    public sealed class DanmakuSequencePlayer : MonoBehaviour
    {
        public DanmakuSequence Sequence;
        public bool PlayOnEnable = true;
        public bool Loop;
        [Range(0.1f, 4)] public float PlaybackSpeed = 1;
        public DanmakuSimulation Simulation { get; private set; }
        public bool Playing { get; private set; }
        public event Action<ProjectileHit> OnProjectileHit;
        private DanmakuSpritePool pool;
        private Transform viewRoot;
        private double accumulator;

        private void OnEnable()
        {
            if (PlayOnEnable && Sequence) Play();
        }

        public void Play()
        {
            if (!Sequence) return;
            DisposeViews();
            Simulation = new DanmakuSimulation(Sequence);
            Simulation.OnHit += ForwardHit;
            var root = new GameObject("Danmaku Views");
            root.transform.SetParent(transform, false);
            viewRoot = root.transform;
            pool = new DanmakuSpritePool(viewRoot);
            pool.Sync(Simulation, Sequence);
            accumulator = 0; Playing = true;
        }

        public void Pause() => Playing = false;
        public void Resume() { if (Simulation != null && !Simulation.Finished && Simulation.Fault == null) Playing = true; }
        public void Seek(int tick)
        {
            if (Simulation == null) Play();
            if (Simulation == null) return;
            Simulation.Seek(tick); accumulator = 0;
            pool.Sync(Simulation, Sequence);
        }

        private void Update()
        {
            if (!Playing || Simulation == null || !Sequence) return;
            accumulator += Time.deltaTime * PlaybackSpeed;
            const double step = 1.0 / DanmakuSequence.TickRate;
            int budget = 240; // Keep delayed frames bounded without changing simulation dt.
            while (accumulator >= step && budget-- > 0)
            {
                accumulator -= step;
                if (Simulation.Finished)
                {
                    if (Loop) Simulation.Reset(Sequence);
                    else { Playing = false; break; }
                }
                Simulation.Step();
                if (Simulation.Fault != null)
                { Debug.LogError(Simulation.Fault, this); Playing = false; break; }
            }
            pool.Sync(Simulation, Sequence);
        }

        private void ForwardHit(ProjectileHit hit) => OnProjectileHit?.Invoke(hit);
        private void OnDisable() { Playing = false; DisposeViews(); Simulation = null; }
        private void DisposeViews()
        {
            if (Simulation != null) Simulation.OnHit -= ForwardHit;
            pool?.Dispose(); pool = null;
            if (viewRoot)
            {
                if (Application.isPlaying) Destroy(viewRoot.gameObject);
                else DestroyImmediate(viewRoot.gameObject);
            }
            viewRoot = null;
        }
    }
}
