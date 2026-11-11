# 互传 ClipBridge

当前 Windows 版本：**0.5.9**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.9/ClipBridge-0.5.9-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.9)

## 更新内容

Windows / Android 版本更新与历史回退
- 优化 Windows 单窗口工作区：输入框、文件按钮、设备导航和设置入口层次更清晰，统一圆角与主题样式。
- 更新状态改为侧栏椭圆控件，支持最新版、检查中、失败重试和发现新版本后的立即更新。
- 设置页新增备用检查更新入口，并支持读取 GitHub Releases 历史版本列表。
- 支持用户主动选择已发布旧版本回退；仅允许通过 RSA 签名清单、HTTPS、文件大小、SHA-256 和安全解压校验的版本进入安装流程。
- 回退安装沿用独立更新助手、启动确认和失败恢复机制，普通自动更新仍不会自动降级。
- 保留 Windows 与 Android 的现有加密传输、文件夹互传和签名更新能力。

验证范围：已通过 Windows Release 构建、会话与更新 UI smoke、更新清单和 GitHub Releases 分页 smoke、安装助手成功/取消/失败恢复测试，以及发布脚本测试。Windows 安装器未进行 Authenticode 代码签名；真实手机、生产网络和实际原位回退仍需单独验证。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.9/ClipBridge-0.5.9-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
