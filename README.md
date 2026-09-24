# 西游：第八十二难 · samsara-west

单机回合制 RPG 的**工程底座**。仓库当前不含可玩内容，验收标准是四条硬指标：
**能编译、能出包、测试全绿、重复数据 ID 会报错**。

- 引擎：Unity `2022.3.62f3c1`（LTS）+ URP 2D + Pixel Perfect；参考分辨率 1920×1080（= 480×270 × 4），Linear 色彩空间
- 输入：Input System；界面：uGUI + TextMeshPro
- 数据：CSV（策划输入面）→ ScriptableObject（程序产物），按文件哈希增量导入
- 脚本：C#（Unity 自带 Roslyn），**不需要 dotnet SDK**；工具脚本一律 Windows PowerShell 5.1

## 快速开始

```powershell
# 1. 挂载外部素材 + 初始化工程（新机器、新克隆跑一次；幂等，可反复执行）
powershell -NoProfile -ExecutionPolicy Bypass -File E:\tx2\samsara-west\Tools\setup-project.ps1

# 2. 跑全套测试（当前基线：EditMode 270 + PlayMode 10）
powershell -NoProfile -ExecutionPolicy Bypass -File E:\tx2\samsara-west\Tools\run-tests.ps1 -Platform All
```

`setup-project.ps1` 做三件事：先调 `setup-external-assets.ps1` 把素材挂进来（`-SkipExternalAssets` 可跳过，
`-ArtFolder` 可换素材位置），再以 `-executeMethod SamsaraWest.Editor.ProjectSetup.RunAll` 跑一次菜单
`SamsaraWest/工程/一键初始化骨架`，最后核验四个产物确实落地（`DefinitionCatalog`、`BattleConfig_Default`、
`LocalizationTable_zh-Hans`、`Bootstrap.unity`）——批处理编辑器早退时也会返回干净退出码，所以只认磁盘上的产物。
退出码：`0` 初始化完成；`2` 挂载失败或数据/本地化校验有错（按提示修表后重跑）；`1` 编译错误、崩溃或超时。
冷 `Library` 首次导入要几分钟，期间脚本按日志体量报告进度，`-TimeoutSeconds` 默认 900。

只缺素材、不想动工程时单独跑 `Tools\setup-external-assets.ps1`（见下节）。

## 目录结构

模块边界由 asmdef 约束成**编译期错误**，不是文档里的君子协定。

| 目录 | 职责 | asmdef 依赖 |
|---|---|---|
| `Core/` | 服务定位、事件总线、时间、可复现随机、对象池、日志 | InputSystem |
| `Data/` | ID 规则、定义基类、CSV 解析、导入映射、校验 | Core |
| `Flow/` | 启动装配、场景与章节状态 | Core, Data, Localization, Save, Battle, InputSystem |
| `Battle/` | 伤害计算、战斗数值配置 | Core, Data |
| `Save/` | 版本化 JSON 存档 + 迁移钩子 | Core |
| `Localization/` | 文本键服务、表资产、常量生成 | Core |
| `UI/` | 本地化标签、运行期错误面板 | Core, Data, Localization, TextMeshPro |
| `Exploration/` `Narrative/` `Progression/` `Economy/` | 骨架占位（目录 + asmdef + 接口契约） | Core, Data |
| `Audio/` | 骨架占位 | Core |
| `Editor/` | 导入、校验、出包、工具窗口 | 全部运行时模块 |
| `Tests/EditMode` `Tests/PlayMode` | 自动化测试 | Editor / 运行时模块 |
| `Assets/_External/` | 外部素材 junction，**不入库** | — |

方向约定：`Core` 不依赖任何模块；`Data` 只依赖 `Core`；`Flow`/`Battle`/`UI` 消费 `Data`；
`Editor` 只读消费全部运行时模块，运行时模块不反向依赖 `Editor`。

## 外部素材（junction）

美术源文件保存在仓库外的 `E:\tx2\素材`，仓库内只留一个**目录 junction**：

```
samsara-west\Assets\_External  ->  E:\tx2\素材
```

