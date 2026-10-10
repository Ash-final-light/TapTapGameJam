using UnityEngine;
using Spine.Unity;
using TapTap.Danmaku;

namespace TapTap.Player
{
    [CreateAssetMenu(menuName = "Player/Definition", fileName = "PlayerDefinition")]
    public sealed class PlayerDefinition : ScriptableObject
    {
        [Min(0.001f)] public float MaxHealth = 5, HitRadius = 0.08f;
        [Min(0)] public float MaxStamina = 100, StaminaRecoveryPerSecond = 20;
        [Min(0)] public int StaminaRecoveryDelayTicks = 30, HurtInvulnerableTicks = 30;
        public PlayerMotorConfig Motor = new();
        public PlayerInputPolicy Input = new();
        public PlayerActionLibrary Actions;
        public PlayerWeaponDefinition Weapon;
        public GameObject VisualPrefab;
        public SkeletonDataAsset SkeletonData;
        public Sprite Portrait, FallbackSprite;
        [Min(0.001f)] public float FallbackVisualSize = 0.22f;
        [Range(0.2f, 1)] public float InvulnerableAlpha = 0.48f;
        [Tooltip("在游戏和动作预览中显示真实受击判定圆。")]
        public bool ShowHitRange = true;
        public Color HitRangeColor = new(1, 0.9f, 0.25f, 1), InvulnerableHitRangeColor = new(0.25f, 1, 1, 1);
        public Color Skill1Color = new(0.35f, 0.75f, 1), Skill2Color = new(1, 0.75f, 0.3f);
        public string Validate()
        {
            if (!Positive(MaxHealth) || !Positive(HitRadius) || !Positive(FallbackVisualSize) || !NonNegative(MaxStamina) ||
                !NonNegative(StaminaRecoveryPerSecond) || HurtInvulnerableTicks < 0 || StaminaRecoveryDelayTicks < 0) return "玩家属性不合法。";
            if (Motor == null || Input == null || !Actions) return "需要 Motor、Input 和 Actions。";
            if (!NonNegative(Motor.MoveSpeed) || !NonNegative(Motor.Gravity) || !NonNegative(Motor.JumpSpeed) || !NonNegative(Motor.DashSpeed) ||
                !NonNegative(Motor.FlightRiseSpeed) || !NonNegative(Motor.FlightStaminaPerSecond) || !PlayerActionClip.Finite(Motor.GroundHeight)) return "玩家运动参数不合法。";
            if (Input.BufferTicks < 1 || Input.FlyHoldTicks < 1) return "输入缓冲／长按阈值必须大于零。";
            string error = Actions.Validate(); if (error != null) return error;
            foreach (var binding in Actions.Bindings)
                foreach (var node in binding.Clip.Nodes)
                    if (node.Kind == PlayerActionNodeKind.Spine && node.Spine.Animation && SkeletonData && node.Spine.Animation.SkeletonDataAsset != SkeletonData)
                        return $"{binding.Slot} 的 Spine 动画属于其他 SkeletonData。";
            if (!Weapon || !Weapon.Projectile || !Positive(Weapon.FireRate) || Weapon.FireRate > PlayerActionClip.TickRate) return "需要有效武器，射速范围为 (0, 60]。";
            if (Weapon.Mode == PlayerWeaponMode.Homing) return "跟踪武器尚未实现。";
            if (!PlayerActionClip.Finite(InvulnerableAlpha) || InvulnerableAlpha < 0.2f || InvulnerableAlpha > 1) return "无敌透明度不合法。";
            return null;
        }
        private static bool Positive(float v) => PlayerActionClip.Finite(v) && v > 0;
        private static bool NonNegative(float v) => PlayerActionClip.Finite(v) && v >= 0;
    }
}
