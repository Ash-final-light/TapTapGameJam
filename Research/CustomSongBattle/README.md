# 共鸣舞台：实施与复测

实施日期：2026-10-15。Unity 2022.3.62f1c1；Windows x64；生成器版本 2。

## 玩家入口

从 `Assets/Scenes/Title.unity` 启动，进入“我的音乐”，导入 MP3/WAV，保存 BPM 与首拍，再选择“进入挑战”。在准备页点击“分析并生成弹幕”，完成后开始战斗。倒数 3 秒后播放歌曲；存活到曲终完成，角色倒下则提前结算。结算支持相同谱面重试和返回歌曲库。

本次正常游戏构建在项目根目录 `Builds/Windows/TapTap_GameJam.exe`。运行、拷贝时须保留整个 Windows 文件夹。该文件夹被 Git 忽略。正常入口采用原来的 Title 场景和游戏存档目录。

## 接入与首版规则

| 部分 | 位置与行为 |
| --- | --- |
| 专用场景 | `Assets/Scenes/CustomSongBattle.unity`；已加入 Build Settings，关卡入口 ID 为 `custom-song` |
| 模板 | `SystemConfig.CustomSongTemplate` 指向现有战斗配置；运行时克隆，避免改写正式歌曲资源 |
| 导入与身份 | 沿用现有音乐库、内容 SHA-256 ID、保存的 BPM 与首拍；缺失音频或未对齐时不能挑战 |
| 分析 | `CustomSongChallengeService` 在主线程读取有限块 PCM，在工作线程执行现有 `MusicSpectrumAnalyzer`；分析可取消，生成令牌阻止旧任务覆盖新任务 |
| 缓存 | 音乐库下 `AnalysisCache/<内容ID>-fft-v1.json`；校验格式及采样信息，损坏时重新分析；缓存写入失败不阻止挑战 |
| 生成 | `CustomSongSequenceGenerator` 输出现有 `DanmakuSequence`，无需新增弹幕执行器；内容 ID、BPM、首拍和版本一致时基础谱面一致；瞄准攻击仍随玩家位置变化 |
| 三频攻击 | 低频“重音扩散”圆环；中频“旋律追射”瞄准扇形；高频“晶音弹群”及可击破“晶音能量球” |
| 预警与节奏 | 主攻击间隔至少 `max(2 拍, 0.75 秒)`，提前 `max(1 拍, 0.5 秒)` 预警；每 6 个四拍段有 1 段恢复空档，另有开场和尾段保护 |
| 密度 | 敌弹保守预算 120，能量球分裂子弹计入预算；弱于全局峰值 3% 的候选剔除；权重归一化保留下限，避免噪声泄漏放大 |
| 表现 | 复用场景背景与音乐频谱，增加歌曲进度、倒数、文字预警及圆环/三角/菱形三种标记 |
| 角色与武器 | 沿用现有角色和场地，首版固定已解锁普通武器、Normal，不携带道具 |
| 成绩 | 复用现有 Boss 命中和按开火 Tick 判断的节拍奖励；评级显示“完成/未完成”，自定义歌曲不发放道具 |
| 记录隔离 | `custom:v2:<内容ID>:<BPM R>:<首拍 R>`；更改对齐或生成版本会使用另一条成绩，不覆盖原参数成绩 |
| 生命周期 | 会话持有解码音频、分析、序列、战斗与歌曲临时对象；重试复用会话，返回歌曲库在场景卸载后释放 |

首版保留手动 BPM 和首拍，不包含自动 BPM、变速曲线、曲式识别、多难度及装备排行规则。原策划中的后续扩展尚未全部实施；本说明描述实际可玩的范围。

## 编辑器接入与校验

当前资源已经接入，一般无需重新安装。需要恢复接线时，保存当前场景后执行菜单“工具 / 共鸣舞台 / 接入自定义歌曲战斗”。该工具通过 Unity Editor API 创建或更新场景、模板引用、入口与构建列表，并恢复之前打开的已保存场景。

