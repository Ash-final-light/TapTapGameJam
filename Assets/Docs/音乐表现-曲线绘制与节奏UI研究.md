# 音乐表现：曲线绘制与节奏 UI 研究

日期：2026-10-10。依据：`D:/UnityProject/__REF/AudioPeer.cs`、`KochGenerator.cs`、`KochLine.cs`，当前项目源码、URP 14.0.12 包源码和 Unity 官方文档。

本文是研究与接入方案。代码块是实现示意，尚未作为运行时脚本导入、编译或进行画面验收。

## 1. 结论与当前项目条件

现有管线可以绘制参考中的音乐曲线，无需切换渲染管线或增加 Renderer Feature。建议共用一个音乐表现数据源，分开输出连续频段能量、拍点脉冲和音乐相位：曲线使用连续能量，UI 左右摇摆使用音乐相位，缩放使用拍点脉冲。

| 已确认事实 | 源码或资产 | 对实现的影响 |
| --- | --- | --- |
| Unity 2022.3.62f1c1；URP 14.0.12；uGUI 1.0.0 | ProjectSettings/ProjectVersion.txt；Packages/manifest.json | 按当前版本实现，不引入 Unity 6 Render Graph API |
| GraphicsSettings 指向 UniversalRP，默认 Renderer 为 Renderer2D | Assets/Settings/UniversalRP.asset；Renderer2D.asset | 场景曲线使用可被 2D Renderer 绘制的 Pass |
| GameplayPlaceholder 相机使用默认 Renderer，正交尺寸 5.94，后处理关闭 | Assets/Scenes/GameplayPlaceholder.unity | 曲线位于 XY 平面；要有 Bloom 还需要单独配置相机后处理和 Volume |
| UI 是 Screen Space - Overlay，参考分辨率 1920×1080 | Assets/UI/Spotlight/Prefabs/SpotlightUI.prefab | HUD 曲线走 Canvas 网格；LineRenderer 不会因为成为 Canvas 子物体而变成 UI Graphic |
| 战斗音乐由 BattleManager 创建 AudioSource，并注册 Music 音量通道 | Assets/Scripts/Danmaku/Runtime/Battle/BattleManager.cs | 绑定同一音乐源，避免另播一份音乐，也避免把音效混入频谱 |
| MusicTimelineTransport 以音乐样本位置推进时间 | Assets/Scripts/Danmaku/Runtime/Audio/MusicTimelineTransport.cs | 拍点与摆动沿用这个时间源，避免自行累积 Time.time |
| 已有三频段起音候选点、整曲波形摘要 | Assets/Scripts/Danmaku/Runtime/Audio/MusicAnalysisAsset.cs | 可驱动候选点脉冲；不能直接当成连续能量包络或固定 BPM |

## 2. 参考代码实际上做了什么

调用链：`AudioSource → 512 点频谱 → 8 个分频值 → 峰值归一化与衰减缓冲 → Koch 顶点插值 → 可选二次 Bézier 采样 → LineRenderer`。

- `AudioPeer` 使用 `GetSpectrumData(samples, 0, FFTWindow.Blackman)`，只读取第 0 声道。频段分别使用 2、4、8、16、32、64、128、258 个频谱元素，合计 512。它输出音量/频谱强弱，没有检测 BPM、小节或拍号。
- `KochGenerator` 从三角形、多边形或圆开始，用 AnimationCurve 的内部关键点细分边。`_position` 是最后一代细分后的基线，`_targetPosition` 是偏移后的形态。它是几何生成器，不依赖特定渲染管线。
- `KochLine` 为每条初始边指定频段，在两个形态之间插值；缓冲频段越强，曲线越接近目标形态。Bézier 平滑也在 CPU 上完成。
- `_EmissionColor` 只是向材质写入的属性名。是否生效取决于 Shader 是否读取它，是否出现光晕还取决于后处理。

因此参考适合“随音乐起伏的分形轮廓”。规则地一拍向左、一拍向右，还需要另一个拍点/相位信号。

## 3. 在 URP 2D 中画场景曲线

### 3.1 首选方案

