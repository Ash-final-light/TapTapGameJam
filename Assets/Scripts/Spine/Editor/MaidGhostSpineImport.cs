using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Spine.Editor
{
    /// <summary>Imports the supplied 3.8 binary without requiring the Spine editor.</summary>
    [InitializeOnLoad]
    public static class MaidGhostSpineImport
    {
        private const string Folder = "Assets/ArtAsset/Spine/MaidGhost";
        private const string Request = "Logs/SpineMaidGhostImport.request";
        private const string Report = "Logs/SpineMaidGhostImport.txt";
        private const string Prefab = Folder + "/MaidGhost.prefab";

        static MaidGhostSpineImport()
        {
            EditorApplication.delayCall += ImportIfRequested;
        }

        private static void ImportIfRequested()
        {
            if (!File.Exists(Request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += ImportIfRequested;
                return;
            }
            // Consume this explicit request once; regular editor reloads never overwrite the prefab.
            File.Delete(Request);
            try { Run(); }
            catch (Exception exception)
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Report, "FAIL\n" + exception);
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Spine/导入并验证 MaidGhost（3.8）")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before importing MaidGhost.");

            // The source atlas is PMA in gamma space. Unpremultiply its encoded RGB before
            // Unity performs sRGB decoding, then let Spine's straight-input shader multiply alpha.
            // Originals are retained under Source, so this conversion is always reproducible.
            AssetDatabase.StartAssetEditing();
            try
            {
                WriteStraightAlphaTexture();
                File.Copy(Folder + "/Source/char_3_maid.skeleton.bin", Folder + "/char_3_maid.skel.bytes", true);
                File.Copy(Folder + "/Source/char_3_maid.atlas", Folder + "/char_3_maid.atlas.txt", true);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/char_3_maid.png");
            if (importer == null) throw new InvalidOperationException("Texture was not imported.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();

            var shader = Shader.Find("Spine/Skeleton");
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Spine/Skeleton shader is missing or has compilation errors.");
            var material = LoadOrCreate<Material>(Folder + "/char_3_maid_Material.mat", () => new Material(shader));
            material.shader = shader;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/char_3_maid.png");
            material.SetFloat("_StraightAlphaInput", 1);
            material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");
            EditorUtility.SetDirty(material);

            var atlas = LoadOrCreate<SpineAtlasAsset>(Folder + "/char_3_maid_Atlas.asset", ScriptableObject.CreateInstance<SpineAtlasAsset>);
            atlas.atlasFile = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/char_3_maid.atlas.txt");
            atlas.materials = new[] { material };
            atlas.Clear();
            if (atlas.GetAtlas() == null) throw new InvalidOperationException("Atlas could not be loaded.");
            EditorUtility.SetDirty(atlas);

            var data = LoadOrCreate<SkeletonDataAsset>(Folder + "/char_3_maid_SkeletonData.asset", ScriptableObject.CreateInstance<SkeletonDataAsset>);
            data.skeletonJSON = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/char_3_maid.skel.bytes");
            data.atlasAssets = new AtlasAssetBase[] { atlas };
            data.scale = 0.01f;
            data.defaultMix = 0.15f;
            data.Clear();
            var skeleton = data.GetSkeletonData(false);
            if (skeleton == null || !skeleton.Version.StartsWith("3.8."))
                throw new InvalidOperationException("Expected a readable Spine 3.8 skeleton.");
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            string animationName = skeleton.Animations.FirstOrDefault(a => a.Name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)?.Name
                                   ?? skeleton.FindAnimation("Standby")?.Name
                                   ?? skeleton.Animations.FirstOrDefault()?.Name;
            var previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("MaidGhost");
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var animation = root.AddComponent<SkeletonAnimation>();
                animation.skeletonDataAsset = data;
                animation.initialSkinName = skeleton.DefaultSkin?.Name ?? skeleton.Skins.FirstOrDefault()?.Name;
                animation.loop = true;
                animation.pmaVertexColors = true;
                animation.Initialize(true);
                animation.AnimationName = animationName;
                animation.Update(0);
                animation.LateUpdate();
                root.GetComponent<MeshRenderer>().sortingOrder = 10;
                if (PrefabUtility.SaveAsPrefabAsset(root, Prefab) == null)
                    throw new InvalidOperationException("Could not save MaidGhost prefab.");

                var results = new List<string>
                {
                    "PASS Spine 3.8 binary + atlas loaded",
                    "Unity: " + Application.unityVersion,
                    "Spine data: " + skeleton.Version,
                    "Bones: " + skeleton.Bones.Count + "; Slots: " + skeleton.Slots.Count,
                    "Skins: " + string.Join(", ", skeleton.Skins.Select(s => s.Name)),
                    "Default animation: " + animationName,
                    "Shader: " + shader.name + " (unlit; straight alpha input)",
                    "Texture: original gamma PMA retained in Source; generated straight alpha, sRGB, uncompressed, no mipmaps",
                    "Color space: " + QualitySettings.activeColorSpace,
                    "Render pipeline: " + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                        ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "Built-in"),
                    "Prefab: " + Prefab
                };
                ValidateAnimations(animation, results);
                animation.Skeleton.SetToSetupPose();
                animation.AnimationState.ClearTracks();
                if (animationName != null) animation.AnimationState.SetAnimation(0, animationName, true);
                animation.Update(0.2f);
                animation.LateUpdate();
                RenderPreview(root);
                results.Add("PASS preview rendered: Logs/SpineMaidGhostPreview.png");
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Report, string.Join("\n", results));
                Debug.Log("MaidGhost Spine import passed:\n" + string.Join("\n", results));
            }
            finally
            {
                // PreviewRenderUtility can own/destroy the root during cleanup.
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
            AssetDatabase.SaveAssets();
        }

        private static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void WriteStraightAlphaTexture()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(Folder + "/Source/char_3_maid.pma.png")))
                    throw new InvalidOperationException("Source PNG could not be decoded.");
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.a == 0) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
                    pixels[i] = new Color32(Unpremultiply(pixel.r, pixel.a), Unpremultiply(pixel.g, pixel.a), Unpremultiply(pixel.b, pixel.a), pixel.a);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(Folder + "/char_3_maid.png", texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static byte Unpremultiply(byte color, byte alpha) => (byte)Math.Min(255, (color * 255 + alpha / 2) / alpha);

        private static void ValidateAnimations(SkeletonAnimation animation, List<string> results)
        {
            foreach (var clip in animation.Skeleton.Data.Animations)
            {
                animation.Skeleton.SetToSetupPose();
                animation.AnimationState.ClearTracks();
                animation.AnimationState.SetAnimation(0, clip.Name, false);
                for (int sample = 0; sample <= 20; sample++)
                {
                    animation.Update(sample == 0 ? 0 : clip.Duration / 20);
                    animation.LateUpdate();
                    var mesh = animation.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("Empty mesh: " + clip.Name);
                    foreach (var v in mesh.vertices)
                        if (float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y))
                            throw new InvalidOperationException("Non-finite vertices: " + clip.Name);
                }
                results.Add("PASS animation: " + clip.Name + "; duration=" + clip.Duration.ToString("F3") + "s; 21 mesh samples");
            }
        }

        private static void RenderPreview(GameObject root)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("A graphics device is required for the preview.");
            var preview = new PreviewRenderUtility();
            try
            {
                var bounds = root.GetComponent<MeshRenderer>().bounds;
                preview.AddSingleGO(root);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.2f;
                preview.camera.transform.position = bounds.center + new Vector3(0, 0, -10);
                preview.camera.transform.rotation = Quaternion.identity;
                preview.camera.nearClipPlane = 0.1f;
                preview.camera.farClipPlane = 100;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.10f, 0.12f, 0.16f);
                preview.BeginStaticPreview(new Rect(0, 0, 640, 640));
                preview.Render(true);
                var image = preview.EndStaticPreview();
                try
                {
                    Directory.CreateDirectory("Logs");
                    File.WriteAllBytes("Logs/SpineMaidGhostPreview.png", image.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(image); }
            }
            finally { preview.Cleanup(); }
        }
    }
}
