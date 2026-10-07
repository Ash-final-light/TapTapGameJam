using System;
using UnityEngine;

namespace TapTap.Systems
{
    public sealed class HudDebugDataSource : MonoBehaviour, IHudDataSource, IGameplayRequestSink, IProgressDataProvider
    {
        public Sprite Portrait;
        [Range(0,20)] public int Health = 5, MaxHealth = 5;
        [Min(0)] public float Stamina = 100, MaxStamina = 100;
        public Color Skill1Color = new(0.35f,0.75f,1), Skill2Color = new(1,0.75f,0.3f);
        public ItemViewData[] Items = Array.Empty<ItemViewData>();
        public string SelectedItemId, Warning;
        public MicrophoneState Microphone;
        public int DebugGrowth;
        public event Action<HudSnapshot> Changed;
        public HudSnapshot Current => new()
        { Portrait = Portrait, Health = Health, MaxHealth = MaxHealth, Stamina = Stamina, MaxStamina = MaxStamina,
            Skill1Color = Skill1Color, Skill2Color = Skill2Color, Items = Items, SelectedItemId = SelectedItemId, Warning = Warning, Microphone = Microphone };
        private void Start()
        {
            if (!SystemRoot.Instance) return;
            SystemRoot.Instance.AttachGameplay(this, this, this);
        }
        private void OnDestroy() { if (SystemRoot.Instance) SystemRoot.Instance.DetachGameplay(this); }
        private void OnValidate()
        { Health = Mathf.Clamp(Health, 0, Math.Max(0, MaxHealth)); Stamina = Mathf.Clamp(Stamina, 0, Math.Max(0, MaxStamina)); if (Application.isPlaying) Publish(); }
        public void Publish() => Changed?.Invoke(Current);
        public void RequestSelectItem(string id)
        {
            var item = Array.Find(Items, i => i.Id == id && i.Count > 0);
            if (item == null) return; SelectedItemId = id; Publish();
        }
        public void RequestUseItem(string id)
        {
            var item = Array.Find(Items, i => i.Id == id && i.Count > 0 && i.Usable);
            if (item == null) return;
            item.Count--; Warning = "调试：使用了" + item.Name;
            if (item.Count == 0) SelectedItemId = Array.Find(Items, i => i.Count > 0)?.Id;
            Publish();
        }
        public void RequestSkill(int index) { Warning = "调试：技能 " + index + " 请求"; Publish(); }
        [ContextMenu("调试/扣一格血")] public void LoseHealth() { Health = Math.Max(0, Health - 1); Publish(); }
        [ContextMenu("调试/消耗25体力")] public void SpendStamina() { Stamina = Math.Max(0, Stamina - 25); Publish(); }
        [ContextMenu("调试/恢复状态")] public void RestoreHud() { Health = MaxHealth; Stamina = MaxStamina; Warning = ""; Publish(); }
        [ContextMenu("调试/增加局外养成")] public void AddGrowth() { DebugGrowth++; Publish(); }
        public ProgressValue[] Export() => new[] { new ProgressValue { Id = "debug-growth", Value = DebugGrowth } };
        public void Restore(ProgressValue[] values) { DebugGrowth = Array.Find(values, v => v.Id == "debug-growth")?.Value ?? 0; Publish(); }
        public void ResetNewGame() { DebugGrowth = 0; RestoreHud(); }
    }
}
