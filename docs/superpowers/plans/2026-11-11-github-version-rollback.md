# GitHub 历史版本回退实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **执行说明：** 按任务逐步实现，每项先补充/运行对应 smoke test，再实施最小修改并验证。不得重置、覆盖或顺手整理工作区中已有的未提交改动。

**目标：** 在 ClipBridge Windows 侧栏和设置页提供清晰的更新状态及 GitHub 历史版本回退，并且只安装通过签名与完整性验证的旧版本。

**架构：** 保持最新版自动更新与用户主动回退为两种不同的策略。`UpdateProtocol` 负责已签名清单的校验；独立 Release catalog 负责分页读取 GitHub Releases 和解析历史候选；`WorkspaceForm` 负责呈现控件；`TrayAppContext` 协调网络任务、确认和现有自更新安装助手。回退复用相同的下载、哈希校验、暂存和启动失败恢复链路，不允许后台自动降级。

**技术栈：** C# 12、.NET 10、Windows Forms、`HttpClient` / `System.Text.Json`、RSA-SHA256、现有 Update/UI smoke test 项目。

**设计稿：** `docs/superpowers/specs/2026-11-11-github-version-rollback-design.md`

## 全局约束
- 本轮仅修改 Windows UI 和 Windows 更新流程；不改变 Android 更新体验、正式发布流程、发布密钥或签名协议；不执行发布，也不改变普通自动更新行为。
- 所有回退候选必须来源于 GitHub Release 标签，并且通过该版本的签名清单与包完整性校验。

- 仅使用固定仓库 `ailuo04/clipbridge` 的 GitHub Releases 和其对应版本清单；不回到 Gitee。
- 普通自动更新只允许升级；只有用户明确选择并确认时才能安装旧版本。
- 回退目标必须通过应用内 RSA 公钥、标签/清单版本一致性、HTTPS 固定仓库/路径校验、大小、SHA-256 和现有 ZIP 安全解压校验。
- GitHub Release API 的标签及下载资产信息不能替代签名清单，API 限流/错误必须失败关闭。
- 有正在进行的发送或文件传输时，不开始安装；继续用现有启动确认和失败恢复流程。
- 不修改 Android 更新、签名密钥、发布流程或无关功能；不丢弃工作区已有更改。
- 任何更新协议修改完成后，按项目约定至少构建主项目、UI smoke test、更新 smoke test，并运行 `tools/test_publish_github.py`。

---

### 任务 1：拆分清单签名验证与目标版本策略

**文件：**
- 修改：`ClipBridge/Windows/UpdateProtocol.cs`
- 修改：`ClipBridge/Windows/AvailableUpdate.cs`
- 测试：`tools/UpdateSmokeTests/UpdateExperienceSmoke.cs`
- 测试：`tools/UpdateSmokeTests/Program.cs`

**接口：**
- 保持 `CheckAsync(CancellationToken)` 和现有普通更新调用兼容。
- 增加历史目标验证入口：`ParseAndVerifyRollback(string envelopeJson, string expectedVersion, Version currentVersion)`。返回已验证的 `AvailableUpdate`；无效签名、版本不一致、非旧版本或不受信任 URL 时抛 `UpdateProtocolException`。测试从仓库根目录读取 `manifest-0.5.0.json`，以当前项目版本 `0.5.6` 验证：`var update = UpdateProtocol.ParseAndVerifyRollback(File.ReadAllText("manifest-0.5.0.json"), "0.5.0", new Version(0, 5, 6));`，再逐项断言元数据。
- 给 `AvailableUpdate` 增加可选的回退意图标志（默认 `false`），保证现有调用方无需改变语义；所有现有构造调用仍按默认值走普通升级。

- [ ] **步骤 1：先补回归测试。** 用仓库里已有的签名历史清单（例如 `manifest-0.5.0.json`）验证其作为低于测试当前版本的历史目标可解析；断言版本、URL、大小和哈希来自已签名载荷，并在同一步为 `Program.cs` 增加 `--verify-manifest` 路由。
- [ ] **步骤 2：运行更新 smoke test 并确认新用例失败。**

```powershell
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-manifest
```

