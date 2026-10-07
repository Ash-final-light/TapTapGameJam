# MaidGhost：Spine 3.8 导入与使用

本角色使用 `export/char_3_maid.json` 内实际存储的 Spine **3.8.99 二进制数据**，配套使用同一 `export` 目录的图集和 PNG。原始文件位于 `D:/qqFiles/char_3_maid_ghost/char_3_maid_ghost/export`，没有修改。根目录的 `.skel` 是 3.6.53，不与本次的 3.8 运行库混用。

## 使用角色

1. 角色资产已在隔离的 Unity 2022.3 项目中生成并验证，随后连同 meta 放回本项目。等待当前 Unity 完成脚本编译和资源刷新；如果没有刷新，使用 `Assets > Refresh`（Ctrl+R）。
2. 将 `Assets/ArtAsset/Spine/MaidGhost/MaidGhost.prefab` 拖进游戏场景。
3. 在 `SkeletonAnimation` 的 `Animation Name` 下拉框中选择动画。这套资源默认使用 `Standby` 待机动画，`Loop` 已开启。
4. 在 `MeshRenderer` 中设置适合当前场景的 Sorting Layer / Order in Layer。预制体初始 Order 为 10。
5. 骨骼导入 Scale 为 0.01，可在 `char_3_maid_SkeletonData` 中调整。预制体没有添加移动、碰撞或弹幕逻辑。

该预制体用于世界空间角色。Canvas 中显示角色时，应使用 `SkeletonGraphic (UI)`，并采用匹配 straight alpha 输入的 UI 材质。

## 文件结构与色彩设置

运行库为官方 `spine-unity-3.8-2021-11-10.unitypackage` 中的 `Assets/Spine`，保留原有许可证、源码和 meta；未安装官方示例场景。

官方下载页：https://esotericsoftware.com/spine-unity-download#Older-Versions

角色目录：`Assets/ArtAsset/Spine/MaidGhost/`。

| 文件 | 用途 |
| --- | --- |
| `Source/char_3_maid.skeleton.bin` | 原始 3.8.99 二进制，完整保留 |
| `Source/char_3_maid.atlas` | 原始图集布局，完整保留 |
| `Source/char_3_maid.pma.png` | 原始 gamma 空间预乘 Alpha 图集，完整保留 |
| `char_3_maid.skel.bytes` | Unity 可识别的骨骼二进制，与原始二进制内容相同 |
| `char_3_maid.atlas.txt` | Unity 可识别的图集文本，与原始布局内容相同 |
| `char_3_maid.png` | 从原始 PMA 图集生成的 straight alpha 副本，像素布局和 Alpha 不变 |
| `char_3_maid_Material.mat` | 开启 Straight Alpha Texture 的 Spine/Skeleton 材质 |
| `char_3_maid_Atlas.asset` | 图集、PNG 和材质的引用 |
| `char_3_maid_SkeletonData.asset` | 骨骼、动画和图集的引用 |
| `MaidGhost.prefab` | 已初始化的 SkeletonAnimation 角色 |

项目使用 Linear 色彩空间。导入脚本在原始 PNG 的编码 RGB 上除以 Alpha，再由 Unity 的 sRGB 纹理采样转换为 Linear；材质在采样后重新乘以 Alpha。无需重新导出 Spine，也没有改变项目的色彩空间。

图集设置：sRGB 开启、Alpha Is Transparency 开启、Clamp、Bilinear、无 Mipmap、无压缩、最大尺寸 2048。

材质使用运行库自带的 `Spine/Skeleton` 无光照 Shader。项目的 URP 2D Renderer 支持其默认的 SRPDefaultUnlit 通道；角色不受 Light2D 明暗影响。若以后需要 2D 灯光，可另外接入并适配 3.8 的 URP 2D Shader。

## 重新生成和验证

菜单：`Tools > Spine > 导入并验证 MaidGhost（3.8）`。

该菜单根据项目内 `Source` 的三个文件重新生成图集副本、配置资产、重建预制体，并验证每段动画的 21 个采样点是否生成有效网格。手动运行会重设材质、骨骼 Scale、默认动画、Sorting Order 并覆盖 `MaidGhost.prefab`；如已定制预制体，请先另存为自己的预制体。

编辑器脚本：`Assets/Scripts/Spine/Editor/MaidGhostSpineImport.cs`。自动导入仅响应 `Logs/SpineMaidGhostImport.request`，不会在每次打开 Unity 时重建预制体。

验证结果：`Logs/SpineMaidGhostImport.txt`。预览图：`Logs/SpineMaidGhostPreview.png`。

本次实际 Unity 验证通过：2022.3.62f1c1、Linear 色彩空间、从本项目复制的 UniversalRP / Renderer2D 配置、30 段动画各 21 个网格采样点。URP 下的预览已经人工检查，角色及透明边缘显示正常。批处理日志为 `Logs/SpineMaidGhostURPValidation.log`；验证过程中没有编辑本项目的游戏场景。

3.8 官方兼容表列出的 Unity 上限为 2020.3。本项目使用 2022.3.62f1c1，因此应以本次实际编译、骨骼读取和动画采样结果为依据；这些验证不替代目标平台构建、完整玩法运行以及后续角色效果的检查。

离线数据验证已成功读取 67 根骨骼、26 个 Slot、64 个图集区域、default 皮肤及 30 段动画，每段动画验证了 21 个骨骼姿态采样点。记录见 `Logs/SpineMaidGhostCoreValidation.txt`。其中 `Standby` 为待机、`Run` 为奔跑、`Jump` 为跳跃、`Hurt` 为受伤、`Die` 为死亡；其余是不同击打/长按动作。该离线检查不代表 Unity 渲染验证已完成，Unity 导入和网格验证另见前述报告。
