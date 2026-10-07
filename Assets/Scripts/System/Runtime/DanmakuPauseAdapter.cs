using TapTap.Danmaku;
using UnityEngine;

namespace TapTap.Systems
{
    [RequireComponent(typeof(DanmakuSequencePlayer))]
    public sealed class DanmakuPauseAdapter : MonoBehaviour, IPauseTarget
    {
        private DanmakuSequencePlayer player;
        private PauseService registered;
        public bool IsRunning => player && player.Playing;
        private void Start() { player = GetComponent<DanmakuSequencePlayer>(); TryRegister(); }
        private void OnEnable() { if (player) TryRegister(); }
        private void TryRegister()
        { if (SystemRoot.Instance && registered == null) { registered = SystemRoot.Instance.Pause; registered.Register(this); } }
        private void OnDisable() { registered?.Unregister(this); registered = null; }
        public void Pause() { if (player) player.Pause(); }
        public void Resume() { if (player) player.Resume(); }
    }
}
