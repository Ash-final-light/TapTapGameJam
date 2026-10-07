# 弹幕输入层（第一步）

`GameplayPlaceholder` 当前通过 BattleManager 读取现有 SystemRoot.Input 和 SpotlightControls，已接入普通/散射射击及鼠标瞄准；操作见[战斗第一阶段接入说明](../Docs/战斗第一阶段与音频解析-接入说明.md)。本目录的 DanmakuControls 和 PlayerInputManager 用于另一个明确配置的独立输入入口，不能在当前实战场景重复创建 PlayerInput。手机屏幕控件仍需后续接入。

## 独立 PlayerInput 场景配置

1. 在玩家对象上添加 `PlayerInput`，将 Actions 指向 `DanmakuControls`，Default Map 设为 `Gameplay`。
2. 在同一对象上添加 `PlayerInputManager`；其 Default Action Map 默认也是 `Gameplay`。管理器自动要求 `PlayerInput` 组件。
3. 玩家逻辑通过 `GetInputAction(InputConstants.Move)?.ReadValue<Vector2>()` 读取当前移动方向，通过 `IsPressed` 读取持续按键；一次性操作可注册 Started／Performed／Canceled 回调或按游戏 Tick 缓冲。
4. 打开菜单等界面时调用 `SwitchCurrentActionMap(InputConstants.UIMap)`，返回游戏时切回 `GameplayMap`。注册过的同名事件会在切换时重新绑定；如果其他系统直接切换 `PlayerInput` 的 Map，管理器也会在下一次 Update 或查询输入时刷新绑定。

PlayerInputSampler 由明确绑定它的 BattleManager 在每个渲染帧采样，InputSnapshot 在统一的 60 Hz 玩家／弹幕 Tick 中消费。输入事件回调不直接移动角色、判定体或子弹。

## 默认绑定

| Action | PC | Gamepad |
| --- | --- | --- |
| Gameplay/Move | WASD／方向键 | 左摇杆／十字键 |
| Gameplay/Focus | 左／右 Shift | 左肩键 |
| Gameplay/Fire | Z／鼠标左键 | 南按钮 |
| Gameplay/Aim | 鼠标位置（Value / Vector2） | 尚未绑定 |
| Gameplay/Bomb | X | 东按钮 |
| Gameplay/Pause | Esc | Start |
| UI/Navigate | 方向键 | 左摇杆／十字键 |
| UI/Submit | Enter／空格 | 南按钮 |
| UI/Cancel | Esc | 东按钮 |
| UI/Point／Click | 鼠标位置／左键 | 由后续场景 UI 输入模块处理 |

手机的虚拟摇杆和按钮后续使用 Input System 的 OnScreenStick／OnScreenButton，Control Path 对应 `<Gamepad>/leftStick`、`<Gamepad>/leftShoulder`、`<Gamepad>/buttonSouth`、`<Gamepad>/buttonEast` 和 `<Gamepad>/start`。这些控件向虚拟 Gamepad 写入，复用同一套 Gameplay 绑定；触控 UI 点击由场景的 InputSystemUIInputModule 负责。此阶段尚未创建屏幕控件或 UI 模块配置。

`PlayerInputManager` 去掉了参考工程的 Sirenix 依赖，回调分发不再每次创建数组；禁用／销毁时解除 Action 订阅。`GetInputAction`、`IsPressed` 和 `WasPerformedThisFrame` 在尚无活动 Map 时返回空或 false。`WasPerformedThisFrame` 是渲染帧查询，固定 Tick 的 Bomb／Pause 等一次性输入接入时仍需独立缓冲，避免低帧下漏读或在多 Tick 中重复消费。
