using System;
using System.IO;
using System.Linq;
using TapTap.Danmaku;
using TapTap.Systems;
using TapTap.Systems.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.MusicPresentation.Editor
{
    [InitializeOnLoad]
    public static class BattleMusicUISetup
    {
        private const string RequestPath = "Temp/BattleMusicUISetup.v2.request";
        private const string ReportPath = "Logs/BattleMusicUISetup.txt";
        private const string ScenePath = "Assets/Scenes/GameplayPlaceholder.unity";

        static BattleMusicUISetup() => EditorApplication.update += ProcessRequest;

        private static void ProcessRequest()
        {
            if (Application.isBatchMode || !File.Exists(RequestPath) || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(RequestPath);
            try { Install(); }
            catch (Exception error)
            {
                Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, "FAIL\n" + error);
                Debug.LogException(error);
            }
        }

        [MenuItem("工具/音乐表现/接入当前战斗HUD跳动")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 PlayMode 后接入音乐 UI。");
            var scene = SceneManager.GetActiveScene();
            if (Find<BattleManager>(scene).Length == 0)
            {
                scene = SceneManager.GetSceneByPath(ScenePath);
                // Keep all open scenes (and their unsaved changes) while loading the requested battle.
                if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }
            var battles = Find<BattleManager>(scene);
            var roots = Find<SystemRoot>(scene);
            if (battles.Length != 1 || roots.Length != 1 || !roots[0].Config || !roots[0].Config.UIPrefab)
                throw new InvalidOperationException("战斗场景需要唯一 BattleManager 和配置完整的 SystemRoot。");
            var battle = battles[0];
            var source = battle.GetComponent<BattleMusicPresentationSource>();
            if (!source || source.Battle != battle)
                throw new InvalidOperationException("请先接入当前战斗音乐数据源。");
            string prefabPath = AssetDatabase.GetAssetPath(roots[0].Config.UIPrefab);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == prefabPath && stage.scene.isDirty)
                throw new InvalidOperationException("HUD 预制体正在编辑且未保存，请先保存预制体后重试。");
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            string[] paths;
            try
            {
                var hud = prefab.GetComponent<SystemUIController>().Hud;
                var images = new[] { hud.Portrait, hud.Skill1, hud.Skill2, hud.Microphone };
                foreach (var image in images) ValidateTarget(image);
                paths = images.Select(image => AnimationUtility.CalculateTransformPath(image.transform, prefab.transform)).ToArray();
                foreach (var image in images)
                {
                    var ui = image.GetComponent<MusicReactiveUI>();
                    if (!ui)
                    {
                        ui = image.gameObject.AddComponent<MusicReactiveUI>();
                        ui.Target = image.rectTransform;
                        ui.Response = MusicUIResponse.Auto; ui.Band = MusicUIBand.Low;
                        ui.ScaleAmount = 0.08f; ui.RotationDegrees = 4;
                        ui.PositionAmount = new Vector2(3, 4);
                    }
                    // Scene references are supplied by BattleMusicHudBinding after instantiation.
                    ui.Source = null; ui.enabled = true;
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("HUD 预制体保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("绑定战斗音乐 HUD");
            var binding = battle.GetComponent<BattleMusicHudBinding>();
            if (!binding) binding = Undo.AddComponent<BattleMusicHudBinding>(battle.gameObject);
            Undo.RecordObject(binding, "绑定 HUD 音乐源"); binding.Source = source; binding.enabled = true;
            EditorUtility.SetDirty(binding); Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("战斗场景保存失败。");
            if (!Application.isBatchMode)
            { SceneManager.SetActiveScene(scene); Selection.activeGameObject = battle.gameObject; }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, "PASS\nScene: " + scene.path + "\nBattle: " + battle.name +
                "\nSource: " + source.name + "\nHUD prefab: " + prefabPath +
                "\nIcons: " + string.Join(", ", paths) + "\nResponse: Auto (Sequence BPM, spectrum fallback)\n");
            Debug.Log("战斗 HUD 音乐跳动已接入并保存：" + scene.path, binding);
        }

        private static void ValidateTarget(UnityEngine.UI.Image image)
        {
            if (!image || !image.rectTransform || image.rectTransform.rect.width <= 0 || image.rectTransform.rect.height <= 0)
                throw new InvalidOperationException("HUD 图标缺失或尺寸无效。");
            var parent = image.transform.parent;
            if (image.GetComponent<UnityEngine.UI.Selectable>() || image.GetComponent<Animator>() ||
                image.GetComponent<UnityEngine.UI.ContentSizeFitter>() ||
                (parent && parent.GetComponent<UnityEngine.UI.LayoutGroup>()))
                throw new InvalidOperationException("图标存在布局、按钮或 Animator 控制，需先选择内部视觉节点：" + image.name);
        }

        private static T[] Find<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

    }
}