预期：编译失败，提示 `ParseAndVerifyRollback` 尚不存在；测试命令已由步骤 1 注册到专用 smoke 方法。
- [ ] **步骤 3：新增无副作用的历史验证入口。** 把“验签和解析 Windows 包元数据”与“普通更新必须高于当前版本”的判定分开；普通 `CheckAsync` 仍调用升级策略；回退入口额外要求目标版本严格小于 `CurrentVersion` 且等于所选标签版本。
- [ ] **步骤 4：验证历史 URL。** 对回退包强制 HTTPS，并逐字段匹配 `raw.githubusercontent.com/ailuo04/clipbridge/master/ClipBridge-<expectedVersion>-win-x64.zip`；拒绝主机、仓库、分支、文件名或版本不符的 URL。普通自动更新既有 URL 行为本任务不扩大改动。
- [ ] **步骤 5：补充拒绝用例并重跑测试。** 覆盖篡改签名、错误清单版本、当前/更高版本、非 HTTPS、错误主机/仓库/路径、大小上限和 SHA-256 格式错误；确认普通 `ParseAndVerify` 对相同/旧版本仍返回无可用更新。

**验收：** 历史清单可被单独安全验证；没有任何共享解析路径能令自动检查接受降级。

---

### 任务 2：分页读取 GitHub Releases 并生成可回退候选

**文件：**
- 新建：`ClipBridge/Windows/GitHubReleaseCatalog.cs`
- 修改：`ClipBridge/Windows/UpdateProtocol.cs`（仅调用签名清单验证入口）
- 修改：`tools/UpdateSmokeTests/UpdateSmokeTests.csproj`（链接新源文件）
- 修改：`tools/UpdateSmokeTests/Program.cs`
- 新建或修改：`tools/UpdateSmokeTests/GitHubReleaseCatalogSmoke.cs`

**接口：**
- `GitHubRollbackCandidate(string TagName, DateTimeOffset? PublishedAt, bool IsPrerelease, AvailableUpdate? Update, string? UnavailableReason)`。
- `GitHubReleaseCatalog.LoadAsync(Version currentVersion, CancellationToken cancellationToken)` 返回按版本号从新到旧排列的旧版候选。`Update == null` 表示该 Release 仍可显示但不可回退，`UnavailableReason` 提供简短原因。
- 正式构造固定使用 `https://api.github.com/repos/ailuo04/clipbridge/releases?per_page=100` 与 `https://raw.githubusercontent.com/ailuo04/clipbridge/master/manifest-<version>.json`；通过内部注入 `HttpMessageHandler` 做确定性测试，不提供运行时 URL 覆盖配置。

- [ ] **步骤 1：写假 HTTP handler 测试。** 构造两页 Release JSON；首页 `Link: <...page=2>; rel="next"`；包含草稿、预发布、当前/较新版本与旧版本。旧版本 Raw 清单由签名 fixture 返回；同时在 `Program.cs` 注册 `--verify-catalog` 到该用例。
- [ ] **步骤 2：先运行测试确认失败。**

```powershell
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-catalog
```

预期：失败，因为 catalog 类型和命令尚不存在。
- [ ] **步骤 3：实现固定来源的 API 分页读取。** 请求头设置产品 `User-Agent` 和 GitHub JSON `Accept`；逐个跟随有效的 `Link rel="next"`，仅接受官方 API 同仓库分页 URL；每个请求设置 15 秒超时、释放 response，并将 Release 页响应限制为 2 MiB、历史清单限制为现有的 1 MiB；检查状态码与取消；不设置会静默遗漏旧版本的固定总页数上限。
- [ ] **步骤 4：解析 Release 标签及排序。** 排除草稿；显示预发布标记；仅把 `v<major>.<minor>.<patch>` 且严格低于当前版本的标签当作回退目标；同一版本去重并按 `Version` 降序排列，不使用字符串排序。
- [ ] **步骤 5：逐候选获取固定 Raw 历史清单。** 使用任务 1 的 `ParseAndVerifyRollback(envelopeJson, tagVersion, currentVersion)` 校验。清单 HTTP 404、格式错误或验签失败的旧版保留为禁用候选并附原因；API 403/429、5xx、超时或取消按整次列表加载失败处理并允许 UI 重试。
- [ ] **步骤 6：覆盖边界后重跑。** handler 测试至少断言多页遍历、无下一页时结束、草稿排除、预发布标识、版本排序、不可回退原因、限流/非成功状态、错误分页主机拒绝、取消传播以及缺少签名清单时不返回可安装目标。

