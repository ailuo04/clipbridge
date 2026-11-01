# 互传 ClipBridge

当前 Windows 版本：**0.3.7**

[下载安装包](https://gitee.com/ailuo047/huchuan/releases/download/v0.3.7/ClipBridge-0.3.7-win-x64-Setup.exe) · [发行版与更新说明](https://gitee.com/ailuo047/huchuan/releases/tag/v0.3.7)

## 更新内容

优化左上角真实应用图标、标题和设备信息排版；更新检查移至顶部，主题切换移至底部，改善主题动画、窗口操作和运行记录；修复更新辅助进程启动并启用新的签名更新渠道；采用约 2.5 MB 轻量安装器，缺少运行环境时才从 Microsoft 官方下载并校验 .NET 10。原版 0.3.5 及更早用户需手动安装一次本版。

## 安装与自动更新

- 下载安装器并运行；缺少 .NET 10 桌面运行时会从 Microsoft 官方联网下载、校验 SHA-256，并请求管理员授权安装。
- 原版 0.3.5 及更早用户需手动安装一次本版，再使用新的自动更新渠道。普通用户不需要任何发布密钥。
- 安装器未进行 Authenticode 代码签名；干净电脑首次安装、原版原位升级和真实生产网络更新尚未全面验证。
- 旧版 latest.json 与历史 ZIP 保留；新客户端读取 latest-v2.json。Android APK 尚未发布。

## 发布方式

源码保存在 Gitee 私有仓库，公开分发和发行版也只使用 Gitee。云端或专用 Windows 发布 runner 通过受保护变量注入 GITEE_TOKEN 和更新签名私钥，完成构建、签名和发布；电脑重置后重新克隆 Gitee 私有源码即可恢复开发。签名私钥必须在仓库外保留加密备份，不能提交到任何仓库。
