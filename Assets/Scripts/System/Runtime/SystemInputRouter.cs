using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TapTap.Systems
{
    public enum GameplayCommand { JumpPressed, JumpReleased, Dodge, Skill1, Skill2, UseItem }
    public readonly struct GameplayInputState
    {
        public readonly int Horizontal;
        public readonly bool JumpFlyHeld;
        public readonly float HeldSeconds;
        public GameplayInputState(int horizontal, bool held, float seconds) { Horizontal = horizontal; JumpFlyHeld = held; HeldSeconds = seconds; }
    }
    public sealed class GameplayCommandBuffer
    {
        private readonly Queue<GameplayCommand> commands = new();
        public int Count => commands.Count;
        public GameplayInputState State { get; internal set; }
        public void Queue(GameplayCommand command) { if (commands.Count < 128) commands.Enqueue(command); }
        public bool TryConsume(out GameplayCommand command)
        { if (commands.Count == 0) { command = default; return false; } command = commands.Dequeue(); return true; }
        public void Clear() { commands.Clear(); State = default; }
    }
    public sealed class SystemInputRouter : IDisposable
    {
        public InputActionAsset Controls { get; }
        public GameplayCommandBuffer Commands { get; } = new();
        public bool GameplayEnabled { get; private set; }
        public event Action PauseRequested, CancelRequested;
        public event Action<bool> SelectionHeldChanged;
        public event Action<int> SlotRequested;
        public event Action<GameplayCommand> CommandQueued;
        private readonly InputActionMap gameplay, ui;
        private bool selectionHeld, suppressSelection;
        private float heldSeconds;
        public SystemInputRouter(InputActionAsset template)
        {
            Controls = UnityEngine.Object.Instantiate(template); Controls.Disable();
            gameplay = Controls.FindActionMap("Gameplay", true); ui = Controls.FindActionMap("UI", true);
            gameplay.FindAction("Pause", true).performed += OnPause;
            ui.FindAction("Cancel", true).performed += OnCancel;
            gameplay.FindAction("JumpFly", true).started += OnJump;
            gameplay.FindAction("JumpFly", true).canceled += OnJumpRelease;
            foreach (var name in new[] { "Dodge", "Skill1", "Skill2", "ItemUse" }) gameplay.FindAction(name, true).performed += OnCommand;
            foreach (var action in gameplay.actions) if (action.name.StartsWith("Slot", StringComparison.Ordinal)) action.performed += OnSlot;
            ui.Enable();
        }
        private void OnPause(InputAction.CallbackContext _) => PauseRequested?.Invoke();
        private void OnCancel(InputAction.CallbackContext _) => CancelRequested?.Invoke();
        private void Queue(GameplayCommand command) { Commands.Queue(command); CommandQueued?.Invoke(command); }
        private void OnJump(InputAction.CallbackContext _) { heldSeconds = 0; if (GameplayEnabled) Queue(GameplayCommand.JumpPressed); }
        private void OnJumpRelease(InputAction.CallbackContext _) { if (GameplayEnabled) Queue(GameplayCommand.JumpReleased); }
        private void OnCommand(InputAction.CallbackContext context)
        {
            if (!GameplayEnabled) return;
            Queue(context.action.name switch { "Dodge" => GameplayCommand.Dodge, "Skill1" => GameplayCommand.Skill1,
                "Skill2" => GameplayCommand.Skill2, _ => GameplayCommand.UseItem });
        }
        private void OnSlot(InputAction.CallbackContext context)
        { if (GameplayEnabled && !suppressSelection && gameplay.FindAction("ItemSelect", true).IsPressed() && int.TryParse(context.action.name.Substring(4), out int slot)) SlotRequested?.Invoke(slot); }
        public void SetGameplay(bool enabled)
        {
            if (GameplayEnabled == enabled) return;
            GameplayEnabled = enabled;
            if (enabled) gameplay.Enable(); else gameplay.Disable();
            // Cancel cannot share Esc with Pause while gameplay is enabled.
            var cancel = ui.FindAction("Cancel", true); if (enabled) cancel.Disable(); else cancel.Enable();
            Clear(); suppressSelection = enabled && gameplay.FindAction("ItemSelect", true).IsPressed();
        }
        public void Update(float deltaSeconds)
        {
            if (!GameplayEnabled) return;
            bool selecting = gameplay.FindAction("ItemSelect", true).IsPressed();
            if (!selecting) suppressSelection = false;
            selecting &= !suppressSelection;
            if (selecting != selectionHeld) { selectionHeld = selecting; SelectionHeldChanged?.Invoke(selectionHeld); }
            bool held = gameplay.FindAction("JumpFly", true).IsPressed();
            heldSeconds = held ? heldSeconds + Math.Max(0, deltaSeconds) : 0;
            int axis = (gameplay.FindAction("Right", true).IsPressed() ? 1 : 0) - (gameplay.FindAction("Left", true).IsPressed() ? 1 : 0);
            Commands.State = new GameplayInputState(axis, held, heldSeconds);
        }
        public void Clear()
        {
            Commands.Clear(); heldSeconds = 0;
            if (selectionHeld) { selectionHeld = false; SelectionHeldChanged?.Invoke(false); }
        }
        public void Dispose()
        {
            Controls.Disable();
            gameplay.FindAction("Pause").performed -= OnPause; ui.FindAction("Cancel").performed -= OnCancel;
            gameplay.FindAction("JumpFly").started -= OnJump; gameplay.FindAction("JumpFly").canceled -= OnJumpRelease;
            foreach (var name in new[] { "Dodge", "Skill1", "Skill2", "ItemUse" }) gameplay.FindAction(name).performed -= OnCommand;
            foreach (var action in gameplay.actions) if (action.name.StartsWith("Slot", StringComparison.Ordinal)) action.performed -= OnSlot;
            UnityEngine.Object.Destroy(Controls);
        }
    }
}
