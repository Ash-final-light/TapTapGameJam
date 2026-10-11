using System;
using System.Collections.Generic;
using TapTap.Danmaku;
using TMPro;
using UnityEngine;

namespace TapTap.Systems.UI
{
    public sealed class LoadoutView : MonoBehaviour
    {
        public UnityEngine.UI.Button Back, StartButton, OptionTemplate, SlotTemplate;
        public RectTransform WeaponsRoot, ItemsRoot, EquippedRoot;
        public TMP_Text Description, WeaponLabel;
        public UnityEngine.UI.Button EquippedWeapon;
        [Min(.1f)] public float DoubleClickSeconds = .3f;
        public PlayerWeaponDefinition SelectedWeapon { get; private set; }
        public IReadOnlyList<ItemDefinition> Equipment => equipment;
        private SystemRoot root;
        private readonly List<ItemDefinition> equipment = new();
        private readonly List<GameObject> generated = new();
        private readonly Dictionary<UnityEngine.UI.Outline, string> borders = new();
        private string selectedKey, lastClickKey;
        private float lastClickAt;
        private static readonly Color EquippedColor = new(.95f, .55f, .1f), AvailableColor = new(.13f, .22f, .32f), LockedColor = new(.73f, .75f, .77f);

        public void Bind(SystemRoot owner)
        {
            root = owner;
            EnsureLayout();
            OptionTemplate.gameObject.SetActive(false); SlotTemplate.gameObject.SetActive(false);
            Back.onClick.AddListener(() => root.UI.OpenSongSelection()); StartButton.onClick.AddListener(StartRun);
            EquippedWeapon.onClick.AddListener(() => Click("equipped-weapon", () => ShowWeapon(SelectedWeapon), () => { SelectedWeapon = null; Refresh(); }));
        }
        public void ResetDraft() { equipment.Clear(); SelectedWeapon = null; selectedKey = lastClickKey = null; Description.text = ""; }
        private bool Usable(PlayerWeaponDefinition weapon) => weapon && weapon.Unlocked && weapon.Mode != PlayerWeaponMode.Homing && weapon.Projectile && weapon.FireRate > 0 && weapon.FireRate <= 60;
        public void Open()
        {
            equipment.RemoveAll(item => !item || root.Saves.Quantity(item.Id) <= 0 || Array.IndexOf(root.Config.Items, item) < 0);
            if (!Usable(SelectedWeapon) || Array.IndexOf(root.Config.Weapons, SelectedWeapon) < 0) SelectedWeapon = null;
            selectedKey = lastClickKey = null; Description.text = "";
            Refresh();
        }
        private void Click(string key, Action preview, Action change)
        {
            bool twice = lastClickKey == key && Time.unscaledTime - lastClickAt <= DoubleClickSeconds;
            selectedKey = key; preview(); UpdateBorders();
            lastClickKey = twice ? null : key; lastClickAt = Time.unscaledTime;
            if (twice) change();
        }
        private void ShowWeapon(PlayerWeaponDefinition weapon)
        { Description.text = weapon ? weapon.Label + "\n" + weapon.Description : ""; }
        private void ShowItem(ItemDefinition item)
        { Description.text = item ? item.DisplayName + "\n" + item.Description : ""; }
        private UnityEngine.UI.Button Make(UnityEngine.UI.Button template, Transform parent, string key, string label, Action preview, Action change, Color color, Sprite icon = null, bool empty = false)
        {
            var button = Instantiate(template, parent); button.name = key; generated.Add(button.gameObject);
            button.gameObject.SetActive(true); button.onClick.RemoveAllListeners(); button.interactable = true;
            button.GetComponent<UnityEngine.UI.Image>().color = color;
            button.onClick.AddListener(() => Click(key, preview, change));
            Render(button, label, icon, empty);
            Border(button, key);
            return button;
        }
        private static void Render(UnityEngine.UI.Button button, string label, Sprite icon, bool empty)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true); text.text = label;
            text.color = empty ? new Color(.91f,.93f,.95f) : Color.white;
            text.fontSize = empty ? 44 : 24;
            text.enableAutoSizing = !empty; text.fontSizeMin = 16; text.fontSizeMax = 24;
            text.alignment = empty ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Center;
            text.rectTransform.offsetMin = new Vector2(8,6); text.rectTransform.offsetMax = new Vector2(-8, empty ? -6 : -50);
            var image = button.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
            if (image) { image.sprite = icon; image.enabled = icon && !empty; image.color = Color.white; }
        }
        private void Border(UnityEngine.UI.Button button, string key)
        {
            var outline = button.GetComponent<UnityEngine.UI.Outline>() ?? button.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(.04f,.62f,.82f); outline.useGraphicAlpha = false; outline.effectDistance = new Vector2(4,-4);
            borders[outline] = key;
        }
        private void UpdateBorders()
        { foreach (var entry in borders) if (entry.Key) entry.Key.enabled = entry.Value == selectedKey; }
        private void Refresh()
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            string focus = events && events.currentSelectedGameObject && generated.Contains(events.currentSelectedGameObject) ? events.currentSelectedGameObject.name : null;
            if (focus != null) events.SetSelectedGameObject(null);
            foreach (var go in generated) { go.SetActive(false); Destroy(go); }
            generated.Clear(); borders.Clear(); lastClickKey = null;
            foreach (var weapon in root.Config.Weapons)
            {
                if (!weapon || weapon.Mode == PlayerWeaponMode.Homing) continue;
                bool usable = Usable(weapon);
                Make(OptionTemplate, WeaponsRoot, "weapon-" + weapon.GetInstanceID(), (weapon == SelectedWeapon ? "✓ " : "") + weapon.Label,
                    () => ShowWeapon(weapon), () => { if (usable) { SelectedWeapon = weapon; Refresh(); } },
                    !usable ? LockedColor : weapon == SelectedWeapon ? EquippedColor : AvailableColor, weapon.Projectile ? weapon.Projectile.Settings.Sprite : null);
            }
            foreach (var item in root.Config.Items)
            {
                if (!item) continue;
                int quantity = root.Saves.Quantity(item.Id); bool equipped = equipment.Contains(item);
                Make(OptionTemplate, ItemsRoot, "item-" + item.Id, (equipped ? "✓ " : "") + item.DisplayName + "\n" + (quantity > 0 ? "已获得 ×" + quantity : "未获得"),
                    () => ShowItem(item), () =>
                    {
                        if (quantity <= 0 || !item.Effect || equipped) return;
                        if (equipment.Count >= Math.Min(3, root.Config.ItemSlotCount)) { root.UI.Toast("道具槽已满，请先卸下一个道具。"); return; }
                        equipment.Add(item); Refresh();
                    }, quantity > 0 ? equipped ? EquippedColor : AvailableColor : LockedColor, item.Icon);
            }
            for (int i = 0; i < Math.Min(3, root.Config.ItemSlotCount); i++)
            {
                var item = i < equipment.Count ? equipment[i] : null;
                Make(SlotTemplate, EquippedRoot, "slot-" + i, item ? item.DisplayName : "+", () => ShowItem(item),
                    () => { if (item) { equipment.Remove(item); Refresh(); } }, item ? AvailableColor : LockedColor, item ? item.Icon : null, !item);
            }
            WeaponLabel.text = "已装备弹幕";
            EquippedWeapon.GetComponent<UnityEngine.UI.Image>().color = SelectedWeapon ? AvailableColor : LockedColor;
            Render(EquippedWeapon, SelectedWeapon ? SelectedWeapon.Label : "+", SelectedWeapon && SelectedWeapon.Projectile ? SelectedWeapon.Projectile.Settings.Sprite : null, !SelectedWeapon);
            Border(EquippedWeapon, "equipped-weapon"); UpdateBorders();
            StartButton.interactable = Usable(SelectedWeapon);
            StartButton.GetComponent<UnityEngine.UI.Image>().color = StartButton.interactable ? new Color(.04f,.62f,.82f) : LockedColor;
            var startColors = StartButton.colors; startColors.disabledColor = Color.white; StartButton.colors = startColors;
            if (focus != null)
            {
                var target = generated.Find(go => go.name == focus);
                events.SetSelectedGameObject(target ? target : Back.gameObject);
            }
        }
        private void StartRun()
        {
            if (!Usable(SelectedWeapon)) return;
            var song = root.UI.Songs.SelectedSong; if (!song) return;
            var setup = new BattleStartData(song, song.Find(root.UI.Songs.SelectedDifficulty), SelectedWeapon, equipment.ToArray());
            var result = root.Scenes.StartRun(setup); if (!result.Success) root.UI.Toast(result.Error);
        }

        // Upgrade the existing hierarchy in place; also used by the Editor installer.
        public void EnsureLayout()
        {
            var left = WeaponLabel.transform.parent;
            WeaponLabel.rectTransform.sizeDelta = new Vector2(710,50); WeaponLabel.rectTransform.anchoredPosition = new Vector2(0,290);
            if (!EquippedWeapon)
            {
                EquippedWeapon = Instantiate(SlotTemplate, left); EquippedWeapon.name = "EquippedWeaponSlot";
                EquippedWeapon.onClick.RemoveAllListeners();
            }
            EquippedWeapon.gameObject.SetActive(true);
            var equippedRect = (RectTransform)EquippedWeapon.transform;
            equippedRect.sizeDelta = new Vector2(215,110); equippedRect.anchoredPosition = new Vector2(0,200);
            var equippedIcon = EquippedWeapon.transform.Find("Icon") as RectTransform;
            if (equippedIcon) { equippedIcon.sizeDelta = new Vector2(50,40); equippedIcon.anchoredPosition = new Vector2(0,30); }
            Render(EquippedWeapon,"+",null,true);
            var heading = left.Find("EquippedItemsHeading") as RectTransform;
            if (heading) heading.anchoredPosition = new Vector2(0,100);
            Description.text = ""; Description.rectTransform.sizeDelta = new Vector2(690,175);
            Description.rectTransform.anchoredPosition = new Vector2(0,-220);
            var weaponsGrid = WeaponsRoot.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            weaponsGrid.constraintCount = 2;
            var scroll = ItemsRoot.GetComponentInParent<UnityEngine.UI.ScrollRect>(true);
            if (!scroll)
            {
                var parent = ItemsRoot.parent;
                var scrollObject = new GameObject("ItemsScroll", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.ScrollRect));
                var rect = (RectTransform)scrollObject.transform; rect.SetParent(parent,false);
                rect.sizeDelta = ItemsRoot.sizeDelta; rect.anchoredPosition = ItemsRoot.anchoredPosition;
                var hit = scrollObject.GetComponent<UnityEngine.UI.Image>(); hit.color = Color.clear; hit.raycastTarget = true;
                var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask));
                var viewport = (RectTransform)viewportObject.transform; viewport.SetParent(rect,false);
                viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = viewport.offsetMax = Vector2.zero;
                viewportObject.GetComponent<UnityEngine.UI.Image>().color = Color.white;
                viewportObject.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                ItemsRoot.SetParent(viewport,false); ItemsRoot.anchorMin = new Vector2(0,1); ItemsRoot.anchorMax = Vector2.one;
                ItemsRoot.pivot = new Vector2(.5f,1); ItemsRoot.anchoredPosition = Vector2.zero; ItemsRoot.sizeDelta = new Vector2(0,310);
                var grid = ItemsRoot.GetComponent<UnityEngine.UI.GridLayoutGroup>(); grid.childAlignment = TextAnchor.UpperCenter;
                grid.padding = new RectOffset(10,10,10,10);
                var fitter = ItemsRoot.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>(); fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                scroll = scrollObject.GetComponent<UnityEngine.UI.ScrollRect>(); scroll.viewport = viewport; scroll.content = ItemsRoot;
                scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            }
        }
    }
}
