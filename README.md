# 流萤加速器（FireflyVPN）

流萤加速器是一个基于 [v2rayN](https://github.com/2dust/v2rayN) 深度定制的桌面网络代理客户端，提供简洁的节点管理、订阅更新、系统代理、分流规则与 TUN 模式等功能。

[![GitHub](https://img.shields.io/badge/GitHub-FireflyVPN--Desktop-181717?logo=github)](https://github.com/Iskongkongyo/FireflyVPN-Desktop)
[![Telegram](https://img.shields.io/badge/Telegram-频道-26A5E4?logo=telegram&logoColor=white)](https://t.me/+N3h80bmqvVMwYzll)
[![License](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)

![预览](./preview.png)

## 功能特点

- 支持 sing-box、Xray、Mihomo 等代理核心
- 支持订阅管理、节点导入、节点分组与测速
- 支持系统代理、PAC、路由分流和 TUN 模式
- 支持 Windows、Linux 与 macOS 桌面环境
- 提供首次使用的新手引导，以及 Windows 安装包与卸载清理选项

## 下载与使用

请前往项目的 [Releases 页面](https://github.com/Iskongkongyo/FireflyVPN-Desktop/releases) 下载适合系统架构的最新版本。

首次启动后，导入订阅或节点链接，选择节点并开启系统代理或 TUN 模式即可使用。更多操作说明可参考上游 [v2rayN Wiki](https://github.com/2dust/v2rayN/wiki)。

> [!TIP]
> 使用代理服务前，请确认当地法律法规、网络服务条款及所在组织的网络管理要求。

## 后端与 Crypto V2

后端位于 [backend](./backend/)，客户端使用纯匿名设备身份，不提供邮箱或密码登录。

核心流程：

1. 从 GET /api/v2/bootstrap 获取公告、更新、公开设置及加密协议版本。
2. 首次安装生成设备 ID 与 P-256 密钥对，并通过 POST /api/v2/devices/enroll 注册。
3. 携带 Device Token 获取 /api/v2/subscriptions 安全目录。
4. 使用一次性 Challenge 获取 Crypto V2 加密正文，在本机完成解密与完整性校验。
5. VPN 会话结束后幂等上报流量；失败报告保留在本地，后续自动重试。

客户端只接受 P256-HKDF-SHA256-A256GCM，不会降级到旧版订阅加密。设备凭据和安全订阅请求均限制在配置的 Worker 同源地址。

后端开发、部署和完整接口说明：

- [后端部署说明](./backend/README.md)
- [API 契约](./backend/API.md)

## 社区

- Telegram 频道：[https://t.me/+N3h80bmqvVMwYzll](https://t.me/+N3h80bmqvVMwYzll)
- 项目地址：[https://github.com/Iskongkongyo/FireflyVPN-Desktop](https://github.com/Iskongkongyo/FireflyVPN-Desktop)

## 开源与致谢

本项目基于 [v2rayN](https://github.com/2dust/v2rayN) 开发，感谢 v2rayN 及所有上游开源项目的贡献者。

本项目采用 [GNU General Public License v3.0](LICENSE)（GPL-3.0）开源。发布、修改或再分发时，请遵守许可证要求，并保留上游版权与许可声明。
