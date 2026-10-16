# 来源与技术调研记录

调研日：2026-10-15（America/Phoenix）。这是源码/文档调研，不是运行成功或真实 Codex 兼容性证明。

## 开源参考

- [iUshio/codexTimeZoneRev](https://github.com/iUshio/codexTimeZoneRev)，读取时 main 提交 `799476432336501c1c7f7438f2ef377605029cfc`。MIT，原 LICENSE 版权行为 `Copyright (c) 2026 Fei-Away`。
  - `native/launcher_core/src/platform/win/process.rs`：仅替换新进程环境中的 TZ，移除 ELECTRON_RUN_AS_NODE，挂起创建与包身份校验。
  - `native/launcher_core/src/proxy.rs`：只读系统代理/PAC 解析，显式 PAC 失败不静默直连。
  - `flutter_app/lib/services/network_info.dart`：多个一般 IP 源与指定 IP 地理定位；可参考取消、响应解析与缓存，但不能将此流程当成实际 Codex 出口证据。
  - 首版排除其皮肤、账号用量、管理员 ACL 修复等无关功能。
- [leihen08/codex-timezone-launcher](https://github.com/leihen08/codex-timezone-launcher)：轻量 Windows 原生启动器。README 区分 TZ/Node 运行时、Windows 原生时区与任务上下文，并报告子进程继承边界。本项目不复制其临时包调试注册流程。

## 官方文档

- [.NET 单文件部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)：self-contained 可免装运行时，但体积较大；IncludeNativeLibrariesForSelfExtract 可把原生库打包，启动时可能解包；构建后需实测。
- [ProcessStartInfo.Environment](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment?view=net-10.0)：对子进程设置环境时需要 UseShellExecute=false；不是用户/系统环境修改。
- [Cloudflare /cdn-cgi endpoint](https://developers.cloudflare.com/fundamentals/reference/cdn-cgi-endpoint/)：trace 为域名相关诊断端点，不是稳定的 Codex API；colo 表示数据中心，不能用于推断用户时区。
- [IPWhois 官方文档](https://ipwhois.io/documentation)：支持按显式 IPv4/IPv6 查询、HTTPS 与 timezone.id；免费端点存在限额且无 SLA。这里不承诺额度长期不变，程序应处理 429/Retry-After。

## Context7 查询

使用当前 Context7 CLI，先 library 再 docs；本轮共 3 条，不包含任何秘密：

1. `library "Microsoft .NET" "Windows Forms self-contained single-file deployment and child process environment variables ProcessStartInfo.Environment"`
2. `docs /dotnet/docs "Deploy a Windows Forms application as a Windows x64 self-contained single executable with PublishSingleFile IncludeNativeLibrariesForSelfExtract; limitations trimming Windows Forms"`
3. `docs /dotnet/docs "ProcessStartInfo.Environment child-process-only environment variables UseShellExecute false Windows inherit environment no global changes"`

查询返回单文件配置、Windows Forms 属性以及子进程环境信息；对于未回答的限制不从片段推导已验证兼容性，采用官方完整页面进一步核对。设计调研当时 SDK 为 10.0.400，存在 Windows Desktop 引用包；当时尚未编译或运行本项目。

## 初始设计阶段尚无的证据（历史记录，不是当前状态）

- 没有本项目真实公网探测结果或保存的原始 IP。
- 没有本项目应用代码、测试通过记录、EXE 或真实 Codex 运行时验证。
- 没有已创建的项目远端仓库或 Release。
- 上一轮发现了同名 Codex CLI/后台路径，不应据此认定桌面安装路径或版本。

## 实现后的证据索引

实际实现、构建、测试和公网结果见 verification.md 及 evidence/ 中的脱敏快照。原始公网 IP 没有保存。已经观察到成功建议和出口冲突两种结果，不能只宣传其中一个；真实 Codex 日期运行时仍需用户安全退出后的独立验收。

2026-10-16 的 GitHub CLI Context7 library 解析取得 `/websites/cli_github_manual`；后续两项 docs 调用被会话中断，未计为已读文档。发布命令参数改以本机官方 `gh repo create --help`、`gh release create --help` 和 `gh release verify-asset --help` 的实际输出核实，不把未返回的文档或训练记忆当作引用。校验采用下载资产后的 SHA-256 比对，不冒充代码签名/已验证的发布证明。