“工具 / 共鸣舞台 / 验证生成与结算”运行 14 项编排、预算和存档规则校验，输出 `Logs/CustomSongBattleValidation.txt`。Test Runner 的 PlayMode 全套包含 19 项测试，其中新增 3 项自定义歌曲流程测试、1 项窗口尺寸回归测试；测试使用独立存档目录。也支持在非播放状态向 `Temp/CustomSongTests.request` 写入 `all`，由编辑器桥接运行并保存报告。

本机批量测试使用 Unity CLI 启动独立项目副本，避免关闭用户已经打开的编辑器。示例在项目根目录的 PowerShell 执行：

```powershell
$unityCli = 'C:/Users/light/AppData/Local/Unity/bin/unity.exe'
$validationProject = 'D:/UnityProject/TapTap_GameJam/tmp/custom-song-validation'
# 先在 tmp 下新建副本，仅复制 Assets、Packages、ProjectSettings。
# 切勿将任何验证脚本复制进正式 Assets，也不要把 Library 或测试存档提交到 Git。
& $unityCli test $validationProject --editor-version 2022.3.62f1c1 --mode PlayMode --output 'D:/UnityProject/TapTap_GameJam/Logs/CustomSongFinalPlayMode.xml' --timeout 300
```

## 独立程序复测工具

这里的 `.cs` 工具位于 Assets 外，只在隔离项目内编译，正式项目不会包含它们。

1. 新建隔离副本并导入成功后，将 `CustomSongPlayerBuild.cs` 复制到副本的 `Assets/Editor/`，将 `CustomSongPlayerProbe.cs` 复制到副本的 `Assets/`。工具依赖本项目运行时代码；不要放入其他项目。
2. Python 环境安装 `numpy`、`soundcard`。运行 `python Research/CustomSongBattle/MakeInputs.py tmp/custom-song-inputs` 生成原创测试信号。可把一首已有合法 MP3 复制到输入的 `auto/real_music.mp3`；报告中的 MP3 来自本项目已有素材，不在证据中重复分发。
3. 使用 Unity CLI `run <副本路径> --editor-version 2022.3.62f1c1 --timeout 600 -- -executeMethod CustomSongPlayerBuild.Build`。构建器执行自定义及原有校验，创建专用验证入口，并生成 `tmp/custom-song-player/CustomSongValidation.exe`。验证存档属于 `TapTapResearch/CustomSongValidation/CustomSongValidation`，与正式游戏隔离。重复构建前先清理副本中的 `Assets/CustomSongValidationConfig.asset` 和验证入口场景，或使用新的副本，避免 CreateAsset 同名失败。
4. 手动模式必须传入结果目录：`CustomSongValidation.exe -resultRoot <绝对结果路径> -logFile <绝对日志路径>`。此模式只监听 UI 按钮、状态及错误，所有操作由操作系统输入完成。验证程序不能不带参数双击启动。
5. 自动模式额外传入 `-auto -inputRoot <auto 输入目录绝对路径>`。自动模式会调用游戏流程接口，并通过 Input System 虚拟鼠标持续开火，输出 `checks.txt`、`events.txt`、`errors.txt`、`state.json` 和 `metrics.json`。自动结果不能代替原生文件选择器和真实鼠标操作验收。
6. 在手动播放、暂停和恢复状态分别运行 `python Research/CustomSongBattle/CheckOutput.py <结果.json> 3`。仅采集默认播放设备的 WASAPI 回环，计算已知 421 Hz 参考音、RMS 和峰值，不使用麦克风，不保存原始音频。运行期间避免其他程序播放声音。

生成正常游戏包时，在隔离副本中把 `CustomSongPlayerProbe.cs` 及其 `.meta` 移到 Assets 外，再执行 `CustomSongPlayerBuild.BuildGame`。它使用正式 Build Settings 中的 Title 入口和真实游戏配置，输出根目录 `Builds/Windows`；没有附加探针或验证入口场景。验证工具使用开发构建，发布前仍可按项目需求配置发布平台和发布构建选项。

本轮结果见 [验收报告](Results/summary.md)。大体积构建、输入音频、完整运行日志保留在本机忽略目录，仅小型证据及可复现工具进入 Git。
