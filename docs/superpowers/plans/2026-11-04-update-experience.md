# Update Experience Implementation Plan

**Goal:** Windows 更新进度可视化、玻璃弹窗与更新后自动打开。
**Architecture:** UpdateProgress 传递阶段和真实工作量；UpdateProtocol/SelfUpdate 报告进度；UpdateDialog 渲染；TrayAppContext 协调生命周期；独立助手确认重启后清理。
**Tech Stack:** .NET 10 WinForms，现有 Ui/ModernButton，隔离烟测。

- [x] 添加失败回归：助手必须携带 --updated 和启动确认事件；更新窗口必须存在；下载必须报告真实进度。
- [x] 添加 UpdateProgress；在下载、校验、解压、替换和重启路径报告进度；加入取消与启动确认/回滚测试。
- [x] 实现玻璃 UpdateDialog 与进度绘制；接入托盘和独立助手；更新后显示控制面板。
- [x] 构建主项目和两个 smoke 项目，运行更新、UI 和发布回归，检查浅深色截图、DPI、取消重试及差异。

执行范围仅限本地改动，不提交、不推送、不发布。保留工作区已有设备配对和发布相关改动。

验证记录：主项目 Release 构建通过；两个 smoke 项目构建通过；下载/校验/取消/解压/交接清理回归通过；独立助手启动确认与失败回滚通过；更新窗口浅深色、主题切换与 1.5 倍布局通过；完整既有 UI 和 TransferTests 通过；test_publish_github.py 的 10 项离线测试通过；安装权限策略检查通过。

限制：UI 放大使用控件缩放模拟，尚未测试真实多显示器 DPI 切换。未执行真实安装目录更新、真实 GitHub 网络升级或正式发布。
