using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class ItemSlotView : MonoBehaviour
    {
        public UnityEngine.UI.Image Icon, Border;
        public TMP_Text Number, Count;
        public UnityEngine.UI.Button Button;
        public UnityEngine.UI.Image CooldownOverlay;
        public TMP_Text CooldownText;
        public string ItemId { get; private set; }
        public void EnsureFeedback()
        {
            if (!CooldownOverlay)
            {
                CooldownOverlay = Instantiate(Icon, Icon.transform); CooldownOverlay.name = "CooldownOverlay";
                var rect = CooldownOverlay.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                CooldownOverlay.color = new Color(0,0,0,.65f); CooldownOverlay.raycastTarget = false;
                CooldownOverlay.type = UnityEngine.UI.Image.Type.Filled; CooldownOverlay.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            }
            if (!CooldownText)
            {
                CooldownText = Instantiate(Number, Icon.transform); CooldownText.name = "CooldownText";
                var rect = CooldownText.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                CooldownText.color = Color.white; CooldownText.fontSize = 20; CooldownText.alignment = TextAlignmentOptions.Center;
                CooldownText.raycastTarget = false; CooldownText.enableWordWrapping = false;
            }
            CooldownOverlay.enabled = false; CooldownText.text = "";
        }
        public void Show(ItemViewData item, int index, bool selected)
        {
            ItemId = item.Id; Icon.sprite = item.Icon; Count.text = item.Count.ToString(); Number.text = index == 9 ? "0" : (index+1).ToString();
            Border.enabled = selected; Icon.color = item.Usable ? item.IconColor : Color.Lerp(item.IconColor, Color.gray, .65f);
            Button.interactable = item.Count > 0;
            if (CooldownOverlay) { CooldownOverlay.enabled = item.CooldownRemainingTicks > 0; CooldownOverlay.sprite = item.Icon;
                CooldownOverlay.fillAmount = item.CooldownTicks > 0 ? Mathf.Clamp01((float)item.CooldownRemainingTicks / item.CooldownTicks) : 0; }
            if (CooldownText) CooldownText.text = item.EffectRemainingTicks > 0 ? ItemRuntime.Seconds(item.EffectRemainingTicks) + "s" :
                item.Count <= 0 ? "已用完" : item.CooldownRemainingTicks > 0 ? ItemRuntime.Seconds(item.CooldownRemainingTicks) + "s" : "";
            gameObject.SetActive(true);
        }
    }
}
