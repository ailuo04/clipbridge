# ClipBridge 项目发布约定

## 唯一发布目标

- 以后所有正式发布只使用 GitHub，唯一仓库是 https://github.com/ailuo04/clipbridge.git。
- 该仓库同时保存公开源码、构建脚本、测试、GitHub Actions、更新 ZIP、签名清单、更新说明和 GitHub Release。
- 不再把新的源码、标签、安装包、更新清单、Release 或自动发布流程推送到 Gitee。历史 Gitee 版本只作为旧版本兼容记录，不是未来发布渠道。
- GitHub Release 必须包含该版本的 Windows Setup.exe 和 ZIP；只提交文件、只创建标签或只更新 README 都不算完成发布。

## 自动更新和迁移边界

- App 必须保留 RSA 签名清单、安装包 SHA-256、大小校验和 HTTPS 地址校验。不得关闭签名校验、信任任意下载地址或改成无签名更新。
- 0.3.8 及更早版本内置旧的 Gitee 更新地址，不能通过远程修改清单让它自动切换到 GitHub。
- 0.3.8 用户必须手动安装一次 0.3.9 迁移版。0.3.9 内置 GitHub Raw 的 latest-v2.json 地址和新的更新公钥。
- 0.3.9 及以后版本持续使用新公钥和 GitHub 更新清单，后续版本可以自动更新。普通用户不需要发布密钥。
- 更新私钥丢失时，不得伪造旧签名、覆盖已发布版本或关闭签名校验；只能发布一次需要人工安装的密钥迁移版本。

## 密钥和权限

- 更新私钥使用环境变量 CLIPBRIDGE_UPDATE_PRIVATE_KEY 注入 GitHub Actions，只能写入随机临时文件，任务成功或失败都必须删除。
- GITHUB_TOKEN 由 GitHub Actions 提供，用于提交公开发布文件和创建 Release；令牌和私钥不能进入源码、日志、命令行参数、压缩包或聊天记录。
- 公钥必须提交到 release-signing-public-key.txt，并作为应用嵌入资源；公钥不能恢复私钥。
- 私钥必须在仓库外至少保留两份加密备份，例如密码管理器和加密介质。GitHub Secret 不是唯一备份。
- 电脑重置后先恢复 GitHub 账号、仓库访问权和 Secret，再继续发布；普通开发和普通用户安装都不需要私钥。

## 电脑重置后的恢复流程

1. 重新克隆 https://github.com/ailuo04/clipbridge.git，源码和发布工作流都在同一个仓库。
2. 按 global.json 安装 .NET SDK 10.0.403，运行 tools/Get-BuildTools.ps1 获取并校验 Inno Setup。
3. 用 tools/Publish-GitHub.ps1 在不带 -Publish 的情况下先完成本地构建、签名和包内容验证。
4. 将仓库外的加密私钥备份恢复到 GitHub Actions Secret CLIPBRIDGE_UPDATE_PRIVATE_KEY；不要把私钥复制进仓库。
5. 正式发布使用 GitHub Actions 的 tag 触发或 workflow_dispatch；发布工作流负责构建、签名、提交清单、上传资产和创建 Release。
6. 发布后确认 GitHub Raw latest-v2.json、版本 ZIP、Release Setup.exe、哈希、签名和匿名下载均通过验证。

## 发布不变量

- 版本号使用 v<major>.<minor>.<patch>，版本化 ZIP、manifest-<version>.json 和 release-notes-<version>.txt 一旦发布禁止覆盖。
- latest-v2.json 必须与版本清单字节一致，payload 必须由当前私钥签名，公钥验证必须通过。
- 更新清单中的 ZIP URL 必须指向 GitHub Raw master，installerUrl 必须指向同版本 GitHub Release 资产。
- 发布前必须检查应用程序集版本、ZIP 文件列表、运行时版本、文件大小、SHA-256、安装器资产、清单签名和匿名下载结果。
- 正式发布入口默认只构建和验证；只有 GitHub Actions 注入 GITHUB_TOKEN、CLIPBRIDGE_UPDATE_PRIVATE_KEY 并显式执行发布时，才允许修改远程发布内容。

## 工具和测试

- tools/Prepare-Release.ps1 负责构建安装器、生成 ZIP 和签名清单。
- tools/Verify-Release.ps1 负责离线验证签名、版本、URL、哈希、ZIP 内容和运行时。
- tools/Publish-GitHub.ps1 是统一入口；本地不带 -Publish，云端由 .github/workflows/release.yml 调用 -Publish。
- tools/publish_github.py 负责 GitHub Contents、Release 资产和匿名下载的最终校验；失败时不得自动覆盖已发布版本。
- 修改更新协议、发布脚本或工作流后，至少运行 dotnet build、两个 smoke test 项目构建和 tools/test_publish_github.py。
- 原版安装器没有 Authenticode 代码签名；构建成功不等于完整安装、原位升级、真实网络更新、剪贴板和局域网业务已经验证。
