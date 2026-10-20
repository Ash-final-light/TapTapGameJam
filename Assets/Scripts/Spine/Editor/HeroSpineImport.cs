using System;
using System.IO;
using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Spine.Editor
{
    /// <summary>Rebuilds the standalone Hero assets from the preserved art delivery.</summary>
    public static class HeroSpineImport
    {
        public const string Folder = "Assets/ArtAsset/Spine/Hero";
        public const string PrefabPath = Folder + "/Hero.prefab";

        [MenuItem("Tools/Spine/导入 Hero（3.8）")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before importing Hero.");
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/Spine/Hero/export"));
            foreach (string file in new[] { "char_hero.json", "char_hero.atlas", "char_hero.png" })
                if (!File.Exists(Path.Combine(source, file))) throw new FileNotFoundException("Hero source is missing", file);

            Directory.CreateDirectory(Folder + "/Animations");
            // The delivered .json contains binary data. Keep its original under ArtSource,
            // and create a byte-identical copy with the extension expected by spine-unity.
            File.Copy(Path.Combine(source, "char_hero.json"), Folder + "/char_hero.skel.bytes", true);
            File.Copy(Path.Combine(source, "char_hero.atlas"), Folder + "/char_hero.atlas.txt", true);
            WriteStraightAlpha(Path.Combine(source, "char_hero.png"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + "/char_hero.png");
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.sRGBTexture = true;
            textureImporter.alphaSource = TextureImporterAlphaSource.FromInput;
            textureImporter.alphaIsTransparency = true;
            textureImporter.mipmapEnabled = false;
            textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.filterMode = FilterMode.Bilinear;
            textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
            textureImporter.maxTextureSize = 2048;
            textureImporter.npotScale = TextureImporterNPOTScale.None;
            textureImporter.SaveAndReimport();

            var shader = Shader.Find("Spine/Skeleton");
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Spine/Skeleton shader is unavailable.");
            var material = LoadOrCreate(Folder + "/char_hero_Material.mat", () => new Material(shader));
            material.shader = shader;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/char_hero.png");
            material.SetFloat("_StraightAlphaInput", 1);
            material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");
            EditorUtility.SetDirty(material);

            var atlas = LoadOrCreate(Folder + "/char_hero_Atlas.asset", ScriptableObject.CreateInstance<SpineAtlasAsset>);
            atlas.atlasFile = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/char_hero.atlas.txt");
            atlas.materials = new[] { material };
            atlas.Clear();
            if (atlas.GetAtlas() == null) throw new InvalidOperationException("Hero atlas could not be loaded.");
            EditorUtility.SetDirty(atlas);

            var data = LoadOrCreate(Folder + "/char_hero_SkeletonData.asset", ScriptableObject.CreateInstance<SkeletonDataAsset>);
            data.skeletonJSON = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/char_hero.skel.bytes");
            data.atlasAssets = new AtlasAssetBase[] { atlas };
            data.scale = .01f;
            data.defaultMix = .12f;
            data.Clear();
            var skeleton = data.GetSkeletonData(false);
            if (skeleton == null || !skeleton.Version.StartsWith("3.8.") || skeleton.FindAnimation("player_idle") == null)
                throw new InvalidOperationException("Expected a Spine 3.8 Hero skeleton with player_idle.");
            EditorUtility.SetDirty(data);

            foreach (var clip in skeleton.Animations)
            {
                var reference = LoadOrCreate(Folder + "/Animations/" + clip.Name + ".asset", ScriptableObject.CreateInstance<AnimationReferenceAsset>);
                var serialized = new SerializedObject(reference);
                serialized.FindProperty("skeletonDataAsset").objectReferenceValue = data;
                serialized.FindProperty("animationName").stringValue = clip.Name;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                reference.Initialize();
                if (reference.Animation == null) throw new InvalidOperationException("Missing Hero animation: " + clip.Name);
            }

            var previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Hero");
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var animation = root.AddComponent<SkeletonAnimation>();
                animation.skeletonDataAsset = data;
                animation.initialSkinName = "default";
                animation.loop = true;
                animation.pmaVertexColors = true;
                animation.Initialize(true);
                animation.AnimationName = "player_idle";
                animation.Update(0);
                animation.LateUpdate();
                var renderer = root.GetComponent<MeshRenderer>();
                renderer.sortingOrder = 20;
                float height = renderer.bounds.size.y;
                if (height < .001f) throw new InvalidOperationException("Hero has no visible mesh.");
                root.transform.localScale = Vector3.one * (2.2f / height);
                if (!PrefabUtility.SaveAsPrefabAsset(root, PrefabPath)) throw new InvalidOperationException("Hero prefab could not be saved.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Hero imported: " + PrefabPath + "; " + skeleton.Animations.Count + " animations.");
        }

        private static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value) return value;
            value = create();
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        private static void WriteStraightAlpha(string source)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(source))) throw new InvalidDataException("Hero texture could not be read.");
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    pixels[i] = p.a == 0 ? new Color32(0, 0, 0, 0) : new Color32(Unpremultiply(p.r, p.a), Unpremultiply(p.g, p.a), Unpremultiply(p.b, p.a), p.a);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(Folder + "/char_hero.png", texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static byte Unpremultiply(byte color, byte alpha) => (byte)Math.Min(255, (color * 255 + alpha / 2) / alpha);
    }
}
