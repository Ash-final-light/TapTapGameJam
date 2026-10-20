using UnityEngine;

namespace TapTap.VFX
{
    /// <summary>Plays a supplied burst once in game time, then releases the instance.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteSequenceEffect : MonoBehaviour
    {
        public Sprite[] Frames;
        [Min(1)] public float FramesPerSecond = 15;
        private SpriteRenderer target;
        private float elapsed;
        private bool paused;

        public void SetPaused(bool value) => paused = value;

        private void OnEnable()
        {
            target = GetComponent<SpriteRenderer>();
            elapsed = 0;
            paused = false;
            if (Frames != null && Frames.Length > 0) target.sprite = Frames[0];
        }

        private void Update()
        {
            if (paused) return;
            if (Frames == null || Frames.Length == 0) { Destroy(gameObject); return; }
            elapsed += Time.deltaTime;
            int frame = Mathf.FloorToInt(elapsed * Mathf.Max(1, FramesPerSecond));
            if (frame >= Frames.Length) { Destroy(gameObject); return; }
            target.sprite = Frames[frame];
        }
    }
}
