using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    /// <summary>Keeps imported artwork fitted when layout groups resize or clone a button.</summary>
    [DisallowMultipleComponent]
    public sealed class ImportedButtonArtwork : MonoBehaviour
    {
        public UnityEngine.UI.Selectable Button;
        public UnityEngine.UI.Image Artwork, HitArea;
        public UnityEngine.UI.Image[] Icons;
        public UnityEngine.UI.Image[] Borders;
        public UnityEngine.UI.Image[] Arrows;
        public TMP_Text[] Labels;
        public Sprite IconSprite;
        public Sprite FrameSprite;
        public Sprite ArrowSprite;
        public Rect SourceBounds;
        public float Rotation;
        public bool SymbolOnly;
        private static readonly Color Ink = new(.12f, .1f, .28f);

        private void OnEnable() => RefreshVisual();
        private void OnRectTransformDimensionsChange() => Fit();
        private void LateUpdate() => RefreshVisual();

        public void RefreshVisual()
        {
            if (!Button || !Artwork || !HitArea) return;
            // Existing equipment/list controllers still own this image's logical state.
            // Keep it as the hit area while rendering the new artwork in a separate child.
            HitArea.sprite = null; HitArea.color = Color.clear; HitArea.raycastTarget = true;
            foreach (var label in Labels)
            {
                if (!label) continue;
                label.color = Ink; label.raycastTarget = false;
                if (SymbolOnly) label.enabled = false;
            }
            foreach (var icon in Icons)
            {
                if (!icon) continue;
                icon.sprite = IconSprite; icon.color = Color.white; icon.preserveAspect = true; icon.raycastTarget = false;
            }
            foreach (var border in Borders)
            {
                if (!border) continue;
                border.sprite = FrameSprite; border.color = Color.white; border.type = UnityEngine.UI.Image.Type.Simple;
                border.raycastTarget = false;
            }
            foreach (var arrow in Arrows)
            {
                if (!arrow) continue;
                arrow.sprite = ArrowSprite; arrow.color = Color.white; arrow.type = UnityEngine.UI.Image.Type.Simple;
                arrow.preserveAspect = true; arrow.raycastTarget = false;
                arrow.rectTransform.sizeDelta = new Vector2(32, 32); arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            Fit();
        }

        private void Fit()
        {
            if (!Artwork || SourceBounds.width <= 0 || SourceBounds.height <= 0) return;
            var rect = (RectTransform)transform;
            var visible = rect.rect.size;
            if (SymbolOnly) visible = Vector2.one * Mathf.Max(1, Mathf.Min(visible.x, visible.y) * .8f);
            var size = new Vector2(visible.x / SourceBounds.width, visible.y / SourceBounds.height);
            var art = Artwork.rectTransform;
            art.anchorMin = art.anchorMax = art.pivot = Vector2.one * .5f;
            art.sizeDelta = size;
            var offset = -Vector2.Scale(SourceBounds.center - Vector2.one * .5f, size);
            art.localRotation = Quaternion.Euler(0, 0, Rotation);
            art.anchoredPosition = Quaternion.Euler(0, 0, Rotation) * offset;
        }
    }
}
