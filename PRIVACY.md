# 隐私说明 / Privacy

## 本地处理

本工具不读取 Codex 登录令牌、聊天记录、项目源码或账号密码，不代理或解密
Codex 的对话流量。客户端发现只读取安装路径、包/桌面身份与运行进程标识。
不会收集遥测，不向作者发送信息。

本地设置保存所选 IANA 时区、自动/手动模式、客户端标识和首次联网确认等必要
信息，不保存公网 IP、代理密码或完整环境变量。默认目录为
`%LOCALAPPDATA%\CodexTimezoneGuard`。开发与测试可通过本进程的
`CODEX_TZ_GUARD_DATA_DIR` 重定向。删除设置目录即可清除本工具的持久设置；
请勿删除 Codex 自己的数据目录。

## 可选的自动检测

在首次联网说明得到确认后，自动检测会发送匿名 HTTPS 请求到：

| 接收方 | 用途 | 接收的信息 |
|---|---|---|
| `chatgpt.com/cdn-cgi/trace` | 观察目标域名请求的出口 | 正常 HTTPS 请求与该连接的出口 IP |
| `api.openai.com/cdn-cgi/trace` | 可用时交叉检查目标出口 | 正常 HTTPS 请求与该连接的出口 IP |
| `ipwho.is/<探测到的公网IP>` | 把明确 IP 映射为地区时区 | 查询的公网 IP 和查询连接自身的出口 IP |

定位服务不是 OpenAI，也不是本项目作者运营的服务。使用自动检测意味着这些
服务会收到上述必要网络信息。我们不附加认证 Cookie、Authorization、Windows
登录凭据或聊天数据；不接受重定向，不关闭 TLS 验证。免费服务可能限流或不可用。

你可以拒绝联网并使用**手动时区**。公网 IP 仅用于本轮检测和界面展示，普通设置
与诊断报告不持久化原始 IP。系统代理/PAC 只读解析；不更改模式、规则、节点或
系统网络设置。代理服务本身的日志/隐私政策由用户所用服务决定。

## 诊断与反馈

复制/导出的诊断应为脱敏状态、错误类别和时区等信息，不应包含原始公网 IP、
代理认证信息、Codex 令牌或完整环境。如果提交 Issue，请再次人工检查诊断，
不要上传账号目录、代理订阅、配置密钥、聊天截图或含个人信息的文件。

## EXE 的运行时文件

“单文件 EXE”指分发形式，不代表完全不落盘。自带的 .NET 原生运行时可能解包
到用户临时目录；设置位于上文目录。没有后台服务、开机自启或自动更新组件。

## English summary

No telemetry, Codex auth/chat access or conversation interception. Optional
detection sends anonymous requests to the two fixed target trace endpoints and
an explicit observed IP to `ipwho.is`. Manual mode is available without these
requests. Raw public IPs and proxy credentials are not persisted by default.
Settings and runtime extraction are local. System timezone, global environment
and proxy configuration are not modified.
