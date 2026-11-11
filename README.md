# 互传 ClipBridge

当前 Windows 与 Android 版本：**0.5.9**

[Windows 下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.9/ClipBridge-0.5.9-win-x64-Setup.exe) · [Android APK](https://github.com/ailuo04/clipbridge/releases/download/v0.5.9/ClipBridge-0.5.9-android.apk) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.9)

## 更新内容

Windows 更新体验与历史版本回退
- 统一单窗口中的更新状态、接收文件夹和设置入口；改进输入框、文件按钮、设备导航及圆角菜单层次。
- 侧栏显示最新版、检查中、失败重试或发现新版本，可从设置页备用检查更新。
- 设置页支持查看 GitHub Releases 历史版本，并在用户确认后回退到经过签名验证的版本。
- 回退继续校验 RSA 签名、HTTPS 地址、包大小、SHA-256 和 ZIP 内容；普通自动更新仍只允许升级，失败时保留原版本恢复机制。
- 保留现有多设备会话、文件与文件夹互传及 Android 固定证书更新校验。

Windows 安装器未进行 Authenticode 代码签名；实际安装、生产网络与手机设备行为需在目标环境验证。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK 使用固定发布证书；调试包不能覆盖正式版。手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [GitHub](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志；私钥还必须在仓库外保留加密备份。
