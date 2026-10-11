# Windows 本地音频导入与对齐实验

这是独立研究程序的可复现源码，位于主项目 `Assets/` 之外，不参与游戏编译。仅用来测量和验证导入／对齐方案，不是已接入主界面的功能。

## 实验条件

- Windows x64，Unity 2022.3.62f1c1，Development Player，Mono 后端。
- 通过脚本路径导入；本轮不操作 Windows 系统文件选择器。
- 生成 20 秒、120／137.5 BPM、首拍 0.25／0.123 秒的 44.1／48 kHz WAV。
- 复制主项目 `Assets/AudioAsset` 里的 MP3 作为解码样本，不提交或发布这些音频。
- 样本目录包含中文、空格、`#` 和 `%`；使用文件 URI 转义。
- 播放音量为零；测量 DSP 时钟和采样游标，不测量可听声音的端到端延迟。
- 输入到达延迟实验使用合成事件时间，不能代表真实键盘／Input System 延迟。

## 复现

在项目根目录执行 PowerShell。实验需要本机已有 Unity Editor 和 Windows 构建模块；根据实际安装位置更改 editor-path。

```powershell
& ./Research/WindowsAudioLab/Prepare.ps1
unity build 'D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab' --target StandaloneWindows64 --execute-method BuildLab.Build --output-path 'D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab/player/WindowsAudioLab.exe' --editor-path 'D:/unityEditor/2022.3.62f1c1/Editor/Unity.exe' --allow-dirty-build --no-tail --no-provenance --timeout 600 --format json --log-file 'D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab/build.log'
Start-Process -FilePath 'D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab/player/WindowsAudioLab.exe' -ArgumentList @('--lab-root', '"D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab"', '-logFile', '"D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab/player.log"', '-screen-fullscreen', '0') -WindowStyle Hidden -Wait
Start-Process -FilePath 'D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab/player/WindowsAudioLab.exe' -ArgumentList @('--lab-root', '"D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab"', '--buffer-frames', '256', '-logFile', '"D:/UnityProject/TapTap_GameJam/tmp/windows-audio-lab/player-buffer-256.log"', '-screen-fullscreen', '0') -WindowStyle Hidden -Wait
Get-Content 'tmp/windows-audio-lab/results/report.json'
& ./Research/WindowsAudioLab/Summarize.ps1
```

## 结果路径与解释

- `tmp/windows-audio-lab/results/report.json`：逐项检查、解码信息与耗时、运行时音频配置、时间测量。
- `tmp/windows-audio-lab/results/report-buffer-256.json`：只在实验进程中请求 256 帧 DSP 缓冲后的对照。以 actual `bufferFrames` 为准，不假定请求必然生效。
- `tmp/windows-audio-lab/results/clock-*.csv`：逐帧实时钟、DSP 时间、播放游标、启动基准和有效测量标记。
- `tmp/windows-audio-lab/results/library/catalog.json`：待对齐与已设置的元数据样例。
- `tmp/windows-audio-lab/results/library/`：内容哈希命名的导入副本。
- `tmp/windows-audio-lab/build.log`、`player.log`：构建与运行日志。

`meanSignedMs / p95AbsoluteMs / maxAbsoluteMs` 是 DSP 预测歌曲位置减去 `AudioSource.timeSamples / frequency` 的差异；不是输入到扬声器延迟。`requestedFps` 是目标值，`observedFps` 是本次实测 Update 频率。

`realtimeDspOffsetRangeMs` 是一段内实时钟与 DSP 时钟差值的范围；`realtimeDspElapsedP95Ms` 是用首个有效采样的固定偏移进行换算后的相对误差 P95。它们反映时钟桥接风险，不是人耳听到的音乐延迟。`loadMs` 只表示请求完成时间，`readyMs` 表示等待 Loaded 的累计时间；实际 PCM 可读取另用 `pcmReadable` 验证。

完整结果以程序生成的 JSON 和研究文档为准。PASS 不代表系统文件选择、主界面交互、IL2CPP、物理输入或听感校准已经验证。

本次实验固化的两组结果与逐帧数据见 [Results](Results/)。初轮损坏文件误判的失败记录为 `initial-corrupt-file-failure.json`；补上文件头与有效 AudioClip 校验后，默认 51 项检查、256 帧对照 52 项检查通过。`preliminary-time-probe.json` 保留补充时钟测量时的前一次短测。固化结果仅是本机快照，重新执行会在 tmp 下生成新的测量。

所有实验产物在被忽略的 `tmp/` 下，源码单独保留以便复现。不得将整个实验项目或本地 MP3 样本加入提交。
