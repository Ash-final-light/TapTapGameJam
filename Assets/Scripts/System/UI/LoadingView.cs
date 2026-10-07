using UnityEngine;
using TMPro;

namespace TapTap.Systems.UI
{
    public sealed class LoadingView : MonoBehaviour
    {
        public TMP_Text Tip, Status;
        public UnityEngine.UI.Slider Progress;
        public UnityEngine.UI.Button Retry, Back;
        public RectTransform Character;
        private float animationTime;
        private void Update()
        {
            animationTime += Time.unscaledDeltaTime;
            if (Character) { Character.localRotation = Quaternion.Euler(0,0, Mathf.Sin(animationTime*8)*5);
                Character.anchoredPosition = new Vector2(0, 80 + Mathf.Abs(Mathf.Sin(animationTime*8))*14); }
        }
    }
}
