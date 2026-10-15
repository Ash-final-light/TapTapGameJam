using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace TapTap.Danmaku.Editor
{
    [InitializeOnLoad]
    internal static class DanmakuDeletionVerificationSession
    {
        private const string Request = "Temp/DanmakuDeletionVerification.request";
        private const string Self = "Assets/Scripts/Danmaku/Editor/DanmakuDeletionVerificationSession.cs";
        static DanmakuDeletionVerificationSession() => EditorApplication.update += Run;
        private static void Run()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || MusicAnalysisJob.Running) return;
            File.Delete(Request); EditorApplication.update -= Run;
            Directory.CreateDirectory("Logs");
            try
            {
                typeof(DanmakuValidation).GetMethod("TestEditorCommands", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                File.WriteAllText("Logs/DanmakuDeletionVerification.txt", "PASS v2: selected-key deletion; stable context target; deferred Inspector deletion; repeated Delete keeps clip; clip retention; Undo/Redo; mother single-use restrictions.\n");
                AssetDatabase.DeleteAsset(Self); AssetDatabase.Refresh();
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException invocation ? invocation.InnerException : error;
                File.WriteAllText("Logs/DanmakuDeletionVerification.txt", "FAIL v2:\n" + cause); Debug.LogException(cause);
            }
        }
    }
}
