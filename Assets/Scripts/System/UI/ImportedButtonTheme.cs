using System.Linq;
using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    /// <summary>Serialized art references also cover pages created at runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class ImportedButtonTheme : MonoBehaviour
    {
        public Sprite Face, Arrow, Icon, Frame;
        public Rect FaceBounds, ArrowBounds;

        public void ApplyAll()
        {
            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true)) Apply(button);
            foreach (var dropdown in GetComponentsInChildren<TMP_Dropdown>(true)) Apply(dropdown);
        }

        public void Apply(UnityEngine.UI.Selectable button)
        {
            if (!Face || !Arrow || !Icon) return;
            // These already use the imported row, difficulty and back artwork.
            if (button.transform.Find("ImportedRowArtwork") || button.transform.Find("ImportedDifficultyFrame") ||
                button.transform.Find("ImportedBackArtwork")) return;
            var hit = button.GetComponent<UnityEngine.UI.Image>();
            if (!hit) return;
            var style = button.GetComponent<ImportedButtonArtwork>();
            if (!style) style = button.gameObject.AddComponent<ImportedButtonArtwork>();
            var labels = button.GetComponentsInChildren<TMP_Text>(true);
            foreach (var arrowLabel in labels.Where(t => t.name == "Arrow"))
            {
                arrowLabel.enabled = false;
                if (arrowLabel.transform.Find("ImportedDropdownArrow")) continue;
                var arrowObject = new GameObject("ImportedDropdownArrow", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                arrowObject.transform.SetParent(arrowLabel.transform, false);
            }
            string label = labels.FirstOrDefault() ? labels[0].text.Trim() : "";
            bool symbol = label == "▲" || label == "▼" || label == "◀" || label == "▶" || label == "☰";
            var existing = button.transform.Find("ImportedButtonArtwork");
            var art = existing ? existing.GetComponent<UnityEngine.UI.Image>() : null;
            if (!art)
            {
                var go = new GameObject("ImportedButtonArtwork", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                go.transform.SetParent(button.transform, false); art = go.GetComponent<UnityEngine.UI.Image>();
                var layout = go.AddComponent<UnityEngine.UI.LayoutElement>(); layout.ignoreLayout = true;
            }
            art.sprite = symbol ? Arrow : Face; art.color = Color.white;
            art.raycastTarget = false; art.type = UnityEngine.UI.Image.Type.Simple;
            art.transform.SetAsFirstSibling();
            style.Button = button; style.HitArea = hit; style.Artwork = art;
            style.Labels = labels; style.Icons = button.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name == "Icon").ToArray();
            style.Borders = button.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name == "Border" || i.name == "SelectedBorder").ToArray();
            style.FrameSprite = Frame;
            style.Arrows = button.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name == "Arrow" || i.name == "ImportedDropdownArrow").ToArray();
            style.ArrowSprite = Arrow;
            style.IconSprite = Icon; style.SourceBounds = symbol ? ArrowBounds : FaceBounds;
            style.SymbolOnly = symbol; style.Rotation = label == "▲" ? -90 : label == "▼" ? 90 : label == "▶" ? 180 : 0;
            button.targetGraphic = art; button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .83f, .94f);
            colors.selectedColor = new Color(.86f, .88f, 1); colors.pressedColor = new Color(.65f, .7f, 1);
            colors.disabledColor = new Color(.65f, .65f, .7f, .4f); colors.colorMultiplier = 1; colors.fadeDuration = .08f;
            button.colors = colors;
            style.RefreshVisual();
        }
    }
}
