# 战斗音乐背景

代码入口位于 `Assets/Scripts/MusicPresentation`，命名空间为 `TapTap.MusicPresentation`。本模块只观察战斗音乐，不播放另一份音轨，不推进战斗时间。

2026-10-11：已通过当前 Unity 编辑器接入并保存 `Assets/Scenes/GameplayPlaceholder.unity`。BattleManager 上配置共享数据源，其子物体 MusicBackground 配置半径 3.2、144 个采样点和明确材质。StagePlaceholder 排序为 -200，音乐曲线为 -100，弹幕为 0；角色和 Boss 保持原有排序。进入 Play Mode 即可随现有战斗音乐运行。

菜单 `工具 > 音乐表现 > 接入当前战斗场景` 可重新接入，已有单条曲线时复用它并保留形状参数。材质资产为 `Assets/Materials/MusicPresentation/BattleMusicBackground.mat`。接入报告输出到 `Logs/BattleMusicSceneSetup.txt`，编辑状态的实际相机渲染截图为 `Logs/BattleMusicBackground.png`。

## 接入

1. 在现有 BattleManager 所在物体上添加 `BattleMusicPresentationSource`，Battle 可自动绑定同物体的 BattleManager，也可明确拖入其他宿主。
2. 在它下面创建空子物体 `MusicBackground`，添加 `BattleMusicBackgroundCurve`，会自动添加 LineRenderer，并查找父级数据源；也可明确指定 Source。
3. 子物体放到战斗背景位置，局部 Z 为 0、旋转为 0、缩放为 1。轮廓在局部 XY 平面绘制。
4. 默认 Radius=2.6、Sides=6、Generations=1、SamplesPerSegment=6，生成 144 个采样点。调 Radius 和位置决定画面覆盖区域。
5. 配置 SortingLayer 和 SortingOrder，保证曲线位于背景之上、角色和弹幕之下。默认 Default/-100；若背景也在 Default 且 order 为 0，需要把背景放到更低的 order，或为装饰曲线建立明确的层。
6. Material 可留空。模块从 Resources 加载随代码提供的 `BattleMusicLine.shader`，为该曲线创建一次材质，并在销毁时释放。自定义材质需支持 URP 2D Pass 和顶点颜色；自动 Brightness/Opacity 仅作用于拥有 `_Tint` 属性的材质。

同一 Battle 下多条曲线共用一个 Source，分别设置形状、半径、颜色和旋转速度即可。Source 只查找明确的 Battle，不查找场景中的任意 AudioSource。

## 表现参数

- `Generator`：使用关键点定义每条边的分形偏移；首尾为 (0,0)、(1,0)，内部关键点决定形状。这里只读取关键点位置，曲线切线不参与几何生成。
- `Generations`：0 为基础多边形，1–3 为分形；最终控制点不能超过 512，最终采样点不能超过 4096。超限时隐藏曲线并打印一次具体错误，调整后重建。
- `Deformation`：控制低/中/高频段驱动的形变强度，初始边按三频段循环映射。每次细分保留所属初始边的频段。
- `RadialPulse`：全曲幅值驱动整体呼吸幅度；不是检测到的 BPM 拍点。
- `RotationDegreesPerSecond`：按歌曲时间计算旋转，暂停不会继续转。
- `Width`：世界单位线宽。`Opacity` 默认 0.3，避免装饰轮廓遮挡弹幕；`Brightness` 可以输出 HDR 颜色，但真实 Bloom 需另行启用相机后处理及 Volume。
- 数据源默认 1024 点 Blackman 频谱，采用 40–250、250–2000、2000–10000 Hz 三频段。合并左右声道幅值，归一化峰值有下限，并使用按秒计算的 attack/release。
- `MinimumPeak` 控制弱信号底限，`Sensitivity` 控制强度，`PeakReleaseSeconds` 控制峰值记忆衰减。静音时实时频谱的表现与播放输出相关；本模块尚未提供离线能量包络。

Inspector 修改几何参数会自动重建；外部代码修改拓扑、材质、线宽、颜色或排序后，调用 `curve.Rebuild()`。形变、整体脉冲、亮度、透明度和旋转速度逐帧读取，无需重建。未绑定 Source 时保持静态轮廓，便于先调整构图。

## 时间和生命周期

BattleManager 只新增音乐源、歌曲秒数和播放状态的只读入口。MusicTimelineTransport 只新增只读的 SongSeconds、AudioPlaying、WaitingForAudio，不改变原有播放逻辑。

暂停和战斗补步暂停冻结包络与歌曲时间；恢复的 DSP 预约阶段也等待。正歌曲偏移等待期间不启动动画，负偏移从实际歌曲位置开始旋转。换曲、重播、检测到时间回退会复位频谱历史；歌曲结束后包络平滑回落并保持最后的旋转角度。Stop 或 Source 禁用时复位。

数组和几何缓存只在首次采样、尺寸变更或重建时分配；常规帧复用缓存。Catmull–Rom 平滑采用闭环邻接，LineRenderer.loop 负责连接末点到首点，不重复存储末点。Catmull–Rom 可能在尖角略有过冲，尖锐风格可将 SamplesPerSegment 设为 1。

## UI 随音乐跳动

`MusicReactiveUI` 可挂在任何带 RectTransform 的 UI 图标上，Source 绑定同一个 BattleMusicPresentationSource。Target 留空时控制自身；指定 Target 时可控制其他图标。对于 LayoutGroup 下的图标或按钮，保持外层布局槽/点击区域稳定，把脚本和动画目标放在内部视觉节点。它只改变位置、旋转和缩放，不修改 Sprite、颜色或点击事件。

