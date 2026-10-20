using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class HudView : MonoBehaviour
    {
        public UnityEngine.UI.Image Portrait, Stamina, Skill1, Skill2, Microphone, EquippedIcon, UseIcon;
        public TMP_Text StaminaText, EquippedCount, UseCount, MicrophoneLabel, SelectionHint, Warning;
        public TMP_Text Skill1Key, Skill2Key, UseKey, SelectorKey;
        public TMP_Text ItemStatus, ItemCooldownText, ItemFeedback;
        public UnityEngine.UI.Image ItemCooldown;
        public UnityEngine.UI.Button PauseButton;
        public GameObject Selector;
        public RectTransform HeartsRoot;
        public UnityEngine.UI.Image HeartTemplate;
        public Sprite FullHeart, EmptyHeart;
        public ItemSlotView[] Slots;
        public Action<int> SlotClicked;
        public IReadOnlyList<ItemViewData> VisibleItems => visibleItems;
        private readonly List<ItemViewData> visibleItems = new();
        private readonly List<UnityEngine.UI.Image> hearts = new();
        private int previousHealth = -1;
        private float damageUntil, itemFeedbackUntil;
        public void RefreshBindings(BindingService bindings, UnityEngine.InputSystem.InputActionAsset controls)
        {
            Skill1Key.text = bindings.Display(controls, "Skill1"); Skill2Key.text = bindings.Display(controls, "Skill2");
            UseKey.text = bindings.Display(controls, "ItemUse"); SelectorKey.text = "按住 " + bindings.Display(controls, "ItemSelect") + " 选择道具";
        }
        private void Awake()
        { EnsureItemFeedback(); for (int i=0;i<Slots.Length;i++) { int slot=i; Slots[i].Button.onClick.AddListener(() => SlotClicked?.Invoke(slot)); } }
        public void EnsureItemFeedback()
        {
            if (!ItemStatus)
            {
                ItemStatus = Instantiate(EquippedCount, EquippedCount.transform.parent); ItemStatus.name = "ItemStatus";
                var rect = ItemStatus.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(.68f,.5f);
                rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(265,62);
                ItemStatus.fontSize = 20; ItemStatus.alignment = TextAlignmentOptions.Center; ItemStatus.raycastTarget = false;
            }
            if (!ItemCooldown)
            {
                ItemCooldown = Instantiate(UseIcon, UseIcon.transform.parent); ItemCooldown.name = "ItemCooldown";
                ItemCooldown.transform.SetSiblingIndex(UseIcon.transform.GetSiblingIndex()+1);
                ItemCooldown.color = new Color(0,0,0,.65f); ItemCooldown.raycastTarget = false;
                ItemCooldown.type = UnityEngine.UI.Image.Type.Filled; ItemCooldown.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            }
            if (!ItemCooldownText)
            {
                ItemCooldownText = Instantiate(UseCount, UseIcon.transform.parent); ItemCooldownText.name = "ItemCooldownText";
                var rect = ItemCooldownText.rectTransform; rect.anchorMin = UseIcon.rectTransform.anchorMin; rect.anchorMax = UseIcon.rectTransform.anchorMax;
                rect.pivot = UseIcon.rectTransform.pivot; rect.anchoredPosition = UseIcon.rectTransform.anchoredPosition; rect.sizeDelta = UseIcon.rectTransform.sizeDelta;
                ItemCooldownText.fontSize = 24; ItemCooldownText.color = Color.white; ItemCooldownText.alignment = TextAlignmentOptions.Center;
                ItemCooldownText.raycastTarget = false; ItemCooldownText.enableWordWrapping = false;
            }
            if (!ItemFeedback)
            {
                var card = new GameObject("ItemFeedbackCard", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                card.transform.SetParent(EquippedCount.transform.parent, false);
                var cardRect = (RectTransform)card.transform; cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f,0);
                cardRect.pivot = new Vector2(.5f,.5f); cardRect.anchoredPosition = new Vector2(0,-34); cardRect.sizeDelta = new Vector2(470,52);
                var background = card.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(1,1,1,.96f); background.raycastTarget = false;
                ItemFeedback = Instantiate(EquippedCount, card.transform); ItemFeedback.name = "ItemFeedback";
                var rect = ItemFeedback.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(10,4); rect.offsetMax = new Vector2(-10,-4);
                ItemFeedback.fontSize = 20; ItemFeedback.alignment = TextAlignmentOptions.Center; ItemFeedback.raycastTarget = false;
            }
            ItemStatus.text = ""; ItemCooldown.enabled = false; ItemCooldownText.text = "";
            ItemFeedback.transform.parent.gameObject.SetActive(false);
            foreach (var slot in Slots) if (slot) slot.EnsureFeedback();
        }
        public void ShowItemFeedback(ItemUseResult result)
        {
            if (!ItemFeedback) EnsureItemFeedback();
            ItemFeedback.text = result.Message;
            ItemFeedback.color = result.Success ? new Color(.05f,.4f,.25f) : new Color(.65f,.12f,.1f);
            ItemFeedback.transform.parent.gameObject.SetActive(true); itemFeedbackUntil = Time.unscaledTime + 2.5f;
        }
        public void Render(HudSnapshot data)
        {
            if (data == null) { Selector.SetActive(false); previousHealth = -1; damageUntil = 0;
                if(ItemFeedback) ItemFeedback.transform.parent.gameObject.SetActive(false);
                if(ItemStatus) ItemStatus.text=""; if(ItemCooldown) ItemCooldown.enabled=false; if(ItemCooldownText) ItemCooldownText.text=""; return; }
            Portrait.sprite = data.Portrait;
            int maxHealth = Mathf.Clamp(data.MaxHealth,0,100);
            while(hearts.Count < maxHealth) { var image = Instantiate(HeartTemplate,HeartsRoot); image.preserveAspect=true; image.gameObject.SetActive(true); hearts.Add(image); }
            if (previousHealth >= 0 && data.Health < previousHealth) damageUntil = Time.unscaledTime+0.3f;
            float heartWidth=Mathf.Clamp((HeartsRoot.rect.width-24-(maxHealth-1)*6)/Math.Max(1,maxHealth),1,50);
            for(int i=0;i<hearts.Count;i++) { hearts[i].GetComponent<UnityEngine.UI.LayoutElement>().preferredWidth=heartWidth; hearts[i].gameObject.SetActive(i<maxHealth); hearts[i].sprite = i<data.Health ? FullHeart : EmptyHeart;
                hearts[i].color = i>=data.Health && i<previousHealth ? new Color(1,0.45f,0.45f) : Color.white; }
            previousHealth = data.Health;
            float ratio = data.MaxStamina > 0 ? Mathf.Clamp01(data.Stamina/data.MaxStamina) : 0;
            Stamina.fillAmount = ratio; Stamina.color = ratio<0.3f ? new Color(1,0.35f,0.3f) : new Color(0.1f,0.75f,1);
            StaminaText.text = Mathf.RoundToInt(data.Stamina)+"/"+Mathf.RoundToInt(data.MaxStamina);
            Skill1.color = data.Skill1Color; Skill2.color = data.Skill2Color;
            Microphone.color = data.Microphone == MicrophoneState.On ? Color.white : data.Microphone == MicrophoneState.Off ? Color.gray : new Color(1,0.5f,0.3f);
            MicrophoneLabel.text = data.Microphone switch { MicrophoneState.On => "开启", MicrophoneState.Off => "关闭", _ => "未授权" };
            Warning.text = data.Warning ?? "";
            var warningRect = Warning.rectTransform; warningRect.anchorMin = warningRect.anchorMax = data.WarningViewport;
            visibleItems.Clear();
            // Keep equipped slots stable, including exhausted items and a final active buff.
            foreach(var item in data.Items) if(item!=null) visibleItems.Add(item);
            for(int i=0;i<Slots.Length;i++) { if(i<visibleItems.Count) Slots[i].Show(visibleItems[i],i,visibleItems[i].Id==data.SelectedItemId); else Slots[i].gameObject.SetActive(false); }
            var equipped = visibleItems.Find(i => i.Id==data.SelectedItemId);
            EquippedIcon.enabled = UseIcon.enabled = equipped != null;
            EquippedIcon.sprite = UseIcon.sprite = equipped?.Icon;
            EquippedCount.text = UseCount.text = equipped?.Count.ToString() ?? "0";
            EquippedIcon.color = UseIcon.color = equipped == null ? Color.gray : equipped.Usable ? equipped.IconColor : Color.Lerp(equipped.IconColor, Color.gray, .65f);
            if (ItemStatus) ItemStatus.text = equipped == null ? (visibleItems.Count == 0 ? "未装备道具" : "本局道具已用完") :
                equipped.Name + "\n" + (equipped.EffectRemainingTicks > 0 ? "效果剩余 " + ItemRuntime.Seconds(equipped.EffectRemainingTicks) + "秒" : equipped.UnavailableReason ?? "可使用");
            if (ItemCooldown) { ItemCooldown.enabled = equipped != null && equipped.CooldownRemainingTicks > 0; ItemCooldown.sprite = equipped?.Icon;
                ItemCooldown.fillAmount = equipped?.CooldownTicks > 0 ? Mathf.Clamp01((float)equipped.CooldownRemainingTicks / equipped.CooldownTicks) : 0; }
            if (ItemCooldownText) ItemCooldownText.text = equipped?.CooldownRemainingTicks > 0 ? ItemRuntime.Seconds(equipped.CooldownRemainingTicks) + "s" : "";
            SelectionHint.text = visibleItems.Count==0 ? "暂无可选道具" : visibleItems.Count>10 ? "道具超出10个槽位，请调整配置" : "数字键选择";
        }
        private void Update()
        {
            if (damageUntil>0 && Time.unscaledTime>=damageUntil) { foreach(var heart in hearts) heart.color=Color.white; damageUntil=0; }
            if (ItemFeedback && Time.unscaledTime >= itemFeedbackUntil) ItemFeedback.transform.parent.gameObject.SetActive(false);
        }
    }
}
