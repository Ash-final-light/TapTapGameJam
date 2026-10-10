using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Player
{
    [CreateAssetMenu(menuName = "Player/Action Clip", fileName = "PlayerAction")]
    public sealed class PlayerActionClip : ScriptableObject
    {
        public const int TickRate = 60;
        public int SchemaVersion = 1;
        public string StableId = Guid.NewGuid().ToString("N");
        public string DisplayName = "动作";
        [Min(1)] public int DurationTicks = 24;
        public PlayerActionKind ActionKind;
        public PlayerClipEndMode EndMode;
        [Tooltip("-1 表示不使用阶段标记。")]
        public int ActiveStartTick = -1, RecoveryStartTick = -1;
        public bool DefaultAllowFire = true;
        public PlayerIntentMask DefaultTransitions = PlayerIntentMask.None;
        [Min(0)] public float StaminaCost;
        [Min(0)] public int CooldownTicks;
        public List<PlayerActionNode> Nodes = new();
        public PlayerActionPhase PhaseAt(int tick) => ActiveStartTick < 0 ? PlayerActionPhase.Active :
            tick < ActiveStartTick ? PlayerActionPhase.Startup :
            RecoveryStartTick >= 0 && tick >= RecoveryStartTick ? PlayerActionPhase.Recovery : PlayerActionPhase.Active;
        public string Validate()
        {
            if (DurationTicks < 1 || SchemaVersion != 1) return "时长必须大于零，且版本必须为 1。";
            if (!Enum.IsDefined(typeof(PlayerActionKind), ActionKind) || !Enum.IsDefined(typeof(PlayerClipEndMode), EndMode) ||
                (DefaultTransitions & ~PlayerIntentMask.All) != 0) return "动作类型／结束类型／接续标记不合法。";
            if (string.IsNullOrWhiteSpace(StableId)) return "缺少 StableId。";
            if (!Finite(StaminaCost) || StaminaCost < 0 || CooldownTicks < 0) return "体力成本／冷却不合法。";
            if (ActiveStartTick < -1 || RecoveryStartTick < -1 || ActiveStartTick >= DurationTicks || RecoveryStartTick >= DurationTicks ||
                (RecoveryStartTick >= 0 && (ActiveStartTick < 0 || RecoveryStartTick < ActiveStartTick))) return "阶段标记顺序不合法。";
            if (Nodes == null) return "节点列表为空。";
            var ids = new HashSet<string>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (n == null || string.IsNullOrWhiteSpace(n.Id) || !ids.Add(n.Id)) return "节点缺失或 Id 重复。";
                if (!Enum.IsDefined(typeof(PlayerActionNodeKind), n.Kind) || n.StartTick < 0 || n.StartTick >= DurationTicks ||
                    (!n.IsPoint && (n.EndTick <= n.StartTick || n.EndTick > DurationTicks))) return $"节点 {n.Name} 的类型／区间不合法。";
                if (!Finite(n.SpeedScale) || n.SpeedScale < 0 || !Finite(n.ProjectileSpeed) || n.ProjectileSpeed < 0 ||
                    !Finite(n.Volume) || n.Volume < 0 || n.Volume > 1) return $"节点 {n.Name} 的参数不合法。";
                if (n.Kind == PlayerActionNodeKind.Spine && (n.Spine == null || n.Spine.TrackIndex < 0 || !Finite(n.Spine.Speed) || n.Spine.Speed <= 0 ||
                    !Finite(n.Spine.SourceOffset) || n.Spine.SourceOffset < 0 || !Finite(n.Spine.MixSeconds) || n.Spine.MixSeconds < 0)) return "Spine 参数不合法。";
                if (n.Kind == PlayerActionNodeKind.PointEvent && !Enum.IsDefined(typeof(PlayerPointEventKind), n.Event)) return "未知玩法事件。";
                if (n.Kind == PlayerActionNodeKind.Motion && !Enum.IsDefined(typeof(PlayerMotionMode), n.Motion)) return "未知运动类型。";
                if (n.Kind == PlayerActionNodeKind.Transition && (n.AllowedIntents & ~PlayerIntentMask.All) != 0) return "未知接续标记。";
                if (n.Conditions == null) return "接续条件列表为空。";
                foreach (var condition in n.Conditions) if (!Enum.IsDefined(typeof(PlayerTransitionCondition), condition)) return "未知接续条件。";
                for (int j = 0; j < i; j++)
                {
                    var other = Nodes[j];
                    bool overlap = !n.IsPoint && !other.IsPoint && n.StartTick < other.EndTick && other.StartTick < n.EndTick;
                    if (overlap && n.Kind == other.Kind && (n.Kind == PlayerActionNodeKind.Motion || n.Kind == PlayerActionNodeKind.FirePermission ||
                        (n.Kind == PlayerActionNodeKind.Spine && n.Spine.TrackIndex == other.Spine.TrackIndex))) return $"节点 {n.Name} 与 {other.Name} 覆盖冲突。";
                    if (n.IsPoint && other.IsPoint && n.StartTick == other.StartTick && n.Order == other.Order) return "同 Tick 点事件需要不同 Order。";
                }
            }
            return null;
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
