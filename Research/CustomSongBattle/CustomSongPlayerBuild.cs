using System;
using System.IO;
using System.Linq;
using TapTap.Systems;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copied only to the ignored validation project, never included in game sources.
public static class CustomSongPlayerBuild
{
    public static void BuildGame()
    {
        if (Type.GetType("CustomSongPlayerProbe, Assembly-CSharp") != null || Type.GetType("CustomSongStartupDiagnostics, Assembly-CSharp") != null)
            throw new InvalidOperationException("Move validation probes outside Assets before building the normal game.");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.FirstOrDefault() != "Assets/Scenes/Title.unity" || !scenes.Contains("Assets/Scenes/CustomSongBattle.unity"))
            throw new InvalidOperationException("The normal build requires Title first and the custom-song scene enabled.");
        PlayerSettings.companyName = "DefaultCompany"; PlayerSettings.productName = "TapTap_GameJam";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        string output = Path.GetFullPath("../../Builds/Windows/TapTap_GameJam.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenes,
            locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception(report.summary.result.ToString());
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/CustomSongGameBuild.txt", "PASS normal title scene and custom-song scene included\n" + output);
    }
    public static void Build()
    {
        TapTap.Systems.Editor.CustomSongBattleSetup.Install();
        TapTap.Systems.Editor.CustomSongBattleValidation.Run();
        TapTap.Systems.Editor.SpotlightValidation.Run();
        TapTap.Systems.Editor.SpotlightRunValidation.Run();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.gameObject.AddComponent<AudioListener>();
        var config = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset"));
        config.SaveFolder = "CustomSongValidation"; config.TitleScene = "Assets/CustomSongValidation.unity";
        AssetDatabase.CreateAsset(config, "Assets/CustomSongValidationConfig.asset");
        var root = new GameObject("SystemRoot").AddComponent<SystemRoot>();
        var serialized = new SerializedObject(root); serialized.FindProperty("config").objectReferenceValue = config; serialized.ApplyModifiedPropertiesWithoutUndo();
        new GameObject("CustomSongPlayerProbe").AddComponent(Type.GetType("CustomSongPlayerProbe, Assembly-CSharp", true));
        EditorSceneManager.SaveScene(scene, config.TitleScene); AssetDatabase.SaveAssets();
        PlayerSettings.companyName = "TapTapResearch"; PlayerSettings.productName = "CustomSongValidation";
        PlayerSettings.runInBackground = true; PlayerSettings.defaultIsFullScreen = false;
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        string output = Path.GetFullPath("../custom-song-player/CustomSongValidation.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { config.TitleScene }.Concat(EditorBuildSettings.scenes.Where(s => s.enabled && s.path != "Assets/Scenes/Title.unity").Select(s => s.path)).Distinct().ToArray(),
            locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception(report.summary.result.ToString());
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/CustomSongPlayerBuild.txt", "PASS Windows x64 Mono development build\n" + output);
    }
}