先保留几何生成思路，用 `LineRenderer` 绘制，材质选 `Universal Render Pipeline/2D/Sprite-Unlit-Default`，纹理先用白色。该 Shader 在当前包里含 `Universal2D` Pass，并读取顶点 COLOR，适合 LineRenderer 的颜色渐变。

关键设置：

```csharp
line.useWorldSpace = false;
line.alignment = LineAlignment.TransformZ; // 本地 XY 平面，法线为 Z
line.sharedMaterial = configuredMaterial;  // 明确绑定材质资产
line.positionCount = points.Length;
line.SetPositions(points);
```

点的 Z 设为 0，物体旋转与 XY 场地一致。参考 `axis = YAxis` 才生成 XY 平面，因为它把旋转轴设为 Z；这个枚举名容易误读。

LineRenderer 的宽度以世界单位计。正交相机下，在物体缩放为 1、铺满目标高度的条件下，可以按 `worldWidth ≈ pixelWidth × 2 × orthographicSize / pixelHeight` 估算。当前相机在 1080 高度、尺寸 5.94 时，3 像素约为 0.033 世界单位；这只是起调值，不是所有分辨率下的固定像素宽度。[Unity Line Renderer 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/class-LineRenderer.html)

闭环只选一种约定：不重复末点并设置 `line.loop = true`，或重复首点并设置 `line.loop = false`。开放曲线不重复首点，也不开 loop。

通过 `sortingLayerID`/`sortingOrder` 将装饰曲线安排在背景之上、弹幕和角色之下；同时确认相机 Culling Mask。尖角先调少量 corner vertices，开放线段再调 cap vertices；它们不会替代曲线采样。厚线在凹角和自交处仍可能叠色。

### 3.2 Pass 与材质兼容性

实际查阅的包源码：

- `Library/PackageCache/com.unity.render-pipelines.universal@14.0.12/Runtime/2D/Passes/Render2DLightingPass.cs`：绘制 Pass 列表包含 `SRPDefaultUnlit` 和 `Universal2D`。
- `Shaders/2D/Sprite-Unlit-Default.shader`：有 `Universal2D` Pass，透明混合、Cull Off、ZWrite Off，并将纹理颜色乘顶点颜色。
- `Shaders/Unlit.shader`：首个 Unlit Pass 没有显式 LightMode，默认按 `SRPDefaultUnlit` 匹配。因此不能笼统断言普通 URP/Unlit 在 2D Renderer 下完全不能画。
- `Shaders/UnlitForwardPass.hlsl`：常规着色使用纹理与 `_BaseColor`，没有使用 LineRenderer 的顶点 COLOR。因此需要渐变时，普通 URP/Unlit 不是首选。

自写 Shader 时，明确使用 `RenderPipeline = UniversalPipeline` 和 `LightMode = Universal2D`。官方文档也确认未声明 LightMode 的 Pass 默认为 SRPDefaultUnlit。[URP 14 Pass tags](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/urp-shaders/urp-shaderlab-pass-tags.html)

### 3.3 发光效果

Sprite-Unlit-Default 没有 `_EmissionColor`，不能直接沿用参考的 SetColor 调用。简单方案是通过线条颜色表现强弱；需要 HDR 发光时，再写读取 HDR 颜色的 Shader，输出 `vertexColor × texture × hdrTint`，配合 Bloom。

可采用透明 Alpha 混合；需要更强的亮线可用 Additive 混合，但交叉处会变亮。Shader 应读取顶点 COLOR，使用 URP Core.hlsl，并明确关闭剔除和深度写入。亮度属性建议命名为 `[HDR] _Tint`，使用 MaterialPropertyBlock 给场景 Renderer 设置每实例参数，或在确有独立材质需求时创建并释放材质实例。

当前 URP 资产支持 HDR，但 GameplayPlaceholder 相机后处理关闭。实际光晕必须在相机/Volume 中验证。若只需描边光感，可以先画一条细亮线和一条较宽、透明度较低的线。

## 4. 曲线属于 HUD 时的绘制方法

Overlay Canvas 绘制在场景画面之上，UI 内部排序主要取决于 Canvas 和层级顺序。[Unity Canvas 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/UICanvas.html)

