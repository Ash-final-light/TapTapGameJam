using UnityEngine;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class MenuView : MonoBehaviour
    {
        public UnityEngine.UI.Button StartButton, ContinueButton, SettingsButton, ExitButton;
        public TMP_Text Title;
        [System.NonSerialized] public UnityEngine.UI.Button MusicButton;
    }
}
