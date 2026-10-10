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
        public PlayerWeaponDefinition SelectedWeapon { get; private set; }
        private SystemRoot root;
        private readonly List<ItemDefinition> equipment = new();
        private readonly List<GameObject> generated = new();
        public void Bind(SystemRoot owner)
        {
            root = owner; OptionTemplate.gameObject.SetActive(false); SlotTemplate.gameObject.SetActive(false);
            Back.onClick.AddListener(() => root.UI.OpenSongSelection()); StartButton.onClick.AddListener(StartRun);
        }
        public void ResetDraft() { equipment.Clear(); SelectedWeapon = null; }
        public void Open()
        {
            equipment.RemoveAll(item => !item || root.Saves.Quantity(item.Id) <= 0);
            if (!SelectedWeapon || Array.IndexOf(root.Config.Weapons, SelectedWeapon) < 0) SelectedWeapon = Array.Find(root.Config.Weapons, w => w && w.Mode != PlayerWeaponMode.Homing);
            Refresh();
        }
        private UnityEngine.UI.Button Make(UnityEngine.UI.Button template, Transform parent, string label, Action action, bool interactable = true, Sprite icon = null)
        {
            var button = Instantiate(template, parent); button.name = parent.name + "Option" + generated.Count; generated.Add(button.gameObject); button.gameObject.SetActive(true); button.onClick.RemoveAllListeners();
            button.GetComponentInChildren<TMP_Text>().text = label; button.interactable = interactable; button.onClick.AddListener(() => action());
            var image = button.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>(); if (image) { image.sprite = icon; image.enabled = icon; }
            return button;
        }
        private void Refresh()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            string focus = selected && generated.Contains(selected) ? selected.name : null;
            if (focus != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            foreach (var go in generated) { go.SetActive(false); Destroy(go); } generated.Clear();
            foreach (var weapon in root.Config.Weapons)
            {
                if (!weapon) continue;
                bool usable = weapon.Mode != PlayerWeaponMode.Homing && weapon.Projectile;
                var button = Make(OptionTemplate, WeaponsRoot, (weapon == SelectedWeapon ? "✓ " : "") + weapon.Label, () => { SelectedWeapon = weapon; Description.text = $"玩家弹幕：{weapon.Label}\n每秒发射：{weapon.FireRate} 轮"; Refresh(); }, usable, weapon.Projectile ? weapon.Projectile.Settings.Sprite : null);
                button.GetComponent<UnityEngine.UI.Image>().color = weapon == SelectedWeapon ? new Color(.95f, .55f, .1f) : new Color(.13f, .22f, .32f);
            }
            foreach (var item in root.Config.Items)
            {
                if (!item) continue; int quantity = root.Saves.Quantity(item.Id); bool equipped = equipment.Contains(item);
                Make(OptionTemplate, ItemsRoot, $"{(equipped ? "✓ " : "")}{item.DisplayName}\n{(quantity > 0 ? "已获得 ×" + quantity : "未获得")}", () =>
                {
                    Description.text = $"{item.DisplayName} · {(item.Passive ? "被动" : "主动")}\n{item.Description}";
                    if (equipped) equipment.Remove(item);
                    else if (equipment.Count < root.Config.ItemSlotCount) equipment.Add(item);
                    else root.UI.Toast("道具槽已满，请先卸下一个道具。");
                    Refresh();
                }, quantity > 0 && item.Effect, item.Icon);
            }
            for (int i = 0; i < root.Config.ItemSlotCount; i++)
            {
                var item = i < equipment.Count ? equipment[i] : null;
                Make(SlotTemplate, EquippedRoot, item ? item.DisplayName + "\n点击卸下" : "空槽", () => { if (item) { equipment.Remove(item); Description.text = item.Description; Refresh(); } }, item, item ? item.Icon : null);
            }
            WeaponLabel.text = SelectedWeapon ? "已装备弹幕：" + SelectedWeapon.Label : "请选择弹幕";
            StartButton.interactable = SelectedWeapon && SelectedWeapon.Mode != PlayerWeaponMode.Homing;
            if (focus != null)
            {
                var target = generated.Find(go => go.name == focus && go.GetComponent<UnityEngine.UI.Button>().interactable);
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(target ? target : StartButton.gameObject);
            }
        }
        private void StartRun()
        {
            var song = root.UI.Songs.SelectedSong; if (!song) return;
            var setup = new BattleStartData(song, song.Find(root.UI.Songs.SelectedDifficulty), SelectedWeapon, equipment.ToArray());
            var result = root.Scenes.StartRun(setup); if (!result.Success) root.UI.Toast(result.Error);
        }
    }
}
