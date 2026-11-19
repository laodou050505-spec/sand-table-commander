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

## 字体与素材

字体均为 SIL Open Font License（许可证文件在 `ArtSource/Fonts/`）：霞鹜文楷 LXGW WenKai、马善政楷书 Ma Shan Zheng、Cinzel。
