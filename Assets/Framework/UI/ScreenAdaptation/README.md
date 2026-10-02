# Screen Adaptation

This module keeps full-screen backgrounds edge-to-edge while constraining interactive UI to the device safe area and supported aspect-ratio range.

## Structure

- `Runtime/Components`: scene-facing `MonoBehaviour` components.
- `Runtime/Services`: shared registration and border-visibility services.
- `Runtime/Utilities`: coordinate conversion and rectangle calculations.
- `Runtime/Android`: Android display-cutout integration and legacy-device fallback.
- `Runtime/EditorIntegration`: editor callbacks exposed without a runtime dependency on `UnityEditor`.
- `Editor/Inspectors`: custom inspectors and gizmos.
- `Editor/Tools`: Simulator helpers, diagnostics, presets, and prefab audits.

## Namespaces

- `AshFramework.UI.ScreenAdaptation.Components`
- `AshFramework.UI.ScreenAdaptation.Services`
- `AshFramework.UI.ScreenAdaptation.Utilities`
- `AshFramework.UI.ScreenAdaptation.Android`
- `AshFramework.UI.ScreenAdaptation.EditorIntegration`
- `AshFramework.UI.ScreenAdaptation.Editor.Inspectors`
- `AshFramework.UI.ScreenAdaptation.Editor.Tools`

Runtime code is compiled by `AshFramework.UI.ScreenAdaptation`. Editor code is compiled separately by `AshFramework.UI.ScreenAdaptation.Editor`, which references the runtime assembly. Runtime code must never reference `UnityEditor`; editor-only callbacks are registered through `ScreenAdaptationEditorBridge`.

## Scene Layout

Use a full-screen background as a direct child of the root Canvas. Put interactive content under a stretched `RectTransform` with the `SafeArea` component:

```text
Canvas
|- Background
`- SafeAreaRoot (SafeArea)
   `- InteractiveContent
```

Add `CanvasWidthAdjuster` to the root Canvas only when the project also needs wide/narrow aspect-ratio clipping. Assign its Canvas and Content references in the Inspector.
