# 互传 ClipBridge

## GitHub-only 发布与恢复

唯一仓库：[github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge)

- 源码、构建脚本、测试和 GitHub Actions 都在本仓库。
- [下载安装器与 GitHub Release](https://github.com/ailuo04/clipbridge/releases)
- 客户端更新清单：[GitHub Raw latest-v2.json](https://raw.githubusercontent.com/ailuo04/clipbridge/master/latest-v2.json)
- 发布手册：[tools/GitHub-Release-Runbook.md](tools/GitHub-Release-Runbook.md)

以后正式发布只使用 GitHub，不再发布到 Gitee。普通用户不需要任何发布密钥；更新清单使用 RSA 签名，安装包使用 SHA-256 校验。

## 0.3.8 到 0.3.9 的迁移

0.3.8 及更早版本仍会请求旧的 Gitee 更新地址，无法通过远程修改清单自动切换到 GitHub。因此 0.3.8 用户需要手动安装一次 0.3.9。0.3.9 内置 GitHub 更新地址和新的签名公钥，0.3.9 之后的版本会继续通过 GitHub 自动更新。

## 本地开发

安装 global.json 指定的 .NET SDK 10.0.403，然后运行：

~~~powershell
dotnet build ClipBridge.csproj -c Release
pwsh ./tools/Get-BuildTools.ps1 -OutputDirectory "$env:LOCALAPPDATA/ClipBridgeBuildTools"
~~~

本地打包和验签默认不上传：

~~~powershell
pwsh ./tools/Publish-GitHub.ps1 -Version 0.3.9 -Notes '本次更新说明。' -OutputDirectory "$env:TEMP/clipbridge-release" -PrivateKeyPath "$env:USERPROFILE/.clipbridge-release-keys/clipbridge-update-private-0.3.9.pem" -InnoCompilerPath "$env:LOCALAPPDATA/ClipBridgeBuildTools/InnoSetup/ISCC.exe"
~~~

## 电脑重置后的发布恢复

重新克隆本仓库即可恢复源码、测试和发布工作流。更新私钥不能由公钥恢复，必须从仓库外的两份加密备份恢复到 GitHub Actions Secret CLIPBRIDGE_UPDATE_PRIVATE_KEY；私钥不提交仓库。发布时推送 v0.3.9 形式的标签，或在 Actions 中手动运行发布工作流；工作流会在 Windows runner 上构建、签名、提交更新清单、上传 Setup.exe 和 ZIP，并创建 GitHub Release。

具体规则见 [AGENTS.md](AGENTS.md)。
