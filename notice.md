# gdRunnerLite 开发记录

## 项目概述

恐怖黎明（Grim Dawn）游戏管理小工具，WPF 项目，UI 和功能处于初期阶段。
VS2026+.NET10

---

## 2026-07-22 · 功能：汉化文本在线更新与游戏版本标签

### 远端清单

- `GitHubPackageTemplate/manifest.json` 继续使用 schema 1，并明确拆分为 `fontPackages` 与 `textPackages`。
- 在线字体清单删除已经内置的仓耳明楷和汉仪正圆，只保留微软雅黑。
- 新增在线汉化文本“词缀职业显示增强”：
  - ID：`affixes-for-professions-enhanced`
  - 英文显示名：`Affixes for professions are enhanced`
  - 远端文件：`Affixes.for.professions.are.enhanced.zip/png`
  - 下载后中文文件：`词缀职业显示增强.zip/png`
  - 支持游戏版本：`v1.2.1.6`
- `set有简述版v1.10` 保持程序内置，不加入在线清单，支持游戏版本显示为 `v1.3.0.0`。
- 新增 `supportedGameVersion` 字段，固定支持 `v1.2.1.6`、`v1.3.0.0 test`、`v1.3.0.0` 和“全版本通用”等标签。

### 客户端功能

- 启动时同一次清单请求同时检查字体和汉化文本更新。
- 汉化文本标题栏新增独立的“检查更新/可更新 N”按钮。
- 文本 ZIP、预览图和 `.package.json` 下载到 `Assets\TextBundle`，继续沿用国内加速优先、GitHub 官方回退和 SHA-256 校验。
- 在线英文 Release Asset 下载后按照 manifest 映射保存为中文文件名。
- 字体与文本商品卡预览图右下角新增支持游戏版本标签；字体默认显示“全版本通用”。
- `Wpf_gdRunnerLite.csproj` 新增 TextBundle ZIP 和预览图的输出与发布复制规则，确保内置文本包随程序发行。

### 文本启用状态修复

- 设置新增当前安装文本包 ID、名称、安装时间和远端文本包状态字典。
- 修复只要游戏目录存在 Text_ZH，就导致所有文本商品同时显示“当前已启用”的问题。
- 现在仅持久化 ID 与当前商品一致的文本包显示“当前已启用/重新安装/删除文本”；其他文本包继续显示“启用文本”。
- 切换文本包后会更新当前 ID；删除已安装文本后清理对应持久化状态。

### 验证结果

- [x] 本地 manifest 解析通过：1 个在线字体包、1 个在线文本包
- [x] 空目录评估结果：字体可更新 1、文本可更新 1
- [x] 国内加速下载文本 ZIP/PNG 成功，ZIP 为 1569461 字节
- [x] GitHub 官方系统网络下载文本 ZIP/PNG 成功
- [x] 文本 ZIP SHA-256：`b2d87833706b7000ba03ce80b217410e9a3c98a405f875e43573ba552b0f8ccc`
- [x] 文本预览 SHA-256：`fd1dbd636e99ac50ae48e1ad54309c254c2046b6595381231d8ebcd580e4ac98`
- [x] 下载后保存为中文文件名并生成同名 `.package.json`
- [x] 本地扫描正确识别 set 简述版与词缀职业增强包的类型、ID 和支持版本
- [x] Debug 构建通过，0 个警告、0 个错误

---

## 2026-07-22 · 功能：国内加速优先与代理线路设置

### 默认更新线路

- 字体更新清单、字体 ZIP 和预览图统一接入多线路下载服务。
- 默认使用“自动选择”：优先通过 `gh-proxy.com` 国内加速线路访问 GitHub 资源。
- 国内加速连接超时、HTTP 错误、下载中断或 SHA-256 校验不一致时，自动切换 GitHub 官方线路重新请求。
- 国内加速地址仅在请求时根据官方 GitHub URL 动态生成；manifest 继续保存官方 URL，避免镜像域名进入资源清单。
- 国内加速线路直接连接，不要求普通用户安装 Git、Clash 或其他代理软件。

