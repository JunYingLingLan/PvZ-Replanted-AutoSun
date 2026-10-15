# PvZ Replanted Auto Sun

《植物大战僵尸：重植版》(Plants vs. Zombies: Replanted) 的自动收集插件：自动收走**阳光、金币、银币、钻石**，同时**完全不占用你的鼠标**。

## 快速开始

### 方式一：直接下载（不需要 git，推荐新手）

**[⬇ 点击下载 PvZReplantedAutoSun.dll](https://github.com/JunYingLingLan/PvZ-Replanted-AutoSun/releases/latest)**（约 19 KB）

把下载到的文件放进 `<游戏目录>\BepInEx\plugins\`，启动游戏即可。

### 方式二：克隆仓库

```powershell
git clone https://github.com/JunYingLingLan/PvZ-Replanted-AutoSun
```

编译好的插件就在 `release\PvZReplantedAutoSun.dll`。

> 两种方式都要求游戏已经装好 BepInEx 6 (IL2CPP)。完整步骤见下方 [安装](#安装) 章节。

## 它和常见的「自动收集」有什么不同

大多数游戏自动化工具走的是「截屏找目标 → 模拟鼠标点击」：

- 会抢走真实鼠标（`SendInput` / `SetCursorPos`），你没法同时干别的事；
- 靠图像识别猜目标，分辨率、缩放、画面遮挡都会影响它；
- 容易误点到卡片、植物或 UI。

本插件走的是另一条路：**直接调用游戏内部的收集逻辑**。

- **零鼠标输入** —— 全项目没有一处 `SendInput`、`SetCursorPos` 或光标移动；
- **零图像识别** —— 不截屏、不做模板匹配，判断依据是游戏自己的类型系统；
- **不会误点** —— 只处理 `Coin` 掉落实例，永远碰不到植物、卡片和 UI。

## 工作原理

重植版是 Unity 6000 + IL2CPP 游戏，但它的游戏逻辑使用**自研渲染系统**：掉落的阳光和金币是 `Reloaded.Gameplay.Coin` 对象（继承 `ReloadedObject`，最终继承 `Il2CppSystem.Object`），带 `Draw()` / `BeginDraw()` / `MakeParentGraphicsFrame()`，**它们不是 Unity GameObject**。

所以「遍历场景 GameObject / 派发 UI 点击事件」这类做法只能扫到界面元素和对象池里的模板，永远碰不到真正的掉落物。

本插件的实际调用链：

```
GameplayActivity  (MonoBehaviour，用 FindObjectsOfType 取得)
    └─ Board m_board
           └─ DataArray<Coin> m_coins
                  └─ 逐个判断 Coin.IsSun() 与 Coin.mType
                         └─ Board.MouseDown(x, y, 1, playerIndex)   ← 游戏鼠标点击总入口
```

也就是说：插件做的事情等价于「替你点了一下那个阳光」，用的是游戏自己的坐标（来自 `Coin.GetClickRect()`）和自己的命中判定流程。

## 收集范围

| 收集 | 判定依据 | 开关 |
|---|---|---|
| 阳光 | `Coin.IsSun()`（涵盖 `Sun` / `SmallSun` / `LargeSun` / `DoubleSun`） | `CollectSun` |
| 银币 | `CoinType.Silver` | `CollectMoney` |
| 金币 | `CoinType.Gold` | `CollectMoney` |
| 钻石 | `CoinType.Diamond` | `CollectMoney` |
| 关卡奖励（钱袋、钻石袋、巧克力、礼物） | `Award*` / `Present*` | `CollectAwards`（默认关） |

**明确不会碰**：`Trophy`、`RIPTrophy`、`VersusTrophyPlant/Zombie`（奖杯类）、`Brain`（脑子）、`Vase`、`Shovel`、`WateringCan` 等具有特殊用途的掉落。

## 环境要求

| 项目 | 要求 |
|---|---|
| 游戏 | 《植物大战僵尸：重植版》 |
| 加载器 | [BepInEx 6 (Unity IL2CPP, x64)](https://builds.bepinex.dev/projects/bepinex_be) 的 bleeding edge 构建 |
| 运行时 | 无需额外安装 .NET —— BepInEx 自带 |

已确认可用的组合：游戏版本 `1.2.1173`，Unity `6000.0.52`，IL2CPP metadata version `31`，BepInEx `6.0.0-be.788`。

## 安装

1. 把 BepInEx 6 (IL2CPP) 解压到游戏根目录，确认出现 `winhttp.dll`、`doorstop_config.ini`、`BepInEx\`；
   > 注意：`winhttp.dll` 必须和游戏 exe 在**同一层**。如果把 BepInEx 文件夹整个拖进去，会变成 `游戏目录\BepInEx-x.x.x\winhttp.dll`，注入不会生效。
2. 启动一次游戏，等待 `BepInEx\interop\` 生成完毕（首次可能要几分钟，期间看起来像卡死），然后退出；
3. 把 `release\PvZReplantedAutoSun.dll` 复制进 `BepInEx\plugins\`（仓库已含编译好的版本，无需自行编译）；
4. 启动游戏，进入关卡即可。

## 配置

配置文件位于 `BepInEx\config\com.dsh.pvzreplanted.autosun.cfg`，游戏内修改后重启生效。

| 分组 | 键 | 默认值 | 说明 |
|---|---|---|---|
| 1-总开关 | `Enabled` | `true` | 总开关 |
| | `ScanIntervalMs` | `250` | 扫描间隔（毫秒），建议 150~500 |
| | `MaxPerScan` | `3` | 每次扫描最多收集几个掉落物 |
| | `DedupeSeconds` | `4.0` | 同一掉落物的去重冷却，防止重复收集 |
| | `RunInBackground` | `true` | 失焦时游戏继续运行（Unity 默认会暂停） |
| 2-收集范围 | `CollectSun` | `true` | 自动收集阳光 |
| | `CollectMoney` | `true` | 自动收集金币 / 银币 / 钻石 |
| | `CollectAwards` | `false` | 自动收集关卡奖励类掉落物 |
| 3-收集方式 | `CollectMode` | `MouseDown` | `MouseDown` = 走游戏点击判定；`Collect` = 直接调用收集方法 |
| | `DryRun` | `false` | 只记录不执行，便于排查 |
| 4-调试 | `DumpReflection` | `false` | 导出含 sun/coin/collect 的类与方法 |
| | `DumpDirectory` | `BepInEx\AutoSun` | dump 输出目录 |
| | `Verbose` | `false` | 输出被跳过的 `CoinType` 等详细日志 |

## 从源码构建

### 方式 A：有 .NET 6 SDK

```powershell
dotnet build -c Release -p:GameDir="<游戏根目录>"
```

产物在 `bin\Release\net6.0\PvZReplantedAutoSun.dll`。

### 方式 B：没有 .NET SDK（用 Visual Studio 自带的 Razor 编译器）

只要装了 Visual Studio 2022 和 .NET 6/8 运行时即可：

```powershell
.\build.ps1 -GameDir "<游戏根目录>"
```

脚本会自动定位 `csc.exe` 与 .NET 运行时程序集，生成引用列表并编译，产物在 `release\PvZReplantedAutoSun.dll`。

> 注意：运行脚本前需要游戏已经装好 BepInEx 并生成过 `BepInEx\interop\`，因为编译需要引用其中的 `Assembly-CSharp.dll`、`UnityEngine.*.dll` 和 `Tekly.*.dll`。

## 常见问题

**日志里没有任何输出？**
说明插件没被加载。检查 `BepInEx\LogOutput.log` 是否存在；若不存在，是 BepInEx 本身没注入（见安装第 1 步的路径提醒）。

**识别不到掉落物？**
把 `Verbose` 改成 `true`，日志会打印每个被跳过的 `CoinType`。若连 `场上Coin=` 都没有，说明没拿到 `Board`，把日志发出来。

**收集动作执行了但游戏里没反应？**
把 `CollectMode` 改成 `Collect`，它会绕过点击判定，直接调用 `Coin.Collect(0, false)`。

**担心收错东西？**
`CollectAwards` 保持 `false` 即可，插件只碰阳光和三种钱币。

## 免责声明

- 本项目是**单人自用**性质的 Mod，会修改游戏进程行为，可能不符合游戏的最终用户协议，请自行判断风险；
- **请勿在联机、对战或排行榜场景中使用**；
- 仓库内不包含任何游戏本体文件或游戏资源，使用时需要你自行拥有游戏。

## License

[MIT](LICENSE)
