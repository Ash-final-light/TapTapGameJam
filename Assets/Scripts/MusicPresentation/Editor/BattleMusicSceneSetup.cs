using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TapTap.Danmaku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.MusicPresentation.Editor
{
    [InitializeOnLoad]
    public static class BattleMusicSceneSetup
    {
        private const string RequestPath = "Temp/BattleMusicSceneSetup.request";
        private const string ReportPath = "Logs/BattleMusicSceneSetup.txt";
        private const string ScenePath = "Assets/Scenes/GameplayPlaceholder.unity";
        private const string MaterialPath = "Assets/Materials/MusicPresentation/BattleMusicBackground.mat";

        static BattleMusicSceneSetup() => EditorApplication.update += ProcessRequest;

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

        [MenuItem("工具/音乐表现/接入当前战斗场景")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 PlayMode 后接入音乐背景。");
            Scene scene = SceneManager.GetActiveScene();
            var managers = Find<BattleManager>(scene);
            if (managers.Length == 0)
            {
                scene = SceneManager.GetSceneByPath(ScenePath);
                if (!scene.isLoaded) throw new InvalidOperationException("当前场景没有 BattleManager，请先打开战斗场景。");
                managers = Find<BattleManager>(scene);
            }
            if (managers.Length != 1) throw new InvalidOperationException("战斗场景必须有唯一 BattleManager。");
            var battle = managers[0];
            if (!battle.AimCamera || !battle.Config || !battle.Config.Sequence || !battle.Config.Sequence.Music)
                throw new InvalidOperationException("战斗需要已绑定的相机、配置和音乐。");
            var curves = Find<BattleMusicBackgroundCurve>(scene);
            if (curves.Length > 1) throw new InvalidOperationException("场景已有多条音乐曲线，请明确选择要接入的组件。");
            var source = battle.GetComponent<BattleMusicPresentationSource>();
            int undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("添加战斗音乐背景");
            if (!source) source = Undo.AddComponent<BattleMusicPresentationSource>(battle.gameObject);
            Undo.RecordObject(source, "绑定战斗音乐"); source.Battle = battle; source.enabled = true;
            var curve = curves.FirstOrDefault();
            if (!curve)
            {
                var obj = new GameObject("MusicBackground");
                Undo.RegisterCreatedObjectUndo(obj, "创建音乐背景");
                SceneManager.MoveGameObjectToScene(obj, scene);
                obj.transform.SetParent(battle.transform, false);
                obj.transform.localPosition = Vector3.zero;
                curve = Undo.AddComponent<BattleMusicBackgroundCurve>(obj);
                curve.Radius = 3.2f; curve.SortingOrder = -100;
            }
            Undo.RecordObject(curve, "配置音乐背景");
            curve.Source = source; curve.enabled = true;
            if (!curve.Material) curve.Material = EnsureMaterial();
            // Bullets draw at order 0; put the stage below the music instead of covering the bullets.
            foreach (var stage in Find<SpriteRenderer>(scene).Where(r => r.name == "StagePlaceholder"))
                if (stage.sortingLayerID == UnityEngine.SortingLayer.NameToID(curve.SortingLayer) && stage.sortingOrder >= curve.SortingOrder)
                { Undo.RecordObject(stage, "背景底图排序"); stage.sortingOrder = curve.SortingOrder - 100; EditorUtility.SetDirty(stage); }
            var line = curve.GetComponent<LineRenderer>();
            Undo.RecordObject(line, "初始化背景曲线");
            typeof(BattleMusicBackgroundCurve).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(curve, null);
            curve.Rebuild();
            typeof(BattleMusicBackgroundCurve).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(curve, null);
            EditorUtility.SetDirty(source); EditorUtility.SetDirty(curve); EditorUtility.SetDirty(line);
            Undo.CollapseUndoOperations(undoGroup);
            int expectedPoints = new MusicCurveGeometry(curve.Sides, curve.Radius, curve.Generations,
                curve.Generator, curve.Outwards).RenderPointCount(curve.SamplesPerSegment);
            if (!line.enabled || line.positionCount != expectedPoints || curve.Source.Battle != battle)
                throw new InvalidOperationException("音乐背景初始化或引用验证失败。");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("战斗场景保存失败。");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, "PASS\nScene: " + scene.path + "\nBattle: " + battle.name +
                "\nMusic: " + battle.Config.Sequence.Music.name + "\nCurve: " + curve.name +
                "\nRadius: " + curve.Radius + "\nPoints: " + line.positionCount +
                "\nSorting: " + line.sortingLayerName + "/" + line.sortingOrder +
                "\nMaterial: " + AssetDatabase.GetAssetPath(curve.Material) + "\n");
            Debug.Log("音乐背景已接入并保存：" + scene.path, curve);
        }

        private static T[] Find<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material) return material;
            var shader = Resources.Load<Shader>("BattleMusicLine");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("音乐曲线 Shader 不可用。");
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/MusicPresentation")) AssetDatabase.CreateFolder("Assets/Materials", "MusicPresentation");
            material = new Material(shader) { name = "BattleMusicBackground" };
            material.SetColor("_Tint", new Color(1.5f, 1.5f, 1.5f, 0.3f));
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

    }
}