### 系统与自定义代理

- GitHub 官方线路新增三种网络方式：
  - 跟随 Windows 系统代理；未启用系统代理时使用当前系统网络直连。
  - 不使用代理，强制直接访问 GitHub。
  - 使用自定义 HTTP 代理，例如 `127.0.0.1:7890`。
- 自定义代理支持自动补全 `http://`，并校验协议、主机、端口和路径格式。
- 设置页面可检测 Windows 系统代理启用状态及当前地址，但不会把已关闭代理残留的 `ProxyServer` 误判为正在使用。
- 新增 GitHub 官方清单连接测试，显示连接方式、成功状态和响应耗时。

### 界面与持久化

- 左侧导航新增“设置”页面，集中管理下载线路和高级网络设置。
- 汉化管理标题区域显示当前更新线路，并以简短文本提示普通网络使用国内加速、代理用户前往左侧“设置”选择系统或自定义代理；不额外放置跳转按钮。
- 字体下载窗口显示当前正在使用的国内加速或 GitHub 官方线路。
- `settings.json` 新增下载线路、代理方式和自定义代理地址字段；旧配置自动使用“国内加速优先 + 跟随系统代理”。

### 验证结果

- [x] Windows 系统代理开启时识别到 `127.0.0.1:7890`
- [x] Windows 系统代理关闭后不再解析该代理地址
- [x] 无系统代理状态下 GitHub Raw 清单请求返回 HTTP 200
- [x] gh-proxy 清单 GET 返回 HTTP 200
- [x] gh-proxy Release Asset Range 请求返回 HTTP 206
- [x] 无系统代理状态下，新下载服务自动选择“国内加速”并成功获取清单
- [x] 国内加速下载 894329 字节预览图并通过 SHA-256 校验
- [x] Debug 与 Release 构建通过，0 个警告、0 个错误

---

## 2026-07-22 · v1.3.0 修复：manifest URL 路径

- `FontUpdateService.ManifestUrl` 由仓库根目录改为 `GitHubPackageTemplate/manifest.json`，与远端实际文件位置一致。
- 修复前检查更新返回 404，修复后正常获取清单。
- 远端访问验证：
  - [x] `GitHubPackageTemplate/manifest.json` — HTTP 200
  - [x] Release Asset `microsoft-yahei-v1.0.0.zip` — HTTP 200
  - [x] Release Asset `microsoft-yahei-v1.0.0.png` — HTTP 200（同名推断）

---

## 2026-07-21 · v1.3.0 · 功能：汉化字体在线更新与第三款字体包

### 在线更新

- 启动器启动后在后台尝试读取 GitHub 仓库根目录的 `manifest.json`，不会阻塞主窗口。
- 汉化字体标题栏新增更新状态按钮；发现远端新增包、版本变化、ZIP 哈希变化或预览图变化时显示绿色“可更新 N”。
- 点击更新按钮打开独立下载窗口，显示中文名、英文名、版本、更新原因、蓝色下载按钮和矩形进度条。
- 网络请求使用 Windows 系统默认代理与当前用户代理凭据，兼容常见的直连和系统代理环境。
- 下载过程先写入 `.download` 临时文件，校验 ZIP SHA-256 后再替换正式文件，避免中断下载破坏本地字体包。
- 远端 ZIP/PNG 使用英文 Release Asset 名称；下载完成后按照 manifest 的映射保存为中文本地文件名。
- 下载后的包会生成同名 `.package.json`，保存远端 ID、中文名、英文名、版本和简介，供本地动态扫描继续识别。
- 字体删除和安装前清理仅处理 `Fonts`，保留独立的 `text_ZH` 汉化文本目录。

### GitHub 字体仓库模板

