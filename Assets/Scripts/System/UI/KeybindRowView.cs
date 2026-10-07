using UnityEngine;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class KeybindRowView : MonoBehaviour
    {
        public string ActionName;
        public TMP_Text Label, BindingText;
        public UnityEngine.UI.Button Button;
    }
}
