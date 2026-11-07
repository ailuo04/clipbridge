# 互传 ClipBridge

当前 Windows 版本：**0.5.5**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.5/ClipBridge-0.5.5-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.5)

## 更新内容

轻量对话窗口与跨设备历史记录
- 设备连接后可使用轻量会话窗口发送文本、接收内容，并从托盘重新打开；窗口隐藏时连接继续保持。
- Windows 与 Android 支持按设备同步传输历史，文本和文件记录保留 7 天，可手动删除历史记录。
- 删除文件历史记录只删除记录、不删除本地文件；可从历史记录访问本地文件位置。
- 改进配对、会话窗口和历史列表的视觉与交互体验，并增强更新流程反馈。
- 保留签名更新清单、SHA-256 与文件大小校验、HTTPS 地址校验和 Android 固定签名证书校验。
- Windows 安装器未进行 Authenticode 代码签名；真实设备间联网传输仍需实际设备验证。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.5/ClipBridge-0.5.5-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
