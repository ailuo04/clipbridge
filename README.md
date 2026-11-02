# 互传 ClipBridge

当前 Windows 版本：**0.4.0**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.4.0/ClipBridge-0.4.0-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.4.0)

## 更新内容

新增 Android 手机与 Windows 电脑的局域网双向互传。
- 两端手动预览并发送文字剪贴板，关闭 Windows 自动监听；不使用 Shizuku、ADB 或默认输入法。
- 支持文件选择、Windows 拖拽及 Android 系统分享；接收前确认，显示进度并支持取消。
- 文件分块加密传输，接收后检查大小和 SHA-256；同名文件保留原件。
- Windows 启动留在托盘，收到文件请求和自动更新检查不弹窗、不抢焦点。
- Android 使用持久发布签名；Windows 保留兼容的 RSA 签名更新机制。
使用：两端连接同一 Wi-Fi 或手机热点，在手机输入 Windows 显示的配对密钥并开始连接；主动选择设备发送。收到文件后主动打开待接收列表接受。
验证边界：构建、无界面协议测试和 Android Lint 已通过；一加 15 真机、Windows 交互界面、真实网络及大视频传输仍待实测。Windows 安装器没有 Authenticode 代码签名。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.4.0/ClipBridge-0.4.0-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
