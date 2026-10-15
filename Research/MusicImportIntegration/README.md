# Windows 歌曲导入与对齐接入验证

目标平台为 Windows，Unity 2022.3.62f1c1。功能代码位于 `Assets/Scripts/System`；本目录的构建脚本和验证脚本不属于游戏运行时代码，只复制到忽略的验证项目中执行。

## 接入范围

- 主界面右上角“我的音乐”入口，进入独立本地歌曲列表。
- Windows 原生单文件选择器，支持 MP3 / WAV，中文、空格、`#`、`%` 路径。
- 异步复制和 SHA-256 内容去重；原音频保留，歌曲库使用自己的副本。
- 导入后进入对齐页，也可直接返回、稍后对齐。
- 手动小数 BPM、首拍秒数输入、波形点击标首拍、当前位置标首拍、全曲 / 15 秒窗口、拖动试听位置、节拍器试听。
- 显式保存；放弃未保存修改前确认；取消确认可继续编辑。
- 歌曲重命名、移除、缺失副本重新导入修复；歌曲库与游戏进度独立。
- 当前限制：单文件 128 MB、单首 15 分钟、1–2 声道、采样率不高于 96 kHz；BPM 30–300。

歌曲和节拍器共用 `AudioSettings.dspTime` 与 `PlayScheduled`。播放前预留 100 ms 调度时间，暂停和拖动建立新的播放起点。试听遵循系统现有音乐和音效音量设置。尚未接入节拍评分、设备延迟校准或自定义战斗关卡。

在对齐页勾选节拍器会从当前位置开始试听。节拍器播放时，仅把当前歌曲预览降低到原音量的 30%，关闭或退出页面后恢复；全局音量设置不变。每拍有提示音和橙色拍数反馈。音效或总音量为 0 时，页面提示静音原因。Windows 输出录音与开关对比验证见 [节拍器实际输出验收](Results/metronome-audibility.md)。

## 验证方法

1. 将项目 `Assets`、`Packages`、`ProjectSettings` 复制到忽略目录 `tmp/run-validation`，保留主项目编辑器状态。
2. 将 `MusicIntegrationValidation.cs` 和 `MusicUIInteractionProbe.cs` 复制到验证项目的 `Assets`，将 `BuildIntegration.cs` 复制到 `Assets/Editor`。
3. 通过 Unity CLI 调用 `BuildIntegration.Build`，生成 Windows x64 Mono 验证播放器。构建脚本同时执行现有的 19 项系统回归检查。
4. 使用 `Research/WindowsAudioLab` 生成的 20 秒校准 WAV 和项目内 MP3 作为输入。音频文件、构建产物、原始播放器日志保留在 `tmp/`，不加入版本控制。
5. 播放器首次运行用 `-inputRoot <绝对音频目录> -resultRoot <绝对输出目录>` 执行完整测试；同一构建再次运行加 `-restart yes` 验证跨进程持久化。
6. 在验证项目运行现有 `Spotlight.Tests.SystemFlowTests` PlayMode 测试，检查原菜单、键位、暂停、歌曲选择和结算等流程。

实际 Windows 窗口交互验收使用同一播放器，启动参数为 `-manualUI yes -resultRoot <绝对输出目录> -logFile <绝对日志路径>`。此模式关闭自动流程，通过鼠标和键盘操作游戏与原生文件窗口。`MusicUIInteractionProbe` 只记录按钮、输入、播放状态和异常，不主动调用 UI 回调；每轮输出 `interaction-events.txt`、`interaction-errors.txt` 和 `state.json`。重启验收必须运行同一构建，以保留该构建生成的独立测试存档目录。具体覆盖见 [Windows 实际交互验收](Results/real-machine-interaction.md)。

`MetronomeAudioProbe.py` 用于本地 Windows 输出验收，需要验证 Python 环境安装 `numpy` 与 `soundcard`，游戏本身不依赖这两个包。先确认其他桌面音频静音；通过 `make-input <WAV路径>` 生成已知 440 Hz 测试音频，再用 `record <输出WAV路径> <秒数>` 对默认播放设备进行 WASAPI loopback 录音。实际点击节拍器后分别录制开启、关闭和退出对齐页后列表试听的声音，使用 `compare <开启.wav> <关闭.wav> <列表试听.wav> <报告.json>` 检查每拍间隔、歌曲衰减和恢复。此录音不是麦克风采集，不测量扬声器到人耳的物理延迟。

原生窗口测试仅操作验证播放器自身的“导入歌曲”窗口，检查进程 ID 后取消或填写文件名。Windows 使用的文件名编辑控件嵌套在 ComboBoxEx32 内；测试脚本按控件类型和 ID 定位，避免操作其他应用。

## 关键实现约束

- Unity 2022 Mono 的原生参数结构保持为 blittable 值类型，UTF-16 字符串和输出缓冲区显式分配 / 释放，避免 `StringBuilder` 结构字段导致的原生封送崩溃。
- 文件选择器返回前恢复进程工作目录；实测仅使用 `OFN_NOCHANGEDIR` 不能可靠保证目录不变。原生接口定义参考 [Microsoft OPENFILENAMEW](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/ns-commdlg-openfilenamew)。
- `UnityWebRequest` 成功不代表音频有效，继续检查解码、加载状态、采样数、声道和采样率；无效音频不创建歌曲条目。
- 不同文件名的相同内容保留已有名称和对齐数据；重新导入可补回缺失副本。
- JSON 以临时文件和替换方式保存，保存成功后更新内存；无效或未知版本的歌曲库保留原文件并阻止覆盖。
- 页面退出、试听暂停、拖动进度都停止旧节拍器调度；退出页面释放解码音频。
- 波形按固定数量峰值降采样，分帧生成；显示窗口内只绘制有限数量波形列和节拍线。

## 结果

最终验证摘要、主菜单和对齐截图位于 [Results](Results/)。验证播放器使用独立公司名、产品名和随机存档目录，不读取玩家正式存档。

这些测试证明导入、持久化、页面操作和共享音频时钟的代码路径可工作。声卡实际输出延迟、蓝牙设备延迟、输入到声音的物理延迟仍需后续专项测量。
