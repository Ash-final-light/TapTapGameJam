using UnityEngine;

namespace TapTap.Systems.UI
{
    /// <summary>Shares the saved menu palette with pages constructed at runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class PopBackgroundTheme : MonoBehaviour
    {
        public Material Paper, Card;

        public void ApplyDynamicPages(SystemUIController ui)
        {
            foreach (var page in new Component[] { ui.ImportedMusic, ui.Alignment, ui.CustomSong })
            {
                if (!page) continue;
                var background = page.GetComponent<UnityEngine.UI.Image>();
                if (background)
                {
                    background.material = Paper;
                    background.color = Color.white;
                }
                foreach (var image in page.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (image.name == "DetailsCard" || image.name == "SongList")
                    {
                        image.material = Card;
                        image.color = Color.white;
                    }
                }
            }
        }
    }
}
