using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TapTap.Systems.Editor
{
    [InitializeOnLoad]
    public static class CustomSongTestRunner
    {
        private const string ActiveKey = "CustomSongTests.Active";
        private static TestRunnerApi api;
        static CustomSongTestRunner()
        {
            api = ScriptableObject.CreateInstance<TestRunnerApi>(); api.RegisterCallbacks(new Results());
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            const string request = "Temp/CustomSongTests.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string selection;
            try { selection = File.ReadAllText(request).Trim(); File.Delete(request); }
            catch (IOException) { return; } // The producer may still be closing the request file.
            SessionState.SetBool(ActiveKey, true);
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode,
                groupNames = new[] { selection == "all" ? "^Spotlight.Tests\\." : "^Spotlight.Tests.CustomSongFlowTests\\." } }));
        }
        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(ActiveKey, false) || result.HasChildren) return;
                Directory.CreateDirectory("Logs");
                File.AppendAllText("Logs/CustomSongTestProgress.txt", result.ResultState + " " + result.FullName + "\n" + result.Message + "\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                SessionState.SetBool(ActiveKey, false); Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/CustomSongPlayMode.xml", result.ToXml().OuterXml);
                File.WriteAllText("Logs/CustomSongPlayMode.txt", "Passed=" + result.PassCount + " Failed=" + result.FailCount + " Skipped=" + result.SkipCount + "\n" + result.Message);
            }
        }
    }
}
