# 互传

这是 ClipBridge / 互传的公开更新发布仓库。

客户端会自动读取本仓库的 `latest.json`，验证签名和安装包哈希后再更新。不要在客户端中加入 Gitee 访问令牌。

发布新版本时，在源码目录运行：

```powershell
.\tools\Publish-Release.ps1 -Version 0.3.0 -Notes '本次更新说明' -AndroidApkPath 'android\app\build\outputs\apk\release\app-release.apk'
```

Windows ZIP 和 Android APK 直接放在仓库根目录，文件名包含版本号，并由 `latest.json` 中的签名清单指向。

首次推送时，将当前目录作为独立仓库：

```powershell
git init -b master
git add .
git commit -m "发布 ClipBridge 0.2.0"
git remote add origin https://gitee.com/ailuo047/huchuan.git
git push -u origin master
```
