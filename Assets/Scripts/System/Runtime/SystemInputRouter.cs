using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Inputs;
using TapTap.Danmaku;
using TapTap.Player;

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
        private readonly Queue<(GameplayCommand Command, string ItemId)> commands = new();
        public int Count => commands.Count;
        public GameplayInputState State { get; internal set; }
        public void Queue(GameplayCommand command, string itemId = null) { if (commands.Count < 128) commands.Enqueue((command, itemId)); }
        public bool TryConsume(out GameplayCommand command)
            => TryConsume(out command, out _);
        public bool TryConsume(out GameplayCommand command, out string itemId)
        {
            if (commands.Count == 0) { command = default; itemId = null; return false; }
            var queued = commands.Dequeue(); command = queued.Command; itemId = queued.ItemId; return true;
        }
        public void Clear() { commands.Clear(); State = default; }
    }
    public sealed class SystemInputRouter : IDisposable
    {
        public InputActionAsset Controls { get; }
        public GameplayCommandBuffer Commands { get; } = new();
        public bool GameplayEnabled { get; private set; }
        public Func<string> SelectedItemId { get; set; }
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
        private void Queue(GameplayCommand command)
        { Commands.Queue(command, command == GameplayCommand.UseItem ? SelectedItemId?.Invoke() : null); CommandQueued?.Invoke(command); }
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
        public BattleInput CaptureBattle(Camera camera, Transform field)
        {
            if (!GameplayEnabled) return default;
            // Read the same runtime actions used by BindingService; never instantiate a second asset.
            int axis = (gameplay.FindAction("Right", true).IsPressed() ? 1 : 0) - (gameplay.FindAction("Left", true).IsPressed() ? 1 : 0);
            var input = new InputSnapshot((short)(axis * short.MaxValue), 0, fireHeld: gameplay.FindAction("Fire", true).IsPressed());
            var point = gameplay.FindAction("Aim", true).ReadValue<Vector2>();
            return BattleInputProjection.Project(input, point, camera, field);
        }
        public PlayerTickInput CapturePlayerTick()
        {
            if (!GameplayEnabled) { Commands.Clear(); return default; }
            PlayerButtonEdges edges = 0;
            string itemId = null;
            while (Commands.TryConsume(out var command, out var queuedItemId))
            {
                // One item press per simulation tick, retaining the identity of the first press.
                if (command == GameplayCommand.UseItem && (edges & PlayerButtonEdges.UseItem) == 0) itemId = queuedItemId;
                edges |= command switch {
                GameplayCommand.JumpPressed => PlayerButtonEdges.JumpPressed,
                GameplayCommand.JumpReleased => PlayerButtonEdges.JumpReleased,
                GameplayCommand.Dodge => PlayerButtonEdges.Dash,
                GameplayCommand.Skill1 => PlayerButtonEdges.Skill1,
                GameplayCommand.Skill2 => PlayerButtonEdges.Skill2,
                GameplayCommand.UseItem => PlayerButtonEdges.UseItem,
                _ => PlayerButtonEdges.None,
                };
            }
            int axis = (gameplay.FindAction("Right", true).IsPressed() ? 1 : 0) - (gameplay.FindAction("Left", true).IsPressed() ? 1 : 0);
            return new PlayerTickInput((short)(axis * short.MaxValue), gameplay.FindAction("JumpFly", true).IsPressed(),
                gameplay.FindAction("Fire", true).IsPressed(), edges, itemId);
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
