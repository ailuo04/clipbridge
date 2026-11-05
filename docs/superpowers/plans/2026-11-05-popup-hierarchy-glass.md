# 弹窗层级与雾面玻璃胶囊 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 ClipBridge Windows 更新提示、启动错误和配对待确认卡片中统一状态优先层级，采用 A 版低反光雾面玻璃胶囊，同时保持现有业务行为。

**Architecture:** 新增本地绘制的 `GlassStatusCapsule`，通过语义状态与现有 `Ui` 调色板绘制主题化胶囊，并提供可访问状态文本。更新窗口将各状态映射到该控件；启动错误复用更新窗口的短提示布局；配对待确认卡片嵌入同一控件。所有更改限于 Windows UI 和 smoke tests，不触碰更新协议或发布流程。

**Tech Stack:** C# 12、.NET 10 WinForms、GDI+、现有 `Ui`/`VisualDrawing`、Windows UI smoke harness。

---

## 文件结构

- Create: `ClipBridge/Windows/GlassStatusCapsule.cs` — 状态枚举和可复用的高 DPI 自绘胶囊控件；负责轻薄雾面渐变、细亮边、漫射高光、图标/标题/徽标和无障碍描述。
- Modify: `ClipBridge/Windows/UpdateDialog.cs` — 把可用更新、下载/校验/安装、完成、失败、最新版本和检查失败映射到胶囊；短提示按场景隐藏重复信息。
- Modify: `ClipBridge/Windows/Program.cs` — 将启动错误从原生 `MessageBox` 转到 ClipBridge 自绘短提示。
- Modify: `ClipBridge/Windows/Devices/DevicesForm.cs` — 在待配对卡片中使用警告/成功状态胶囊，不改变验证码核对和同意/拒绝逻辑。
- Modify: `tools/UiSmokeTests/UpdateDialogSmoke.cs` — 覆盖胶囊绘制、语义状态、更新状态映射、主题/DPI 和启动/检查提示。
- Modify: `tools/UiSmokeTests/DevicesFormSmoke.cs` — 覆盖配对卡片的状态胶囊、验证码、过期与确认行为。
- Modify: `tools/UiSmokeTests/README.md` — 说明新增截图内容和针对性 smoke 命令。

## Task 1: 先为玻璃状态胶囊添加失败测试

**Files:**
- Modify: `tools/UiSmokeTests/UpdateDialogSmoke.cs`

- [ ] **Step 1: 添加反射测试并确认缺少控件时失败**

在 `UpdateDialogSmoke.Run` 开头加入下列检查；在 `Check(...)` 后遇到缺少类型时立即返回，避免空引用掩盖预期失败：

```csharp
Type? capsuleType = app.GetType("ClipBridge.Windows.GlassStatusCapsule");
Check(capsuleType != null, "glass status capsule exists");
if (capsuleType == null) return;
Type kindType = app.GetType("ClipBridge.Windows.GlassStatusKind")!;
MethodInfo setContent = capsuleType.GetMethod("SetContent")!;
string[][] samples =
{
    new[] { "Info", "软件更新", "发现可用更新", "v0.5.5" },
    new[] { "Success", "检查完成", "已是最新版本", "v0.5.4" },
    new[] { "Warning", "待确认", "确认设备身份", "Pixel 9" },
    new[] { "Error", "检查失败", "暂时无法连接", "" },
    new[] { "Progress", "正在更新", "正在下载更新", "40%" }
};
foreach (string[] sample in samples)
{
    using Control capsule = (Control)Activator.CreateInstance(capsuleType)!;
    capsule.Size = new Size(480, 76);
    setContent.Invoke(capsule, new object[]
    {
        Enum.Parse(kindType, sample[0]), sample[1], sample[2], sample[3]
    });
    Check(capsule.AccessibleRole == AccessibleRole.StatusBar, sample[0] + " capsule has a status role");
    Check(capsule.AccessibleDescription!.Contains(sample[2]), sample[0] + " capsule exposes its conclusion");
    using var image = new Bitmap(capsule.Width, capsule.Height);
    capsule.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
}
```

- [ ] **Step 2: 运行更新 UI smoke 并确认红灯**