- 选 junction 而不是符号链接：**不需要管理员权限**；素材不必复制进仓库，美术与 Unity 共用同一份文件。
- junction 是机器本地概念，**不随 git 分发**，新克隆或换机器必须重建：
  `Tools/setup-external-assets.ps1`（幂等，可反复执行）。
- 脚本行为：链接缺失 → 创建；已正确 → 原样保留；指向别处 → 加 `-Repair` 才替换；
  `Assets\_External` 是普通目录 → **拒绝执行**（绝不删真实目录，否则会丢掉手工拷进来的素材）；
  源目录位于仓库内 → 拒绝执行。
- Unity 会在素材源目录旁边写 `.meta`，属正常现象，因此 `E:\tx2\素材` 必须可写（脚本会探测并告警）。
- 编辑器内自查：菜单 `SamsaraWest/工程/检查外部素材 junction`。
  `Assets/_External/` 与 `Assets/_External.meta` 已在 `.gitignore` 中忽略。

## 数据管线

```
Data/Tables/*.csv                 （17 张表 · 策划输入面 · 入库）
        │  CsvImporter：引号 / 逗号 / 换行 / BOM + 特性驱动列映射
        ▼
Data/Definitions/**/*.asset       （ScriptableObject 程序产物）
Data/Generated/ImportManifest.asset · DefinitionCatalog.asset

Localization/Tables/localization-zh-Hans.csv
        │  LocalizationImporter
        ▼
Localization/Generated/LocalizationTable_zh-Hans.asset · LocalizationKeys.g.cs
```

- **增量导入**：`ImportManifest.asset` 记录每张表的文件哈希，未改动的表直接跳过。
- **强制全量**：`HeadlessTasks.ImportAndValidate`（改过导入逻辑后跑这个）。
- **ID 规则**：`CHR_*` / `SKL_*` / `ENC_CH01_001` / `CH01_MAP01` 等，重复 ID 或缺失关键字段在编辑期即报错。
- 编辑器入口：`SamsaraWest/数据/数据工具窗口`（Ctrl+Shift+D）、`SamsaraWest/本地化/本地化工具窗口`、
  `SamsaraWest/诊断/日志控制台`。

## 本地化

界面文案全量走文本键，不硬编码中文：键写在 `Localization/Tables/localization-zh-Hans.csv`，导入后生成常量类
`LocalizationKeys.g.cs`（代码里引用 `LocalizationKeys.Xxx`）。兜底扫描 `HardcodedChineseScanner`，
白名单在 `Localization/scan-ignore.txt`。

## 测试

```powershell
Tools\run-tests.ps1                          # EditMode + PlayMode
Tools\run-tests.ps1 -Platform EditMode       # 只跑一套
Tools\run-tests.ps1 -CompileOnly             # 只证明还能编译
Tools\run-tests.ps1 -Platform EditMode -TestFilter SamsaraWest.Tests.EditMode.ObjectPoolTests
Tools\start-tests.ps1 -Platform EditMode     # 后台启动，轮询 Logs\TestResults\EditMode.status.txt
Tools\diag-unity.ps1                         # 只读诊断：进程 CPU、日志体积、资产导入循环
```

| 退出码 | 含义 |
|---|---|
| 0 | 全部通过 |
| 1 | 有失败或不确定的用例 |
| 2 | Unity 自身没跑起来（编译错误、崩溃、拿不到结果文件） |

- Unity 只认一个 `-testFilter`，多个 fixture 用分号写进同一个字符串。
- 无头 Unity 偶尔在写完结果后卡在退出流程，因此脚本以「结果文件可解析出 `<test-run>`」为完成信号，
  超时后杀掉进程并按成功计；`-TimeoutSeconds` 默认 900。
- 同一工程不能并发启动两个 Unity（会写坏 `Library`）：脚本检测到会直接拒绝。
- 纪律：新增断言必须先证明它能失败，避免「永远绿」的假测试。

## 出包与无头入口

