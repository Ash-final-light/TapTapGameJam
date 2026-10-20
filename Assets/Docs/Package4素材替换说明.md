# Package4 素材替换

素材来自 `D:/qqFiles/Extracted_20261020/package4/`。原始 20 张 PNG 已按原名复制到 `Assets/ArtAsset/package4/`，包含 10 张 UI/图标与两组各 5 帧的弹反特效；交付原件的内容、扩展名大小写、中文和空格均保留。参考 PNG、GIF 与 macOS 元数据不导入运行时目录。

## 已接入的界面

- `SpotlightUI.prefab` 与独立 `SongSelect.prefab`：弧形歌曲列表、歌曲封面框、歌曲信息条、最高成绩框、评级与分数字段、三个难度的基础/选中状态、圆形确认按钮。
- `SpotlightUI.prefab` 与独立 `MainMenu.prefab`、`Loading.prefab`：VARIATION Logo，保留“聚光灯”的文本标题。
- `SpotlightUI.prefab` 与独立 `HUD.prefab`：技能栏的键盘图标，仅调整 UI 显示。
- 选曲页内容根据可用宽高缩放，返回按钮和页标题锚定顶部；图片和文字仍是独立对象，歌曲列表、难度、历史成绩和确认流程由原有系统驱动。

UI 使用完整纹理加 `RawImage.uvRect` 显示所需区域；难度按钮的 `SlantedArtwork` 使用斜边网格，排除同张图上相邻按钮的残影。没有裁改原 PNG，也没有手工编辑 Sprite 元数据。新素材通过 `Package4Artwork` 标记避免被通用按钮主题覆盖。

## 弹反特效

- `Assets/VFX/Parry/绿色弹反.prefab`
- `Assets/VFX/Parry/黄色弹反.prefab`

两者均采用原始数字顺序 1→5、相同的完整画布与默认中心 Pivot，以 15 FPS 单次播放并自动销毁，约 0.33 秒。`SpriteSequenceEffect` 使用游戏时间，暂停时停止推进。

这两个是可直接实例化的视觉特效；没有添加弹反判定、弹幕反射或角色动作触发。后续在实际成功判定事件的位置实例化相应预制体即可，再按玩法反馈调整帧率与缩放。

## 角色范围

本次没有导入 `char_hero`，没有替换玩家的骨骼、材质、预制体、动作库或运行时角色脚本。修改前记录的 `Assets/Player`、`Assets/ArtAsset/Spine`、`Assets/Scripts/Player` 共 94 个文件用于完成后的 SHA-256 核对。

## 验证与预览

临时导入、预览与检查脚本仅保存在忽略的 `tmp/ReleaseAuditProject/` 中，未加入实际项目的 Editor/测试目录。

- `Logs/Package4Apply.txt`：导入、难度状态、列表导航及引用检查。
- `Logs/Package4Smoke.txt`：Play 模式初始化、选曲轮换、难度回调、确认进入配装、设置开关、特效播放与销毁检查。
- `Logs/Package4Build.txt`：Windows 发布构建结果。
- `Logs/Package4Songs.png`、`Logs/Package4Menu.png`、`Logs/Package4HUD.png`、`Logs/Package4Parry.png`：Unity 渲染预览。
- `Logs/Package4ClassicSongs.png`、`Logs/Package4WideSongs.png`：4:3 与超宽屏预览。
