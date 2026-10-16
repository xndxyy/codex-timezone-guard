# 发布状态与来源

更新：2026-10-16（Asia/Shanghai）。

## 已公开发布并完成下载校验

- 公开源码：[xndxyy/codex-timezone-guard](https://github.com/xndxyy/codex-timezone-guard)，默认分支 main。
- 预发布：[v0.1.0-preview.1](https://github.com/xndxyy/codex-timezone-guard/releases/tag/v0.1.0-preview.1)。API 确认 prerelease=true、draft=false。
- 冻结来源/标签提交：`b9faea237609f01f4db58c7f4d99b6c0f16cce17`。
- 最终本地构建时间：2026-10-16 05:51:21 UTC。
- 远端完整校验时间：2026-10-16 05:56:39 UTC（北京时间 13:56:39）。

最终构建前工作树干净，收据 sourceCommit 与远端标签提交相同。发布后本记录、
目标/路线/状态的文档更新可以位于后续 main 提交；**不会悄悄替换已发布二进制或移动标签**。
Release 内的验证文档是冻结时版本，发布后事实以本记录及公开证据补充。

## 资产与 SHA-256

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| CodexTimezoneGuard-win-x64.exe | 51,710,799 | `7575733834382b2d79dfaa63ba36055232ccc38702c8281e7b3d5db5bb2c8e10` |
| CodexTimezoneGuard-v0.1.0-preview.1-win-x64.zip | 46,395,403 | `bda2a063c1b81e3ba3f7a44255674429c27e41f769e699d7bfddbc312e5760d1` |

共 7 个资产：EXE、ZIP、SHA256SUMS.txt、build-receipt.json、tests.json、
packaged-self-test.json、verification.md。逐个核对 GitHub 资产 uploaded 状态、大小与
服务器 SHA-256 digest，再**不带 Authorization 凭据**从公开下载链接实际下载，
本地重新计算 SHA-256。**7/7 均与本地冻结资产一致**。下载文件未被再次执行。

完整无敏感信息的机器可读记录见
[remote-release-verification.json](evidence/remote-release-verification.json)。

## 独立 GitHub Windows 构建

[Windows verification #38858021914](https://github.com/xndxyy/codex-timezone-guard/actions/runs/38858021914)
在相同冻结提交完成，conclusion=success。实际日志确认 98/98 逻辑测试、Release
编译 0 Warning(s)/0 Error(s)，并完成同一打包/EXE 自检脚本与资产上传。
简明 API 记录见 [ci-release-run.json](evidence/ci-release-run.json)。

非阻断提示：GitHub 对固定版本的 checkout/upload-artifact Action 给出 Node 20
弃用提示，运行器已按平台策略使用 Node 24；本次作业成功。没有为消除提示而在
发布后未经审查地替换工作流依赖。

## 仍未验证

真实 Codex/Store 新进程与 Date/Intl 日期运行时、Windows 10、干净虚拟机兼容性
仍未验证，不因公开发布、自动测试或 CI 成功而改变。详见
[verification.md](verification.md) 和 [manual-acceptance.md](manual-acceptance.md)。
