using System;
using UnityEngine;

namespace TapTap.Danmaku
{
    /// <summary>Pool shared by the isolated editor preview and the game player.</summary>
    public sealed class DanmakuSpritePool : IDisposable
    {
        private readonly Transform root;
        private readonly SpriteRenderer player, boss;
        private readonly DanmakuSpriteBatch batches;
        public int ActiveBatchCount => batches.ActiveBatchCount;
        private Texture2D fallbackTexture;
        private Sprite fallbackSprite;

        public DanmakuSpritePool(Transform root, bool showActors = true)
        {
            this.root = root;
            fallbackTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            { name = "Danmaku preview circle", hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2(x - 15.5f, y - 15.5f).magnitude;
                    pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Clamp01(15.5f - radius));
                }
            fallbackTexture.SetPixels(pixels); fallbackTexture.Apply(false, true);
            fallbackSprite = Sprite.Create(fallbackTexture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32);
            fallbackSprite.hideFlags = HideFlags.HideAndDontSave;
            if (showActors) { player = Create("Fixed test player", 20); boss = Create("Emitter", 10); }
            batches = new DanmakuSpriteBatch(root, fallbackSprite);
        }

        private SpriteRenderer Create(string name, int order)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            return renderer;
        }

        public void Sync(DanmakuSimulation simulation, DanmakuSequence sequence)
        {
            batches.Sync(simulation, sequence);
            if (player) Setup(player, sequence.PreviewSprite, Color.white,
                sequence.PreviewPlayerConfig ? sequence.PreviewPlayerConfig.VisualSize : 0.22f,
                DanmakuSimulation.ToVector(simulation.PlayerState.Position), 0);
            if (boss) Setup(boss, sequence.BossSprite, new Color(1, 0.8f, 0.25f), 0.42f,
                sequence.Clips.Count > 0 ? simulation.EmitterPosition(0) : new Vector2(0, 3), 0);
        }

        public void Sync(BattleSimulation battle, BattleConfig config)
        {
            batches.Sync(battle.World, config.Sequence);
            // The independent PlayerSpineView owns the player's presentation.
            if (player) player.enabled = false;
            if (boss) Setup(boss, config.Sequence.BossSprite, new Color(1, 0.8f, 0.25f), 0.8f,
                DanmakuSimulation.ToVector(battle.Boss.Pose.Position), 0);
        }

        private void Setup(SpriteRenderer view, Sprite sprite, Color color, float size, Vector2 position, float angle)
        {
            view.sprite = sprite ? sprite : fallbackSprite;
            view.color = color;
            view.transform.localPosition = new Vector3(position.x, position.y, 0);
            view.transform.localRotation = Quaternion.Euler(0, 0, angle);
            Vector3 bounds = view.sprite.bounds.size;
            float scale = Mathf.Max(0.001f, size) / Mathf.Max(0.001f, Mathf.Max(bounds.x, bounds.y));
            view.transform.localScale = Vector3.one * scale;
        }

        public void Dispose()
        {
            batches.Dispose();
            if (player) Destroy(player.gameObject);
            if (boss) Destroy(boss.gameObject);
            if (fallbackSprite) Destroy(fallbackSprite);
            if (fallbackTexture) Destroy(fallbackTexture);
        }

        private static void Destroy(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
