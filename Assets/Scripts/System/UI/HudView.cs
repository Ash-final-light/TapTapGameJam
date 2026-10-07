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
        private float damageUntil;
        public void RefreshBindings(BindingService bindings, UnityEngine.InputSystem.InputActionAsset controls)
        {
            Skill1Key.text = bindings.Display(controls, "Skill1"); Skill2Key.text = bindings.Display(controls, "Skill2");
            UseKey.text = bindings.Display(controls, "ItemUse"); SelectorKey.text = "按住 " + bindings.Display(controls, "ItemSelect") + " 选择道具";
        }
        private void Awake()
        { for (int i=0;i<Slots.Length;i++) { int slot=i; Slots[i].Button.onClick.AddListener(() => SlotClicked?.Invoke(slot)); } }
        public void Render(HudSnapshot data)
        {
            if (data == null) { Selector.SetActive(false); return; }
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
            foreach(var item in data.Items) if(item!=null && item.Count>0) visibleItems.Add(item);
            for(int i=0;i<Slots.Length;i++) { if(i<visibleItems.Count) Slots[i].Show(visibleItems[i],i,visibleItems[i].Id==data.SelectedItemId); else Slots[i].gameObject.SetActive(false); }
            var equipped = visibleItems.Find(i => i.Id==data.SelectedItemId);
            EquippedIcon.enabled = UseIcon.enabled = equipped != null;
            EquippedIcon.sprite = UseIcon.sprite = equipped?.Icon;
            EquippedCount.text = UseCount.text = equipped?.Count.ToString() ?? "0";
            SelectionHint.text = visibleItems.Count==0 ? "暂无可选道具" : visibleItems.Count>10 ? "道具超出10个槽位，请调整配置" : "数字键选择";
        }
        private void Update()
        { if (damageUntil>0 && Time.unscaledTime>=damageUntil) { foreach(var heart in hearts) heart.color=Color.white; damageUntil=0; } }
    }
}
