using System.Runtime.InteropServices;
using System.Text.Json;
using CodexTimezoneGuard.Core;

namespace CodexTimezoneGuard;

internal static class AppVersion
{
    internal static string Value => System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "unknown";
}

internal static class SafeMessages
{
    internal static string ForCode(string code) => code switch
    {
        "conflicting_egress" => "重复探测或不同目标观察到不同出口，必须手动确认。",
        "pac_failed" => "系统 PAC/自动代理解析失败；为避免错误出口，没有降级直连。",
        "proxy_config_unavailable" => "无法读取当前用户系统代理配置。",
        "proxy_unsupported" => "代理配置不支持（SOCKS-only、含凭据或格式不明确）；没有静默直连。",
        "already_running" => "检测到同一客户端/包的现有实例。请保存工作并自行退出后重试，本工具不会结束它。",
        "running_check_uncertain" => "某个可能匹配的进程不可检查，已安全停止。请自行确认已退出；不要为此提升权限。",
        "invalid_desktop_identity" => "无法重新验证 Codex 桌面身份，可能是 CLI、路径失效或资源不完整。",
        "client_ambiguous" => "存在多个客户端候选，不能擅自选择。",
        "store_not_registered" => "当前用户没有唯一可验证的 Codex Store 包，请刷新或重新定位。",
        "store_identity_failed" => "新建挂起进程的 Store 包/AUMID 不符合预期，仅清理了本工具尚未执行的子进程。未修复权限。",
        "store_create_failed" or "store_resume_failed" => "Store 安全启动失败；此安装渠道尚未实测，不会改 ACL、注册包调试器或自动提权。",
        "process_create_failed" => "子进程创建失败；没有结束任何现有 Codex。",
        "settings_save_failed" => "设置保存失败，没有报告已保存。请检查启动器数据目录。",
        "settings_reset_failed" => "启动器设置删除失败；未影响 Windows 或 Codex。",
        "request_timeout" => "匿名请求/代理解析超时。", "cancelled" => "操作已取消。",
        "network_failed" => "匿名网络请求失败（可能是代理、TLS、DNS 或离线）。未绕过证书验证。",
        "http_429" => "第三方接口限流，本轮已停止；请稍后重试或手动设置。",
        "invalid_trace" => "目标未提供可用的明确公网 IP（可能返回挑战页或不支持 trace）。",
        "body_too_large" => "响应超过安全大小限制，已拒绝。",
        "geo_ip_mismatch" => "定位服务回显的 IP 与明确查询 IP 不一致。",
        "invalid_geo" or "geo_unavailable" => "IP 定位服务未提供可信结果。",
        "invalid_timezone" => "未获得有效 IANA 时区；不能按国家或固定偏移猜测。",
        _ when code.StartsWith("http_", StringComparison.Ordinal) && int.TryParse(code.AsSpan(5), out _) => "目标返回 HTTP " + code[5..] + "，没有改用通用 IP 网站替代目标证据。",
        _ => "操作未能安全完成（" + (code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && code.Length <= 64 ? code : "safe_error") + "）。"
    };
}
internal static class DiagnosticReport
{
    internal static object Snapshot(DetectionResult? result, DiscoveryResult discovery) => new
    {
        schemaVersion = 1, appVersion = AppVersion.Value, prerelease = true,
        osVersion = Environment.OSVersion.Version.ToString(), architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        systemTimezone = TimeZoneInfo.Local.Id,
        proxyEnvironmentPresent = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" }.Any(n => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))),
        proxyEnvironmentWarning = "Launcher uses read-only system proxy/PAC. Proxy environment and per-process/TUN rules may differ.",
        network = result is null ? null : new { result.Suggested, result.Timezone, result.Code, result.Observations, result.AddressFamily },
        discovery = new { count = discovery.Candidates.Count, code = discovery.Code, storeCandidates = discovery.Candidates.Count(c => c.PackageFamily is not null), desktopCandidates = discovery.Candidates.Count(c => c.PackageFamily is null) },
        realCodexRuntimeVerified = false,
        privacy = "No raw IP, query URL, proxy host/credentials, full client path, response body, auth data or raw exception text."
    };
    internal static async Task<int> DiagnoseAsync(string path)
    {
        // Invocation of --diagnose is explicit consent to the documented fixed anonymous probes.
        DiscoveryResult discovery;
        try { discovery = await Task.Run(CodexDiscovery.Discover); } catch { discovery = new([], "discovery_failed"); }
        var resolver = new WindowsProxyResolver(); ProxyRoute route;
        try { route = await Task.Run(() => resolver.Resolve(EgressDetector.Primary)); } catch { route = new(ProxyKind.Error, null, "proxy_config_unavailable"); }
        DetectionResult result;
        try { result = await new EgressDetector(new SafeHttpTransport(resolver)).DetectAsync(true, default); }
        catch { result = new(false, null, "diagnostic_probe_failed", 0); }
        var runningStates = await Task.Run(() => discovery.Candidates.Select(candidate =>
        {
            try { return new WindowsRunningGuard().Check(candidate).ToString(); } catch { return "Uncertain"; }
        }).ToArray());
        var report = new { success = true, purpose = "Explicit anonymous diagnostic; no Codex launch", proxy = new { kind = route.Kind.ToString(), code = route.Code }, runningStates, evidence = Snapshot(result, discovery) };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
}
