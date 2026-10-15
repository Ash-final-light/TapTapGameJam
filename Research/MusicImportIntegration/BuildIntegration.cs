using System;
using System.IO;
using TapTap.Systems;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildIntegration
{
    public static void Build()
    {
        TapTap.Systems.Editor.SpotlightValidation.Run();
        TapTap.Systems.Editor.SpotlightRunValidation.Run();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.gameObject.AddComponent<AudioListener>();
        var config = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset"));
        config.SaveFolder = "MusicImportIntegration-" + Guid.NewGuid().ToString("N");
        config.TitleScene = "Assets/MusicIntegration.unity";
        AssetDatabase.CreateAsset(config, "Assets/MusicIntegrationConfig.asset");
        var root = new GameObject("SystemRoot").AddComponent<SystemRoot>();
        var serialized = new SerializedObject(root); serialized.FindProperty("config").objectReferenceValue = config; serialized.ApplyModifiedPropertiesWithoutUndo();
        new GameObject("IntegrationValidation").AddComponent<MusicIntegrationValidation>();
        new GameObject("InteractionProbe").AddComponent<MusicUIInteractionProbe>();
        EditorSceneManager.SaveScene(scene, config.TitleScene);
        AssetDatabase.SaveAssets();
        PlayerSettings.companyName = "TapTapResearch"; PlayerSettings.productName = "MusicImportIntegration";
        PlayerSettings.runInBackground = true; PlayerSettings.defaultIsFullScreen = false;
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        string output = Path.GetFullPath("../music-import-player/MusicImportIntegration.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { config.TitleScene },
            locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception(report.summary.result.ToString());
        Debug.Log("MUSIC IMPORT INTEGRATION BUILD PASS");
    }
}