- 新增 `GitHubPackageTemplate/manifest.json` 与上传说明，目标仓库为 `712123846456zcj/GrimDawnRunnerLite-Packages`。
- 首版清单包含仓耳明楷、汉仪正圆和微软雅黑 3 款字体。
- 新增微软雅黑远端包：
  - ID：`microsoft-yahei`
  - 英文资源名：`microsoft-yahei-v1.0.0.zip/png`
  - 本地显示与保存名：`微软雅黑.zip/png`
  - ZIP 内检测到 20 个 `.fnt` 文件。
  - 预览图为 1018×500，适合当前约 400×180 的商品预览区。
- `upload` 中的微软雅黑 ZIP/PNG 已按 GitHub Release 英文资源名整理。

### 验证结果

- [x] 3 款字体的 ZIP 大小、ZIP SHA-256 和预览图 SHA-256 均与 manifest 一致
- [x] manifest JSON 解析通过，共识别 3 款字体
- [x] 微软雅黑 ZIP 结构扫描通过，共识别 20 个 FNT 文件
- [x] Debug 构建通过，0 个编译错误（构建完成后补录）

---

## 2026-07-21 · v1.2.3 · 功能：创建桌面快捷方式

- 主页快捷操作区由三列扩展为四列。
- 在“打开存档”左侧新增首个“创建桌面图标”按钮。
- 新增 `DesktopShortcutService`，通过 Windows Shell 创建标准 `.lnk` 文件。
- 快捷方式名称固定为 `Grim Dawn Runner Lite.lnk`。
- 快捷方式目标自动指向当前 `GrimDawnLauncher.exe`，工作目录为启动器所在目录，图标使用启动器 EXE 自身图标。
- 桌面路径通过 `Environment.SpecialFolder.DesktopDirectory` 获取，不包含固定用户名。
- 如果桌面已经存在同名快捷方式，点击按钮会更新覆盖。
- 创建成功或失败时同时更新主页操作状态并显示结果弹窗。
- 使用临时目录完成创建和重复覆盖烟雾测试，没有向真实桌面写入测试文件。

---

## 2026-07-21 · v1.2.2 · 调整：便携持久化与字体预览

- 设置主路径从 `%LOCALAPPDATA%` 改为程序目录下的 `settings.json`。
- 首次运行新版时，如程序目录没有配置但存在旧配置，会读取旧配置、写入程序目录并清理旧的 JSON、备份和临时文件。
- 字体扫描、手动新增和后续下载统一使用 `程序目录\Assets\FontBundle`，不再使用 C 盘扩展包目录。
- “扩展字体目录”按钮调整为“字体包目录”，直接打开程序旁的字体包文件夹。
- 字体预览高度调整为 180，移除装饰背景图、暗色遮罩和 26 像素图片边距。
- 预览缩放改为高质量 `UniformToFill`，让截图直接铺满约 400×180 的商品预览区。
- 初始化路径提示更新为“程序目录下的 settings.json”。
- 仓耳明楷新预览图检测结果为 658×421，可以直接用于当前商品卡。

---

## 2026-07-21 · v1.2.1 · 功能：字体商品自动扫描

- 汉化字体商品列表由硬编码两项改为动态扫描。
- 每次进入页面或点击“重新检测”时扫描 `程序目录\Assets\FontBundle` 和 `%LOCALAPPDATA%\GrimDawnRunnerLite\Packages\FontBundle`。
- 每个 `*.zip` 自动生成商品卡片，ZIP 文件名作为字体标题。
- 自动匹配同名 PNG、JPG、JPEG 或 `.preview.*` 预览图。
- 新增“扩展字体目录”按钮；放入 ZIP 和同名图片后刷新即可载入。
- 内置字体与扩展字体同名时，扩展目录版本优先。
- 仓耳明楷和汉仪正圆保留专用简介及原有持久化 ID，其他字体使用自动 ID 和默认简介。
- 动态扫描合成 ZIP 烟雾测试通过。

