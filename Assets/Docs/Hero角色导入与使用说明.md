# Hero 角色导入与使用

角色来自 `char_hero.zip`，已独立导入为 Spine 3.8 资源，并接入普通战斗和自定义歌曲战斗。两个战斗入口使用独立的 `Assets/Player/Hero.asset`；原 MaidGhost 角色和动作库保留。

## 战斗角色与动作

- 玩家配置：`Assets/Player/Hero.asset`。
- 动作库：`Assets/Player/HeroActions.asset`，13 个片段位于 `Assets/Player/HeroActions/`。
- 角色头像：`Assets/Player/HeroPortrait.png`，由 Hero 待机姿态渲染。
- 场景入口：`GameplayPlaceholder.unity`、`CustomSongBattle.unity` 的 BattleManager。
- 动作编辑器和新建战斗的默认角色均为 Hero。

| 动作槽 | Hero 动画 | 播放规则 |
| --- | --- | --- |
| Idle / Move | `player_idle` / `player_run` | 分别以 160 / 48 Tick 循环 |
| Jump | `player_jump_up` | 适配原 28 Tick 起跳动作；保留跳跃冲量 |
| Fall | `player_jump_fall` | 40 Tick 循环 |
| FlyStart | `player_jump_up` | 6 Tick 短片段 |
| FlyLoop | `player_idle` | 160 Tick 循环，作为悬浮表现 |
| FlyEnd | `player_jump_fall` | 6 Tick 短片段，然后进入下落 |
| Land | `player_idle` | 保留 6 Tick 过渡，然后回到待机或移动 |
| Dash | `player_dash` | 适配原 24 Tick；位移、无敌和接续窗口保持原配置 |
| Skill1 / Skill2 | `palyer_counter` | 2 倍速播放，保留 30 Tick 动作、第 6 Tick 释放和 60 Tick 冷却 |
| Hurt | `player_hurt_flash` | 适配原 32 Tick 受伤动作 |
| Dead | `player_dead` | 60 Tick，末帧保持 |

每秒 60 Tick。两个技能允许正常左右移动，随输入立即转身；转身不重启动画。技能期间保留 Hero 原色，HUD 技能颜色为绿、黄。

技能一在释放时播放 `Assets/VFX/Parry/绿色弹反.prefab`，技能二播放黄色版本。序列帧以 15 FPS 播放一次，约 0.33 秒；跟随玩家位置和朝向，使用战斗坐标偏移和预制体原始缩放。技能被中断时清理特效，暂停时冻结序列帧。

这里仅复用反击动画和弹反命名的视觉素材，不添加弹反窗口、弹幕反射或额外无敌。原技能投射物和普通射击规则保留；`player_shoot` 尚未作为普通射击叠加动画接入。

菜单 `Tools > 角色 > 接入 Hero 到现有战斗` 可在配置缺失时生成独立 Hero 配置，并重新绑定两个入口。已有 Hero 配置不会被该菜单重建；有未保存修改的目标场景会阻止接入。

接入在隔离副本中使用 Unity 2022.3.62f1c1 验证：13 个动作绑定、两个场景引用、技能第 6 Tick 单次释放、移动和即时转身、受伤中断清理、30 Tick 技能结束、6 Tick 落地、飞行切换均通过；真实 Play 模式的序列帧播放、暂停/恢复、镜像和自动销毁通过。包含 5 个启用场景的 Windows 构建成功，0 错误、0 警告。

记录位于 `Logs/HeroIntegrationVerify.txt`、`Logs/HeroIntegrationPlay.txt`、`Logs/HeroIntegrationBuild.txt`；技能画面为 `Logs/HeroSkills.png` 和 `Logs/HeroSkillsTurned.png`。

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

预制体只提供视觉表现；运动、碰撞、射击和技能触发由游戏系统驱动。Hero 动作库使用新角色自己的 AnimationReferenceAsset，不沿用旧角色的 Standby、Run、Jump 等动画名。

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
