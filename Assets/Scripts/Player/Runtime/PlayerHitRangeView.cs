using System;
using TapTap.Danmaku;
using UnityEngine;

namespace TapTap.Player
{
    /// <summary>Presentation of the exact combat circle; it never feeds collision data back into the simulation.</summary>
    public sealed class PlayerHitRangeView : IDisposable
    {
        private const int Segments = 64;
        private readonly PlayerDefinition definition;
        private readonly Transform arena;
        private readonly GameObject root;
        private readonly Material material;
        private readonly LineRenderer outline, boundary, core;
        private readonly Vector3[] points = new Vector3[Segments];

        public PlayerHitRangeView(PlayerDefinition definition, Transform playerVisual)
        {
            this.definition = definition; arena = playerVisual.parent;
            root = new GameObject("Player Hit Range"); root.transform.SetParent(playerVisual, false);
            root.hideFlags = HideFlags.DontSave;
            material = new Material(Shader.Find("Sprites/Default")) { name = "Player Hit Range", hideFlags = HideFlags.DontSave };
            var renderer = playerVisual.GetComponentInChildren<Renderer>();
            int layer = renderer ? renderer.sortingLayerID : 0, order = renderer ? renderer.sortingOrder + 10 : 30;
            outline = MakeLine("Outline", layer, order);
            boundary = MakeLine("Boundary", layer, order + 1);
            core = MakeLine("Core", layer, order + 2);
        }
        private LineRenderer MakeLine(string name, int layer, int order)
        {
            var obj = new GameObject(name); obj.transform.SetParent(root.transform, false);
            var line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true; line.loop = true; line.positionCount = Segments;
            line.sortingLayerID = layer; line.sortingOrder = order;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            return line;
        }
        public void Sync(PlayerRuntime player)
        {
            bool visible = definition.ShowHitRange && player.Actor.Alive;
            root.SetActive(visible); if (!visible) return;
            var center = DanmakuSimulation.ToVector(player.Actor.Pose.Position);
            float radius = player.Actor.Pose.HitRadius.ToFloat();
            var color = player.Invulnerable ? definition.InvulnerableHitRangeColor : definition.HitRangeColor;
            float width = Mathf.Clamp(radius * 0.1f, 0.004f, 0.015f);
            DrawCircle(outline, center, radius, width * 2.5f, new Color(0.02f, 0.02f, 0.02f, 0.95f));
            DrawCircle(boundary, center, radius, width, color);
            DrawCircle(core, center, Mathf.Min(radius * 0.12f, 0.012f), Mathf.Min(radius * 0.24f, 0.024f), color);
        }
        private void DrawCircle(LineRenderer line, Vector3 center, float radius, float width, Color color)
        {
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                points[i] = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
                if (arena) points[i] = arena.TransformPoint(points[i]);
                points[i].z -= 0.01f;
            }
            line.SetPositions(points); line.startWidth = line.endWidth = width; line.startColor = line.endColor = color;
        }
        public void Dispose() { Release(root); Release(material); }
        private static void Release(UnityEngine.Object obj)
        { if (!obj) return; if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); }
    }
}
