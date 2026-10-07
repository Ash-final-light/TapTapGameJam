using System;
using UnityEngine;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class DialogView : MonoBehaviour
    {
        public TMP_Text Message;
        public UnityEngine.UI.Button ConfirmButton, CancelButton;
        private Action confirmation;
        private GameObject previousSelection;
        private void Awake() { ConfirmButton.onClick.AddListener(Confirm); CancelButton.onClick.AddListener(Cancel); }
        public void Show(string message, Action action)
        { confirmation = action; Message.text = message; previousSelection = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            gameObject.SetActive(true); UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(ConfirmButton.gameObject); }
        public void Cancel() { confirmation = null; gameObject.SetActive(false); if (previousSelection) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(previousSelection); }
        private void Confirm() { var action = confirmation; Cancel(); action?.Invoke(); }
    }
}
