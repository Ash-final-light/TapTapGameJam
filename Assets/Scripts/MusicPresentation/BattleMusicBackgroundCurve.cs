using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap.MusicPresentation
{
    [AddComponentMenu("TapTap/Music Presentation/Battle Music Background Curve")]
    [DisallowMultipleComponent, RequireComponent(typeof(LineRenderer)), DefaultExecutionOrder(600)]
    public sealed class BattleMusicBackgroundCurve : MonoBehaviour
    {
        public BattleMusicPresentationSource Source;
        [Header("局部 XY 平面轮廓")]
        [Range(3, 64)] public int Sides = 6;
        [Min(0.01f)] public float Radius = 2.6f;
        [Range(0, 3)] public int Generations = 1;
        public bool Outwards = true;
        public AnimationCurve Generator = new(new Keyframe(0, 0), new Keyframe(1f / 3, 0),
            new Keyframe(0.5f, 0.288675f), new Keyframe(2f / 3, 0), new Keyframe(1, 0));
        [Range(1, 16)] public int SamplesPerSegment = 6;
        [Range(0, 2)] public float Deformation = 1;
        [Range(0, 0.5f)] public float RadialPulse = 0.08f;
        public float RotationDegreesPerSecond = 4;
        [Header("绘制与背景排序")]
        [Tooltip("可留空，自动使用随模块提供的 URP 2D Shader。自定义材质必须支持顶点颜色。")]
        public Material Material;
        public Gradient Colors = DefaultColors();
        [Min(0.001f)] public float Width = 0.035f;
        [Range(0, 1)] public float Opacity = 0.3f;
        [Min(0)] public float Brightness = 1.5f;
        public string SortingLayer = "Default";
        public int SortingOrder = -100;
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private LineRenderer line;
        private Material ownedMaterial;
        private MaterialPropertyBlock properties;
        private MusicCurveGeometry geometry;
        private Vector3[] points;
        private bool dirty = true;

        private void Reset()
        {
            Colors = DefaultColors();
        }

        private static Gradient DefaultColors()
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(0.15f, 0.6f, 1), 0),
                new GradientColorKey(new Color(0.6f, 0.25f, 1), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }

        private void Awake()
        {
            line = GetComponent<LineRenderer>(); properties = new MaterialPropertyBlock();
            if (!Source) Source = GetComponentInParent<BattleMusicPresentationSource>();
        }

        private void OnEnable() => dirty = true;
        private void OnValidate() => dirty = true;
        [ContextMenu("重建音乐背景曲线")]
        public void Rebuild() => dirty = true;

        private void LateUpdate()
        {
            if (dirty)
            {
                dirty = false;
                try { Build(); }
                catch (ArgumentException error)
                { geometry = null; line.enabled = false; Debug.LogError(error.Message, this); }
            }
            if (geometry == null) return;
            float amplitude = Source ? Source.Envelope.Amplitude : 0;
            double seconds = Source ? Source.SongSeconds : 0;
            float rotation = (float)((seconds * RotationDegreesPerSecond) % 360);
            geometry.Fill(points, SamplesPerSegment, Source ? Source.Envelope : null, Deformation,
                1 + amplitude * RadialPulse, rotation);
            line.SetPositions(points);
            // _Tint is supplied by the bundled shader; custom materials keep their own tint contract.
            if (line.sharedMaterial && line.sharedMaterial.HasProperty(TintId))
            {
                properties.SetColor(TintId, new Color(Brightness, Brightness, Brightness, Opacity));
                line.SetPropertyBlock(properties);
            }
        }

        private void Build()
        {
            geometry = new MusicCurveGeometry(Sides, Radius, Generations, Generator, Outwards);
            points = new Vector3[geometry.RenderPointCount(SamplesPerSegment)];
            if (!Material && !ownedMaterial)
            {
                var shader = Resources.Load<Shader>("BattleMusicLine");
                if (!shader) throw new ArgumentException("缺少 Resources/BattleMusicLine Shader，请指定兼容 URP 2D 的材质。");
                ownedMaterial = new Material(shader) { name = "Battle Music Line (Runtime)" };
            }
            line.sharedMaterial = Material ? Material : ownedMaterial;
            line.useWorldSpace = false; line.alignment = LineAlignment.TransformZ;
            line.loop = true; line.positionCount = points.Length;
            line.widthMultiplier = Mathf.Max(0.001f, Width);
            line.widthCurve = AnimationCurve.Constant(0, 1, 1);
            line.colorGradient = Colors;
            line.numCornerVertices = 2; line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off; line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.sortingLayerID = UnityEngine.SortingLayer.NameToID(SortingLayer); line.sortingOrder = SortingOrder;
            line.enabled = true;
        }

        private void OnDisable() { if (line) line.enabled = false; }
        private void OnDestroy()
        {
            if (ownedMaterial)
            {
                if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
            }
        }
    }
}