**验收：** 候选发现只依据固定 GitHub 仓库；每个可选版本都持有匹配标签的已签名 Windows ZIP 元数据；未经验证版本始终禁用。

---

### 任务 3：整理侧栏更新状态和设置入口

**文件：**
- 修改：`ClipBridge/Windows/WorkspaceForm.cs`
- 修改：`ClipBridge/Windows/TrayAppContext.cs`（同步状态生命周期）
- 修改：`tools/UiSmokeTests/ConversationSmoke.cs`

**接口：**
- 增加 `SetUpdateStatus(UpdateSidebarState state, string? detail = null)` UI 状态入口和 `internal enum UpdateSidebarState { Latest, Checking, Failed, Available, Installing }`；保留现有 `SetUpdateAvailable` / `SetUpdateCheckState` 作为兼容包装并转发到该入口；`TrayAppContext` 改用统一状态入口。有新版仍通过已验证 `AvailableUpdate` 的回调执行。

- [ ] **步骤 1：补 UI smoke 断言。** 确认更新控件位于“接收文件夹”上方；底部导航为更新、接收文件夹、设置；测试控件唯一标识和层级间距；检查浅色/深色主题。
- [ ] **步骤 2：补交互状态测试。** 分别触发“当前为最新版”“检查中”“检查失败可重试”“发现版本 + 立即更新”，验证文字、可点击/禁用状态及无误报状态。
- [ ] **步骤 3：实现侧栏状态控件。** 用现有圆角控件样式做椭圆形最新版状态；将日常设置/文件夹导航与更新状态分组。更新状态在接收文件夹上方，最新版控件可点击重新检查，有新版显示版本和立即更新操作。
- [ ] **步骤 4：实现状态重置规则。** 成功无新版时回到最新版；异常时显示失败和重试；检查/安装时禁用重复触发。状态变化不能只改按钮文字而保留旧有版本操作。
- [ ] **步骤 5：在设置主题选项下方放备用“检查更新”按钮。** 按钮沿用同一回调及运行状态；不创建第二条检查逻辑。
- [ ] **步骤 6：运行 UI smoke。**

```powershell
dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release
```

**验收：** 所有状态可访问、主题一致、窗口最小尺寸下不溢出，且两个检查入口调用同一个事件。

---

### 任务 4：在设置页显示并选择历史版本

**文件：**
- 修改：`ClipBridge/Windows/WorkspaceForm.cs`
- 修改：`ClipBridge/Windows/TrayAppContext.cs`
- 修改：`tools/UiSmokeTests/ConversationSmoke.cs`

**接口：**
- `WorkspaceForm` 接收两个回调：启动加载历史版本列表、用户确认后启动所选 `AvailableUpdate` 的回退流程。
- 增加 UI 线程安全的 `ShowRollbackCandidates(...)` / `SetRollbackLoading(...)`，显示 catalog 候选及禁用原因。

- [ ] **步骤 1：先补 UI 测试。** 断言回退入口位于设置页主题和备用检查按钮之后；触发后有加载态；候选按已排序数据显示版本、发布日期、预发布标记；无效候选禁用并展示原因。
- [ ] **步骤 2：实现历史版本列表页面。** 复用现有 `ShowUtility`、`UtilitySection`、主题、按钮和状态文案模式；加入返回设置和重试入口；避免在离开页面后迟到的请求覆盖其它页面。
- [ ] **步骤 3：实现选择确认边界。** 点击可用候选仅打开回退确认，不立即下载；确认文字包含当前版本与目标旧版本；取消后保持当前版本且不触发网络包下载。
- [ ] **步骤 4：接入加载回调。** `TrayAppContext` 加载完成后通过 UI dispatch 更新候选；失败/限流时停止 loading，显示错误和重试；页面销毁或应用退出时取消本次 token。
- [ ] **步骤 5：运行 UI smoke 与浅/深色截图检查。**

```powershell
dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --conversation-only
```

**验收：** 列表响应式、可访问、可取消/重试；只有显式确认的有效旧版本会进入回退执行回调。

