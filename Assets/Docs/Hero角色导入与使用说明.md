# Hero 角色导入与使用

角色来自 `char_hero.zip`，已独立导入为 Spine 3.8 资源。当前战斗仍使用原有玩家配置；本次不修改 MaidGhost、玩家动作库或战斗入口的角色引用。

## 在 Unity 中使用

将 `Assets/ArtAsset/Spine/Hero/Hero.prefab` 拖入场景即可预览角色。默认播放 `player_idle` 并循环；可在 SkeletonAnimation 的 Animation Name 中选择其他动画。预制体使用 Spine/Skeleton 无光照材质，Sorting Order 为 20。

SkeletonData 的导入 Scale 为 0.01。预制体根节点另按待机初始帧缩放至约 2.2 个世界单位高，方便放入现有场景查看；可以按使用场景调整根节点 Scale。

`Assets/ArtAsset/Spine/Hero/Animations/` 提供全部 9 个 AnimationReferenceAsset，便于后续配置动作系统。`palyer_counter` 保留了原始拼写，不能在引用时写成 `player_counter`。

| 原始动画名 | 用途（按素材命名） |
| --- | --- |
| `player_idle` | 待机 |
| `player_run` | 跑步 |
| `player_jump_up` | 起跳 |
| `player_jump_fall` | 下落 |
| `player_dash` | 冲刺 |
| `player_hurt_flash` | 受伤闪烁 |
| `player_dead` | 死亡 |
| `player_shoot` | 射击 |
| `palyer_counter` | 反击/弹反 |

预制体只提供视觉表现；运动、碰撞、射击、弹反和技能触发应由游戏系统驱动。接入玩家系统时需要为新角色配置动作映射，不能沿用旧角色的 Standby、Run、Jump 等动画名。

## 原始素材与运行时副本

完整 40 个交付源文件保存在项目根目录的 `ArtSource/Spine/Hero/`，包含 `char_hero.spine`、`images/` 和 `export/`。文件名、相对路径与内容保持交付时的状态；本目录使用局部 .gitattributes 禁止换行转换。编辑源文件不放入 Unity 的 Assets，以避免重复导入部件图及误识别骨骼格式。

`export/char_hero.json` 实际存储 Spine 3.8.99 二进制骨骼数据，因此运行时目录使用内容相同的 `char_hero.skel.bytes`。`char_hero.atlas.txt` 同样是原 atlas 的副本，图集页面名保持 `char_hero.png`。

源图集为预乘 Alpha（PMA）：半透明边缘像素没有 RGB 分量高于 Alpha。本项目使用 Linear 色彩空间，导入工具在原始编码 RGB 上去预乘，生成独立的 straight-alpha 纹理；Spine 材质启用 Straight Alpha Input，在采样后重新乘以 Alpha。原始 PMA 图集继续完整保留在 ArtSource，不被改写。

## 重新生成

菜单：`Tools > Spine > 导入 Hero（3.8）`。

该操作从项目内 ArtSource 重建运行时纹理、材质、骨骼数据、9 个动画引用与 Hero.prefab，会重设这些生成资源。需要定制角色预制体时，先另存为自己的预制体。

导入代码：`Assets/Scripts/Spine/Editor/HeroSpineImport.cs`。仅在显式调用菜单或 Run 方法时执行，没有打开项目即覆盖预制体的自动行为。

本次在 Unity 2022.3.62f1c1、Linear 色彩空间和现有 URP 配置下完成读取与渲染：9 个动画引用有效，每段动画检查 21 个网格采样点，合计 189 个；5 个场景和 14 个预制体的引用检查通过。

Unity 导入及动画网格检查记录：`Logs/HeroImport.txt`。预览图：`Logs/HeroPreview.png`；9 段动画的代表帧总览：`Logs/HeroAnimations.png`。临时验证脚本保存在忽略的 tmp 目录，不属于实际 Assets。
