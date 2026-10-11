using System;
using TapTap.Systems;
using TapTap.Systems.UI;
using UnityEngine;

namespace TapTap.MusicPresentation
{
    /// <summary>Binds the persistent, runtime-created HUD to this battle's music.</summary>
    [AddComponentMenu("TapTap/Music Presentation/Battle Music HUD Binding")]
    [DisallowMultipleComponent, DefaultExecutionOrder(600)]
    public sealed class BattleMusicHudBinding : MonoBehaviour
    {
        public BattleMusicPresentationSource Source;
        private HudView boundHud;
        private BattleMusicPresentationSource boundSource;
        private MusicReactiveUI[] icons = Array.Empty<MusicReactiveUI>();

        private void Awake()
        { if (!Source) Source = GetComponent<BattleMusicPresentationSource>(); }

        private void LateUpdate()
        {
            var root = SystemRoot.Instance;
            BindHud(root && root.UI ? root.UI.Hud : null);
        }

        public void BindHud(HudView hud)
        {
            if (boundHud == hud && boundSource == Source) return;
            ReleaseHud();
            boundHud = hud; boundSource = Source;
            if (!hud) return;
            icons = hud.GetComponentsInChildren<MusicReactiveUI>(true);
            foreach (var icon in icons) icon.Bind(Source);
        }

        private void ReleaseHud()
        {
            // A departing battle must not unbind a HUD already taken over by the next battle.
            foreach (var icon in icons)
                if (icon && icon.Source == boundSource) icon.Bind(null);
            icons = Array.Empty<MusicReactiveUI>(); boundHud = null; boundSource = null;
        }

        private void OnDisable() => ReleaseHud();
        private void OnDestroy() => ReleaseHud();
    }
}
