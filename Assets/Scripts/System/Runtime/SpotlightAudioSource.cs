using UnityEngine;

namespace TapTap.Systems
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class SpotlightAudioSource : MonoBehaviour
    {
        public AudioChannel Channel;
        private AudioVolumeService service;
        private AudioSource source;
        private void OnEnable() { source=GetComponent<AudioSource>(); TryRegister(); }
        private void Start() => TryRegister();
        private void TryRegister()
        { if(service==null && SystemRoot.Instance?.Audio!=null) { service=SystemRoot.Instance.Audio;service.Register(source,Channel); } }
        private void OnDisable() { service?.Unregister(source);service=null; }
    }
}
