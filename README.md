# 互传 ClipBridge

当前 Windows 版本：**0.3.8**

[下载安装包](https://gitee.com/ailuo047/huchuan/releases/download/v0.3.8/ClipBridge-0.3.8-win-x64-Setup.exe) · [发行版与更新说明](https://gitee.com/ailuo047/huchuan/releases/tag/v0.3.8)

## 更新内容

建立 Gitee-only 可恢复发布链路，源码迁移到 Gitee 私有仓库；同时保留签名更新机制。由于更新签名密钥迁移，0.3.7 用户需要手动安装一次 0.3.8，之后恢复自动更新。原版 0.3.5 及更早用户也需要手动安装一次本版。

## 安装与自动更新

- 下载安装器并运行；缺少 .NET 10 桌面运行时会从 Microsoft 官方联网下载、校验 SHA-256，并请求管理员授权安装。
- 0.3.7 以及原版 0.3.5 及更早用户需手动安装一次本版，再使用新的自动更新渠道。普通用户不需要任何发布密钥。
- 安装器未进行 Authenticode 代码签名；干净电脑首次安装、原版原位升级和真实生产网络更新尚未全面验证。
- 旧版 latest.json 与历史 ZIP 保留；新客户端读取 latest-v2.json。Android APK 尚未发布。

## 发布方式

源码保存在 Gitee 私有仓库，公开分发和发行版也只使用 Gitee。云端或专用 Windows 发布 runner 通过受保护变量注入 GITEE_TOKEN 和更新签名私钥，完成构建、签名和发布；电脑重置后重新克隆 Gitee 私有源码即可恢复开发。签名私钥必须在仓库外保留加密备份，不能提交到任何仓库。
