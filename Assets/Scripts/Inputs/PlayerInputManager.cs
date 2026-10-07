using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inputs
{
    /// <summary>
    /// Routes action callbacks through one PlayerInput, including after its action map changes.
    /// Movement should be sampled by the game clock via GetInputAction, not integrated in callbacks.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputManager : MonoBehaviour
    {
        [SerializeField] private string defaultActionMap = InputConstants.GameplayMap;

        private readonly Dictionary<string, ActionRoute> routes = new(StringComparer.Ordinal);
        private PlayerInput playerInput;
        private InputActionMap boundMap;
        public event Action<InputActionMap> ActionMapChanged;

        public PlayerInput PlayerInput => playerInput ? playerInput : playerInput = GetComponent<PlayerInput>();
        public InputActionMap CurrentActionMap => PlayerInput ? PlayerInput.currentActionMap : null;

        private void Awake() => playerInput = GetComponent<PlayerInput>();

        private void OnEnable() => RefreshBindings();

        private void Start()
        {
            if (!string.IsNullOrWhiteSpace(defaultActionMap))
                SwitchCurrentActionMap(defaultActionMap);
            else
                RefreshBindings();
        }

        private void Update()
        {
            // Also catches maps switched directly through PlayerInput by other systems.
            if (CurrentActionMap != boundMap) RefreshBindings();
        }

        private void OnDisable() => UnbindAll();

        private void OnDestroy()
        {
            UnbindAll();
            routes.Clear();
        }

        public void SwitchCurrentActionMap(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) throw new ArgumentException("Action map name is required.", nameof(mapName));
            var input = PlayerInput;
            if (!input || !input.actions)
            {
                Debug.LogError("PlayerInput has no InputActionAsset assigned.", this);
                return;
            }

            if (input.actions.FindActionMap(mapName, false) == null)
            {
                Debug.LogError($"Input action map '{mapName}' does not exist in {input.actions.name}.", this);
                return;
            }

            if (input.currentActionMap == null || input.currentActionMap.name != mapName)
                input.SwitchCurrentActionMap(mapName);
            RefreshBindings();
        }

        public InputAction GetInputAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return null;
            RefreshBindingsIfNeeded();
            return CurrentActionMap?.FindAction(actionName, false);
        }

        public bool WasPerformedThisFrame(string actionName) => GetInputAction(actionName)?.WasPerformedThisFrame() ?? false;
        public bool IsPressed(string actionName) => GetInputAction(actionName)?.IsPressed() ?? false;

        public void RegisterActionStarted(string actionName, Action<InputAction.CallbackContext> callback) => Register(actionName, callback, ActionPhase.Started);
        public void UnregisterActionStarted(string actionName, Action<InputAction.CallbackContext> callback) => Unregister(actionName, callback, ActionPhase.Started);
        public void RegisterActionPerformed(string actionName, Action<InputAction.CallbackContext> callback) => Register(actionName, callback, ActionPhase.Performed);
        public void UnregisterActionPerformed(string actionName, Action<InputAction.CallbackContext> callback) => Unregister(actionName, callback, ActionPhase.Performed);
        public void RegisterActionCanceled(string actionName, Action<InputAction.CallbackContext> callback) => Register(actionName, callback, ActionPhase.Canceled);
        public void UnregisterActionCanceled(string actionName, Action<InputAction.CallbackContext> callback) => Unregister(actionName, callback, ActionPhase.Canceled);

        private void Register(string actionName, Action<InputAction.CallbackContext> callback, ActionPhase phase)
        {
            if (string.IsNullOrEmpty(actionName)) throw new ArgumentException("Action name is required.", nameof(actionName));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!routes.TryGetValue(actionName, out var route))
            {
                route = new ActionRoute(actionName);
                routes.Add(actionName, route);
            }
            route.Add(phase, callback);
            RefreshBindingsIfNeeded();
            if (isActiveAndEnabled && route.BoundAction == null)
                route.Bind(boundMap?.FindAction(actionName, false));
        }

        private void Unregister(string actionName, Action<InputAction.CallbackContext> callback, ActionPhase phase)
        {
            if (string.IsNullOrEmpty(actionName) || callback == null || !routes.TryGetValue(actionName, out var route)) return;
            route.Remove(phase, callback);
            if (!route.HasListeners)
            {
                route.Bind(null);
                routes.Remove(actionName);
            }
        }

        private void RefreshBindingsIfNeeded()
        {
            if (isActiveAndEnabled && CurrentActionMap != boundMap) RefreshBindings();
        }

        private void RefreshBindings()
        {
            if (!isActiveAndEnabled) return;
            var map = CurrentActionMap;
            if (map == boundMap) return;
            foreach (var route in routes.Values)
                route.Bind(map?.FindAction(route.Name, false));
            boundMap = map;
            ActionMapChanged?.Invoke(map);
        }

        private void UnbindAll()
        {
            foreach (var route in routes.Values) route.Bind(null);
            boundMap = null;
        }

        private enum ActionPhase { Started, Performed, Canceled }

        private sealed class ActionRoute
        {
            public readonly string Name;
            private readonly List<Action<InputAction.CallbackContext>> started = new();
            private readonly List<Action<InputAction.CallbackContext>> performed = new();
            private readonly List<Action<InputAction.CallbackContext>> canceled = new();
            public InputAction BoundAction { get; private set; }
            public bool HasListeners => started.Count != 0 || performed.Count != 0 || canceled.Count != 0;

            public ActionRoute(string name) => Name = name;

            public void Add(ActionPhase phase, Action<InputAction.CallbackContext> callback)
            {
                var listeners = ListFor(phase);
                if (!listeners.Contains(callback)) listeners.Add(callback);
            }

            public void Remove(ActionPhase phase, Action<InputAction.CallbackContext> callback) => ListFor(phase).Remove(callback);

            public void Bind(InputAction action)
            {
                if (BoundAction == action) return;
                if (BoundAction != null)
                {
                    BoundAction.started -= OnStarted;
                    BoundAction.performed -= OnPerformed;
                    BoundAction.canceled -= OnCanceled;
                }
                BoundAction = action;
                if (action == null) return;
                action.started += OnStarted;
                action.performed += OnPerformed;
                action.canceled += OnCanceled;
            }

            private List<Action<InputAction.CallbackContext>> ListFor(ActionPhase phase) => phase switch
            {
                ActionPhase.Started => started,
                ActionPhase.Performed => performed,
                _ => canceled
            };

            private void OnStarted(InputAction.CallbackContext context) => Dispatch(started, context);
            private void OnPerformed(InputAction.CallbackContext context) => Dispatch(performed, context);
            private void OnCanceled(InputAction.CallbackContext context) => Dispatch(canceled, context);

            private static void Dispatch(List<Action<InputAction.CallbackContext>> listeners, InputAction.CallbackContext context)
            {
                // Registrations are normally stable during dispatch. The bounds check also makes
                // removal during a callback safe without making a per-event array copy.
                int count = listeners.Count;
                for (int i = 0; i < count && i < listeners.Count; i++) listeners[i](context);
            }
        }
    }
}
