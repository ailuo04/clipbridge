# 互传 ClipBridge

当前 Windows 版本：**0.5.2**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.2/ClipBridge-0.5.2-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.2)

## 更新内容

新增扫码配对，并优化手机与电脑的局域网连接体验。
- 电脑显示配对二维码，手机扫码后仍须在两端核对并确认验证码；也可继续搜索设备或手动输入 IP 配对。
- 搜索入口更醒目，网络切换和手动搜索时更快刷新；改进配对请求提醒、本地网络权限引导及连接失败提示。
- 保留原有签名更新、双端配对确认及设备身份校验；二维码不包含设备密钥。
- 本版已完成自动化测试，但尚未完成真实手机相机扫码、NTU 网络互通和实际安装升级测试；校园网如隔离设备，可尝试个人热点。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.2/ClipBridge-0.5.2-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
