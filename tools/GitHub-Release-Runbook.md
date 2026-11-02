# GitHub-only 发布恢复手册

## 固定结构

- 唯一源码和发布仓库：https://github.com/ailuo04/clipbridge.git
- 客户端更新清单：https://raw.githubusercontent.com/ailuo04/clipbridge/master/latest-v2.json
- 正式发行版：https://github.com/ailuo04/clipbridge/releases
- 自动发布工作流：.github/workflows/release.yml

源码、构建脚本、测试、清单和发行版元数据均保存在同一个 GitHub 仓库，电脑重置后不需要依赖另一份源码仓库。

## GitHub Actions Secret

在仓库 Settings -> Secrets and variables -> Actions 中配置：

- CLIPBRIDGE_UPDATE_PRIVATE_KEY：完整 PEM 私钥，只在 runner 任务期间注入。
- CLIPBRIDGE_ANDROID_KEYSTORE_BASE64：持久 Android 发布 PKCS12 密钥的 Base64 内容。
- CLIPBRIDGE_ANDROID_KEYSTORE_PASSWORD：Android 发布密钥的密码；不得打印或提交。
- GITHUB_TOKEN：GitHub Actions 自动提供，不要手工复制到仓库或脚本参数。

签名私钥还必须在仓库外保留至少两份加密备份。公钥不能恢复私钥，普通用户不需要私钥。

## 发布流程

1. 提交源码、测试和发布说明，确保版本代码和 release-notes-<version>.txt 一致。
2. 先使用 tools/Build-AndroidRelease.ps1 构建并验签 Android APK，再将它的输出目录以 -AndroidDirectory 传给不带 -Publish 的 tools/Publish-GitHub.ps1，验证 Windows Setup、ZIP、Android APK 和 RSA 签名清单。
3. 推送版本标签，例如：

~~~powershell
git tag v0.3.9
git push origin v0.3.9
~~~

4. GitHub Actions 在 Windows runner 上安装固定 SDK 和经过校验的 Inno Setup，运行构建、发布器离线测试和签名验证。
5. 工作流使用 GitHub Contents API 写入版本 ZIP、版本清单、更新说明和 latest-v2.json，上传 Setup.exe 与 ZIP 到 GitHub Release，并进行匿名下载校验。
6. 只有 Setup、ZIP、清单签名、SHA-256、版本和匿名下载全部通过后，Release 和自动更新频道才算完成。

也可以在 Actions 中手动运行工作流，填写版本号和更新说明；正式发布仍必须使用受保护 Secret。

## 0.3.8 迁移边界

0.3.8 及更早客户端仍读取旧的 Gitee 更新地址，不能通过远程清单让其自动切换。0.3.8 用户必须手动安装一次 0.3.9。0.3.9 使用 GitHub Raw 地址和新的 RSA 公钥，0.3.9 之后继续使用同一公钥自动更新。

## 电脑重置后的恢复

1. 重新克隆本仓库。
2. 安装 global.json 指定的 .NET SDK 10.0.403。
3. 运行 tools/Get-BuildTools.ps1 下载并校验 Inno Setup。
4. 从仓库外的加密备份恢复私钥并更新 GitHub Actions Secret；不要把私钥写入仓库。
5. 运行本地无发布构建，确认工作流文件和测试正常，再推送版本标签。

如果签名私钥丢失，不能继续签发当前频道的兼容清单；必须人工安装一次新的密钥迁移版本，并在迁移前后重新备份新私钥。

## 0.4.0 双端发布

正式发布要求 Java 17、Android SDK 36 / Build Tools 36.0.0。工作流先构建签名 APK、验证固定证书与非调试属性，再生成 Windows 安装器、ZIP 和含 android 字段的 RSA 签名清单。三个安装资产下载后均需验证大小和 SHA-256，验证成功后更新频道。

Android 私钥位于仓库之外的持久密钥目录，密码使用当前 Windows 用户 DPAPI 加密保存；另有本机备份。DPAPI 密码文件无法在重装系统或换用户后直接解密，重置电脑前应把解密后的密钥及密码转存至密码管理器和独立加密介质，并重新配置 GitHub Secrets。不要把调试 APK 当作正式附件上传。