---

### 任务 5：连接手动回退与现有更新安装助手

**文件：**
- 修改：`ClipBridge/Windows/TrayAppContext.cs`
- 修改：`ClipBridge/Windows/UpdateDialog.cs`
- 修改：`ClipBridge/Windows/UpdateCardSurface.cs`
- 修改：`tools/UiSmokeTests/UpdateDialogSmoke.cs`
- 修改：`tools/UpdateSmokeTests/UpdateExperienceSmoke.cs`

**接口：**
- 为手动回退单独添加流程入口，目标必须是 `AvailableUpdate.IsRollback == true`，且任务 2 返回的已验证候选。
- `UpdateDialog(string version, string notes, long packageSize, bool applying = false, bool isRollback = false)` 明确接收更新意图；`UpdateCardSurface` 保留现有三参数 `ConfigureAvailable` 包装，并新增 `ConfigureAvailable(string version, string notes, long packageSize, bool isRollback)`。普通升级默认值保持不变，原有弹窗构造调用继续兼容。

- [ ] **步骤 1：补弹窗测试。** 用现有反射 smoke 测试分别创建升级与回退确认窗；回退确认、下载、安装中、失败重试、完成提示和辅助技术描述均显示目标版本及“回退”语义；升级继续显示原文案。
- [ ] **步骤 2：加入单任务/重入保护。** 列表加载、最新版检查、升级安装和回退安装互不重复启动；同一时刻只能有一个版本替换流程。
- [ ] **步骤 3：回退点击先二次确认。** 校验所选候选非空、低于 `CurrentVersion`、版本标记为回退且来自已验签清单；不满足时拒绝启动并显示错误。
- [ ] **步骤 4：复用 `DownloadAndInstallUpdateAsync` 主链路。** 按选定 `AvailableUpdate` 下载，继续校验长度、SHA-256、ZIP 安全路径；沿用活动传输拦截、取消、暂存清理、helper、自动重启和启动确认。状态与提示在回退期间显示目标版本，不能说“安装新版”。
- [ ] **步骤 5：加入失败恢复回归。** 确认无效包、取消、活动传输和 helper 启动失败不导致安装目录被降级；启动目标失败后原版本会恢复。
- [ ] **步骤 6：运行更新 dialog 和经验 smoke。**

```powershell
dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --updates-only
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-experience
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-helper
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-rollback
```

**验收：** 自动更新策略未变；回退必须经过列表选择和用户确认；替换失败时恢复行为仍通过隔离目录 smoke。

---

### 任务 6：集成验证与改动审阅

**文件：** 无新增产品代码；仅修复上述任务引出的失败用例。

- [ ] **步骤 1：检查改动边界。** `git status --short` 对照任务文件，确认其它既有未提交文件未被重置、格式化或混入改动。
- [ ] **步骤 2：运行主项目构建。**

```powershell
dotnet build ClipBridge.csproj -c Release --nologo
```

- [ ] **步骤 3：构建 UI smoke 和更新 smoke 项目。**

```powershell
dotnet build tools/UiSmokeTests/UiSmokeTests.csproj -c Release --nologo
dotnet build tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release --nologo
```

- [ ] **步骤 4：运行 UI、协议/下载、安装助手、启动失败恢复测试。** 使用前述任务中的 smoke 命令；另外运行：

```powershell
python tools/test_publish_github.py
```

- [ ] **步骤 5：报告验证边界。** 区分本地假 HTTP/API 测试与真实 GitHub 网络测试；未实际执行的真实安装目录回退、安装器权限、真实网络下载不得声称已验证。

**设计覆盖自检：** 侧栏层级/椭圆最新版/失败重试由任务 3–4 覆盖；设置备用检查与历史列表由任务 3–4 覆盖；GitHub 分页/草稿/预发布/标签排序/签名清单由任务 1–2 覆盖；确认、回退文案、活动传输拦截和失败恢复由任务 4–5 覆盖；自动升级不降级及发布约束由任务 1、5、6 覆盖。

**最终验收：** 需求 UI 和回退流程都覆盖；RSA/HTTPS/大小/SHA-256 不变量保留；自动更新不降级；所有相关测试报告真实结果。
