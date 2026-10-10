using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Player
{
    [CreateAssetMenu(menuName = "Player/Action Library", fileName = "PlayerActions")]
    public sealed class PlayerActionLibrary : ScriptableObject
    {
        public List<PlayerClipBinding> Bindings = new();
        public PlayerActionClip Get(PlayerClipSlot slot)
        { foreach (var binding in Bindings) if (binding != null && binding.Slot == slot) return binding.Clip; return null; }
        public string Validate()
        {
            if (Bindings == null) return "动作库列表为空。";
            var slots = new HashSet<PlayerClipSlot>();
            foreach (var binding in Bindings)
            {
                if (binding == null || !Enum.IsDefined(typeof(PlayerClipSlot), binding.Slot) || !slots.Add(binding.Slot) || !binding.Clip) return "动作库含空绑定／重复槽位／未知槽位。";
                string error = binding.Clip.Validate(); if (error != null) return $"{binding.Slot}：{error}";
            }
            return null;
        }
    }
}