| 表现位置 | 推荐实现 | 选择依据 |
| --- | --- | --- |
| 战斗场地背景、围绕 Boss 的轮廓 | LineRenderer + URP 2D Unlit | 沿用参考，世界坐标与场景排序方便 |
| HUD 图标周围的圆环、菜单音乐曲线 | 继承 UnityEngine.UI.MaskableGraphic | 跟随 RectTransform、Canvas 排序和 UI 遮罩 |
| HUD 中确实需要复杂场景特效和后处理 | 独立相机 → RenderTexture → RawImage | 能显示相机特效，但有额外渲染、纹理与尺寸维护成本 |

HUD 首选 `MaskableGraphic.OnPopulateMesh(VertexHelper)`：把采样后的二维点沿切线两侧扩展半个线宽，连接为三角形带。开放曲线处理端点，闭环处理首尾相邻；跳过重合点，限制 miter 长度或使用 bevel join，防止急转角产生尖刺。先复用默认 UI 材质；自写 UI Shader 时需保留 UI 裁剪、Stencil、CanvasGroup Alpha 和顶点颜色支持。

每帧更新固定长度的点缓存，数据变化后调用 `SetVerticesDirty()`，不重新创建 GameObject 或 Material。装饰 Graphic 设置 `raycastTarget = false`。动态曲线与频繁变动的图标可放在小型子 Canvas 中，是否拆分以 Canvas 重建和 draw call 的实际性能为依据。

普通 Overlay UI 不进入场景相机的 Bloom。HUD 光晕优先用透明外层线条；确需真实 Bloom 时再采用 RenderTexture 路径。

## 5. 音乐信号：连续能量与拍点分别获取

### 5.1 曲线连续变形

轻量第一版：绑定 BattleManager 已创建的 musicSource，每帧只采样一次频谱，再共享给所有表现组件。建议采用现有项目的低/中/高三频段：40–250 Hz、250–2000 Hz、2000–10000 Hz。只有要复刻每条边的不同频段时，才扩到 8 段。

`GetSpectrumData` 使用 `AudioSettings.outputSampleRate`，不是 AudioClip.frequency；长度需为 64–8192 的二次幂。N 个输出元素覆盖约 0 到输出采样率的一半，按 `bin ≈ Hz × 2N / outputSampleRate` 映射，并限制到有效范围。当前 API 文档指出第一次调用可能返回空数据，且不适合要求严格时序或低延迟的分析；因此仅用于装饰变化。[Unity GetSpectrumData 文档](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.GetSpectrumData.html)

平滑用以秒为单位的 attack/release：

```text
tau = target > smoothed ? attackSeconds : releaseSeconds
smoothed += (target - smoothed) * (1 - exp(-deltaSeconds / tau))
```

建议起调 attack 0.02–0.05 秒、release 0.12–0.25 秒。除数至少 epsilon，输出 Clamp01，静音/停止时复位或回落；不要让负缓冲或 NaN 传入顶点。

实时频谱是播放输出相关信号，音量变化、声音虚拟化等情况需在目标版本中实测。若要求静音时仍保持相同视觉，使用离线能量包络；不要用音量为零时的频谱反推原始歌曲能量。

更稳定的后续方案：扩展离线解析，按 Hop 保存连续 Low/Mid/High 能量包络，运行时按歌曲时间插值。现有 `MusicMarker.Strength` 是起音频谱变化强度，不是连续能量；`WaveformMinMax` 是整首歌的 2048 桶波形摘要，也不能直接代替该包络。

### 5.2 图标按拍摆动与缩放

规则摇摆推荐新增手工填写的 `Bpm` 和 `FirstBeatSeconds`，变速歌曲再升级为 tempo map。现有 SongDefinition、DanmakuSequence 都没有这两个字段，也没有确认过歌曲 BPM，实施时不能猜测实际值。

采用统一时间定义：

```text
songSeconds = musicTransport.Seconds - sequence.MusicStartSeconds
beat = (songSeconds - firstBeatSeconds) * bpm / 60
phase = beat - floor(beat)
sway = -cos(PI * beat)
angle = maxAngleDegrees * sway
offsetX = maxOffsetX * sway
pulse = exp(-phase / decayInBeats)
scale = 1 + maxScaleExtra * pulse
```

