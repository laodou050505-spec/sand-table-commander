# 将军沙盘 Sand Table Commander

PICO 头显上的混合现实（MR）沙盘指挥游戏：一张实木沙盘摆在你面前的真实桌面上，伸手捏住令牌拖过去，指挥步兵、弓兵、骑兵把对面的军队打崩。中英双语。

五种兵种风格可在封面“时代”里切换，玩法相同，兵种、武器、坐骑、旗帜、场景和地面各是一套：
**剑盾**（中古）、**铁誓**（哥特）、**硝烟**（现代）、**星尘**（未来）、**深空**（重型机甲）。

- 引擎：Unity 6000.5.5f1 + URP，PICO Unity Integration SDK 6.0.0（手势追踪 + 眼动追踪 + 视频透视）
- 包名：`com.toybox.wartable`
- 安装包：见 [Releases](../../releases)

## 怎么玩

全程只用手，不用手柄、也不靠眼睛点选：伸手捏住令牌，拖过去就行。

| 操作 | 做法 |
|---|---|
| 摆沙盘 | 手掌平放在桌上停一秒，沙盘摆在你正前方 |
| 挪 / 缩放沙盘 | 双手捏住木框前沿的两个圆扣：一起拖 = 挪位置，两手拉开 / 靠拢 = 放大 / 缩小 |
| 移动部队 | 伸手捏住蓝色令牌，拖到别处松手，部队走过去（手伸远了，落点会自动往前延伸） |
| 进攻 | 把令牌拖到红色敌军令牌上松手 |
| 预定进攻 | 布阵时把令牌拖到敌军令牌上，开战那一刻这支部队就直接去打它 |
| 开战 | 捏住身边的蓝色帅旗停一下，金圈转满开战 |
| 冲锋 / 坚守 / 撤退 | 沙盘前沿靠着三支令牌：捏起一支，扔到沙盘上任意位置 = 对全军下令 |
| 暂停 | 手指点一下沙盘左前角的印章 |

指尖上的小圈显示捏合状态：张开是淡圈，捏住是金点，够着令牌时变金色。
第一次玩会有操作提示，打完一局后不再出现；封面菜单里的“教学”可以重看。
沙盘摆歪了或换了桌子：封面“设置”里点“重新定位”，再用手掌按一次桌面。

## 安装

1. 头显打开开发者模式，用 USB 连接电脑。
2. `adb install -r -g SandTableCommander_<版本>.apk`
3. 首次安装后在头显里允许“手势追踪”和“眼动追踪”权限（或 `adb shell pm grant com.toybox.wartable com.picovr.permission.HAND_TRACKING` / `...EYE_TRACKING`）。

## 工程结构

| 路径 | 内容 |
|---|---|
| `Assets/_WarTable/Runtime/Sim` | 战斗模拟（士兵、武器、士气、AI），从 Three.js 原型逐行移植 |
| `Assets/_WarTable/Runtime/Command` | 指挥层：拖拽令牌、军令、预定进攻 |
| `Assets/_WarTable/Runtime/XR` | 手部输入（捏合、手势识别、手部遮挡、托盘） |
| `Assets/_WarTable/Runtime/Game` | 游戏流程、沙盘界面、教学、结算、特效、声音 |
| `Assets/_WarTable/Editor` | 搭建沙盘 / 封面 / 玩法装配、打包、平衡测试、手势测试 |
| `Tools/` | 生成界面贴图、处理模型的 Python 脚本 |
| `ArtSource/` | 打包所需的界面原图和开源字体（其余美术原始素材未入库） |
| `Docs/` | 方案与美术规范、截图 |

打包：在 Unity 里执行菜单 `WarTable/打包 APK（PICO）`（先切到 Android 平台），产物在 `Builds/`。

测试：`WarTable.EditorTools.GestureTest.Run()`（手势识别正反例）、`WarTable.EditorTools.BalanceTest.Run(true, 20)`（五种打法各 20 局的胜率）。

## 协作（多人一起改这个工程）

### 第一次拉下来

