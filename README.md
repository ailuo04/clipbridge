# 互传 ClipBridge

当前 Windows 版本：**0.5.10**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.10/ClipBridge-0.5.10-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.10)

## 更新内容

更新 Windows 与 Android 应用图标
- 采用统一的彩色双向数据流图标，以通用端点表达任意设备之间互传。
- Windows 图标提供 16 至 256 像素的七档尺寸及透明圆角，用于程序、窗口、托盘和安装器。
- Android 图标适配自适应遮罩与安全边距，并采用匹配的蓝紫渐变背景。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.10/ClipBridge-0.5.10-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