```powershell
# Windows x64
Unity.exe -batchmode -quit -projectPath E:\tx2\samsara-west ^
  -executeMethod SamsaraWest.Editor.BuildPipeline.BuildPipeline.BuildWindows -logFile <日志>
# WebGL（试玩版）
Unity.exe -batchmode -quit -projectPath E:\tx2\samsara-west ^
  -executeMethod SamsaraWest.Editor.BuildPipeline.BuildPipeline.BuildWebGl -logFile <日志>
```

菜单入口：`SamsaraWest/出包/Windows x64`、`SamsaraWest/出包/WebGL（试玩版）`；
产物落 `Builds/Windows`、`Builds/WebGL`（在 E 盘，不要落到 C 盘，本机 C 盘空间紧张）。

| 无头入口 | 用途 | 退出码 |
|---|---|---|
| `SamsaraWest.Editor.HeadlessTasks.ImportAndValidate` | 全量重导入 + 本地化导入 + 全量校验 | 0 通过 / 2 校验有错 |
| `SamsaraWest.Editor.HeadlessTasks.ValidateOnly` | 不重导入，只校验现有资产 | 0 / 2 |
| `SamsaraWest.Editor.HeadlessTasks.ScanHardcodedChinese` | 硬编码中文扫描 | 0 / 3 命中 |

退出码同时以 `[CI] exit_code=` 写进日志，便于流水线抓取。

## 已知坑

1. **Unity 编辑器（Mono）下，空条件调用叠加泛型集合取值会挂死主线程**：
   `_onDestroy?.Invoke(_inactive.Pop())` 会让 EditMode 测试永久等待、结果文件永不生成。
   已改为局部变量 + 显式判空（见 `Core/Runtime/Pooling.cs` 的 `DestroyItem`），**不要改回去**。
2. 无头 Unity 可能写完结果后卡在退出流程 → 见「测试」一节的结果文件信号机制。
3. 带中文的 `.ps1` **必须存成 UTF-8 with BOM**，否则 PowerShell 5.1 按 GBK 解码会乱码；
   `Tools/setup-external-assets.ps1` 的默认中文路径用 char code 拼出，不依赖文件编码。
4. 素材源目录不可写会导致导入失败（Unity 要写 `.meta`）。
5. 引导场景只在必要时重建。重建会重新分配场景里的 `fileID`（实测连续两次运行产出的 blob 互不相同），
   所以 `SamsaraWest/工程/一键初始化骨架`（含 `setup-project.ps1`）对已存在的场景直接跳过，不再弄脏工作区；
   只有场景缺失、或它已引用不到当前数据资产（资产被删掉重建、GUID 变了）时才重建。
   要强制重建用菜单 `SamsaraWest/工程/重建引导场景`。
6. **`SetDirty` + `SaveAssets` 会把内容没变的资产整份重写**，mtime 一变，`git status` 就会把生成物显示成已修改
   （`git diff` 却是空的：内容逐字节相同）。所以导入管线一律「内容没变就不落盘」——清单、数据目录、
   本地化表、本地化常量类都按这条写，新增生成物时请照办，否则每次初始化都会留下假改动。
   注意测试套件里有强制全量导入（`ImportAll(force: true)`），跑完测试清单时间戳会变，这是预期内的。

## 设计文档

工程自身的文档在 `Docs/`（随仓库分发）：

- `Docs/架构决策.md`：13 条架构决策（模块与依赖、服务定位、事件总线、随机、日志、存档迁移、生成物不白写盘等）与未决事项。
- `Docs/数据管线.md`：17 张表 → 资产的映射、解析与列映射规则、增量导入的跳过条件、校验码表、常用操作与故障排查。
- `Docs/战斗数值-v1.md`：伤害公式与运算顺序、五行倍率、护体/破防、行动速度、首章数值快照、输出与回合数校算，以及待人工审改的三个方向。

策划与剧情文档不入库，实体在仓库外的 `E:\tx2\开发md文件\`；与本工程配套的评估清单在
`E:\tx2\项目评估与待确认清单.md`。

## 提交纪律

- 提交前必须测试全绿，且新增断言已证伪过一次。
- AI 只执行 `commit`；**push 前必须经人确认**。
