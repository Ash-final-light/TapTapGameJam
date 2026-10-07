using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TapTap.Systems
{
    public static class SpotlightActions
    {
        public static readonly string[] Names = { "Left", "Right", "JumpFly", "Dodge", "Skill1", "Skill2", "ItemSelect", "ItemUse" };
        public static readonly string[] DefaultPaths = { "<Keyboard>/a", "<Keyboard>/d", "<Keyboard>/space", "<Keyboard>/leftShift",
            "<Keyboard>/q", "<Keyboard>/e", "<Keyboard>/tab", "<Keyboard>/r" };
    }
    public sealed class BindingService
    {
        public InputActionAsset Runtime { get; }
        public BindingService(InputActionAsset runtime) { Runtime = runtime; }
        public OperationResult Validate(InputActionAsset asset)
        {
            for (int i = 0; i < SpotlightActions.Names.Length; i++)
            {
                var action = asset.FindAction("Gameplay/" + SpotlightActions.Names[i], true);
                if (action.bindings.Count != 1) return OperationResult.Fail("动作绑定结构不兼容。");
                string path = action.bindings[0].effectivePath;
                if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail("请选择键盘按键。");
                string key = path.Substring("<Keyboard>/".Length);
                if (key.Equals("escape", StringComparison.OrdinalIgnoreCase) || (key.Length == 1 && char.IsDigit(key[0])))
                    return OperationResult.Fail("Esc 和数字键保留用于界面操作。");
                for (int j = 0; j < i; j++) if (string.Equals(path, asset.FindAction("Gameplay/" + SpotlightActions.Names[j], true).bindings[0].effectivePath, StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail("该键已绑定到其他动作，请选择其他键。");
            }
            return OperationResult.Ok();
        }
        public OperationResult TrySet(InputActionAsset asset, string name, string path)
        {
            var action = asset.FindAction("Gameplay/" + name, true);
            string previous = action.bindings[0].overridePath;
            action.ApplyBindingOverride(0, path);
            var result = Validate(asset);
            if (!result.Success) { if (previous == null) action.RemoveBindingOverride(0); else action.ApplyBindingOverride(0, previous); }
            return result;
        }
        public OperationResult Load(InputActionAsset asset, string json)
        {
            asset.RemoveAllBindingOverrides();
            try
            {
                if (!string.IsNullOrEmpty(json)) asset.LoadBindingOverridesFromJson(json);
                var result = Validate(asset);
                if (!result.Success) asset.RemoveAllBindingOverrides();
                return result;
            }
            catch (Exception) { asset.RemoveAllBindingOverrides(); return OperationResult.Fail("键位数据无效，已恢复默认。"); }
        }
        public string Display(InputActionAsset asset, string name) => asset.FindAction("Gameplay/" + name, true).GetBindingDisplayString(0);
    }
}
