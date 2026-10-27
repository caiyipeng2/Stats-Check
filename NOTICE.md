# NOTICE

本仓库包含原创代码，并在设计调研阶段参考了以下公开项目与文档。除明确列出的运行时组件外，发布产物中不包含任何第三方项目的源码、二进制或数据。

## 参考项目（仅参考，未复制代码）

| 项目 | 许可证 | 参考内容 |
| --- | --- | --- |
| [LeagueAkari](https://github.com/LeagueAkari/LeagueAkari) | MIT | LCU 接口用法、BP 匿名机制说明、腾讯服 LCU 数据格式 |
| [League-Toolkit](https://github.com/comtnt/League-Toolkit) | MIT | LCU 会话与数据模型的组织方式 |
| [rank-analysis](https://github.com/wnzzer/rank-analysis) | MIT | 战绩统计与悬浮窗交互思路 |
| [lcu-and-riotclient-api](https://github.com/KebsCS/lcu-and-riotclient-api) | 文档 | LCU 本地接口说明 |

上述项目与本项目不存在代码托管、打包或分发关系；本项目不包含 LeagueAkari 的 `magic` 解密模块，也不包含任何身份还原逻辑。

## 外部兜底工具（不打包、不分发）

| 工具 | 许可证 | 说明 |
| --- | --- | --- |
| [LeagueAkari](https://github.com/LeagueAkari/LeagueAkari) | MIT | 可选兜底。本项目仅自动检测用户本机已安装的 LeagueAkari（或用户自行放置在程序目录 `fallback\LeagueAkari` 下的副本），在自研查询失败时按设置启动；不随仓库或发布包提供其二进制，不读取/注入其数据，不调用其 `magic` 身份还原能力，也不会因隐藏身份而自动启动。其能力、兼容性与账号风险由用户自行承担。 |
| [HH-Lol-Prophet](https://github.com/real-web-world/hh-lol-prophet) | MIT | 备选外部程序。用户自行下载与配置，本项目只提供按用户配置路径启动的入口，不修改、不注入、不读取其数据。 |

## 运行时与平台组件

- 发布产物为 .NET 9 自包含单文件 exe，内含 .NET Runtime 与 WPF/WinForms 运行时组件，遵循其各自许可证（MIT 等）。
- 英雄名称、英雄头像、玩家头像与段位图标均在运行时从用户本机英雄联盟客户端读取，不随本仓库分发。
- “英雄联盟”“League of Legends”及相关商标归 Riot Games / 腾讯所有。本项目为非官方工具，与 Riot Games、腾讯无关。

## 测试数据

`tests/LolStatsCheck.Tests/Fixtures/` 下的 JSON 均为人工构造的脱敏样本，仅用于验证解析逻辑；不包含真实玩家的 PUUID、召唤师名或对局记录。

真实 LCU 样本只在开发者本机临时目录中用于本地校验，不提交到仓库。