Run:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --updates-only
```

Expected: app and smoke harness build; smoke exits non-zero with `FAIL glass status capsule exists` because the control has not been added.

- [ ] **Step 3: 实现最小控件契约并验证绿灯**

Create `GlassStatusKind` with `Info`, `Success`, `Warning`, `Error`, and `Progress`, then implement `GlassStatusCapsule.SetContent(GlassStatusKind kind, string context, string title, string badge = "")`. Set `AccessibleRole.StatusBar`, `AccessibleName` to `状态提示`, and `AccessibleDescription` to the visible context/title/badge. Use `ControlStyles.UserPaint | AllPaintingInWmPaint | OptimizedDoubleBuffer | ResizeRedraw | SupportsTransparentBackColor`; scale the 76-DPI-independent base height, padding, icon and strokes using `DeviceDpi / 96f`.

The `OnPaint` implementation must use `VisualDrawing.RoundedRectangle`, `FillRoundedRectangle`, and `DrawRoundedRectangle`: fill a two-ended capsule with a low-saturation `LinearGradientBrush`, layer a soft white-to-transparent upper highlight and a thin theme-aware rim, then draw opaque text and the semantic icon. Select `Ui.SuccessSoft`/`SuccessText`, `WarningSoft`/`WarningText`, `DangerSoft`/`Danger`, or `AccentSoft`/`Accent` by `Kind`; in dark mode use subtle alpha overlays and preserve `Ui.Ink` contrast. Do not call desktop capture APIs or lower text alpha.

Run the command from Step 2 again. Expected: `PASS glass status capsule exists`、五种状态的角色/描述断言均通过，整个更新 smoke 进程退出 0。

- [ ] **Step 4: Commit component and test**

```powershell
git add .\ClipBridge\Windows\GlassStatusCapsule.cs .\tools\UiSmokeTests\UpdateDialogSmoke.cs
git commit -m "feat: add frosted status capsule control"
```

## Task 2: 在更新窗口与启动错误中统一层级

**Files:**
- Modify: `ClipBridge/Windows/UpdateDialog.cs`
- Modify: `ClipBridge/Windows/Program.cs`
- Modify: `tools/UiSmokeTests/UpdateDialogSmoke.cs`

- [ ] **Step 1: 为每种更新状态和短提示添加失败断言**

扩展 `UpdateDialogSmoke.Run`：每个 light/dark updater 都必须包含 `UpdateStatusCapsule`；调用 `BeginDownload`, `Report(Verifying)`, `Report(Installing)`, `Report(Completed)` 和 `ShowFailure` 后，检查胶囊的可访问描述分别包含当前阶段结论。创建 latest notice 时检查结论仅出现一次；创建 error notice 时检查说明可读且重试/关闭边界保持不变。使用下列 latest 检查，利用现有 `Find`/`Check` helper：

```csharp
Control status = N("UpdateStatusCapsule");
Check(status.AccessibleDescription!.Contains("已是最新版本"), "latest notice exposes one primary conclusion");
Check(!N("UpdateProgressSurface").Visible && !N("UpdateCancelButton").Visible, "latest notice omits irrelevant progress and cancel actions");
```

保留现有下载速度、真实进度、取消、安装时禁止关闭、重试和完成确认断言。

- [ ] **Step 2: 运行更新 UI smoke 并确认新增断言失败**

Run the Task 1 update smoke command.

Expected: build passes; the new first missing-control assertion fails with `FAIL latest notice exposes one primary conclusion` or `Find` reports missing `UpdateStatusCapsule`, demonstrating the updater has not yet been migrated.

- [ ] **Step 3: 用胶囊承载唯一主结论并保留更新状态机**

In `UpdateDialog`, create one docked `GlassStatusCapsule` named `UpdateStatusCapsule` in the hero/status row. Replace repeated headline/status/hint text with `SetContent` calls:

```csharp
_statusCapsule.SetContent(GlassStatusKind.Info, "发现新版本", "让互传更好用", $"v{version} · {SizeText(packageSize)}");
// BeginDownload:
_statusCapsule.SetContent(GlassStatusKind.Progress, "正在下载更新", "为你更新 ClipBridge", $"v{UpdateProtocol.CurrentVersion.ToString(3)} → v{_version}");
// ShowFailure:
_statusCapsule.SetContent(GlassStatusKind.Error, "更新未完成", "可以再试一次", "");
```

In `Report`, set Success for `Completed`, Progress for active stages, and put the current phase plus percentage in context/badge. Keep exact byte count, speed, remaining-time text, release notes, progress bar, step strip, cancellation rules and installer recovery unchanged. Latest notice uses Success and the capsule contains `已是最新版本`; hide the result detail surface when its body would merely repeat that conclusion. Error notice uses Error and preserves unique, actionable details.

在类字段声明中增加 `private readonly GlassStatusCapsule _statusCapsule;` 和 `private readonly string _version;`；构造函数将传入 `version` 保存到 `_version`，实例方法使用 `_version` 而不是构造函数局部参数。将提示工厂签名统一为 `CreateNotice(string title, string message, bool error, string context = "软件更新")`，展示方法统一为 `ShowNotice(IWin32Window? owner, string title, string message, bool error = false, string context = "软件更新")`。所有原调用保持兼容；`owner` 为空时调用无 owner 的 `ShowDialog()`。

- [ ] **Step 4: 替换启动失败原生消息框并验证主题提示**

In the existing `Program.Main` catch block, replace `MessageBox.Show(...)` with:

```csharp
UpdateDialog.ShowNotice(
    null,
    "启动失败",
    "请检查 45821/45822 端口是否被占用，以及当前用户密钥存储是否可访问。\n错误类型：" + ex.GetType().Name,
    error: true,
    context: "应用启动");
