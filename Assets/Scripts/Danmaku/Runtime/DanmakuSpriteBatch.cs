using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Danmaku
{
    /// <summary>
    /// Batches real Sprite geometry (including tight/atlas UVs) into reusable dynamic meshes.
    /// A bullet has no GameObject, Transform, Rigidbody2D or Collider2D. Meshes are capped
    /// below the 16-bit vertex limit for mobile GPUs. One draw per Sprite per mesh chunk.
    /// </summary>
    public sealed class DanmakuSpriteBatch : IDisposable
    {
        private const int VertexLimit = 16000;
        private readonly Transform root;
        private readonly Sprite fallback;
        private readonly Dictionary<Sprite, Group> groups = new();
        private readonly List<Batch> all = new();
        public int ActiveBatchCount { get; private set; }

        private sealed class Group
        {
            public Sprite Sprite;
            public Vector2[] Vertices, Uv;
            public ushort[] Triangles;
            public float Size;
            public int Cursor;
            public readonly List<Batch> Batches = new();
        }

        private sealed class Batch
        {
            public GameObject Object;
            public Mesh Mesh;
            public Material Material;
            public MeshRenderer Renderer;
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector2> Uv = new();
            public readonly List<Color32> Colors = new();
            public readonly List<int> Indices = new();
        }

        public DanmakuSpriteBatch(Transform root, Sprite fallback) { this.root = root; this.fallback = fallback; }

        public void Sync(DanmakuSimulation simulation, DanmakuSequence sequence) => Sync(simulation.World, sequence);
        public void Sync(ProjectileWorld simulation, DanmakuSequence sequence)
        {
            foreach (var group in groups.Values) group.Cursor = 0;
            foreach (var batch in all)
            { batch.Vertices.Clear(); batch.Uv.Clear(); batch.Colors.Clear(); batch.Indices.Clear(); }
            for (int i = 0; i < simulation.Bullets.Count; i++)
            {
                var p = simulation.Bullets[i]; Sprite sprite = p.Settings.Sprite ? p.Settings.Sprite : fallback;
                if (!groups.TryGetValue(sprite, out var group))
                {
                    // Sprite getters copy arrays: cache once per Sprite, never once per bullet/frame.
                    group = new Group { Sprite = sprite, Vertices = sprite.vertices, Uv = sprite.uv,
                        Triangles = sprite.triangles, Size = Mathf.Max(0.001f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y)) };
                    groups.Add(sprite, group);
                }
                if (group.Batches.Count == 0) group.Batches.Add(CreateBatch(group));
                Batch batch = group.Batches[group.Cursor];
                if (batch.Vertices.Count + group.Vertices.Length > VertexLimit)
                {
                    group.Cursor++;
                    if (group.Cursor >= group.Batches.Count) group.Batches.Add(CreateBatch(group));
                    batch = group.Batches[group.Cursor];
                }
                int offset = batch.Vertices.Count;
                Vector2 position = DanmakuSimulation.ToVector(p.Position);
                float angle = p.Direction.ToFloat(), sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                float scale = Mathf.Max(0.001f, p.Settings.VisualSize) / group.Size;
                Color32 color = p.Settings.Color;
                for (int v = 0; v < group.Vertices.Length; v++)
                {
                    Vector2 vertex = group.Vertices[v] * scale;
                    batch.Vertices.Add(new Vector3(position.x + vertex.x * cos - vertex.y * sin,
                        position.y + vertex.x * sin + vertex.y * cos, 0));
                    batch.Uv.Add(group.Uv[v]); batch.Colors.Add(color);
                }
                for (int t = 0; t < group.Triangles.Length; t++) batch.Indices.Add(offset + group.Triangles[t]);
            }
            ActiveBatchCount = 0;
            foreach (var batch in all)
            {
                bool active = batch.Vertices.Count > 0;
                if (batch.Renderer.enabled != active) batch.Renderer.enabled = active;
                if (!active) continue;
                ActiveBatchCount++;
                batch.Mesh.Clear(false);
                batch.Mesh.SetVertices(batch.Vertices); batch.Mesh.SetUVs(0, batch.Uv);
                batch.Mesh.SetColors(batch.Colors); batch.Mesh.SetTriangles(batch.Indices, 0, false);
                batch.Mesh.bounds = new Bounds(Vector3.zero, new Vector3(sequence.FieldSize.x + 4, sequence.FieldSize.y + 4, 2));
            }
        }

        private Batch CreateBatch(Group group)
        {
            var obj = new GameObject("Danmaku Sprite Batch"); obj.transform.SetParent(root, false);
            var mesh = new Mesh { name = "Dynamic Danmaku Sprite Geometry", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            // Resources reference keeps the shader in PC/mobile player builds.
            var shader = Resources.Load<Shader>("DanmakuSprite");
            if (!shader) shader = Shader.Find("TapTap/DanmakuSprite");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave, mainTexture = group.Sprite.texture };
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var batch = new Batch { Object = obj, Mesh = mesh, Material = material, Renderer = renderer };
            all.Add(batch); return batch;
        }

        public void Dispose()
        {
            foreach (var batch in all)
            { Destroy(batch.Object); Destroy(batch.Mesh); Destroy(batch.Material); }
            all.Clear(); groups.Clear();
        }

        private static void Destroy(UnityEngine.Object obj)
        {
            if (!obj) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
