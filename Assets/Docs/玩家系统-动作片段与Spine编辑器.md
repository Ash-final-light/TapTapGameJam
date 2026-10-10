# 玩家系统：动作片段与 Spine 编辑器

本次实现基于 Unity 2022.3、项目已有 Spine 3.8 运行库及 60 Hz 定点数模拟。运动规则为横向移动、重力跳跃、长按飞行。

## 开始使用

1. 等待 Unity 导入脚本和资产。打开 `Tools > Player > 动作片段编辑器`。
2. 玩家选择 `Assets/Player/MaidGhost.asset`。左侧选择动作，双击动作资产也会打开窗口。
3. 预览区域使用实际 Spine 模型；时间轴可逐 Tick 播放、拖动节点或调整区间两端。点事件带菱形标记。
4. 右侧编辑片段参数和选中节点。支持复制、粘贴、删除和 Unity Undo/Redo；点击保存资产落盘。
5. 下方开启输入调试模式，再点击同 Tick 输入、冲刺、跳跃，检查缓冲和拒绝原因。JumpFly 保持使用逻辑 Tick 计数。
6. `Tools > Player > 运行玩家系统验证` 运行逻辑、数据和编辑器导入检查。

`GameplayPlaceholder` 中 BattleManager 分别引用 BattleConfig 与 PlayerDefinition。默认 A/D 横移、Space 跳跃/保持飞行、Left Shift 冲刺、Q/E 技能、鼠标左键射击；改键继续使用原 SystemRoot 输入资产。

游戏内常显受击判定圈：黄色轮廓与中心点标记真实受击圆，无敌时变为青色，黑色描边保证背景对比。它直接读取战斗结算的 Actor.Pose 中心与半径，不根据 Spine 图像包围盒猜测范围。玩家定义中的 ShowHitRange 可开关显示，HitRangeColor / InvulnerableHitRangeColor 可改色；隐藏仅影响显示，不改变受击判定。死亡时自动隐藏，暂停时保持当前位置。

当前逻辑中心仍在角色原点附近，半径 0.08，本次可视化不改动受击规则。若后续希望中心移到躯干，需要增加独立的受击中心偏移，并同步连续碰撞检测与预览，不能只移动判定圈。

## 配置归属

- `BattleConfig` 保留 Sequence、Boss 生命/位置/判定半径、无敌接触耗弹规则和弹幕容量。
- `PlayerDefinition` 保存玩家生命/体力/判定半径、Motor 参数、输入规则、武器、动作库、Spine 与 HUD 表现配置。
- 出生位置保存在场景入口 BattleManager 的 PlayerSpawn 中。
- 玩家运行时唯一持有 ActorState。BattleSimulation 的 Player 是这个同一对象的引用，不再另建玩家生命状态。
- DanmakuSequence 的旧 PreviewPlayerConfig 与旧平面 Motor 保留给弹幕编辑器预览和旧示例；正式 BattleManager 使用新玩家系统。

外部创建战斗：

```csharp
var battle = new BattleSimulation(battleConfig, playerDefinition, spawnPosition);
battle.Step(battleInput, playerTickInput);
```

`SystemInputRouter.CapturePlayerTick()` 在权威 Tick 边界统一消费一次性命令。正式战斗不再调用旧的 `ConsumeGameplayCommands()` 直接派发技能；技能必须先通过玩家仲裁，才进入动作片段。

## 时间与节点

- 固定 60 Tick/s，时长只保存整数 Tick。编辑器秒数由 Tick 换算。
- 持续节点使用 `[StartTick, EndTick)`；Start 生效，End 已失效。
- 点事件在 StartTick 执行一次。同 Tick 的点事件按 Order 排序，重复 Order 会提示。
- 节点类型包含 Spine、Motion、Invulnerability、Transition、FirePermission、PointEvent、Audio、Effect。
- 同一时间同一 Spine Track、运动覆盖或射击覆盖不能重叠。多个无敌来源允许叠加。
- 运行时复制节点与动作规则，弹幕模板在战斗创建时编译；修改源资产不改变当前战斗逻辑。

