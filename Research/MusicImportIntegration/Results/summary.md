# 接入验证结果

日期：2026-10-11。平台：Windows x64。编辑器：Unity 2022.3.62f1c1。验证播放器后端：Mono。

| 验证 | 结果 | 记录 |
| --- | --- | --- |
| Windows 验证播放器构建 | 成功，Unity 退出码 0 | 原始构建日志位于忽略的 `tmp/music-import-compile/IntegrationBuildFinal.log` |
| 导入、对齐、保存、波形和页面生命周期 | 65 个流程断言全部通过，播放器退出码 0 | [windows-flow.txt](windows-flow.txt) |
| 新进程重启与持久化 | 5 个断言全部通过，播放器退出码 0 | [restart.txt](restart.txt) |
| 现有主界面 PlayMode 测试 | 6 项通过，0 失败，0 跳过 | [system-flow.txt](system-flow.txt) |
| 现有系统和歌曲/装备/结算回归 | 19 项通过 | [system-regression.txt](system-regression.txt) |
| UI 截图人工检查 | 主菜单入口、歌曲列表、对齐页面文字和波形可见，无重叠 | 下方截图 |

覆盖了 Windows 原生窗口的取消和选择、中文/空格/特殊字符路径、选文件后工作目录恢复、MP3 和 WAV、真实波形网格和指针点击标首拍、小数 BPM、拖动和暂停、节拍器试听、保存与放弃草稿、取消放弃、稍后对齐、内容去重、缺失副本修复、重命名、滚动、移除、开始新游戏保留歌曲库、离开页面停止试听并释放解码音频、损坏音频不产生条目、空 JSON 和未知版本歌曲库不被覆盖。

校准 WAV 使用已知 137.5 BPM、0.123 秒首拍。重启后页面恢复为相同参数，波形尖峰与节拍线相符。播放器使用独立随机存档目录，主项目正式玩家存档未用于测试。

这里的试听验证检查调度和播放状态，不测量扬声器/耳机实际延迟。节拍评分、玩家输入时间戳映射、声卡/蓝牙延迟校准和变速 BPM 均属于后续阶段。

## 2026-10-12：波形 CanvasRenderer 缺失修复

用户在 Editor 导入歌曲后遇到 `GraphicRaycaster.cs:323` 的 `MissingComponentException`。`AudioWaveformGraphic` 没有声明 `CanvasRenderer` 依赖，运行时创建的 `Waveform` 只有 `RectTransform`。修复前在 Windows Unity Editor PlayMode 复现了相同射线检测异常；增加 `[RequireComponent(typeof(CanvasRenderer))]` 后，新增两项波形回归及现有六项菜单流程测试全部通过。

原播放器测试直接向波形发送点击回调，未覆盖 Editor 的 `EventSystem.RaycastAll` 路径。新增测试在访问 `Graphic.canvasRenderer` 属性前检查实际组件，并覆盖加载时的空波形、绘制后的射线命中和首拍点击、页面重新启用。失败基线及通过记录见 [waveform-renderer.txt](waveform-renderer.txt)。本次未重新构建 Windows 播放器；上方播放器结果属于 2026-10-11 的接入验证。

## 2026-10-12：第二轮 Windows 实际窗口验收

本轮已重新构建 Windows x64 Mono 播放器，并通过实际鼠标和键盘操作完成两轮窗口验收。补充修复了旧实例不会自动补齐依赖的问题：创建页面时显式添加 `CanvasRenderer`，波形启用时在 UI 注册前检查并补齐依赖。主项目 Editor 重新编译后，实际点击已有歌曲的“设置对齐”成功，波形组件完整，Console 错误数为 0。

实际覆盖歌曲列表与对齐页全部按钮、波形点击、进度拖动、BPM/首拍输入、节拍器、确认/取消、MP3/WAV 导入、正常退出后恢复参数，以及移除到空列表后的禁用按钮。两轮记录共 43 个控件事件（38 次按钮回调），两份错误日志均为 0 字节；本轮 8 项 PlayMode 测试和 19 项系统回归通过。详情和证据见 [Windows 实际交互验收](real-machine-interaction.md)。上面的第一轮修复说明和 2026-10-11 结果保留作为历史记录。

## 2026-10-12：节拍器听感与实际音频输出

增强节拍提示音、为当前歌曲试听加入可恢复的音量衰减、勾选后从当前位置播放，并增加每拍橙色文字反馈。重新构建 Windows 播放器，通过实际 UI 操作与 WASAPI 播放设备录音验证：120 BPM 下 5 秒检测到 10 拍，间隔 0.5 秒；MP3 的 155 BPM 下检测到 13 拍，间隔约 0.39 秒。关闭和离开对齐页后节拍声消失、歌曲音量恢复；10 项 PlayMode 测试及 19 项系统回归通过，实际交互错误日志为 0 字节。记录见 [节拍器实际输出验收](metronome-audibility.md)。

![主菜单右上角入口](main-menu.png)

![独立歌曲列表](music-library.png)

![重启后恢复 BPM 和首拍的对齐页面](alignment.png)
