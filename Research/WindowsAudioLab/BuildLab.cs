using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildLab
{
    public static void Build()
    {
        var input = Path.Combine(Directory.GetCurrentDirectory(), "inputs", "外部 音频 #100%");
        Directory.CreateDirectory(input);
        foreach (int rate in new[] { 44100, 48000 })
        {
            WriteClicks(Path.Combine(input, $"校准 120bpm {rate}.wav"), rate, 120, .25);
            WriteClicks(Path.Combine(input, $"校准 137.5bpm {rate}.wav"), rate, 137.5, .123);
        }
        File.WriteAllText(Path.Combine(input, "损坏 文件.wav"), "This is not a WAV file.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.gameObject.AddComponent<AudioListener>();
        EditorSceneManager.SaveScene(scene, "Assets/Lab.unity");
        PlayerSettings.companyName = "TapTapResearch";
        PlayerSettings.productName = "WindowsAudioImportLab";
        PlayerSettings.defaultIsFullScreen = false;
        PlayerSettings.defaultScreenWidth = 640; PlayerSettings.defaultScreenHeight = 360;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        string output = null;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-buildOutput") output = args[i + 1];
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Missing -buildOutput");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Lab.unity" }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
        Debug.Log("WINDOWS AUDIO LAB BUILD PASS");
    }
    private static void WriteClicks(string path, int rate, double bpm, double offset)
    {
        int frames = rate * 20;
        var pcm = new short[frames];
        for (int n = 0; ; n++)
        {
            int start = (int)Math.Round((offset + n * 60 / bpm) * rate);
            if (start >= frames) break;
            for (int j = 0; j < rate / 100 && start + j < frames; j++)
                pcm[start + j] = (short)(19660 * Math.Exp(-j / (rate * .002)));
        }
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
            foreach (short value in pcm) writer.Write(value);
        }
    }
}
