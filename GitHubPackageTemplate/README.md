# GrimDawnRunnerLite-Packages 上传模板

仓库地址：`712123846456zcj/GrimDawnRunnerLite-Packages`

## 1. 清单位置

将本目录的 `manifest.json` 上传到远端仓库：

`GitHubPackageTemplate/manifest.json`

客户端默认优先使用国内镜像访问，失败后沿用 GitHub 官方线路及系统/自定义代理设置。

## 2. 字体包

字体使用 `fontPackages` 数组。当前只在线发布未内置的微软雅黑：

- Release Tag：`fonts-v1.0.0`
- 远端 ZIP：`microsoft-yahei-v1.0.0.zip`
- 远端预览：`microsoft-yahei-v1.0.0.png`
- 下载后本地名称：`微软雅黑.zip/png`
- 支持版本：`全版本通用`

仓耳明楷和汉仪正圆已经随程序内置，不再写入在线更新清单。

## 3. 汉化文本包

汉化文本使用 `textPackages` 数组。

### 词缀职业显示增强

- Release Tag：`text-v1.0.0`
- 中文名称：`词缀职业显示增强`
- 英文名称：`Affixes for professions are enhanced`
- 作者：留空
- 分享者：`发光的洛伦兹`
- 远端 ZIP：`Affixes.for.professions.are.enhanced.zip`
- 远端预览：`Affixes.for.professions.are.enhanced.png`
- 下载后本地名称：`词缀职业显示增强.zip/png`
- 支持版本：`v1.2.1.6`

`set有简述版v1.10` 已随程序内置，支持 `v1.3.0.0`，不参与在线更新。

## 4. 支持的游戏版本标签

`supportedGameVersion` 建议使用以下固定显示值：

- `v1.2.1.6`
- `v1.3.0.0 test`
- `v1.3.0.0`
- `全版本通用`

字体一般填写 `全版本通用`；汉化文本根据实际适配版本填写。

## 5. 作者与分享者字段

汉化文本包可在清单中填写：

- `authorName`：文本作者。
- `sharerName`：资源分享者。

客户端下载后会把两个字段写入同名 `.package.json`，安装确认弹窗按照 `authorName` → `sharerName` → `未知作者` 的顺序显示署名。手动放入 ZIP/PNG 时，也可以创建同名 `.package.json` 提供这些字段。内置文本包的作者信息由程序内置。

## 6. 更新字段

发布新文件时同步修改：

- `version`
- Release Tag 和 URL
- `supportedGameVersion`
- `sha256`
- `previewSha256`
- `size`
- `updatedAt`

客户端会校验 SHA-256，并按照 `archiveFileName` 和 `previewFileName` 保存为中文本地文件名。
