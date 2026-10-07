using UnityEngine;
using UnityEngine.InputSystem;

namespace Inputs
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputManager))]
    public sealed class PlayerInputSampler : MonoBehaviour
    {
        private readonly InputTickBuffer buffer = new();
        private PlayerInputManager manager;
        public PlayerInputManager Manager => manager ? manager : manager = GetComponent<PlayerInputManager>();

        private void OnEnable()
        {
            Manager.ActionMapChanged += OnMapChanged;
            Manager.RegisterActionPerformed(InputConstants.Bomb, BufferBomb);
            Manager.RegisterActionPerformed(InputConstants.Pause, BufferPause);
            buffer.Clear();
        }

        private void OnDisable()
        {
            if (manager)
            {
                manager.ActionMapChanged -= OnMapChanged;
                manager.UnregisterActionPerformed(InputConstants.Bomb, BufferBomb);
                manager.UnregisterActionPerformed(InputConstants.Pause, BufferPause);
            }
            buffer.Clear();
        }

        // The battle runner calls this once before advancing its logic ticks.
        public void Capture()
        {
            bool active = isActiveAndEnabled && Manager.isActiveAndEnabled && Manager.PlayerInput.isActiveAndEnabled &&
                Manager.CurrentActionMap?.name == InputConstants.GameplayMap && Manager.CurrentActionMap.enabled;
            if (!active) { buffer.Clear(); return; }
            var move = Manager.GetInputAction(InputConstants.Move);
            buffer.Capture(move?.ReadValue<Vector2>() ?? Vector2.zero,
                Manager.IsPressed(InputConstants.Focus), Manager.IsPressed(InputConstants.Fire));
        }

        public InputSnapshot ConsumeTick() => buffer.ConsumeTick();
        public void DiscardButtons() => buffer.DiscardButtons();
        // Avoid On<Action> names: PlayerInput's default Send Messages uses those with InputValue.
        private void BufferBomb(InputAction.CallbackContext context) => buffer.QueueBomb();
        private void BufferPause(InputAction.CallbackContext context) => buffer.QueuePause();
        private void OnMapChanged(InputActionMap map) => buffer.Clear();
        private void OnApplicationFocus(bool focused) { if (!focused) buffer.Clear(); }
    }
}
