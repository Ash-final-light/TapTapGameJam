using UnityEngine;
using TMPro;

namespace TapTap.Systems.UI
{
    /// <summary>Switches the supplied difficulty states without changing source textures.</summary>
    public sealed class Package4SongSelectArt : MonoBehaviour
    {
        public UnityEngine.UI.RawImage[] DifficultyImages;
        public Texture2D[] ActiveTextures, InactiveTextures;
        public TMP_Text[] Labels;
        public RectTransform Content;

        private void OnEnable() => FitContent();
        private void OnRectTransformDimensionsChange() => FitContent();

        public void FitContent()
        {
            if (!Content) return;
            var viewport = ((RectTransform)transform).rect.size;
            float scale = Mathf.Clamp(Mathf.Min((viewport.x - 60) / 1660, (viewport.y - 170) / 900), .1f, 1);
            Content.localScale = Vector3.one * scale;
        }

        public void ShowDifficulty(int selected)
        {
            FitContent();
            for (int i = 0; i < DifficultyImages.Length; i++)
            {
                bool active = i == selected;
                DifficultyImages[i].texture = active ? ActiveTextures[i] : InactiveTextures[i];
                Labels[i].color = active ? Color.white : new Color(.12f, .1f, .28f);
            }
        }
    }
}