```

为扩展签名更新既有反射工厂调用：`noticeFactory.Invoke(null, new object[] { title, message, failed, "软件更新" })`；启动提示使用 `noticeFactory.Invoke(null, new object[] { "启动失败", "端口或用户密钥存储不可用。错误类型：IOException", true, "应用启动" })`。断言其胶囊的可访问描述包含启动上下文及失败结论，且细节保留可读原因。测试不触发真实启动异常、不绑定业务端口、不读真实密钥。Do not include exception messages, key material, or update payloads.

- [ ] **Step 5: 运行更新 smoke、检查布局并提交**

Run:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --updates-only --capture
```

Expected: 更新 smoke 退出 0；captures include latest, check-error, available-update, downloading, installing and failure states; text fits in both themes. Inspect generated PNGs under `%TEMP%\clipbridge-ui-smoke` for the subdued A-style capsule, unique latest-version copy and clear primary action, then commit only the four Task 2 files.

## Task 3: 将配对待确认卡片纳入同一状态层级

**Files:**
- Modify: `ClipBridge/Windows/Devices/DevicesForm.cs`
- Modify: `tools/UiSmokeTests/DevicesFormSmoke.cs`

- [ ] **Step 1: 添加待确认、已确认和过期状态的失败断言**

在现有合成 `PairingRequest` 测试插入请求并调用 `Refresh()` 后，增加断言：

```csharp
Control requestStatus = Descendants(form).Single(control => control.Name == "PairingRequestStatusCapsule");
Check(requestStatus.AccessibleDescription!.Contains("待确认"), "pairing request exposes its pending status");
Check(approve.Enabled && Descendants(form).Any(control => control.Text.Replace(" ", "") == "123456"), "pending code and confirmation action remain visible");
```

在确认和过期步骤后分别检查 capsule 的 Info/Warning 描述与按钮禁用状态。先运行 `dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --devices-only` 并确认因缺少命名控件而失败。

- [ ] **Step 2: 在 `AddRequest` 中替换重复标题行**

在 `DevicesForm.AddRequest` 将请求标题行换成 `GlassStatusCapsule`，命名为 `PairingRequestStatusCapsule`。待确认用 Warning，本端已确认用 Info（不得表示双端配对已完成），拒绝/过期用 Warning；`context` 显示 `待确认`/`已确认，等待对方`/`请求已过期`，`title` 使用设备名称，`badge` 显示平台。下方继续展示居中的数字验证码、地址和有效期；保留原来的 `ConfirmPairing-<session>`/`RejectPairing-<session>` 按钮名称、回调、过期禁用行为和布局刷新逻辑。按胶囊高度增加卡片高度与表格行数时使用 `Pixels(...)`，避免高 DPI 裁切。

- [ ] **Step 3: 运行设备 smoke 并检查两种主题截图**

Run:

```powershell
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --devices-only --capture
```

Expected: `DEVICE WINDOW TESTS PASSED`; `devices-request` 与 `devices-dark` 截图里验证码和动作不被胶囊挤压，过期后确认按钮仍禁用。提交两个 Task 3 文件。

## Task 4: 全量验证、文档和最终提交

**Files:**
- Modify: `tools/UiSmokeTests/README.md`

- [ ] **Step 1: 更新 smoke 文档**

在 README 的 updater checks 说明中注明更新弹窗已验证五种胶囊状态、无重复的 latest 结论、启动错误采用 ClipBridge 主题提示，以及设备 smoke 覆盖配对状态胶囊。保留现有 Windows 交互桌面要求和命令。

- [ ] **Step 2: 运行全量 Windows UI 验证**

Run:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --updates-only --capture
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --devices-only --capture
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --capture
git diff --check
```

Expected: all three smoke invocations exit 0; 设备 smoke 输出 `DEVICE WINDOW TESTS PASSED`，更新 smoke 的全部 PASS 断言通过。逐一打开截图检查两种主题、150% 放大和长错误说明，不以自动断言代替视觉检查。相对实现前保存的基线 diff 检查新增变动仅属于 UI/test/docs；工作区本来就存在协议和发布文件改动，不能把它们归因于本次任务或删除。

- [ ] **Step 3: 提交最终说明**

```powershell
git add .\tools\UiSmokeTests\README.md
git commit -m "test: document hierarchical dialog smoke coverage"
```

执行前将相关文件的基线内容与 `git status --short` 保存到仓库外的临时目录；本计划涉及的部分文件目前包含用户未提交改动，不能直接 `git add` 全文件。以上提交命令仅适用于干净的隔离工作树；如在当前工作树执行，改为只暂存本次任务的差异，无法安全分离时不提交并报告原因。不得复制、撤销或提交已有业务/发布修改；新的 `UpdateDialog` 等未跟踪依赖也属于现有用户基线。规划阶段不创建工作树或搬动这些依赖，实现前再选择隔离策略。
