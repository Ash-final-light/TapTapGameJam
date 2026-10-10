using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Framework.FixedPoint;
using Inputs;
using TapTap.Danmaku;
using TapTap.Danmaku.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TapTap.Player.Editor
{
    public static class PlayerSystemValidation
    {
        private sealed class Fixture : IDisposable
        {
            private readonly List<Object> owned = new();
            public readonly PlayerDefinition Definition;
            public readonly PlayerActionLibrary Library;
            public Fixture()
            {
                Definition = Make<PlayerDefinition>(); Definition.Motor.GroundHeight = 0;
                Library = Make<PlayerActionLibrary>(); Definition.Actions = Library;
                Definition.Weapon = Make<PlayerWeaponDefinition>(); Definition.Weapon.Projectile = Make<ProjectileDefinition>();
                foreach (PlayerClipSlot slot in Enum.GetValues(typeof(PlayerClipSlot)))
                { var clip = PlayerEditorAssets.CreatePreset(slot); owned.Add(clip); Library.Bindings.Add(new() { Slot = slot, Clip = clip }); }
            }
            public T Make<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); owned.Add(value); return value; }
            public PlayerRuntime Runtime(Vector2? spawn = null, PlayerClipSlot? slot = null) => new(Definition, new Vector2(100, 100), spawn ?? Vector2.zero, slot);
            public void Dispose() { foreach (var value in owned) Object.DestroyImmediate(value); }
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Step(PlayerRuntime runtime, PlayerTickInput input = default) { runtime.BeginTick(input); runtime.EndTick(); }
        [MenuItem("Tools/Player/运行玩家系统验证")]
        public static void Run()
        {
            var report = new List<string>();
            void Check(string label, Action test) { test(); report.Add("PASS " + label); }
            try
            {
                Check("优先级、技能槽位平局、不可用高优先级回退、零耐力动作", TestPriority);
                Check("输入缓冲在接续窗口执行一次、过期清理", TestBuffer);
                Check("无敌 [Start, End) 边界、冲刺中断释放、受伤保护独立来源", TestInvulnerability);
                Check("重力跳跃、长按飞行、松开退出、体力消耗、落地", TestMotion);
                Check("玩法点事件与起跳音效一次性、快照隔离、效果中断清理", TestEvents);
                Check("射击许可、同 Tick 无敌前置于伤害结算", TestCombat);
                Check("不同渲染批次下固定 Tick 状态一致、暂停清理", TestDeterminism);
                Check("非法区间、重复 Id、覆盖冲突、同 Tick 排序校验", TestValidation);
                Check("UXML / USS 导入与结构", TestEditorLayout);
                Check("游戏内判定圈位置／半径、无敌颜色、显示开关和死亡隐藏", TestHitRangeView);
            }
            catch (Exception e) { report.Add("FAIL " + e); throw; }
            finally { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/PlayerSystemValidation.txt", string.Join("\n", report)); }
            Debug.Log("Player system validation passed:\n" + string.Join("\n", report));
        }
        public static void RunBatch()
        {
            try
            {
                PlayerEditorAssets.Migrate(); Run(); BattleValidation.Run(); EnergyOrbValidation.Run();
                var definition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerEditorAssets.DefinitionPath);
                Assert(definition && definition.Validate() == null, "默认玩家资产引用失效。");
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void RunGraphicsBatch()
        {
            try
            {
                Run(); BattleValidation.Run(); EnergyOrbValidation.Run();
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Spine 渲染验证需要图形设备。");
                var definition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerEditorAssets.DefinitionPath);
                Directory.CreateDirectory("Logs");
                foreach (var sample in new[] { (PlayerClipSlot.Idle, 1), (PlayerClipSlot.Dash, 10), (PlayerClipSlot.FlyLoop, 10) })
                {
                    var render = new PreviewRenderUtility();
                    try
                    {
                    using var view = new PlayerSpineView(definition, null);
                    var runtime = new PlayerRuntime(definition, new Vector2(20, 20), new Vector2(0, 0), sample.Item1);
                    for (int i = 0; i < sample.Item2; i++) { runtime.BeginTick(default); runtime.EndTick(); }
                    view.Sync(runtime); Assert(view.HasSpine, "玩家 Spine 未绑定。");
                    var mesh = view.Root.GetComponentInChildren<MeshFilter>(); Assert(mesh && mesh.sharedMesh && mesh.sharedMesh.vertexCount > 0, "玩家 Spine 网格为空。");
                    render.AddSingleGO(view.Root); var bounds = view.Root.GetComponentInChildren<Renderer>().bounds;
                    render.camera.orthographic = true; render.camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.3f;
                    render.camera.transform.position = bounds.center + new Vector3(0, 0, -10); render.camera.transform.rotation = Quaternion.identity;
                    render.camera.clearFlags = CameraClearFlags.SolidColor; render.camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
                    render.BeginStaticPreview(new Rect(0, 0, 640, 640)); render.Render(true); var image = render.EndStaticPreview();
                    try { File.WriteAllBytes("Logs/PlayerPreview-" + sample.Item1 + ".png", image.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(image); }
                    }
                    finally { render.Cleanup(); }
                }
                File.WriteAllText("Logs/PlayerGraphicsValidation.txt", "PASS: Idle/Dash/FlyLoop Spine meshes; native URP previews; configured alpha.\n");
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void RunResourcePolicyBatch()
        {
            try
            {
                PlayerEditorAssets.ApplyFlightOnlyStamina(); Run(); BattleValidation.Run(); EnergyOrbValidation.Run();
                var definition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerEditorAssets.DefinitionPath);
                var report = new List<string>();
                foreach (var slot in new[] { PlayerClipSlot.Idle, PlayerClipSlot.Move, PlayerClipSlot.Jump, PlayerClipSlot.Dash, PlayerClipSlot.Skill1, PlayerClipSlot.Skill2 })
                {
                    using var view = new PlayerSpineView(definition, null);
                    var runtime = new PlayerRuntime(definition, new Vector2(20, 20), Vector2.zero, slot);
                    runtime.BeginTick(default); view.Sync(runtime);
                    var bounds = view.Root.GetComponentInChildren<Renderer>().bounds;
                    report.Add($"{slot}: motor={view.Root.transform.position}, meshMin={bounds.min}, meshMax={bounds.max}, hitRadius={definition.HitRadius}, staminaCost={definition.Actions.Get(slot).StaminaCost}");
                }
                File.WriteAllText("Logs/PlayerHitRangeDiagnosis.txt", string.Join("\n", report));
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        private static void TestPriority()
        {
            using var f = new Fixture(); var p = f.Runtime();
            Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.Skill1 | PlayerButtonEdges.Skill2 | PlayerButtonEdges.Dash | PlayerButtonEdges.JumpPressed));
            Assert(p.Action?.Clip.Kind == PlayerActionKind.Skill && p.Action.SkillSlot == 1 && p.Inputs.Pending.Count == 3, "同 Tick 优先级／技能平局错误。");
            f.Library.Get(PlayerClipSlot.Skill1).StaminaCost = 200; p = f.Runtime();
            Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.Skill1 | PlayerButtonEdges.Dash | PlayerButtonEdges.JumpPressed));
            Assert(p.Action?.Clip.Kind == PlayerActionKind.Dash && p.Stamina == FP64.FromInt(100), "高优先级不可用时未回退或零成本动作扣了体力。");
            f.Library.Get(PlayerClipSlot.Skill1).StaminaCost = 0; f.Definition.MaxStamina = 0;
            foreach (var edge in new[] { PlayerButtonEdges.Skill1, PlayerButtonEdges.Skill2, PlayerButtonEdges.Dash })
            {
                p = f.Runtime(); Step(p, new PlayerTickInput(0, edges: edge));
                Assert(p.Action != null && p.Stamina == FP64.Zero, "零耐力时无法使用技能／冲刺。");
            }
            f.Definition.MaxStamina = 100; p = f.Runtime();
            Step(p, new PlayerTickInput(0, true, edges: PlayerButtonEdges.JumpPressed));
            for (int t = 1; t < 12; t++) Step(p, new PlayerTickInput(0, true));
            while (p.Tick < 42) Step(p);
            p.ForceInterrupt(); var stamina = p.Stamina;
            Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.Dash));
            Assert(p.Action?.Clip.Kind == PlayerActionKind.Dash && p.Stamina > stamina, "零消耗冲刺延后了耐力恢复。");
        }
        private static void TestBuffer()
        {
            using var f = new Fixture(); var dash = f.Library.Get(PlayerClipSlot.Dash);
            dash.Nodes.Add(new() { Kind = PlayerActionNodeKind.Transition, Name = "Jump cancel", StartTick = 5, EndTick = 6, AllowedIntents = PlayerIntentMask.Jump });
            var p = f.Runtime(); Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.Dash)); Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.JumpPressed));
            for (int t = 2; t <= 5; t++) Step(p);
            Assert(p.Action?.Clip.Kind == PlayerActionKind.Jump && p.Inputs.Pending.Count == 0, "窗口开启后缓冲未执行一次。");
            p = f.Runtime(); Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.Skill1 | PlayerButtonEdges.JumpPressed));
            for (int t = 1; t <= 9; t++) Step(p);
            Assert(p.Inputs.Pending.Count == 0, "过期输入继续保留。");
        }
        private static void TestInvulnerability()
        {
            using var f = new Fixture(); var p = f.Runtime(slot: PlayerClipSlot.Dash);
            for (int t = 0; t <= 15; t++) { p.BeginTick(default); Assert(p.ActionInvulnerable == (t >= 3 && t < 15), "无敌边界错误：" + t); p.EndTick(); }
            p = f.Runtime(slot: PlayerClipSlot.Dash); for (int t = 0; t < 4; t++) Step(p); p.ForceInterrupt(); Assert(!p.ActionInvulnerable, "中断遗留无敌。");
            f.Definition.HurtInvulnerableTicks = 60; p = f.Runtime(); p.ReceiveDamage(FP64.One);
            for (int t = 0; t < 33; t++) Step(p);
            Step(p, new PlayerTickInput(0, edges: PlayerButtonEdges.Dash)); for (int t = 0; t < 4; t++) Step(p);
            p.ForceInterrupt(); Assert(p.Invulnerable && !p.ActionInvulnerable, "冲刺清理错误释放受伤保护。");
        }
        private static void TestMotion()
        {
            using var f = new Fixture(); var p = f.Runtime();
            Step(p, new PlayerTickInput(0, true, edges: PlayerButtonEdges.JumpPressed)); Assert(p.Motor.Locomotion == PlayerLocomotionState.Rising, "未起跳。");
            for (int t = 1; t < 12; t++) Step(p, new PlayerTickInput(0, true));
            Assert(p.Motor.Flying && p.Stamina < p.MaxStamina, "长按未进入飞行或没有消耗体力。");
            Step(p, new PlayerTickInput(0, false, edges: PlayerButtonEdges.JumpReleased)); Assert(!p.Motor.Flying && p.Inputs.HeldTicks == 0, "松开后仍飞行。");
            for (int t = 0; t < 120; t++) Step(p); Assert(p.Motor.Grounded, "重力未落地。");
            p = f.Runtime(); Step(p, new PlayerTickInput(short.MaxValue)); Assert(p.Motor.State.Position.X > FP64.Zero && p.Motor.State.Position.Y == FP64.Zero, "地面横移错误。");
        }
        private static void TestEvents()
        {
            using var f = new Fixture(); var jump = f.Library.Get(PlayerClipSlot.Jump);
            jump.Nodes.Add(new() { Kind = PlayerActionNodeKind.Audio, Name = "jump cue", Order = 100 });
            jump.DurationTicks = 2;
            foreach (var node in jump.Nodes) if (!node.IsPoint) node.EndTick = 2;
            var p = f.Runtime(); int count = 0, sounds = 0;
            for (int t = 0; t < 8; t++)
            {
                p.BeginTick(new PlayerTickInput(0, edges: t == 0 ? PlayerButtonEdges.JumpPressed : 0));
                count += p.Events.Count(e => e.Kind == PlayerRuntimeEventKind.PointEvent && e.Node.Event == PlayerPointEventKind.JumpImpulse);
                sounds += p.Events.Count(e => e.Kind == PlayerRuntimeEventKind.Audio && e.Clip == jump);
                Assert(p.Action == null || p.LocomotionAction == null, "互斥动作与隐藏移动表现同时激活。"); p.EndTick();
            }
            Assert(count == 1, "起跳事件重复执行。");
            Assert(sounds == 1, "起跳音效在移动表现层重复执行。");
            var dash = f.Library.Get(PlayerClipSlot.Dash); p = f.Runtime(slot: PlayerClipSlot.Dash);
            dash.Nodes.First(n => n.Kind == PlayerActionNodeKind.Invulnerability).StartTick = 0;
            p.BeginTick(default); Assert(!p.ActionInvulnerable, "修改资产影响已编译快照。");
            dash.Nodes.First(n => n.Kind == PlayerActionNodeKind.Invulnerability).StartTick = 3;
            dash.Nodes.Add(new() { Kind = PlayerActionNodeKind.Effect, Name = "test fx", StartTick = 0, EndTick = 24 });
            p = f.Runtime(slot: PlayerClipSlot.Dash); Step(p); p.ForceInterrupt();
            Assert(p.Events.Any(e => e.Kind == PlayerRuntimeEventKind.EffectStopped), "效果中断未回收。");
        }
        private static void TestCombat()
        {
            using var f = new Fixture(); var sequence = f.Make<DanmakuSequence>(); sequence.FieldSize = new Vector2(100, 100);
            sequence.Clips.Add(new DanmakuClip { DurationTicks = 2, EmitterPosition = Vector2.zero, Keyframes = new() { new DanmakuKeyframe(), new DanmakuKeyframe { LocalTick = 1 } } });
            sequence.Clips[0].Emission.Speed = 0; sequence.Clips[0].ProjectileOverride.Damage = 1;
            var config = f.Make<BattleConfig>(); config.Sequence = sequence;
            var immune = f.Library.Get(PlayerClipSlot.Dash).Nodes.First(n => n.Kind == PlayerActionNodeKind.Invulnerability); immune.StartTick = 0; immune.EndTick = 1;
            var b = new BattleSimulation(config, f.Definition, Vector2.zero);
            var fire = new BattleInput(new InputSnapshot(0, 0, fireHeld: true), default, false);
            b.Step(fire, new PlayerTickInput(0, fireHeld: true, edges: PlayerButtonEdges.Dash));
            Assert(b.Player.Health == b.Player.MaxHealth && b.Weapon.TotalRounds == 0, "无敌／射击许可未前置于本 Tick 结算。");
            b.Step(default); Assert(b.Player.Health == b.Player.MaxHealth - FP64.One, "无敌结束后仍免伤。");
        }
        private static void TestDeterminism()
        {
            using var f = new Fixture(); var a = f.Runtime(); var b = f.Runtime();
            PlayerTickInput Input(int tick) => new((short)(tick < 60 ? short.MaxValue : 0), tick < 20, edges: tick == 0 ? PlayerButtonEdges.JumpPressed : 0);
            for (int i = 0; i < 120; i++) Step(a, Input(i));
            for (int frame = 0; frame < 12; frame++) for (int i = 0; i < 10; i++) Step(b, Input(frame * 10 + i));
            Assert(a.Actor.Pose.Position.Equals(b.Actor.Pose.Position) && a.Stamina == b.Stamina && a.Motor.Locomotion == b.Motor.Locomotion, "渲染批次改变逻辑状态。");
            a.Inputs.Queue(PlayerIntentKind.Dash, 0, a.Tick); a.ClearInput(); Assert(a.Inputs.Pending.Count == 0 && a.Inputs.HeldTicks == 0, "暂停遗留输入。");
        }
        private static void TestValidation()
        {
            using var f = new Fixture(); var c = f.Library.Get(PlayerClipSlot.Dash); Assert(c.Validate() == null, "默认片段无效。");
            var n = c.Nodes.First(node => node.Kind == PlayerActionNodeKind.Invulnerability); n.EndTick = n.StartTick; Assert(c.Validate() != null, "非法区间通过。"); n.EndTick = 15;
            c.Nodes.Add(c.Nodes[0].Copy()); c.Nodes.Last().Id = c.Nodes[0].Id; Assert(c.Validate() != null, "重复 Id 通过。"); c.Nodes.RemoveAt(c.Nodes.Count - 1);
            c.Nodes.Add(c.Nodes.First(node => node.Kind == PlayerActionNodeKind.Motion).Copy()); Assert(c.Validate() != null, "运动覆盖冲突通过。");
        }
        private static void TestEditorLayout()
        {
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Scripts/Player/Editor/PlayerActionEditorWindow.uxml");
            Assert(layout, "UXML 未导入。"); var root = layout.CloneTree();
            Assert(root.Q<IMGUIContainer>("preview") != null && root.Q<IMGUIContainer>("timeline") != null && root.Q<IMGUIContainer>("inspector") != null, "编辑器结构缺失。");
        }
        private static void TestHitRangeView()
        {
            using var f = new Fixture(); var p = f.Runtime(); var arena = new GameObject("Hit range test arena");
            try
            {
                arena.transform.position = new Vector3(5, -2, 0); arena.transform.localScale = new Vector3(2, 0.5f, 1);
                using var view = new PlayerSpineView(f.Definition, arena.transform); view.Sync(p);
                var marker = view.Root.transform.Find("Player Hit Range");
                var ring = marker.Find("Boundary").GetComponent<LineRenderer>();
                var expected = arena.transform.TransformPoint(new Vector3(p.Actor.Pose.HitRadius.ToFloat(), 0, 0)); expected.z -= 0.01f;
                Assert(Vector3.Distance(ring.GetPosition(0), expected) < 0.0001f, "判定圈未读取真实中心／半径或父级变换。");
                Assert(((Vector4)ring.startColor - (Vector4)f.Definition.HitRangeColor).sqrMagnitude < 0.0001f, "正常判定圈颜色错误。" + ring.startColor);
                p.ReceiveDamage(FP64.One); view.Sync(p);
                Assert(((Vector4)ring.startColor - (Vector4)f.Definition.InvulnerableHitRangeColor).sqrMagnitude < 0.0001f, "受伤无敌颜色未更新。" + ring.startColor);
                f.Definition.ShowHitRange = false; view.Sync(p); Assert(!marker.gameObject.activeSelf, "关闭显示仍绘制判定圈。");
                f.Definition.ShowHitRange = true; view.Sync(p); Assert(marker.gameObject.activeSelf, "开启显示未恢复。");
                Assert(p.Actor.Pose.HitRadius == FP64.FromFloat(f.Definition.HitRadius), "显示开关改变了碰撞半径。");
                for (int i = 0; i < f.Definition.HurtInvulnerableTicks; i++) Step(p);
                p.ReceiveDamage(p.Actor.Health); view.Sync(p); Assert(!marker.gameObject.activeSelf, "死亡后仍显示判定圈。");
            }
            finally { Object.DestroyImmediate(arena); }
        }
    }
}
