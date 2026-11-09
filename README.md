# 互传 ClipBridge

当前 Windows 版本：**0.5.8**

[下载安装器](https://github.com/ailuo04/clipbridge/releases/download/v0.5.8/ClipBridge-0.5.8-win-x64-Setup.exe) · [发行版与更新说明](https://github.com/ailuo04/clipbridge/releases/tag/v0.5.8)

## 更新内容

双端会话与原文件互传
- Windows 改为统一主窗口：左侧设备会话，右侧聊天；添加设备、设置、更新集中导航。
- Android 增加设备会话列表，适配独立草稿、设备切换、删除设备及手机输入布局。
- 手动编辑、粘贴后发送文字和图片；统一文件入口，支持选择文件或文件夹，Windows 支持拖入。
- 图片和视频按原文件传输，不重新编码；文件夹保留子目录和空目录。接收端继续校验文件大小和 SHA-256。
- 点击消息卡片打开图片、视频、文件或文件夹；已配对设备自动接收。
- 多设备草稿与发送状态分开保存，切换会话不影响正在进行的文件传输。
- 保留签名自动更新、HTTPS 下载及 Android 固定发布证书校验。

使用说明：文件夹互传需要两端均升级至 0.5.8；单次最多 256 个文件和目录条目。Android 文件夹保存在应用接收目录，可从会话打开，卸载应用会清除此目录。普通接收文件仍在 Download/ClipBridge。
验证范围：已通过 Windows/Android 构建、单元测试和 Kotlin/C# 双向目录协议互通；真实手机 UI、生产网络速度、安装升级流程未实测。Windows 安装器未进行 Authenticode 签名。

## 安装与自动更新

- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。
- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。
- 0.3.10 及以后安装器需要管理员权限创建安装目录；安装目录会授予当前用户 Modify 权限，后续更新无需重复 UAC。
- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。
- 安装器未进行 Authenticode 代码签名。
- Android APK：[下载安装](https://github.com/ailuo04/clipbridge/releases/download/v0.5.8/ClipBridge-0.5.8-android.apk)；手动配对和发送步骤见 [安卓说明](android/README.md)。

## 开发与发布

源码、GitHub Actions、更新清单和发行版都在 [https://github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。
