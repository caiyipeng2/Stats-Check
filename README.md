# LOL BP 战绩速查（LolStatsCheck）

国服《英雄联盟》客户端 BP 阶段实时战绩速查工具：只读本机 LCU 接口，进入房间/BP 时自动识别当前模式，按“我方 / 敌方”展示双方玩家最近战绩、常用英雄与段位。

- 版本：1.1.0
- 平台：Windows 10/11 x64
- 交付：单文件、免安装、免运行时的 `LolStatsCheck.exe`
- 联网范围：仅 `127.0.0.1`（本机客户端 / Live Client Data）

## 功能

- 支持单双排、灵活排位、匹配、快速匹配、极地大乱斗、海克斯大乱斗及官方队列目录中的其他模式。
- 进入房间、匹配中、接受对局、BP、对局中都会识别并显示当前模式；BP 开始自动弹出置顶悬浮窗。
- 每人展示最近 10/20/30 场（默认 20）的胜负、胜率、平均 KDA、连胜/连败、补位标记。
- 展示最近使用的 4 个英雄与当前英雄战绩；段位可获取时显示。
- BP 结束后按设置继续显示 15/30/60 秒或一直显示；进入游戏后尝试从本地对局数据补齐双方。
- 托盘常驻、全局快捷键 `Ctrl+Alt+L`、窗口透明度/置顶/位置记忆、单实例运行。
- 客户端公开身份才查询；`nameVisibilityType = HIDDEN` 的玩家显示锁定占位，不绕过、不解密。

## 快速开始

1. 运行 `LolStatsCheck.exe`（首次运行会展示免责声明）。
2. 启动并登录英雄联盟客户端，工具会自动连接；也可在设置中手动选择 `LeagueClientUx.exe`。
3. 进入房间或接受对局后，托盘与窗口会显示当前模式；进入 BP 时窗口自动弹出。
4. 退出游戏后窗口自动隐藏，托盘继续等待下一局。

配置文件：`%APPDATA%\LolStatsCheck\config.json`  
日志目录：`%APPDATA%\LolStatsCheck\logs\`

## 支持的模式

内置了国服官方队列目录中的主要模式（2026-10 整理），并会在连接后合并客户端实时返回的队列名称，因此新上线模式即使未内置也能正确显示。常见队列包括：

- 排位：单双排（420）、灵活排位（440）、5 人排位赛（710）
- 匹配：匹配模式·征召/自选（400/430）、快速匹配（490）、快速模式（480）
- 大乱斗：极地大乱斗（450）、海克斯大乱斗（2400）、海克斯大乱斗 巅峰赛（2410）、海克斯大乱斗 经典模式版（2450）
- 轮换/娱乐：无限乱斗（900）、无限火力（1900）、冰雪无限火力（1010）、克隆大作战（1020）、极限闪击（1200/1300）、终极魔典（1400）、斗魂竞技场（1700/1710）、神木之门（2300）、无尽狂潮（1810-1840）、末日人工智能系列、冠军杯赛系列
- 自定义、人机与官方队列目录中的历史模式使用官方名称回退

> 国服不同模式的身份公开策略由客户端决定；单双排 BP 中大部分对手会被 Riot 隐藏，这是客户端机制，本工具不会绕过。进入对局后若客户端公开身份，会自动补齐。

## 兜底工具：LeagueAkari

自研查询失败（接口异常、战绩为空等）时，可按设置自动启动你本机安装的 [LeagueAkari](https://github.com/LeagueAkari/LeagueAkari)（MIT 开源）。本工具不附带、不下载第三方程序，也不读取或注入 LeagueAkari 的数据。

- 自动检测位置：程序目录下的 `fallback\LeagueAkari\LeagueAkari.exe`、`fallback\LeagueAkari.exe`，以及 `%LOCALAPPDATA%\Programs\LeagueAkari` 等常见安装目录。
- 也可在“设置 → 兜底工具”中手动选择 exe；点击“获取 LeagueAkari”会打开官方最新发布页。
- 自动启动默认开启，每个对局最多一次；**仅在存在查询失败时触发**，玩家身份未公开时不会自动启动。
- 隐藏身份玩家卡片上仍可手动点击兜底按钮；是否使用第三方工具及其账号风险由你自行判断。
- LeagueAkari 内含的 `magic` 等身份还原能力属于 Riot 明确禁止的能力，本工具不调用、不配置、不自动化该能力。

如需便携分发，可自行从官方发布页下载 `league-akari-win-x64.7z`，解压到 exe 旁的 `fallback\LeagueAkari\` 目录，程序会自动识别；本项目不随包分发该二进制。

## 构建

环境：.NET SDK 9、Windows 10/11 x64。

```powershell
# 运行离线测试 + 单文件发布（powershell 5.1 或 pwsh 均可）
pwsh -File build.ps1

# 跳过测试直接发布
pwsh -File build.ps1 -SkipTests
```

产物：`dist\LolStatsCheck.exe`（自包含单文件，约 66 MB）。
SHA256：`3505660AEFA6C3ADA20E0B8855466C1E2CB4EBE7073B7032F32A4FF0699968BC`

手动发布命令：

```powershell
dotnet publish src/LolStatsCheck.App/LolStatsCheck.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist
```

## 测试

```powershell
dotnet run --project tests/LolStatsCheck.Tests/LolStatsCheck.Tests.csproj -c Release
```

离线测试覆盖：lockfile、BP 会话/隐藏身份、房间队列解析、腾讯服战绩历史、统计口径、段位、设置读写、队列名称回退、兜底路径解析、Mock 数据。测试样本均为脱敏构造数据。

UI 预览：

```powershell
dist\LolStatsCheck.exe --mock
```

其他参数：`--settings` 启动即打开设置，`--tray` 仅托盘启动，`--diagnose` 记录诊断日志。

## 隐私与合规

- 只读取本机客户端公开数据与本地 Live Client Data；不注入、不读写游戏内存、不修改客户端。
- 除 `127.0.0.1` 外不发起网络请求；不上传任何数据。
- 日志不写入 remoting 认证令牌、完整 lockfile 或完整 PUUID（只记录前 8 位）。
- 不提供 BP 身份解密、自动选人/接受/点赞/聊天等自动化能力。
- 非官方工具，与 Riot Games、腾讯无关；使用风险由使用者自行承担。

## 已知限制

- 单双排 BP 阶段被客户端隐藏的对手无法显示，进入对局后能否补齐取决于客户端是否公开身份。
- 腾讯服战绩服务在夜间可能波动，失败时保留缓存并提示重试。
- 未做代码签名，首次运行可能触发 SmartScreen：选择“更多信息 → 仍要运行”。
- 英雄/头像资源来自本机客户端，客户端未启动时无法加载头像。

## 许可与归属

参考项目、第三方工具与商标归属见 [NOTICE.md](NOTICE.md)。发布产物不包含任何第三方项目的源码或二进制。
