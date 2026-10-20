using UnityEngine;

namespace TapTap.MusicPresentation
{
    /// <summary>Keeps the geometric stage backdrop aligned with the visible orthographic field.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
    public sealed class StageBackdropFit : MonoBehaviour
    {
        public Camera Target;
        private SpriteRenderer backdrop;

        private void OnEnable()
        {
            backdrop = GetComponent<SpriteRenderer>();
            Fit();
        }

        private void LateUpdate() => Fit();

        private void Fit()
        {
            if (!Target || !Target.orthographic) return;
            if (!backdrop) backdrop = GetComponent<SpriteRenderer>();
            if (!backdrop.sprite) return;
            var bounds = backdrop.sprite.bounds.size;
            float height = Target.orthographicSize * 2;
            transform.SetPositionAndRotation(Target.transform.position + Target.transform.forward * 20, Target.transform.rotation);
            transform.localScale = new Vector3(
                height * Target.aspect / Mathf.Max(.001f, bounds.x),
                height / Mathf.Max(.001f, bounds.y), 1);
        }
    }
}