- `Response = Auto`：优先读取当前 Sequence.ManualMusicBpm 和 ManualMusicBeatOffsetSeconds，没有有效 BPM 时使用频谱。
- `Response = Spectrum`：用 Band 指定的低/中/高频或总幅值控制放大和向上跳动，左右摇摆速度由 SpectrumSwayFrequency 控制。
- `Response = Beat`：按音乐时间计算拍点脉冲，相邻整拍分别摆到左、右。需要有效的 Sequence BPM 或 BpmOverride；首拍之前保持基础姿态。
- `BpmOverride > 0`：覆盖歌曲节拍配置，并使用 FirstBeatSeconds 作为首拍时间。它的时间以歌曲开头为 0，不是战斗开始时间。
- 默认 ScaleAmount=0.1，最大为初始缩放的 1.1 倍；RotationDegrees=6，摆角 ±6°；PositionAmount=(4,5)，左右移动 ±4，向上跳动最多 5 个 UI 单位。把对应幅度设为 0 可分别关闭缩放、旋转或位移。
- BeatDecay 决定拍内脉冲衰减，AttackSeconds/ReleaseSeconds 决定姿态的响应和回落。音乐暂停、补步暂停和恢复预约期间冻结；歌曲结束或数据源禁用时平滑回到基础姿态，组件禁用时立即还原。

运行时创建的 HUD 可以显式调用 `reactiveUI.Bind(battleMusicSource)`，不需要查找场景中任意音源。更换动画目标使用 `SetTarget(rectTransform)`，会先还原旧图标。每次启用缓存基础姿态，每帧直接从基础值计算，避免缩放/旋转累积漂移。不要同时让 Animator、Tween 和该脚本写相同目标的 Transform。

战斗 HUD 由 SystemRoot 在运行时创建。`BattleMusicHudBinding` 挂在战斗场景的 BattleManager 上，Source 指向同物体的共享音乐数据源；它在 HUD 创建后绑定其内部的 MusicReactiveUI，退出战斗时解绑并还原图标。重复绑定不会重置动画，旧战斗销毁时不会清除下一场战斗已接管的绑定。

菜单 `工具 > 音乐表现 > 接入当前战斗HUD跳动` 使用当前战斗 SystemRoot.Config.UIPrefab 对应的实际 UI 预制体，给头像、技能 1、技能 2、麦克风添加 MusicReactiveUI，并保存场景中的绑定组件。已有组件保留动画幅度，新组件设置为 Auto、ScaleAmount=0.08、RotationDegrees=4、PositionAmount=(3,4)。执行前检查图标尺寸以及 LayoutGroup、按钮、Animator 和 ContentSizeFitter，避免与现有布局或点击控件冲突。HUD 预制体中的 Source 留空，由场景绑定组件在运行时填写。接入报告见 `Logs/BattleMusicUISetup.txt`。

2026-10-11：实际 Unity 编辑器已接入并保存 GameplayPlaceholder 战斗场景及 SpotlightUI 预制体。BattleManager 上新增 BattleMusicHudBinding，Source 指向既有 BattleMusicPresentationSource；SafeAreaRoot/HUD 下的 Portrait、Skill1、Skill2、Microphone 均已添加 MusicReactiveUI。战斗场景未加载时，接入菜单会额外打开 GameplayPlaceholder，保留其他场景和未保存内容。此流程以及绑定、战斗交接和解绑还原已通过 Unity 验证。

2026-10-11：MusicReactiveUI 已通过 Unity 2022.3.62f1c1 编译与验证，覆盖首拍偏移、相邻拍左右交替、拍内衰减、无 BPM/异常时间、重复更新无姿态漂移、暂停冻结、更换目标与禁用还原。验证脚本采用反射调用编辑模式下的组件方法，避免通过 SendMessage 触发 Unity 的 ShouldRunBehaviour 断言。报告见 `Logs/MusicPresentationValidation.txt`。

## 验证

菜单 `Tools > Music Presentation > 运行音乐背景验证` 检查静音与无效频谱值、左右声道、44.1/48 kHz 分频、不同帧率的平滑、七边形闭环、分形形变、顶点上限、音乐偏移/预约/暂停/结束状态，以及 Shader 导入和 LineRenderer 初始化。报告输出到 `Logs/MusicPresentationValidation.txt`。它不会修改当前场景。

背景组件的视觉位置、SortingLayer 与实际音乐强度可继续在目标战斗场景中调试；当前 GameplayPlaceholder 已挂载组件，HUD 保持原有结构。

2026-10-10：已在隔离验证项目中使用 Unity 2022.3.62f1c1 完成脚本编译并运行上述验证，全部通过。报告同时复制到当前项目的 `Logs/MusicPresentationValidation.txt`。此次验证包含 Shader 导入与 LineRenderer 初始化，没有进行实际战斗场景的截图、音频延迟或 Profiler 验收。

2026-10-11：接入脚本编译与音乐表现验证再次通过。在当前编辑器中保存战斗场景后，使用已有相机执行 URP 渲染请求，并对比启用/隐藏曲线的结果，1440 个像素发生明显变化，实际可见性验证通过。截图为编辑状态，没有进行完整战斗播放、音频延迟或 Profiler 验收。
