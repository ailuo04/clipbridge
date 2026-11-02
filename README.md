# 互传 ClipBridge

当前 Windows 版本：**0.3.10**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.3.10/ClipBridge-0.3.10-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.3.10)

## 更新内容

安装器权限修复：安装时使用管理员权限创建 D:\own\app\ClipBridge，并只对 ClipBridge 安装目录授予普通用户 Modify 权限。应用和后续 GitHub 自动更新仍可由普通用户运行，不需要每次更新都弹出 UAC。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名；Android APK 尚未发布。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。

## 开发中的手动互传预览

本分支新增 Android 客户端与 Windows 手动互传模式：两端主动预览并发送剪贴板、双向加密文件传输、接收确认、进度、取消、大小和 SHA-256 校验。Windows 不再监听复制事件，启动和接收请求不抢焦点，自动更新检查不自动弹窗。

这属于本地预览开发，不修改上方正式版的 Release 或更新清单。安装、配对、构建和当前验证限制见 [android/README.md](android/README.md)。
