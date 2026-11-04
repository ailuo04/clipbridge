# 互传 ClipBridge

当前 Windows 版本：**0.5.3**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.3/ClipBridge-0.5.3-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.3)

## 更新内容

统一双端界面，并完善扫码配对和 Windows 自动更新体验。
- Windows 设备与配对窗口使用统一明暗主题，在窗口内直接显示二维码；保留地址选择、重新搜索和备用 IP 连接，刷新时保留输入和滚动位置。
- Windows 更新窗口集中显示版本、说明和真实进度，支持下载取消、失败重试及安装阶段防误关；新版确认启动后才完成更新，启动失败保留或恢复旧版。
- 更新后并发再次启动应用时，保留新文件和备份，避免将已有实例接管误判为启动失败并执行回滚；未确认启动不会显示更新完成。
- Android 设备、传输、设置页面统一柔光玻璃卡片和底部弹窗，优化传输状态分组、小屏、大字体及页面状态保留。
- 保留双端配对确认、设备身份校验、原有发布证书，以及 RSA、SHA-256、大小和 HTTPS 更新校验。
- Windows 安装器未进行 Authenticode 代码签名；真实手机、相机扫码、生产局域网及实际安装与原位升级仍需按实际环境验证。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.3/ClipBridge-0.5.3-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
