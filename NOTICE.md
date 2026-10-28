# NOTICE

本仓库包含原创代码，并在设计调研阶段参考了以下公开项目与文档。发布产物除 .NET 运行时外，还附带 LeagueAkari 官方二进制作为可选兜底工具（见下文）。

## 参考项目（仅参考，未复制代码）

| 项目 | 许可证 | 参考内容 |
| --- | --- | --- |
| [LeagueAkari](https://github.com/LeagueAkari/LeagueAkari) | MIT | LCU 接口用法、BP 匿名机制说明、腾讯服 LCU 数据格式 |
| [League-Toolkit](https://github.com/comtnt/League-Toolkit) | MIT | LCU 会话与数据模型的组织方式 |
| [rank-analysis](https://github.com/wnzzer/rank-analysis) | MIT | 战绩统计与悬浮窗交互思路 |
| [lcu-and-riotclient-api](https://github.com/KebsCS/lcu-and-riotclient-api) | 文档 | LCU 本地接口说明 |

上述源码项目与本项目不存在代码托管关系；本项目不包含 LeagueAkari 的 `magic` 解密模块，也不包含任何身份还原逻辑。

## 内置兜底工具（随发布包附带）

| 工具 | 版本 | 许可证 | 说明 |
| --- | --- | --- | --- |
| [LeagueAkari](https://github.com/LeagueAkari/LeagueAkari) | v1.5.1 | MIT | 随发布包以原样二进制形式置于 `fallback\LeagueAkari\`，许可证见 [third_party/LeagueAkari-LICENSE.txt](third_party/LeagueAkari-LICENSE.txt)（Electron/Chromium 许可文本亦随目录附带）。本项目仅在自研查询失败时按设置启动该程序，默认参数为空；不修改、不注入、不读取其数据，不调用其 `magic` 身份还原能力，也不会因隐藏身份而自动启动。第三方程序可能自行发起外部网络请求，其行为与账号风险由使用者自行判断。 |

## 外部兜底工具（不打包、不分发）

| 工具 | 许可证 | 说明 |
| --- | --- | --- |
| [HH-Lol-Prophet](https://github.com/real-web-world/hh-lol-prophet) | MIT | 备选外部程序。用户自行下载与配置，本项目只提供按用户配置路径启动的入口，不修改、不注入、不读取其数据。 |

## 运行时与平台组件

- 发布产物为 .NET 9 自包含单文件 exe，内含 .NET Runtime 与 WPF/WinForms 运行时组件，遵循其各自许可证（MIT 等）。
- 英雄名称、英雄头像、玩家头像与段位图标均在运行时从用户本机英雄联盟客户端读取，不随本仓库分发。
- “英雄联盟”“League of Legends”及相关商标归 Riot Games / 腾讯所有。本项目为非官方工具，与 Riot Games、腾讯无关。

## 测试数据

`tests/LolStatsCheck.Tests/Fixtures/` 下的 JSON 均为人工构造的脱敏样本，仅用于验证解析逻辑；不包含真实玩家的 PUUID、召唤师名或对局记录。

真实 LCU 样本只在开发者本机临时目录中用于本地校验，不提交到仓库。

