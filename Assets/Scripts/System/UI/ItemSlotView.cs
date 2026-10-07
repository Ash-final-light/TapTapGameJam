using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class ItemSlotView : MonoBehaviour
    {
        public UnityEngine.UI.Image Icon, Border;
        public TMP_Text Number, Count;
        public UnityEngine.UI.Button Button;
        public string ItemId { get; private set; }
        public void Show(ItemViewData item, int index, bool selected)
        { ItemId = item.Id; Icon.sprite = item.Icon; Count.text = item.Count.ToString(); Number.text = index == 9 ? "0" : (index+1).ToString(); Border.enabled = selected; gameObject.SetActive(true); }
    }
}
