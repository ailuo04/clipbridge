# 互传

这是 ClipBridge / 互传的公开更新发布仓库。

客户端会自动读取本仓库的 `latest.json`，验证签名和安装包哈希后再更新。不要在客户端中加入 Gitee 访问令牌。

当前 Windows 0.3.5 发布文件：

- 安装器：`ClipBridge-0.3.5-win-x64-Setup.exe`
- 自动更新包：`ClipBridge-0.3.5-win-x64.zip`
- 更新清单：`latest.json`

本次更新调整了 Windows 控制面板界面，支持白色、黑色和跟随电脑主题。

Android APK 尚未发布；0.3.5 尚未完成实际安装和端到端更新验证，安装器未进行 Authenticode 代码签名。

安装器会按需安装 Microsoft .NET 8 Desktop Runtime，默认创建桌面快捷方式，并创建开始菜单和卸载入口。程序和安装器统一使用新的 ClipBridge 应用图标。

发布新版本时，在源码目录运行：

```powershell
.\tools\Publish-Release.ps1 -Version 0.3.0 -Notes '本次更新说明' -AndroidApkPath 'android\app\build\outputs\apk\release\app-release.apk'
```

Windows ZIP 和 Android APK 直接放在仓库根目录，文件名包含版本号，并由 `latest.json` 中的签名清单指向。

构建 Setup EXE 需要 Inno Setup 6：

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

`Publish-Release.ps1` 会同时生成 Windows ZIP 和 Setup EXE，并把安装器地址、大小和 SHA-256 写进签名清单。

首次推送时，将当前目录作为独立仓库：

```powershell
git init -b master
git add .
git commit -m "发布 ClipBridge 0.2.0"
git remote add origin https://gitee.com/ailuo047/huchuan.git
git push -u origin master
```