DefaultTransitions 是整个片段的许可；Transition 节点仅在其区间内额外开放许可，并检查指定条件。Fly 的条件包含空中、保持、达到长按阈值、体力足够。允许飞行不代表强制进入飞行。

## 默认动作

| 片段 | 行为 |
| --- | --- |
| Idle / Move | 待机与横移表现，支持射击 |
| Jump | Tick 0 起跳冲量，允许技能/冲刺/长按飞行接续 |
| Fall | 重力下落表现 |
| FlyStart / FlyLoop / FlyEnd | 飞行切换表现；飞行保持时消耗体力，松开或体力不足恢复重力 |
| Land | 落地反馈 |
| Dash | 24 Tick；不消耗耐力；[3,15) 锁定方向冲刺与无敌；[12,24) 可接飞行 |
| Skill1 / Skill2 | 30 Tick；不消耗耐力；Tick 6 发射节点；[18,30) 可接冲刺 |
| Hurt | 受伤表现；独立受伤保护默认 30 Tick |
| Dead | 禁止移动与射击；死亡表现保持末帧 |

默认技能节点发射现有武器单弹模板，是可替换的技能片段预设。正式弹反、特定技能机制和道具库存不由此预设定义，可替换节点 Projectile 和动作数据后继续接入。

耐力条默认仅用于飞行：每秒消耗 12，停止消耗 30 Tick 后每秒恢复 20。技能与冲刺只使用各自动作冷却，不要求剩余耐力，也不会延后耐力恢复。`Tools > Player > 耐力仅用于飞行` 可将当前默认动作库的三项成本重新设为 0，不修改动画和事件节点。

Spine 现有 Standby、Run、Jump、Hurt、Die 及击打动画用于默认绑定。Dash/Fly 没有专用动画，明确使用 Jump 占位。右侧 Spine 节点可指定 AnimationReferenceAsset；编辑器会提示使用占位绑定。

音效为生成的短提示音，供动作状态验证；DashTrail 使用 TrailRenderer。正式音效和特效可替换节点资源。音效接入现有 Effects 音量服务。

## 输入与生命周期

互斥动作默认优先级为 Skill > Dash > Jump，同优先级先比较按下 Tick，再比较技能槽位和序列。高优先级不可用时继续检查低优先级。缓冲默认 8 Tick，长按阈值默认 10 Tick。

射击是独立保持通道，读取当前动作的射击许可。冲刺/技能默认禁止射击，结束后保持键仍按住则恢复。移动不参与互斥仲裁。

冲刺无敌与受伤保护分别计算。动作结束或强制中断释放该动作的速度覆盖、无敌、音源和特效；不会提前释放独立受伤保护。无敌保留接触检测，是否耗弹由 BattleConfig 决定。

暂停、失焦和 Stop 清理按下缓冲及长按计数。暂停冻结逻辑与表现音源；继续时不补发旧按下事件。

## 预览与边界

拖动时间轴会重建隔离预览，从 Tick 0 重放到选定时间，恢复逻辑、动画及持续效果，不播放过去的提示音。顺序播放才触发音效。窗口关闭时释放模型、音源、效果与 PreviewRenderUtility。

当前 Motor 使用可配置 GroundHeight 的平面竞技场地面，以及弹幕场地边界。尚未接入坡面、平台、墙面或 Unity Physics2D 地形碰撞；这些需要后续提供地面查询实现，不能依赖 Spine 或 Transform 反向修改定点数状态。

迁移菜单读取旧 Battle.asset 中的玩家/武器引用、生命、无敌时间和出生位置，生成独立玩家资产，再通过 Unity Editor API 写入场景绑定并重新序列化 BattleConfig。已存在的独立玩家资产不覆盖重建，迁移拒绝保存有未保存修改的已加载场景。
