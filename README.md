# 互传 ClipBridge

当前 Windows 版本：**0.3.9**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.3.9/ClipBridge-0.3.9-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.3.9)

## 更新内容

GitHub-only 发布迁移版：源码、更新清单、Windows 安装包和 GitHub Actions 统一到 ailuo04/clipbridge。0.3.8 用户需要手动安装一次本版；本版之后使用 GitHub 上的新签名更新渠道自动更新。普通用户不需要发布密钥。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名；Android APK 尚未发布。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
