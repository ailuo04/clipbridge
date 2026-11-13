# 互传 ClipBridge

当前 Windows 版本：**0.6.2**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.6.2/ClipBridge-0.6.2-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.6.2)

## 更新内容

Windows 0.6.2
- 修复“回退到历史版本”因 GitHub 匿名 API 限流（HTTP 403/429）而无法读取的问题，受限时自动通过 GitHub 官方发布网页分页读取历史版本。
- 修复程序集四段版本号与发布标签三段版本号比较不一致的问题，避免将当前版本误列为可回退目标，并拒绝回退到相同版本。
- 历史版本继续使用 RSA 签名清单以及 HTTPS、安装包大小和 SHA-256 校验。

Android 0.6.2
- 同步发布版本号，本次无 Android 功能变更。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.6.2/ClipBridge-0.6.2-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
