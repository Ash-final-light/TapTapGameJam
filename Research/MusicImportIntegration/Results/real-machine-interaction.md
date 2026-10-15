# Windows 实际交互验收：歌曲导入与对齐

日期：2026-10-12。Unity 2022.3.62f1c1；Windows x64 Development Player；Mono；Direct3D 11。分支：`codex/song-import-rhythm-research`。

本轮重新构建 Windows 播放器，使用实际窗口中的鼠标和键盘输入完成验收。通过 Computer Use 控制游戏和 Windows 原生文件窗口。验证脚本处于 `-manualUI` 模式，关闭自动流程；探针仅监听实际发生的事件与 Unity 错误，不主动调用按钮或指针回调。

## 原因与修复

在主项目当前运行实例中实际观察到：`Waveform` 只有 `RectTransform` 和 `AudioWaveformGraphic`，缺少 `CanvasRenderer`，Console 持续出现同一 `GraphicRaycaster` 异常。仅追加 `RequireComponent` 不会补齐已经创建的旧实例。

修复包含三处保障：声明组件依赖；构建对齐页面时先创建 `CanvasRenderer`，再添加波形 Graphic；`OnEnable` 在 `base.OnEnable()` 注册 UI 射线检测前补齐缺失依赖。停止旧 Play 会话、重新编译后，在主项目实际点击已有歌曲的“设置对齐”，波形正常显示，Inspector 确认组件存在，Console 错误数为 0。此检查未修改正式歌曲的名称或对齐参数。

## 实际操作覆盖

| 页面或状态 | 已实际操作 | 结果 |
| --- | --- | --- |
| 主菜单与原生导入 | 我的音乐；导入后取消原生窗口；选择中文、空格、`#`、`%` 路径的 WAV；重启后导入 MP3 | 成功，取消不添加条目 |
| 导入后对齐 | 稍后对齐/返回；从列表点击设置对齐 | 波形可见，无异常 |
| 对齐页所有按钮 | 播放、暂停、播放结束后再次播放；当前位置设为首拍；查看全曲/返回 15 秒窗口；保存对齐；返回 | 成功 |
| 对齐页其他控件 | 开启节拍器后播放；点击波形标首拍；鼠标拖动进度；输入小数 BPM 和首拍 | 状态与输入一致，无异常 |
| 未保存修改 | 返回触发确认；取消继续编辑；再次修改后确认放弃 | 取消保留草稿，放弃保留已保存值 |
| 歌曲列表所有按钮 | 点击歌曲行；修改名称并保存；试听/暂停；设置对齐；移除/取消；返回主菜单 | 成功，离开页面停止试听 |
| 跨进程重启 | 正常退出后启动同一构建，进入歌曲列表和对齐页 | 名称、137.5 BPM、0.123 秒首拍正确恢复 |
| MP3 | 实际导入 90 秒 MP3，进入对齐页并播放、暂停、返回 | 成功 |
| 移除确认 | 确认移除 MP3，然后确认移除最后一首测试 WAV | 两首→一首→空列表，页面正确刷新 |
| 空列表 | 实际点击禁用的试听、设置对齐、保存名称、移除按钮 | 不触发操作，不报错 |
| 退出 | 两轮均通过主菜单退出游戏 | 两个测试进程正常结束 |

首次运行记录 30 个控件事件，其中 25 次按钮回调；重启后记录 13 次按钮回调。另实际操作了波形点击、原生窗口和空列表禁用按钮，这些没有 Button 回调，不计入上述事件数。两轮 `interaction-errors.txt` 均为 0 字节；探针覆盖 `LogType.Error`、`Exception`、`Assert`。最终状态中 `ErrorCount=0`、`MissingRenderers=[]`，两个预览播放器均停止。

输入为 20 秒、48 kHz 单声道校准 WAV，以及 90.72 秒、48 kHz 双声道 MP3。验证播放器使用独立公司名、产品名和随机存档目录；移除操作只针对本轮测试歌曲，正式歌曲库与原始音频文件保留。

## 自动回归与证据

- 新 Windows 播放器构建成功，Unity CLI 退出码 0。
- 波形依赖/实际射线检测测试及现有菜单流程测试：8 项通过，0 失败，0 跳过。
- 构建时系统与局内流程回归：19 项通过；部分负例故意产生保存/读取失败日志，与本轮窗口交互日志分开。
- [首次实际操作事件](real-machine-events.txt)、[重启后实际操作事件](real-machine-restart-events.txt)。
- [重启后恢复的对齐状态](real-machine-restored-alignment.json)、[移除后的空列表状态](real-machine-empty-library.json)、[退出前最终状态](real-machine-final-state.json)。
- [本轮构建与自动回归摘要](real-machine-regression.txt)。

完整原始日志保留在忽略目录 `tmp/music-ui-real-machine/`、`tmp/music-ui-real-machine/restart-visible/` 和 `tmp/music-import-compile/`。覆盖结论针对以上实际执行的有效音频与页面状态；本轮不测量声卡物理延迟或节拍评分。
