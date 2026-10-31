# 互传 ClipBridge

当前 Windows 版本：**0.3.7**

[下载安装包](https://gitee.com/ailuo047/huchuan/releases/download/v0.3.7/ClipBridge-0.3.7-win-x64-Setup.exe) · [发行版与更新说明](https://gitee.com/ailuo047/huchuan/releases/tag/v0.3.7)

## 更新内容

优化左上角真实应用图标、标题和设备信息排版；更新检查移至顶部，主题切换移至底部，改善主题动画、窗口操作和运行记录；修复更新辅助进程启动并启用新的签名更新渠道；采用约 2.5 MB 轻量安装器，缺少运行环境时才从 Microsoft 官方下载并校验 .NET 10；支持登录 GitHub 后一键云端构建、签名和 Gitee 发布。原版 0.3.5 及更早用户需手动安装一次本版。

## 安装与自动更新

- 下载安装器并运行；缺少 .NET 10 桌面运行时会从 Microsoft 官方联网下载、校验 SHA-256，并请求管理员授权安装。
- 原版 0.3.5 及更早用户需手动安装一次本版，再使用新的自动更新渠道。普通用户不需要任何发布密钥。
- 安装器未进行 Authenticode 代码签名；干净电脑首次安装、原版原位升级和真实生产网络更新尚未全面验证。
- 旧版 latest.json 与历史 ZIP 保留；新客户端读取 latest-v2.json。Android APK 尚未发布。

## 发布方式

私有源码在 GitHub；GitHub Actions 负责 Windows 构建与签名，Gitee 仅分发公开产物。发布者重置电脑后登录 GitHub，在私有源码仓库 Actions 运行 Publish to Gitee 即可；不依赖本地密钥。账户/令牌若失效仍需重新授权，发布签名私钥应另留加密备份。