`sway` 每两拍完成一个左右周期，相邻整拍分别到达左、右极值；想每拍完成整个周期就用 `sin(2PI × beat)`。`pulse` 在每拍开始最大，然后快速回落。起调范围：摆角 ±4–8°、横移 0–8 个参考 UI 单位、缩放 1.00–1.08 或 1.12、decay 0.15–0.25 拍。

旋转和横移都支持，先以旋转为主、横移较小。强弱拍可通过四拍循环调整脉冲幅度；这个做法需要歌曲节拍网格配置正确。

没有 BPM 时，可按现有低频起音候选点产生快速放大脉冲。候选点不保证间隔规则，不宜把每个检测结果当成准确音乐拍；左右交替只能得到“起音驱动的摆动”，不能保证与固定节拍同相。

低帧率时，按当前音乐时间直接计算姿态；候选点消费检查 `(previousSongSeconds, currentSongSeconds]`，跨过多个点时选择最近有效点并按已过去的时间计算衰减，避免一帧补播全部动画。

## 6. 在现有 HUD 中接入图标动画

已读取 HUD.prefab 的父子关系：Portrait（150×150）、Microphone/Skill1/Skill2（100×100）都是 HUD 的直接子物体；Slots 位于 ItemSelector 下，槽内 Icon 是 46×46，ItemUse 内的 Icon 为 70×70。

优先选择 Portrait 或新增音乐装饰图标作为首个验收对象。Microphone 目前代表麦克风状态，若给它增加音乐动画，应保持这个状态含义清晰。技能图标、道具图标是否一起动属于表现选择，建议先少量使用以保持 HUD 易读。

推荐层级：

```text
原有图标位置或布局槽
└── MotionRoot       // 只承担节奏动画
    └── Image        // 原有 Sprite、颜色、状态继续由 HudView 管理
```

MotionRoot 的 Pivot 居中；原有按钮尺寸和布局槽保持稳定。位于 LayoutGroup 下时，由外层负责布局，内层负责摆动，避免每帧改布局驱动的 RectTransform。

初始化缓存基础 `anchoredPosition`、`localRotation`、`localScale`，每帧从基础值算当前姿态：

```csharp
motionRoot.anchoredPosition = basePosition + new Vector2(offsetX, 0);
motionRoot.localRotation = baseRotation * Quaternion.Euler(0, 0, angle);
motionRoot.localScale = baseScale * scale;
```

不累乘上一帧的 scale，也不逐帧累加 angle。不同时让 Animator、Tween 和本组件写同一组 Transform 属性。频谱采样在统一数据源完成，视觉更新可在 LateUpdate 读取当帧数据。

## 7. 统一时钟与生命周期

当前 BattleManager 的 musicSource、music 是私有字段。实施时建议提供只读音乐表现上下文或显式 Bind，不让视觉脚本搜索场景内第一个 AudioSource，也不自行启动第二份 MusicTimelineTransport。

MusicTimelineTransport.Seconds 的语义是战斗时间轴秒数；正 MusicStartSeconds 表示音乐延迟开始，负值表示从歌曲中段开始。拍点计算必须转换为歌曲时间，不能直接将 Seconds 当作歌曲时间。

RuntimeMusicBackend 每次播放/恢复有 0.1 秒 DSP 预约窗口。仅检查 transport.Playing 还不能说明音频已开始，需要上下文提供等待预约、播放、暂停、停止/结束等状态。暂停时冻结姿态或在固定短时间内回正；恢复等待真实音乐推进，再继续相位。

BattleManager 超出补步预算会暂停音乐追齐逻辑，这期间同样冻结音乐表现。歌曲结束后 transport 仍可推进战斗时间，此时停止节奏输出或渐隐，避免图标持续对着无声音乐摇摆。

Seek、重播、换曲时重定位候选点游标、清理平滑历史和归一化峰值。正偏移开始前保持基础姿态；负偏移从实际歌曲位置开始。BPM 模式静音仍可按时间运行；是否静音时暂停表现可以作为显式配置。

