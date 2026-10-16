# Codex Timezone Guard 0.1.0-preview.1

首个公开预发布：一个中文 Windows Codex 桌面端时区启动器。

**请先看边界：真实 Codex/Store 新进程启动和 Date/Intl 采用所选时区尚未完成安全的人工验收，所以此版本不是“全部渠道已验证”的稳定版，也不是 OpenAI 官方工具。**

## 下载与使用

- 普通用户下载 `CodexTimezoneGuard-win-x64.exe`，无需自行安装 .NET。
- ZIP 包含同一 EXE、许可证、隐私/安全说明和离线验证报告。
- 使用 `SHA256SUMS.txt` 校验下载文件；EXE 未作商业代码签名，不要关闭安全软件来运行来源不明的文件。
- 保存工作并正常退出 Codex，再选择自动建议或手动 IANA 时区，确认客户端并启动。

## 本版功能

- 只给新启动的 Codex 和它的后代设置 `TZ`，不修改 Windows 时区、系统/用户环境。
- 目标域名探测优先，明确 IP 地理查询与回显核对；重复/不同目标出口矛盾时转手动确认。
- 只读系统 HTTP/HTTPS 代理、PAC 和现有 TUN；显式代理失败不静默直连，不修改规则、节点或全局模式。
- 离线手动时区、夏令时预览、中文界面、检测取消、过期结果保护、脱敏诊断。
- 桌面/Store 身份与当前版本发现，同名 CLI 和独立 ChatGPT 产品不作为 Codex。
- 已运行或无法检查的 Codex 实例会阻止启动，不强制结束客户端；包含跨安装渠道/路径保护。
- 创建/验证/清理新进程时暂时阻止正常关闭启动器，完成后恢复；网络检测仍可取消。
- self-contained Windows x64 单文件分发；实际启动时可能解包平台运行时文件。

## 验证与限制

确定性测试、独立环境探针、离线表单自检、真实匿名网络观测和真实 Codex 运行时是不同证据层级。测试数量、实际结果和人工验收步骤见仓库的 `docs/verification.md` 和 `docs/manual-acceptance.md`。

本地最终代码已执行 **98 项逻辑/隔离测试、16 项构建脚本契约检查、14 项打包后离线自检**，全部通过，Release 编译 0 警告/错误；这些不等于真实客户端验收。

真实网络记录中保留了“可形成建议”和“出口观测冲突”两类结果，不只挑成功案例。探测由启动器而不是 Codex 发起，不能证明按进程分流或共享后台的实际路径。免费定位服务可能限流、离线或定位不准确。

Codex 内启动的终端、Python、Node 等可能继承 `TZ`；已存在的共享 daemon 可能不继承新值，直接使用 Windows 原生时区 API 的组件仍可能采用系统值。首版不热切换、不自动重启、不常驻托盘、不含自动更新或遥测。Windows 10 与其他架构未实机验证，SOCKS-only 配置不受支持。

## English

Initial independent Windows x64 prerelease. Launch a validated Codex desktop
client with a process-local IANA `TZ`, with destination-aware suggestions or
offline manual selection. Windows/global environment and proxy rules remain
unchanged; descendants may inherit `TZ`. Running/uncertain desktop instances are
blocked, not terminated. Real Codex/Store launch and date-runtime acceptance
remain unverified and require a safe user-controlled check.
