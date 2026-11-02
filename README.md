# 互传 ClipBridge

当前 Windows 版本：**0.5.0**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.0/ClipBridge-0.5.0-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.0)

## 更新内容

新增手机版自动更新、设备自动发现与新版三页界面。
- 手机首页自动搜索同一 Wi-Fi 或热点内运行互传的设备；未配对即可发现，首次双端核对六位验证码，无需输入长密钥。
- 新配对使用系统 P-256 ECDH、承诺校验、双端确认与独立设备密钥；可单独撤销，保留旧版密钥配对兼容。
- 安卓增加设备、传输、设置页面，支持系统明暗主题、文件摘要、可读大小和进度、接收请求及系统分享。
- 手机自动检查更新，默认非计费网络自动下载；校验 RSA 签名、包名、版本、大小、SHA-256 与固定证书后，由用户点击进入系统安装。
- Windows 增加附近设备与配对管理入口，所有后台请求保持安静，不自动打开窗口或打断游戏。
- 当前最低 Android 10、目标 API 36，预留 API 37 目标版本提升后的本地网络权限处理；未来系统版本仍需持续实测适配。
- 已安装 0.4.0 手机正式版需手动覆盖安装这次新版；后续可使用应用内更新。剪贴板继续手动发送，不使用 Shizuku、ADB 或输入法权限。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.0/ClipBridge-0.5.0-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