运行时不要调用位于 Editor 目录的 MusicAnalysisJob.IsCurrent。离线资产过期检查在编辑器/构建前执行，检查歌曲引用、源/设置指纹与算法版本；运行时只读取已验证的数据。

## 8. 参考代码应修正的问题

| 位置 | 发现 | 移植处理 |
| --- | --- | --- |
| AudioPeer.CreateAudioBands / GetAmplitude | 初始峰值为 0，静音或无能量时可能 0/0 | epsilon、有限值检查、Clamp01 |
| AudioPeer.OnUpdate 静音分支 | 只清频段，不清 Amplitude 等旧值 | 明确复位总幅值与状态 |
| AudioPeer.BandBuffer | 每帧固定减量且加速，可能减成负值，速度受帧率影响 | 改成以秒为单位的 attack/release |
| AudioPeer.MakeFrequencyBand | 按频谱索引加权，除以累计索引数量；不是标准每段平均值 | 明确求和、均值或能量公式，并按 Hz 定义频段 |
| AudioPeer.AudiopeerReset | 在 8 次循环中反复创建整套数组 | 缓存数组，只清空一次 |
| KochGenerator.Awake | `360 / pointCount` 使用整数除法，七边形步进有误差 | 改为 `360f / pointCount` |
| KochGenerator.BezierCurve | 每帧 List/ToArray 分配；步长 2 对某些点数会漏掉闭合尾段；重复条件无意义 | 固定缓存、整数采样循环、显式首尾拓扑 |
| KochGenerator.KochGenerate | 空关键点、空数组或无边时缺少有效性检查 | 校验关键点数量、有效曲线域、边数与采样数 |
| KochLine.Update | `_audioBand` 长度/索引无校验，source/material 可为空 | 校验并提供安全默认映射 |
| KochLine | `_loop`、`_generateMultiplier` 没有参与实际绘制逻辑 | 明确闭环和形变倍率语义 |
| KochLine.Update 材质 | 每帧 Debug.Log；硬写 `_EmissionColor` | 删除日志；匹配实际 Shader 属性 |
| KochLine.Start | 创建材质实例后没有释放 | 共用材质/PropertyBlock，或在销毁时释放实例 |

生成规模也要限制：初始 E 条边、K 个关键点，每代边数乘以 K−1。例如 E=60、K=4，三代为 1620 段，Bézier 再采样可能上万点。首版从 1–2 代和有限采样数开始，配置明确的最终顶点上限；不要每帧重建分形拓扑。

## 9. 建议的最小实施顺序与验收

1. 给战斗宿主提供只读音乐表现上下文：歌曲时间、播放状态、音乐源、当前 Sequence/Analysis；重播和结束时显式解绑。
2. 实现一个共享数据源，先提供三频段实时能量，以及手工 BPM/首拍配置或低频候选点脉冲。
3. 在独立示例中验证场景 LineRenderer + Sprite-Unlit-Default，先固定曲线再接能量形变。若目标为 HUD，改由 MaskableGraphic 绘制。
4. 给一个明确的图标增加 MotionRoot，验证左右旋转/横移与每拍缩放；再逐个扩展到其他图标。
5. 需要发光时追加专用 Shader/Bloom；需要静音一致性或更稳定幅值时追加离线包络。

验收重点：静音与歌曲开头无 NaN；30/60/120 FPS 摆动相位一致；暂停/恢复和补步暂停无追赶跳动；正负歌曲偏移及 Seek 对齐；歌曲结束停止输出；UI 缩放与原有布局/点击范围协调；闭环无缺口；实际曲线在 2D Renderer 下可见且渐变正确；Profiler 中曲线更新不产生持续托管分配。

本次完成了源码与配置研究，并核对了当前 URP 包的 Pass 与 Shader 颜色路径。`unity status` 没有发现装有 Pipeline 的可连接编辑器；机器上存在 Unity 2022.3 进程，因此这不代表没有打开编辑器。此次没有通过编辑器修改场景或 Prefab，也没有执行实际画面、音频延迟或性能验收。