1. 装 **Unity 6000.5.5f1**，勾选 Android Build Support（含 OpenJDK、Android SDK & NDK）。
2. `git clone https://github.com/laodou050505-spec/sand-table-commander.git`
3. **PICO SDK 不在仓库里**（PICO 的许可证不允许公开分发）。拿到 PICO Unity Integration SDK **6.0.0**（PICO 开发者平台下载，或找组里要压缩包），把里面的 `XR` 文件夹放到工程根目录下：

   ```
   sand-table-commander/
     Assets/  Packages/  ProjectSettings/ ...
     PICO-SDK/
       XR/            ← SDK 里的 XR 文件夹，package.json 在这一层
   ```

   `Packages/manifest.json` 按相对路径 `file:../PICO-SDK/XR` 找它，放错位置 Unity 会报找不到 `com.bytedance.pico.xr`。
4. 用 Unity Hub 打开工程（第一次导入要十几分钟），打开主场景 `Assets/_WarTable/Scenes/Board.unity`。
5. 菜单 `WarTable/切到 Android 平台` → `WarTable/打包 APK（PICO）`，安装包在 `Builds/`，装机方法见上面“安装”。

### 没有头显时在电脑上看

进入运行模式后 Game 窗口默认就是**自由视角**（视野 80°，接近头显里看到的范围，开场对准封面），左下角有操作提示。先点一下 Game 窗口，按键才会生效。

| 操作 | 做法 |
|---|---|
| 转视角 | 按住鼠标右键拖动 |
| 飞行 | 按住右键的同时 W / A / S / D 移动，Q 下降、E 上升，Shift 加速，滚轮调速度 |
| 前后推拉 | 不按右键时滚轮 |
| 平移 | 按住鼠标中键拖动 |
| 回到起始视角 | Backspace |
| 切到座位视角（和头显同一个位置） | Tab（再按一次切回自由视角） |
| 点按钮、拖令牌 | 左键照常；右键“点一下不拖” = 手指戳兵 |

看到哪里就能点到哪里：游戏里所有“鼠标 → 射线”的地方都走 `FlyCam.View()`，用正在显示的那台相机。没按右键时键盘快捷键照常（W/S 等下令键只在按住右键飞行时让给相机）。

这台相机只在编辑器里存在（`Runtime/Game/EditorFlyCam.cs` 的相机部分整个包在 `#if UNITY_EDITOR` 里），打进安装包的代码里没有它，头显视角不受影响；它是另开的一台相机，叠在座位相机上面，座位相机和跟着它走的封面都不动。

### 改动怎么交

- **main 只保留最新版本**：每次发布会把 main 整体覆盖成一个提交（旧历史不留）。所以别直接在 main 上提交：开自己的分支（如 `feat/titan-anim`），推上去开 Pull Request，合并后再发布。
- main 被覆盖后，`git pull` 会报历史不一致：`git fetch` 后用 `git reset --hard origin/main` 同步（先确认自己的改动都已经在 PR 里或另存了）；还没合并的分支用 `git rebase --onto origin/main <覆盖前的 main> <你的分支>` 接到新 main 上。
- **主场景是“生成”出来的**：打包菜单每次都会重建封面、重新装配玩法、重放标题机甲（`CoverBuilder.Build` → `GameSetup.Setup` → `TitanBuilder.Add`），所以场景文件每次打包都会变。两个人的场景文件冲突了，直接任选一边，再执行一次打包（或对应的搭建菜单）重新生成就行，别手工合并场景的 YAML。想要永久保留的改动写进这些搭建脚本，而不是只在场景里手动改。
- 大文件：GitHub 单个文件上限 100 MB；美术原始素材（`ArtSource/` 大部分）、`Builds/`、`Library/`、`PICO-SDK/` 都不入库。
- 安装包签名：工程用的是每台电脑自己的调试签名。别人打的包要覆盖你装过的版本时，会报 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`，先 `adb uninstall com.toybox.wartable` 再装（会清掉头显里的存档）。

## 字体与素材

字体均为 SIL Open Font License（许可证文件在 `ArtSource/Fonts/`）：霞鹜文楷 LXGW WenKai、马善政楷书 Ma Shan Zheng、Cinzel。