---

## 2026-07-21 · v1.2.0 · 功能：设置持久化与汉化字体管理

### 设置持久化

- 新增统一的 `SettingsService`，继续使用 `%LOCALAPPDATA%\GrimDawnRunnerLite\settings.json`。
- 设置文件改为临时文件写入后原子替换，并保留一份 `settings.json.bak`。
- 除游戏根目录外，新增持久化内容：
  - 启动成功后是否关闭启动器
  - 最后打开的页面
  - 最近使用的启动模式
  - 当前安装的字体包、字体名称和安装时间
- 旧版本仅包含 `GameRoot` 的 JSON 可以直接加载，其余字段自动使用默认值。

### 汉化字体管理

- 将“汉化管理”页面拆分为“汉化字体”和“汉化文本”两个区域。
- 首批本地内置字体包：
  - 仓耳明楷
  - 汉仪正圆
- 字体包 ZIP 保持为 EXE 外置发行文件，发布后位于 `Assets\FontBundle`，不会嵌入单文件程序。
- 自动读取同名字体预览图，例如 `仓耳明楷.png` 和 `汉仪正圆.png`。
- 安装前检查以下旧内容：
  - 游戏根目录 `settings\Fonts`
  - 游戏根目录 `settings\text_ZH`
  - 本地存档设置目录 `Settings\Fonts`
  - 本地存档设置目录 `Settings\text_ZH`
- 检测到旧内容时列出具体路径，得到用户确认后清理，再安装新字体。
- ZIP 内即使存在额外顶层文件夹，也只提取 `.fnt` 文件并直接写入：
  - `游戏根目录\settings\Fonts\zh`
- 使用游戏根目录下的临时 staging 目录完成解压，避免产生“文件夹套娃”。
- 页面提供重新检测、打开游戏字体目录、打开本地设置目录和重新安装功能。

### 其他修复

- 初始化游戏目录窗口的“确定”按钮改为“退出”，并补充 Enter/Esc 默认操作。
- 修复 `Publish.ps1` 的 UTF-8 编码兼容问题，Windows PowerShell 5.1 现在可以正常执行发布脚本。

### 涉及文件

- `Models/LauncherSettings.cs`
- `Models/FontPackage.cs`
- `Services/SettingsService.cs`
- `Services/GameLocationService.cs`
- `Services/LocalizationService.cs`
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `GamePathSetupDialog.xaml`
- `Wpf_gdRunnerLite.csproj`

### 验证结果

- [x] Debug 构建通过，0 个编译错误
- [x] win-x64 自包含单文件发布通过
- [x] 两个 ZIP 和同名预览图作为 EXE 外置内容复制到发布目录
- [x] 合成嵌套 ZIP 烟雾测试通过
- [x] FNT 文件正确落到 `settings\Fonts\zh`
- [x] 旧字体文件被替换且没有产生额外嵌套目录

> 构建时出现的 NU1900 仅表示当前环境无法访问 NuGet 漏洞数据源，不影响项目编译。

---

## 2026-07-21 修复：顶部白条问题

**现象：** 窗口顶部出现一条白色横条，位于自定义标题栏上方。

**原因：** 使用了 `WindowStyle="None"` + `ResizeMode="CanResize"`，但未设置 `WindowChrome`。DWM 仍会为可调整大小的窗口保留非客户区，导致顶部出现白条。

**修复内容（MainWindow.xaml）：**

1. 在 `<Window.Resources>` 前添加 `WindowChrome`：
   - `CaptionHeight="38"` 匹配标题栏行高
   - `GlassFrameThickness="0"` 消除 Aero 玻璃残留
   - `UseAeroCaptionButtons="False"` 禁用系统默认按钮

2. 给标题栏按钮的 StackPanel 添加 `WindowChrome.IsHitTestVisibleInChrome="True"`，确保最小化/最大化/关闭按钮可正常点击。
