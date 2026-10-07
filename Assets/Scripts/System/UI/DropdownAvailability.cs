using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace TapTap.Systems.UI
{
    [RequireComponent(typeof(TMP_Dropdown))]
    public sealed class DropdownAvailability : MonoBehaviour
    {
        private TMP_Dropdown dropdown;
        private void Awake() => dropdown = GetComponent<TMP_Dropdown>();
        private void LateUpdate()
        {
            if (!dropdown.IsExpanded) return;
            foreach (var toggle in GetComponentsInChildren<UnityEngine.UI.Toggle>(true))
            {
                if (!toggle.gameObject.activeInHierarchy) continue;
                var label = toggle.GetComponentInChildren<TMP_Text>();
                if (!label || !label.text.Contains("不可用")) continue;
                toggle.interactable = false; label.color = new Color(0.5f,0.5f,0.5f,1);
            }
        }
    }
}
